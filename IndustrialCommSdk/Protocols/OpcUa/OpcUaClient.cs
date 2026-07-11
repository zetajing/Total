using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using UaDataValue = Opc.Ua.DataValue;
using DataValue = IndustrialCommSdk.Abstractions.DataValue;
using ReadRequest = IndustrialCommSdk.Abstractions.ReadRequest;
using WriteRequest = IndustrialCommSdk.Abstractions.WriteRequest;

namespace IndustrialCommSdk.Protocols.OpcUa
{
    public sealed class OpcUaClientOptions
    {
        public string DeviceId { get; set; }
        public string EndpointUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseSecurity { get; set; }
        public bool AutoAcceptUntrustedCertificates { get; set; } = true;
        public int ConnectTimeoutMilliseconds { get; set; } = 10000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
        public int SessionTimeoutMilliseconds { get; set; } = 60000;
    }

    /// <summary>
    /// OPC UA client based on the OPC Foundation reference stack. Addresses are standard NodeId strings,
    /// for example ns=2;s=Machine/Temperature or ns=2;i=1001.
    /// </summary>
    public sealed class OpcUaClient : IndustrialClientBase
    {
        private readonly OpcUaClientOptions _options;
        private Session _session;

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

        public override bool IsConnected { get { return _session != null && _session.Connected; } }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            CloseSession();
            try
            {
                var configuration = await CreateConfigurationAsync().ConfigureAwait(false);
                var selected = CoreClientUtils.SelectEndpoint(configuration, _options.EndpointUrl,
                    _options.UseSecurity, _options.ConnectTimeoutMilliseconds);
                var endpoint = new ConfiguredEndpoint(null, selected, EndpointConfiguration.Create(configuration));
                IUserIdentity identity = string.IsNullOrWhiteSpace(_options.Username)
                    ? (IUserIdentity)new UserIdentity(new AnonymousIdentityToken())
                    : new UserIdentity(_options.Username, Encoding.UTF8.GetBytes(_options.Password ?? string.Empty));
                _session = await Session.Create(configuration, endpoint, false, false,
                    "IndustrialCommSdk-" + DeviceId, (uint)_options.SessionTimeoutMilliseconds,
                    identity, null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { CloseSession(); throw new IndustrialConnectionException("Failed to connect OPC UA endpoint.", ex); }
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseSession();
            return Task.CompletedTask;
        }

        protected override Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            return ReadManyCoreAsync(new[] { request }, cancellationToken).ContinueWith(
                task => task.GetAwaiter().GetResult().Values[0], cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        protected override Task<BatchReadResult> ReadManyCoreAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();
            var list = requests.ToList();
            var nodes = new ReadValueIdCollection(list.Select(x => new ReadValueId
            {
                NodeId = ParseNodeId(x.Address), AttributeId = Attributes.Value
            }));
            Opc.Ua.DataValueCollection values;
            DiagnosticInfoCollection diagnostics;
            _session.Read(null, 0, TimestampsToReturn.Both, nodes, out values, out diagnostics);
            ClientBase.ValidateResponse(values, nodes);
            ClientBase.ValidateDiagnosticInfos(diagnostics, nodes);
            var result = new List<DataValue>(list.Count);
            for (var i = 0; i < list.Count; i++) result.Add(ConvertValue(list[i], values[i]));
            return Task.FromResult(new BatchReadResult(result));
        }

        protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            return WriteManyCoreAsync(new[] { request }, cancellationToken);
        }

        protected override Task WriteManyCoreAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();
            var writes = new WriteValueCollection(requests.Select(x => new WriteValue
            {
                NodeId = ParseNodeId(x.Address), AttributeId = Attributes.Value,
                Value = new UaDataValue(new Variant(ConvertForWrite(x)))
            }));
            StatusCodeCollection results;
            DiagnosticInfoCollection diagnostics;
            _session.Write(null, writes, out results, out diagnostics);
            ClientBase.ValidateResponse(results, writes);
            ClientBase.ValidateDiagnosticInfos(diagnostics, writes);
            for (var i = 0; i < results.Count; i++)
                if (StatusCode.IsBad(results[i])) throw new IndustrialProtocolException("OPC UA write failed: " + results[i]);
            return Task.CompletedTask;
        }

        internal static NodeId ParseNodeId(string address)
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

        private async Task<ApplicationConfiguration> CreateConfigurationAsync()
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
            await config.Validate(ApplicationType.Client).ConfigureAwait(false);
            if (_options.AutoAcceptUntrustedCertificates)
                config.CertificateValidator.CertificateValidation += (sender, args) => { args.Accept = true; };
            return config;
        }

        private void EnsureConnected()
        {
            if (!IsConnected) throw new IndustrialConnectionException("OPC UA client is not connected.");
        }

        protected override void OnOperationTimeout() { CloseSession(); }
        protected override void DisposeCore() { CloseSession(); }

        private void CloseSession()
        {
            var session = Interlocked.Exchange(ref _session, null);
            if (session == null) return;
            try { session.Close(); } catch { }
            session.Dispose();
        }
    }
}
