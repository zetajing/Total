using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommDemo.Services
{
    /// <summary>
    /// 产品层设备运行服务。应用层只负责启动、停止和展示，连接、轮询、重连、
    /// 点位解析等能力全部委托给 IndustrialCommSdk 的 IndustrialDeviceHost。
    /// </summary>
    public sealed class IndustrialApplicationRuntime : IDisposable
    {
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private IndustrialDeviceHost _host;
        private int _disposed;

        public IndustrialApplicationRuntime(string configFilePath, IIndustrialLogger logger)
        {
            if (string.IsNullOrWhiteSpace(configFilePath))
                throw new ArgumentException("Config file path cannot be null or empty.", nameof(configFilePath));

            ConfigFilePath = Path.GetFullPath(configFilePath);
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public string ConfigFilePath { get; }
        public bool IsLoaded { get { return _host != null; } }
        public bool IsRunning { get; private set; }

        public event EventHandler<RuntimeDevicesChangedEventArgs> DevicesChanged;
        public event EventHandler<RuntimeDeviceStateEventArgs> DeviceStateChanged;
        public event EventHandler<RuntimeValuesEventArgs> ValuesReceived;

        public async Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await StopAndDisposeHostAsync(cancellationToken).ConfigureAwait(false);

                CreateHost();
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_host == null)
                {
                    CreateHost();
                }

                await _host.StartAsync(cancellationToken).ConfigureAwait(false);
                IsRunning = true;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_host != null) await _host.StopAsync(cancellationToken).ConfigureAwait(false);
                IsRunning = false;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task StopAndDisposeHostAsync(CancellationToken cancellationToken)
        {
            var host = _host;
            _host = null;
            IsRunning = false;
            if (host == null) return;

            host.DeviceStateChanged -= Host_DeviceStateChanged;
            host.ValuesReceived -= Host_ValuesReceived;
            await host.StopAsync(cancellationToken).ConfigureAwait(false);
            host.Dispose();
        }

        private void Host_DeviceStateChanged(object sender, IndustrialDeviceStateChangedEventArgs e)
        {
            RaiseSafely(DeviceStateChanged, new RuntimeDeviceStateEventArgs(
                e.DeviceName,
                e.Health == null ? "未知" : e.Health.Status.ToString(),
                e.ErrorMessage ?? string.Empty), "设备状态处理失败");
        }

        private void Host_ValuesReceived(object sender, IndustrialDeviceValuesEventArgs e)
        {
            var values = new List<RuntimeValueInfo>();
            var count = Math.Min(e.Tags.Count, e.Values.Count);
            for (var i = 0; i < count; i++)
            {
                var tag = e.Tags[i];
                var value = e.Values[i];
                values.Add(new RuntimeValueInfo
                {
                    DeviceName = e.DeviceName,
                    TagName = tag.Name,
                    Address = tag.Address,
                    Value = value.Value == null ? string.Empty : value.Value.ToString(),
                    Quality = value.Quality.ToString(),
                    Timestamp = e.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Error = value.ErrorMessage ?? string.Empty,
                });
            }

            var sourceHost = sender as IndustrialDeviceHost;
            var client = sourceHost == null ? null : sourceHost.Get(e.DeviceName).Device.Client;
            RaiseSafely(ValuesReceived, new RuntimeValuesEventArgs(e.DeviceName, values, client, e.Values), "实时数据处理失败");
        }

        private void RaiseDevicesChanged(IReadOnlyList<RuntimeDeviceInfo> devices)
        {
            RaiseSafely(DevicesChanged, new RuntimeDevicesChangedEventArgs(devices), "设备列表处理失败");
        }

        private void CreateHost()
        {
            var host = IndustrialDeviceHost.Load(ConfigFilePath, _logger);
            host.DeviceStateChanged += Host_DeviceStateChanged;
            host.ValuesReceived += Host_ValuesReceived;
            _host = host;

            RaiseDevicesChanged(host.Devices.Values.Select(device => new RuntimeDeviceInfo
            {
                Name = device.Config.Name,
                Protocol = device.Config.Protocol,
                Endpoint = FormatEndpoint(device.Config),
                State = "已加载",
                Error = string.Empty,
            }).ToList());
        }

        private void RaiseSafely<TEventArgs>(EventHandler<TEventArgs> handlers, TEventArgs args, string errorMessage)
            where TEventArgs : EventArgs
        {
            if (handlers == null) return;
            foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception ex)
                {
                    _logger.Error(errorMessage, ex);
                }
            }
        }

        private static string FormatEndpoint(IndustrialDeviceConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.PortName)) return config.PortName;
            if (string.IsNullOrWhiteSpace(config.Host)) return "-";
            return config.Port.HasValue ? config.Host + ":" + config.Port.Value : config.Host;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifecycleGate.Wait();
            try
            {
                StopAndDisposeHostAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                _lifecycleGate.Release();
                _lifecycleGate.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(IndustrialApplicationRuntime));
        }
    }

    public sealed class RuntimeDeviceInfo
    {
        public string Name { get; set; }
        public string Protocol { get; set; }
        public string Endpoint { get; set; }
        public string State { get; set; }
        public string Error { get; set; }
    }

    public sealed class RuntimeValueInfo
    {
        public string DeviceName { get; set; }
        public string TagName { get; set; }
        public string Address { get; set; }
        public string Value { get; set; }
        public string Quality { get; set; }
        public string Timestamp { get; set; }
        public string Error { get; set; }
    }

    public sealed class RuntimeDevicesChangedEventArgs : EventArgs
    {
        public RuntimeDevicesChangedEventArgs(IReadOnlyList<RuntimeDeviceInfo> devices) { Devices = devices; }
        public IReadOnlyList<RuntimeDeviceInfo> Devices { get; }
    }

    public sealed class RuntimeDeviceStateEventArgs : EventArgs
    {
        public RuntimeDeviceStateEventArgs(string deviceName, string state, string error)
        {
            DeviceName = deviceName;
            State = state;
            Error = error;
        }
        public string DeviceName { get; }
        public string State { get; }
        public string Error { get; }
    }

    public sealed class RuntimeValuesEventArgs : EventArgs
    {
        public RuntimeValuesEventArgs(
            string deviceName,
            IReadOnlyList<RuntimeValueInfo> values,
            IIndustrialClient client,
            IReadOnlyList<DataValue> rawValues)
        {
            DeviceName = deviceName;
            Values = values;
            Client = client;
            RawValues = rawValues;
        }
        public string DeviceName { get; }
        public IReadOnlyList<RuntimeValueInfo> Values { get; }
        public IIndustrialClient Client { get; }
        public IReadOnlyList<DataValue> RawValues { get; }
    }
}
