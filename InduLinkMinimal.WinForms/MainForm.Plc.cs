using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using InduLink;
using InduLink.Abstractions;
using InduLink.Diagnostics;
using InduLink.Protocols.Mc;
using InduLink.Protocols.Modbus;
using InduLink.Protocols.S7;

namespace InduLinkMinimal.WinForms
{
    public partial class MainForm
    {
        private async void ModbusTcpConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectInduLinkAsync(
                ModbusTcpOutputTextBox,
                () => new ModbusTcpClient(new ModbusTcpClientOptions
                {
                    DeviceId = "winforms-modbus-tcp",
                    Host = ModbusTcpHostTextBox.Text.Trim(),
                    Port = ParsePort(ModbusTcpPortTextBox.Text),
                    SlaveId = ParseSlaveId(ModbusTcpSlaveTextBox.Text),
                    DeviceProfile = GetSelectedModbusProfile(),
                }),
                client => _modbusTcpClient = client,
                _modbusTcpClient);
        }

        private async void ModbusRtuConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectInduLinkAsync(
                ModbusRtuOutputTextBox,
                () => new ModbusRtuClient(new ModbusRtuClientOptions
                {
                    DeviceId = "winforms-modbus-rtu",
                    PortName = ModbusRtuPortTextBox.Text.Trim(),
                    BaudRate = ParsePositive(ModbusRtuBaudTextBox.Text, "波特率"),
                    SlaveId = ParseSlaveId(ModbusRtuSlaveTextBox.Text),
                }),
                client => _modbusRtuClient = client,
                _modbusRtuClient);
        }

        private async void S7ConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectInduLinkAsync(
                S7OutputTextBox,
                () => new SiemensS7Client(new SiemensS7ClientOptions
                {
                    DeviceId = "winforms-s7",
                    Host = S7HostTextBox.Text.Trim(),
                    Rack = (short)ParseNonNegative(S7RackTextBox.Text, "机架"),
                    Slot = (short)ParseNonNegative(S7SlotTextBox.Text, "插槽"),
                }),
                client => _s7Client = client,
                _s7Client);
        }

        private async void McConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectInduLinkAsync(
                McOutputTextBox,
                () => new MitsubishiMcClient(new MitsubishiMcClientOptions
                {
                    DeviceId = "winforms-mc",
                    Host = McHostTextBox.Text.Trim(),
                    Port = ParsePort(McPortTextBox.Text),
                    ReceiveTimeoutMilliseconds = ParsePositive(McTimeoutTextBox.Text, "接收超时"),
                }),
                client => _mcClient = client,
                _mcClient);
        }

        private async Task ConnectInduLinkAsync(
            TextBox output,
            Func<IInduLinkClient> createClient,
            Action<IInduLinkClient> setClient,
            IInduLinkClient currentClient)
        {
            await RunAsync(output, async () =>
            {
                currentClient?.Dispose();
                setClient(null);

                var client = createClient();
                try
                {
                    await client.ConnectAsync(CancellationToken.None);
                    setClient(client);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }

                Append(output, $"已连接 {client.DeviceId}");
                AppendDiagnostics(output, client);
            });
        }

        private void LoadModbusProfiles()
        {
            ModbusTcpProfileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            ModbusTcpProfileComboBox.DisplayMember = "DisplayName";
            ModbusTcpProfileComboBox.Items.AddRange(ModbusDeviceProfiles.All.Cast<object>().ToArray());

            ModbusTcpProfileComboBox.SelectedItem = ModbusTcpProfileComboBox.Items
                .Cast<IModbusDeviceProfile>()
                .First(profile => string.Equals(
                    profile.Key,
                    ModbusDeviceProfiles.Generic.Key,
                    StringComparison.OrdinalIgnoreCase));

            ModbusTcpProfileComboBox.SelectedIndexChanged += ModbusTcpProfileComboBox_SelectedIndexChanged;
        }

        private IModbusDeviceProfile GetSelectedModbusProfile()
        {
            return ModbusTcpProfileComboBox.SelectedItem as IModbusDeviceProfile
                ?? ModbusDeviceProfiles.Generic;
        }

        private void ModbusTcpProfileComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var profile = GetSelectedModbusProfile();
            if (!string.IsNullOrWhiteSpace(profile.DefaultAddress))
            {
                ModbusTcpAddressTextBox.Text = profile.DefaultAddress;
            }

            Append(
                ModbusTcpOutputTextBox,
                $"设备配置={profile.DisplayName}，地址示例={profile.ExampleAddresses}");
        }

        private async void ModbusTcpReadButton_Click(object sender, EventArgs e) =>
            await ReadAsync(_modbusTcpClient, ModbusTcpAddressTextBox, ModbusTcpTypeComboBox, ModbusTcpOutputTextBox);

        private async void ModbusRtuReadButton_Click(object sender, EventArgs e) =>
            await ReadAsync(_modbusRtuClient, ModbusRtuAddressTextBox, ModbusRtuTypeComboBox, ModbusRtuOutputTextBox);

        private async void S7ReadButton_Click(object sender, EventArgs e) =>
            await ReadAsync(_s7Client, S7AddressTextBox, S7TypeComboBox, S7OutputTextBox);

        private async void McReadButton_Click(object sender, EventArgs e) =>
            await ReadAsync(_mcClient, McAddressTextBox, McTypeComboBox, McOutputTextBox);

        private async Task ReadAsync(IInduLinkClient client, TextBox address, ComboBox type, TextBox output)
        {
            await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = ParseDataType(type.Text);
                var result = await client.ReadAsync(
                    new ReadRequest(client.DeviceId, address.Text.Trim(), dataType),
                    CancellationToken.None);

                Append(
                    output,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "读取 {0}: {1} [{2}] {3}",
                        result.Address,
                        result.Value ?? "(null)",
                        result.Quality,
                        result.ErrorMessage));
                AppendDiagnostics(output, client);
            });
        }

        private async void ModbusTcpWriteButton_Click(object sender, EventArgs e) =>
            await WriteAsync(_modbusTcpClient, ModbusTcpAddressTextBox, ModbusTcpTypeComboBox, ModbusTcpValueTextBox, ModbusTcpOutputTextBox);

        private async void ModbusRtuWriteButton_Click(object sender, EventArgs e) =>
            await WriteAsync(_modbusRtuClient, ModbusRtuAddressTextBox, ModbusRtuTypeComboBox, ModbusRtuValueTextBox, ModbusRtuOutputTextBox);

        private async void S7WriteButton_Click(object sender, EventArgs e) =>
            await WriteAsync(_s7Client, S7AddressTextBox, S7TypeComboBox, S7ValueTextBox, S7OutputTextBox);

        private async void McWriteButton_Click(object sender, EventArgs e) =>
            await WriteAsync(_mcClient, McAddressTextBox, McTypeComboBox, McValueTextBox, McOutputTextBox);

        private async Task WriteAsync(
            IInduLinkClient client,
            TextBox address,
            ComboBox type,
            TextBox value,
            TextBox output)
        {
            await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = ParseDataType(type.Text);
                var request = new WriteRequest(
                    client.DeviceId,
                    address.Text.Trim(),
                    dataType,
                    ConvertValue(value.Text, dataType));

                await client.WriteAsync(request, CancellationToken.None);
                Append(output, "写入完成");
                AppendDiagnostics(output, client);
            });
        }

        private async void ModbusTcpDisconnectButton_Click(object sender, EventArgs e) =>
            await DisconnectAsync(_modbusTcpClient, client => _modbusTcpClient = client, ModbusTcpOutputTextBox);

        private async void ModbusRtuDisconnectButton_Click(object sender, EventArgs e) =>
            await DisconnectAsync(_modbusRtuClient, client => _modbusRtuClient = client, ModbusRtuOutputTextBox);

        private async void S7DisconnectButton_Click(object sender, EventArgs e) =>
            await DisconnectAsync(_s7Client, client => _s7Client = client, S7OutputTextBox);

        private async void McDisconnectButton_Click(object sender, EventArgs e) =>
            await DisconnectAsync(_mcClient, client => _mcClient = client, McOutputTextBox);

        private async Task DisconnectAsync(
            IInduLinkClient client,
            Action<IInduLinkClient> setClient,
            TextBox output)
        {
            await RunAsync(output, async () =>
            {
                if (client == null)
                {
                    return;
                }

                await client.DisconnectAsync(CancellationToken.None);
                client.Dispose();
                setClient(null);
                Append(output, "已断开");
            });
        }

        private static void AppendDiagnostics(TextBox output, IInduLinkClient client)
        {
            var snapshot = client.GetDiagnosticSnapshot();
            Append(
                output,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "诊断: 耗时={0}ms, 总计={1}, 失败={2}, 超时={3}, 最近分类={4}",
                    snapshot.LastOperationElapsedMilliseconds,
                    snapshot.TotalOperations,
                    snapshot.FailedOperations,
                    snapshot.TimeoutCount,
                    snapshot.LastFailureCategory));
        }
    }
}
