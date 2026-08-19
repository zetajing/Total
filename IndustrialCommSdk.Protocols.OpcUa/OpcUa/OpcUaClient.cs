using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Polling;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using UaDataValue = Opc.Ua.DataValue;
using DataValue = IndustrialCommSdk.Abstractions.DataValue;
using ReadRequest = IndustrialCommSdk.Abstractions.ReadRequest;
using WriteRequest = IndustrialCommSdk.Abstractions.WriteRequest;
using UaMonitoredItem = Opc.Ua.Client.MonitoredItem;
using UaSubscription = Opc.Ua.Client.Subscription;
using UaMonitoredItemNotificationEventArgs = Opc.Ua.Client.MonitoredItemNotificationEventArgs;

namespace IndustrialCommSdk.Protocols.OpcUa
{
    public sealed class OpcUaClientOptions
    {
        public string DeviceId { get; set; }
        public string EndpointUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseSecurity { get; set; }
        public bool AutoAcceptUntrustedCertificates { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 10000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
        public int SessionTimeoutMilliseconds { get; set; } = 60000;
    }

    /// <summary>
    /// OPC UA client based on the OPC Foundation reference stack. Addresses are standard NodeId strings,
    /// for example ns=2;s=Machine/Temperature or ns=2;i=1001.
    /// </summary>
    public sealed class OpcUaClient : IndustrialClientBase, INativeSubscriptionClient
    {
        private const string NativeSubscriptionPrefix = "opcua:";
        private readonly OpcUaClientOptions _options;
        private readonly ConcurrentDictionary<string, NativeSubscriptionRegistration> _nativeSubscriptions =
            new ConcurrentDictionary<string, NativeSubscriptionRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _nativeSubscriptionGate = new SemaphoreSlim(1, 1);
        private ISession _session;

        public OpcUaClient(OpcUaClientOptions options, IIndustrialLogger logger = null,
            IPollingScheduler pollingScheduler = null)
            : base(GetDeviceId(options), ProtocolKind.OpcUa,
                pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance,
                options.OperationTimeoutMilliseconds)
        {
            _options = options;
            Uri endpoint;
            if (string.IsNullOrWhiteSpace(options.EndpointUrl) ||
                !Uri.TryCreate(options.EndpointUrl, UriKind.Absolute, out endpoint) ||
                !string.Equals(endpoint.Scheme, "opc.tcp", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A valid opc.tcp endpoint URL is required.", nameof(options));
            if (options.ConnectTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options.ConnectTimeoutMilliseconds));
            if (options.OperationTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options.OperationTimeoutMilliseconds));
            if (options.SessionTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options.SessionTimeoutMilliseconds));
        }

        private static string GetDeviceId(OpcUaClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceId)) throw new ArgumentException("Device ID is required.", nameof(options));
            return options.DeviceId;
        }

        public override bool IsConnected
        {
            get
            {
                var session = Volatile.Read(ref _session);
                return session != null && session.Connected;
            }
        }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            await _nativeSubscriptionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await CloseSessionAsync(cancellationToken).ConfigureAwait(false);
                var configuration = await CreateConfigurationAsync(cancellationToken).ConfigureAwait(false);
                var selected = await CoreClientUtils.SelectEndpointAsync(
                    configuration,
                    _options.EndpointUrl,
                    _options.UseSecurity,
                    _options.ConnectTimeoutMilliseconds,
                    null,
                    cancellationToken).ConfigureAwait(false);
                var endpoint = new ConfiguredEndpoint(null, selected, EndpointConfiguration.Create(configuration));
                IUserIdentity identity = string.IsNullOrWhiteSpace(_options.Username)
                    ? (IUserIdentity)new UserIdentity(new AnonymousIdentityToken())
                    : new UserIdentity(_options.Username, Encoding.UTF8.GetBytes(_options.Password ?? string.Empty));
                var session = await new DefaultSessionFactory(null).CreateAsync(configuration, endpoint, false, false,
                    "IndustrialCommSdk-" + DeviceId, (uint)_options.SessionTimeoutMilliseconds,
                    identity, null, cancellationToken).ConfigureAwait(false);
                session.KeepAlive += OnSessionKeepAlive;
                _session = session;
                await RestoreNativeSubscriptionsAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await CloseSessionAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await CloseSessionAsync(CancellationToken.None).ConfigureAwait(false);
                throw new IndustrialConnectionException("Failed to connect OPC UA endpoint.", ex);
            }
            finally
            {
                _nativeSubscriptionGate.Release();
            }
        }

        protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _nativeSubscriptionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await CloseSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The session has already been detached and disposed. Treat the committed
                // disconnect as complete so the base class can publish Disconnected state.
            }
            finally
            {
                _nativeSubscriptionGate.Release();
            }
        }

        public async Task<string> SubscribeNativeAsync(
            SubscriptionRequest request,
            EventHandler<SubscriptionEvent> handler,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!string.Equals(DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Subscription device ID does not match the client device ID.", nameof(request));
            if (request.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Subscription must contain at least one read request.", nameof(request));
            foreach (var item in request.Items)
            {
                if (!string.Equals(DeviceId, item.DeviceId, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Subscription item device ID does not match the client device ID.", nameof(request));
            }

            var subscriptionId = NativeSubscriptionPrefix + request.SubscriptionKey;
            var registration = new NativeSubscriptionRegistration(subscriptionId, request, handler);
            if (!_nativeSubscriptions.TryAdd(subscriptionId, registration))
                throw new InvalidOperationException("Subscription '" + request.SubscriptionKey + "' already exists.");

            await _nativeSubscriptionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var session = GetConnectedSession();
                await InstallNativeSubscriptionAsync(session, registration, cancellationToken).ConfigureAwait(false);
                Logger.Info(string.Format(
                    "OPC UA native subscription started | Key={0} | Device={1} | Items={2} | Interval={3}ms",
                    request.SubscriptionKey,
                    DeviceId,
                    request.Items.Count,
                    request.Interval.TotalMilliseconds));
                return subscriptionId;
            }
            catch
            {
                NativeSubscriptionRegistration ignored;
                _nativeSubscriptions.TryRemove(subscriptionId, out ignored);
                DisposeSubscription(registration.DetachSubscription());
                throw;
            }
            finally
            {
                _nativeSubscriptionGate.Release();
            }
        }

        public async Task<bool> TryUnsubscribeNativeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId) ||
                !subscriptionId.StartsWith(NativeSubscriptionPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            await _nativeSubscriptionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                NativeSubscriptionRegistration registration;
                if (!_nativeSubscriptions.TryRemove(subscriptionId, out registration))
                    return true;

                var subscription = registration.DetachSubscription();
                var session = Volatile.Read(ref _session);
                try
                {
                    if (session != null && subscription != null && session.Connected)
                        await session.RemoveSubscriptionAsync(subscription, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Warn("OPC UA native subscription removal failed; local registration was removed. " + ex.Message);
                }
                finally
                {
                    DisposeSubscription(subscription);
                }

                Logger.Info("OPC UA native subscription stopped | Key=" + subscriptionId);
                return true;
            }
            finally
            {
                _nativeSubscriptionGate.Release();
            }
        }

        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            var result = await ReadManyCoreAsync(new[] { request }, cancellationToken).ConfigureAwait(false);
            return result.Values[0];
        }

        protected override async Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests,
            CancellationToken cancellationToken)
        {
            var session = GetConnectedSession();
            var list = requests.ToList();
            var nodes = new ReadValueIdCollection(list.Select(x => new ReadValueId
            {
                NodeId = ParseNodeId(x.Address), AttributeId = Attributes.Value
            }));
            var response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                nodes,
                cancellationToken).ConfigureAwait(false);
            var values = response.Results;
            var diagnostics = response.DiagnosticInfos;
            ClientBase.ValidateResponse(values, nodes);
            ClientBase.ValidateDiagnosticInfos(diagnostics, nodes);
            var result = new List<DataValue>(list.Count);
            for (var i = 0; i < list.Count; i++) result.Add(ConvertValue(list[i], values[i]));
            return new BatchReadResult(result);
        }

        protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            return WriteManyCoreAsync(new[] { request }, cancellationToken);
        }

        protected override async Task WriteManyCoreAsync(
            IReadOnlyCollection<WriteRequest> requests,
            CancellationToken cancellationToken)
        {
            var session = GetConnectedSession();
            var writes = new WriteValueCollection(requests.Select(x => new WriteValue
            {
                NodeId = ParseNodeId(x.Address), AttributeId = Attributes.Value,
                Value = new UaDataValue(new Variant(ConvertForWrite(x)))
            }));
            var response = await session.WriteAsync(null, writes, cancellationToken).ConfigureAwait(false);
            var results = response.Results;
            var diagnostics = response.DiagnosticInfos;
            ClientBase.ValidateResponse(results, writes);
            ClientBase.ValidateDiagnosticInfos(diagnostics, writes);
            for (var i = 0; i < results.Count; i++)
                if (StatusCode.IsBad(results[i])) throw new IndustrialProtocolException("OPC UA write failed: " + results[i]);
        }

        public static NodeId ParseNodeId(string address)
        {
            try { return NodeId.Parse(address); }
            catch (Exception ex) { throw new IndustrialAddressParseException("Invalid OPC UA NodeId: " + address, ex); }
        }

        internal static object ConvertForWrite(WriteRequest request)
        {
            try
            {
                switch (request.DataType)
                {
                    case DataType.Bool: return Convert.ToBoolean(request.Value, CultureInfo.InvariantCulture);
                    case DataType.Byte: return Convert.ToByte(request.Value, CultureInfo.InvariantCulture);
                    case DataType.Char: return Convert.ToChar(request.Value, CultureInfo.InvariantCulture).ToString();
                    case DataType.Int16: return Convert.ToInt16(request.Value, CultureInfo.InvariantCulture);
                    case DataType.UInt16: return Convert.ToUInt16(request.Value, CultureInfo.InvariantCulture);
                    case DataType.Int32: return Convert.ToInt32(request.Value, CultureInfo.InvariantCulture);
                    case DataType.UInt32: return Convert.ToUInt32(request.Value, CultureInfo.InvariantCulture);
                    case DataType.Float: return Convert.ToSingle(request.Value, CultureInfo.InvariantCulture);
                    case DataType.Double: return Convert.ToDouble(request.Value, CultureInfo.InvariantCulture);
                    case DataType.String: return Convert.ToString(request.Value, CultureInfo.InvariantCulture);
                    case DataType.ByteArray:
                        var bytes = request.Value as byte[];
                        if (bytes == null) throw new InvalidCastException("ByteArray requires byte[].");
                        return bytes;
                    default: throw new NotSupportedException("Unsupported OPC UA data type: " + request.DataType);
                }
            }
            catch (Exception ex) when (!(ex is IndustrialDataConversionException))
            { throw new IndustrialDataConversionException("Cannot convert OPC UA write value to " + request.DataType + ".", ex); }
        }

        private static DataValue ConvertValue(ReadRequest request, UaDataValue source)
        {
            var timestamp = source.SourceTimestamp == DateTime.MinValue ? DateTimeOffset.UtcNow : new DateTimeOffset(source.SourceTimestamp);
            if (StatusCode.IsBad(source.StatusCode))
                return new DataValue(request.Address, request.DataType, null, null, QualityStatus.Bad, timestamp, source.StatusCode.ToString());
            try
            {
                object value;
                var raw = source.Value as byte[];
                switch (request.DataType)
                {
                    case DataType.Bool: value = Convert.ToBoolean(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.Byte: value = Convert.ToByte(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.Char: value = Convert.ToChar(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.Int16: value = Convert.ToInt16(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.UInt16: value = Convert.ToUInt16(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.Int32: value = Convert.ToInt32(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.UInt32: value = Convert.ToUInt32(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.Float: value = Convert.ToSingle(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.Double: value = Convert.ToDouble(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.String: value = Convert.ToString(source.Value, CultureInfo.InvariantCulture); break;
                    case DataType.ByteArray: value = raw ?? throw new InvalidCastException("OPC UA value is not a byte array."); break;
                    default: throw new NotSupportedException();
                }
                return new DataValue(request.Address, request.DataType, value, raw, QualityStatus.Good, timestamp, null);
            }
            catch (Exception ex)
            { return new DataValue(request.Address, request.DataType, null, null, QualityStatus.Bad, timestamp, ex.Message); }
        }

        private async Task<ApplicationConfiguration> CreateConfigurationAsync(CancellationToken cancellationToken)
        {
            var config = new ApplicationConfiguration
            {
                ApplicationName = "IndustrialCommSdk",
                ApplicationUri = "urn:" + Utils.GetHostName() + ":IndustrialCommSdk",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = "%LocalApplicationData%/IndustrialCommSdk/pki/own", SubjectName = "CN=IndustrialCommSdk" },
                    TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = "%LocalApplicationData%/IndustrialCommSdk/pki/trusted" },
                    RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = "%LocalApplicationData%/IndustrialCommSdk/pki/rejected" },
                    AutoAcceptUntrustedCertificates = _options.AutoAcceptUntrustedCertificates
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = _options.OperationTimeoutMilliseconds },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = _options.SessionTimeoutMilliseconds }
            };
            await config.ValidateAsync(ApplicationType.Client, cancellationToken).ConfigureAwait(false);
            return config;
        }

        private ISession GetConnectedSession()
        {
            var session = Volatile.Read(ref _session);
            if (session == null || !session.Connected)
                throw new IndustrialConnectionException("OPC UA client is not connected.");
            return session;
        }

        protected override void OnOperationTimeout() { AbortSession(); }

        protected override void DisposeCore()
        {
            AbortSession();
            foreach (var pair in _nativeSubscriptions)
            {
                NativeSubscriptionRegistration removed;
                if (_nativeSubscriptions.TryRemove(pair.Key, out removed))
                    DisposeSubscription(removed.DetachSubscription());
            }
        }

        private async Task RestoreNativeSubscriptionsAsync(ISession session, CancellationToken cancellationToken)
        {
            foreach (var registration in _nativeSubscriptions.Values.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await InstallNativeSubscriptionAsync(session, registration, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task InstallNativeSubscriptionAsync(
            ISession session,
            NativeSubscriptionRegistration registration,
            CancellationToken cancellationToken)
        {
            var subscription = new UaSubscription(null, new SubscriptionOptions());
            subscription.DisplayName = "IndustrialCommSdk-" + registration.Id;
            subscription.PublishingInterval = (int)Math.Min(int.MaxValue,
                Math.Max(50d, registration.Request.Interval.TotalMilliseconds));
            subscription.KeepAliveCount = 10;
            subscription.LifetimeCount = 30;
            subscription.TimestampsToReturn = TimestampsToReturn.Both;
            subscription.SequentialPublishing = true;

            var items = new List<NativeSubscriptionItem>();
            try
            {
                foreach (var request in registration.Request.Items)
                {
                    var monitoredItem = new UaMonitoredItem(null, new MonitoredItemOptions());
                    monitoredItem.DisplayName = request.Address;
                    monitoredItem.StartNodeId = ParseNodeId(request.Address);
                    monitoredItem.AttributeId = Attributes.Value;
                    monitoredItem.SamplingInterval = (int)Math.Min(int.MaxValue,
                        Math.Max(0d, registration.Request.Interval.TotalMilliseconds));
                    monitoredItem.QueueSize = 1;
                    monitoredItem.DiscardOldest = true;
                    monitoredItem.Notification += (item, args) => OnMonitoredItemNotification(registration, item, args);
                    subscription.AddItem(monitoredItem);
                    items.Add(new NativeSubscriptionItem(monitoredItem, request));
                }

                session.AddSubscription(subscription);
                registration.AttachSubscription(subscription, items);
                await subscription.CreateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                registration.DetachSubscription(subscription);
                try { await session.RemoveSubscriptionAsync(subscription, CancellationToken.None).ConfigureAwait(false); }
                catch { }
                DisposeSubscription(subscription);
                throw;
            }
        }

        private void OnMonitoredItemNotification(
            NativeSubscriptionRegistration registration,
            UaMonitoredItem monitoredItem,
            UaMonitoredItemNotificationEventArgs args)
        {
            try
            {
                ReadRequest request;
                if (!registration.TryGetRequest(monitoredItem, out request))
                    return;

                var values = monitoredItem.DequeueValues();
                if (values == null)
                    return;

                foreach (var value in values)
                {
                    var handler = registration.Handler;
                    if (handler == null)
                        continue;

                    var converted = ConvertValue(request, value);
                    IReadOnlyList<DataValue> snapshot;
                    if (!registration.TryBuildSnapshot(monitoredItem, converted, out snapshot))
                        continue;

                    handler(this, new SubscriptionEvent(
                        registration.Id,
                        snapshot,
                        converted.Timestamp));
                }
            }
            catch (Exception ex)
            {
                Logger.Error("OPC UA native subscription handler failed | Key=" + registration.Id, ex);
            }
        }

        private void OnSessionKeepAlive(ISession sender, KeepAliveEventArgs e)
        {
            if (e == null || e.Status == null || !StatusCode.IsBad(e.Status.StatusCode))
                return;

            Logger.Warn("OPC UA KeepAlive failed; detaching the session so the host reconnect loop can rebuild it. " + e.Status);
            AbortSession();
        }

        private async Task CloseSessionAsync(CancellationToken cancellationToken)
        {
            var session = Interlocked.Exchange(ref _session, null);
            DetachNativeSubscriptions();
            if (session == null) return;
            session.KeepAlive -= OnSessionKeepAlive;
            try
            {
                await session.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Closing is best effort; Dispose below still releases the transport resources.
            }
            finally
            {
                try { session.Dispose(); }
                catch (Exception ex) { Logger.Error("OPC UA session disposal failed.", ex); }
            }
        }

        private void AbortSession()
        {
            var session = Interlocked.Exchange(ref _session, null);
            DetachNativeSubscriptions();
            if (session == null) return;
            session.KeepAlive -= OnSessionKeepAlive;
            try { session.Dispose(); } catch { }
        }

        private void DetachNativeSubscriptions()
        {
            foreach (var registration in _nativeSubscriptions.Values)
                DisposeSubscription(registration.DetachSubscription());
        }

        private static void DisposeSubscription(UaSubscription subscription)
        {
            if (subscription == null) return;
            try { subscription.Dispose(); }
            catch { }
        }

        private sealed class NativeSubscriptionRegistration
        {
            private readonly object _sync = new object();
            private UaSubscription _subscription;
            private List<NativeSubscriptionItem> _items = new List<NativeSubscriptionItem>();
            private Dictionary<UaMonitoredItem, ReadRequest> _requestsByItem =
                new Dictionary<UaMonitoredItem, ReadRequest>();
            private Dictionary<UaMonitoredItem, DataValue> _latestValues =
                new Dictionary<UaMonitoredItem, DataValue>();

            public NativeSubscriptionRegistration(
                string id,
                SubscriptionRequest request,
                EventHandler<SubscriptionEvent> handler)
            {
                Id = id;
                Request = request;
                Handler = handler;
            }

            public string Id { get; private set; }
            public SubscriptionRequest Request { get; private set; }
            public EventHandler<SubscriptionEvent> Handler { get; private set; }

            public void AttachSubscription(
                UaSubscription subscription,
                List<NativeSubscriptionItem> items)
            {
                lock (_sync)
                {
                    _subscription = subscription;
                    _items = items;
                    _requestsByItem = items.ToDictionary(item => item.MonitoredItem, item => item.Request);
                    _latestValues = new Dictionary<UaMonitoredItem, DataValue>();
                }
            }

            public UaSubscription DetachSubscription(UaSubscription expected = null)
            {
                lock (_sync)
                {
                    if (expected != null && !ReferenceEquals(_subscription, expected))
                        return null;

                    var subscription = _subscription;
                    _subscription = null;
                    _items = new List<NativeSubscriptionItem>();
                    _requestsByItem = new Dictionary<UaMonitoredItem, ReadRequest>();
                    _latestValues = new Dictionary<UaMonitoredItem, DataValue>();
                    return subscription;
                }
            }

            public bool TryGetRequest(UaMonitoredItem item, out ReadRequest request)
            {
                lock (_sync)
                    return _requestsByItem.TryGetValue(item, out request);
            }

            public bool TryBuildSnapshot(
                UaMonitoredItem item,
                DataValue value,
                out IReadOnlyList<DataValue> snapshot)
            {
                lock (_sync)
                {
                    if (!_requestsByItem.ContainsKey(item))
                    {
                        snapshot = null;
                        return false;
                    }

                    _latestValues[item] = value;
                    if (_latestValues.Count < _items.Count)
                    {
                        snapshot = null;
                        return false;
                    }

                    var ordered = new List<DataValue>(_items.Count);
                    foreach (var entry in _items)
                        ordered.Add(_latestValues[entry.MonitoredItem]);

                    snapshot = ordered;
                    return true;
                }
            }
        }

        private sealed class NativeSubscriptionItem
        {
            public NativeSubscriptionItem(UaMonitoredItem monitoredItem, ReadRequest request)
            {
                MonitoredItem = monitoredItem;
                Request = request;
            }

            public UaMonitoredItem MonitoredItem { get; private set; }
            public ReadRequest Request { get; private set; }
        }
    }
}
