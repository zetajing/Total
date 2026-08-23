using System;
using System.ComponentModel;
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
