using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using InduLink.Abstractions;
using InduLink.Simulation;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace InduLinkDemo.Views
{
    public partial class ProtocolSimulatorTab : UserControl
    {
        private readonly ObservableCollection<SimulatorPointRow> _rows = new ObservableCollection<SimulatorPointRow>();
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly DispatcherTimer _refreshTimer;
        private DemoAppContext _context;
        private ProtocolSimulator _simulator;
        private bool _opcUa;
        private bool _busy;
        private bool _closing;
        private string _lastUiError;

        public ProtocolSimulatorTab()
        {
            InitializeComponent();
            PointsGrid.ItemsSource = _rows;
            BehaviorColumn.ItemsSource = new[]
            {
                new { Value = SimulationBehavior.Fixed, Label = "固定值" },
                new { Value = SimulationBehavior.Increment, Label = "递增" },
                new { Value = SimulationBehavior.Toggle, Label = "周期切换" },
            };
            _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => RefreshValues(), Dispatcher);
            _refreshTimer.Stop();
            Loaded += (_, _) => { if (_simulator?.IsRunning == true) _refreshTimer.Start(); };
            Unloaded += (_, _) => _refreshTimer.Stop();
            ConfigureProtocol(false);
        }

        public void Initialize(DemoAppContext context, bool opcUa)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            ConfigureProtocol(opcUa);
        }

        internal void ConfigureProtocol(bool opcUa)
        {
            _opcUa = opcUa;
            TitleText.Text = opcUa ? "OPC UA 虚拟服务" : "Modbus TCP 虚拟服务";
            ConnectionHintText.Text = opcUa
                ? "客户端使用下方 Endpoint 和 ns=2;s=<名称>。默认无加密连接；勾选加密后需双向信任应用证书。节点可浏览、读写和订阅。"
                : "客户端设备类型选择 Generic Modbus，站号与下方一致。地址 C/DI 为 Bool，HR/IR 为数值；多寄存器使用高字在前。";
            ModbusSettingsPanel.Visibility = opcUa ? Visibility.Collapsed : Visibility.Visible;
            OpcUaSettingsPanel.Visibility = opcUa ? Visibility.Visible : Visibility.Collapsed;
            CertificatePanel.Visibility = opcUa ? Visibility.Visible : Visibility.Collapsed;
            CertificateDirectoryTextBox.Text = OpcUaSimulator.DefaultCertificateStoreDirectory;
            TypeColumn.ItemsSource = new[] { DataType.Bool, DataType.Int16, DataType.UInt16, DataType.Int32, DataType.UInt32, DataType.Float, DataType.Double }
                .Concat(opcUa ? new[] { DataType.String } : Array.Empty<DataType>()).ToArray();
            LoadSamples();
        }

        private void LoadSamples()
        {
            _rows.Clear();
            if (_opcUa)
            {
                _rows.Add(new SimulatorPointRow { Address = "ns=2;s=Demo/Running", DataType = DataType.Bool, InitialValue = "true" });
                _rows.Add(new SimulatorPointRow { Address = "ns=2;s=Demo/Counter", DataType = DataType.Int32, InitialValue = "0", Behavior = SimulationBehavior.Increment });
                _rows.Add(new SimulatorPointRow { Address = "ns=2;s=Demo/Temperature", DataType = DataType.Float, InitialValue = "23.5" });
                _rows.Add(new SimulatorPointRow { Address = "ns=2;s=Demo/Status", DataType = DataType.String, InitialValue = "Ready" });
            }
            else
            {
                _rows.Add(new SimulatorPointRow { Address = "C0", DataType = DataType.Bool, InitialValue = "true" });
                _rows.Add(new SimulatorPointRow { Address = "DI0", DataType = DataType.Bool, InitialValue = "false", Behavior = SimulationBehavior.Toggle });
                _rows.Add(new SimulatorPointRow { Address = "HR0", DataType = DataType.Int16, InitialValue = "100", Behavior = SimulationBehavior.Increment });
                _rows.Add(new SimulatorPointRow { Address = "HR2", DataType = DataType.Float, InitialValue = "12.5" });
                _rows.Add(new SimulatorPointRow { Address = "IR0", DataType = DataType.UInt16, InitialValue = "42" });
            }
            PointsGrid.SelectedIndex = 0;
        }

        internal async Task StartAsync()
        {
            await _operationGate.WaitAsync();
            try
            {
                if (_closing) return;
                if (_simulator?.IsRunning == true) return;
                SetBusy(true);
                CommitPointEdits();
                var points = _rows.Select(row => row.ToPoint()).ToArray();
                var interval = TimeSpan.FromMilliseconds(int.Parse(IntervalTextBox.Text, CultureInfo.InvariantCulture));
                var service = _opcUa
                    ? (ProtocolSimulator)new OpcUaSimulator(points, EndpointTextBox.Text.Trim(), SecurityCheckBox.IsChecked == true,
                        string.IsNullOrWhiteSpace(CertificateDirectoryTextBox.Text) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(CertificateDirectoryTextBox.Text.Trim())), interval)
                    : new ModbusTcpSimulator(points, AddressTextBox.Text.Trim(), int.Parse(PortTextBox.Text, CultureInfo.InvariantCulture),
                        byte.Parse(SlaveIdTextBox.Text, CultureInfo.InvariantCulture), interval);
                try
                {
                    using var startup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    startup.CancelAfter(TimeSpan.FromSeconds(15));
                    await service.StartAsync(startup.Token);
                    _simulator = service;
                }
                catch { await service.DisposeAsync(); throw; }
                StatusText.Text = "运行中 · " + service.Endpoint;
                _lastUiError = null;
                StatusText.Foreground = Brushes.ForestGreen;
                _context?.DemoLogger.Info(TitleText.Text + " 已启动：" + service.Endpoint);
                _refreshTimer.Start();
                RefreshValues();
            }
            finally { SetBusy(false); _operationGate.Release(); }
        }

        internal async Task StopAsync()
        {
            await _operationGate.WaitAsync();
            try
            {
                SetBusy(true);
                _refreshTimer.Stop();
                if (_simulator != null)
                {
                    await _simulator.DisposeAsync();
                    _simulator = null;
                }
                StatusText.Text = "已停止";
                _lastUiError = null;
                StatusText.Foreground = Brushes.SlateGray;
            }
            finally { SetBusy(false); _operationGate.Release(); }
        }

        public async Task ResetAsync()
        {
            _closing = true;
            _lifetime.Cancel();
            await StopAsync();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            var running = _simulator?.IsRunning == true;
            StartButton.IsEnabled = !busy && !running && !_closing;
            StopButton.IsEnabled = !busy && running;
            SettingsPanel.IsEnabled = !busy && !running;
            CertificatePanel.IsEnabled = !busy && !running;
            PointsGrid.IsReadOnly = busy || running;
            AddPointButton.IsEnabled = RemovePointButton.IsEnabled = SamplesButton.IsEnabled = ImportButton.IsEnabled = !busy && !running;
            ExportButton.IsEnabled = !busy;
            ApplyValueButton.IsEnabled = WriteValueTextBox.IsEnabled = !busy && running && PointsGrid.SelectedItem != null;
        }

        private void RefreshValues()
        {
            if (_busy || _simulator?.IsRunning != true) return;
            try
            {
                var values = _simulator.ReadValues().ToDictionary(value => value.Address, StringComparer.Ordinal);
                foreach (var row in _rows)
                    if (values.TryGetValue(row.Address.Trim(), out var value)) row.CurrentValue = value.Text;
                var error = _lastUiError ?? _simulator.LastError;
                StatusText.Text = "运行中 · " + _simulator.Endpoint + (string.IsNullOrEmpty(error) ? "" : " · " + error);
                StatusText.Foreground = string.IsNullOrEmpty(error) ? Brushes.ForestGreen : Brushes.IndianRed;
            }
            catch (Exception ex) { ReportError(ex); _refreshTimer.Stop(); }
        }

        private void CommitPointEdits()
        {
            if (!PointsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !PointsGrid.CommitEdit(DataGridEditingUnit.Row, true))
                throw new FormatException("请修正点位表中的无效输入。数值使用小数点，例如 12.5。");
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try { await StartAsync(); }
            catch (Exception ex) { if (!_closing) ReportError(ex); }
        }
        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try { await StopAsync(); }
            catch (Exception ex) { ReportError(ex); }
        }
        private void ApplyValueButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_simulator == null || !(PointsGrid.SelectedItem is SimulatorPointRow row)) return;
                _simulator.SetValue(row.Address.Trim(), WriteValueTextBox.Text);
                _lastUiError = null;
                RefreshValues();
            }
            catch (Exception ex) { ReportError(ex); }
        }
        private void PointsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PointsGrid.SelectedItem is SimulatorPointRow row) WriteValueTextBox.Text = row.CurrentValue ?? row.InitialValue;
            SetBusy(_busy);
        }
        private void AddPointButton_Click(object sender, RoutedEventArgs e)
        {
            _rows.Add(new SimulatorPointRow { Address = _opcUa ? "ns=2;s=NewPoint" : "HR10", InitialValue = "0", DataType = DataType.Int16 });
            PointsGrid.SelectedIndex = _rows.Count - 1;
        }
        private void RemovePointButton_Click(object sender, RoutedEventArgs e)
        {
            if (PointsGrid.SelectedItem is SimulatorPointRow row) _rows.Remove(row);
        }
        private void SamplesButton_Click(object sender, RoutedEventArgs e) { LoadSamples(); }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "模拟服务配置 (*.json)|*.json" };
            if (dialog.ShowDialog() != true) return;
            try { ImportConfiguration(File.ReadAllText(dialog.FileName)); }
            catch (Exception ex) { ReportError(ex); }
        }

        internal void ImportConfiguration(string json)
        {
            if (_simulator?.IsRunning == true) throw new InvalidOperationException("请先停止服务再导入配置。");
            var configuration = JsonConvert.DeserializeObject<SimulatorConfiguration>(json);
            if (configuration == null || configuration.Points == null || configuration.Points.Length == 0)
                throw new FormatException("配置需要至少一个点位。");
            if (configuration.Protocol != (_opcUa ? "opcua" : "modbus-tcp")) throw new FormatException("配置的协议与当前页面不一致。");
            foreach (var row in configuration.Points)
            {
                if (row == null) throw new FormatException("配置不能包含空点位。");
                row.ToPoint();
            }
            AddressTextBox.Text = configuration.Address;
            PortTextBox.Text = configuration.Port.ToString(CultureInfo.InvariantCulture);
            SlaveIdTextBox.Text = configuration.SlaveId.ToString(CultureInfo.InvariantCulture);
            EndpointTextBox.Text = configuration.Endpoint;
            SecurityCheckBox.IsChecked = configuration.UseSecurity;
            CertificateDirectoryTextBox.Text = configuration.CertificateStoreDirectory ?? OpcUaSimulator.DefaultCertificateStoreDirectory;
            IntervalTextBox.Text = configuration.IntervalMilliseconds.ToString(CultureInfo.InvariantCulture);
            _rows.Clear();
            foreach (var row in configuration.Points) _rows.Add(row);
            PointsGrid.SelectedIndex = 0;
        }
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "模拟服务配置 (*.json)|*.json", FileName = _opcUa ? "opcua-simulator.json" : "modbus-simulator.json" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, ExportConfiguration());
                _context?.DemoLogger.Info("模拟服务配置已导出：" + dialog.FileName);
            }
            catch (Exception ex) { ReportError(ex); }
        }

        internal string ExportConfiguration()
        {
            CommitPointEdits();
            if (_rows.Count == 0) throw new FormatException("配置需要至少一个点位。");
            foreach (var row in _rows) row.ToPoint();
            var configuration = new SimulatorConfiguration
            {
                Protocol = _opcUa ? "opcua" : "modbus-tcp", Address = AddressTextBox.Text.Trim(), Port = int.Parse(PortTextBox.Text, CultureInfo.InvariantCulture),
                SlaveId = byte.Parse(SlaveIdTextBox.Text, CultureInfo.InvariantCulture), Endpoint = EndpointTextBox.Text.Trim(), UseSecurity = SecurityCheckBox.IsChecked == true,
                CertificateStoreDirectory = _opcUa ? CertificateDirectoryTextBox.Text.Trim() : null,
                IntervalMilliseconds = int.Parse(IntervalTextBox.Text, CultureInfo.InvariantCulture), Points = _rows.ToArray(),
            };
            return JsonConvert.SerializeObject(configuration, Formatting.Indented, new StringEnumConverter());
        }
        private void ReportError(Exception exception)
        {
            StatusText.Text = exception.Message;
            _lastUiError = exception.Message;
            StatusText.Foreground = Brushes.IndianRed;
            _context?.HandleError(TitleText.Text + "操作失败。", exception, false);
        }

        private sealed class SimulatorConfiguration
        {
            public string Protocol { get; set; }
            public string Address { get; set; } = "127.0.0.1";
            public int Port { get; set; } = 1502;
            public byte SlaveId { get; set; } = 1;
            public string Endpoint { get; set; } = "opc.tcp://localhost:4840/InduLinkSimulator";
            public bool UseSecurity { get; set; }
            public string CertificateStoreDirectory { get; set; }
            public int IntervalMilliseconds { get; set; } = 1000;
            public SimulatorPointRow[] Points { get; set; }
        }
    }

    internal sealed class SimulatorPointRow : INotifyPropertyChanged
    {
        private string _currentValue;
        public string Address { get; set; }
        public DataType DataType { get; set; }
        public string InitialValue { get; set; }
        public SimulationBehavior Behavior { get; set; }
        public double Step { get; set; } = 1;
        [JsonIgnore]
        public string CurrentValue
        {
            get => _currentValue;
            set { if (_currentValue == value) return; _currentValue = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentValue))); }
        }
        public SimulationPoint ToPoint() => new SimulationPoint(Address, DataType, InitialValue, Behavior, Step);
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
