using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Mes;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Transport;

namespace IndustrialCommMinimal.WinForms
{
    /// <summary>
    /// 工业通讯协议最小验证主窗体。
    /// 每个页签对应一种协议，只提供建立连接、最小读写或报文收发能力，
    /// 用于快速确认 SDK、网络、串口和现场设备参数是否能够正常工作。
    /// </summary>
    public partial class MainForm : Form
    {
        // PLC 类协议统一实现 IIndustrialClient，因此可以复用连接、读取、写入和断开逻辑。
        // 每个页签独立保存客户端，切换页签不会意外复用另一种协议的连接。
        private IIndustrialClient _modbusTcpClient;
        private IIndustrialClient _modbusRtuClient;
        private IIndustrialClient _s7Client;
        private IIndustrialClient _mcClient;

        // 原始 TCP 和 MES TCP 有各自专用 API，不属于统一 PLC 读写抽象，单独持有实例。
        private TcpTransportClient _rawTcpClient;
        private FramedTcpClient _framedTcpClient;
        private MesTcpClient _mesTcpClient;

        /// <summary>创建主窗体并加载由 Visual Studio 设计器维护的全部控件。</summary>
        public MainForm()
        {
            InitializeComponent();
            LoadModbusProfiles();
        }

        /// <summary>读取页面参数，创建并连接通用地址映射的 Modbus TCP 客户端。</summary>
        private async void ModbusTcpConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(ModbusTcpOutputTextBox, () => SimpleClient.ModbusTcp(
                ModbusTcpHostTextBox.Text.Trim(), ParsePort(ModbusTcpPortTextBox.Text), ParseSlaveId(ModbusTcpSlaveTextBox.Text),
                deviceProfile: GetSelectedModbusProfile()),
                client => _modbusTcpClient = client, _modbusTcpClient);
        }

        /// <summary>加载内置和 JSON 注册的 Modbus 设备配置，并默认选择通用映射。</summary>
        private void LoadModbusProfiles()
        {
            ModbusTcpProfileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            ModbusTcpProfileComboBox.DisplayMember = "DisplayName";
            foreach (var profile in ModbusDeviceProfiles.All) ModbusTcpProfileComboBox.Items.Add(profile);
            for (var index = 0; index < ModbusTcpProfileComboBox.Items.Count; index++)
            {
                var profile = (IModbusDeviceProfile)ModbusTcpProfileComboBox.Items[index];
                if (string.Equals(profile.Key, ModbusDeviceProfiles.Generic.Key, StringComparison.OrdinalIgnoreCase))
                {
                    ModbusTcpProfileComboBox.SelectedIndex = index;
                    break;
                }
            }
            ModbusTcpProfileComboBox.SelectedIndexChanged += ModbusTcpProfileComboBox_SelectedIndexChanged;
        }

        private IModbusDeviceProfile GetSelectedModbusProfile()
        {
            return ModbusTcpProfileComboBox.SelectedItem as IModbusDeviceProfile ?? ModbusDeviceProfiles.Generic;
        }

        /// <summary>切换设备映射时同步显示该配置推荐的地址格式，降低地址体系混用风险。</summary>
        private void ModbusTcpProfileComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var profile = GetSelectedModbusProfile();
            if (!string.IsNullOrWhiteSpace(profile.DefaultAddress)) ModbusTcpAddressTextBox.Text = profile.DefaultAddress;
            Append(ModbusTcpOutputTextBox, "设备配置=" + profile.DisplayName + "，地址示例=" + profile.ExampleAddresses);
        }

        /// <summary>按串口、波特率和站号创建 Modbus RTU 客户端；示例默认使用偶校验。</summary>
        private async void ModbusRtuConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(ModbusRtuOutputTextBox, () => SimpleClient.ModbusRtu(
                ModbusRtuPortTextBox.Text.Trim(), ParsePositive(ModbusRtuBaudTextBox.Text, "波特率"), ParseSlaveId(ModbusRtuSlaveTextBox.Text)),
                client => _modbusRtuClient = client, _modbusRtuClient);
        }

        /// <summary>按主机、机架和插槽创建 Siemens S7 客户端并建立连接。</summary>
        private async void S7ConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(S7OutputTextBox, () => SimpleClient.S7(
                S7HostTextBox.Text.Trim(), rack: (short)ParseNonNegative(S7RackTextBox.Text, "机架"), slot: (short)ParseNonNegative(S7SlotTextBox.Text, "插槽")),
                client => _s7Client = client, _s7Client);
        }

        /// <summary>按主机、端口和接收超时创建 Mitsubishi MC 3E 客户端。</summary>
        private async void McConnectButton_Click(object sender, EventArgs e)
        {
            await ConnectIndustrialAsync(McOutputTextBox, () => SimpleClient.Mc(
                McHostTextBox.Text.Trim(), ParsePort(McPortTextBox.Text), ParsePositive(McTimeoutTextBox.Text, "接收超时")),
                client => _mcClient = client, _mcClient);
        }

        /// <summary>
        /// 执行 PLC 类协议的公共连接流程：释放旧实例、创建新实例、保存引用并异步连接。
        /// 所有异常交给 <see cref="RunAsync"/> 转换成页面日志，避免 async void 事件导致程序退出。
        /// </summary>
        /// <param name="output">当前协议页签的日志输出框。</param>
        /// <param name="factory">根据页面参数创建协议客户端的工厂函数。</param>
        /// <param name="assign">将新客户端写回对应窗体字段的回调。</param>
        /// <param name="oldClient">当前页签此前创建的客户端；存在时先释放。</param>
        private async Task ConnectIndustrialAsync(TextBox output, Func<IIndustrialClient> factory, Action<IIndustrialClient> assign, IIndustrialClient oldClient)
        {
            await RunAsync(output, async () =>
            {
                if (oldClient != null) oldClient.Dispose();
                var client = factory();
                assign(client);
                await client.ConnectAsync(CancellationToken.None);
                Append(output, "已连接 " + client.DeviceId);
                AppendDiagnostics(output, client);
            });
        }

        /// <summary>四个 PLC 读取按钮的统一入口，根据事件来源路由到对应客户端和控件。</summary>
        private async void IndustrialReadButton_Click(object sender, EventArgs e)
        {
            if (sender == ModbusTcpReadButton) await ReadAsync(_modbusTcpClient, ModbusTcpAddressTextBox, ModbusTcpTypeComboBox, ModbusTcpOutputTextBox);
            else if (sender == ModbusRtuReadButton) await ReadAsync(_modbusRtuClient, ModbusRtuAddressTextBox, ModbusRtuTypeComboBox, ModbusRtuOutputTextBox);
            else if (sender == S7ReadButton) await ReadAsync(_s7Client, S7AddressTextBox, S7TypeComboBox, S7OutputTextBox);
            else await ReadAsync(_mcClient, McAddressTextBox, McTypeComboBox, McOutputTextBox);
        }

        /// <summary>
        /// 使用页面选择的地址和数据类型执行一次读取，并输出值、质量状态和错误信息。
        /// </summary>
        private async Task ReadAsync(IIndustrialClient client, TextBox address, ComboBox type, TextBox output)
        {
            await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = ParseDataType(type.Text);
                var result = await client.ReadAsync(new ReadRequest(client.DeviceId, address.Text.Trim(), dataType), CancellationToken.None);
                Append(output, string.Format(CultureInfo.InvariantCulture, "读取 {0}: {1} [{2}] {3}", result.Address, result.Value ?? "(null)", result.Quality, result.ErrorMessage));
                AppendDiagnostics(output, client);
            });
        }

        /// <summary>四个 PLC 写入按钮的统一入口，根据事件来源选择对应页面参数。</summary>
        private async void IndustrialWriteButton_Click(object sender, EventArgs e)
        {
            if (sender == ModbusTcpWriteButton) await WriteAsync(_modbusTcpClient, ModbusTcpAddressTextBox, ModbusTcpTypeComboBox, ModbusTcpValueTextBox, ModbusTcpOutputTextBox);
            else if (sender == ModbusRtuWriteButton) await WriteAsync(_modbusRtuClient, ModbusRtuAddressTextBox, ModbusRtuTypeComboBox, ModbusRtuValueTextBox, ModbusRtuOutputTextBox);
            else if (sender == S7WriteButton) await WriteAsync(_s7Client, S7AddressTextBox, S7TypeComboBox, S7ValueTextBox, S7OutputTextBox);
            else await WriteAsync(_mcClient, McAddressTextBox, McTypeComboBox, McValueTextBox, McOutputTextBox);
        }

        /// <summary>把页面文本转换为 SDK 数据类型，构造写入请求并执行单点写入。</summary>
        private async Task WriteAsync(IIndustrialClient client, TextBox address, ComboBox type, TextBox value, TextBox output)
        {
            await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = ParseDataType(type.Text);
                await client.WriteAsync(new WriteRequest(client.DeviceId, address.Text.Trim(), dataType, ConvertValue(value.Text, dataType)), CancellationToken.None);
                Append(output, "写入完成");
                AppendDiagnostics(output, client);
            });
        }

        /// <summary>四个 PLC 断开按钮的统一入口，断开后同步清空对应窗体字段。</summary>
        private async void IndustrialDisconnectButton_Click(object sender, EventArgs e)
        {
            if (sender == ModbusTcpDisconnectButton) await DisconnectAsync(_modbusTcpClient, c => _modbusTcpClient = c, ModbusTcpOutputTextBox);
            else if (sender == ModbusRtuDisconnectButton) await DisconnectAsync(_modbusRtuClient, c => _modbusRtuClient = c, ModbusRtuOutputTextBox);
            else if (sender == S7DisconnectButton) await DisconnectAsync(_s7Client, c => _s7Client = c, S7OutputTextBox);
            else await DisconnectAsync(_mcClient, c => _mcClient = c, McOutputTextBox);
        }

        /// <summary>安全断开并释放一个统一协议客户端；空实例视为已经断开。</summary>
        private async Task DisconnectAsync(IIndustrialClient client, Action<IIndustrialClient> assign, TextBox output)
        {
            await RunAsync(output, async () => { if (client == null) return; await client.DisconnectAsync(CancellationToken.None); client.Dispose(); assign(null); Append(output, "已断开"); });
        }

        /// <summary>创建原始 TCP 传输客户端并连接页面指定的远程端点。</summary>
        private async void RawTcpConnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () =>
            {
                if (_rawTcpClient != null) { _rawTcpClient.Dispose(); _rawTcpClient = null; }
                if (_framedTcpClient != null) { _framedTcpClient.Dispose(); _framedTcpClient = null; }
                var options = new TcpTransportOptions { Host = RawTcpHostTextBox.Text.Trim(), Port = ParsePort(RawTcpPortTextBox.Text), AutoReconnect = false };
                if (RawTcpFramingComboBox.Text == "原始字节流")
                {
                    _rawTcpClient = new TcpTransportClient(options);
                    await _rawTcpClient.ConnectAsync(CancellationToken.None);
                }
                else
                {
                    _framedTcpClient = new FramedTcpClient(options, CreateSelectedFramer());
                    await _framedTcpClient.ConnectAsync(CancellationToken.None);
                }
                Append(RawTcpOutputTextBox, "已连接，模式=" + RawTcpFramingComboBox.Text);
            });
        }

        /// <summary>
        /// 将页面文本按 UTF-8 编码发送，并读取最多 4096 字节的单批响应。
        /// 注意：TCP 是字节流，本页面仅用于最小验证，不把一次接收视为通用业务分帧方案。
        /// </summary>
        private async void RawTcpSendButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () =>
            {
                var bytes = Encoding.UTF8.GetBytes(RawTcpPayloadTextBox.Text);
                byte[] response;
                if (_framedTcpClient != null && _framedTcpClient.IsConnected)
                {
                    await _framedTcpClient.SendFrameAsync(bytes, CancellationToken.None);
                    response = await _framedTcpClient.ReceiveFrameAsync(CancellationToken.None);
                }
                else
                {
                    if (_rawTcpClient == null || !_rawTcpClient.IsConnected) throw new InvalidOperationException("请先连接。");
                    await _rawTcpClient.SendAsync(bytes, CancellationToken.None);
                    response = await _rawTcpClient.ReceiveAsync(4096, CancellationToken.None);
                }
                Append(RawTcpOutputTextBox, "TX: " + RawTcpPayloadTextBox.Text);
                Append(RawTcpOutputTextBox, "RX: " + Encoding.UTF8.GetString(response));
            });
        }

        /// <summary>主动断开原始 TCP 连接并释放套接字相关资源。</summary>
        private async void RawTcpDisconnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () =>
            {
                if (_rawTcpClient != null) { await _rawTcpClient.DisconnectAsync(CancellationToken.None); _rawTcpClient.Dispose(); _rawTcpClient = null; }
                if (_framedTcpClient != null) { await _framedTcpClient.DisconnectAsync(CancellationToken.None); _framedTcpClient.Dispose(); _framedTcpClient = null; }
                Append(RawTcpOutputTextBox, "已断开");
            });
        }

        /// <summary>
        /// 根据 MES 身份和端点参数创建长连接客户端。
        /// 示例关闭自动重连，使连接按钮的结果更适合人工最小验证；原始收发报文写入页面日志。
        /// </summary>
        private async void MesTcpConnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(MesTcpOutputTextBox, async () => { if (_mesTcpClient != null) _mesTcpClient.Dispose(); _mesTcpClient = new MesTcpClient(new MesClientOptions { Host = MesTcpHostTextBox.Text.Trim(), Port = ParsePort(MesTcpPortTextBox.Text), DeviceNo = MesTcpDeviceNoTextBox.Text.Trim(), DeviceName = MesTcpDeviceNameTextBox.Text.Trim(), DeviceIp = MesTcpDeviceIpTextBox.Text.Trim(), DeviceMac = MesTcpDeviceMacTextBox.Text.Trim(), AutoReconnect = false }); _mesTcpClient.RawMessage += (o, a) => Append(MesTcpOutputTextBox, (a.Sent ? "TX: " : "RX: ") + a.Message); await _mesTcpClient.ConnectAsync(CancellationToken.None); Append(MesTcpOutputTextBox, "已连接"); });
        }

        /// <summary>通过已建立的 MES TCP 长连接发送设备上线报文。</summary>
        private async void MesTcpOnlineButton_Click(object sender, EventArgs e) { await RunAsync(MesTcpOutputTextBox, async () => { if (_mesTcpClient == null || !_mesTcpClient.IsConnected) throw new InvalidOperationException("请先连接。"); await _mesTcpClient.SendOnlineAsync(CancellationToken.None); }); }

        /// <summary>停止 MES TCP 连接并释放后台接收任务、套接字和同步资源。</summary>
        private async void MesTcpDisconnectButton_Click(object sender, EventArgs e) { await RunAsync(MesTcpOutputTextBox, async () => { if (_mesTcpClient == null) return; await _mesTcpClient.DisconnectAsync(CancellationToken.None); _mesTcpClient.Dispose(); _mesTcpClient = null; Append(MesTcpOutputTextBox, "已断开"); }); }

        /// <summary>
        /// 创建一次性 MES HTTP 客户端并发送上线请求。
        /// HTTP 无需预先建立长连接，因此客户端在单次验证完成后立即释放。
        /// </summary>
        private async void MesHttpOnlineButton_Click(object sender, EventArgs e)
        {
            await RunAsync(MesHttpOutputTextBox, async () => { using (var client = new MesHttpClient(new MesHttpClientOptions { BaseUrl = MesHttpUrlTextBox.Text.Trim(), DeviceNo = MesHttpDeviceNoTextBox.Text.Trim(), DeviceName = MesHttpDeviceNameTextBox.Text.Trim(), DeviceIp = MesHttpDeviceIpTextBox.Text.Trim(), DeviceMac = MesHttpDeviceMacTextBox.Text.Trim() })) { var response = await client.SendOnlineAsync(CancellationToken.None); Append(MesHttpOutputTextBox, string.Format("响应: success={0}, code={1}, message={2}", response.IsSuccess, response.Code, response.Message)); } });
        }

        /// <summary>窗体关闭时统一释放全部协议客户端，避免串口、套接字或后台任务残留。</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DisposeClient(_modbusTcpClient); DisposeClient(_modbusRtuClient); DisposeClient(_s7Client); DisposeClient(_mcClient);
            if (_rawTcpClient != null) _rawTcpClient.Dispose(); if (_framedTcpClient != null) _framedTcpClient.Dispose(); if (_mesTcpClient != null) _mesTcpClient.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>释放可空资源，便于关闭流程统一处理不同客户端类型。</summary>
        private static void DisposeClient(IDisposable client) { if (client != null) client.Dispose(); }

        /// <summary>执行 UI 异步操作并把异常转换成当前页签日志。</summary>
        private static async Task RunAsync(TextBox output, Func<Task> action) { try { await action(); } catch (Exception ex) { Append(output, "错误: " + ex.Message); } }

        /// <summary>以时间戳追加日志；来自 SDK 后台线程时自动切换回 UI 线程。</summary>
        private static void Append(TextBox output, string text) { if (output.IsDisposed) return; if (output.InvokeRequired) { output.BeginInvoke(new Action<TextBox, string>(Append), output, text); return; } output.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, text, Environment.NewLine)); }

        /// <summary>在读写前确认客户端存在且底层连接仍然可用。</summary>
        private static void EnsureConnected(IIndustrialClient client) { if (client == null || !client.IsConnected) throw new InvalidOperationException("请先连接。"); }

        /// <summary>把设计器下拉框中的类型名称转换为 SDK 数据类型枚举。</summary>
        private static DataType ParseDataType(string value) { return (DataType)Enum.Parse(typeof(DataType), value); }

        private ITcpMessageFramer CreateSelectedFramer()
        {
            var maximum = ParsePositive(RawTcpMaximumFrameLengthTextBox.Text, "最大帧长");
            switch (RawTcpFramingComboBox.Text)
            {
                case "固定长度": return new FixedLengthMessageFramer(ParsePositive(RawTcpFrameLengthTextBox.Text, "固定帧长"));
                case "分隔符": return new DelimiterMessageFramer(ParseEscapedBytes(RawTcpDelimiterTextBox.Text), maximum);
                case "2字节长度头": return new LengthPrefixMessageFramer(2, maximum);
                case "4字节长度头": return new LengthPrefixMessageFramer(4, maximum);
                default: throw new InvalidOperationException("请选择有效的分帧模式。");
            }
        }

        private static byte[] ParseEscapedBytes(string text)
        {
            var value = (text ?? string.Empty).Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\0", "\0");
            if (value.Length == 0) throw new FormatException("分隔符不能为空。");
            return Encoding.UTF8.GetBytes(value);
        }

        private static void AppendDiagnostics(TextBox output, IIndustrialClient client)
        {
            var snapshot = client.GetDiagnosticSnapshot();
            Append(output, string.Format(CultureInfo.InvariantCulture,
                "诊断: 耗时={0}ms, 总计={1}, 失败={2}, 超时={3}, 最近分类={4}",
                snapshot.LastOperationElapsedMilliseconds, snapshot.TotalOperations, snapshot.FailedOperations,
                snapshot.TimeoutCount, snapshot.LastFailureCategory));
        }

        /// <summary>解析 TCP 端口并限制在标准 1~65535 范围内。</summary>
        private static int ParsePort(string value) { var port = ParsePositive(value, "端口"); if (port > 65535) throw new ArgumentOutOfRangeException("端口", "端口必须小于 65536。"); return port; }

        /// <summary>解析 Modbus 单播站号并限制在协议允许的 1~247 范围内。</summary>
        private static byte ParseSlaveId(string value) { var id = ParsePositive(value, "站号"); if (id > 247) throw new ArgumentOutOfRangeException("站号", "站号必须在 1 到 247 之间。"); return (byte)id; }

        /// <summary>解析必须大于零的整数参数，例如波特率和超时时间。</summary>
        private static int ParsePositive(string value, string name) { int result; if (!int.TryParse(value, out result) || result <= 0) throw new FormatException(name + "必须是正整数。"); return result; }

        /// <summary>解析允许为零的整数参数，例如 S7 机架号和插槽号。</summary>
        private static int ParseNonNegative(string value, string name) { int result; if (!int.TryParse(value, out result) || result < 0) throw new FormatException(name + "必须是非负整数。"); return result; }

        /// <summary>
        /// 按页面选择的数据类型转换写入文本。
        /// 数值统一使用不受系统区域影响的格式；ByteArray 使用 Base64，String 保留原文本。
        /// </summary>
        private static object ConvertValue(string value, DataType type) { switch (type) { case DataType.Bool: return bool.Parse(value); case DataType.Int16: return short.Parse(value, CultureInfo.InvariantCulture); case DataType.UInt16: return ushort.Parse(value, CultureInfo.InvariantCulture); case DataType.Int32: return int.Parse(value, CultureInfo.InvariantCulture); case DataType.UInt32: return uint.Parse(value, CultureInfo.InvariantCulture); case DataType.Float: return float.Parse(value, CultureInfo.InvariantCulture); case DataType.Double: return double.Parse(value, CultureInfo.InvariantCulture); case DataType.Byte: return byte.Parse(value, CultureInfo.InvariantCulture); case DataType.Char: return char.Parse(value); case DataType.ByteArray: return Convert.FromBase64String(value); default: return value; } }

    }
}
