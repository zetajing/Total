using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.OpcUa;
using IndustrialCommSdk.Runtime;

namespace IndustrialCommDemo.Views
{
    /// <summary>演示 OPC UA Endpoint 连接、NodeId 读写和数据类型转换。</summary>
    public partial class OpcUaTab : UserControl
    {
        private DemoAppContext _ctx;
        private OpcUaClient _client;

        public OpcUaTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            ApplySavedState();
            RefreshCapabilityText();
            RefreshAddressHistory();
        }

        public async Task ResetClientAsync()
        {
            var client = _client;
            _client = null;
            if (client == null)
            {
                UpdateStatus(false, "未连接");
                RefreshCapabilityText();
                return;
            }

            try { await client.DisconnectAsync(CancellationToken.None); }
            catch { }
            finally
            {
                client.Dispose();
                UpdateStatus(false, "未连接");
                RefreshCapabilityText();
            }
        }

        public void SaveState()
        {
            if (_ctx == null) return;
            var state = _ctx.UiState.OpcUa;
            state.DeviceId = DeviceIdTextBox.Text;
            state.EndpointUrl = EndpointUrlTextBox.Text;
            state.Username = UsernameTextBox.Text;
            state.UseSecurity = UseSecurityCheckBox.IsChecked == true;
            state.AutoAcceptUntrustedCertificates = AutoAcceptCertificatesCheckBox.IsChecked == true;
            state.ConnectTimeout = ConnectTimeoutTextBox.Text;
            state.OperationTimeout = OperationTimeoutTextBox.Text;
            state.SessionTimeout = SessionTimeoutTextBox.Text;
            state.Address = NodeIdTextBox.Text;
            state.DataType = (DataTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            state.Length = LengthTextBox.Text;
            state.WriteValue = WriteValueTextBox.Text;
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.OpcUa;
            if (state == null) return;
            if (!string.IsNullOrWhiteSpace(state.DeviceId)) DeviceIdTextBox.Text = state.DeviceId;
            if (!string.IsNullOrWhiteSpace(state.EndpointUrl)) EndpointUrlTextBox.Text = state.EndpointUrl;
            if (!string.IsNullOrWhiteSpace(state.Username)) UsernameTextBox.Text = state.Username;
            UseSecurityCheckBox.IsChecked = state.UseSecurity;
            AutoAcceptCertificatesCheckBox.IsChecked = state.AutoAcceptUntrustedCertificates;
            if (!string.IsNullOrWhiteSpace(state.ConnectTimeout)) ConnectTimeoutTextBox.Text = state.ConnectTimeout;
            if (!string.IsNullOrWhiteSpace(state.OperationTimeout)) OperationTimeoutTextBox.Text = state.OperationTimeout;
            if (!string.IsNullOrWhiteSpace(state.SessionTimeout)) SessionTimeoutTextBox.Text = state.SessionTimeout;
            if (!string.IsNullOrWhiteSpace(state.Address)) NodeIdTextBox.Text = state.Address;
            if (!string.IsNullOrWhiteSpace(state.Length)) LengthTextBox.Text = state.Length;
            if (!string.IsNullOrWhiteSpace(state.WriteValue)) WriteValueTextBox.Text = state.WriteValue;
            if (Enum.TryParse(state.DataType, out DataType dataType))
                ComboHelper.SelectDataType(DataTypeComboBox, dataType);
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();
                var options = new OpcUaClientOptions
                {
                    DeviceId = ParseHelper.RequireText(DeviceIdTextBox.Text, "OPC UA 设备 ID"),
                    EndpointUrl = ParseHelper.RequireText(EndpointUrlTextBox.Text, "OPC UA Endpoint URL"),
                    Username = string.IsNullOrWhiteSpace(UsernameTextBox.Text) ? null : UsernameTextBox.Text.Trim(),
                    Password = PasswordBox.Password,
                    UseSecurity = UseSecurityCheckBox.IsChecked == true,
                    AutoAcceptUntrustedCertificates = AutoAcceptCertificatesCheckBox.IsChecked == true,
                    ConnectTimeoutMilliseconds = ParsePositiveInt(ConnectTimeoutTextBox.Text, "OPC UA 连接超时"),
                    OperationTimeoutMilliseconds = ParsePositiveInt(OperationTimeoutTextBox.Text, "OPC UA 操作超时"),
                    SessionTimeoutMilliseconds = ParsePositiveInt(SessionTimeoutTextBox.Text, "OPC UA 会话超时"),
                };

                var client = new OpcUaClient(options, _ctx.SdkLogger);
                _client = client;
                await client.ConnectAsync(CancellationToken.None);
                UpdateStatus(true, "已连接");
                RefreshCapabilityText();
                _ctx.SetHeaderStatus("OPC UA 已连接", Brushes.LightGreen);
                _ctx.DemoLogger.Info("OPC UA 已连接到 " + options.EndpointUrl + "。");
            }
            catch (Exception ex)
            {
                await ResetClientAsync();
                UpdateStatus(false, "连接失败");
                _ctx.HandleError("OPC UA 连接失败。", ex, true);
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();
                ResultTextBlock.Text = "已断开。";
                _ctx.SetHeaderStatus("OPC UA 已断开", Brushes.Khaki);
                _ctx.DemoLogger.Info("OPC UA 已断开。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("OPC UA 断开失败。", ex, false);
            }
        }

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                var request = new ReadRequest(
                    ParseHelper.RequireText(DeviceIdTextBox.Text, "OPC UA 设备 ID"),
                    ParseHelper.RequireText(NodeIdTextBox.Text, "OPC UA NodeId"),
                    ComboHelper.GetSelectedDataType(DataTypeComboBox),
                    ParseHelper.ParseUShortValue(LengthTextBox.Text, "OPC UA 长度"));
                var result = await _client.ReadAsync(request, CancellationToken.None);
                ResultTextBlock.Text = FormatHelper.FormatDataValue(result);
                RememberCurrentAddress(request.Address);
                _ctx.QueueDatabaseValues(_client, new[] { result });
                _ctx.SetHeaderStatus("OPC UA 读取完成", Brushes.LightGreen);
                _ctx.DemoLogger.Info("OPC UA 读取完成：" + request.Address + "。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("OPC UA 读取失败。", ex, true);
            }
        }

        private async void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                var dataType = ComboHelper.GetSelectedDataType(DataTypeComboBox);
                var length = ParseHelper.ParseUShortValue(LengthTextBox.Text, "OPC UA 长度");
                var request = new WriteRequest(
                    ParseHelper.RequireText(DeviceIdTextBox.Text, "OPC UA 设备 ID"),
                    ParseHelper.RequireText(NodeIdTextBox.Text, "OPC UA NodeId"),
                    dataType,
                    ParseHelper.ParseValue(WriteValueTextBox.Text, dataType, length),
                    length);
                await _client.WriteAsync(request, CancellationToken.None);
                RememberCurrentAddress(request.Address);
                ResultTextBlock.Text = string.Format("写入成功：{0} = {1}", request.Address, FormatHelper.FormatDisplayValue(request.Value));
                _ctx.SetHeaderStatus("OPC UA 写入完成", Brushes.LightGreen);
                _ctx.DemoLogger.Info("OPC UA 写入完成：" + request.Address + "。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("OPC UA 写入失败。", ex, true);
            }
        }

        private void NodeIdHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboHelper.ApplyHistorySelection(NodeIdHistoryComboBox, NodeIdTextBox);
        }

        private bool EnsureConnected()
        {
            if (_client != null && _client.IsConnected) return true;
            _ctx.HandleError("OPC UA 尚未连接。", new InvalidOperationException("请先连接 OPC UA Endpoint。"), true);
            return false;
        }

        private void RememberCurrentAddress(string address)
        {
            AddressHistoryHelper.RememberRecentAddress(_ctx.UiState.OpcUa.RecentAddresses, address);
            RefreshAddressHistory();
        }

        private void RefreshAddressHistory()
        {
            ComboHelper.RefreshAddressHistory(NodeIdHistoryComboBox, _ctx.UiState.OpcUa.RecentAddresses);
        }

        private void RefreshCapabilityText()
        {
            var capabilities = _client == null
                ? ProtocolCapabilities.ForProtocol(ProtocolKind.OpcUa)
                : IndustrialClientPlatformExtensions.GetCapabilities(_client);
            CapabilityTextBlock.Text = CapabilityDisplayHelper.Format(capabilities);
        }

        private void UpdateStatus(bool connected, string text)
        {
            StatusTextBlock.Text = text;
            StatusTextBlock.Foreground = connected ? Brushes.ForestGreen : Brushes.IndianRed;
        }

        private static int ParsePositiveInt(string text, string fieldName)
        {
            var value = ParseHelper.ParseIntValue(text, fieldName);
            if (value <= 0) throw new InvalidOperationException(fieldName + " 必须大于 0。");
            return value;
        }
    }
}
