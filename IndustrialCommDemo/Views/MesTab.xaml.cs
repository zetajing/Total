using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommDemo.Services;
using IndustrialCommSdk.Mes;

namespace IndustrialCommDemo.Views
{
    /// <summary>使用可编辑 JSON 配置和原始 JSON 报文演示 MES HTTP API。</summary>
    public partial class MesTab : UserControl
    {
        private DemoAppContext _ctx;
        private IMesHttpClient _httpClient;
        private string _endpoint;
        private bool _initialized;

        public MesTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            ApplySavedState();
            UpdateClientState(false, "配置未应用");
        }

        private void ConfigJsonTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized || _httpClient == null) return;
            try { _httpClient.Dispose(); } catch { }
            _httpClient = null;
            UpdateClientState(false, "配置已修改，请重新应用");
        }

        private void FormatConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MesDemoJson.ParseConfiguration(MesConfigJsonTextBox.Text);
                MesConfigJsonTextBox.Text = MesDemoJson.FormatObject(MesConfigJsonTextBox.Text);
                _ctx.SetHeaderStatus("MES HTTP 配置 JSON 校验通过", Brushes.LightGreen);
            }
            catch (Exception ex) { _ctx.HandleError("MES HTTP 配置 JSON 无效。", ex, true); }
        }

        private async void ApplyConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var options = MesDemoJson.ParseConfiguration(MesConfigJsonTextBox.Text);
                var endpoint = MesDemoJson.ParseEndpoint(MesConfigJsonTextBox.Text);
                await ResetClientAsync();
                _httpClient = new MesHttpClient(options, _ctx.SdkLogger);
                _endpoint = endpoint;
                UpdateClientState(true, "HTTP JSON 配置已应用");
                _ctx.SetHeaderStatus("MES HTTP JSON 配置已应用（未发送请求）", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                await ResetClientAsync();
                UpdateClientState(false, "配置应用失败");
                _ctx.HandleError("MES HTTP 配置应用失败。", ex, true);
            }
        }

        private async void ResetConfigButton_Click(object sender, RoutedEventArgs e)
        {
            await ResetClientAsync();
            MesConfigJsonTextBox.Text = MesDemoJson.CreateDefaultConfiguration();
            UpdateClientState(false, "已恢复默认，等待应用");
            _ctx.SetHeaderStatus("MES HTTP 配置已恢复默认，尚未应用", Brushes.Khaki);
        }

        private void FormatPayloadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MesPayloadJsonTextBox.Text = MesDemoJson.FormatObject(MesPayloadJsonTextBox.Text);
                _ctx.SetHeaderStatus("MES 上传报文 JSON 校验通过", Brushes.LightGreen);
            }
            catch (Exception ex) { _ctx.HandleError("MES 上传报文 JSON 无效。", ex, true); }
        }

        private async void SendJsonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_httpClient == null) throw new InvalidOperationException("请先应用 MES HTTP 配置。");

                MesResponseEndpointTextBlock.Text = _endpoint;
                MesHttpStatusTextBlock.Text = "请求中...";
                MesContentTypeTextBlock.Text = "（等待响应）";
                MesReasonPhraseTextBlock.Text = "（等待响应）";
                MesResponseBodyTextBox.Clear();
                MesSendButton.IsEnabled = false;

                var response = await _httpClient.SendJsonAsync(
                    _endpoint,
                    MesPayloadJsonTextBox.Text,
                    CancellationToken.None);

                MesHttpStatusTextBlock.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ({1})",
                    response.StatusCode,
                    response.IsSuccessStatusCode ? "成功" : "失败");
                MesHttpStatusTextBlock.Foreground = response.IsSuccessStatusCode
                    ? Brushes.ForestGreen
                    : Brushes.IndianRed;
                MesContentTypeTextBlock.Text = response.ContentType ?? "（未提供）";
                MesReasonPhraseTextBlock.Text = response.ReasonPhrase ?? "（未提供）";
                MesResponseBodyTextBox.Text = response.Body ?? string.Empty;
                _ctx.SetHeaderStatus(
                    "MES HTTP JSON 已发送: " + response.StatusCode,
                    response.IsSuccessStatusCode ? Brushes.LightGreen : Brushes.Khaki);
            }
            catch (Exception ex)
            {
                MesHttpStatusTextBlock.Text = "请求失败";
                MesHttpStatusTextBlock.Foreground = Brushes.IndianRed;
                _ctx.HandleError("MES HTTP JSON 发送失败。", ex, true);
            }
            finally
            {
                MesSendButton.IsEnabled = _httpClient != null;
            }
        }

        public Task ResetClientAsync()
        {
            var client = _httpClient;
            _httpClient = null;
            _endpoint = null;
            if (client != null)
            {
                try { client.Dispose(); } catch { }
            }
            if (_initialized) UpdateClientState(false, "配置未应用");
            return Task.CompletedTask;
        }

        public void SaveState()
        {
            if (!_initialized) return;
            var state = _ctx.UiState.Mes ?? (_ctx.UiState.Mes = new MesUiState());
            state.ConfigJson = MesConfigJsonTextBox.Text;
            state.RequestJson = MesPayloadJsonTextBox.Text;
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Mes ?? new MesUiState();
            MesConfigJsonTextBox.Text = string.IsNullOrWhiteSpace(state.ConfigJson)
                ? MesDemoJson.CreateDefaultConfiguration()
                : state.ConfigJson;
            MesPayloadJsonTextBox.Text = string.IsNullOrWhiteSpace(state.RequestJson)
                ? MesDemoJson.CreateDefaultRequest()
                : state.RequestJson;
            _initialized = true;
        }

        private void UpdateClientState(bool ready, string text)
        {
            MesStatusTextBlock.Text = text;
            MesStatusTextBlock.Foreground = ready ? Brushes.ForestGreen : Brushes.IndianRed;
            MesSendButton.IsEnabled = ready;
        }

    }
}
