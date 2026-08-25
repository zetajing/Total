using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Polling;
using TwinCAT.Ads;

namespace IndustrialCommSdk.Protocols.Ads
{
    /// <summary>
    /// TwinCAT ADS 客户端。变量地址使用 PLC 符号名，例如 MAIN.bool1、MAIN.str1 或 MAIN.ComplexStruct1。
    /// 基础 IIndustrialClient API 覆盖常用标量、字符串和字节数组；结构体等 ADS 任意类型使用 ReadAnyAsync/WriteAnyAsync。
    /// </summary>
    public sealed class AdsClient : IndustrialClientBase, INativeSubscriptionClient, IRegisterClient, IEventSubscriptionClient
    {
        private const string NativeSubscriptionPrefix = "ads:";
        private const string AnySubscriptionPrefix = "ads:any:";

        private readonly AdsClientOptions _options;
        private readonly AdsAddressParser _parser;
        private readonly object _clientSync = new object();
        private readonly object _notificationSync = new object();
        private readonly SemaphoreSlim _nativeGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, NativeSubscriptionRegistration> _nativeSubscriptions =
            new Dictionary<string, NativeSubscriptionRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, NotificationBinding> _notificationBindings =
            new Dictionary<int, NotificationBinding>();
        private readonly AdsNotificationExEventHandler _notificationHandler;
        private TcAdsClient _adsClient;

        public AdsClient(
            AdsClientOptions options,
            IIndustrialLogger logger = null,
            IPollingScheduler pollingScheduler = null,
            AdsAddressParser parser = null)
            : base(
                GetDeviceId(options),
                ProtocolKind.TwinCatAds,
                pollingScheduler ?? new PollingScheduler(logger),
                logger ?? NullIndustrialLogger.Instance,
                GetOperationTimeout(options))
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Port < 1 || options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));
            if (options.ConnectTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options.ConnectTimeoutMilliseconds));
            if (options.OperationTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options.OperationTimeoutMilliseconds));
            if (!string.IsNullOrWhiteSpace(options.AmsNetId))
            {
                var amsError = AdsProtocolProvider.ValidateAmsNetId(options.AmsNetId);
                if (!string.IsNullOrWhiteSpace(amsError)) throw new ArgumentException(amsError, nameof(options.AmsNetId));
            }

            _options = options;
            _parser = parser ?? new AdsAddressParser();
            _notificationHandler = OnAdsNotification;
        }

        /// <summary>返回 ADS 协议的能力描述。</summary>
        public override ProtocolCapabilities Capabilities
        {
            get { return ProtocolCapabilities.ForProtocol(Kind); }
        }

        public override bool IsConnected
        {
            get
            {
                lock (_clientSync)
                {
                    return _adsClient != null && _adsClient.IsConnected;
                }
            }
        }

        /// <summary>连接到目标 AMS Net ID 和 ADS 端口。</summary>
        protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => ConnectCore(cancellationToken), cancellationToken);
        }

        /// <summary>断开 ADS 连接，并删除当前连接上的通知注册。</summary>
        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => DisconnectCore(cancellationToken), cancellationToken);
        }

        protected override Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            return Task.Run(() => ReadValue(request, cancellationToken), cancellationToken);
        }

        protected override Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var values = new List<DataValue>(requests.Count);
                foreach (var request in requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        values.Add(ReadValue(request, cancellationToken));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        values.Add(new DataValue(
                            request.Address,
                            request.DataType,
                            null,
                            null,
                            QualityStatus.Bad,
                            DateTimeOffset.UtcNow,
                            ex.Message));
                    }
                }
                return new BatchReadResult(values);
            }, cancellationToken);
        }

        protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            return Task.Run(() => WriteValue(request, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// 使用 ADS 的 ReadAny 读取结构体、数组或其它任意 CLR 类型。
        /// 对 STRING(n) 或数组类型，args 传入与参考工程相同的长度/维度参数。
        /// </summary>
        public Task<T> ReadAnyAsync<T>(
            string variableName,
            int[] args = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var address = _parser.ParseTyped(variableName);
            var copiedArgs = CopyArguments(args);
            return ExecuteExclusiveAsync<T>(token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var client = GetConnectedClient();
                var handle = client.CreateVariableHandle(address.Normalized);
                try
                {
                    var value = copiedArgs == null
                        ? client.ReadAny(handle, typeof(T))
                        : client.ReadAny(handle, typeof(T), copiedArgs);
                    if (value == null) throw new IndustrialProtocolException("ADS ReadAny returned no value.");
                    return (T)value;
                }
                catch (IndustrialCommunicationException) { throw; }
                catch (Exception ex)
                {
                    throw new IndustrialProtocolException("ADS ReadAny failed for '" + address.Normalized + "'.", ex);
                }
                finally
                {
                    DeleteVariableHandle(client, handle);
                }
            }, token), cancellationToken);
        }

        /// <summary>使用 ADS 的 WriteAny 写入结构体、数组或其它任意 CLR 类型。</summary>
        public Task WriteAnyAsync(
            string variableName,
            object value,
            int[] args = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var address = _parser.ParseTyped(variableName);
            var copiedArgs = CopyArguments(args);
            return ExecuteExclusiveAsync(token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var client = GetConnectedClient();
                var handle = client.CreateVariableHandle(address.Normalized);
                try
                {
                    if (copiedArgs == null) client.WriteAny(handle, value);
                    else client.WriteAny(handle, value, copiedArgs);
                }
                catch (IndustrialCommunicationException) { throw; }
                catch (Exception ex)
                {
                    throw new IndustrialProtocolException("ADS WriteAny failed for '" + address.Normalized + "'.", ex);
                }
                finally
                {
                    DeleteVariableHandle(client, handle);
                }
            }, token), cancellationToken);
        }

        public async Task<string> SubscribeNativeAsync(
            SubscriptionRequest request,
            EventHandler<SubscriptionEvent> handler,
            CancellationToken cancellationToken)
        {
            ValidateSubscriptionRequest(request, handler);
            var subscriptionId = NativeSubscriptionPrefix + request.SubscriptionKey;
            var registration = NativeSubscriptionRegistration.ForShared(subscriptionId, request, handler);

            await _nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_nativeSubscriptions.ContainsKey(subscriptionId))
                    throw new InvalidOperationException("Subscription '" + request.SubscriptionKey + "' already exists.");

                _nativeSubscriptions.Add(subscriptionId, registration);
                try
                {
                    InstallNativeSubscription(registration, cancellationToken);
                    Logger.Info(string.Format(
                        CultureInfo.InvariantCulture,
                        "ADS native subscription started | Key={0} | Device={1} | Items={2} | Interval={3}ms",
                        request.SubscriptionKey,
                        DeviceId,
                        request.Items.Count,
                        request.Interval.TotalMilliseconds));
                    return subscriptionId;
                }
                catch
                {
                    _nativeSubscriptions.Remove(subscriptionId);
                    throw;
                }
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        public async Task<bool> TryUnsubscribeNativeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId) ||
                (!subscriptionId.StartsWith(NativeSubscriptionPrefix, StringComparison.OrdinalIgnoreCase) &&
                 !subscriptionId.StartsWith(AnySubscriptionPrefix, StringComparison.OrdinalIgnoreCase)))
                return false;

            await _nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                NativeSubscriptionRegistration registration;
                if (!_nativeSubscriptions.TryGetValue(subscriptionId, out registration)) return true;
                _nativeSubscriptions.Remove(subscriptionId);
                DeleteNativeNotificationHandles(registration, GetClientSnapshot());
                Logger.Info("ADS native subscription stopped | Id=" + subscriptionId);
                return true;
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        /// <summary>
        /// 为结构体等任意 ADS 类型创建原生通知。返回值可传给 UnsubscribeAnyAsync。
        /// </summary>
        public async Task<string> SubscribeAnyAsync(
            string variableName,
            Type valueType,
            TimeSpan interval,
            EventHandler<AdsValueNotificationEventArgs> handler,
            bool reportOnChangeOnly = true,
            int[] args = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var address = _parser.ParseTyped(variableName);
            if (valueType == null) throw new ArgumentNullException(nameof(valueType));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            var registrationId = AnySubscriptionPrefix + Guid.NewGuid().ToString("N");
            var registration = NativeSubscriptionRegistration.ForAny(
                registrationId,
                address.Normalized,
                valueType,
                CopyArguments(args),
                interval,
                reportOnChangeOnly,
                handler);

            await _nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _nativeSubscriptions.Add(registrationId, registration);
                try
                {
                    InstallNativeSubscription(registration, cancellationToken);
                    return registrationId;
                }
                catch
                {
                    _nativeSubscriptions.Remove(registrationId);
                    throw;
                }
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        /// <summary>取消 SubscribeAnyAsync 创建的订阅。</summary>
        public Task UnsubscribeAnyAsync(string subscriptionId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return UnsubscribeAsync(subscriptionId, cancellationToken);
        }

        protected override void DisposeCore()
        {
            _nativeGate.Wait();
            try
            {
                var client = GetClientSnapshot();
                foreach (var registration in _nativeSubscriptions.Values.ToList())
                    DeleteNativeNotificationHandles(registration, client);
                _nativeSubscriptions.Clear();
                lock (_notificationSync) { _notificationBindings.Clear(); }
                CloseClient(client);
            }
            finally
            {
                _nativeGate.Release();
                _nativeGate.Dispose();
            }
        }

        private void ConnectCore(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TcAdsClient client = null;
            try
            {
                client = new TcAdsClient();
                client.Timeout = _options.ConnectTimeoutMilliseconds;
                client.Synchronize = _options.SynchronizeNotifications;
                client.AdsNotificationEx += _notificationHandler;

                if (string.IsNullOrWhiteSpace(_options.AmsNetId))
                    client.Connect(_options.Port);
                else
                    client.Connect(_options.AmsNetId, _options.Port);

                cancellationToken.ThrowIfCancellationRequested();
                client.Timeout = _options.OperationTimeoutMilliseconds;
                lock (_clientSync) { _adsClient = client; }
                client = null;

                _nativeGate.Wait();
                try
                {
                    foreach (var registration in _nativeSubscriptions.Values.ToList())
                    {
                        try { InstallNativeSubscription(registration, cancellationToken); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Logger.Warn("ADS subscription restore failed | Id=" + registration.Id + " | " + ex.Message);
                        }
                    }
                }
                finally
                {
                    _nativeGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                CloseClient(client ?? GetClientSnapshot());
                throw;
            }
            catch (Exception ex)
            {
                CloseClient(client ?? GetClientSnapshot());
                throw new IndustrialConnectionException("Failed to connect TwinCAT ADS endpoint.", ex);
            }
        }

        private void DisconnectCore(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _nativeGate.Wait(cancellationToken);
            try
            {
                var client = GetClientSnapshot();
                foreach (var registration in _nativeSubscriptions.Values.ToList())
                    DeleteNativeNotificationHandles(registration, client);
                CloseClient(client);
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        private DataValue ReadValue(ReadRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = _parser.ParseTyped(request.Address);
            var client = GetConnectedClient();
            var handle = client.CreateVariableHandle(address.Normalized);
            try
            {
                var type = AdsTypeCodec.GetClrType(request.DataType);
                var args = AdsTypeCodec.GetArguments(request.DataType, request.Length);
                var value = args == null
                    ? client.ReadAny(handle, type)
                    : client.ReadAny(handle, type, args);
                return new DataValue(request.Address, request.DataType, value, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
            }
            finally
            {
                DeleteVariableHandle(client, handle);
            }
        }

        private void WriteValue(WriteRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = _parser.ParseTyped(request.Address);
            var client = GetConnectedClient();
            var handle = client.CreateVariableHandle(address.Normalized);
            try
            {
                var value = AdsTypeCodec.ConvertForWrite(request.DataType, request.Value);
                var args = AdsTypeCodec.GetArguments(request.DataType, request.Length);
                if (args == null) client.WriteAny(handle, value);
                else client.WriteAny(handle, value, args);
            }
            finally
            {
                DeleteVariableHandle(client, handle);
            }
        }

        private void InstallNativeSubscription(NativeSubscriptionRegistration registration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = GetConnectedClient();
            var installed = new List<NotificationBinding>();
            try
            {
                foreach (var binding in registration.Bindings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var mode = registration.ReportOnChangeOnly ? AdsTransMode.OnChange : AdsTransMode.Cyclic;
                    var cycleTime = ToAdsMilliseconds(registration.Interval);
                    var handle = binding.Arguments == null
                        ? client.AddDeviceNotificationEx(binding.VariableName, mode, cycleTime, 0, binding, binding.ValueType)
                        : client.AddDeviceNotificationEx(binding.VariableName, mode, cycleTime, 0, binding, binding.ValueType, binding.Arguments);
                    binding.Handle = handle;
                    lock (_notificationSync) { _notificationBindings[handle] = binding; }
                    installed.Add(binding);
                }
            }
            catch
            {
                foreach (var binding in installed)
                {
                    lock (_notificationSync) { _notificationBindings.Remove(binding.Handle); }
                    try { client.DeleteDeviceNotification(binding.Handle); }
                    catch (Exception ex) { Logger.Warn("ADS notification cleanup failed: " + ex.Message); }
                    binding.Handle = 0;
                }
                throw;
            }
        }

        private void DeleteNativeNotificationHandles(NativeSubscriptionRegistration registration, TcAdsClient client)
        {
            foreach (var binding in registration.Bindings)
            {
                var handle = binding.Handle;
                if (handle == 0) continue;
                lock (_notificationSync) { _notificationBindings.Remove(handle); }
                binding.Handle = 0;
                if (client == null) continue;
                try { client.DeleteDeviceNotification(handle); }
                catch (Exception ex) { Logger.Warn("ADS notification delete failed: " + ex.Message); }
            }
        }

        private void OnAdsNotification(object sender, AdsNotificationExEventArgs eventArgs)
        {
            NotificationBinding binding;
            lock (_notificationSync)
            {
                if (!_notificationBindings.TryGetValue(eventArgs.NotificationHandle, out binding)) return;
            }

            var timestamp = DateTimeOffset.UtcNow;
            try
            {
                if (binding.AnyHandler != null)
                {
                    binding.AnyHandler(this, new AdsValueNotificationEventArgs(
                        binding.Registration.Id,
                        binding.VariableName,
                        eventArgs.Value,
                        timestamp));
                    return;
                }

                var request = binding.Request;
                binding.Registration.Handler(this, new SubscriptionEvent(
                    binding.Registration.Id,
                    new[] { new DataValue(request.Address, request.DataType, eventArgs.Value, null, QualityStatus.Good, timestamp, null) },
                    timestamp));
            }
            catch (Exception ex)
            {
                Logger.Error("ADS notification handler failed | Variable=" + binding.VariableName, ex);
            }
        }

        private TcAdsClient GetConnectedClient()
        {
            var client = GetClientSnapshot();
            if (client == null || !client.IsConnected)
                throw new IndustrialConnectionException("TwinCAT ADS client is not connected.");
            return client;
        }

        private TcAdsClient GetClientSnapshot()
        {
            lock (_clientSync) { return _adsClient; }
        }

        private void CloseClient(TcAdsClient client)
        {
            if (client == null) return;
            lock (_clientSync)
            {
                if (ReferenceEquals(_adsClient, client)) _adsClient = null;
            }
            try { client.AdsNotificationEx -= _notificationHandler; }
            catch (Exception ex) { Logger.Warn("ADS notification event detach failed: " + ex.Message); }
            try { client.Dispose(); }
            catch (Exception ex) { Logger.Warn("ADS client dispose failed: " + ex.Message); }
        }

        private static void DeleteVariableHandle(TcAdsClient client, int handle)
        {
            if (client == null || handle == 0) return;
            try { client.DeleteVariableHandle(handle); }
            catch { /* The connection may already have gone away. */ }
        }

        private void ValidateSubscriptionRequest(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!string.Equals(DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Subscription device ID does not match the client device ID.", nameof(request));
            if (request.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Subscription must contain at least one read request.", nameof(request));
            foreach (var item in request.Items)
            {
                if (!string.Equals(DeviceId, item.DeviceId, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Subscription item device ID does not match the client device ID.", nameof(request));
                _parser.ParseTyped(item.Address);
                AdsTypeCodec.GetArguments(item.DataType, item.Length);
            }
        }

        private static int ToAdsMilliseconds(TimeSpan interval)
        {
            var milliseconds = interval.TotalMilliseconds;
            if (milliseconds < 1) return 1;
            return milliseconds > int.MaxValue ? int.MaxValue : (int)Math.Round(milliseconds, MidpointRounding.AwayFromZero);
        }

        private static int[] CopyArguments(int[] args)
        {
            return args == null ? null : (int[])args.Clone();
        }

        private static string GetDeviceId(AdsClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceId)) throw new ArgumentException("Device ID is required.", nameof(options));
            return options.DeviceId;
        }

        private static int GetOperationTimeout(AdsClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return options.OperationTimeoutMilliseconds;
        }

        private sealed class NativeSubscriptionRegistration
        {
            private NativeSubscriptionRegistration(string id, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler,
                string anyVariableName, Type anyType, int[] anyArguments, TimeSpan interval, bool reportOnChangeOnly,
                EventHandler<AdsValueNotificationEventArgs> anyHandler)
            {
                Id = id;
                Request = request;
                Handler = handler;
                Interval = interval;
                ReportOnChangeOnly = reportOnChangeOnly;
                AnyHandler = anyHandler;
                Bindings = new List<NotificationBinding>();
                if (request != null)
                {
                    foreach (var item in request.Items)
                        Bindings.Add(new NotificationBinding(this, item, AdsTypeCodec.GetClrType(item.DataType), AdsTypeCodec.GetArguments(item.DataType, item.Length)));
                }
                else
                {
                    Bindings.Add(new NotificationBinding(this, anyVariableName, anyType, anyArguments));
                }
            }

            public static NativeSubscriptionRegistration ForShared(string id, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler)
            {
                return new NativeSubscriptionRegistration(id, request, handler, null, null, null, request.Interval, request.ReportOnChangeOnly, null);
            }

            public static NativeSubscriptionRegistration ForAny(string id, string variableName, Type valueType, int[] arguments,
                TimeSpan interval, bool reportOnChangeOnly, EventHandler<AdsValueNotificationEventArgs> handler)
            {
                return new NativeSubscriptionRegistration(id, null, null, variableName, valueType, arguments, interval, reportOnChangeOnly, handler);
            }

            public string Id { get; private set; }
            public SubscriptionRequest Request { get; private set; }
            public EventHandler<SubscriptionEvent> Handler { get; private set; }
            public EventHandler<AdsValueNotificationEventArgs> AnyHandler { get; private set; }
            public TimeSpan Interval { get; private set; }
            public bool ReportOnChangeOnly { get; private set; }
            public List<NotificationBinding> Bindings { get; private set; }
        }

        private sealed class NotificationBinding
        {
            public NotificationBinding(NativeSubscriptionRegistration registration, ReadRequest request, Type valueType, int[] arguments)
            {
                Registration = registration;
                Request = request;
                VariableName = request.Address;
                ValueType = valueType;
                Arguments = arguments;
            }

            public NotificationBinding(NativeSubscriptionRegistration registration, string variableName, Type valueType, int[] arguments)
            {
                Registration = registration;
                VariableName = variableName;
                ValueType = valueType;
                Arguments = arguments;
            }

            public NativeSubscriptionRegistration Registration { get; private set; }
            public ReadRequest Request { get; private set; }
            public string VariableName { get; private set; }
            public Type ValueType { get; private set; }
            public int[] Arguments { get; private set; }
            public int Handle { get; set; }
            public EventHandler<AdsValueNotificationEventArgs> AnyHandler { get { return Registration.AnyHandler; } }
        }
    }

    internal static class AdsTypeCodec
    {
        public static Type GetClrType(DataType dataType)
        {
            switch (dataType)
            {
                case DataType.Bool: return typeof(bool);
                case DataType.Byte: return typeof(byte);
                case DataType.Char: return typeof(char);
                case DataType.Int16: return typeof(short);
                case DataType.UInt16: return typeof(ushort);
                case DataType.Int32: return typeof(int);
                case DataType.UInt32: return typeof(uint);
                case DataType.Float: return typeof(float);
                case DataType.Double: return typeof(double);
                case DataType.String: return typeof(string);
                case DataType.ByteArray: return typeof(byte[]);
                default:
                    throw new IndustrialProtocolException("TwinCAT ADS does not map DataType " + dataType + "; use ReadAnyAsync/WriteAnyAsync for custom types.");
            }
        }

        public static int[] GetArguments(DataType dataType, ushort length)
        {
            switch (dataType)
            {
                case DataType.String:
                case DataType.ByteArray:
                    if (length == 0) throw new IndustrialDataConversionException("ADS string and byte-array lengths must be greater than zero.");
                    return new[] { (int)length };
                case DataType.S7String:
                    throw new IndustrialProtocolException("S7String is not an ADS data type; use DataType.String with the PLC STRING length.");
                default:
                    return null;
            }
        }

        public static object ConvertForWrite(DataType dataType, object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            try
            {
                switch (dataType)
                {
                    case DataType.Bool: return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    case DataType.Byte: return Convert.ToByte(value, CultureInfo.InvariantCulture);
                    case DataType.Char: return Convert.ToChar(value, CultureInfo.InvariantCulture);
                    case DataType.Int16: return Convert.ToInt16(value, CultureInfo.InvariantCulture);
                    case DataType.UInt16: return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                    case DataType.Int32: return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    case DataType.UInt32: return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                    case DataType.Float: return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    case DataType.Double: return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    case DataType.String: return Convert.ToString(value, CultureInfo.InvariantCulture);
                    case DataType.ByteArray:
                        var bytes = value as byte[];
                        if (bytes == null) throw new InvalidCastException("ADS ByteArray writes require a byte[] value.");
                        return bytes;
                    default:
                        GetClrType(dataType);
                        throw new InvalidOperationException("Unreachable ADS data type branch.");
                }
            }
            catch (IndustrialCommunicationException) { throw; }
            catch (Exception ex)
            {
                throw new IndustrialDataConversionException("Cannot convert value to ADS " + dataType + ".", ex);
            }
        }
    }
}
