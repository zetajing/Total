using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Runtime.Configuration;

namespace IndustrialCommSdk.Runtime
{
    /// <summary>表示 DeviceHost 管理的一台设备，并代理按点位名称的读写操作。</summary>
    public sealed class IndustrialHostedDevice : IDisposable
    {
        private readonly IndustrialDeviceConfig _config;
        private readonly Action<IndustrialDeviceStateChangedEventArgs> _stateChanged;
        private readonly Action<IndustrialDeviceValuesEventArgs> _valuesReceived;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _runCancellation;
        private Task _reconnectTask;
        private string _subscriptionId;
        private string _lastError;
        private int _started;
        private int _disposed;

        internal IndustrialHostedDevice(
            IndustrialDeviceConfig config,
            IndustrialConfiguredClient device,
            Action<IndustrialDeviceStateChangedEventArgs> stateChanged,
            Action<IndustrialDeviceValuesEventArgs> valuesReceived,
            IIndustrialLogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Device = device ?? throw new ArgumentNullException(nameof(device));
            _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
            _valuesReceived = valuesReceived ?? throw new ArgumentNullException(nameof(valuesReceived));
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        /// <summary>获取 devices.json 中的设备配置。</summary>
        public IndustrialDeviceConfig Config { get { return _config; } }

        /// <summary>获取已加载点位表和协议客户端的设备运行时。</summary>
        public IndustrialConfiguredClient Device { get; private set; }

        /// <summary>获取最近一次连接或轮询错误。</summary>
        public string LastError { get { return _lastError; } }

        /// <summary>获取底层客户端的健康快照。</summary>
        public HealthSnapshot Health { get { return Device.Client.GetHealth(); } }

        /// <summary>获取该设备是否已由主机启动。</summary>
        public bool IsStarted { get { return Volatile.Read(ref _started) != 0; } }

        /// <summary>按点位名称读取原始数据值。</summary>
        public Task<DataValue> ReadAsync(string tagName, CancellationToken cancellationToken = default)
        {
            return Device.ReadAsync(tagName, cancellationToken);
        }

        /// <summary>按点位名称读取强类型数据。</summary>
        public Task<T> ReadAsync<T>(string tagName, CancellationToken cancellationToken = default)
        {
            return Device.ReadAsync<T>(tagName, cancellationToken);
        }

        /// <summary>读取该设备点位表中的全部点位。</summary>
        public Task<IndustrialTagReadResult> ReadManyAsync(CancellationToken cancellationToken = default)
        {
            return Device.ReadManyAsync(cancellationToken);
        }

        /// <summary>按点位名称写入一个值。</summary>
        public Task WriteAsync(string tagName, object value, CancellationToken cancellationToken = default)
        {
            return Device.WriteAsync(tagName, value, cancellationToken);
        }

        /// <summary>按点位名称批量写入值。</summary>
        public Task WriteManyAsync(IReadOnlyDictionary<string, object> values, CancellationToken cancellationToken = default)
        {
            return Device.WriteManyAsync(values, cancellationToken);
        }

        internal async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(IndustrialHostedDevice));
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            {
                return;
            }

            var runCancellation = new CancellationTokenSource();
            _runCancellation = runCancellation;
            try
            {
                await ConnectAndSubscribeAsync(false, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _reconnectTask = Task.Run(() => ReconnectLoopAsync(runCancellation.Token));
            }
            catch
            {
                runCancellation.Cancel();
                if (await CleanupFailedStartAsync().ConfigureAwait(false))
                {
                    _reconnectTask = null;
                    _runCancellation = null;
                    runCancellation.Dispose();
                    Interlocked.Exchange(ref _started, 0);
                }
                throw;
            }
        }

        internal async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _started) == 0)
            {
                return;
            }

            var runCancellation = _runCancellation;
            if (runCancellation != null)
            {
                runCancellation.Cancel();
            }

            var reconnectTask = _reconnectTask;
            if (reconnectTask != null)
            {
                try
                {
                    await reconnectTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await StopSubscriptionAsync(cancellationToken).ConfigureAwait(false);
                if (Device.Client.IsConnected)
                {
                    await Device.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                }

                _lastError = null;
                Interlocked.Exchange(ref _started, 0);
                RaiseStateChanged();
            }
            finally
            {
                _lifecycleGate.Release();
            }

            _reconnectTask = null;
            _runCancellation = null;
            if (runCancellation != null) runCancellation.Dispose();
        }

        /// <summary>停止后台任务并释放底层协议客户端。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                Device.Dispose();
                _lifecycleGate.Dispose();
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromMilliseconds(_config.Runtime.ReconnectDelayMilliseconds);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    if (!Device.Client.IsConnected ||
                        Health.Status == ConnectionStatus.Faulted ||
                        string.IsNullOrWhiteSpace(_subscriptionId))
                    {
                        await ConnectAndSubscribeAsync(true, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    _lastError = ex.Message;
                    _logger.Error("Device reconnect operation was cancelled unexpectedly: " + Device.DeviceName, ex);
                    RaiseStateChanged();
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _logger.Error("Device reconnect loop failed: " + Device.DeviceName, ex);
                    RaiseStateChanged();
                }
            }
        }

        private async Task ConnectAndSubscribeAsync(bool reconnecting, CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (reconnecting)
                {
                    await StopSubscriptionAsync(cancellationToken).ConfigureAwait(false);
                    if (Device.Client.IsConnected)
                    {
                        await Device.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (!Device.Client.IsConnected)
                {
                    RaiseStateChanged();
                    await Device.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(_subscriptionId))
                {
                    var request = new SubscriptionRequest(
                        Device.DeviceName + ".devicehost",
                        Device.Client.DeviceId,
                        CreateReadRequests(),
                        TimeSpan.FromMilliseconds(_config.Runtime.PollingIntervalMilliseconds),
                        _config.Runtime.ReportOnChangeOnly);
                    _subscriptionId = await Device.Client.SubscribeAsync(request, OnSubscriptionReceived, cancellationToken).ConfigureAwait(false);
                }

                _lastError = null;
                RaiseStateChanged();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.Error("Device connect failed: " + Device.DeviceName, ex);
                RaiseStateChanged();
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task StopSubscriptionAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_subscriptionId))
            {
                return;
            }

            var subscriptionId = _subscriptionId;
            await Device.Client.UnsubscribeAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            if (string.Equals(_subscriptionId, subscriptionId, StringComparison.Ordinal))
                _subscriptionId = null;
        }

        private async Task<bool> CleanupFailedStartAsync()
        {
            var succeeded = true;
            await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                try { await StopSubscriptionAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    succeeded = false;
                    _logger.Error("Device failed-start subscription cleanup failed: " + Device.DeviceName, ex);
                }

                if (Device.Client.IsConnected)
                {
                    try { await Device.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        succeeded = false;
                        _logger.Error("Device failed-start disconnect failed: " + Device.DeviceName, ex);
                    }
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
            return succeeded;
        }

        private IReadOnlyCollection<ReadRequest> CreateReadRequests()
        {
            var requests = new List<ReadRequest>();
            foreach (var tag in Device.Tags.Tags)
            {
                requests.Add(new ReadRequest(Device.Client.DeviceId, tag.Address, tag.DataType, tag.Length));
            }

            return requests;
        }

        private void OnSubscriptionReceived(object sender, SubscriptionEvent args)
        {
            try
            {
                _valuesReceived(new IndustrialDeviceValuesEventArgs(Device.DeviceName, Device.Tags.Tags, args.Values, args.Timestamp));
            }
            catch (Exception ex)
            {
                _logger.Error("Device values event handler failed: " + Device.DeviceName, ex);
            }
        }

        private void RaiseStateChanged()
        {
            _stateChanged(new IndustrialDeviceStateChangedEventArgs(Device.DeviceName, Health, _lastError));
        }
    }

    /// <summary>提供托管设备状态变化时的健康快照和错误信息。</summary>
    public sealed class IndustrialDeviceStateChangedEventArgs : EventArgs
    {
        /// <summary>创建设备状态变化事件。</summary>
        public IndustrialDeviceStateChangedEventArgs(string deviceName, HealthSnapshot health, string errorMessage)
        {
            DeviceName = deviceName;
            Health = health;
            ErrorMessage = errorMessage;
        }

        /// <summary>获取设备名称。</summary>
        public string DeviceName { get; private set; }

        /// <summary>获取最新健康快照。</summary>
        public HealthSnapshot Health { get; private set; }

        /// <summary>获取最近一次连接或轮询错误。</summary>
        public string ErrorMessage { get; private set; }
    }

    /// <summary>提供托管设备一轮批量读取的点位和数据值。</summary>
    public sealed class IndustrialDeviceValuesEventArgs : EventArgs
    {
        /// <summary>创建设备批量读取事件。</summary>
        public IndustrialDeviceValuesEventArgs(string deviceName, IReadOnlyList<IndustrialTag> tags, IReadOnlyList<DataValue> values, DateTimeOffset timestamp)
        {
            DeviceName = deviceName;
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Timestamp = timestamp;
        }

        /// <summary>获取设备名称。</summary>
        public string DeviceName { get; private set; }

        /// <summary>获取与 Values 索引对应的点位定义。</summary>
        public IReadOnlyList<IndustrialTag> Tags { get; private set; }

        /// <summary>获取本轮批量读取的数据值。</summary>
        public IReadOnlyList<DataValue> Values { get; private set; }

        /// <summary>获取本轮数据的接收时间。</summary>
        public DateTimeOffset Timestamp { get; private set; }
    }
}
