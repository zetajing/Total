using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using S7.Net;
using PlcArea = S7.Net.DataType;
using PlcVarType = S7.Net.VarType;

namespace IndustrialCommSdk.Protocols.S7
{
    public enum S7Area
    {
        Db,
        Memory,
        Input,
        Output,
    }

    public sealed class S7Address : IIndustrialAddress
    {
        public S7Address(S7Area area, int dbNumber, int byteOffset, int bitOffset, string normalized, string original = null)
        {
            if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("Normalized S7 address is required.", nameof(normalized));
            Area = area;
            DbNumber = dbNumber;
            ByteOffset = byteOffset;
            BitOffset = bitOffset;
            Normalized = normalized;
            Original = string.IsNullOrWhiteSpace(original) ? normalized : original;
        }

        public S7Area Area { get; private set; }
        public int DbNumber { get; private set; }
        public int ByteOffset { get; private set; }
        public int BitOffset { get; private set; }
        public string Original { get; private set; }
        public string Normalized { get; private set; }
        public bool IsBitAddress { get { return BitOffset >= 0; } }

        string IIndustrialAddress.Area { get { return Area.ToString(); } }
        int IIndustrialAddress.Offset { get { return ByteOffset; } }
        int? IIndustrialAddress.Bit { get { return IsBitAddress ? (int?)BitOffset : null; } }

        public override string ToString()
        {
            return Normalized;
        }
    }

    /// <summary>
    /// S7 address parser aligned with S7.NetPlus address conventions.
    /// Supports DB1.DBX0.0 / DB1.DBB0 / DB1.DBW0 / DB1.DBD0 and M/I/Q areas.
    /// </summary>
    public sealed class S7AddressParser : IAddressParser, IAddressParser<S7Address>
    {
        private static readonly Regex DbRegex = new Regex(
            @"^DB(?<db>\d+)\.DB(?<type>[XBWDL])(?<offset>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AreaRegex = new Regex(
            @"^(?<area>[MIQ])(?<type>[XBWDL])?(?<offset>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public object Parse(string address)
        {
            return ParseTyped(address);
        }

        S7Address IAddressParser<S7Address>.Parse(string address)
        {
            return ParseTyped(address);
        }

        public S7Address ParseTyped(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new IndustrialAddressParseException("S7 address is required.");

            var input = NormalizeAddress(address);
            var dbMatch = DbRegex.Match(input);
            if (dbMatch.Success)
            {
                var type = dbMatch.Groups["type"].Value.ToUpperInvariant();
                var bit = ParseBit(dbMatch.Groups["bit"].Value);
                ValidateBitUsage(type, bit, input);

                var dbNumber = ParseNonNegative(dbMatch.Groups["db"].Value, "DB number");
                if (dbNumber <= 0)
                    throw new IndustrialAddressParseException("S7 DB number must be greater than zero.");

                return new S7Address(
                    S7Area.Db,
                    dbNumber,
                    ParseNonNegative(dbMatch.Groups["offset"].Value, "byte offset"),
                    bit,
                    input,
                    address);
            }

            var areaMatch = AreaRegex.Match(input);
            if (areaMatch.Success)
            {
                var type = areaMatch.Groups["type"].Value.ToUpperInvariant();
                var bit = ParseBit(areaMatch.Groups["bit"].Value);
                ValidateBitUsage(type, bit, input);

                return new S7Address(
                    ParseArea(areaMatch.Groups["area"].Value),
                    0,
                    ParseNonNegative(areaMatch.Groups["offset"].Value, "byte offset"),
                    bit,
                    input,
                    address);
            }

            throw new IndustrialAddressParseException("Unsupported S7 address: " + address);
        }

        private static string NormalizeAddress(string address)
        {
            var input = address.Trim().ToUpperInvariant();
            if (input.StartsWith("P#", StringComparison.Ordinal)) input = input.Substring(2);
            if (input.StartsWith("%", StringComparison.Ordinal)) input = input.Substring(1);
            return input;
        }

        private static int ParseNonNegative(string token, string name)
        {
            int value;
            if (!int.TryParse(token, out value) || value < 0)
                throw new IndustrialAddressParseException("Invalid S7 " + name + ": " + token);
            return value;
        }

        private static int ParseBit(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return -1;
            int bit;
            if (!int.TryParse(token, out bit) || bit < 0 || bit > 7)
                throw new IndustrialAddressParseException("S7 bit offset must be in the range 0-7.");
            return bit;
        }

        private static void ValidateBitUsage(string type, int bit, string address)
        {
            if (type == "X" && bit < 0)
                throw new IndustrialAddressParseException("S7 bit address requires a bit index: " + address);
            if (!string.IsNullOrEmpty(type) && type != "X" && bit >= 0)
                throw new IndustrialAddressParseException("Only S7 bit addresses may contain a bit index: " + address);
        }

        private static S7Area ParseArea(string token)
        {
            switch (token.ToUpperInvariant())
            {
                case "M": return S7Area.Memory;
                case "I": return S7Area.Input;
                case "Q": return S7Area.Output;
                default: throw new IndustrialAddressParseException("Unsupported S7 area: " + token);
            }
        }
    }

    public sealed class SiemensS7ClientOptions
    {
        public string DeviceId { get; set; }
        public string Host { get; set; }
        public short Rack { get; set; }
        public short Slot { get; set; } = 1;
        public CpuType CpuType { get; set; } = CpuType.S71200;
        public bool AutoReconnect { get; set; } = true;
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
    }

    /// <summary>
    /// Siemens S7 client built on S7.NetPlus. The wrapper owns connection lifecycle,
    /// serializes access through IndustrialClientBase, and retries one time after a stale session.
    /// </summary>
    public sealed class SiemensS7Client : IndustrialClientBase, IBatchOperationPlanner
    {
        private readonly SiemensS7ClientOptions _options;
        private readonly S7AddressParser _parser;
        private Plc _plc;

        public SiemensS7Client(
            SiemensS7ClientOptions options,
            IIndustrialLogger logger = null,
            IPollingScheduler pollingScheduler = null,
            S7AddressParser parser = null)
            : base(GetDeviceId(options), ProtocolKind.SiemensS7,
                pollingScheduler ?? new PollingScheduler(logger),
                logger ?? NullIndustrialLogger.Instance,
                options.OperationTimeoutMilliseconds)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.Host))
                throw new ArgumentException("S7 host is required.", nameof(options));
            if (_options.Rack < 0) throw new ArgumentOutOfRangeException(nameof(options), "Rack cannot be negative.");
            if (_options.Slot < 0) throw new ArgumentOutOfRangeException(nameof(options), "Slot cannot be negative.");
            if (_options.ConnectTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Connect timeout must be positive.");
            if (_options.OperationTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Operation timeout must be positive.");
            _parser = parser ?? new S7AddressParser();
        }

        private static string GetDeviceId(SiemensS7ClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceId))
                throw new ArgumentException("Device ID is required.", nameof(options));
            return options.DeviceId;
        }

        public override bool IsConnected
        {
            get
            {
                try { return _plc != null && _plc.IsConnected; }
                catch { return false; }
            }
        }

        public BatchSplitPlan PlanRead(IReadOnlyCollection<ReadRequest> requests, BatchReadOptions options, ProtocolCapabilities capabilities)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            options = options ?? BatchReadOptions.Default;
            capabilities = capabilities ?? Capabilities;

            var planned = requests
                .Select(request => new PlannedS7Read(request, _parser.ParseTyped(request.Address), EstimateEndOffset(_parser.ParseTyped(request.Address), request)))
                .OrderBy(item => item.Address.Area)
                .ThenBy(item => item.Address.DbNumber)
                .ThenBy(item => item.Request.DataType)
                .ThenBy(item => item.Address.ByteOffset)
                .ThenBy(item => item.Address.BitOffset)
                .ToList();

            var groups = BuildReadGroups(planned, options, capabilities);
            return new BatchSplitPlan(ProtocolKind.SiemensS7, BatchOperationKind.Read, groups, requests.Count);
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
                    BuildAreaKey(address),
                    address.ByteOffset,
                    EstimateEndOffset(address, request),
                    request.DataType,
                    new[] { request }));
            }
            return new BatchSplitPlan(ProtocolKind.SiemensS7, BatchOperationKind.Write, groups, requests.Count);
        }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            ClosePlc();
            var plc = new Plc(_options.CpuType, _options.Host, _options.Rack, _options.Slot);
            try
            {
                using (var timeoutCts = new CancellationTokenSource(_options.ConnectTimeoutMilliseconds))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    try { await plc.OpenAsync(linkedCts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    { throw new IndustrialTimeoutException("Siemens S7 connect timeout."); }
                }
                if (!plc.IsConnected)
                    throw new IndustrialConnectionException("S7.NetPlus completed OpenAsync but the PLC is not connected.");
                _plc = plc;
            }
            catch (IndustrialTimeoutException)
            {
                SafeClose(plc);
                throw;
            }
            catch (OperationCanceledException)
            {
                SafeClose(plc);
                throw;
            }
            catch (Exception ex)
            {
                SafeClose(plc);
                throw new IndustrialConnectionException("Failed to connect Siemens S7 device.", ex);
            }
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClosePlc();
            return Task.CompletedTask;
        }

        protected override void OnOperationTimeout() { ClosePlc(); }

        protected override Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            var address = _parser.ParseTyped(request.Address);
            return ExecuteWithReconnectAsync(async token =>
            {
                var value = await ReadValueAsync(address, request, token).ConfigureAwait(false);
                return new DataValue(request.Address, request.DataType, value, null,
                    QualityStatus.Good, DateTimeOffset.UtcNow, null);
            }, cancellationToken);
        }

        protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            var address = _parser.ParseTyped(request.Address);
            return ExecuteWithReconnectAsync(async token =>
            {
                await WriteValueAsync(address, request, token).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        private async Task<T> ExecuteWithReconnectAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            EnsureConnected();
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (IndustrialAddressParseException) { throw; }
            catch (IndustrialDataConversionException) { throw; }
            catch (Exception first)
            {
                ClosePlc();
                if (!_options.AutoReconnect)
                    throw new IndustrialConnectionException("S7 communication failed.", first);

                try
                {
                    await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception retry)
                {
                    ClosePlc();
                    throw new IndustrialConnectionException("S7 communication failed after reconnect.", retry);
                }
            }
        }

        public Task<T> ReadDbClassAsync<T>(int dbNumber, int startByteAddress = 0,
            CancellationToken cancellationToken = default(CancellationToken)) where T : class, new()
        {
            ValidateDbBlockArguments(dbNumber, startByteAddress);
            return ExecuteExclusiveAsync(token => ExecuteWithReconnectAsync(async inner =>
            {
                var value = await _plc.ReadClassAsync<T>(dbNumber, startByteAddress, inner).ConfigureAwait(false);
                if (value == null) throw new IndustrialProtocolException("S7 DB class read returned no data.");
                return value;
            }, token), cancellationToken);
        }

        public Task<T> ReadDbClassAsync<T>(Func<T> factory, int dbNumber, int startByteAddress = 0,
            CancellationToken cancellationToken = default(CancellationToken)) where T : class
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            ValidateDbBlockArguments(dbNumber, startByteAddress);
            return ExecuteExclusiveAsync(token => ExecuteWithReconnectAsync(async inner =>
            {
                var value = await _plc.ReadClassAsync(factory, dbNumber, startByteAddress, inner).ConfigureAwait(false);
                if (value == null) throw new IndustrialProtocolException("S7 DB class read returned no data.");
                return value;
            }, token), cancellationToken);
        }

        public Task<int> ReadDbClassAsync(object instance, int dbNumber, int startByteAddress = 0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            ValidateDbBlockArguments(dbNumber, startByteAddress);
            return ExecuteExclusiveAsync(token => ExecuteWithReconnectAsync(async inner =>
            {
                var result = await _plc.ReadClassAsync(instance, dbNumber, startByteAddress, inner).ConfigureAwait(false);
                return result.Item1;
            }, token), cancellationToken);
        }

        public Task WriteDbClassAsync(object value, int dbNumber, int startByteAddress = 0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ValidateDbBlockArguments(dbNumber, startByteAddress);
            return ExecuteExclusiveAsync(token => ExecuteWithReconnectAsync(async inner =>
            {
                await _plc.WriteClassAsync(value, dbNumber, startByteAddress, inner).ConfigureAwait(false);
                return true;
            }, token), cancellationToken);
        }

        protected override void DisposeCore()
        {
            ClosePlc();
        }

        private void EnsureConnected()
        {
            if (!IsConnected) throw new IndustrialConnectionException("S7 client is not connected.");
        }

        private async Task<object> ReadValueAsync(S7Address address, ReadRequest request, CancellationToken token)
        {
            switch (request.DataType)
            {
                case Abstractions.DataType.String:
                    var stringBytes = await _plc.ReadBytesAsync(ToPlcArea(address.Area), address.DbNumber,
                        address.ByteOffset, request.Length, token).ConfigureAwait(false);
                    return Encoding.ASCII.GetString(stringBytes).TrimEnd('\0');
                case Abstractions.DataType.ByteArray:
                    return await _plc.ReadBytesAsync(ToPlcArea(address.Area), address.DbNumber,
                        address.ByteOffset, request.Length, token).ConfigureAwait(false);
                case Abstractions.DataType.Char:
                    return Convert.ToChar(Convert.ToByte(await ReadTypedValueAsync(address, PlcVarType.Byte, 1, token).ConfigureAwait(false)));
                case Abstractions.DataType.Byte:
                    return await ReadTypedValueAsync(address, PlcVarType.Byte, request.Length, token).ConfigureAwait(false);
                default:
                    return await ReadTypedValueAsync(address, ToVarType(request.DataType), request.Length, token).ConfigureAwait(false);
            }
        }

        private Task<object> ReadTypedValueAsync(S7Address address, PlcVarType varType, int count, CancellationToken token)
        {
            return _plc.ReadAsync(ToPlcArea(address.Area), address.DbNumber, address.ByteOffset,
                varType, count, address.BitOffset >= 0 ? (byte)address.BitOffset : (byte)0, token);
        }

        private async Task WriteValueAsync(S7Address address, WriteRequest request, CancellationToken token)
        {
            switch (request.DataType)
            {
                case Abstractions.DataType.Bool:
                    if (address.BitOffset < 0)
                        throw new IndustrialAddressParseException("S7 bool write requires a bit address: " + address.Normalized);
                    await _plc.WriteBitAsync(ToPlcArea(address.Area), address.DbNumber, address.ByteOffset,
                        address.BitOffset, Convert.ToBoolean(request.Value), token).ConfigureAwait(false);
                    return;
                case Abstractions.DataType.String:
                    await _plc.WriteBytesAsync(ToPlcArea(address.Area), address.DbNumber, address.ByteOffset,
                        Encoding.ASCII.GetBytes((request.Value ?? string.Empty).ToString()), token).ConfigureAwait(false);
                    return;
                case Abstractions.DataType.ByteArray:
                    await _plc.WriteBytesAsync(ToPlcArea(address.Area), address.DbNumber, address.ByteOffset,
                        (byte[])request.Value, token).ConfigureAwait(false);
                    return;
                case Abstractions.DataType.Char:
                    var text = (request.Value ?? string.Empty).ToString();
                    await _plc.WriteAsync(address.Normalized, (byte)(text.Length == 0 ? '\0' : text[0]), token).ConfigureAwait(false);
                    return;
                default:
                    await _plc.WriteAsync(address.Normalized, request.Value, token).ConfigureAwait(false);
                    return;
            }
        }

        private static IReadOnlyList<BatchRequestGroup> BuildReadGroups(
            IReadOnlyList<PlannedS7Read> planned,
            BatchReadOptions options,
            ProtocolCapabilities capabilities)
        {
            var groups = new List<BatchRequestGroup>();
            if (planned.Count == 0) return groups;

            var maxItems = options.MaxItemsPerBatch ?? capabilities.MaxReadItems;
            var maxSpan = options.MaxAddressSpan ?? capabilities.MaxAddressSpan;
            var current = new List<PlannedS7Read>();
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
                    current = new List<PlannedS7Read>();
                }
                current.Add(item);
            }

            if (current.Count > 0)
                groups.Add(CreateReadGroup(sequence, current));
            return groups;
        }

        private static bool CanJoin(IReadOnlyList<PlannedS7Read> current, PlannedS7Read next, int maxItems, int maxSpan)
        {
            if (current.Count >= maxItems) return false;
            var first = current[0];
            if (first.Address.Area != next.Address.Area) return false;
            if (first.Address.DbNumber != next.Address.DbNumber) return false;
            if (first.Request.DataType != next.Request.DataType) return false;
            var start = Math.Min(current.Min(item => item.Address.ByteOffset), next.Address.ByteOffset);
            var end = Math.Max(current.Max(item => item.EndOffset), next.EndOffset);
            return end - start + 1 <= maxSpan;
        }

        private static BatchRequestGroup CreateReadGroup(int sequence, IReadOnlyList<PlannedS7Read> current)
        {
            var start = current.Min(item => item.Address.ByteOffset);
            var end = current.Max(item => item.EndOffset);
            return BatchRequestGroup.ForRead(
                sequence,
                BuildAreaKey(current[0].Address),
                start,
                end,
                current[0].Request.DataType,
                current.Select(item => item.Request).ToList());
        }

        private static string BuildAreaKey(S7Address address)
        {
            return address.Area == S7Area.Db
                ? string.Format("DB{0}", address.DbNumber)
                : address.Area.ToString();
        }

        private static int EstimateEndOffset(S7Address address, ReadRequest request)
        {
            if (address.IsBitAddress) return address.ByteOffset;
            return address.ByteOffset + Math.Max(1, EstimateByteLength(request.DataType, request.Length)) - 1;
        }

        private static int EstimateEndOffset(S7Address address, WriteRequest request)
        {
            if (address.IsBitAddress) return address.ByteOffset;
            return address.ByteOffset + Math.Max(1, EstimateByteLength(request.DataType, request.Length)) - 1;
        }

        private static int EstimateByteLength(Abstractions.DataType dataType, ushort length)
        {
            var count = Math.Max(1, (int)length);
            switch (dataType)
            {
                case Abstractions.DataType.Bool:
                case Abstractions.DataType.Byte:
                case Abstractions.DataType.Char:
                case Abstractions.DataType.String:
                case Abstractions.DataType.ByteArray:
                    return count;
                case Abstractions.DataType.Int16:
                case Abstractions.DataType.UInt16:
                    return count * 2;
                case Abstractions.DataType.Int32:
                case Abstractions.DataType.UInt32:
                case Abstractions.DataType.Float:
                    return count * 4;
                case Abstractions.DataType.Double:
                    return count * 8;
                default:
                    return count;
            }
        }

        private static PlcArea ToPlcArea(S7Area area)
        {
            switch (area)
            {
                case S7Area.Db: return PlcArea.DataBlock;
                case S7Area.Memory: return PlcArea.Memory;
                case S7Area.Input: return PlcArea.Input;
                case S7Area.Output: return PlcArea.Output;
                default: throw new IndustrialAddressParseException("Unsupported S7 area.");
            }
        }

        private static PlcVarType ToVarType(Abstractions.DataType dataType)
        {
            switch (dataType)
            {
                case Abstractions.DataType.Bool: return PlcVarType.Bit;
                case Abstractions.DataType.Int16: return PlcVarType.Int;
                case Abstractions.DataType.UInt16: return PlcVarType.Word;
                case Abstractions.DataType.Int32: return PlcVarType.DInt;
                case Abstractions.DataType.UInt32: return PlcVarType.DWord;
                case Abstractions.DataType.Float: return PlcVarType.Real;
                case Abstractions.DataType.Double: return PlcVarType.LReal;
                case Abstractions.DataType.Byte:
                case Abstractions.DataType.Char: return PlcVarType.Byte;
                default: throw new IndustrialDataConversionException("S7 does not support data type " + dataType + ".");
            }
        }

        private static void ValidateDbBlockArguments(int dbNumber, int startByteAddress)
        {
            if (dbNumber <= 0) throw new ArgumentOutOfRangeException(nameof(dbNumber));
            if (startByteAddress < 0) throw new ArgumentOutOfRangeException(nameof(startByteAddress));
        }

        private void ClosePlc()
        {
            var plc = _plc;
            _plc = null;
            SafeClose(plc);
        }

        private static void SafeClose(Plc plc)
        {
            if (plc == null) return;
            try { plc.Close(); } catch { }
        }

        private sealed class PlannedS7Read
        {
            public PlannedS7Read(ReadRequest request, S7Address address, int endOffset)
            {
                Request = request;
                Address = address;
                EndOffset = endOffset;
            }

            public ReadRequest Request { get; private set; }
            public S7Address Address { get; private set; }
            public int EndOffset { get; private set; }
        }
    }
}
