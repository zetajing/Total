using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Common
{
    /// <summary>
    /// 寄存器值编解码器。提供静态方法，用于在不同数据类型与 Modbus 寄存器值之间进行双向转换，
    /// 包括字节与寄存器的互转、数据值的编码与解码、位打包以及字节序转换等操作。
    /// </summary>
    public static class RegisterValueCodec
    {
        /// <summary>
        /// 获取指定数据类型在 Modbus 寄存器中所需要的长度（以 16 位寄存器为单位）。
        /// 对于 <see cref="DataType.String"/> 和 <see cref="DataType.ByteArray"/> 类型，需要提供具体的值以计算长度。
        /// </summary>
        /// <param name="dataType">要查询的数据类型。</param>
        /// <param name="value">可选的数值，用于计算可变长度类型（如字符串和字节数组）所需的寄存器长度。</param>
        /// <returns>所需的 16 位寄存器数量。</returns>
        /// <exception cref="IndustrialDataConversionException">当 <paramref name="dataType"/> 为不支持的数据类型时抛出。</exception>
        public static ushort GetRequiredRegisterLength(DataType dataType, object value = null)
        {
            switch (dataType)
            {
                case DataType.Bool:
                case DataType.Byte:
                case DataType.Char:
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

        /// <summary>
        /// 将 Modbus 寄存器数组转换为字节数组。每个 16 位寄存器按大端序（高字节在前）拆分为两个字节。
        /// </summary>
        /// <param name="registers">要转换的 16 位寄存器数组。</param>
        /// <returns>转换后的字节数组，长度为寄存器数量的两倍。</returns>
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

        /// <summary>
        /// 将字节数组转换为 Modbus 寄存器数组。每两个字节按大端序组合为一个 16 位寄存器。
        /// 如果字节数为奇数，则在末尾补一个零字节。
        /// </summary>
        /// <param name="bytes">要转换的字节数组。长度为奇数时自动填充零字节。</param>
        /// <returns>转换后的 16 位寄存器数组。</returns>
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

        /// <summary>
        /// 将寄存器数组转换为 <see cref="DataValue"/> 对象。先将寄存器转换为字节数组，再根据读取请求的数据类型进行解析。
        /// </summary>
        /// <param name="request">读取请求，包含要读取的地址、数据类型和长度信息。</param>
        /// <param name="registers">从设备读取的 16 位寄存器数组。</param>
        /// <returns>包含解析后的值的 <see cref="DataValue"/> 对象。</returns>
        public static DataValue ToDataValue(ReadRequest request, ushort[] registers)
        {
            var bytes = GetBytesFromRegisters(registers);
            return ToDataValue(request, bytes);
        }

        /// <summary>
        /// 将布尔值数组转换为 <see cref="DataValue"/> 对象。如果请求长度大于 1，则返回整个布尔数组；
        /// 否则返回单个布尔值。同时使用 <see cref="PackBits"/> 方法将布尔数组打包为字节数组。
        /// </summary>
        /// <param name="request">读取请求，包含要读取的地址、数据类型和长度信息。</param>
        /// <param name="values">从设备读取的布尔值数组。</param>
        /// <returns>包含解析后的布尔值的 <see cref="DataValue"/> 对象。</returns>
        public static DataValue ToDataValue(ReadRequest request, bool[] values)
        {
            object value = request.Length > 1 ? (object)values : values.FirstOrDefault();
            return new DataValue(request.Address, request.DataType, value, PackBits(values), QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        /// <summary>
        /// 将字节数组转换为 <see cref="DataValue"/> 对象。根据读取请求中指定的数据类型，
        /// 将字节数组解析为相应的 .NET 类型（如 <see cref="short"/>、<see cref="int"/>、<see cref="float"/>、字符串等）。
        /// 多字节数值在解析前会从小端序转换为主机字节序。
        /// </summary>
        /// <param name="request">读取请求，包含要读取的地址、数据类型和长度信息。</param>
        /// <param name="bytes">从设备读取的原始字节数组。</param>
        /// <returns>包含按指定数据类型解析后的值的 <see cref="DataValue"/> 对象。</returns>
        /// <exception cref="IndustrialDataConversionException">当 <see cref="ReadRequest.DataType"/> 为不支持的数据类型时抛出。</exception>
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

        /// <summary>
        /// 将写入请求编码为 Modbus 寄存器数组。先通过 <see cref="EncodeBytes"/> 将值编码为字节数组，再转换为寄存器数组。
        /// </summary>
        /// <param name="request">写入请求，包含要写入的地址、数据类型和值。</param>
        /// <returns>编码后的 16 位寄存器数组，可直接用于 Modbus 写入操作。</returns>
        public static ushort[] EncodeRegisters(WriteRequest request)
        {
            return GetRegistersFromBytes(EncodeBytes(request));
        }

        /// <summary>
        /// 将写入请求中的值编码为布尔数组。支持 <see cref="bool[]"/>、<see cref="IEnumerable{bool}"/> 和可转换为布尔值的单一值。
        /// </summary>
        /// <param name="request">写入请求，包含要写入的值。</param>
        /// <returns>编码后的布尔值数组。</returns>
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

        /// <summary>
        /// 将写入请求中的值编码为字节数组。根据请求中指定的数据类型，将值转换为适合 Modbus 传输的字节表示。
        /// 多字节数值在编码时会转换为大端序（网络字节序）。对于字符串和字节数组类型，会根据寄存器长度自动填充或截断。
        /// </summary>
        /// <param name="request">写入请求，包含要写入的地址、数据类型和值。</param>
        /// <returns>编码后的字节数组，可直接用于 Modbus 写入操作。</returns>
        /// <exception cref="IndustrialDataConversionException">当 <see cref="WriteRequest.DataType"/> 为不支持的数据类型时抛出。</exception>
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

        /// <summary>
        /// 将布尔值数组打包为字节数组。每个字节的 8 个位依次对应 8 个布尔值，
        /// 第 0 位对应第一个布尔值，依此类推。打包结果长度为 (n + 7) / 8 字节。
        /// </summary>
        /// <param name="values">要打包的布尔值数组。</param>
        /// <returns>打包后的字节数组。</returns>
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

        /// <summary>
        /// 将大端序字节数组转换为指定大小的小端序字节数组。
        /// 从源数组末尾开始截取指定长度的字节，然后反转顺序。
        /// </summary>
        /// <param name="source">大端序的源字节数组。</param>
        /// <param name="size">目标字节数组的大小（字节数）。</param>
        /// <returns>转换后的小端序字节数组。</returns>
        private static byte[] ToLittleEndian(byte[] source, int size)
        {
            var buffer = new byte[size];
            var copyLength = Math.Min(size, source.Length);
            Buffer.BlockCopy(source, 0, buffer, size - copyLength, copyLength);
            Array.Reverse(buffer);
            return buffer;
        }

        /// <summary>
        /// 将小端序字节数组转换为大端序字节数组（网络字节序）。
        /// 直接反转整个数组的顺序。
        /// </summary>
        /// <param name="littleEndianBytes">小端序的源字节数组。</param>
        /// <returns>转换后的大端序字节数组。</returns>
        private static byte[] ToBigEndian(byte[] littleEndianBytes)
        {
            Array.Reverse(littleEndianBytes);
            return littleEndianBytes;
        }

        /// <summary>
        /// 根据字节数计算所需的 16 位寄存器长度。至少返回 1 个寄存器，
        /// 计算公式为：<c>Max(1, (byteCount + 1) / 2)</c>。
        /// </summary>
        /// <param name="byteCount">字节数。</param>
        /// <returns>所需的 16 位寄存器数量。</returns>
        private static ushort GetRegisterLengthFromByteCount(int byteCount)
        {
            return (ushort)Math.Max(1, (byteCount + 1) / 2);
        }

        /// <summary>
        /// 将字节数组适配到指定的寄存器长度。目标长度为 2 × registerLength 字节。
        /// 如果源数组超过目标长度，则抛出 <see cref="IndustrialDataConversionException"/>；
        /// 如果不足，则在高位补零填充。
        /// </summary>
        /// <param name="bytes">要适配的源字节数组。</param>
        /// <param name="registerLength">目标寄存器长度（16 位寄存器数量）。</param>
        /// <returns>适配后的字节数组，长度为 2 × registerLength 字节。</returns>
        /// <exception cref="IndustrialDataConversionException">当 <paramref name="bytes"/> 的长度超过目标长度时抛出。</exception>
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
