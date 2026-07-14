using System;
using System.Windows.Controls;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk.Protocols.Modbus;

namespace IndustrialCommDemo.Views
{
    public partial class ModbusTab
    {
        public void SaveState()
        {
            _ctx.UiState.Modbus.DeviceId = DeviceIdTextBox.Text;
            _ctx.UiState.Modbus.Host = HostTextBox.Text;
            _ctx.UiState.Modbus.Port = PortTextBox.Text;
            _ctx.UiState.Modbus.SlaveId = SlaveIdTextBox.Text;
            _ctx.UiState.Modbus.ModelKey = _profile?.Key ?? ModbusDeviceProfiles.InovanceEasyPlc.Key;
            _ctx.UiState.Modbus.Address = AddressTextBox.Text;
            _ctx.UiState.Modbus.Length = LengthTextBox.Text;
            _ctx.UiState.Modbus.WriteValue = WriteValueTextBox.Text;
            _ctx.UiState.Modbus.PollInterval = PollIntervalTextBox.Text;
            _ctx.UiState.Modbus.ConnectionType = ConnectionTypeComboBox.SelectedIndex == 1 ? "Rtu" : "Tcp";
            _ctx.UiState.Modbus.PortName = PortNameComboBox.Text;
            _ctx.UiState.Modbus.BaudRate = BaudRateComboBox.Text;
            _ctx.UiState.Modbus.DataBits = DataBitsComboBox.Text;
            _ctx.UiState.Modbus.Parity = (ParityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _ctx.UiState.Modbus.StopBits = (StopBitsComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Modbus ?? new Services.ModbusUiState();
            ComboHelper.SetIfNotEmpty(DeviceIdTextBox, state.DeviceId);
            ComboHelper.SetIfNotEmpty(HostTextBox, state.Host);
            ComboHelper.SetIfNotEmpty(PortTextBox, state.Port);
            ComboHelper.SetIfNotEmpty(SlaveIdTextBox, state.SlaveId);
            if (string.Equals(state.ConnectionType, "Rtu", StringComparison.OrdinalIgnoreCase))
                ConnectionTypeComboBox.SelectedIndex = 1;
            RefreshProfileOptions();
            SelectModel(state.ModelKey);
            ApplyProfile();
            ComboHelper.SetIfNotEmpty(AddressTextBox, state.Address);
            ComboHelper.SetIfNotEmpty(LengthTextBox, state.Length);
            ComboHelper.SetIfNotEmpty(WriteValueTextBox, state.WriteValue);
            ComboHelper.SetIfNotEmpty(PollIntervalTextBox, state.PollInterval);
            if (!string.IsNullOrEmpty(state.PortName)) PortNameComboBox.Text = state.PortName;
            if (!string.IsNullOrEmpty(state.BaudRate)) BaudRateComboBox.Text = state.BaudRate;
            ComboHelper.SelectComboBoxByContent(DataBitsComboBox, state.DataBits);
            ComboHelper.SelectComboBoxByTag(ParityComboBox, state.Parity);
            ComboHelper.SelectComboBoxByTag(StopBitsComboBox, state.StopBits);
        }
    }
}
