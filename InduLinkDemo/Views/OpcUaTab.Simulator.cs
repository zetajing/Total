using System;
using System.Globalization;
using InduLink.Abstractions;
using InduLinkDemo.Helpers;

namespace InduLinkDemo.Views
{
    public partial class OpcUaTab
    {
        // Connect 会嵌套 Reset，计数可覆盖整个异步连接和断开过程。
        private int _connectionOperations;

        internal void ApplySimulatorRequest(SimulatorClientRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Protocol != ProtocolKind.OpcUa) throw new ArgumentException("需要 OPC UA 虚拟服务参数。", nameof(request));
            if (_connectionOperations != 0 || _client != null)
                throw new InvalidOperationException("OPC UA 客户端正在使用中，请先在客户端页断开连接，再打开虚拟 PLC 参数。");

            EndpointUrlTextBox.Text = request.Endpoint;
            DeviceIdTextBox.Text = "opcua-simulator";
            // 模拟服务使用匿名身份；不将原设备的账号密码带到新端点。
            UsernameTextBox.Clear();
            PasswordBox.Clear();
            UseSecurityCheckBox.IsChecked = request.UseSecurity;
            AutoAcceptCertificatesCheckBox.IsChecked = false;
            NodeIdTextBox.Text = request.Address;
            ComboHelper.SelectDataType(DataTypeComboBox, request.DataType);
            WriteValueTextBox.Text = request.ValueText;
            LengthTextBox.Text = request.Length.ToString(CultureInfo.InvariantCulture);
            ResultTextBlock.Text = request.UseSecurity
                ? "已填入虚拟 PLC 参数；请先完成双方应用证书信任，再点击连接后读取。"
                : "已填入虚拟 PLC 参数，请点击连接后读取。";
        }
    }
}
