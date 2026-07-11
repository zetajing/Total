using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Mes;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Transport;

namespace IndustrialCommMinimal.WinForms
{
    public partial class MainForm : Form
    {
        private IIndustrialClient _modbusTcpClient;
        private IIndustrialClient _modbusRtuClient;
        private IIndustrialClient _s7Client;
        private IIndustrialClient _mcClient;
        private TcpTransportClient _rawTcpClient;
        private MesTcpClient _mesTcpClient;

        public MainForm()
        {
            InitializeComponent();
        }

        private async void ModbusTcpConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(ModbusTcpOutputTextBox, () => IndustrialClientFactory.ModbusTcp(
                ModbusTcpHostTextBox.Text.Trim(), ParsePort(ModbusTcpPortTextBox.Text), ParseSlaveId(ModbusTcpSlaveTextBox.Text),
                deviceProfile: ModbusDeviceProfiles.Generic), client => _modbusTcpClient = client, _modbusTcpClient);
        }

        private async void ModbusRtuConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(ModbusRtuOutputTextBox, () => IndustrialClientFactory.ModbusRtu(
                ModbusRtuPortTextBox.Text.Trim(), ParsePositive(ModbusRtuBaudTextBox.Text, "波特率"), ParseSlaveId(ModbusRtuSlaveTextBox.Text), parity: Parity.Even),
                client => _modbusRtuClient = client, _modbusRtuClient);
        }

        private async void S7ConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(S7OutputTextBox, () => IndustrialClientFactory.SiemensS7(
                S7HostTextBox.Text.Trim(), rack: (short)ParseNonNegative(S7RackTextBox.Text, "机架"), slot: (short)ParseNonNegative(S7SlotTextBox.Text, "插槽")),
                client => _s7Client = client, _s7Client);
        }

        private async void McConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(McOutputTextBox, () => IndustrialClientFactory.MitsubishiMc(
                McHostTextBox.Text.Trim(), ParsePort(McPortTextBox.Text), receiveTimeoutMilliseconds: ParsePositive(McTimeoutTextBox.Text, "接收超时")),
                client => _mcClient = client, _mcClient);
        }

        private async Task ConnectIndustrialAsync(TextBox output, Func<IIndustrialClient> factory, Action<IIndustrialClient> assign, IIndustrialClient oldClient)
        {
            await RunAsync(output, async () =>
            {
                if (oldClient != null) oldClient.Dispose();
                var client = factory();
                assign(client);
                await client.ConnectAsync(CancellationToken.None);
                Append(output, "已连接 " + client.DeviceId);
            });
        }

        private async void IndustrialReadButton_Click(object sender, EventArgs e)
        {
            if (sender == ModbusTcpReadButton) await ReadAsync(_modbusTcpClient, ModbusTcpAddressTextBox, ModbusTcpTypeComboBox, ModbusTcpOutputTextBox);
            else if (sender == ModbusRtuReadButton) await ReadAsync(_modbusRtuClient, ModbusRtuAddressTextBox, ModbusRtuTypeComboBox, ModbusRtuOutputTextBox);
            else if (sender == S7ReadButton) await ReadAsync(_s7Client, S7AddressTextBox, S7TypeComboBox, S7OutputTextBox);
            else await ReadAsync(_mcClient, McAddressTextBox, McTypeComboBox, McOutputTextBox);
        }

        private async Task ReadAsync(IIndustrialClient client, TextBox address, ComboBox type, TextBox output)
        {
            await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = ParseDataType(type.Text);
                var result = await client.ReadAsync(new ReadRequest(client.DeviceId, address.Text.Trim(), dataType), CancellationToken.None);
                Append(output, string.Format(CultureInfo.InvariantCulture, "读取 {0}: {1} [{2}] {3}", result.Address, result.Value ?? "(null)", result.Quality, result.ErrorMessage));
            });
        }

        private async void IndustrialWriteButton_Click(object sender, EventArgs e)
        {
            if (sender == ModbusTcpWriteButton) await WriteAsync(_modbusTcpClient, ModbusTcpAddressTextBox, ModbusTcpTypeComboBox, ModbusTcpValueTextBox, ModbusTcpOutputTextBox);
            else if (sender == ModbusRtuWriteButton) await WriteAsync(_modbusRtuClient, ModbusRtuAddressTextBox, ModbusRtuTypeComboBox, ModbusRtuValueTextBox, ModbusRtuOutputTextBox);
            else if (sender == S7WriteButton) await WriteAsync(_s7Client, S7AddressTextBox, S7TypeComboBox, S7ValueTextBox, S7OutputTextBox);
            else await WriteAsync(_mcClient, McAddressTextBox, McTypeComboBox, McValueTextBox, McOutputTextBox);
        }

        private async Task WriteAsync(IIndustrialClient client, TextBox address, ComboBox type, TextBox value, TextBox output)
        {
            await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = ParseDataType(type.Text);
                await client.WriteAsync(new WriteRequest(client.DeviceId, address.Text.Trim(), dataType, ConvertValue(value.Text, dataType)), CancellationToken.None);
                Append(output, "写入完成");
            });
        }

        private async void IndustrialDisconnectButton_Click(object sender, EventArgs e)
        {
            if (sender == ModbusTcpDisconnectButton) await DisconnectAsync(_modbusTcpClient, c => _modbusTcpClient = c, ModbusTcpOutputTextBox);
            else if (sender == ModbusRtuDisconnectButton) await DisconnectAsync(_modbusRtuClient, c => _modbusRtuClient = c, ModbusRtuOutputTextBox);
            else if (sender == S7DisconnectButton) await DisconnectAsync(_s7Client, c => _s7Client = c, S7OutputTextBox);
            else await DisconnectAsync(_mcClient, c => _mcClient = c, McOutputTextBox);
        }

        private async Task DisconnectAsync(IIndustrialClient client, Action<IIndustrialClient> assign, TextBox output)
        {
            await RunAsync(output, async () => { if (client == null) return; await client.DisconnectAsync(CancellationToken.None); client.Dispose(); assign(null); Append(output, "已断开"); });
        }

        private async void RawTcpConnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () => { if (_rawTcpClient != null) _rawTcpClient.Dispose(); _rawTcpClient = new TcpTransportClient(new TcpTransportOptions { Host = RawTcpHostTextBox.Text.Trim(), Port = ParsePort(RawTcpPortTextBox.Text), AutoReconnect = false }); await _rawTcpClient.ConnectAsync(CancellationToken.None); Append(RawTcpOutputTextBox, "已连接"); });
        }

        private async void RawTcpSendButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () => { if (_rawTcpClient == null || !_rawTcpClient.IsConnected) throw new InvalidOperationException("请先连接。"); var bytes = Encoding.UTF8.GetBytes(RawTcpPayloadTextBox.Text); await _rawTcpClient.SendAsync(bytes, CancellationToken.None); Append(RawTcpOutputTextBox, "TX: " + RawTcpPayloadTextBox.Text); var response = await _rawTcpClient.ReceiveAsync(4096, CancellationToken.None); Append(RawTcpOutputTextBox, "RX: " + Encoding.UTF8.GetString(response)); });
        }

        private async void RawTcpDisconnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () => { if (_rawTcpClient == null) return; await _rawTcpClient.DisconnectAsync(CancellationToken.None); _rawTcpClient.Dispose(); _rawTcpClient = null; Append(RawTcpOutputTextBox, "已断开"); });
        }

        private async void MesTcpConnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(MesTcpOutputTextBox, async () => { if (_mesTcpClient != null) _mesTcpClient.Dispose(); _mesTcpClient = new MesTcpClient(new MesClientOptions { Host = MesTcpHostTextBox.Text.Trim(), Port = ParsePort(MesTcpPortTextBox.Text), DeviceNo = MesTcpDeviceNoTextBox.Text.Trim(), DeviceName = MesTcpDeviceNameTextBox.Text.Trim(), DeviceIp = MesTcpDeviceIpTextBox.Text.Trim(), DeviceMac = MesTcpDeviceMacTextBox.Text.Trim(), AutoReconnect = false }); _mesTcpClient.RawMessage += (o, a) => Append(MesTcpOutputTextBox, (a.Sent ? "TX: " : "RX: ") + a.Message); await _mesTcpClient.ConnectAsync(CancellationToken.None); Append(MesTcpOutputTextBox, "已连接"); });
        }

        private async void MesTcpOnlineButton_Click(object sender, EventArgs e) { await RunAsync(MesTcpOutputTextBox, async () => { if (_mesTcpClient == null || !_mesTcpClient.IsConnected) throw new InvalidOperationException("请先连接。"); await _mesTcpClient.SendOnlineAsync(CancellationToken.None); }); }
        private async void MesTcpDisconnectButton_Click(object sender, EventArgs e) { await RunAsync(MesTcpOutputTextBox, async () => { if (_mesTcpClient == null) return; await _mesTcpClient.DisconnectAsync(CancellationToken.None); _mesTcpClient.Dispose(); _mesTcpClient = null; Append(MesTcpOutputTextBox, "已断开"); }); }

        private async void MesHttpOnlineButton_Click(object sender, EventArgs e)
        {
            await RunAsync(MesHttpOutputTextBox, async () => { using (var client = new MesHttpClient(new MesHttpClientOptions { BaseUrl = MesHttpUrlTextBox.Text.Trim(), DeviceNo = MesHttpDeviceNoTextBox.Text.Trim(), DeviceName = MesHttpDeviceNameTextBox.Text.Trim(), DeviceIp = MesHttpDeviceIpTextBox.Text.Trim(), DeviceMac = MesHttpDeviceMacTextBox.Text.Trim() })) { var response = await client.SendOnlineAsync(CancellationToken.None); Append(MesHttpOutputTextBox, string.Format("响应: success={0}, code={1}, message={2}", response.IsSuccess, response.Code, response.Message)); } });
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DisposeClient(_modbusTcpClient); DisposeClient(_modbusRtuClient); DisposeClient(_s7Client); DisposeClient(_mcClient);
            if (_rawTcpClient != null) _rawTcpClient.Dispose(); if (_mesTcpClient != null) _mesTcpClient.Dispose();
            base.OnFormClosed(e);
        }

        private static void DisposeClient(IDisposable client) { if (client != null) client.Dispose(); }
        private static async Task RunAsync(TextBox output, Func<Task> action) { try { await action(); } catch (Exception ex) { Append(output, "错误: " + ex.Message); } }
        private static void Append(TextBox output, string text) { if (output.IsDisposed) return; if (output.InvokeRequired) { output.BeginInvoke(new Action<TextBox, string>(Append), output, text); return; } output.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, text, Environment.NewLine)); }
        private static void EnsureConnected(IIndustrialClient client) { if (client == null || !client.IsConnected) throw new InvalidOperationException("请先连接。"); }
        private static DataType ParseDataType(string value) { return (DataType)Enum.Parse(typeof(DataType), value); }
        private static int ParsePort(string value) { var port = ParsePositive(value, "端口"); if (port > 65535) throw new ArgumentOutOfRangeException("端口", "端口必须小于 65536。"); return port; }
        private static byte ParseSlaveId(string value) { var id = ParsePositive(value, "站号"); if (id > 247) throw new ArgumentOutOfRangeException("站号", "站号必须在 1 到 247 之间。"); return (byte)id; }
        private static int ParsePositive(string value, string name) { int result; if (!int.TryParse(value, out result) || result <= 0) throw new FormatException(name + "必须是正整数。"); return result; }
        private static int ParseNonNegative(string value, string name) { int result; if (!int.TryParse(value, out result) || result < 0) throw new FormatException(name + "必须是非负整数。"); return result; }
        private static object ConvertValue(string value, DataType type) { switch (type) { case DataType.Bool: return bool.Parse(value); case DataType.Int16: return short.Parse(value, CultureInfo.InvariantCulture); case DataType.UInt16: return ushort.Parse(value, CultureInfo.InvariantCulture); case DataType.Int32: return int.Parse(value, CultureInfo.InvariantCulture); case DataType.UInt32: return uint.Parse(value, CultureInfo.InvariantCulture); case DataType.Float: return float.Parse(value, CultureInfo.InvariantCulture); case DataType.Double: return double.Parse(value, CultureInfo.InvariantCulture); case DataType.Byte: return byte.Parse(value, CultureInfo.InvariantCulture); case DataType.Char: return char.Parse(value); case DataType.ByteArray: return Convert.FromBase64String(value); default: return value; } }

    }
}
