using System;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.S7
{
    internal static class S7StringCodec
    {
        private const int HeaderLength = 2;
        private const int MaximumReservedLength = 254;

        public static int GetByteLength(int reservedLength)
        {
            ValidateReservedLength(reservedLength);
            return reservedLength + HeaderLength;
        }

        public static string Decode(byte[] bytes, int reservedLength)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            ValidateReservedLength(reservedLength);

            var requiredLength = GetByteLength(reservedLength);
            if (bytes.Length < requiredLength)
                throw new IndustrialDataConversionException(
                    string.Format("S7 STRING requires {0} bytes, but only {1} bytes were received.", requiredLength, bytes.Length));

            if (bytes[0] != reservedLength)
                throw new IndustrialDataConversionException(
                    string.Format("S7 STRING maximum length is {0}, but {1} was expected.", bytes[0], reservedLength));

            if (bytes[1] > reservedLength)
                throw new IndustrialDataConversionException(
                    string.Format("S7 STRING current length {0} exceeds maximum length {1}.", bytes[1], reservedLength));

            return global::S7.Net.Types.S7String.FromByteArray(bytes);
        }

        public static void ValidateReservedLength(int reservedLength)
        {
            if (reservedLength <= 0 || reservedLength > MaximumReservedLength)
                throw new ArgumentOutOfRangeException(
                    nameof(reservedLength),
                    reservedLength,
                    "S7 STRING maximum length must be between 1 and 254.");
        }
    }
}
