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
using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.SumCommand;
using TwinCAT.TypeSystem;
using BeckhoffAdsClient = TwinCAT.Ads.AdsClient;

namespace IndustrialCommSdk.Protocols.Ads
{
    /// <summary>
    /// TwinCAT ADS 客户端。变量地址使用 PLC 符号名，例如 MAIN.xStart、MAIN.nCount 或 MAIN.ComplexStruct1。
    /// 标量和字符串使用 SDK 的通用 IIndustrialClient API；结构体、数组和其它任意 CLR 类型使用 ReadAnyAsync/WriteAnyAsync。
    /// </summary>
    public sealed class AdsClient : IndustrialClientBase, INativeSubscriptionClient, IRegisterClient, IEventSubscriptionClient
    {
        private const string NativeSubscriptionPrefix = "ads:";
        private const string AnySubscriptionPrefix = "ads:any:";

        private readonly AdsClientOptions _options;
        private readonly AdsAddressParser _parser;
        private readonly object _clientSync = new object();
        private readonly object _handleSync = new object();
        private readonly object _notificationSync = new object();
        private readonly SemaphoreSlim _nativeGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, uint> _variableHandles =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NativeSubscriptionRegistration> _nativeSubscriptions =
            new Dictionary<string, NativeSubscriptionRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, NotificationBinding> _notificationBindings =
            new Dictionary<uint, NotificationBinding>();
        private readonly EventHandler<AdsNotificationExEventArgs> _notificationHandler;
        private readonly EventHandler<ConnectionStateChangedEventArgs> _connectionStateHandler;
        private BeckhoffAdsClient _adsClient;
        private AdsDeviceStateSnapshot _deviceState;
        private int _transportLost;

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
            if (options.MaxBatchItems <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxBatchItems));
            if (options.MaxBatchPayloadBytes < 4096) throw new ArgumentOutOfRangeException(nameof(options.MaxBatchPayloadBytes));
            if (!string.IsNullOrWhiteSpace(options.AmsNetId))
            {
                var amsError = AdsProtocolProvider.ValidateAmsNetId(options.AmsNetId);
                if (!string.IsNullOrWhiteSpace(amsError)) throw new ArgumentException(amsError, nameof(options.AmsNetId));
            }

            _options = options;
            _parser = parser ?? new AdsAddressParser();
            _notificationHandler = OnAdsNotification;
            _connectionStateHandler = OnConnectionStateChanged;
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
                    return _adsClient != null && _adsClient.IsConnected && Volatile.Read(ref _transportLost) == 0;
                }
            }
        }

        /// <summary>读取最近一次成功连接时获取的 ADS 设备状态。</summary>
        public AdsDeviceStateSnapshot LastDeviceState
        {
            get { lock (_clientSync) { return _deviceState; } }
        }

        /// <summary>主动读取目标 PLC 的 ADS 状态，用于连接探测和诊断。</summary>
        public Task<AdsDeviceStateSnapshot> ReadDeviceStateAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteExclusiveAsync(async token =>
            {
                var client = GetConnectedClient();
                var result = await client.ReadStateAsync(token).ConfigureAwait(false);
                EnsureSucceeded(result, "ADS ReadState");
                var state = new AdsDeviceStateSnapshot(
                    result.State.AdsState.ToString(),
                    result.State.DeviceState,
                    result.TimeStamp == default(DateTimeOffset) ? DateTimeOffset.UtcNow : result.TimeStamp);
                lock (_clientSync) { _deviceState = state; }
                return state;
            }, cancellationToken);
        }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = GetClientSnapshot();
            if (existing != null && existing.IsConnected) return;

            BeckhoffAdsClient client = null;
            try
            {
                var settings = _options.SynchronizeNotifications
                    ? AdsClientSettings.CompatibilityDefault
                    : AdsClientSettings.Default;
                settings.Timeout = _options.ConnectTimeoutMilliseconds;
                client = new BeckhoffAdsClient(settings);
                client.Timeout = _options.ConnectTimeoutMilliseconds;
                client.ConnectionStateChanged += _connectionStateHandler;
                client.AdsNotificationEx += _notificationHandler;

                if (string.IsNullOrWhiteSpace(_options.AmsNetId))
                    await client.ConnectAsync(_options.Port, cancellationToken).ConfigureAwait(false);
                else
                    await client.ConnectAsync(new AmsNetId(_options.AmsNetId), _options.Port, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                if (_options.ValidateTargetStateOnConnect)
                {
                    var stateResult = await client.ReadStateAsync(cancellationToken).ConfigureAwait(false);
                    EnsureSucceeded(stateResult, "ADS target state probe");
                    lock (_clientSync)
                    {
                        _deviceState = new AdsDeviceStateSnapshot(
                            stateResult.State.AdsState.ToString(),
                            stateResult.State.DeviceState,
                            stateResult.TimeStamp == default(DateTimeOffset) ? DateTimeOffset.UtcNow : stateResult.TimeStamp);
                    }
                }

                client.Timeout = _options.OperationTimeoutMilliseconds;
                var previousClient = GetClientSnapshot();
                if (previousClient != null && !ReferenceEquals(previousClient, client))
                    await CloseClientAsync(previousClient, CancellationToken.None).ConfigureAwait(false);

                lock (_clientSync)
                {
                    _adsClient = client;
                    _transportLost = 0;
                }
                client = null;

                await _nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    foreach (var registration in _nativeSubscriptions.Values.ToList())
                    {
                        try { await InstallNativeSubscriptionAsync(registration, cancellationToken).ConfigureAwait(false); }
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
                await CloseClientAsync(client ?? GetClientSnapshot(), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await CloseClientAsync(client ?? GetClientSnapshot(), CancellationToken.None).ConfigureAwait(false);
                throw new IndustrialConnectionException("Failed to connect TwinCAT ADS endpoint.", ex);
            }
        }

        protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _nativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var client = GetClientSnapshot();
                await DeleteNativeNotificationHandlesAsync(client, cancellationToken).ConfigureAwait(false);
                await CloseClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        protected override Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            return ReadValueAsync(request, cancellationToken);
        }

        protected override async Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests,
            CancellationToken cancellationToken)
        {
            var ordered = requests.ToList();
            if (!_options.EnableSumCommands || ordered.Count == 1)
            {
                var values = new List<DataValue>(ordered.Count);
                foreach (var request in ordered)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { values.Add(await ReadValueAsync(request, cancellationToken).ConfigureAwait(false)); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { values.Add(BadValue(request, ex.Message)); }
                }
                return new BatchReadResult(values);
            }

            var client = GetConnectedClient();
            var batchValues = new List<DataValue>(ordered.Count);
            foreach (var chunk in CreateReadChunks(ordered))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var builder = SumInstancePathAnyTypeRead.Create(client);
                foreach (var request in chunk)
                {
                    var type = AdsTypeCodec.GetClrType(request.DataType);
                    var args = AdsTypeCodec.GetArguments(request.DataType, request.Length);
                    var specifier = args == null ? new AnyTypeSpecifier(type) : new AnyTypeSpecifier(type, args);
                    builder.AddEntry(_parser.ParseTyped(request.Address).Normalized, specifier);
                }

                var result = await builder
                    .WithFallbackMode(SumFallbackMode.Discrete)
                    .Build()
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                var results = result == null ? null : result.ValueResults;
                for (var i = 0; i < chunk.Count; i++)
                {
                    var request = chunk[i];
                    if (results == null || i >= results.Length || results[i] == null || !results[i].Succeeded)
                    {
                        var code = results != null && i < results.Length && results[i] != null
                            ? (AdsErrorCode)results[i].ErrorCode
                            : (result == null ? AdsErrorCode.InternalError : result.OverallError);
                        batchValues.Add(BadValue(request, FormatAdsError(code)));
                    }
                    else
                    {
                        var item = results[i];
                        batchValues.Add(new DataValue(
                            request.Address,
                            request.DataType,
                            item.Value,
                            null,
                            QualityStatus.Good,
                            item.TimeStamp == default(DateTimeOffset) ? DateTimeOffset.UtcNow : item.TimeStamp,
                            null));
                    }
                }
            }

            return new BatchReadResult(batchValues);
        }

        protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            return WriteValueAsync(request, cancellationToken);
        }

        protected override async Task WriteManyCoreAsync(
            IReadOnlyCollection<WriteRequest> requests,
            CancellationToken cancellationToken)
        {
            var ordered = requests.ToList();
            if (!_options.EnableSumCommands || ordered.Count == 1)
            {
                foreach (var request in ordered)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WriteValueAsync(request, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            var client = GetConnectedClient();
            foreach (var chunk in CreateWriteChunks(ordered))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var builder = SumWriteBySymbolPath.Create(client);
                var values = new object[chunk.Count];
                for (var i = 0; i < chunk.Count; i++)
                {
                    builder.AddEntry(_parser.ParseTyped(chunk[i].Address).Normalized);
                    values[i] = AdsTypeCodec.ConvertForWrite(chunk[i].DataType, chunk[i].Value);
                }

                var result = await builder.Build().WriteAsync(values, cancellationToken).ConfigureAwait(false);
                if (result != null && result.OverallSucceeded) continue;

                var errors = new List<AdsBatchWriteError>();
                var subErrors = result == null ? null : result.SubErrors;
                for (var i = 0; i < chunk.Count; i++)
                {
                    var code = subErrors != null && i < subErrors.Length
                        ? subErrors[i]
                        : (result == null ? AdsErrorCode.InternalError : result.OverallError);
                    if (code != AdsErrorCode.NoError)
                        errors.Add(new AdsBatchWriteError(chunk[i].Address, code, FormatAdsError(code)));
                }

                if (errors.Count == 0)
                {
                    var code = result == null ? AdsErrorCode.InternalError : result.OverallError;
                    errors.AddRange(chunk.Select(item => new AdsBatchWriteError(item.Address, code, FormatAdsError(code))));
                }
                throw new AdsBatchWriteException(errors);
            }
        }

        /// <summary>使用 ADS 的 ReadAny 读取结构体、数组或其它任意 CLR 类型。</summary>
        public async Task<T> ReadAnyAsync<T>(
            string variableName,
            int[] args = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var address = _parser.ParseTyped(variableName);
            var copiedArgs = CopyArguments(args);
            return await ExecuteExclusiveAsync(async token =>
            {
                var client = GetConnectedClient();
                var result = copiedArgs == null
                    ? await client.ReadAnyAsync(address.Normalized, typeof(T), token).ConfigureAwait(false)
                    : await client.ReadAnyAsync(address.Normalized, typeof(T), copiedArgs, token).ConfigureAwait(false);
                EnsureSucceeded(result, "ADS ReadAny '" + address.Normalized + "'");
                if (result.Value == null) throw new IndustrialProtocolException("ADS ReadAny returned no value.");
                return (T)result.Value;
            }, cancellationToken).ConfigureAwait(false);
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
            return ExecuteExclusiveAsync(async token =>
            {
                var client = GetConnectedClient();
                var handle = await GetVariableHandleAsync(client, address.Normalized, token).ConfigureAwait(false);
                var result = copiedArgs == null
                    ? await client.WriteAnyAsync(handle, value, token).ConfigureAwait(false)
                    : await client.WriteAnyAsync(handle, value, copiedArgs, token).ConfigureAwait(false);
                EnsureSucceeded(result, "ADS WriteAny '" + address.Normalized + "'");
            }, cancellationToken);
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
                    await InstallNativeSubscriptionAsync(registration, cancellationToken).ConfigureAwait(false);
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
                if (!_nativeSubscriptions.TryGetValue(subscriptionId, out var registration)) return true;
                _nativeSubscriptions.Remove(subscriptionId);
                await DeleteNativeNotificationHandlesAsync(GetClientSnapshot(), cancellationToken, registration).ConfigureAwait(false);
                Logger.Info("ADS native subscription stopped | Id=" + subscriptionId);
                return true;
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        /// <summary>为结构体等任意 ADS 类型创建原生通知。返回值可传给 UnsubscribeAnyAsync。</summary>
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
                    await InstallNativeSubscriptionAsync(registration, cancellationToken).ConfigureAwait(false);
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
                DeleteNativeNotificationHandles(client);
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

        private async Task<DataValue> ReadValueAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = _parser.ParseTyped(request.Address);
            var client = GetConnectedClient();
            var type = AdsTypeCodec.GetClrType(request.DataType);
            var args = AdsTypeCodec.GetArguments(request.DataType, request.Length);
            var result = args == null
                ? await client.ReadAnyAsync(address.Normalized, type, cancellationToken).ConfigureAwait(false)
                : await client.ReadAnyAsync(address.Normalized, type, args, cancellationToken).ConfigureAwait(false);
            EnsureSucceeded(result, "ADS read '" + address.Normalized + "'");
            return new DataValue(
                request.Address,
                request.DataType,
                result.Value,
                null,
                QualityStatus.Good,
                result.TimeStamp == default(DateTimeOffset) ? DateTimeOffset.UtcNow : result.TimeStamp,
                null);
        }

        private async Task WriteValueAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var address = _parser.ParseTyped(request.Address);
            var client = GetConnectedClient();
            var value = AdsTypeCodec.ConvertForWrite(request.DataType, request.Value);
            var args = AdsTypeCodec.GetArguments(request.DataType, request.Length);
            var handle = await GetVariableHandleAsync(client, address.Normalized, cancellationToken).ConfigureAwait(false);
            var result = args == null
                ? await client.WriteAnyAsync(handle, value, cancellationToken).ConfigureAwait(false)
                : await client.WriteAnyAsync(handle, value, args, cancellationToken).ConfigureAwait(false);
            EnsureSucceeded(result, "ADS write '" + address.Normalized + "'");
        }

        private async Task InstallNativeSubscriptionAsync(NativeSubscriptionRegistration registration, CancellationToken cancellationToken)
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
                    var settings = new NotificationSettings(mode, ToAdsMilliseconds(registration.Interval), 0);
                    var result = await client.AddDeviceNotificationExAsync(
                        binding.VariableName,
                        settings,
                        binding,
                        binding.ValueType,
                        binding.Arguments,
                        cancellationToken).ConfigureAwait(false);
                    EnsureSucceeded(result, "ADS add notification '" + binding.VariableName + "'");
                    binding.Handle = result.Handle;
                    lock (_notificationSync) { _notificationBindings[binding.Handle] = binding; }
                    installed.Add(binding);
                }
            }
            catch
            {
                foreach (var binding in installed)
                {
                    lock (_notificationSync) { _notificationBindings.Remove(binding.Handle); }
                    try { await client.DeleteDeviceNotificationAsync(binding.Handle, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) { Logger.Warn("ADS notification cleanup failed: " + ex.Message); }
                    binding.Handle = 0;
                }
                throw;
            }
        }

        private async Task DeleteNativeNotificationHandlesAsync(
            BeckhoffAdsClient client,
            CancellationToken cancellationToken,
            NativeSubscriptionRegistration only = null)
        {
            var bindings = (only == null ? _nativeSubscriptions.Values.SelectMany(x => x.Bindings) : only.Bindings).ToList();
            foreach (var binding in bindings)
            {
                var handle = binding.Handle;
                if (handle == 0) continue;
                lock (_notificationSync) { _notificationBindings.Remove(handle); }
                binding.Handle = 0;
                if (client == null) continue;
                try { await client.DeleteDeviceNotificationAsync(handle, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { Logger.Warn("ADS notification delete failed: " + ex.Message); }
            }
        }

        private void DeleteNativeNotificationHandles(BeckhoffAdsClient client)
        {
            var bindings = _nativeSubscriptions.Values.SelectMany(x => x.Bindings).ToList();
            foreach (var binding in bindings)
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
                if (!_notificationBindings.TryGetValue(eventArgs.Handle, out binding)) return;
            }

            var timestamp = eventArgs.TimeStamp == default(DateTimeOffset) ? DateTimeOffset.UtcNow : eventArgs.TimeStamp;
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

        private void OnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs eventArgs)
        {
            var connected = eventArgs.NewState == ConnectionState.Connected;
            Interlocked.Exchange(ref _transportLost, connected ? 0 : 1);
            if (!connected)
            {
                lock (_notificationSync)
                {
                    // Notification handles belong to the old ADS connection.
                    // Ignore late events until the logical subscriptions are restored.
                    _notificationBindings.Clear();
                }
                var detail = eventArgs.Exception == null ? string.Empty : " | " + eventArgs.Exception.Message;
                Logger.Warn("ADS connection state changed to " + eventArgs.NewState + detail);
            }
        }

        private async Task<uint> GetVariableHandleAsync(BeckhoffAdsClient client, string address, CancellationToken cancellationToken)
        {
            lock (_handleSync)
            {
                if (_variableHandles.TryGetValue(address, out var cached)) return cached;
            }

            var result = await client.CreateVariableHandleAsync(address, cancellationToken).ConfigureAwait(false);
            EnsureSucceeded(result, "ADS create handle '" + address + "'");
            lock (_handleSync)
            {
                if (_variableHandles.TryGetValue(address, out var cached))
                {
                    try { client.DeleteVariableHandle(result.Handle); } catch { }
                    return cached;
                }
                _variableHandles[address] = result.Handle;
                return result.Handle;
            }
        }

        private async Task CloseClientAsync(BeckhoffAdsClient client, CancellationToken cancellationToken)
        {
            if (client == null) return;
            await DeleteVariableHandlesAsync(client, cancellationToken).ConfigureAwait(false);
            try
            {
                if (client.IsConnected) await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) { Logger.Warn("ADS client disconnect failed: " + ex.Message); }
            finally
            {
                DetachAndDisposeClient(client);
            }
        }

        private async Task DeleteVariableHandlesAsync(BeckhoffAdsClient client, CancellationToken cancellationToken)
        {
            KeyValuePair<string, uint>[] handles;
            lock (_handleSync)
            {
                handles = _variableHandles.ToArray();
                _variableHandles.Clear();
            }

            foreach (var item in handles)
            {
                try { await client.DeleteVariableHandleAsync(item.Value, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { Logger.Warn("ADS variable handle cleanup failed | Address=" + item.Key + " | " + ex.Message); }
            }
        }

        private void CloseClient(BeckhoffAdsClient client)
        {
            if (client == null) return;
            KeyValuePair<string, uint>[] handles;
            lock (_handleSync)
            {
                handles = _variableHandles.ToArray();
                _variableHandles.Clear();
            }
            foreach (var item in handles)
            {
                try { client.DeleteVariableHandle(item.Value); } catch { }
            }
            try { if (client.IsConnected) client.Disconnect(); } catch (Exception ex) { Logger.Warn("ADS client disconnect failed: " + ex.Message); }
            DetachAndDisposeClient(client);
        }

        private void DetachAndDisposeClient(BeckhoffAdsClient client)
        {
            lock (_clientSync)
            {
                if (ReferenceEquals(_adsClient, client)) _adsClient = null;
                _transportLost = 1;
            }
            try { client.ConnectionStateChanged -= _connectionStateHandler; } catch { }
            try { client.AdsNotificationEx -= _notificationHandler; } catch { }
            try { client.Dispose(); } catch (Exception ex) { Logger.Warn("ADS client dispose failed: " + ex.Message); }
        }

        private BeckhoffAdsClient GetConnectedClient()
        {
            var client = GetClientSnapshot();
            if (client == null || !client.IsConnected || Volatile.Read(ref _transportLost) != 0)
                throw new IndustrialConnectionException("TwinCAT ADS client is not connected.");
            return client;
        }

        private BeckhoffAdsClient GetClientSnapshot()
        {
            lock (_clientSync) { return _adsClient; }
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
                AdsTypeCodec.GetClrType(item.DataType);
                AdsTypeCodec.GetArguments(item.DataType, item.Length);
            }
        }

        private IEnumerable<IReadOnlyList<ReadRequest>> CreateReadChunks(IReadOnlyList<ReadRequest> requests)
        {
            var chunk = new List<ReadRequest>();
            var estimatedBytes = 0;
            foreach (var request in requests)
            {
                var estimate = AdsTypeCodec.EstimateSize(request.DataType, request.Length);
                if (chunk.Count > 0 && (chunk.Count >= _options.MaxBatchItems || estimatedBytes + estimate > _options.MaxBatchPayloadBytes))
                {
                    yield return chunk;
                    chunk = new List<ReadRequest>();
                    estimatedBytes = 0;
                }
                chunk.Add(request);
                estimatedBytes += estimate;
            }
            if (chunk.Count > 0) yield return chunk;
        }

        private IEnumerable<IReadOnlyList<WriteRequest>> CreateWriteChunks(IReadOnlyList<WriteRequest> requests)
        {
            var chunk = new List<WriteRequest>();
            var estimatedBytes = 0;
            foreach (var request in requests)
            {
                var estimate = AdsTypeCodec.EstimateSize(request.DataType, request.Length);
                if (chunk.Count > 0 && (chunk.Count >= _options.MaxBatchItems || estimatedBytes + estimate > _options.MaxBatchPayloadBytes))
                {
                    yield return chunk;
                    chunk = new List<WriteRequest>();
                    estimatedBytes = 0;
                }
                chunk.Add(request);
                estimatedBytes += estimate;
            }
            if (chunk.Count > 0) yield return chunk;
        }

        private static DataValue BadValue(ReadRequest request, string message)
        {
            return new DataValue(
                request.Address,
                request.DataType,
                null,
                null,
                QualityStatus.Bad,
                DateTimeOffset.UtcNow,
                message);
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

        private static string FormatAdsError(AdsErrorCode code)
        {
            return "ADS error " + code + " (0x" + ((int)code).ToString("X8", CultureInfo.InvariantCulture) + ").";
        }

        private static void EnsureSucceeded(ResultAds result, string operation)
        {
            if (result == null) throw new IndustrialProtocolException(operation + " returned no result.");
            if (!result.Succeeded) throw new IndustrialProtocolException(operation + " failed: " + FormatAdsError(result.ErrorCode));
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
            public uint Handle { get; set; }
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
                case DataType.SByte: return typeof(sbyte);
                case DataType.Char: return typeof(char);
                case DataType.Int16: return typeof(short);
                case DataType.UInt16: return typeof(ushort);
                case DataType.Int32: return typeof(int);
                case DataType.UInt32: return typeof(uint);
                case DataType.Int64: return typeof(long);
                case DataType.UInt64: return typeof(ulong);
                case DataType.Float: return typeof(float);
                case DataType.Double: return typeof(double);
                case DataType.String:
                case DataType.WString: return typeof(string);
                case DataType.Time: return typeof(TimeSpan);
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
                case DataType.WString:
                case DataType.ByteArray:
                    if (length == 0) throw new IndustrialDataConversionException("ADS string and byte-array lengths must be greater than zero.");
                    return new[] { (int)length };
                case DataType.S7String:
                    throw new IndustrialProtocolException("S7String is not an ADS data type; use DataType.String with the PLC STRING length.");
                default:
                    return null;
            }
        }

        public static int EstimateSize(DataType dataType, ushort length)
        {
            switch (dataType)
            {
                case DataType.Bool:
                case DataType.Byte:
                case DataType.SByte:
                case DataType.Char: return 1;
                case DataType.Int16:
                case DataType.UInt16: return 2;
                case DataType.Int32:
                case DataType.UInt32:
                case DataType.Float:
                case DataType.Time: return 4;
                case DataType.Int64:
                case DataType.UInt64:
                case DataType.Double: return 8;
                case DataType.String:
                case DataType.WString:
                case DataType.ByteArray: return Math.Max(1, (int)length) * (dataType == DataType.WString ? 2 : 1);
                default: return 256;
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
                    case DataType.SByte: return Convert.ToSByte(value, CultureInfo.InvariantCulture);
                    case DataType.Char:
                        if (value is char) return value;
                        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                        if (text.Length != 1) throw new InvalidCastException("ADS CHAR writes require a single character.");
                        return text[0];
                    case DataType.Int16: return Convert.ToInt16(value, CultureInfo.InvariantCulture);
                    case DataType.UInt16: return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                    case DataType.Int32: return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    case DataType.UInt32: return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                    case DataType.Int64: return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    case DataType.UInt64: return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                    case DataType.Float: return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    case DataType.Double: return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    case DataType.String:
                    case DataType.WString: return Convert.ToString(value, CultureInfo.InvariantCulture);
                    case DataType.Time:
                        if (value is TimeSpan) return value;
                        if (value is string timeText) return TimeSpan.Parse(timeText, CultureInfo.InvariantCulture);
                        return TimeSpan.FromMilliseconds(Convert.ToDouble(value, CultureInfo.InvariantCulture));
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
