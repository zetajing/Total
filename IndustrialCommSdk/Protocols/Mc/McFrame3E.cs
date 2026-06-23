using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Mc
{
    /// <summary>
    /// 三菱 PLC MC 协议 3E 帧的构建与解析工具类。
    /// 提供读写字、读写位等请求帧的构建方法，以及响应帧的解析辅助方法。
    /// </summary>
    internal static class McFrame3E
    {
        /// <summary>子报头（Subheader），表示请求帧，值为 0x5000。</summary>
        private const ushort SubheaderRequest = 0x5000;
        /// <summary>网络编号，默认 0x00。</summary>
        private const byte NetworkNo = 0x00;
        /// <summary>PC 编号，默认 0xFF。</summary>
        private const byte PcNo = 0xFF;
        /// <summary>目标 I/O 编号，默认 0x03FF。</summary>
        private const ushort DestIoNo = 0x03FF;
        /// <summary>目标站号，默认 0x00。</summary>
        private const byte DestStationNo = 0x00;
        /// <summary>定时器默认值，0x000A（10 个单位）。</summary>
        private const ushort DefaultTimer = 0x000A;
        /// <summary>批量读取命令码，0x0401。</summary>
        private const ushort CmdBatchRead = 0x0401;
        /// <summary>批量写入命令码，0x1401。</summary>
        private const ushort CmdBatchWrite = 0x1401;
        /// <summary>字设备子命令，0x0000。</summary>
        private const ushort SubcmdWord = 0x0000;
        /// <summary>位设备子命令，0x0001。</summary>
        private const ushort SubcmdBit = 0x0001;

        /// <summary>
        /// 构建批量读取字设备的请求帧。
        /// </summary>
        /// <param name="start">起始地址（字设备）。</param>
        /// <param name="count">读取的字数。</param>
        /// <returns>完整的 3E 帧字节数组。</returns>
        public static byte[] BuildReadWordsRequest(McAddress start, ushort count)
        {
            return BuildReadRequest(start, count, SubcmdWord);
        }

        /// <summary>
        /// 构建批量读取位设备的请求帧。
        /// </summary>
        /// <param name="start">起始地址（位设备）。</param>
        /// <param name="count">读取的位数。</param>
        /// <returns>完整的 3E 帧字节数组。</returns>
        public static byte[] BuildReadBitsRequest(McAddress start, ushort count)
        {
            return BuildReadRequest(start, count, SubcmdBit);
        }

        /// <summary>
        /// 构建批量写入字设备的请求帧。
        /// </summary>
        /// <param name="start">起始地址（字设备）。</param>
        /// <param name="values">待写入的字数据数组。</param>
        /// <returns>完整的 3E 帧字节数组。</returns>
        public static byte[] BuildWriteWordsRequest(McAddress start, ushort[] values)
        {
            var appLength = 11 + values.Length * 2;
            var packet = BuildPacket(appLength);
            var pos = 9;
            WriteU16LE(packet, ref pos, CmdBatchWrite);
            WriteU16LE(packet, ref pos, SubcmdWord);
            packet[pos++] = GetDeviceCode(start.DeviceType);
            WriteI32LE3(packet, ref pos, start.Index);
            WriteU16LE(packet, ref pos, (ushort)values.Length);
            foreach (var value in values)
            {
                WriteU16LE(packet, ref pos, value);
            }
            return packet;
        }

        /// <summary>
        /// 构建批量写入位设备的请求帧。
        /// </summary>
        /// <param name="start">起始地址（位设备）。</param>
        /// <param name="values">待写入的布尔值数组。</param>
        /// <returns>完整的 3E 帧字节数组。</returns>
        public static byte[] BuildWriteBitsRequest(McAddress start, bool[] values)
        {
            var byteLength = (values.Length + 7) / 8;
            var appLength = 11 + byteLength;
            var packet = BuildPacket(appLength);
            var pos = 9;
            WriteU16LE(packet, ref pos, CmdBatchWrite);
            WriteU16LE(packet, ref pos, SubcmdBit);
            packet[pos++] = GetDeviceCode(start.DeviceType);
            WriteI32LE3(packet, ref pos, start.Index);
            WriteU16LE(packet, ref pos, (ushort)values.Length);
            var packed = Common.RegisterValueCodec.PackBits(values);
            System.Buffer.BlockCopy(packed, 0, packet, pos, packed.Length);
            return packet;
        }

        /// <summary>
        /// 解析 MC 协议 3E 帧的响应数据。
        /// 检查响应长度和结束码，提取有效载荷。
        /// </summary>
        /// <param name="response">完整的响应帧字节数组。</param>
        /// <returns>去除头部和结束码后的有效数据载荷。</returns>
        /// <exception cref="IndustrialProtocolException">响应长度不足或结束码不为 0x0000 时引发。</exception>
        public static byte[] ParseResponse(byte[] response)
        {
            if (response == null || response.Length < 11)
            {
                throw new IndustrialProtocolException("Invalid MC response length.");
            }

            var pos = 7;
            var responseLength = ReadU16LE(response, pos);
            pos += 2;
            var endCode = ReadU16LE(response, pos);
            pos += 2;

            if (endCode != 0x0000)
            {
                throw new IndustrialProtocolException(string.Format("MC end code: 0x{0:X4}", endCode));
            }

            var payloadLength = responseLength - 2;
            var payload = new byte[payloadLength];
            System.Buffer.BlockCopy(response, pos, payload, 0, payloadLength);
            return payload;
        }

        /// <summary>
        /// 以小端序从字节数组中读取一个 16 位无符号整数。
        /// </summary>
        /// <param name="buffer">源字节数组。</param>
        /// <param name="offset">读取起始偏移量。</param>
        /// <returns>读取的 16 位无符号整数值。</returns>
        public static ushort ReadU16LE(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        /// <summary>
        /// 将两个字节数组合并为一个新数组。
        /// </summary>
        /// <param name="header">前置的头部字节数组。</param>
        /// <param name="body">后置的主体字节数组。</param>
        /// <returns>合并后的字节数组。</returns>
        public static byte[] Combine(byte[] header, byte[] body)
        {
            var buffer = new byte[header.Length + body.Length];
            System.Buffer.BlockCopy(header, 0, buffer, 0, header.Length);
            System.Buffer.BlockCopy(body, 0, buffer, header.Length, body.Length);
            return buffer;
        }

        /// <summary>
        /// 将字节数组转换为 16 位无符号整数寄存器数组（每 2 字节为一个寄存器）。
        /// </summary>
        /// <param name="payload">输入字节数组，长度应为偶数。</param>
        /// <returns>转换后的寄存器数组。</returns>
        public static ushort[] ToRegisters(byte[] payload)
        {
            var registers = new ushort[payload.Length / 2];
            for (var i = 0; i < registers.Length; i++)
            {
                registers[i] = ReadU16LE(payload, i * 2);
            }
            return registers;
        }

        /// <summary>
        /// 将字节数组解包为指定位数的布尔数组。
        /// </summary>
        /// <param name="payload">包含位数据的字节数组。</param>
        /// <param name="count">要解包的位数。</param>
        /// <returns>解包后的布尔值数组。</returns>
        public static bool[] UnpackBits(byte[] payload, ushort count)
        {
            var values = new bool[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = ((payload[i / 8] >> (i % 8)) & 1) == 1;
            }
            return values;
        }

        /// <summary>
        /// 构建批量读取请求帧（字或位设备）。
        /// </summary>
        /// <param name="start">起始地址。</param>
        /// <param name="count">读取数量（字或位）。</param>
        /// <param name="subcommand">子命令，<see cref="SubcmdWord"/> 或 <see cref="SubcmdBit"/>。</param>
        /// <returns>完整的读取请求帧字节数组。</returns>
        private static byte[] BuildReadRequest(McAddress start, ushort count, ushort subcommand)
        {
            var packet = BuildPacket(11);
            var pos = 9;
            WriteU16LE(packet, ref pos, CmdBatchRead);
            WriteU16LE(packet, ref pos, subcommand);
            packet[pos++] = GetDeviceCode(start.DeviceType);
            WriteI32LE3(packet, ref pos, start.Index);
            WriteU16LE(packet, ref pos, count);
            return packet;
        }

        /// <summary>
        /// 构建 3E 帧的公共报文头（不包含应用数据部分）。
        /// 包含子报头、网络号、PC 号、目标 I/O 号、站号、请求数据长度和定时器。
        /// </summary>
        /// <param name="appDataLength">应用数据的长度。</param>
        /// <returns>包含完整报头的字节数组（头部 9 字节 + 应用数据空间）。</returns>
        private static byte[] BuildPacket(int appDataLength)
        {
            var packet = new byte[9 + appDataLength];
            var pos = 0;
            WriteU16LE(packet, ref pos, SubheaderRequest);
            packet[pos++] = NetworkNo;
            packet[pos++] = PcNo;
            WriteU16LE(packet, ref pos, DestIoNo);
            packet[pos++] = DestStationNo;
            WriteU16LE(packet, ref pos, (ushort)(appDataLength + 2));
            WriteU16LE(packet, ref pos, DefaultTimer);
            return packet;
        }

        /// <summary>
        /// 以小端序向字节数组指定位置写入一个 16 位无符号整数，并自动推进偏移量。
        /// </summary>
        /// <param name="buffer">目标字节数组。</param>
        /// <param name="offset">起始偏移量（写入后递增 2）。</param>
        /// <param name="value">待写入的 16 位无符号整数。</param>
        private static void WriteU16LE(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
        }

        /// <summary>
        /// 以小端序向字节数组指定位置写入一个 24 位（3 字节）整数，并自动推进偏移量。
        /// 用于写入 MC 协议中的设备地址索引。
        /// </summary>
        /// <param name="buffer">目标字节数组。</param>
        /// <param name="offset">起始偏移量（写入后递增 3）。</param>
        /// <param name="value">待写入的 32 位整数值（仅低 24 位有效）。</param>
        private static void WriteI32LE3(byte[] buffer, ref int offset, int value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
        }

        /// <summary>
        /// 根据设备类型获取对应的 MC 协议设备代码。
        /// </summary>
        /// <param name="type">MC 设备类型。</param>
        /// <returns>表示该设备的单字节代码。</returns>
        /// <exception cref="IndustrialProtocolException">遇到不受支持的设备类型时引发。</exception>
        private static byte GetDeviceCode(McDeviceType type)
        {
            switch (type)
            {
                case McDeviceType.D: return 0xA8;
                case McDeviceType.W: return 0xB4;
                case McDeviceType.R: return 0xAF;
                case McDeviceType.SD: return 0xA9;
                case McDeviceType.Z: return 0xCC;
                case McDeviceType.ZR: return 0xB0;
                case McDeviceType.M: return 0x90;
                case McDeviceType.X: return 0x9C;
                case McDeviceType.Y: return 0x9D;
                case McDeviceType.L: return 0x92;
                case McDeviceType.TN: return 0xC0;
                case McDeviceType.SS: return 0xC1;
                case McDeviceType.CN: return 0xC5;
                default: throw new IndustrialProtocolException("Unsupported MC device type.");
            }
        }
    }
}
