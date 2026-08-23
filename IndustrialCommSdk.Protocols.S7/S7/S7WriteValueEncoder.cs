using System;
using System.Text;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.S7
{
    internal static class S7WriteValueEncoder
    {
        public static byte[] EncodeString(object value, ushort length)
        {
            var bytes = Encoding.ASCII.GetBytes((value ?? string.Empty).ToString());
            return FitToConfiguredLength(bytes, length, "string");
        }

        public static byte[] EncodeS7String(object value, ushort reservedLength)
        {
            S7StringCodec.ValidateReservedLength(reservedLength);
            var bytes = Encoding.ASCII.GetBytes((value ?? string.Empty).ToString());
            if (bytes.Length > reservedLength)
                throw new IndustrialDataConversionException(string.Format(
                    "S7 STRING payload length {0} exceeds configured maximum length {1}.",
                    bytes.Length,
                    reservedLength));

            var output = new byte[S7StringCodec.GetByteLength(reservedLength)];
            output[0] = (byte)reservedLength;
            output[1] = (byte)bytes.Length;
            Buffer.BlockCopy(bytes, 0, output, 2, bytes.Length);
            return output;
        }

        public static byte[] EncodeByteArray(object value, ushort length)
        {
            var bytes = value as byte[];
            if (bytes == null)
                throw new IndustrialDataConversionException("S7 byte-array write value must be a byte array.");

            return FitToConfiguredLength(bytes, length, "byte-array");
        }

        private static byte[] FitToConfiguredLength(byte[] value, ushort length, string valueType)
        {
            if (length == 0)
                throw new IndustrialDataConversionException("S7 " + valueType + " write length must be greater than zero.");
            if (value.Length > length)
                throw new IndustrialDataConversionException(string.Format(
                    "S7 {0} write payload length {1} exceeds configured length {2}.",
                    valueType,
                    value.Length,
                    length));

            var output = new byte[length];
            Buffer.BlockCopy(value, 0, output, 0, value.Length);
            return output;
        }
    }
}
