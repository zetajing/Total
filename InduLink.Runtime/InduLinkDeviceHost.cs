using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Runtime.Configuration;
using InduLink.Diagnostics;

namespace InduLink.Runtime
{
    public sealed class InduLinkDeviceHostOptions
    {
        /// <summary>一次主机停止或释放调用的总等待预算；未退出任务继续受跟踪。</summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>承载多个配置化设备的运行时，负责启动、轮询、状态通知和断线重连。</summary>
    public sealed class InduLinkDeviceHost : IDisposable, IAsyncDisposable
    {
        private readonly Dictionary<string, InduLinkHostedDevice> _devices = new Dictionary<string, InduLinkHostedDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly IInduLinkLogger _logger;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private int _started;
        private int _disposed;
        private readonly TimeSpan _shutdownTimeout;
        private Task _disposeTask;

        /// <summary>使用已解析配置创建设备主机，clientFactory 可用于自定义协议客户端或测试。</summary>
        public InduLinkDeviceHost(
            InduLinkSdkConfig config,
            string configDirectory,
            Func<InduLinkDeviceConfig, IInduLinkClient> clientFactory,
            IInduLinkLogger logger = null,
            InduLinkDeviceHostOptions options = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(configDirectory)) throw new ArgumentException("Config directory cannot be null or empty.", nameof(configDirectory));
            if (clientFactory == null) throw new ArgumentNullException(nameof(clientFactory));

            _logger = logger ?? NullInduLinkLogger.Instance;
            _shutdownTimeout = (options ?? new InduLinkDeviceHostOptions()).ShutdownTimeout;
            if (_shutdownTimeout <= TimeSpan.Zero || _shutdownTimeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(options), "ShutdownTimeout must be positive and fit the cancellation timer.");
            if (config.Devices == null)
            {
                throw new InvalidOperationException("Device configuration collection cannot be null.");
            }

            try
            {
                foreach (var deviceConfig in config.Devices)
                {
                    if (deviceConfig == null || !deviceConfig.Enabled)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(deviceConfig.Name))
                    {
                        throw new InvalidOperationException("Enabled device name cannot be null or empty.");
                    }

                    if (_devices.ContainsKey(deviceConfig.Name))
                    {
                        throw new InvalidOperationException("Duplicate enabled device name: " + deviceConfig.Name);
                    }

                    if (deviceConfig.Runtime == null || deviceConfig.Runtime.PollingIntervalMilliseconds <= 0)
                    {
                        throw new InvalidOperationException("pollingIntervalMilliseconds must be greater than zero for device: " + deviceConfig.Name);
                    }

                    if (deviceConfig.Runtime.ReconnectDelayMilliseconds <= 0)
                    {
                        throw new InvalidOperationException("reconnectDelayMilliseconds must be greater than zero for device: " + deviceConfig.Name);
                    }

                    InduLinkConfiguredClient configuredClient = null;
                    try
                    {
                        var tags = TagTable.Load(deviceConfig.ResolvePointsFile(configDirectory), ProtocolAddressComparer.ForProtocol(deviceConfig.Protocol));
                        configuredClient = new InduLinkConfiguredClient(deviceConfig.Name, clientFactory(deviceConfig), tags);
                        var hostedDevice = new InduLinkHostedDevice(
                            deviceConfig,
                            configuredClient,
                            RaiseStateChanged,
                            RaiseValuesReceived,
                            _logger,
                            _shutdownTimeout);
                        _devices.Add(deviceConfig.Name, hostedDevice);
                        configuredClient = null;
                    }
                    finally
                    {
                        if (configuredClient != null) configuredClient.Dispose();
                    }
                }
            }
            catch
            {
                foreach (var device in _devices.Values) device.Dispose();
                _devices.Clear();
                throw;
            }
        }

        /// <summary>获取所有已启用的托管设备。</summary>
        public IReadOnlyDictionary<string, InduLinkHostedDevice> Devices { get { return _devices; } }

        /// <summary>设备连接状态或错误变化时触发。</summary>
        public event EventHandler<InduLinkDeviceStateChangedEventArgs> DeviceStateChanged;

        /// <summary>设备轮询读取到一组点位时触发。</summary>
        public event EventHandler<InduLinkDeviceValuesEventArgs> ValuesReceived;

        /// <summary>启动全部已启用设备；单台设备连接失败不会阻止其他设备运行。</summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (Volatile.Read(ref _started) != 0) return;

                try
                {
                    foreach (var device in _devices.Values)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await device.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    Volatile.Write(ref _started, 1);
                }
                catch
                {
                    var rollbackFailed = false;
                    foreach (var device in _devices.Values)
                    {
                        if (!device.IsStarted) continue;
                        try { await device.StopAsync(CancellationToken.None).ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            rollbackFailed = true;
                            _logger.Error("Device start rollback failed: " + device.Device.DeviceName, ex);
                        }
                    }
                    Volatile.Write(ref _started, rollbackFailed ? 1 : 0);
                    throw;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        /// <summary>停止全部设备的轮询与重连任务并断开连接。</summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            using var budget = new CancellationTokenSource(_shutdownTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
            var token = linked.Token;
            try
            {
                await _lifecycleGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    if (Volatile.Read(ref _started) == 0) return;

                    var failures = new List<Exception>();
                    foreach (var device in _devices.Values)
                    {
                        token.ThrowIfCancellationRequested();
                        try { await device.StopAsync(token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception ex)
                        {
                            failures.Add(ex);
                            _logger.Error("Device stop failed: " + device.Device.DeviceName, ex);
                        }
                    }

                    if (failures.Count > 0)
                        throw new AggregateException("One or more devices failed to stop.", failures);

                    Volatile.Write(ref _started, 0);
                }
                finally
                {
                    _lifecycleGate.Release();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            {
                throw new TimeoutException("Device host shutdown exceeded its configured budget; cleanup remains tracked.");
            }
        }

        /// <summary>按名称取得托管设备，名称匹配不区分大小写。</summary>
        public InduLinkHostedDevice Get(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("Device name cannot be null or empty.", nameof(deviceName));

            InduLinkHostedDevice device;
            if (_devices.TryGetValue(deviceName, out device))
            {
                return device;
            }

            throw new KeyNotFoundException("Enabled device was not found: " + deviceName);
        }

        /// <summary>停止设备主机并释放所有底层协议客户端。</summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>异步停止设备主机并释放全部底层协议客户端。</summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _disposeTask = Task.Run(DisposeCoreAsync);
            try { await _disposeTask.WaitAsync(_shutdownTimeout).ConfigureAwait(false); }
            catch (TimeoutException ex) { _logger.Error("Device host disposal deferred until active tasks finish.", ex); }
        }

        private async Task DisposeCoreAsync()
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var device in _devices.Values)
                {
                    try { await device.DisposeFullyAsync().ConfigureAwait(false); }
                    catch (Exception ex) { _logger.Error("Hosted device disposal failed.", ex); }
                }
            }
            finally
            {
                _lifecycleGate.Release();
                _lifecycleGate.Dispose();
            }
        }

        private void RaiseStateChanged(InduLinkDeviceStateChangedEventArgs args)
        {
            try
            {
                DeviceStateChanged?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.Error("Device state event handler failed.", ex);
            }
        }

        private void RaiseValuesReceived(InduLinkDeviceValuesEventArgs args)
        {
            try
            {
                ValuesReceived?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.Error("Device values event handler failed.", ex);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(InduLinkDeviceHost));
            }
        }
    }

}
