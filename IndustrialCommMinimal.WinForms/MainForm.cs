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
    public sealed class MainForm : Form
    {
        public MainForm()
        {
            Text = "IndustrialCommSdk 协议最小系统";
            Width = 1000;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(CreateIndustrialPage("Modbus TCP", "127.0.0.1", "502", "站号", "1", "D100", CreateModbusTcp));
            tabs.TabPages.Add(CreateIndustrialPage("Modbus RTU", "COM3", "9600", "站号", "1", "HR0", CreateModbusRtu));
            tabs.TabPages.Add(CreateIndustrialPage("Siemens S7", "127.0.0.1", "102", "插槽", "1", "DB1.DBW0", (h, p, slot) => IndustrialClientFactory.SiemensS7(h, rack: 0, slot: (short)ParsePositive(slot, "插槽"))));
            tabs.TabPages.Add(CreateIndustrialPage("Mitsubishi MC", "127.0.0.1", "5000", "接收超时(ms)", "5000", "D100", (h, p, timeout) => IndustrialClientFactory.MitsubishiMc(h, ParsePort(p), receiveTimeoutMilliseconds: ParsePositive(timeout, "接收超时"))));
            tabs.TabPages.Add(CreateRawTcpPage());
            tabs.TabPages.Add(CreateMesTcpPage());
            tabs.TabPages.Add(CreateMesHttpPage());
            Controls.Add(tabs);
        }

        private static TabPage CreateIndustrialPage(string title, string hostValue, string portValue, string optionLabel, string optionValue, string addressValue, Func<string, string, string, IIndustrialClient> factory)
        {
            var page = new TabPage(title);
            var layout = CreateLayout();
            var host = AddField(layout, 0, title == "Modbus RTU" ? "串口" : "主机", hostValue);
            var port = AddField(layout, 1, title == "Modbus RTU" ? "波特率" : "端口", portValue);
            var option = AddField(layout, 2, optionLabel, optionValue);
            var address = AddField(layout, 3, "地址", addressValue);
            var type = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            type.Items.AddRange(Enum.GetNames(typeof(DataType)));
            type.SelectedItem = DataType.Int16.ToString();
            AddControl(layout, 4, "类型", type);
            var value = AddField(layout, 5, "写入值", "0");
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var connect = AddButton(buttons, "连接");
            var read = AddButton(buttons, "读取");
            var write = AddButton(buttons, "写入");
            var disconnect = AddButton(buttons, "断开");
            layout.Controls.Add(buttons, 1, 6);
            var output = AddOutput(layout, 7);
            page.Controls.Add(layout);

            IIndustrialClient client = null;
            connect.Click += async (s, e) => await RunAsync(output, async () =>
            {
                if (client != null) client.Dispose();
                client = factory(host.Text.Trim(), port.Text.Trim(), option.Text.Trim());
                await client.ConnectAsync(CancellationToken.None);
                Append(output, "已连接 " + client.DeviceId);
            });
            read.Click += async (s, e) => await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = (DataType)Enum.Parse(typeof(DataType), type.Text);
                var result = await client.ReadAsync(new ReadRequest(client.DeviceId, address.Text.Trim(), dataType), CancellationToken.None);
                Append(output, string.Format(CultureInfo.InvariantCulture, "读取 {0}: {1} [{2}] {3}", result.Address, result.Value ?? "(null)", result.Quality, result.ErrorMessage));
            });
            write.Click += async (s, e) => await RunAsync(output, async () =>
            {
                EnsureConnected(client);
                var dataType = (DataType)Enum.Parse(typeof(DataType), type.Text);
                await client.WriteAsync(new WriteRequest(client.DeviceId, address.Text.Trim(), dataType, ConvertValue(value.Text, dataType)), CancellationToken.None);
                Append(output, "写入完成");
            });
            disconnect.Click += async (s, e) => await RunAsync(output, async () =>
            {
                if (client == null) return;
                await client.DisconnectAsync(CancellationToken.None);
                client.Dispose();
                client = null;
                Append(output, "已断开");
            });
            page.Disposed += (s, e) => { if (client != null) client.Dispose(); };
            return page;
        }

        private static IIndustrialClient CreateModbusTcp(string host, string port, string slaveId)
        {
            return IndustrialClientFactory.ModbusTcp(host, ParsePort(port), ParseSlaveId(slaveId), deviceProfile: ModbusDeviceProfiles.Generic);
        }

        private static IIndustrialClient CreateModbusRtu(string portName, string baudRate, string slaveId)
        {
            return IndustrialClientFactory.ModbusRtu(portName, ParsePositive(baudRate, "波特率"), ParseSlaveId(slaveId), parity: Parity.Even);
        }

        private static TabPage CreateRawTcpPage()
        {
            var page = new TabPage("原始 TCP");
            var layout = CreateLayout();
            var host = AddField(layout, 0, "主机", "127.0.0.1");
            var port = AddField(layout, 1, "端口", "9000");
            var payload = AddField(layout, 2, "发送文本", "hello");
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var connect = AddButton(buttons, "连接");
            var send = AddButton(buttons, "发送并接收");
            var disconnect = AddButton(buttons, "断开");
            layout.Controls.Add(buttons, 1, 3);
            var output = AddOutput(layout, 4);
            page.Controls.Add(layout);
            TcpTransportClient client = null;

            connect.Click += async (s, e) => await RunAsync(output, async () =>
            {
                if (client != null) client.Dispose();
                client = new TcpTransportClient(new TcpTransportOptions { Host = host.Text.Trim(), Port = ParsePort(port.Text), AutoReconnect = false });
                await client.ConnectAsync(CancellationToken.None);
                Append(output, "已连接");
            });
            send.Click += async (s, e) => await RunAsync(output, async () =>
            {
                if (client == null || !client.IsConnected) throw new InvalidOperationException("请先连接。");
                var bytes = Encoding.UTF8.GetBytes(payload.Text);
                await client.SendAsync(bytes, CancellationToken.None);
                Append(output, "TX: " + payload.Text);
                var response = await client.ReceiveAsync(4096, CancellationToken.None);
                Append(output, "RX: " + Encoding.UTF8.GetString(response));
            });
            disconnect.Click += async (s, e) => await RunAsync(output, async () =>
            {
                if (client == null) return;
                await client.DisconnectAsync(CancellationToken.None);
                client.Dispose(); client = null; Append(output, "已断开");
            });
            page.Disposed += (s, e) => { if (client != null) client.Dispose(); };
            return page;
        }

        private static TabPage CreateMesTcpPage()
        {
            var page = new TabPage("MES TCP");
            var layout = CreateLayout();
            var host = AddField(layout, 0, "主机", "127.0.0.1");
            var port = AddField(layout, 1, "端口", "9312");
            var device = AddField(layout, 2, "设备编号", "DEVICE-001");
            var deviceName = AddField(layout, 3, "设备名称", "MinimalClient");
            var deviceIp = AddField(layout, 4, "设备 IP", "127.0.0.1");
            var deviceMac = AddField(layout, 5, "设备 MAC", "00-00-00-00-00-00");
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var connect = AddButton(buttons, "连接"); var online = AddButton(buttons, "发送上线"); var disconnect = AddButton(buttons, "断开");
            layout.Controls.Add(buttons, 1, 6); var output = AddOutput(layout, 7); page.Controls.Add(layout);
            MesTcpClient client = null;
            connect.Click += async (s, e) => await RunAsync(output, async () =>
            {
                if (client != null) client.Dispose();
                client = new MesTcpClient(new MesClientOptions { Host = host.Text.Trim(), Port = ParsePort(port.Text), DeviceNo = device.Text.Trim(), DeviceName = deviceName.Text.Trim(), DeviceIp = deviceIp.Text.Trim(), DeviceMac = deviceMac.Text.Trim(), AutoReconnect = false });
                client.RawMessage += (o, a) => Append(output, (a.Sent ? "TX: " : "RX: ") + a.Message);
                await client.ConnectAsync(CancellationToken.None); Append(output, "已连接");
            });
            online.Click += async (s, e) => await RunAsync(output, async () => { if (client == null || !client.IsConnected) throw new InvalidOperationException("请先连接。"); await client.SendOnlineAsync(CancellationToken.None); });
            disconnect.Click += async (s, e) => await RunAsync(output, async () => { if (client == null) return; await client.DisconnectAsync(CancellationToken.None); client.Dispose(); client = null; Append(output, "已断开"); });
            page.Disposed += (s, e) => { if (client != null) client.Dispose(); };
            return page;
        }

        private static TabPage CreateMesHttpPage()
        {
            var page = new TabPage("MES HTTP"); var layout = CreateLayout();
            var url = AddField(layout, 0, "API 地址", "http://127.0.0.1:8080/api");
            var device = AddField(layout, 1, "设备编号", "DEVICE-001");
            var deviceName = AddField(layout, 2, "设备名称", "MinimalClient");
            var deviceIp = AddField(layout, 3, "设备 IP", "127.0.0.1");
            var deviceMac = AddField(layout, 4, "设备 MAC", "00-00-00-00-00-00");
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; var online = AddButton(buttons, "发送上线");
            layout.Controls.Add(buttons, 1, 5); var output = AddOutput(layout, 6); page.Controls.Add(layout);
            online.Click += async (s, e) => await RunAsync(output, async () =>
            {
                using (var client = new MesHttpClient(new MesHttpClientOptions { BaseUrl = url.Text.Trim(), DeviceNo = device.Text.Trim(), DeviceName = deviceName.Text.Trim(), DeviceIp = deviceIp.Text.Trim(), DeviceMac = deviceMac.Text.Trim() }))
                {
                    var response = await client.SendOnlineAsync(CancellationToken.None);
                    Append(output, string.Format("响应: success={0}, code={1}, message={2}", response.IsSuccess, response.Code, response.Message));
                }
            });
            return page;
        }

        private static TableLayoutPanel CreateLayout()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, AutoScroll = true };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }
        private static TextBox AddField(TableLayoutPanel panel, int row, string label, string value) { var box = new TextBox { Dock = DockStyle.Fill, Text = value }; AddControl(panel, row, label, box); return box; }
        private static void AddControl(TableLayoutPanel panel, int row, string label, Control control) { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) }, 0, row); panel.Controls.Add(control, 1, row); }
        private static Button AddButton(Control parent, string text) { var button = new Button { Text = text, AutoSize = true }; parent.Controls.Add(button); return button; }
        private static TextBox AddOutput(TableLayoutPanel panel, int row) { var output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical }; panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.Controls.Add(output, 0, row); panel.SetColumnSpan(output, 2); return output; }
        private static async Task RunAsync(TextBox output, Func<Task> action) { try { await action(); } catch (Exception ex) { Append(output, "错误: " + ex.Message); } }
        private static void Append(TextBox output, string text) { if (output.IsDisposed) return; if (output.InvokeRequired) { output.BeginInvoke(new Action<TextBox, string>(Append), output, text); return; } output.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, text, Environment.NewLine)); }
        private static void EnsureConnected(IIndustrialClient client) { if (client == null || !client.IsConnected) throw new InvalidOperationException("请先连接。"); }
        private static int ParsePort(string value) { var port = ParsePositive(value, "端口"); if (port > 65535) throw new ArgumentOutOfRangeException("端口", "端口必须小于 65536。"); return port; }
        private static byte ParseSlaveId(string value) { var slaveId = ParsePositive(value, "站号"); if (slaveId > 247) throw new ArgumentOutOfRangeException("站号", "站号必须在 1 到 247 之间。"); return (byte)slaveId; }
        private static int ParsePositive(string value, string name) { int result; if (!int.TryParse(value, out result) || result <= 0) throw new FormatException(name + "必须是正整数。"); return result; }
        private static object ConvertValue(string value, DataType type)
        {
            switch (type) { case DataType.Bool: return bool.Parse(value); case DataType.Int16: return short.Parse(value, CultureInfo.InvariantCulture); case DataType.UInt16: return ushort.Parse(value, CultureInfo.InvariantCulture); case DataType.Int32: return int.Parse(value, CultureInfo.InvariantCulture); case DataType.UInt32: return uint.Parse(value, CultureInfo.InvariantCulture); case DataType.Float: return float.Parse(value, CultureInfo.InvariantCulture); case DataType.Double: return double.Parse(value, CultureInfo.InvariantCulture); case DataType.Byte: return byte.Parse(value, CultureInfo.InvariantCulture); case DataType.Char: return char.Parse(value); case DataType.ByteArray: return Convert.FromBase64String(value); default: return value; }
        }
    }
}
