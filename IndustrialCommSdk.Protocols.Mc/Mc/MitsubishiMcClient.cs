using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        D, W, R, SD, Z, ZR, M, X, Y, L, TN, SS, SN, CN
    }

    internal sealed class McDeviceMetadata
    {
        public McDeviceMetadata(byte code, int radix, bool isBitDevice)
        {
            Code = code;
            Radix = radix;
            IsBitDevice = isBitDevice;
        }

        public byte Code { get; private set; }
        public int Radix { get; private set; }
        public bool IsBitDevice { get; private set; }
    }

    internal static class McDeviceCatalog
    {
        private static readonly IReadOnlyDictionary<McDeviceType, McDeviceMetadata> Devices =
            new Dictionary<McDeviceType, McDeviceMetadata>
            {
                [McDeviceType.D] = new McDeviceMetadata(0xA8, 10, false),
                [McDeviceType.W] = new McDeviceMetadata(0xB4, 16, false),
                [McDeviceType.R] = new McDeviceMetadata(0xAF, 10, false),
                [McDeviceType.SD] = new McDeviceMetadata(0xA9, 10, false),
                [McDeviceType.Z] = new McDeviceMetadata(0xCC, 10, false),
                // ZR device numbers are decimal in Mitsubishi device notation.
                [McDeviceType.ZR] = new McDeviceMetadata(0xB0, 10, false),
                [McDeviceType.M] = new McDeviceMetadata(0x90, 10, true),
                [McDeviceType.X] = new McDeviceMetadata(0x9C, 16, true),
                [McDeviceType.Y] = new McDeviceMetadata(0x9D, 16, true),
                [McDeviceType.L] = new McDeviceMetadata(0x92, 10, true),
                [McDeviceType.TN] = new McDeviceMetadata(0xC2, 10, false),
                [McDeviceType.SS] = new McDeviceMetadata(0xC7, 10, true),
                [McDeviceType.SN] = new McDeviceMetadata(0xC8, 10, false),
                [McDeviceType.CN] = new McDeviceMetadata(0xC5, 10, false),
            };

        public static McDeviceMetadata Get(McDeviceType type)
        {
            McDeviceMetadata metadata;
            if (!Devices.TryGetValue(type, out metadata))
            {
                throw new Exceptions.IndustrialAddressParseException("Unsupported MC device type: " + type);
            }
            return metadata;
        }
    }

    public sealed class McAddress : IIndustrialAddress
    {
        public McAddress(McDeviceType deviceType, int index, bool isBitDevice, string normalized = null, string original = null)
        {
            DeviceType = deviceType;
            Index = index;
            IsBitDevice = isBitDevice;
            Normalized = string.IsNullOrWhiteSpace(normalized) ? deviceType + index.ToString(CultureInfo.InvariantCulture) : normalized;
            Original = string.IsNullOrWhiteSpace(original) ? Normalized : original;
        }

        public McDeviceType DeviceType { get; private set; }
        public int Index { get; private set; }
        public bool IsBitDevice { get; private set; }
        public string Original { get; private set; }
        public string Normalized { get; private set; }

        string IIndustrialAddress.Area { get { return DeviceType.ToString(); } }
        int IIndustrialAddress.Offset { get { return Index; } }
        int? IIndustrialAddress.Bit { get { return null; } }
        bool IIndustrialAddress.IsBitAddress { get { return IsBitDevice; } }

        public override string ToString()
        {
            return Normalized;
        }
    }

    public sealed class McAddressParser : IAddressParser, IAddressParser<McAddress>
    {
        private static readonly Regex Pattern = new Regex(
            @"^(ZR|SD|TN|SS|SN|CN|[A-Z])([0-9A-F]+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public object Parse(string address)
        {
            return ParseTyped(address);
        }

        McAddress IAddressParser<McAddress>.Parse(string address)
        {
            return ParseTyped(address);
        }

        public McAddress ParseTyped(string address)
        {
            var input = (address ?? string.Empty).Trim().ToUpperInvariant();
            var match = Pattern.Match(input);
            if (!match.Success)
            {
                throw new Exceptions.IndustrialAddressParseException("Unsupported MC address: " + address);
            }

            McDeviceType type;
            if (!Enum.TryParse(match.Groups[1].Value, true, out type))
            {
                throw new Exceptions.IndustrialAddressParseException("Unsupported MC device type.");
            }

            var metadata = McDeviceCatalog.Get(type);
            int index;
            try
            {
                index = Convert.ToInt32(match.Groups[2].Value, metadata.Radix);
            }
            catch (Exception ex)
            {
                throw new Exceptions.IndustrialAddressParseException(
                    string.Format(CultureInfo.InvariantCulture, "Invalid {0} address: {1}", type, address), ex);
            }

            if (index < 0 || index > 0xFFFFFF)
            {
                throw new Exceptions.IndustrialAddressParseException("MC device address must fit in 24 bits.");
            }

            return new McAddress(type, index, metadata.IsBitDevice, input, address);
        }
    }

    public sealed class MitsubishiMcClientOptions
    {
        public string DeviceId { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 5000;
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        public int ReceiveTimeoutMilliseconds { get; set; } = 5000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
    }

    public sealed class MitsubishiMcClient : IndustrialClientBase, IBatchOperationPlanner
    {
        private readonly ITransportClient _transport;
        private readonly McAddressParser _parser;

        public MitsubishiMcClient(
            MitsubishiMcClientOptions options,
            IIndustrialLogger logger = null,
            IPollingScheduler pollingScheduler = null,
            McAddressParser parser = null)
            : base(GetDeviceId(options), ProtocolKind.MitsubishiMc,
                pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance, options.OperationTimeoutMilliseconds)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Host)) throw new ArgumentException("Host is required.", nameof(options));
            if (options.Port < 1 || options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));
            if (options.OperationTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options.OperationTimeoutMilliseconds));

            _transport = new TcpTransportClient(new TcpTransportOptions
            {
                Host = options.Host,
                Port = options.Port,
                SendTimeoutMilliseconds = options.SendTimeoutMilliseconds,
                ReceiveTimeoutMilliseconds = options.ReceiveTimeoutMilliseconds,
            });
            _parser = parser ?? new McAddressParser();
        }

        internal MitsubishiMcClient(
            string deviceId,
            ITransportClient transport,
            IIndustrialLogger logger = null,
            IPollingScheduler pollingScheduler = null,
            McAddressParser parser = null)
            : base(deviceId, ProtocolKind.MitsubishiMc,
                pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _parser = parser ?? new McAddressParser();
        }

        private static string GetDeviceId(MitsubishiMcClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return options.DeviceId;
        }

        public override bool IsConnected { get { return _transport.IsConnected; } }

        public BatchSplitPlan PlanRead(IReadOnlyCollection<ReadRequest> requests, BatchReadOptions options, ProtocolCapabilities capabilities)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            options = options ?? BatchReadOptions.Default;
            capabilities = capabilities ?? Capabilities;

            var planned = requests
                .Select(request => new PlannedMcRead(request, _parser.ParseTyped(request.Address), EstimateEndOffset(_parser.ParseTyped(request.Address), request)))
                .OrderBy(item => item.Address.DeviceType)
                .ThenBy(item => item.Request.DataType)
                .ThenBy(item => item.Address.Index)
                .ToList();

            var groups = BuildReadGroups(planned, options, capabilities);
            return new BatchSplitPlan(ProtocolKind.MitsubishiMc, BatchOperationKind.Read, groups, requests.Count);
        }

        public BatchSplitPlan PlanWrite(IReadOnlyCollection<WriteRequest> requests, BatchWriteOptions options, ProtocolCapabilities capabilities)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            var groups = new List<BatchRequestGroup>();
            var sequence = 0;
            foreach (var request in requests)
            {
                var address = _parser.ParseTyped(request.Address);
                groups.Add(BatchRequestGroup.ForWrite(
                    sequence++,
                    address.DeviceType.ToString(),
                    address.Index,
                    EstimateEndOffset(address, request),
                    request.DataType,
                    new[] { request }));
            }
            return new BatchSplitPlan(ProtocolKind.MitsubishiMc, BatchOperationKind.Write, groups, requests.Count);
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
            EnsureConnected();
            var address = _parser.ParseTyped(request.Address);
            var frame = address.IsBitDevice
                ? McFrame3E.BuildReadBitsRequest(address, request.Length)
                : McFrame3E.BuildReadWordsRequest(address, request.Length);

            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            var header = await _transport.ReceiveExactAsync(11, cancellationToken).ConfigureAwait(false);
            var remainingLength = McFrame3E.GetRemainingResponseLength(header);
            var body = await _transport.ReceiveExactAsync(remainingLength, cancellationToken).ConfigureAwait(false);
            var payload = McFrame3E.ParseResponse(McFrame3E.Combine(header, body));

            return address.IsBitDevice
                ? RegisterValueCodec.ToDataValue(request, McFrame3E.UnpackBits(payload, request.Length))
                : RegisterValueCodec.ToDataValue(request, McFrame3E.ToRegisters(payload));
        }

        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var address = _parser.ParseTyped(request.Address);
            var frame = address.IsBitDevice
                ? McFrame3E.BuildWriteBitsRequest(address, RegisterValueCodec.EncodeBits(request))
                : McFrame3E.BuildWriteWordsRequest(address, RegisterValueCodec.EncodeRegisters(request));

            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            var header = await _transport.ReceiveExactAsync(11, cancellationToken).ConfigureAwait(false);
            var remainingLength = McFrame3E.GetRemainingResponseLength(header);
            var body = await _transport.ReceiveExactAsync(remainingLength, cancellationToken).ConfigureAwait(false);
            McFrame3E.ParseResponse(McFrame3E.Combine(header, body));
        }

        private static IReadOnlyList<BatchRequestGroup> BuildReadGroups(
            IReadOnlyList<PlannedMcRead> planned,
            BatchReadOptions options,
            ProtocolCapabilities capabilities)
        {
            var groups = new List<BatchRequestGroup>();
            if (planned.Count == 0) return groups;

            var maxItems = options.MaxItemsPerBatch ?? capabilities.MaxReadItems;
            var maxSpan = options.MaxAddressSpan ?? capabilities.MaxAddressSpan;
            var current = new List<PlannedMcRead>();
            var sequence = 0;

            foreach (var item in planned)
            {
                if (current.Count == 0)
                {
                    current.Add(item);
                    continue;
                }

                if (!CanJoin(current, item, maxItems, maxSpan))
                {
                    groups.Add(CreateReadGroup(sequence++, current));
                    current = new List<PlannedMcRead>();
                }
                current.Add(item);
            }

            if (current.Count > 0)
                groups.Add(CreateReadGroup(sequence, current));
            return groups;
        }

        private static bool CanJoin(IReadOnlyList<PlannedMcRead> current, PlannedMcRead next, int maxItems, int maxSpan)
        {
            if (current.Count >= maxItems) return false;
            var first = current[0];
            if (first.Address.DeviceType != next.Address.DeviceType) return false;
            if (first.Address.IsBitDevice != next.Address.IsBitDevice) return false;
            if (first.Request.DataType != next.Request.DataType) return false;
            var start = Math.Min(current.Min(item => item.Address.Index), next.Address.Index);
            var end = Math.Max(current.Max(item => item.EndOffset), next.EndOffset);
            return end - start + 1 <= maxSpan;
        }

        private static BatchRequestGroup CreateReadGroup(int sequence, IReadOnlyList<PlannedMcRead> current)
        {
            var start = current.Min(item => item.Address.Index);
            var end = current.Max(item => item.EndOffset);
            return BatchRequestGroup.ForRead(
                sequence,
                current[0].Address.DeviceType.ToString(),
                start,
                end,
                current[0].Request.DataType,
                current.Select(item => item.Request).ToList());
        }

        private static int EstimateEndOffset(McAddress address, ReadRequest request)
        {
            return address.Index + Math.Max(1, (int)request.Length) - 1;
        }

        private static int EstimateEndOffset(McAddress address, WriteRequest request)
        {
            return address.Index + Math.Max(1, (int)request.Length) - 1;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new Exceptions.IndustrialConnectionException("Mitsubishi MC client is not connected.");
            }
        }

        protected override void DisposeCore()
        {
            _transport.Dispose();
        }

        protected override void OnOperationTimeout()
        {
            try { _transport.DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch { }
        }

        private sealed class PlannedMcRead
        {
            public PlannedMcRead(ReadRequest request, McAddress address, int endOffset)
            {
                Request = request;
                Address = address;
                EndOffset = endOffset;
            }

            public ReadRequest Request { get; private set; }
            public McAddress Address { get; private set; }
            public int EndOffset { get; private set; }
        }
    }
}
