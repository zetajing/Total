using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Common
{
    internal static class RegisterValueCodec
    {
        public static ushort GetRequiredRegisterLength(DataType dataType, object value = null)
        {
            switch (dataType)
            {
                case DataType.Bool:
                case DataType.Int16:
                case DataType.UInt16:
                    return 1;
                case DataType.Int32:
                case DataType.UInt32:
                case DataType.Float:
                    return 2;
                case DataType.Double:
                    return 4;
                case DataType.String:
                    return GetRegisterLengthFromByteCount(Encoding.ASCII.GetByteCount(Convert.ToString(value) ?? string.Empty));
                case DataType.ByteArray:
                    var bytes = value as byte[];
                    return GetRegisterLengthFromByteCount(bytes == null ? 0 : bytes.Length);
                default:
                    throw new IndustrialDataConversionException("Unsupported data type.");
            }
        }

        public static byte[] GetBytesFromRegisters(ushort[] registers)
        {
            var bytes = new byte[registers.Length * 2];
            for (var i = 0; i < registers.Length; i++)
            {
                bytes[i * 2] = (byte)(registers[i] >> 8);
                bytes[i * 2 + 1] = (byte)(registers[i] & 0xFF);
            }
            return bytes;
        }

        public static ushort[] GetRegistersFromBytes(byte[] bytes)
        {
            if (bytes.Length % 2 != 0)
            {
                var padded = new byte[bytes.Length + 1];
                Buffer.BlockCopy(bytes, 0, padded, 0, bytes.Length);
                bytes = padded;
            }

            var registers = new ushort[bytes.Length / 2];
            for (var i = 0; i < registers.Length; i++)
            {
                registers[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
            }
            return registers;
        }

        public static DataValue ToDataValue(ReadRequest request, ushort[] registers)
        {
            var bytes = GetBytesFromRegisters(registers);
            return ToDataValue(request, bytes);
        }

        public static DataValue ToDataValue(ReadRequest request, bool[] values)
        {
            object value = request.Length > 1 ? (object)values : values.FirstOrDefault();
            return new DataValue(request.Address, request.DataType, value, PackBits(values), QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        public static DataValue ToDataValue(ReadRequest request, byte[] bytes)
        {
            object value;

            switch (request.DataType)
            {
                case DataType.Bool:
                    value = bytes.Length > 0 && bytes[bytes.Length - 1] != 0;
                    break;
                case DataType.Int16:
                    value = BitConverter.ToInt16(ToLittleEndian(bytes, 2), 0);
                    break;
                case DataType.UInt16:
                    value = BitConverter.ToUInt16(ToLittleEndian(bytes, 2), 0);
                    break;
                case DataType.Int32:
                    value = BitConverter.ToInt32(ToLittleEndian(bytes, 4), 0);
                    break;
                case DataType.UInt32:
                    value = BitConverter.ToUInt32(ToLittleEndian(bytes, 4), 0);
                    break;
                case DataType.Float:
                    value = BitConverter.ToSingle(ToLittleEndian(bytes, 4), 0);
                    break;
                case DataType.Double:
                    value = BitConverter.ToDouble(ToLittleEndian(bytes, 8), 0);
                    break;
                case DataType.String:
                    value = Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
                    break;
                case DataType.ByteArray:
                    value = bytes;
                    break;
                default:
                    throw new IndustrialDataConversionException("Unsupported data type.");
            }

            return new DataValue(request.Address, request.DataType, value, bytes, QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        public static ushort[] EncodeRegisters(WriteRequest request)
        {
            return GetRegistersFromBytes(EncodeBytes(request));
        }

        public static bool[] EncodeBits(WriteRequest request)
        {
            if (request.Value is bool[])
            {
                return (bool[])request.Value;
            }

            if (request.Value is IEnumerable<bool>)
            {
                return ((IEnumerable<bool>)request.Value).ToArray();
            }

            return new[] { Convert.ToBoolean(request.Value) };
        }

        public static byte[] EncodeBytes(WriteRequest request)
        {
            var registerLength = request.Length == 0 ? GetRequiredRegisterLength(request.DataType, request.Value) : request.Length;

            switch (request.DataType)
            {
                case DataType.Bool:
                    return new[] { Convert.ToBoolean(request.Value) ? (byte)1 : (byte)0 };
                case DataType.Int16:
                    return ToBigEndian(BitConverter.GetBytes(Convert.ToInt16(request.Value)));
                case DataType.UInt16:
                    return ToBigEndian(BitConverter.GetBytes(Convert.ToUInt16(request.Value)));
                case DataType.Int32:
                    return ToBigEndian(BitConverter.GetBytes(Convert.ToInt32(request.Value)));
                case DataType.UInt32:
                    return ToBigEndian(BitConverter.GetBytes(Convert.ToUInt32(request.Value)));
                case DataType.Float:
                    return ToBigEndian(BitConverter.GetBytes(Convert.ToSingle(request.Value)));
                case DataType.Double:
                    return ToBigEndian(BitConverter.GetBytes(Convert.ToDouble(request.Value)));
                case DataType.String:
                    return FitToRegisterLength(Encoding.ASCII.GetBytes(Convert.ToString(request.Value)), registerLength);
                case DataType.ByteArray:
                    return FitToRegisterLength((byte[])request.Value, registerLength);
                default:
                    throw new IndustrialDataConversionException("Unsupported data type.");
            }
        }

        public static byte[] PackBits(bool[] values)
        {
            var bytes = new byte[(values.Length + 7) / 8];
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i])
                {
                    bytes[i / 8] |= (byte)(1 << (i % 8));
                }
            }
            return bytes;
        }

        private static byte[] ToLittleEndian(byte[] source, int size)
        {
            var buffer = new byte[size];
            var copyLength = Math.Min(size, source.Length);
            Buffer.BlockCopy(source, 0, buffer, size - copyLength, copyLength);
            Array.Reverse(buffer);
            return buffer;
        }

        private static byte[] ToBigEndian(byte[] littleEndianBytes)
        {
            Array.Reverse(littleEndianBytes);
            return littleEndianBytes;
        }

        private static ushort GetRegisterLengthFromByteCount(int byteCount)
        {
            return (ushort)Math.Max(1, (byteCount + 1) / 2);
        }

        private static byte[] FitToRegisterLength(byte[] bytes, ushort registerLength)
        {
            var targetLength = Math.Max(1, (int)registerLength) * 2;
            if (bytes.Length > targetLength)
            {
                throw new IndustrialDataConversionException("Value length exceeds configured Modbus register length.");
            }

            if (bytes.Length == targetLength)
            {
                return bytes;
            }

            var buffer = new byte[targetLength];
            Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);
            return buffer;
        }
    }
}
