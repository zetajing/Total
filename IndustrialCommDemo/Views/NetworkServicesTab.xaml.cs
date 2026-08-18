using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Services;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.FileTransfer.Ftp;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Runtime.Security;
using IndustrialCommSdk.Web.Gateway;
using IndustrialCommSdk.Web.Http;
using IndustrialCommSdk.Web.WebSockets;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace IndustrialCommDemo.Views
{
    /// <summary>MQTT、WebAPI/WebSocket 与 FTP/FTPS 的统一运行和联调页面。</summary>
    public partial class NetworkServicesTab : UserControl
    {
        private DemoAppContext _ctx;
        private HttpApiClient _httpClient;
        private MqttClient _mqttClient;
        private IndustrialWebSocketClient _webSocketClient;
        private IMqttBrokerService _observedBroker;
        private IIndustrialWebGateway _observedWebGateway;
        private CancellationTokenSource _ftpTransferCancellation;
        private JsonConfigurationValidationService _jsonValidation;
        private bool _reset;

        public NetworkServicesTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _jsonValidation = new JsonConfigurationValidationService(
                _ctx.Runtime.Sdk,
                Path.GetDirectoryName(_ctx.NetworkServices.ConfigurationFilePath),
                _ctx.SdkLogger);
            _httpClient = new HttpApiClient(new HttpApiClientOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxResponseContentBytes = 4 * 1024 * 1024,
            });
            ApplyConfiguration(_ctx.NetworkServices.Configuration);
            ApplySavedState();
            _ctx.NetworkServices.StateChanged += NetworkServices_StateChanged;
            UpdateServiceStatuses();
        }

        public async Task StartAutoServicesAsync()
        {
            if (_ctx == null || _reset) return;
            try
            {
                await _ctx.NetworkServices.StartAutoServicesAsync(CancellationToken.None);
                if (_reset)
                {
                    await _ctx.NetworkServices.StopAllAsync(CancellationToken.None);
                    return;
                }
                AttachBrokerEvents();
                AttachWebGatewayEvents();
                await RefreshMqttClientsAsync();
                RefreshWebSocketSessions();
                UpdateServiceStatuses();
            }
            catch (Exception ex)
            {
                _ctx.HandleError("网络服务自动启动失败。", ex, false);
                UpdateServiceStatuses();
            }
        }

        public async Task ResetAsync()
        {
            if (_reset) return;
            _reset = true;
            _ctx.NetworkServices.StateChanged -= NetworkServices_StateChanged;
            CancelFtpTransfer();
            DetachBrokerEvents();
            DetachWebGatewayEvents();
            await ResetMqttClientAsync();
            await ResetWebSocketClientAsync();
            try { await _ctx.NetworkServices.StopAllAsync(CancellationToken.None); }
            catch (Exception ex) { _ctx.HandleError("网络服务停止失败。", ex, false); }
            _httpClient?.Dispose();
            _httpClient = null;
            MqttBrokerPasswordBox.Clear();
            MqttClientPasswordBox.Clear();
            WebApiKeyPasswordBox.Clear();
            WebSocketApiKeyPasswordBox.Clear();
            FtpPasswordBox.Clear();
        }

        public void SaveState()
        {
            if (_ctx == null) return;
            var state = _ctx.UiState.NetworkServices;
            state.SelectedTabIndex = NetworkServicesTabControl.SelectedIndex;
            state.MqttClientHost = MqttClientHostTextBox.Text;
            state.MqttClientPort = MqttClientPortTextBox.Text;
            state.HttpRequestUrl = HttpUrlTextBox.Text;
            state.WebSocketUrl = WebSocketUrlTextBox.Text;
            state.FtpRemotePath = FtpRemotePathTextBox.Text;
        }

        private void ApplyConfiguration(NetworkServicesConfiguration configuration)
        {
            var mqtt = configuration.MqttBroker;
            MqttBindAddressTextBox.Text = mqtt.BindAddress;
            MqttPortTextBox.Text = mqtt.Port.ToString();
            MqttUseTlsCheckBox.IsChecked = mqtt.UseTls;
            MqttTlsPortTextBox.Text = mqtt.TlsPort.ToString();
            MqttCertificateTextBox.Text = mqtt.CertificateThumbprint ?? string.Empty;
            MqttUsernameTextBox.Text = mqtt.Username;
            MqttAutoStartCheckBox.IsChecked = mqtt.AutoStart;
            MqttClientHostTextBox.Text = mqtt.BindAddress;
            MqttClientPortTextBox.Text = (mqtt.UseTls ? mqtt.TlsPort : mqtt.Port).ToString();
            MqttClientTlsCheckBox.IsChecked = mqtt.UseTls;
            MqttClientUsernameTextBox.Text = mqtt.Username;

            var web = configuration.WebGateway;
            WebListenPrefixTextBox.Text = web.ListenPrefix;
            WebOriginsTextBox.Text = string.Join(Environment.NewLine, web.AllowedOrigins ?? new List<string>());
            WebEnableWritesCheckBox.IsChecked = web.EnableRemoteWrites;
            WebAllowRawReadsCheckBox.IsChecked = web.AllowRawAddressReads;
            WebExposeAddressesCheckBox.IsChecked = web.ExposeRawAddresses;
            WebAutoStartCheckBox.IsChecked = web.AutoStart;
            HttpUrlTextBox.Text = BuildWebEndpoint(web.ListenPrefix, "api/v1/health").AbsoluteUri;
            WebSocketUrlTextBox.Text = BuildWebSocketEndpoint(web.ListenPrefix).AbsoluteUri;

            var ftp = configuration.Ftp;
            FtpHostTextBox.Text = ftp.Host;
            FtpPortTextBox.Text = ftp.Port.ToString();
            FtpUsernameTextBox.Text = ftp.Username;
            FtpRootTextBox.Text = ftp.RemoteRoot;
            FtpUseTlsCheckBox.IsChecked = ftp.UseTls;
            FtpAllowPlainCheckBox.IsChecked = ftp.AllowInsecureFtp;
            FtpPassiveCheckBox.IsChecked = ftp.PassiveMode;
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.NetworkServices ?? new NetworkServicesUiState();
            NetworkServicesTabControl.SelectedIndex = Math.Max(0, Math.Min(3, state.SelectedTabIndex));
            SetIfNotEmpty(MqttClientHostTextBox, state.MqttClientHost);
            SetIfNotEmpty(MqttClientPortTextBox, state.MqttClientPort);
            SetIfNotEmpty(HttpUrlTextBox, state.HttpRequestUrl);
            SetIfNotEmpty(WebSocketUrlTextBox, state.WebSocketUrl);
            SetIfNotEmpty(FtpRemotePathTextBox, state.FtpRemotePath);
        }

        // MQTT Broker

        private async Task SaveMqttConfigurationAsync()
        {
            var configuration = _ctx.NetworkServices.Configuration;
            ApplyMqttControls(configuration);
            EnsureNetworkConfigurationValid(configuration);
            var mqtt = configuration.MqttBroker;
            if (!string.IsNullOrEmpty(MqttBrokerPasswordBox.Password))
                _ctx.NetworkServices.SetSecret(mqtt.PasswordSecretName, MqttBrokerPasswordBox.Password);
            await _ctx.NetworkServices.SaveConfigurationAsync(configuration, CancellationToken.None);
            MqttBrokerPasswordBox.Clear();
            _ctx.DemoLogger.Info("MQTT Broker 配置已保存；密码保存在 Windows DPAPI 密钥库中。");
        }

        private async void SaveMqttButton_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveMqttConfigurationAsync(); }
            catch (Exception ex) { _ctx.HandleError("MQTT 配置保存失败。", ex, true); }
        }

        private void ValidateNetworkConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var configuration = _ctx.NetworkServices.Configuration;
                ApplyMqttControls(configuration);
                ApplyWebControls(configuration);
                ApplyFtpControls(configuration);
                EnsureNetworkConfigurationValid(configuration);
                _ctx.SetHeaderStatus("当前网络服务 JSON 校验通过。", ThemeBrush.Success);
            }
            catch (Exception ex) { _ctx.HandleError("网络服务 JSON 校验失败。", ex, true); }
        }

        private void RestoreNetworkTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = _jsonValidation.LoadTemplate(JsonConfigurationDocument.NetworkServices);
                var result = _jsonValidation.Validate(
                    JsonConfigurationDocument.NetworkServices,
                    json,
                    Path.GetDirectoryName(_ctx.NetworkServices.ConfigurationFilePath),
                    false);
                if (!result.IsValid) throw new InvalidOperationException(result.ToDisplayText());

                var configuration = JsonConvert.DeserializeObject<NetworkServicesConfiguration>(json);
                ApplyConfiguration(configuration);
                MqttBrokerPasswordBox.Clear();
                WebApiKeyPasswordBox.Clear();
                FtpPasswordBox.Clear();
                _ctx.SetHeaderStatus("已加载网络服务模板，保存后生效。", ThemeBrush.Warning);
            }
            catch (Exception ex) { _ctx.HandleError("加载网络服务模板失败。", ex, true); }
        }

        private void EnsureNetworkConfigurationValid(NetworkServicesConfiguration configuration)
        {
            var result = _jsonValidation.ValidateNetworkConfiguration(configuration);
            if (!result.IsValid) throw new InvalidOperationException(result.ToDisplayText());
        }

        private void ApplyMqttControls(NetworkServicesConfiguration configuration)
        {
            var mqtt = configuration.MqttBroker;
            mqtt.BindAddress = RequireText(MqttBindAddressTextBox.Text, "MQTT 监听地址");
            mqtt.Port = ParsePort(MqttPortTextBox.Text, "MQTT 端口");
            mqtt.UseTls = MqttUseTlsCheckBox.IsChecked == true;
            mqtt.TlsPort = ParsePort(MqttTlsPortTextBox.Text, "MQTT TLS 端口");
            mqtt.CertificateThumbprint = NullIfWhiteSpace(MqttCertificateTextBox.Text);
            mqtt.Username = RequireText(MqttUsernameTextBox.Text, "MQTT 用户名");
            mqtt.AutoStart = MqttAutoStartCheckBox.IsChecked == true;
        }

        private void ApplyWebControls(NetworkServicesConfiguration configuration)
        {
            var web = configuration.WebGateway;
            web.ListenPrefix = RequireText(WebListenPrefixTextBox.Text, "Web 监听前缀");
            web.EnableRemoteWrites = WebEnableWritesCheckBox.IsChecked == true;
            web.AllowRawAddressReads = WebAllowRawReadsCheckBox.IsChecked == true;
            web.ExposeRawAddresses = WebExposeAddressesCheckBox.IsChecked == true;
            web.AutoStart = WebAutoStartCheckBox.IsChecked == true;
            web.AllowedOrigins = SplitOrigins(WebOriginsTextBox.Text);
        }

        private void ApplyFtpControls(NetworkServicesConfiguration configuration)
        {
            var ftp = configuration.Ftp;
            ftp.Host = RequireText(FtpHostTextBox.Text, "FTP 服务器");
            ftp.Port = ParsePort(FtpPortTextBox.Text, "FTP 端口");
            ftp.Username = RequireText(FtpUsernameTextBox.Text, "FTP 用户名");
            ftp.RemoteRoot = RequireText(FtpRootTextBox.Text, "FTP 远端根目录");
            ftp.UseTls = FtpUseTlsCheckBox.IsChecked == true;
            ftp.AllowInsecureFtp = FtpAllowPlainCheckBox.IsChecked == true;
            ftp.PassiveMode = FtpPassiveCheckBox.IsChecked == true;
        }

        private async void StartMqttButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveMqttConfigurationAsync();
                if (_ctx.NetworkServices.IsMqttRunning)
                {
                    DetachBrokerEvents();
                    await _ctx.NetworkServices.StopMqttAsync(CancellationToken.None);
                }
                await _ctx.NetworkServices.StartMqttAsync(CancellationToken.None);
                AttachBrokerEvents();
                await RefreshMqttClientsAsync();
                UpdateServiceStatuses();
                _ctx.SetHeaderStatus("MQTT Broker 已启动", ThemeBrush.Success);
            }
            catch (Exception ex)
            {
                UpdateServiceStatuses();
                _ctx.HandleError("MQTT Broker 启动失败。", ex, true);
            }
        }

        private async void StopMqttButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DetachBrokerEvents();
                await _ctx.NetworkServices.StopMqttAsync(CancellationToken.None);
                MqttBrokerClientsDataGrid.ItemsSource = null;
                UpdateServiceStatuses();
                _ctx.SetHeaderStatus("MQTT Broker 已停止", ThemeBrush.Warning);
            }
            catch (Exception ex) { _ctx.HandleError("MQTT Broker 停止失败。", ex, false); }
        }

        private async void RefreshMqttClientsButton_Click(object sender, RoutedEventArgs e)
        {
            try { await RefreshMqttClientsAsync(); }
            catch (Exception ex) { _ctx.HandleError("MQTT 客户端列表刷新失败。", ex, false); }
        }

        private async Task RefreshMqttClientsAsync()
        {
            var broker = _ctx.NetworkServices.MqttBroker;
            MqttBrokerClientsDataGrid.ItemsSource = broker == null || !broker.IsRunning
                ? null
                : await broker.GetClientsAsync(CancellationToken.None);
        }

        private void AttachBrokerEvents()
        {
            var broker = _ctx.NetworkServices.MqttBroker;
            if (ReferenceEquals(_observedBroker, broker)) return;
            DetachBrokerEvents();
            _observedBroker = broker;
            if (broker == null) return;
            broker.MessageReceived += Broker_MessageReceived;
            broker.ClientConnected += Broker_ClientChanged;
            broker.ClientDisconnected += Broker_ClientChanged;
        }

        private void DetachBrokerEvents()
        {
            var broker = _observedBroker;
            _observedBroker = null;
            if (broker == null) return;
            broker.MessageReceived -= Broker_MessageReceived;
            broker.ClientConnected -= Broker_ClientChanged;
            broker.ClientDisconnected -= Broker_ClientChanged;
        }

        private void Broker_MessageReceived(object sender, MqttBrokerMessageReceivedEventArgs e)
        {
            var payload = Limit(Encoding.UTF8.GetString(e.Payload ?? new byte[0]), 2000);
            _ctx.RunOnUi(() => AppendLog(MqttBrokerLogTextBox,
                string.Format("{0:HH:mm:ss} RX {1} | {2}", e.ReceivedUtc.LocalDateTime, e.Topic, payload)));
        }

        private void Broker_ClientChanged(object sender, MqttBrokerClientEventArgs e)
        {
            _ctx.RunOnUi(async () =>
            {
                AppendLog(MqttBrokerLogTextBox,
                    string.Format("{0:HH:mm:ss} SESSION {1} | {2}", e.TimestampUtc.LocalDateTime,
                        e.Session == null ? "unknown" : e.Session.ClientId, e.Reason ?? "connected"));
                try { await RefreshMqttClientsAsync(); } catch { }
            });
        }

        // MQTT debug client

        private async void ConnectMqttClientButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetMqttClientAsync();
                var clientId = RequireText(MqttClientIdTextBox.Text, "MQTT Client ID");
                var password = MqttClientPasswordBox.Password;
                if (string.IsNullOrEmpty(password))
                {
                    var config = _ctx.NetworkServices.Configuration.MqttBroker;
                    _ctx.NetworkServices.TryGetSecret(config.PasswordSecretName, out password);
                }
                var client = new MqttClient(new MqttClientOptions
                {
                    DeviceId = clientId,
                    ClientId = clientId,
                    Host = RequireText(MqttClientHostTextBox.Text, "MQTT 主机"),
                    Port = ParsePort(MqttClientPortTextBox.Text, "MQTT 客户端端口"),
                    Username = NullIfWhiteSpace(MqttClientUsernameTextBox.Text),
                    Password = password,
                    UseTls = MqttClientTlsCheckBox.IsChecked == true,
                    QualityOfService = 1,
                    AutoReconnect = true,
                    CleanSession = false,
                }, _ctx.SdkLogger);
                client.MessageReceived += MqttClient_MessageReceived;
                client.ConnectionChanged += MqttClient_ConnectionChanged;
                _mqttClient = client;
                await client.ConnectAsync(CancellationToken.None);
                UpdateMqttClientStatus();
                AppendLog(MqttClientLogTextBox, DateTime.Now.ToString("HH:mm:ss") + " 已连接。");
            }
            catch (Exception ex)
            {
                await ResetMqttClientAsync();
                _ctx.HandleError("MQTT 调试客户端连接失败。", ex, true);
            }
        }

        private async void DisconnectMqttClientButton_Click(object sender, RoutedEventArgs e)
        {
            try { await ResetMqttClientAsync(); }
            catch (Exception ex) { _ctx.HandleError("MQTT 调试客户端断开失败。", ex, false); }
        }

        private async void SubscribeMqttButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var client = RequireMqttClient();
                var topic = RequireText(MqttSubscribeTopicTextBox.Text, "订阅 Topic");
                await client.SubscribeTopicAsync(topic, CancellationToken.None);
                AppendLog(MqttClientLogTextBox, DateTime.Now.ToString("HH:mm:ss") + " SUB " + topic);
            }
            catch (Exception ex) { _ctx.HandleError("MQTT 订阅失败。", ex, true); }
        }

        private async void PublishMqttButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var client = RequireMqttClient();
                var topic = RequireText(MqttPublishTopicTextBox.Text, "发布 Topic");
                await client.WriteAsync(new WriteRequest(client.DeviceId, topic, DataType.String,
                    MqttPayloadTextBox.Text ?? string.Empty), CancellationToken.None);
                AppendLog(MqttClientLogTextBox, DateTime.Now.ToString("HH:mm:ss") + " PUB " + topic);
            }
            catch (Exception ex) { _ctx.HandleError("MQTT 发布失败。", ex, true); }
        }

        private void MqttClient_MessageReceived(object sender, MqttMessageReceivedEventArgs e)
        {
            var payload = Limit(Encoding.UTF8.GetString(e.Payload ?? new byte[0]), 2000);
            _ctx.RunOnUi(() => AppendLog(MqttClientLogTextBox,
                string.Format("{0:HH:mm:ss} RX {1} | {2}", e.ReceivedUtc.LocalDateTime, e.Topic, payload)));
        }

        private void MqttClient_ConnectionChanged(object sender, MqttConnectionChangedEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                UpdateMqttClientStatus();
                AppendLog(MqttClientLogTextBox,
                    string.Format("{0:HH:mm:ss} {1} | {2}", e.TimestampUtc.LocalDateTime,
                        e.IsConnected ? "CONNECTED" : "DISCONNECTED", e.Reason));
            });
        }

        private MqttClient RequireMqttClient()
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
                throw new InvalidOperationException("请先连接 MQTT 调试客户端。");
            return _mqttClient;
        }

        private async Task ResetMqttClientAsync()
        {
            var client = _mqttClient;
            _mqttClient = null;
            if (client != null)
            {
                client.MessageReceived -= MqttClient_MessageReceived;
                client.ConnectionChanged -= MqttClient_ConnectionChanged;
                try { await client.DisconnectAsync(CancellationToken.None); } catch { }
                client.Dispose();
            }
            MqttClientPasswordBox.Clear();
            UpdateMqttClientStatus();
        }

        private void UpdateMqttClientStatus()
        {
            var connected = _mqttClient != null && _mqttClient.IsConnected;
            MqttClientStatusTextBlock.Text = connected ? "已连接" : "未连接";
            MqttClientStatusTextBlock.Foreground = connected ? ThemeBrush.Success : ThemeBrush.Danger;
        }

        // Web gateway and HTTP client

        private async Task SaveWebConfigurationAsync()
        {
            var configuration = _ctx.NetworkServices.Configuration;
            ApplyWebControls(configuration);
            EnsureNetworkConfigurationValid(configuration);
            var web = configuration.WebGateway;
            if (!string.IsNullOrEmpty(WebApiKeyPasswordBox.Password))
                _ctx.NetworkServices.SetSecret(web.ApiKeySecretName, WebApiKeyPasswordBox.Password);
            await _ctx.NetworkServices.SaveConfigurationAsync(configuration, CancellationToken.None);
            WebApiKeyPasswordBox.Clear();
            _ctx.DemoLogger.Info("Web 网关配置已保存；API Key 保存在 Windows DPAPI 密钥库中。");
        }

        private async void SaveWebButton_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveWebConfigurationAsync(); }
            catch (Exception ex) { _ctx.HandleError("Web 网关配置保存失败。", ex, true); }
        }

        private async void StartWebButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveWebConfigurationAsync();
                if (_ctx.NetworkServices.IsWebGatewayRunning)
                {
                    DetachWebGatewayEvents();
                    await _ctx.NetworkServices.StopWebGatewayAsync(CancellationToken.None);
                }
                await _ctx.NetworkServices.StartWebGatewayAsync(CancellationToken.None);
                AttachWebGatewayEvents();
                UpdateServiceStatuses();
                _ctx.SetHeaderStatus("WebAPI/WebSocket 网关已启动", ThemeBrush.Success);
            }
            catch (Exception ex)
            {
                AppendLog(HttpLogTextBox, "启动提示：如果 HTTP.sys 拒绝访问，请检查 URL ACL；HTTPS 还需检查证书绑定。");
                UpdateServiceStatuses();
                _ctx.HandleError("WebAPI/WebSocket 网关启动失败。", ex, true);
            }
        }

        private async void StopWebButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DetachWebGatewayEvents();
                await _ctx.NetworkServices.StopWebGatewayAsync(CancellationToken.None);
                UpdateServiceStatuses();
                WebSocketSessionsDataGrid.ItemsSource = null;
                _ctx.SetHeaderStatus("WebAPI/WebSocket 网关已停止", ThemeBrush.Warning);
            }
            catch (Exception ex) { _ctx.HandleError("Web 网关停止失败。", ex, false); }
        }

        private async void HealthWebButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var uri = BuildWebEndpoint(WebListenPrefixTextBox.Text, "api/v1/health");
                await SendHttpRequestAsync(HttpMethod.Get, uri, null, true);
            }
            catch (Exception ex) { _ctx.HandleError("Web 网关健康测试失败。", ex, true); }
        }

        private async void SendHttpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = HttpMethodComboBox.SelectedItem as ComboBoxItem;
                var methodName = item == null ? "GET" : Convert.ToString(item.Content);
                var method = new HttpMethod(methodName);
                var uri = new Uri(RequireText(HttpUrlTextBox.Text, "HTTP URL"), UriKind.Absolute);
                var hasBody = method != HttpMethod.Get && method != HttpMethod.Delete;
                await SendHttpRequestAsync(method, uri, hasBody ? HttpBodyTextBox.Text : null,
                    HttpUseSavedApiKeyCheckBox.IsChecked == true);
            }
            catch (Exception ex) { _ctx.HandleError("HTTP/HTTPS 请求失败。", ex, true); }
        }

        private async Task SendHttpRequestAsync(HttpMethod method, Uri uri, string body, bool useSavedApiKey)
        {
            if (_httpClient == null) throw new ObjectDisposedException(nameof(HttpApiClient));
            var request = body == null
                ? new HttpApiRequest(method, uri)
                : HttpApiRequest.Text(method, uri, body, "application/json; charset=utf-8");
            var secrets = new List<string>();
            if (useSavedApiKey)
            {
                string apiKey;
                var secretName = _ctx.NetworkServices.Configuration.WebGateway.ApiKeySecretName;
                if (!_ctx.NetworkServices.TryGetSecret(secretName, out apiKey) || string.IsNullOrEmpty(apiKey))
                    throw new InvalidOperationException("尚未保存 Web API Key。");
                request.Headers["X-Industrial-Api-Key"] = apiKey;
                secrets.Add(apiKey);
            }
            var response = await _httpClient.SendAsync(request, CancellationToken.None);
            var responseText = SecretRedactor.Redact(Limit(response.ReadText(), 12000), secrets);
            AppendLog(HttpLogTextBox, string.Format("{0:HH:mm:ss} {1} {2} -> {3} {4}\r\n{5}",
                DateTime.Now, method.Method, uri.GetLeftPart(UriPartial.Path), (int)response.StatusCode,
                response.ReasonPhrase, responseText));
        }

        private void AttachWebGatewayEvents()
        {
            var gateway = _ctx.NetworkServices.WebGateway;
            if (ReferenceEquals(_observedWebGateway, gateway)) return;
            DetachWebGatewayEvents();
            _observedWebGateway = gateway;
            if (gateway != null) gateway.RequestCompleted += WebGateway_RequestCompleted;
        }

        private void DetachWebGatewayEvents()
        {
            var gateway = _observedWebGateway;
            _observedWebGateway = null;
            if (gateway != null) gateway.RequestCompleted -= WebGateway_RequestCompleted;
        }

        private void WebGateway_RequestCompleted(object sender, IndustrialWebRequestEventArgs e)
        {
            _ctx.RunOnUi(() => AppendLog(HttpLogTextBox, string.Format(
                "{0:HH:mm:ss} IN {1} {2} -> {3} | {4}", e.TimestampUtc.LocalDateTime,
                e.Method, e.Path, e.StatusCode, e.RemoteEndpoint)));
        }

        // WebSocket debug client

        private async void ConnectWebSocketButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetWebSocketClientAsync();
                var apiKey = WebSocketApiKeyPasswordBox.Password;
                if (string.IsNullOrEmpty(apiKey))
                {
                    var secretName = _ctx.NetworkServices.Configuration.WebGateway.ApiKeySecretName;
                    _ctx.NetworkServices.TryGetSecret(secretName, out apiKey);
                }
                if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("请输入或先保存 Web API Key。");
                var client = new IndustrialWebSocketClient(new IndustrialWebSocketClientOptions
                {
                    Uri = new Uri(RequireText(WebSocketUrlTextBox.Text, "WebSocket URL"), UriKind.Absolute),
                    ApiKey = apiKey,
                    Origin = NullIfWhiteSpace(WebSocketOriginTextBox.Text),
                    AutoReconnect = true,
                });
                client.Connected += WebSocketClient_Connected;
                client.MessageReceived += WebSocketClient_MessageReceived;
                client.Closed += WebSocketClient_Closed;
                _webSocketClient = client;
                await client.ConnectAsync(CancellationToken.None);
                UpdateWebSocketClientStatus();
                AppendLog(WebSocketLogTextBox, DateTime.Now.ToString("HH:mm:ss") + " CONNECTED");
            }
            catch (Exception ex)
            {
                await ResetWebSocketClientAsync();
                _ctx.HandleError("WebSocket 客户端连接失败。", ex, true);
            }
        }

        private async void DisconnectWebSocketButton_Click(object sender, RoutedEventArgs e)
        {
            try { await ResetWebSocketClientAsync(); }
            catch (Exception ex) { _ctx.HandleError("WebSocket 客户端断开失败。", ex, false); }
        }

        private async void SendWebSocketButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var client = _webSocketClient;
                if (client == null || !client.IsConnected) throw new InvalidOperationException("请先连接 WebSocket 客户端。");
                await client.SendTextAsync(WebSocketMessageTextBox.Text ?? string.Empty, CancellationToken.None);
                AppendLog(WebSocketLogTextBox, DateTime.Now.ToString("HH:mm:ss") + " TX " + Limit(WebSocketMessageTextBox.Text, 2000));
            }
            catch (Exception ex) { _ctx.HandleError("WebSocket 消息发送失败。", ex, true); }
        }

        private void WebSocketClient_Connected(object sender, EventArgs e)
        {
            _ctx.RunOnUi(UpdateWebSocketClientStatus);
        }

        private void WebSocketClient_MessageReceived(object sender, WebSocketMessageEventArgs e)
        {
            var text = e.Text ?? Convert.ToBase64String(e.Payload ?? new byte[0]);
            _ctx.RunOnUi(() => AppendLog(WebSocketLogTextBox,
                DateTime.Now.ToString("HH:mm:ss") + " RX " + Limit(text, 8000)));
        }

        private void WebSocketClient_Closed(object sender, WebSocketClosedEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                UpdateWebSocketClientStatus();
                AppendLog(WebSocketLogTextBox, string.Format("{0:HH:mm:ss} CLOSED {1} | {2}",
                    DateTime.Now, e.CloseStatus, e.Description));
            });
        }

        private async Task ResetWebSocketClientAsync()
        {
            var client = _webSocketClient;
            _webSocketClient = null;
            if (client != null)
            {
                client.Connected -= WebSocketClient_Connected;
                client.MessageReceived -= WebSocketClient_MessageReceived;
                client.Closed -= WebSocketClient_Closed;
                try { await client.CloseAsync(CancellationToken.None); } catch { }
                client.Dispose();
            }
            WebSocketApiKeyPasswordBox.Clear();
            UpdateWebSocketClientStatus();
        }

        private void UpdateWebSocketClientStatus()
        {
            var connected = _webSocketClient != null && _webSocketClient.IsConnected;
            WebSocketClientStatusTextBlock.Text = connected ? "已连接" : "未连接";
            WebSocketClientStatusTextBlock.Foreground = connected ? ThemeBrush.Success : ThemeBrush.Danger;
        }

        private void RefreshWebSocketSessionsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshWebSocketSessions();
        }

        private void RefreshWebSocketSessions()
        {
            var gateway = _ctx.NetworkServices.WebGateway;
            WebSocketSessionsDataGrid.ItemsSource = gateway == null ? null : gateway.WebSocketSessions.ToList();
        }

        // FTP / FTPS client

        private async Task SaveFtpConfigurationAsync()
        {
            var configuration = _ctx.NetworkServices.Configuration;
            ApplyFtpControls(configuration);
            EnsureNetworkConfigurationValid(configuration);
            var ftp = configuration.Ftp;
            if (!string.IsNullOrEmpty(FtpPasswordBox.Password))
                _ctx.NetworkServices.SetSecret(ftp.PasswordSecretName, FtpPasswordBox.Password);
            await _ctx.NetworkServices.SaveConfigurationAsync(configuration, CancellationToken.None);
            FtpPasswordBox.Clear();
            _ctx.DemoLogger.Info("FTP/FTPS 配置已保存；密码保存在 Windows DPAPI 密钥库中。");
        }

        private async void SaveFtpButton_Click(object sender, RoutedEventArgs e)
        {
            try { await SaveFtpConfigurationAsync(); }
            catch (Exception ex) { _ctx.HandleError("FTP/FTPS 配置保存失败。", ex, true); }
        }

        private async void ConnectFtpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveFtpConfigurationAsync();
                if (_ctx.NetworkServices.IsFtpConnected)
                    await _ctx.NetworkServices.DisconnectFtpAsync(CancellationToken.None);
                await _ctx.NetworkServices.ConnectFtpAsync(CancellationToken.None);
                UpdateServiceStatuses();
                await RefreshFtpListAsync();
                _ctx.SetHeaderStatus("FTP/FTPS 已连接", ThemeBrush.Success);
            }
            catch (Exception ex)
            {
                UpdateServiceStatuses();
                _ctx.HandleError("FTP/FTPS 连接失败。", ex, true);
            }
        }

        private async void DisconnectFtpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CancelFtpTransfer();
                await _ctx.NetworkServices.DisconnectFtpAsync(CancellationToken.None);
                FtpItemsDataGrid.ItemsSource = null;
                UpdateServiceStatuses();
            }
            catch (Exception ex) { _ctx.HandleError("FTP/FTPS 断开失败。", ex, false); }
        }

        private async void ListFtpButton_Click(object sender, RoutedEventArgs e)
        {
            try { await RefreshFtpListAsync(); }
            catch (Exception ex) { _ctx.HandleError("FTP 目录刷新失败。", ex, true); }
        }

        private async Task RefreshFtpListAsync()
        {
            var client = RequireFtpClient();
            var items = await client.ListDirectoryAsync(RequireText(FtpRemotePathTextBox.Text, "FTP 远端路径"), CancellationToken.None);
            FtpItemsDataGrid.ItemsSource = items;
            FtpProgressTextBlock.Text = string.Format("目录中有 {0} 项；服务器加密={1}，断点续传={2}，校验={3}",
                items.Count, client.Capabilities.IsEncrypted, client.Capabilities.SupportsResume,
                client.Capabilities.SupportsChecksum);
        }

        private async void CreateFtpDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = RequireText(FtpTargetPathTextBox.Text, "要创建的远端目录");
                await RequireFtpClient().CreateDirectoryAsync(path, true, CancellationToken.None);
                await RefreshFtpListAsync();
            }
            catch (Exception ex) { _ctx.HandleError("FTP 创建目录失败。", ex, true); }
        }

        private void BrowseFtpLocalButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) FtpLocalPathTextBox.Text = dialog.FileName;
        }

        private async void UploadFtpButton_Click(object sender, RoutedEventArgs e)
        {
            var client = default(IFtpFileClient);
            CancellationTokenSource transfer = null;
            try
            {
                client = RequireFtpClient();
                var localPath = RequireText(FtpLocalPathTextBox.Text, "上传本地文件");
                if (!File.Exists(localPath)) throw new FileNotFoundException("上传文件不存在。", localPath);
                var remotePath = RequireText(FtpTargetPathTextBox.Text, "上传目标路径");
                transfer = BeginFtpTransfer();
                var result = await client.UploadFileAsync(localPath, remotePath, new FtpUploadOptions
                {
                    Resume = true,
                    Atomic = true,
                    Verify = true,
                    Overwrite = true,
                    CreateRemoteDirectory = true,
                }, CreateFtpProgress(), transfer.Token);
                FtpProgressTextBlock.Text = string.Format("上传完成：{0} bytes，校验={1} ({2})",
                    result.Bytes, result.WasVerified, result.VerificationMethod);
                await RefreshFtpListAsync();
            }
            catch (OperationCanceledException) { FtpProgressTextBlock.Text = "上传已取消。"; }
            catch (Exception ex) { _ctx.HandleError("FTP 上传失败。", ex, true); }
            finally { EndFtpTransfer(transfer); }
        }

        private async void DownloadFtpButton_Click(object sender, RoutedEventArgs e)
        {
            CancellationTokenSource transfer = null;
            try
            {
                var client = RequireFtpClient();
                var remotePath = GetSelectedRemotePathOrCurrent();
                var localPath = NullIfWhiteSpace(FtpLocalPathTextBox.Text);
                if (localPath == null)
                {
                    var dialog = new SaveFileDialog { FileName = Path.GetFileName(remotePath) };
                    if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
                    localPath = dialog.FileName;
                    FtpLocalPathTextBox.Text = localPath;
                }
                transfer = BeginFtpTransfer();
                var result = await client.DownloadFileAsync(remotePath, localPath, new FtpDownloadOptions
                {
                    Resume = true,
                    Verify = true,
                    Overwrite = true,
                    CreateLocalDirectory = true,
                }, CreateFtpProgress(), transfer.Token);
                FtpProgressTextBlock.Text = string.Format("下载完成：{0} bytes，校验={1} ({2})",
                    result.Bytes, result.WasVerified, result.VerificationMethod);
            }
            catch (OperationCanceledException) { FtpProgressTextBlock.Text = "下载已取消。"; }
            catch (Exception ex) { _ctx.HandleError("FTP 下载失败。", ex, true); }
            finally { EndFtpTransfer(transfer); }
        }

        private async void DeleteFtpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = GetSelectedRemotePathOrCurrent();
                if (MessageBox.Show(Window.GetWindow(this), "确定删除远端文件？\n" + path, "FTP 删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                await RequireFtpClient().DeleteFileAsync(path, CancellationToken.None);
                await RefreshFtpListAsync();
            }
            catch (Exception ex) { _ctx.HandleError("FTP 删除文件失败。", ex, true); }
        }

        private async void RenameFtpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var source = GetSelectedRemotePathOrCurrent();
                var destination = RequireText(FtpTargetPathTextBox.Text, "改名后的目标路径");
                await RequireFtpClient().RenameAsync(source, destination, false, CancellationToken.None);
                await RefreshFtpListAsync();
            }
            catch (Exception ex) { _ctx.HandleError("FTP 改名或移动失败。", ex, true); }
        }

        private void CancelFtpTransferButton_Click(object sender, RoutedEventArgs e)
        {
            CancelFtpTransfer();
        }

        private void FtpItemsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = FtpItemsDataGrid.SelectedItem as FtpDirectoryItem;
            if (item != null) FtpProgressTextBlock.Text = item.RemotePath;
        }

        private IFtpFileClient RequireFtpClient()
        {
            var client = _ctx.NetworkServices.FtpClient;
            if (client == null || !client.IsConnected) throw new InvalidOperationException("请先连接 FTP/FTPS 服务器。");
            return client;
        }

        private string GetSelectedRemotePathOrCurrent()
        {
            var selected = FtpItemsDataGrid.SelectedItem as FtpDirectoryItem;
            return selected == null
                ? RequireText(FtpRemotePathTextBox.Text, "FTP 远端路径")
                : selected.RemotePath;
        }

        private CancellationTokenSource BeginFtpTransfer()
        {
            CancelFtpTransfer();
            var source = new CancellationTokenSource();
            _ftpTransferCancellation = source;
            FtpProgressBar.Value = 0;
            FtpProgressTextBlock.Text = "正在传输…";
            return source;
        }

        private IProgress<FtpTransferProgress> CreateFtpProgress()
        {
            return new Progress<FtpTransferProgress>(progress => _ctx.RunOnUi(() =>
            {
                FtpProgressBar.Value = Math.Max(0, Math.Min(100, progress.Percentage));
                FtpProgressTextBlock.Text = string.Format("{0:F1}%  {1}/{2} bytes  {3:F0} B/s",
                    progress.Percentage, progress.TransferredBytes, progress.TotalBytes, progress.BytesPerSecond);
            }));
        }

        private void EndFtpTransfer(CancellationTokenSource source)
        {
            if (source == null) return;
            if (ReferenceEquals(_ftpTransferCancellation, source)) _ftpTransferCancellation = null;
            source.Dispose();
        }

        private void CancelFtpTransfer()
        {
            var source = _ftpTransferCancellation;
            _ftpTransferCancellation = null;
            if (source == null) return;
            try { source.Cancel(); } catch { }
        }

        // Shared status and helpers

        private void NetworkServices_StateChanged(object sender, NetworkServiceStateChangedEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                if (_reset) return;
                UpdateServiceStatuses();
                if (e.Service == NetworkServiceKind.MqttBroker && e.State == NetworkServiceState.Running)
                    AttachBrokerEvents();
                else if (e.Service == NetworkServiceKind.MqttBroker &&
                         (e.State == NetworkServiceState.Stopped || e.State == NetworkServiceState.Faulted))
                {
                    DetachBrokerEvents();
                    MqttBrokerClientsDataGrid.ItemsSource = null;
                }
                if (e.Service == NetworkServiceKind.WebGateway && e.State == NetworkServiceState.Running)
                    AttachWebGatewayEvents();
                else if (e.Service == NetworkServiceKind.WebGateway &&
                         (e.State == NetworkServiceState.Stopped || e.State == NetworkServiceState.Faulted))
                {
                    DetachWebGatewayEvents();
                    WebSocketSessionsDataGrid.ItemsSource = null;
                }
                if (e.Service == NetworkServiceKind.WebGateway) RefreshWebSocketSessions();
            });
        }

        private void UpdateServiceStatuses()
        {
            var mqtt = _ctx.NetworkServices.IsMqttRunning;
            MqttBrokerStatusTextBlock.Text = mqtt ? "运行中" : "已停止";
            MqttBrokerStatusTextBlock.Foreground = mqtt ? ThemeBrush.Success : ThemeBrush.Danger;

            var web = _ctx.NetworkServices.IsWebGatewayRunning;
            WebGatewayStatusTextBlock.Text = web ? "网关运行中" : "网关已停止";
            WebGatewayStatusTextBlock.Foreground = web ? ThemeBrush.Success : ThemeBrush.Danger;

            var ftp = _ctx.NetworkServices.IsFtpConnected;
            FtpStatusTextBlock.Text = ftp ? "已连接" : "未连接";
            FtpStatusTextBlock.Foreground = ftp ? ThemeBrush.Success : ThemeBrush.Danger;
        }

        private static Uri BuildWebEndpoint(string listenPrefix, string relativePath)
        {
            return new Uri(new Uri(RequireText(listenPrefix, "Web 监听前缀"), UriKind.Absolute), relativePath);
        }

        private static Uri BuildWebSocketEndpoint(string listenPrefix)
        {
            var http = BuildWebEndpoint(listenPrefix, "ws/v1/tags");
            var builder = new UriBuilder(http) { Scheme = http.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" };
            return builder.Uri;
        }

        private static List<string> SplitOrigins(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int ParsePort(string text, string name)
        {
            int value;
            if (!int.TryParse(text, out value) || value < 1 || value > 65535)
                throw new ArgumentOutOfRangeException(name, "端口必须在 1 到 65535 之间。");
            return value;
        }

        private static string RequireText(string text, string name)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(name + "不能为空。", name);
            return text.Trim();
        }

        private static string NullIfWhiteSpace(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static void SetIfNotEmpty(TextBox textBox, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) textBox.Text = value;
        }

        private static string Limit(string text, int length)
        {
            text = text ?? string.Empty;
            return text.Length <= length ? text : text.Substring(0, length) + "…";
        }

        private static void AppendLog(TextBox textBox, string message)
        {
            if (textBox.Text.Length > 60000) textBox.Clear();
            textBox.AppendText((message ?? string.Empty) + Environment.NewLine);
            textBox.ScrollToEnd();
        }
    }
}
