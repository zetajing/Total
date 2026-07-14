using System;
using System.Text.RegularExpressions;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

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

        public override string ToString() { return Normalized; }
    }

    public sealed class S7AddressParser : IAddressParser, IAddressParser<S7Address>
    {
        private static readonly Regex DbRegex = new Regex(
            @"^DB(?<db>\d+)\.DB(?<type>[XBWDL])(?<offset>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AreaRegex = new Regex(
            @"^(?<area>[MIQ])(?<type>[XBWDL])?(?<offset>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public object Parse(string address) { return ParseTyped(address); }
        S7Address IAddressParser<S7Address>.Parse(string address) { return ParseTyped(address); }

        public S7Address ParseTyped(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new IndustrialAddressParseException("S7 address is required.");
            var input = NormalizeAddress(address);
            var dbMatch = DbRegex.Match(input);
            if (dbMatch.Success)
            {
                var type = dbMatch.Groups["type"].Value.ToUpperInvariant();
                var bit = ParseBit(dbMatch.Groups["bit"].Value);
                ValidateBitUsage(type, bit, input);
                var dbNumber = ParseNonNegative(dbMatch.Groups["db"].Value, "DB number");
                if (dbNumber <= 0) throw new IndustrialAddressParseException("S7 DB number must be greater than zero.");
                return new S7Address(S7Area.Db, dbNumber,
                    ParseNonNegative(dbMatch.Groups["offset"].Value, "byte offset"), bit, input, address);
            }

            var areaMatch = AreaRegex.Match(input);
            if (areaMatch.Success)
            {
                var type = areaMatch.Groups["type"].Value.ToUpperInvariant();
                var bit = ParseBit(areaMatch.Groups["bit"].Value);
                ValidateBitUsage(type, bit, input);
                return new S7Address(ParseArea(areaMatch.Groups["area"].Value), 0,
                    ParseNonNegative(areaMatch.Groups["offset"].Value, "byte offset"), bit, input, address);
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
            if (type == "X" && bit < 0) throw new IndustrialAddressParseException("S7 bit address requires a bit index: " + address);
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
}
