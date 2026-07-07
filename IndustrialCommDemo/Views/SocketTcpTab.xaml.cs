using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommDemo.SocketDebug;

namespace IndustrialCommDemo.Views
{
    public partial class SocketTcpTab : UserControl
    {
        private DemoAppContext _ctx;
        private LineBasedTcpServer _server;
        private LineBasedTcpClient _client;

        public SocketTcpTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            ApplySavedState();
        }

        public async Task ResetAllAsync()
        {
            await ResetClientAsync();
            await ResetServerAsync();
        }

        public async Task ResetServerAsync()
        {
            if (_server == null) { UpdateServerStatus(); return; }
            var server = _server;
            _server = null;
            server.ClientConnected -= Server_ClientConnected;
            server.ClientDisconnected -= Server_ClientDisconnected;
            server.MessageReceived -= Server_MessageReceived;
            try { await server.StopAsync(CancellationToken.None); } catch { }
            server.Dispose();
            UpdateServerStatus();
            ServerSessionsTextBlock.Text = "0";
        }

        public async Task ResetClientAsync()
        {
            if (_client == null) { UpdateClientStatus(); return; }
            var client = _client;
            _client = null;
            client.Connected -= Client_Connected;
            client.Disconnected -= Client_Disconnected;
            client.MessageReceived -= Client_MessageReceived;
            try { await client.DisconnectAsync(CancellationToken.None); } catch { }
            client.Dispose();
            UpdateClientStatus();
        }

        private void UpdateServerStatus()
        {
            var isRunning = _server != null && _server.IsRunning;
            ServerStatusTextBlock.Text = isRunning ? "运行中" : "已停止";
            ServerStatusTextBlock.Foreground = isRunning ? Brushes.ForestGreen : Brushes.IndianRed;
        }

        private void UpdateClientStatus()
        {
            var isConnected = _client != null && _client.IsConnected;
            ClientStatusTextBlock.Text = isConnected ? "已连接" : "未连接";
            ClientStatusTextBlock.Foreground = isConnected ? Brushes.ForestGreen : Brushes.IndianRed;
            if (!isConnected) ClientLastReceivedTextBlock.Text = "（无）";
        }

        private async void StartServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetServerAsync();
                _server = new LineBasedTcpServer();
                _server.ClientConnected += Server_ClientConnected;
                _server.ClientDisconnected += Server_ClientDisconnected;
                _server.MessageReceived += Server_MessageReceived;
                var listenAddress = ParseHelper.ParseListenAddress(SocketServerIpTextBox.Text);
                var port = ParseHelper.ParseIntValue(SocketServerPortTextBox.Text, "Socket 服务端端口");
                await _server.StartAsync(listenAddress, port, CancellationToken.None);
                UpdateServerStatus();
                _ctx.SetHeaderStatus("Socket 服务端已启动", Brushes.LightGreen);
                _ctx.DemoLogger.Info(string.Format("Socket 服务端已在 {0}:{1} 启动。", listenAddress, port));
            }
            catch (Exception ex)
            {
                UpdateServerStatus();
                _ctx.HandleError("Socket 服务端启动失败。", ex, true);
            }
        }

        private async void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            try { await ResetServerAsync(); _ctx.SetHeaderStatus("Socket 服务端已停止", Brushes.Khaki); _ctx.DemoLogger.Info("Socket 服务端已停止。"); }
            catch (Exception ex) { _ctx.HandleError("Socket 服务端停止失败。", ex, false); }
        }

        private async void ConnectClientButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();
                _client = new LineBasedTcpClient();
                _client.Connected += Client_Connected;
                _client.Disconnected += Client_Disconnected;
                _client.MessageReceived += Client_MessageReceived;
                var host = ParseHelper.RequireText(SocketClientHostTextBox.Text, "Socket 服务端主机");
                var port = ParseHelper.ParseIntValue(SocketClientPortTextBox.Text, "Socket 服务端端口");
                await _client.ConnectAsync(host, port, CancellationToken.None);
                UpdateClientStatus();
                _ctx.SetHeaderStatus("Socket 客户端已连接", Brushes.LightGreen);
                _ctx.DemoLogger.Info(string.Format("Socket 客户端已连接到 {0}:{1}。", host, port));
            }
            catch (Exception ex)
            {
                UpdateClientStatus();
                _ctx.HandleError("Socket 客户端连接失败。", ex, true);
            }
        }

        private async void DisconnectClientButton_Click(object sender, RoutedEventArgs e)
        {
            try { await ResetClientAsync(); _ctx.SetHeaderStatus("Socket 客户端已断开", Brushes.Khaki); _ctx.DemoLogger.Info("Socket 客户端已断开。"); }
            catch (Exception ex) { _ctx.HandleError("Socket 客户端断开失败。", ex, false); }
        }

        private async void SendFromServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_server == null || !_server.IsRunning) { MessageBox.Show("请先启动 Socket 服务端。", "Socket", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                await _server.BroadcastAsync(SocketServerMessageTextBox.Text ?? string.Empty, CancellationToken.None);
                _ctx.SetHeaderStatus("Socket 服务端广播已发送", Brushes.LightGreen);
                _ctx.DemoLogger.Info("Socket 服务端广播已发送。");
            }
            catch (Exception ex) { _ctx.HandleError("Socket 服务端发送失败。", ex, true); }
        }

        private async void SendFromClientButton_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected) { MessageBox.Show("请先连接 Socket 客户端。", "Socket", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                await _client.SendAsync(SocketClientMessageTextBox.Text ?? string.Empty, CancellationToken.None);
                _ctx.SetHeaderStatus("Socket 客户端消息已发送", Brushes.LightGreen);
                _ctx.DemoLogger.Info("Socket 客户端消息已发送。");
            }
            catch (Exception ex) { _ctx.HandleError("Socket 客户端发送失败。", ex, true); }
        }

        private void Server_ClientConnected(object sender, SocketSessionEventArgs e)
        {
            _ctx.RunOnUi(() => { UpdateServerStatus(); ServerSessionsTextBlock.Text = (e.SessionCount).ToString(CultureInfo.InvariantCulture); _ctx.SetHeaderStatus("Socket 服务端接受了一个客户端", Brushes.LightGreen); _ctx.DemoLogger.Info(string.Format("Socket 服务端客户端已连接：{0}。", e.RemoteEndPoint)); });
        }

        private void Server_ClientDisconnected(object sender, SocketSessionEventArgs e)
        {
            _ctx.RunOnUi(() => { UpdateServerStatus(); ServerSessionsTextBlock.Text = (e.SessionCount).ToString(CultureInfo.InvariantCulture); _ctx.SetHeaderStatus("客户端已从服务端断开", Brushes.Khaki); _ctx.DemoLogger.Info(string.Format("Socket 服务端客户端已断开：{0}。", e.RemoteEndPoint)); });
        }

        private async void Server_MessageReceived(object sender, SocketTextMessageEventArgs e)
        {
            var shouldEcho = _ctx.Dispatcher.CheckAccess()
                ? EchoCheckBox.IsChecked == true
                : (bool)_ctx.Dispatcher.Invoke(() => EchoCheckBox.IsChecked == true);

            _ctx.RunOnUi(() => { _ctx.SetHeaderStatus("Socket 服务端收到数据", Brushes.LightGreen); _ctx.DemoLogger.Info(string.Format("Socket 服务端收到来自 {0} 的数据：{1}", e.RemoteEndPoint, e.Message)); });

            if (shouldEcho && _server != null)
            {
                try { await _server.SendToAsync(e.SessionId, "echo: " + e.Message, CancellationToken.None); }
                catch (Exception ex) { _ctx.RunOnUi(() => _ctx.HandleError("Socket 服务端回显失败。", ex, false)); }
            }
        }

        private void Client_Connected(object sender, EventArgs e) { _ctx.RunOnUi(UpdateClientStatus); }

        private void Client_Disconnected(object sender, EventArgs e) { _ctx.RunOnUi(() => { UpdateClientStatus(); _ctx.SetHeaderStatus("Socket 客户端已断开", Brushes.Khaki); }); }

        private void Client_MessageReceived(object sender, SocketTextMessageEventArgs e)
        {
            _ctx.RunOnUi(() => { ClientLastReceivedTextBlock.Text = e.Message; _ctx.SetHeaderStatus("Socket 客户端收到数据", Brushes.LightGreen); _ctx.DemoLogger.Info(string.Format("Socket 客户端收到：{0}", e.Message)); });
        }

        // ── State ──

        public void SaveState()
        {
            _ctx.UiState.Socket.ServerIp = SocketServerIpTextBox.Text;
            _ctx.UiState.Socket.ServerPort = SocketServerPortTextBox.Text;
            _ctx.UiState.Socket.ClientHost = SocketClientHostTextBox.Text;
            _ctx.UiState.Socket.ClientPort = SocketClientPortTextBox.Text;
            _ctx.UiState.Socket.EchoEnabled = EchoCheckBox.IsChecked ?? true;
            _ctx.UiState.Socket.ServerMessage = SocketServerMessageTextBox.Text;
            _ctx.UiState.Socket.ClientMessage = SocketClientMessageTextBox.Text;
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Socket ?? new Services.SocketUiState();
            ComboHelper.SetIfNotEmpty(SocketServerIpTextBox, state.ServerIp);
            ComboHelper.SetIfNotEmpty(SocketServerPortTextBox, state.ServerPort);
            ComboHelper.SetIfNotEmpty(SocketClientHostTextBox, state.ClientHost);
            ComboHelper.SetIfNotEmpty(SocketClientPortTextBox, state.ClientPort);
            ComboHelper.SetIfNotEmpty(SocketServerMessageTextBox, state.ServerMessage);
            ComboHelper.SetIfNotEmpty(SocketClientMessageTextBox, state.ClientMessage);
            EchoCheckBox.IsChecked = state.EchoEnabled;
        }
    }
}
