using System;
using System.Text;
using InduLink.Abstractions;
using InduLink.Exceptions;

namespace InduLink.Protocols.S7
{
    /// <summary>
    /// Decodes values from one S7 byte-area read. S7.NetPlus uses big-endian PLC
    /// representations; keeping this conversion beside the batch path prevents
    /// the merged read from changing the existing single-value semantics.
    /// </summary>
    internal static class S7BatchValueDecoder
    {
        public static int EstimateEndOffset(S7Address address, ReadRequest request)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateAddress(request, address);

            // DBX/MX/IX/QX addresses can also be used as byte-position inputs
            // when the request explicitly supplies a non-Boolean value type
            // (for example DB1.DBX8.0 + Float). Only Boolean requests are
            // bit-width reads; every other type must reserve its full byte
            // width so that the shared payload contains enough data to decode.
            if (request.DataType == DataType.Bool)
            {
                var bitCount = Math.Max(1, (int)request.Length);
                var bitOffset = Math.Max(0, address.BitOffset);
                return address.ByteOffset + (bitOffset + bitCount - 1) / 8;
            }

            return address.ByteOffset + Math.Max(1, GetByteLength(request)) - 1;
        }

        public static DataValue Decode(ReadRequest request, S7Address address, byte[] payload, int batchStartOffset)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            ValidateAddress(request, address);

            object value;
            if (request.DataType == DataType.Bool)
            {
                value = DecodeBits(request, address, payload, batchStartOffset);
            }
            else
            {
                var bytes = Slice(payload, address.ByteOffset - batchStartOffset, GetByteLength(request));
                value = DecodeBytes(request, bytes);
            }

            return new DataValue(
                request.Address,
                request.DataType,
                value,
                null,
                QualityStatus.Good,
                DateTimeOffset.UtcNow,
                null);
        }

        internal static void ValidateAddress(ReadRequest request, S7Address address)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (request.DataType == DataType.S7String && address.Area != S7Area.Db)
            {
                throw new IndustrialAddressParseException(
                    "S7 STRING[n] reads must point to a data block: " + request.Address);
            }
            if (request.DataType != DataType.Bool && address.IsBitAddress && address.BitOffset != 0)
            {
                throw new IndustrialAddressParseException(
                    "S7 non-Boolean reads using X address syntax require bit index 0: " +
                    request.Address);
            }
        }

        private static object DecodeBits(ReadRequest request, S7Address address, byte[] payload, int batchStartOffset)
        {
            var count = Math.Max(1, (int)request.Length);
            var values = new bool[count];
            var firstBit = (address.ByteOffset - batchStartOffset) * 8 + Math.Max(0, address.BitOffset);

            for (var i = 0; i < count; i++)
            {
                var bitIndex = firstBit + i;
                var byteIndex = bitIndex / 8;
                if (byteIndex < 0 || byteIndex >= payload.Length)
                    throw new IndustrialDataConversionException("S7 batch read payload does not contain the requested bit.");

                values[i] = (payload[byteIndex] & (1 << (bitIndex % 8))) != 0;
            }

            return request.Length > 1 ? (object)values : values[0];
        }

        private static object DecodeBytes(ReadRequest request, byte[] bytes)
        {
            switch (request.DataType)
            {
                case DataType.S7String:
                    return S7StringCodec.Decode(bytes, request.Length);
                case DataType.String:
                    return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
                case DataType.ByteArray:
                    return bytes;
                case DataType.Char:
                    return Convert.ToChar(bytes[0]);
                case DataType.Byte:
                    return request.Length > 1
                        ? (object)bytes
                        : global::S7.Net.Types.Byte.FromByteArray(bytes);
                case DataType.Int16:
                    return request.Length > 1
                        ? (object)global::S7.Net.Types.Int.ToArray(bytes)
                        : global::S7.Net.Types.Int.FromByteArray(bytes);
                case DataType.UInt16:
                    return request.Length > 1
                        ? (object)global::S7.Net.Types.Word.ToArray(bytes)
                        : global::S7.Net.Types.Word.FromByteArray(bytes);
                case DataType.Int32:
                    return request.Length > 1
                        ? (object)global::S7.Net.Types.DInt.ToArray(bytes)
                        : global::S7.Net.Types.DInt.FromByteArray(bytes);
                case DataType.UInt32:
                    return request.Length > 1
                        ? (object)global::S7.Net.Types.DWord.ToArray(bytes)
                        : global::S7.Net.Types.DWord.FromByteArray(bytes);
                case DataType.Float:
                    return request.Length > 1
                        ? (object)global::S7.Net.Types.Real.ToArray(bytes)
                        : global::S7.Net.Types.Real.FromByteArray(bytes);
                case DataType.Double:
                    return request.Length > 1
                        ? (object)global::S7.Net.Types.LReal.ToArray(bytes)
                        : global::S7.Net.Types.LReal.FromByteArray(bytes);
                default:
                    throw new IndustrialDataConversionException("S7 does not support data type " + request.DataType + ".");
            }
        }

        private static int GetByteLength(ReadRequest request)
        {
            var count = Math.Max(1, (int)request.Length);
            switch (request.DataType)
            {
                case DataType.S7String:
                    return S7StringCodec.GetByteLength(request.Length);
                case DataType.String:
                case DataType.ByteArray:
                    return request.Length;
                case DataType.Byte:
                    return count;
                case DataType.Char:
                    return 1;
                case DataType.Int16:
                case DataType.UInt16:
                    return count * 2;
                case DataType.Int32:
                case DataType.UInt32:
                case DataType.Float:
                    return count * 4;
                case DataType.Double:
                    return count * 8;
                case DataType.Bool:
                    return 1;
                default:
                    throw new IndustrialDataConversionException("S7 does not support data type " + request.DataType + ".");
            }
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > source.Length - length)
                throw new IndustrialDataConversionException("S7 batch read payload does not contain the requested value.");

            var result = new byte[length];
            if (length > 0) Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }
    }
}
