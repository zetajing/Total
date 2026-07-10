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
    /// <summary>演示 MES 通讯：TCP 长连接 + HTTP API 双模式。</summary>
    public partial class MesTab : UserControl
    {
        private DemoAppContext _ctx;

        // TCP 客户端
        private IMesClient _tcpClient;
        // HTTP 客户端
        private IMesHttpClient _httpClient;

        /// <summary>当前是否为 HTTP 模式。</summary>
        private bool _isHttpMode;

        public MesTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            ApplySavedState();
            UpdateModeDependentVisibility();
        }

        // ── 模式切换 ──

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = GetSelectedModeTag();
            _isHttpMode = string.Equals(tag, "http", StringComparison.OrdinalIgnoreCase);
            UpdateModeDependentVisibility();
        }

        private string GetSelectedModeTag()
        {
            if (MesModeComboBox.SelectedItem is ComboBoxItem item)
                return item.Tag as string ?? "tcp";
            return "tcp";
        }

        private void UpdateModeDependentVisibility()
        {
            // TCP 专用字段
            var isTcp = !_isHttpMode;
            MesHostLabel.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
            MesHostTextBox.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
            MesPortLabel.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
            MesPortTextBox.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;

            // HTTP 专用字段
            MesBaseUrlLabel.Visibility = _isHttpMode ? Visibility.Visible : Visibility.Collapsed;
            MesBaseUrlTextBox.Visibility = _isHttpMode ? Visibility.Visible : Visibility.Collapsed;

            // 提示：HTTP 模式下发上线按钮依然可用
            MesOnlineButton.IsEnabled = _isHttpMode || (_tcpClient != null && _tcpClient.IsConnected);
        }

        // ── 连接 / 断开 ──

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();

                var tag = GetSelectedModeTag();
                if (string.Equals(tag, "http", StringComparison.OrdinalIgnoreCase))
                {
                    await ConnectHttpAsync();
                }
                else
                {
                    await ConnectTcpAsync();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus();
                _ctx.HandleError("MES 连接失败。", ex, true);
            }
        }

        private async Task ConnectTcpAsync()
        {
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

            _tcpClient = client;
            _isHttpMode = false;

            await client.ConnectAsync(CancellationToken.None);
            _ctx.SetHeaderStatus("MES TCP 已连接", Brushes.LightGreen);
        }

        private async Task ConnectHttpAsync()
        {
            var options = new MesHttpClientOptions
            {
                BaseUrl = ParseHelper.RequireText(MesBaseUrlTextBox.Text, "MES API 地址"),
                DeviceNo = ParseHelper.RequireText(MesDeviceNoTextBox.Text, "MES 设备编号"),
                DeviceName = ParseHelper.RequireText(MesDeviceNameTextBox.Text, "MES 设备名称"),
                DeviceIp = ParseHelper.RequireText(MesDeviceIpTextBox.Text, "MES 设备 IP"),
                DeviceMac = ParseHelper.RequireText(MesDeviceMacTextBox.Text, "MES 设备 MAC"),
            };

            var client = new MesHttpClient(options, _ctx.SdkLogger);
            _httpClient = client;
            _isHttpMode = true;

            // HTTP 模式无长连接，发送上线以验证连通性
            if (options.TimeoutMilliseconds > 0)
            {
                var response = await client.SendOnlineAsync(CancellationToken.None);
                if (!response.IsSuccess)
                {
                    _ctx.HandleError("MES HTTP 上线请求返回失败: " + (response.Message ?? response.Code), null, true);
                }
            }

            UpdateStatus();
            _ctx.SetHeaderStatus("MES HTTP 已就绪", Brushes.LightGreen);
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();
                _ctx.SetHeaderStatus("MES 已断开", Brushes.Khaki);
            }
            catch (Exception ex) { _ctx.HandleError("MES 断开失败。", ex, false); }
        }

        public async Task ResetClientAsync()
        {
            // 清理 TCP 客户端
            var tcp = _tcpClient;
            if (tcp != null)
            {
                _tcpClient = null;
                tcp.ConnectionStateChanged -= MesClient_ConnectionStateChanged;
                tcp.FaCheckReceived -= MesClient_FaCheckReceived;
                tcp.FaNumReceived -= MesClient_FaNumReceived;
                tcp.RawMessage -= MesClient_RawMessage;
                tcp.ProtocolError -= MesClient_ProtocolError;
                try { await tcp.DisconnectAsync(CancellationToken.None); } catch { }
                finally { tcp.Dispose(); }
            }

            // 清理 HTTP 客户端
            var http = _httpClient;
            if (http != null)
            {
                _httpClient = null;
                try { http.Dispose(); } catch { }
            }

            _isHttpMode = false;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_isHttpMode)
            {
                if (_httpClient != null && _httpClient.IsConnected)
                {
                    MesStatusTextBlock.Text = "HTTP 就绪";
                    MesStatusTextBlock.Foreground = Brushes.ForestGreen;
                }
                else if (_httpClient != null)
                {
                    MesStatusTextBlock.Text = "HTTP 未就绪";
                    MesStatusTextBlock.Foreground = Brushes.IndianRed;
                }
                else
                {
                    MesStatusTextBlock.Text = "未连接";
                    MesStatusTextBlock.Foreground = Brushes.IndianRed;
                }
            }
            else
            {
                var state = _tcpClient == null ? MesConnectionState.Disconnected : _tcpClient.State;
                switch (state)
                {
                    case MesConnectionState.Connected: MesStatusTextBlock.Text = "已连接"; MesStatusTextBlock.Foreground = Brushes.ForestGreen; break;
                    case MesConnectionState.Connecting: MesStatusTextBlock.Text = "正在连接"; MesStatusTextBlock.Foreground = Brushes.SteelBlue; break;
                    case MesConnectionState.Reconnecting: MesStatusTextBlock.Text = "正在重连"; MesStatusTextBlock.Foreground = Brushes.DarkGoldenrod; break;
                    default: MesStatusTextBlock.Text = "未连接"; MesStatusTextBlock.Foreground = Brushes.IndianRed; break;
                }
            }

            // 更新上线按钮状态
            if (_isHttpMode)
            {
                MesOnlineButton.IsEnabled = _httpClient != null;
            }
            else
            {
                MesOnlineButton.IsEnabled = _tcpClient != null && _tcpClient.IsConnected;
            }
        }

        // ── 发送 ──

        private async void OnlineButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isHttpMode)
                {
                    if (_httpClient == null) throw new InvalidOperationException("请先连接 MES。");
                    var response = await _httpClient.SendOnlineAsync(CancellationToken.None);
                    _ctx.SetHeaderStatus("MES HTTP 上线请求已发送: " + (response.IsSuccess ? "成功" : "失败"), Brushes.LightGreen);
                    AppendTraffic("HTTP 上线", response.IsSuccess ? "成功" : "失败: " + (response.Message ?? response.Code));
                }
                else
                {
                    if (_tcpClient == null || !_tcpClient.IsConnected) throw new InvalidOperationException("请先连接 MES。");
                    await _tcpClient.SendOnlineAsync(CancellationToken.None);
                    _ctx.SetHeaderStatus("MES 上线信息已发送", Brushes.LightGreen);
                }
            }
            catch (Exception ex) { _ctx.HandleError("MES 上线信息发送失败。", ex, true); }
        }

        private async void SendTrackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isHttpMode)
                {
                    if (_httpClient == null) throw new InvalidOperationException("请先连接 MES。");

                    var request = new MesFaTrackRequest
                    {
                        DeviceNo = MesDeviceNoTextBox.Text,
                        DeviceName = MesDeviceNameTextBox.Text,
                        DeviceIp = MesDeviceIpTextBox.Text,
                        DeviceMac = MesDeviceMacTextBox.Text,
                        Process = ParseHelper.RequireText(MesProcessTextBox.Text, "MES 工序编码"),
                        SerialNo = ParseHelper.RequireText(MesSerialNoTextBox.Text, "MES SN"),
                        Number = ParseHelper.RequireText(MesNumberTextBox.Text, "MES 螺丝数量"),
                        Parameters = ParseMesParameters(MesParametersTextBox.Text),
                    };

                    var response = await _httpClient.SendFaTrackAsync(request, CancellationToken.None);
                    _ctx.SetHeaderStatus("MES HTTP FATRACK 已发送", Brushes.LightGreen);
                    AppendTraffic("HTTP FATRACK", "Code=" + (response.Code ?? "(none)") + " Msg=" + (response.Message ?? "(none)"));
                }
                else
                {
                    if (_tcpClient == null || !_tcpClient.IsConnected) throw new InvalidOperationException("请先连接 MES。");

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
                    await _tcpClient.SendTrackAsync(message, CancellationToken.None);
                    _ctx.SetHeaderStatus("MES TCP FATRACK 已发送", Brushes.LightGreen);
                }
            }
            catch (Exception ex) { _ctx.HandleError("MES FATRACK 发送失败。", ex, true); }
        }

        // ── TCP 客户端事件 ──

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
                if (_isHttpMode) return; // HTTP 模式下忽略 TCP 事件
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
                if (_isHttpMode) return;
                var result = e.Message?.Message?.Result ?? string.Empty;
                MesFaNumTextBlock.Text = string.IsNullOrWhiteSpace(result) ? "消息缺少 result" : result;
                MesFaNumTextBlock.Foreground = FormatHelper.ResultBrush(result);
            });
        }

        private void MesClient_RawMessage(object sender, MesRawMessageEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                if (_isHttpMode) return;
                AppendTraffic(e.Sent ? "TCP 发送" : "TCP 接收", e.Message);
            });
        }

        private void MesClient_ProtocolError(object sender, MesProtocolErrorEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                if (_isHttpMode) return;
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
            var state = _ctx.UiState.Mes;
            state.Mode = GetSelectedModeTag();
            state.BaseUrl = MesBaseUrlTextBox.Text;
            state.Host = MesHostTextBox.Text;
            state.Port = MesPortTextBox.Text;
            state.DeviceNo = MesDeviceNoTextBox.Text;
            state.DeviceName = MesDeviceNameTextBox.Text;
            state.DeviceIp = MesDeviceIpTextBox.Text;
            state.DeviceMac = MesDeviceMacTextBox.Text;
            state.Process = MesProcessTextBox.Text;
            state.SerialNo = MesSerialNoTextBox.Text;
            state.Number = MesNumberTextBox.Text;
            state.Parameters = MesParametersTextBox.Text;
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Mes ?? new Services.MesUiState();

            // 恢复模式
            if (!string.IsNullOrEmpty(state.Mode))
            {
                foreach (ComboBoxItem item in MesModeComboBox.Items)
                {
                    if (string.Equals(item.Tag as string, state.Mode, StringComparison.OrdinalIgnoreCase))
                    {
                        MesModeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            _isHttpMode = string.Equals(state.Mode, "http", StringComparison.OrdinalIgnoreCase);

            ComboHelper.SetIfNotEmpty(MesBaseUrlTextBox, state.BaseUrl);
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

            UpdateModeDependentVisibility();
        }
    }
}
