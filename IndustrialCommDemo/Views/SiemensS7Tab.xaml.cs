using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk.Abstractions;
using CpuType = S7.Net.CpuType;

namespace IndustrialCommDemo.Views
{
    /// <summary>演示 Siemens S7 连接、地址规范化、读写和轮询订阅。</summary>
    public partial class SiemensS7Tab : UserControl
    {
        private DemoAppContext _ctx;
        private ViewModels.SiemensS7ViewModel _vm;
        private Process _snap7ServerProcess;

        public SiemensS7Tab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _vm = new ViewModels.SiemensS7ViewModel(ctx);
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.RecentAddressChanged += OnRecentAddressChanged;

            _vm.RestoreState();

            S7CpuTypeComboBox.ItemsSource = Enum.GetValues(typeof(CpuType));
            S7CpuTypeComboBox.SelectedItem = _vm.SelectedCpuType;

            _vm.RefreshCapabilityText();
            ComboHelper.SelectDataType(S7DataTypeComboBox, _vm.SelectedDataType);

            // Sync initial VM state to UI
            StatusTextBlock.Text = _vm.StatusText;
            StatusTextBlock.Foreground = _vm.StatusBrush;
            ResultTextBlock.Text = _vm.ResultText;
            CapabilityTextBlock.Text = _vm.CapabilityText;
            NativeStringResultTextBlock.Text = _vm.NativeStringResultText;
            UpdateSnap7ServerStatus();

            // Load saved field values into TextBox controls
            if (!string.IsNullOrWhiteSpace(_vm.DeviceId)) S7DeviceIdTextBox.Text = _vm.DeviceId;
            if (!string.IsNullOrWhiteSpace(_vm.Host)) S7HostTextBox.Text = _vm.Host;
            if (!string.IsNullOrWhiteSpace(_vm.PortOrRack)) S7RackTextBox.Text = _vm.PortOrRack;
            if (!string.IsNullOrWhiteSpace(_vm.SlotOrLength)) S7SlotTextBox.Text = _vm.SlotOrLength;
            if (!string.IsNullOrWhiteSpace(_vm.Address)) S7AddressTextBox.Text = _vm.Address;
            if (!string.IsNullOrWhiteSpace(_vm.Length)) S7LengthTextBox.Text = _vm.Length;
            if (!string.IsNullOrWhiteSpace(_vm.WriteValue)) S7WriteValueTextBox.Text = _vm.WriteValue;
            if (!string.IsNullOrWhiteSpace(_vm.NativeStringAddress))
                S7NativeStringAddressTextBox.Text = _vm.NativeStringAddress;
            if (!string.IsNullOrWhiteSpace(_vm.NativeStringLength))
                S7NativeStringLengthTextBox.Text = _vm.NativeStringLength;

            RefreshAddressHistory();
        }

        public async Task ResetClientAsync() => await _vm.ResetClientAsync();

        public async Task ResetSnap7ServerAsync()
        {
            var process = _snap7ServerProcess;
            _snap7ServerProcess = null;
            if (process != null)
            {
                process.Exited -= Snap7ServerProcess_Exited;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await Task.Run(() => process.WaitForExit(2000));
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }
            UpdateSnap7ServerStatus();
        }

        public void SaveState() => _vm.SaveState();

        // ── VM property bridge ──

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!IsLoaded) return;
            switch (e.PropertyName)
            {
                case nameof(_vm.StatusText):
                    StatusTextBlock.Text = _vm.StatusText;
                    break;
                case nameof(_vm.StatusBrush):
                    StatusTextBlock.Foreground = _vm.StatusBrush;
                    break;
                case nameof(_vm.ResultText):
                    ResultTextBlock.Text = _vm.ResultText;
                    break;
                case nameof(_vm.CapabilityText):
                    CapabilityTextBlock.Text = _vm.CapabilityText;
                    break;
                case nameof(_vm.NativeStringResultText):
                    NativeStringResultTextBlock.Text = _vm.NativeStringResultText;
                    break;
                case nameof(_vm.SelectedCpuType):
                    if (!Equals(S7CpuTypeComboBox.SelectedItem, _vm.SelectedCpuType))
                        S7CpuTypeComboBox.SelectedItem = _vm.SelectedCpuType;
                    break;
            }
        }

        private void CpuTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm != null && S7CpuTypeComboBox.SelectedItem is CpuType cpuType)
            {
                _vm.SelectedCpuType = cpuType;
            }
        }

        private void OnRecentAddressChanged()
        {
            RunOnUi(RefreshAddressHistory);
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.BeginInvoke(action);
        }

        private void RefreshAddressHistory()
        {
            ComboHelper.RefreshAddressHistory(S7AddressHistoryComboBox, _vm.RecentAddresses);
        }

        // ── Event handlers (delegate to ViewModel commands) ──

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.DeviceId = S7DeviceIdTextBox.Text;
            _vm.Host = S7HostTextBox.Text;
            _vm.PortOrRack = S7RackTextBox.Text;
            _vm.SlotOrLength = S7SlotTextBox.Text;

            if (S7CpuTypeComboBox.SelectedItem is CpuType cpuType)
                _vm.SelectedCpuType = cpuType;

            await _vm.ConnectAsync();
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await _vm.DisconnectAsync();
        }

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.Address = S7AddressTextBox.Text;
            _vm.SelectedDataType = ComboHelper.GetSelectedDataType(S7DataTypeComboBox);
            _vm.Length = S7LengthTextBox.Text;
            await _vm.ReadAsync();
        }

        private async void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.Address = S7AddressTextBox.Text;
            _vm.SelectedDataType = ComboHelper.GetSelectedDataType(S7DataTypeComboBox);
            _vm.Length = S7LengthTextBox.Text;
            _vm.WriteValue = S7WriteValueTextBox.Text;
            await _vm.WriteAsync();
        }

        private async void ReadNativeStringButton_Click(object sender, RoutedEventArgs e)
        {
            await _vm.ReadNativeStringAsync(
                S7NativeStringAddressTextBox.Text,
                S7NativeStringLengthTextBox.Text);
        }

        private async void StartSnap7ServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetSnap7ServerAsync();

                var executable = FindSnap7ServerExecutable();
                var address = ParseHelper.RequireText(Snap7ServerAddressTextBox.Text, "Snap7Server 监听地址");
                var port = ParseHelper.ParseIntValue(Snap7ServerPortTextBox.Text, "Snap7Server 端口");
                if (port < 1 || port > 65535)
                    throw new ArgumentOutOfRangeException("port", "Snap7Server 端口必须在 1 到 65535 之间。");

                if (!float.TryParse(
                    Snap7ServerFloatTextBox.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var floatValue))
                {
                    throw new FormatException("Snap7Server REAL 值必须是数字。");
                }

                var pointSpecs = ParseSnap7ServerPointSpecs(Snap7ServerPointsTextBox.Text);
                var arguments = string.Format(
                    CultureInfo.InvariantCulture,
                    "--address {0} --port {1} --float {2} --bool {3}",
                    QuoteProcessArgument(address),
                    port,
                    floatValue.ToString("R", CultureInfo.InvariantCulture),
                    Snap7ServerBoolCheckBox.IsChecked == true ? "true" : "false");
                arguments = string.Join(
                    " ",
                    new[] { arguments }.Concat(
                        pointSpecs.Select(point => "--point " + QuoteProcessArgument(point))));
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        WorkingDirectory = Path.GetDirectoryName(executable),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                    },
                    EnableRaisingEvents = true,
                };
                process.Exited += Snap7ServerProcess_Exited;
                if (!process.Start())
                    throw new InvalidOperationException("Snap7Server 进程未能启动。");

                _snap7ServerProcess = process;
                await Task.Delay(300);
                if (process.HasExited)
                {
                    var error = process.StandardError.ReadToEnd();
                    _snap7ServerProcess = null;
                    process.Dispose();
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error) ? "Snap7Server 进程已退出。" : error.Trim());
                }

                UpdateSnap7ServerStatus();
                SetHeaderStatus("Snap7Server 已启动", Brushes.LightGreen);
                LogInfo(string.Format(
                    CultureInfo.InvariantCulture,
                    "Snap7Server 已在 {0}:{1} 启动，初始点位 {2} 个。",
                    address,
                    port,
                    pointSpecs.Length + 2));
            }
            catch (Exception ex)
            {
                UpdateSnap7ServerStatus();
                if (_ctx != null)
                    _ctx.HandleError("Snap7Server 启动失败。", ex, true);
            }
        }

        private async void StopSnap7ServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetSnap7ServerAsync();
                SetHeaderStatus("Snap7Server 已停止", Brushes.Khaki);
                LogInfo("Snap7Server 已停止。");
            }
            catch (Exception ex)
            {
                if (_ctx != null)
                    _ctx.HandleError("Snap7Server 停止失败。", ex, false);
            }
        }

        private void Snap7ServerProcess_Exited(object sender, EventArgs e)
        {
            var process = sender as Process;
            RunOnUi(() =>
            {
                if (ReferenceEquals(_snap7ServerProcess, process))
                {
                    _snap7ServerProcess = null;
                    UpdateSnap7ServerStatus();
                    SetHeaderStatus("Snap7Server 已退出", Brushes.Khaki);
                }
            });
        }

        private void UpdateSnap7ServerStatus()
        {
            var running = _snap7ServerProcess != null;
            try { running = running && !_snap7ServerProcess.HasExited; } catch { running = false; }
            Snap7ServerStatusTextBlock.Text = running ? "运行中" : "未启动";
            Snap7ServerStatusTextBlock.Foreground = running ? Brushes.ForestGreen : Brushes.SlateGray;
        }

        private static string FindSnap7ServerExecutable()
        {
            const string fileName = "IndustrialCommDemo.Snap7Server.exe";
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDirectory, fileName),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "IndustrialCommDemo.Snap7Server", "bin", "Debug", "net472", fileName)),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "IndustrialCommDemo.Snap7Server", "bin", "Release", "net472", fileName)),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "IndustrialCommDemo.Snap7Server", "bin", "x86", "Debug", "net472", fileName)),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "IndustrialCommDemo.Snap7Server", "bin", "x86", "Release", "net472", fileName)),
            };
            var executable = candidates.FirstOrDefault(File.Exists);
            if (executable == null)
            {
                throw new FileNotFoundException(
                    "找不到 IndustrialCommDemo.Snap7Server.exe。请先生成 IndustrialCommDemo.Snap7Server 项目。",
                    fileName);
            }
            return executable;
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string[] ParseSnap7ServerPointSpecs(string text)
        {
            var lines = (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .ToArray();

            foreach (var line in lines)
            {
                if (line.IndexOf('=') <= 0 || line.LastIndexOf('=') == line.Length - 1)
                    throw new FormatException(
                        "额外点位格式错误：每行应为 REAL DB1.DBD12=20.0、INT DB1.DBW2=1500 或 BOOL DB1.DBX0.1=false。错误行：" + line);
            }

            return lines;
        }

        private void SetHeaderStatus(string text, Brush brush)
        {
            if (_ctx != null)
                _ctx.SetHeaderStatus(text, brush);
        }

        private void LogInfo(string text)
        {
            if (_ctx != null)
                _ctx.DemoLogger.Info(text);
        }

        // ── Address input parsing ──

        private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _vm == null) return;
            try
            {
                var analysis = _vm.AnalyzeAddress(S7AddressTextBox.Text);
                if (analysis.InferredDataType.HasValue)
                    SelectDataType(analysis.InferredDataType.Value);
                if (analysis.InferredLength.HasValue)
                    S7LengthTextBox.Text = analysis.InferredLength.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else
                    S7LengthTextBox.Text = "1";
                _vm.Length = S7LengthTextBox.Text;
            }
            catch { }
        }

        private void DataTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _vm == null) return;
            _vm.SelectedDataType = ComboHelper.GetSelectedDataType(S7DataTypeComboBox);
            S7LengthTextBox.Text = "1";
        }

        private void AddressHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboHelper.ApplyHistorySelection(S7AddressHistoryComboBox, S7AddressTextBox);
        }

        private void SelectDataType(IndustrialCommSdk.Abstractions.DataType dataType)
        {
            foreach (var item in S7DataTypeComboBox.Items.OfType<ComboBoxItem>())
            {
                IndustrialCommSdk.Abstractions.DataType parsed;
                if (Enum.TryParse(item.Content.ToString(), out parsed) && parsed == dataType)
                {
                    S7DataTypeComboBox.SelectedItem = item;
                    return;
                }
            }
        }
    }
}
