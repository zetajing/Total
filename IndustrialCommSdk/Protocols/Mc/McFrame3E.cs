using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Mc
{
    internal static class McFrame3E
    {
        private const ushort SubheaderRequest = 0x5000;
        private const byte NetworkNo = 0x00;
        private const byte PcNo = 0xFF;
        private const ushort DestIoNo = 0x03FF;
        private const byte DestStationNo = 0x00;
        private const ushort DefaultTimer = 0x000A;
        private const ushort CmdBatchRead = 0x0401;
        private const ushort CmdBatchWrite = 0x1401;
        private const ushort SubcmdWord = 0x0000;
        private const ushort SubcmdBit = 0x0001;

        public static byte[] BuildReadWordsRequest(McAddress start, ushort count)
        {
            return BuildReadRequest(start, count, SubcmdWord);
        }

        public static byte[] BuildReadBitsRequest(McAddress start, ushort count)
        {
            return BuildReadRequest(start, count, SubcmdBit);
        }

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

        public static ushort ReadU16LE(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        public static byte[] Combine(byte[] header, byte[] body)
        {
            var buffer = new byte[header.Length + body.Length];
            System.Buffer.BlockCopy(header, 0, buffer, 0, header.Length);
            System.Buffer.BlockCopy(body, 0, buffer, header.Length, body.Length);
            return buffer;
        }

        public static ushort[] ToRegisters(byte[] payload)
        {
            var registers = new ushort[payload.Length / 2];
            for (var i = 0; i < registers.Length; i++)
            {
                registers[i] = ReadU16LE(payload, i * 2);
            }
            return registers;
        }

        public static bool[] UnpackBits(byte[] payload, ushort count)
        {
            var values = new bool[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = ((payload[i / 8] >> (i % 8)) & 1) == 1;
            }
            return values;
        }

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

        private static void WriteU16LE(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
        }

        private static void WriteI32LE3(byte[] buffer, ref int offset, int value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
        }

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
