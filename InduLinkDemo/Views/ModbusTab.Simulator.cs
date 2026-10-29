using System;
using System.Globalization;
using InduLink.Abstractions;
using InduLink.Protocols.Modbus;
using InduLinkDemo.Helpers;

namespace InduLinkDemo.Views
{
    public partial class ModbusTab
    {
        // Connect 会嵌套 Reset，计数可覆盖整个异步连接和断开过程。
        private int _connectionOperations;

        internal void ApplySimulatorRequest(SimulatorClientRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Protocol != ProtocolKind.ModbusTcp) throw new ArgumentException("需要 Modbus TCP 虚拟服务参数。", nameof(request));
            // 连接中、断开中或已创建的客户端都必须由用户先关闭，不能悄悄替换现场连接。
            if (_connectionOperations != 0 || _client != null)
                throw new InvalidOperationException("Modbus 客户端正在使用中，请先在客户端页断开连接，再打开虚拟 PLC 参数。");

            ConnectionTypeComboBox.SelectedIndex = 0;
            RefreshProfileOptions();
            SelectModel(ModbusDeviceProfiles.Generic.Key);
            ApplyProfile();
            HostTextBox.Text = request.Host;
            PortTextBox.Text = request.Port.ToString(CultureInfo.InvariantCulture);
            SlaveIdTextBox.Text = request.SlaveId.ToString(CultureInfo.InvariantCulture);
            DeviceIdTextBox.Text = "modbus-simulator";
            AddressTextBox.Text = request.Address;
            RefreshDataTypeState();
            ComboHelper.SelectDataType(DataTypeComboBox, request.DataType);
            WriteValueTextBox.Text = request.ValueText;
            LengthTextBox.Text = request.Length.ToString(CultureInfo.InvariantCulture);
            RefreshInputHints();
            RefreshCapabilityText();
            ResultTextBlock.Text = "已填入虚拟 PLC 参数，请点击连接后读取。";
        }
    }
}
