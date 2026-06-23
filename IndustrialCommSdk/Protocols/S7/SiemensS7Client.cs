using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using S7.Net;

namespace IndustrialCommSdk.Protocols.S7
{
    public enum S7Area
    {
        Db,
        Memory,
        Input,
        Output,
    }

    public sealed class S7Address
    {
        public S7Address(S7Area area, int dbNumber, int byteOffset, int bitOffset, string normalized)
        {
            Area = area;
            DbNumber = dbNumber;
            ByteOffset = byteOffset;
            BitOffset = bitOffset;
            Normalized = normalized;
        }

        public S7Area Area { get; private set; }
        public int DbNumber { get; private set; }
        public int ByteOffset { get; private set; }
        public int BitOffset { get; private set; }
        public string Normalized { get; private set; }
    }

    public sealed class S7AddressParser : IAddressParser
    {
        private static readonly Regex DbRegex = new Regex(@"^DB(?<db>\d+)\.DB(?<type>[XBWDL])(?<offset>\d+)(\.(?<bit>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AreaRegex = new Regex(@"^(?<area>[MIQ])(?<type>[XBWDL])?(?<offset>\d+)(\.(?<bit>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public object Parse(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new IndustrialAddressParseException("Address is required.");
            }

            var input = address.Trim().ToUpperInvariant();
            var dbMatch = DbRegex.Match(input);
            if (dbMatch.Success)
            {
                return new S7Address(S7Area.Db, int.Parse(dbMatch.Groups["db"].Value), int.Parse(dbMatch.Groups["offset"].Value), ParseBit(dbMatch.Groups["bit"].Value), input);
            }

            var areaMatch = AreaRegex.Match(input);
            if (areaMatch.Success)
            {
                var area = ParseArea(areaMatch.Groups["area"].Value);
                return new S7Address(area, 0, int.Parse(areaMatch.Groups["offset"].Value), ParseBit(areaMatch.Groups["bit"].Value), input);
            }

            throw new IndustrialAddressParseException(string.Format("Unsupported S7 address: {0}", address));
        }

        private static S7Area ParseArea(string token)
        {
            switch (token)
            {
                case "M": return S7Area.Memory;
                case "I": return S7Area.Input;
                case "Q": return S7Area.Output;
                default: throw new IndustrialAddressParseException("Unsupported S7 area.");
            }
        }

        private static int ParseBit(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return -1;
            }
            return int.Parse(token);
        }
    }

    public sealed class SiemensS7ClientOptions
    {
        public string DeviceId { get; set; }
        public string Host { get; set; }
        public short Rack { get; set; }
        public short Slot { get; set; } = 1;
        public CpuType CpuType { get; set; } = CpuType.S71200;
    }

    public sealed class SiemensS7Client : IndustrialClientBase
    {
        private readonly SiemensS7ClientOptions _options;
        private readonly S7AddressParser _parser;
        private Plc _plc;

        public SiemensS7Client(SiemensS7ClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null, S7AddressParser parser = null)
            : base(options.DeviceId, ProtocolKind.SiemensS7, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _parser = parser ?? new S7AddressParser();
        }

        public override bool IsConnected
        {
            get { return _plc != null && _plc.IsConnected; }
        }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            _plc = new Plc(_options.CpuType, _options.Host, _options.Rack, _options.Slot);
            await _plc.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            if (_plc != null)
            {
                _plc.Close();
                _plc = null;
            }
            return Task.CompletedTask;
        }

        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = (S7Address)_parser.Parse(request.Address);
            var value = await _plc.ReadAsync(parsed.Normalized, cancellationToken).ConfigureAwait(false);
            return new DataValue(request.Address, request.DataType, value, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            _parser.Parse(request.Address);
            await _plc.WriteAsync(request.Address, request.Value, cancellationToken).ConfigureAwait(false);
        }

        protected override void DisposeCore()
        {
            if (_plc != null)
            {
                _plc.Close();
                _plc = null;
            }
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new IndustrialConnectionException("S7 client is not connected.");
            }
        }
    }
}
