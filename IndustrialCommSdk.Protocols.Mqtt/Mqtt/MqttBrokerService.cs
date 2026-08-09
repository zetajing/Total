using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Diagnostics;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace IndustrialCommSdk.Protocols.Mqtt
{
    public sealed class MqttBrokerOptions
    {
        public string BindAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 1883;
        /// <summary>
        /// Adds the encrypted endpoint. On loopback this intentionally keeps the plain 1883 endpoint and also opens
        /// the TLS endpoint (8883 by default); a non-loopback bind exposes only the TLS endpoint.
        /// </summary>
        public bool UseTls { get; set; }
        public int TlsPort { get; set; } = 8883;
        public X509Certificate2 ServerCertificate { get; set; }
        public SslProtocols? TlsProtocols { get; set; }
        public bool AllowAnonymous { get; set; }
        public Dictionary<string, string> Credentials { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public Func<string, string, bool> CredentialValidator { get; set; }
        /// <summary>Optional authorization callback with username, client ID and publication topic.</summary>
        public Func<string, string, string, bool> PublishAuthorizer { get; set; }
        /// <summary>Optional authorization callback with username, client ID and subscription topic filter.</summary>
        public Func<string, string, string, bool> SubscribeAuthorizer { get; set; }
        public bool EnablePersistentSessions { get; set; } = true;
        public int MaxPendingMessagesPerClient { get; set; } = 1000;
        public string ServerClientId { get; set; } = "IndustrialCommSdk.Broker";
    }

    public sealed class MqttBrokerClientSession
    {
        public MqttBrokerClientSession(string clientId, string username, string endpoint, DateTimeOffset connectedUtc, string protocolVersion)
        {
            ClientId = clientId;
            Username = username;
            Endpoint = endpoint;
            ConnectedUtc = connectedUtc;
            ProtocolVersion = protocolVersion;
        }

        public string ClientId { get; private set; }
        public string Username { get; private set; }
        public string Endpoint { get; private set; }
        public DateTimeOffset ConnectedUtc { get; private set; }
        public string ProtocolVersion { get; private set; }
    }

    public sealed class MqttBrokerClientEventArgs : EventArgs
    {
        public MqttBrokerClientEventArgs(MqttBrokerClientSession session, string reason)
        {
            Session = session;
            Reason = reason;
            TimestampUtc = DateTimeOffset.UtcNow;
        }

        public MqttBrokerClientSession Session { get; private set; }
        public string Reason { get; private set; }
        public DateTimeOffset TimestampUtc { get; private set; }
    }

    public sealed class MqttBrokerMessageReceivedEventArgs : EventArgs
    {
        public MqttBrokerMessageReceivedEventArgs(string clientId, string topic, byte[] payload, int qualityOfService, bool retain)
        {
            ClientId = clientId;
            Topic = topic;
            Payload = payload == null ? new byte[0] : (byte[])payload.Clone();
            QualityOfService = qualityOfService;
            Retain = retain;
            ReceivedUtc = DateTimeOffset.UtcNow;
        }

        public string ClientId { get; private set; }
        public string Topic { get; private set; }
        public byte[] Payload { get; private set; }
        public int QualityOfService { get; private set; }
        public bool Retain { get; private set; }
        public DateTimeOffset ReceivedUtc { get; private set; }
    }

    public interface IMqttBrokerService : IDisposable
    {
        MqttBrokerOptions Options { get; }
        bool IsRunning { get; }
        event EventHandler Started;
        event EventHandler Stopped;
        event EventHandler<MqttBrokerClientEventArgs> ClientConnected;
        event EventHandler<MqttBrokerClientEventArgs> ClientDisconnected;
        event EventHandler<MqttBrokerMessageReceivedEventArgs> MessageReceived;
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task PublishAsync(string topic, byte[] payload, int qualityOfService, bool retain, CancellationToken cancellationToken);
        Task<IReadOnlyList<MqttBrokerClientSession>> GetClientsAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// In-process MQTT broker based on MQTTnet. Plain MQTT is limited to loopback; any non-loopback bind requires TLS.
    /// </summary>
    public sealed class MqttBrokerService : IMqttBrokerService
    {
        private const string InternalPublishMarker = "IndustrialCommSdk.Mqtt.InternalPublish";
        private readonly MqttBrokerOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, MqttBrokerClientSession> _sessions =
            new ConcurrentDictionary<string, MqttBrokerClientSession>(StringComparer.Ordinal);
        private MqttServer _server;
        private IReadOnlyDictionary<string, string> _credentials = new Dictionary<string, string>();
        private int _disposeRequested;

        public MqttBrokerService(MqttBrokerOptions options, IIndustrialLogger logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public MqttBrokerOptions Options { get { return _options; } }
        public bool IsRunning { get { var server = _server; return server != null && server.IsStarted; } }

        public event EventHandler Started;
        public event EventHandler Stopped;
        public event EventHandler<MqttBrokerClientEventArgs> ClientConnected;
        public event EventHandler<MqttBrokerClientEventArgs> ClientDisconnected;
        public event EventHandler<MqttBrokerMessageReceivedEventArgs> MessageReceived;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (IsRunning) return;

                IPAddress bindAddress;
                var serverOptions = BuildServerOptions(out bindAddress);
                _credentials = SnapshotCredentials(_options.Credentials);
                var server = new MqttFactory().CreateMqttServer(serverOptions);
                AttachEvents(server);
                try
                {
                    await server.StartAsync().ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await server.StopAsync().ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    _server = server;
                    _logger.Info(string.Format("MQTT broker started | Bind={0} | PlainPort={1} | TlsPort={2} | TLS={3}",
                        bindAddress,
                        IPAddress.IsLoopback(bindAddress) ? _options.Port.ToString() : "disabled",
                        _options.UseTls ? _options.TlsPort.ToString() : "disabled",
                        _options.UseTls));
                    RaiseSimpleEvent(Started, "MQTT broker started event handler failed.");
                }
                catch
                {
                    DetachEvents(server);
                    server.Dispose();
                    throw;
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task PublishAsync(string topic, byte[] payload, int qualityOfService, bool retain, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(topic)) throw new ArgumentException("MQTT topic is required.", nameof(topic));
            if (qualityOfService < 0 || qualityOfService > 2) throw new ArgumentOutOfRangeException(nameof(qualityOfService));
            cancellationToken.ThrowIfCancellationRequested();
            var server = _server;
            if (server == null || !server.IsStarted) throw new InvalidOperationException("MQTT broker is not running.");

            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload ?? new byte[0])
                .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qualityOfService)
                .WithRetainFlag(retain)
                .Build();
            var injectedMessage = new InjectedMqttApplicationMessage(applicationMessage)
            {
                SenderClientId = string.IsNullOrWhiteSpace(_options.ServerClientId) ? "IndustrialCommSdk.Broker" : _options.ServerClientId,
                CustomSessionItems = new Hashtable { [InternalPublishMarker] = true },
            };
            await server.InjectApplicationMessage(injectedMessage, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<MqttBrokerClientSession>> GetClientsAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var server = _server;
            if (server == null || !server.IsStarted) return new MqttBrokerClientSession[0];
            var clients = await server.GetClientsAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return clients.Select(client =>
            {
                MqttBrokerClientSession tracked;
                _sessions.TryGetValue(client.Id, out tracked);
                return new MqttBrokerClientSession(
                    client.Id,
                    tracked == null ? null : tracked.Username,
                    client.Endpoint,
                    ToDateTimeOffset(client.ConnectedTimestamp),
                    client.ProtocolVersion.ToString());
            }).ToArray();
        }

        private MqttServerOptions BuildServerOptions(out IPAddress bindAddress)
        {
            if (!IPAddress.TryParse(_options.BindAddress, out bindAddress))
                throw new ArgumentException("MQTT broker BindAddress must be a valid IP address.", nameof(_options));
            if (_options.Port < 1 || _options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(_options.Port));
            if (_options.TlsPort < 1 || _options.TlsPort > 65535) throw new ArgumentOutOfRangeException(nameof(_options.TlsPort));
            if (_options.MaxPendingMessagesPerClient <= 0) throw new ArgumentOutOfRangeException(nameof(_options.MaxPendingMessagesPerClient));

            var isLoopback = IPAddress.IsLoopback(bindAddress);
            if (!isLoopback && !_options.UseTls)
                throw new InvalidOperationException("A non-loopback MQTT broker endpoint requires TLS.");
            if (_options.UseTls && (_options.ServerCertificate == null || !_options.ServerCertificate.HasPrivateKey))
                throw new InvalidOperationException("A TLS MQTT broker endpoint requires a certificate with a private key.");

            var builder = new MqttServerOptionsBuilder()
                .WithPersistentSessions(_options.EnablePersistentSessions)
                .WithMaxPendingMessagesPerClient(_options.MaxPendingMessagesPerClient)
                .WithKeepAlive();
            var ipv4Address = bindAddress.AddressFamily == AddressFamily.InterNetwork ? bindAddress : IPAddress.Loopback;
            var ipv6Address = bindAddress.AddressFamily == AddressFamily.InterNetworkV6 ? bindAddress : IPAddress.IPv6Loopback;

            if (isLoopback)
            {
                builder.WithDefaultEndpoint()
                    .WithDefaultEndpointPort(_options.Port)
                    .WithDefaultEndpointBoundIPAddress(ipv4Address)
                    .WithDefaultEndpointBoundIPV6Address(ipv6Address);
            }
            else
            {
                builder.WithoutDefaultEndpoint();
            }

            if (_options.UseTls)
            {
                builder.WithEncryptedEndpoint()
                    .WithEncryptedEndpointPort(_options.TlsPort)
                    .WithEncryptedEndpointBoundIPAddress(ipv4Address)
                    .WithEncryptedEndpointBoundIPV6Address(ipv6Address)
                    .WithEncryptionCertificate(_options.ServerCertificate);
                if (_options.TlsProtocols.HasValue) builder.WithEncryptionSslProtocol(_options.TlsProtocols.Value);
            }
            else
            {
                builder.WithoutEncryptedEndpoint();
            }

            return builder.Build();
        }

        private Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
        {
            try
            {
                if (_options.AllowAnonymous && string.IsNullOrEmpty(args.UserName) && string.IsNullOrEmpty(args.Password))
                {
                    args.ReasonCode = MqttConnectReasonCode.Success;
                    return Task.CompletedTask;
                }

                var isValid = _options.CredentialValidator != null
                    ? _options.CredentialValidator(args.UserName, args.Password)
                    : ValidateStoredCredential(args.UserName, args.Password);
                args.ReasonCode = isValid ? MqttConnectReasonCode.Success : MqttConnectReasonCode.BadUserNameOrPassword;
                if (!isValid) args.ReasonString = "Invalid MQTT username or password.";
            }
            catch (Exception ex)
            {
                args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
                args.ReasonString = "MQTT credential validation failed.";
                _logger.Error("MQTT credential validator failed.", ex);
            }
            return Task.CompletedTask;
        }

        private Task OnClientConnectedAsync(ClientConnectedEventArgs args)
        {
            var session = new MqttBrokerClientSession(args.ClientId, args.UserName, args.Endpoint,
                DateTimeOffset.UtcNow, args.ProtocolVersion.ToString());
            _sessions[args.ClientId] = session;
            RaiseClientEvent(ClientConnected, new MqttBrokerClientEventArgs(session, null), "MQTT client-connected event handler failed.");
            return Task.CompletedTask;
        }

        private Task OnClientDisconnectedAsync(ClientDisconnectedEventArgs args)
        {
            MqttBrokerClientSession session;
            if (!_sessions.TryRemove(args.ClientId, out session))
                session = new MqttBrokerClientSession(args.ClientId, null, args.Endpoint, DateTimeOffset.UtcNow, null);
            var reason = string.IsNullOrWhiteSpace(args.ReasonString)
                ? (args.ReasonCode.HasValue ? args.ReasonCode.Value.ToString() : args.DisconnectType.ToString())
                : args.ReasonString;
            RaiseClientEvent(ClientDisconnected, new MqttBrokerClientEventArgs(session, reason), "MQTT client-disconnected event handler failed.");
            return Task.CompletedTask;
        }

        private Task OnInterceptingPublishAsync(InterceptingPublishEventArgs args)
        {
            var message = args.ApplicationMessage;
            if (!IsInternalPublish(args.SessionItems) && _options.PublishAuthorizer != null)
            {
                var username = GetSessionUsername(args.ClientId);
                try
                {
                    if (!_options.PublishAuthorizer(username, args.ClientId, message.Topic))
                    {
                        args.ProcessPublish = false;
                        args.Response.ReasonCode = MqttPubAckReasonCode.NotAuthorized;
                        args.Response.ReasonString = "Publishing to this topic is not authorized.";
                        return Task.CompletedTask;
                    }
                }
                catch (Exception ex)
                {
                    args.ProcessPublish = false;
                    args.Response.ReasonCode = MqttPubAckReasonCode.NotAuthorized;
                    args.Response.ReasonString = "MQTT publication authorization failed.";
                    _logger.Error("MQTT publication authorizer failed.", ex);
                    return Task.CompletedTask;
                }
            }

            RaiseMessageEvent(new MqttBrokerMessageReceivedEventArgs(
                args.ClientId,
                message.Topic,
                message.PayloadSegment.ToArray(),
                (int)message.QualityOfServiceLevel,
                message.Retain));
            return Task.CompletedTask;
        }

        private Task OnInterceptingSubscriptionAsync(InterceptingSubscriptionEventArgs args)
        {
            if (_options.SubscribeAuthorizer == null) return Task.CompletedTask;
            var username = GetSessionUsername(args.ClientId);
            try
            {
                if (_options.SubscribeAuthorizer(username, args.ClientId, args.TopicFilter.Topic)) return Task.CompletedTask;
                args.ProcessSubscription = false;
                args.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
                args.Response.ReasonString = "Subscribing to this topic filter is not authorized.";
                args.ReasonString = args.Response.ReasonString;
            }
            catch (Exception ex)
            {
                args.ProcessSubscription = false;
                args.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
                args.Response.ReasonString = "MQTT subscription authorization failed.";
                args.ReasonString = args.Response.ReasonString;
                _logger.Error("MQTT subscription authorizer failed.", ex);
            }
            return Task.CompletedTask;
        }

        private string GetSessionUsername(string clientId)
        {
            MqttBrokerClientSession session;
            return clientId != null && _sessions.TryGetValue(clientId, out session) ? session.Username : null;
        }

        private static bool IsInternalPublish(IDictionary sessionItems)
        {
            return sessionItems != null && sessionItems.Contains(InternalPublishMarker) && Equals(sessionItems[InternalPublishMarker], true);
        }

        private bool ValidateStoredCredential(string username, string password)
        {
            if (string.IsNullOrEmpty(username)) return false;
            string expectedPassword;
            return _credentials.TryGetValue(username, out expectedPassword) && FixedTimeEquals(expectedPassword, password);
        }

        private static bool FixedTimeEquals(string expected, string actual)
        {
            using (var hash = SHA256.Create())
            {
                var expectedHash = hash.ComputeHash(Encoding.UTF8.GetBytes(expected ?? string.Empty));
                var actualHash = hash.ComputeHash(Encoding.UTF8.GetBytes(actual ?? string.Empty));
                var difference = 0;
                for (var i = 0; i < expectedHash.Length; i++) difference |= expectedHash[i] ^ actualHash[i];
                return difference == 0;
            }
        }

        private static IReadOnlyDictionary<string, string> SnapshotCredentials(IDictionary<string, string> credentials)
        {
            if (credentials == null) return new Dictionary<string, string>(StringComparer.Ordinal);
            return new Dictionary<string, string>(credentials, StringComparer.Ordinal);
        }

        private static DateTimeOffset ToDateTimeOffset(DateTime value)
        {
            if (value.Kind == DateTimeKind.Unspecified) value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return new DateTimeOffset(value.ToUniversalTime());
        }

        private void AttachEvents(MqttServer server)
        {
            server.ValidatingConnectionAsync += ValidateConnectionAsync;
            server.ClientConnectedAsync += OnClientConnectedAsync;
            server.ClientDisconnectedAsync += OnClientDisconnectedAsync;
            server.InterceptingPublishAsync += OnInterceptingPublishAsync;
            server.InterceptingSubscriptionAsync += OnInterceptingSubscriptionAsync;
        }

        private void DetachEvents(MqttServer server)
        {
            server.ValidatingConnectionAsync -= ValidateConnectionAsync;
            server.ClientConnectedAsync -= OnClientConnectedAsync;
            server.ClientDisconnectedAsync -= OnClientDisconnectedAsync;
            server.InterceptingPublishAsync -= OnInterceptingPublishAsync;
            server.InterceptingSubscriptionAsync -= OnInterceptingSubscriptionAsync;
        }

        private async Task StopCoreAsync()
        {
            var server = _server;
            if (server == null) return;
            _server = null;
            try
            {
                if (server.IsStarted) await server.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                DetachEvents(server);
                server.Dispose();
                _sessions.Clear();
                _logger.Info("MQTT broker stopped.");
                RaiseSimpleEvent(Stopped, "MQTT broker stopped event handler failed.");
            }
        }

        private void RaiseSimpleEvent(EventHandler handler, string errorMessage)
        {
            if (handler == null) return;
            foreach (EventHandler subscriber in handler.GetInvocationList())
            {
                try { subscriber(this, EventArgs.Empty); }
                catch (Exception ex) { _logger.Error(errorMessage, ex); }
            }
        }

        private void RaiseClientEvent(EventHandler<MqttBrokerClientEventArgs> handler, MqttBrokerClientEventArgs args, string errorMessage)
        {
            if (handler == null) return;
            foreach (EventHandler<MqttBrokerClientEventArgs> subscriber in handler.GetInvocationList())
            {
                try { subscriber(this, args); }
                catch (Exception ex) { _logger.Error(errorMessage, ex); }
            }
        }

        private void RaiseMessageEvent(MqttBrokerMessageReceivedEventArgs args)
        {
            var handler = MessageReceived;
            if (handler == null) return;
            foreach (EventHandler<MqttBrokerMessageReceivedEventArgs> subscriber in handler.GetInvocationList())
            {
                try { subscriber(this, args); }
                catch (Exception ex) { _logger.Error("MQTT broker message event handler failed.", ex); }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposeRequested) != 0) throw new ObjectDisposedException(GetType().FullName);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
            _lifecycleLock.Wait();
            try
            {
                StopCoreAsync().GetAwaiter().GetResult();
            }
            finally
            {
                _lifecycleLock.Release();
                _lifecycleLock.Dispose();
            }
        }
    }
}
