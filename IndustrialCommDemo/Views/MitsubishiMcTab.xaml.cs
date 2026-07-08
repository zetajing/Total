using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IndustrialCommDemo.Helpers;

namespace IndustrialCommDemo.Views
{
    public partial class MitsubishiMcTab : UserControl
    {
        private ViewModels.MitsubishiMcViewModel _vm;

        public MitsubishiMcTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _vm = new ViewModels.MitsubishiMcViewModel(ctx);
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.RecentAddressChanged += OnRecentAddressChanged;
            _vm.RestoreState();
            ComboHelper.SelectDataType(McDataTypeComboBox, _vm.SelectedDataType);

            StatusTextBlock.Text = _vm.StatusText;
            StatusTextBlock.Foreground = _vm.StatusBrush;
            ResultTextBlock.Text = _vm.ResultText;

            if (!string.IsNullOrWhiteSpace(_vm.DeviceId)) McDeviceIdTextBox.Text = _vm.DeviceId;
            if (!string.IsNullOrWhiteSpace(_vm.Host)) McHostTextBox.Text = _vm.Host;
            if (!string.IsNullOrWhiteSpace(_vm.PortOrRack)) McPortTextBox.Text = _vm.PortOrRack;
            if (!string.IsNullOrWhiteSpace(_vm.Address)) McAddressTextBox.Text = _vm.Address;
            if (!string.IsNullOrWhiteSpace(_vm.Length)) McLengthTextBox.Text = _vm.Length;
            if (!string.IsNullOrWhiteSpace(_vm.WriteValue)) McWriteValueTextBox.Text = _vm.WriteValue;

            RefreshAddressHistory();
        }

        public async Task ResetClientAsync() => await _vm.ResetClientAsync();

        public void SaveState() => _vm.SaveState();

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
            }
        }

        private void OnRecentAddressChanged()
        {
            RunOnUi(RefreshAddressHistory);
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.BeginInvoke(action);
        }

        private void RefreshAddressHistory()
        {
            ComboHelper.RefreshAddressHistory(McAddressHistoryComboBox, _vm.RecentAddresses);
        }

        // ── Event handlers ──

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.DeviceId = McDeviceIdTextBox.Text;
            _vm.Host = McHostTextBox.Text;
            _vm.PortOrRack = McPortTextBox.Text;
            await _vm.ConnectAsync();
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await _vm.DisconnectAsync();
        }

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.Address = McAddressTextBox.Text;
            _vm.SelectedDataType = ComboHelper.GetSelectedDataType(McDataTypeComboBox);
            _vm.Length = McLengthTextBox.Text;
            await _vm.ReadAsync();
        }

        private async void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.Address = McAddressTextBox.Text;
            _vm.SelectedDataType = ComboHelper.GetSelectedDataType(McDataTypeComboBox);
            _vm.Length = McLengthTextBox.Text;
            _vm.WriteValue = McWriteValueTextBox.Text;
            await _vm.WriteAsync();
        }

        private void AddressHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboHelper.ApplyHistorySelection(McAddressHistoryComboBox, McAddressTextBox);
        }

        private void DataTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _vm == null) return;
            _vm.SelectedDataType = ComboHelper.GetSelectedDataType(McDataTypeComboBox);
            McLengthTextBox.Text = "1";
        }
    }
}
