using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommDemo.ViewModels
{
    /// <summary>
    /// Base ViewModel for protocol tabs (S7, MC, etc.).
    /// Encapsulates the common Connect/Disconnect/Read/Write lifecycle
    /// with automatic double-click prevention and status/result binding.
    /// </summary>
    internal abstract class ProtocolTabViewModel : ViewModelBase
    {
        private IIndustrialClient _client;
        private string _statusText = "未连接";
        private Brush _statusBrush = Brushes.IndianRed;
        private string _resultText = "等待中...";
        private string _capabilityText = "协议能力：连接后显示。";
        private string _deviceId;
        private string _host;
        private string _portOrRack;
        private string _slotOrLength;
        private string _address;
        private string _length = "1";
        private string _writeValue;
        private DataType _selectedDataType = DataType.Int16;
        private bool _isBusy;

        // ── Bindable properties ──

        public string DeviceId { get => _deviceId; set => SetProperty(ref _deviceId, value); }
        public string Host { get => _host; set => SetProperty(ref _host, value); }
        public string PortOrRack { get => _portOrRack; set => SetProperty(ref _portOrRack, value); }
        public string SlotOrLength { get => _slotOrLength; set => SetProperty(ref _slotOrLength, value); }
        public string Address { get => _address; set => SetProperty(ref _address, value); }
        public string Length { get => _length; set => SetProperty(ref _length, value); }
        public string WriteValue { get => _writeValue; set => SetProperty(ref _writeValue, value); }
        public DataType SelectedDataType { get => _selectedDataType; set => SetProperty(ref _selectedDataType, value); }

        public string StatusText
        {
            get => _statusText;
            protected set => SetProperty(ref _statusText, value);
        }

        public Brush StatusBrush
        {
            get => _statusBrush;
            protected set => SetProperty(ref _statusBrush, value);
        }

        public string ResultText
        {
            get => _resultText;
            protected set => SetProperty(ref _resultText, value);
        }

        public string CapabilityText
        {
            get => _capabilityText;
            private set => SetProperty(ref _capabilityText, value);
        }

        /// <summary>
        /// True while any command is executing.
        /// Buttons can bind IsEnabled to !IsBusy for visual feedback.
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>
        /// The underlying IIndustrialClient. Subclasses can access for protocol-specific casts.
        /// </summary>
        protected IIndustrialClient Client => _client;

        // ── Commands ──

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ReadCommand { get; }
        public ICommand WriteCommand { get; }

        // ── Abstract hooks ──

        protected abstract string ProtocolTag { get; }           // "S7", "MC"
        protected abstract ProtocolKind ProtocolKind { get; }
        protected abstract IIndustrialClient CreateClient();
        protected abstract ReadRequest BuildReadRequest();
        protected abstract WriteRequest BuildWriteRequest();

        // ── Constructor ──

        protected ProtocolTabViewModel(DemoAppContext ctx) : base(ctx)
        {
            ConnectCommand = new RelayCommand(ConnectAsync);
            DisconnectCommand = new RelayCommand(DisconnectAsync);
            ReadCommand = new RelayCommand(ReadAsync);
            WriteCommand = new RelayCommand(WriteAsync);
        }

        // ── Virtual lifecycle hooks ──

        /// <summary>
        /// Called before creating the client. Override to apply additional config.
        /// </summary>
        protected virtual void OnBeforeConnect() { }

        /// <summary>
        /// Called after a successful connection. Override for protocol-specific actions.
        /// </summary>
        protected virtual void OnAfterConnect() { }

        /// <summary>
        /// Called after disconnect is complete. Override for cleanup.
        /// </summary>
        protected virtual void OnAfterDisconnect() { }

        // ── Command implementations ──

        internal async Task ConnectAsync()
        {
            await ResetClientInternalAsync();

            try
            {
                OnBeforeConnect();
                _client = CreateClient();
                await _client.ConnectAsync(CancellationToken.None);
                OnAfterConnect();
                RefreshCapabilityText();
                UpdateStatus(true);
                Ctx.SetHeaderStatus(ProtocolTag + " 已连接", Brushes.LightGreen);
                LogInfo(ProtocolTag + " 已连接。");
            }
            catch (Exception ex)
            {
                RefreshCapabilityText();
                UpdateStatus(false);
                HandleError(ProtocolTag + " 连接失败。", ex, true);
            }
        }

        internal async Task DisconnectAsync()
        {
            try
            {
                await ResetClientInternalAsync();
                ResultText = "已断开。";
                RefreshCapabilityText();
                UpdateStatus(false);
                Ctx.SetHeaderStatus(ProtocolTag + " 已断开", Brushes.Khaki);
                LogInfo(ProtocolTag + " 已断开。");
            }
            catch (Exception ex)
            {
                HandleError(ProtocolTag + " 断开失败。", ex, false);
            }
        }

        internal async Task ReadAsync()
        {
            if (!EnsureConnected()) return;
            try
            {
                var request = BuildReadRequest();
                var result = await _client.ReadAsync(request, CancellationToken.None);
                ResultText = FormatHelper.FormatDataValue(result);
                Ctx.QueueDatabaseValues(_client, new[] { result });
                RememberCurrentAddress();
                Ctx.SetHeaderStatus(ProtocolTag + " 读取完成", Brushes.LightGreen);
                LogInfo(ProtocolTag + " 读取完成。");
            }
            catch (Exception ex)
            {
                HandleError(ProtocolTag + " 读取失败。", ex, true);
            }
        }

        internal async Task WriteAsync()
        {
            if (!EnsureConnected()) return;
            try
            {
                var request = BuildWriteRequest();
                await _client.WriteAsync(request, CancellationToken.None);
                RememberCurrentAddress();
                ResultText = string.Format("写入成功：{0} = {1}", request.Address, FormatHelper.FormatDisplayValue(request.Value));
                Ctx.SetHeaderStatus(ProtocolTag + " 写入完成", Brushes.LightGreen);
                LogInfo(ProtocolTag + " 写入完成。");
            }
            catch (Exception ex)
            {
                HandleError(ProtocolTag + " 写入失败。", ex, true);
            }
        }

        // ── Shared helpers ──

        private async Task ResetClientInternalAsync()
        {
            var client = _client;
            _client = null;
            if (client == null) return;
            try { await client.DisconnectAsync(CancellationToken.None); } catch { }
            client.Dispose();
        }

        /// <summary>
        /// Public entry for MainWindow cleanup. Safe to call multiple times.
        /// </summary>
        public async Task ResetClientAsync()
        {
            await ResetClientInternalAsync();
            UpdateStatus(false);
            RefreshCapabilityText();
        }

        public void RefreshCapabilityText()
        {
            var client = _client;
            var capabilities = client == null
                ? ProtocolCapabilities.ForProtocol(ProtocolKind)
                : IndustrialClientPlatformExtensions.GetCapabilities(client);
            CapabilityText = CapabilityDisplayHelper.Format(capabilities);
        }

        private bool EnsureConnected()
        {
            if (_client != null && _client.IsConnected) return true;
            return false; // UI should bind to IsConnected/IsBusy for guard
        }

        private void UpdateStatus(bool connected)
        {
            if (!connected || _client == null)
            {
                StatusText = "未连接";
                StatusBrush = Brushes.IndianRed;
                return;
            }
            if (_client.IsConnected)
            {
                StatusText = "已连接";
                StatusBrush = Brushes.ForestGreen;
                return;
            }
            var health = _client.GetHealth();
            StatusText = health?.Status.ToString() ?? "未连接";
            StatusBrush = Brushes.DarkGoldenrod;
        }

        /// <summary>
        /// Override to save the current address to the protocol's recent-address list.
        /// </summary>
        protected virtual void RememberCurrentAddress() { }

        /// <summary>
        /// Save UI state to the shared DemoUiState.
        /// Called by MainWindow.OnClosing.
        /// Override to persist protocol-specific controls.
        /// </summary>
        public virtual void SaveState() { }

        /// <summary>
        /// Restore UI state from the shared DemoUiState.
        /// Called during Initialize.
        /// </summary>
        public virtual void RestoreState() { }

        /// <summary>
        /// Call from Dispose or MainWindow cleanup to tear down client.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ResetClientInternalAsync().GetAwaiter().GetResult();
            }
            base.Dispose(disposing);
        }
    }
}
