using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using IndustrialCommSdk.Protocols.Common;
using IndustrialCommSdk.Transport;

namespace IndustrialCommSdk.Protocols.Mc
{
    public enum McDeviceType
    {
        D, W, R, SD, Z, ZR, M, X, Y, L, TN, SS, CN
    }

    public sealed class McAddress
    {
        public McAddress(McDeviceType deviceType, int index, bool isBitDevice)
        {
            DeviceType = deviceType;
            Index = index;
            IsBitDevice = isBitDevice;
        }

        public McDeviceType DeviceType { get; private set; }
        public int Index { get; private set; }
        public bool IsBitDevice { get; private set; }
    }

    public sealed class McAddressParser : IAddressParser
    {
        private static readonly Regex Pattern = new Regex(@"^(ZR|SD|TN|SS|CN|[A-Z])([0-9A-F]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public object Parse(string address)
        {
            var match = Pattern.Match((address ?? string.Empty).Trim().ToUpperInvariant());
            if (!match.Success)
            {
                throw new Exceptions.IndustrialAddressParseException(string.Format("Unsupported MC address: {0}", address));
            }

            McDeviceType type;
            if (!Enum.TryParse(match.Groups[1].Value, true, out type))
            {
                throw new Exceptions.IndustrialAddressParseException("Unsupported MC device type.");
            }

            var isHex = type == McDeviceType.X || type == McDeviceType.Y || type == McDeviceType.W;
            var index = Convert.ToInt32(match.Groups[2].Value, isHex ? 16 : 10);
            var isBit = type == McDeviceType.M || type == McDeviceType.X || type == McDeviceType.Y || type == McDeviceType.L;
            return new McAddress(type, index, isBit);
        }
    }

    public sealed class MitsubishiMcClientOptions
    {
        public string DeviceId { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 5000;
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        public int ReceiveTimeoutMilliseconds { get; set; } = 5000;
    }

    public sealed class MitsubishiMcClient : IndustrialClientBase
    {
        private readonly TcpTransportClient _transport;
        private readonly McAddressParser _parser;

        public MitsubishiMcClient(MitsubishiMcClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null, McAddressParser parser = null)
            : base(options.DeviceId, ProtocolKind.MitsubishiMc, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _transport = new TcpTransportClient(new TcpTransportOptions
            {
                Host = options.Host,
                Port = options.Port,
                SendTimeoutMilliseconds = options.SendTimeoutMilliseconds,
                ReceiveTimeoutMilliseconds = options.ReceiveTimeoutMilliseconds,
            });
            _parser = parser ?? new McAddressParser();
        }

        public override bool IsConnected
        {
            get { return _transport.IsConnected; }
        }

        protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.ConnectAsync(cancellationToken);
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.DisconnectAsync(cancellationToken);
        }

        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            var address = (McAddress)_parser.Parse(request.Address);
            var frame = address.IsBitDevice
                ? McFrame3E.BuildReadBitsRequest(address, request.Length)
                : McFrame3E.BuildReadWordsRequest(address, request.Length);

            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            var header = await _transport.ReceiveExactAsync(11, cancellationToken).ConfigureAwait(false);
            var responseLength = McFrame3E.ReadU16LE(header, 7);
            var body = await _transport.ReceiveExactAsync(responseLength, cancellationToken).ConfigureAwait(false);
            var response = McFrame3E.Combine(header, body);
            var payload = McFrame3E.ParseResponse(response);

            if (address.IsBitDevice)
            {
                return RegisterValueCodec.ToDataValue(request, McFrame3E.UnpackBits(payload, request.Length));
            }

            return RegisterValueCodec.ToDataValue(request, McFrame3E.ToRegisters(payload));
        }

        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            var address = (McAddress)_parser.Parse(request.Address);
            var frame = address.IsBitDevice
                ? McFrame3E.BuildWriteBitsRequest(address, RegisterValueCodec.EncodeBits(request))
                : McFrame3E.BuildWriteWordsRequest(address, RegisterValueCodec.EncodeRegisters(request));

            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            var header = await _transport.ReceiveExactAsync(11, cancellationToken).ConfigureAwait(false);
            var responseLength = McFrame3E.ReadU16LE(header, 7);
            var body = await _transport.ReceiveExactAsync(responseLength, cancellationToken).ConfigureAwait(false);
            McFrame3E.ParseResponse(McFrame3E.Combine(header, body));
        }

        protected override void DisposeCore()
        {
            _transport.Dispose();
        }
    }
}
