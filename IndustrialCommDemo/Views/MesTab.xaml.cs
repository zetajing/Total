using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk.Mes;

namespace IndustrialCommDemo.Views
{
    public partial class MesTab : UserControl
    {
        private DemoAppContext _ctx;
        private IMesClient _client;

        public MesTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            ApplySavedState();
        }

        public async Task ResetClientAsync()
        {
            var client = _client;
            _client = null;
            if (client == null) { UpdateStatus(); return; }
            client.ConnectionStateChanged -= MesClient_ConnectionStateChanged;
            client.FaCheckReceived -= MesClient_FaCheckReceived;
            client.FaNumReceived -= MesClient_FaNumReceived;
            client.RawMessage -= MesClient_RawMessage;
            client.ProtocolError -= MesClient_ProtocolError;
            try { await client.DisconnectAsync(CancellationToken.None); } catch { }
            finally { client.Dispose(); UpdateStatus(); }
        }

        private void UpdateStatus()
        {
            var state = _client == null ? MesConnectionState.Disconnected : _client.State;
            switch (state)
            {
                case MesConnectionState.Connected: MesStatusTextBlock.Text = "已连接"; MesStatusTextBlock.Foreground = Brushes.ForestGreen; break;
                case MesConnectionState.Connecting: MesStatusTextBlock.Text = "正在连接"; MesStatusTextBlock.Foreground = Brushes.SteelBlue; break;
                case MesConnectionState.Reconnecting: MesStatusTextBlock.Text = "正在重连"; MesStatusTextBlock.Foreground = Brushes.DarkGoldenrod; break;
                default: MesStatusTextBlock.Text = "未连接"; MesStatusTextBlock.Foreground = Brushes.IndianRed; break;
            }
        }

        private void RequireClient()
        {
            if (_client == null || !_client.IsConnected)
                throw new InvalidOperationException("请先连接 MES。");
        }

        // ── Event handlers ──

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();
                var options = new MesClientOptions
                {
                    Host = ParseHelper.RequireText(MesHostTextBox.Text, "MES 主机"),
                    Port = ParseHelper.ParseIntValue(MesPortTextBox.Text, "MES 端口"),
                    DeviceNo = ParseHelper.RequireText(MesDeviceNoTextBox.Text, "MES 设备编号"),
                    DeviceName = ParseHelper.RequireText(MesDeviceNameTextBox.Text, "MES 设备名称"),
                    DeviceIp = ParseHelper.RequireText(MesDeviceIpTextBox.Text, "MES 设备 IP"),
                    DeviceMac = ParseHelper.RequireText(MesDeviceMacTextBox.Text, "MES 设备 MAC"),
                    AutoReconnect = true,
                };
                var client = new MesTcpClient(options, _ctx.SdkLogger);
                client.ConnectionStateChanged += MesClient_ConnectionStateChanged;
                client.FaCheckReceived += MesClient_FaCheckReceived;
                client.FaNumReceived += MesClient_FaNumReceived;
                client.RawMessage += MesClient_RawMessage;
                client.ProtocolError += MesClient_ProtocolError;
                _client = client;
                await client.ConnectAsync(CancellationToken.None);
                _ctx.SetHeaderStatus("MES 已连接", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                UpdateStatus();
                _ctx.HandleError("MES 首次连接失败，后台将继续重连。", ex, true);
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try { await ResetClientAsync(); _ctx.SetHeaderStatus("MES 已断开", Brushes.Khaki); }
            catch (Exception ex) { _ctx.HandleError("MES 断开失败。", ex, false); }
        }

        private async void OnlineButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireClient();
                await _client.SendOnlineAsync(CancellationToken.None);
                _ctx.SetHeaderStatus("MES 上线信息已发送", Brushes.LightGreen);
            }
            catch (Exception ex) { _ctx.HandleError("MES 上线信息发送失败。", ex, true); }
        }

        private async void SendTrackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireClient();
                var message = new FaTrackMessage
                {
                    Message = new FaTrackBody
                    {
                        Process = ParseHelper.RequireText(MesProcessTextBox.Text, "MES 工序编码"),
                        SerialNo = ParseHelper.RequireText(MesSerialNoTextBox.Text, "MES SN"),
                        Number = ParseHelper.RequireText(MesNumberTextBox.Text, "MES 螺丝数量"),
                        Parameters = ParseMesParameters(MesParametersTextBox.Text),
                    },
                };
                await _client.SendTrackAsync(message, CancellationToken.None);
                _ctx.SetHeaderStatus("MES FATRACK 已发送", Brushes.LightGreen);
            }
            catch (Exception ex) { _ctx.HandleError("MES FATRACK 发送失败。", ex, true); }
        }

        // ── MES client events ──

        private void MesClient_ConnectionStateChanged(object sender, MesConnectionStateChangedEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                UpdateStatus();
                if (!string.IsNullOrWhiteSpace(e.ErrorMessage)) MesProtocolErrorTextBlock.Text = e.ErrorMessage;
            });
        }

        private void MesClient_FaCheckReceived(object sender, MesMessageEventArgs<FaCheckMessage> e)
        {
            _ctx.RunOnUi(() =>
            {
                var body = e.Message?.Message;
                var result = body?.Result ?? string.Empty;
                MesFaCheckTextBlock.Text = body == null ? "消息缺少 message 内容" :
                    string.Format(CultureInfo.InvariantCulture, "{0} · SN={1} · 工序={2}{3}", result, body.SerialNo, body.Process,
                        string.IsNullOrWhiteSpace(body.Description) ? string.Empty : " · " + body.Description);
                MesFaCheckTextBlock.Foreground = FormatHelper.ResultBrush(result);
            });
        }

        private void MesClient_FaNumReceived(object sender, MesMessageEventArgs<FaNumMessage> e)
        {
            _ctx.RunOnUi(() =>
            {
                var result = e.Message?.Message?.Result ?? string.Empty;
                MesFaNumTextBlock.Text = string.IsNullOrWhiteSpace(result) ? "消息缺少 result" : result;
                MesFaNumTextBlock.Foreground = FormatHelper.ResultBrush(result);
            });
        }

        private void MesClient_RawMessage(object sender, MesRawMessageEventArgs e)
        {
            _ctx.RunOnUi(() => AppendTraffic(e.Sent ? "发送" : "接收", e.Message));
        }

        private void MesClient_ProtocolError(object sender, MesProtocolErrorEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                MesProtocolErrorTextBlock.Text = e.ErrorMessage +
                    (e.Exception == null ? string.Empty : " " + e.Exception.Message);
            });
        }

        private void AppendTraffic(string direction, string message)
        {
            MesTrafficTextBox.AppendText(string.Format(CultureInfo.InvariantCulture,
                "[{0:HH:mm:ss.fff}] {1} {2}{3}", DateTime.Now, direction, message, Environment.NewLine));
            if (MesTrafficTextBox.Text.Length > 50000)
                MesTrafficTextBox.Text = MesTrafficTextBox.Text.Substring(MesTrafficTextBox.Text.Length - 40000);
            MesTrafficTextBox.ScrollToEnd();
        }

        // ── Helpers ──

        private static Dictionary<string, string> ParseMesParameters(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in (text ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) throw new InvalidOperationException("MES 参数必须使用 key=value 格式，每行一个。");
                var key = line.Substring(0, separator).Trim();
                if (key.Length == 0) throw new InvalidOperationException("MES 参数名不能为空。");
                result[key] = line.Substring(separator + 1).Trim();
            }
            return result;
        }

        // ── State ──

        public void SaveState()
        {
            _ctx.UiState.Mes.Host = MesHostTextBox.Text;
            _ctx.UiState.Mes.Port = MesPortTextBox.Text;
            _ctx.UiState.Mes.DeviceNo = MesDeviceNoTextBox.Text;
            _ctx.UiState.Mes.DeviceName = MesDeviceNameTextBox.Text;
            _ctx.UiState.Mes.DeviceIp = MesDeviceIpTextBox.Text;
            _ctx.UiState.Mes.DeviceMac = MesDeviceMacTextBox.Text;
            _ctx.UiState.Mes.Process = MesProcessTextBox.Text;
            _ctx.UiState.Mes.SerialNo = MesSerialNoTextBox.Text;
            _ctx.UiState.Mes.Number = MesNumberTextBox.Text;
            _ctx.UiState.Mes.Parameters = MesParametersTextBox.Text;
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Mes ?? new Services.MesUiState();
            ComboHelper.SetIfNotEmpty(MesHostTextBox, state.Host);
            ComboHelper.SetIfNotEmpty(MesPortTextBox, state.Port);
            ComboHelper.SetIfNotEmpty(MesDeviceNoTextBox, state.DeviceNo);
            ComboHelper.SetIfNotEmpty(MesDeviceNameTextBox, state.DeviceName);
            ComboHelper.SetIfNotEmpty(MesDeviceIpTextBox, state.DeviceIp);
            ComboHelper.SetIfNotEmpty(MesDeviceMacTextBox, state.DeviceMac);
            ComboHelper.SetIfNotEmpty(MesProcessTextBox, state.Process);
            ComboHelper.SetIfNotEmpty(MesSerialNoTextBox, state.SerialNo);
            ComboHelper.SetIfNotEmpty(MesNumberTextBox, state.Number);
            ComboHelper.SetIfNotEmpty(MesParametersTextBox, state.Parameters);
        }
    }
}
