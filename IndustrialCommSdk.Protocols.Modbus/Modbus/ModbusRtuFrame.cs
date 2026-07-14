using System;
using System.Collections.Generic;
using System.Globalization;

namespace IndustrialCommSdk.Protocols.Modbus
{
    public enum ModbusRtuFrameDirection
    {
        Transmit,
        Receive
    }

    /// <summary>一次实际串口收发的 Modbus RTU ADU（包含 CRC 低字节、高字节）。</summary>
    public sealed class ModbusRtuFrameEventArgs : EventArgs
    {
        public ModbusRtuFrameEventArgs(ModbusRtuFrameDirection direction, byte[] frame, bool crcValid)
        {
            Direction = direction;
            Frame = (byte[])(frame ?? throw new ArgumentNullException(nameof(frame))).Clone();
            CrcValid = crcValid;
            Timestamp = DateTimeOffset.Now;
        }

        public ModbusRtuFrameDirection Direction { get; }
        public byte[] Frame { get; }
        public bool CrcValid { get; }
        public DateTimeOffset Timestamp { get; }
        public string Hex => BitConverter.ToString(Frame).Replace('-', ' ');
    }

    /// <summary>Modbus RTU CRC16 校验工具。CRC 在报文中按低字节、高字节排列。</summary>
    public static class ModbusRtuFrameCodec
    {
        public static byte[] ParseHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("RTU 报文不能为空。");
            var tokens = text.Split(new[] { ' ', '\t', '\r', '\n', ',', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>(tokens.Length);
            foreach (var token in tokens)
            {
                var value = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token.Substring(2) : token;
                if (value.Length != 2 || !byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                    throw new FormatException("无效的十六进制字节：" + token);
                bytes.Add(parsed);
            }
            return bytes.ToArray();
        }

        public static byte[] AppendCrc(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length < 2) throw new ArgumentException("RTU 请求至少需要站号和功能码。", nameof(payload));
            var result = new byte[payload.Length + 2];
            Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
            var crc = ComputeCrc(payload, 0, payload.Length);
            result[result.Length - 2] = (byte)(crc & 0xFF);
            result[result.Length - 1] = (byte)(crc >> 8);
            return result;
        }

        public static ushort ComputeCrc(byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 0 || offset > bytes.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            ushort crc = 0xFFFF;
            for (var index = offset; index < offset + count; index++)
            {
                crc ^= bytes[index];
                for (var bit = 0; bit < 8; bit++)
                    crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }
            return crc;
        }

        public static bool HasValidCrc(byte[] frame)
        {
            if (frame == null || frame.Length < 4) return false;
            var crc = ComputeCrc(frame, 0, frame.Length - 2);
            return frame[frame.Length - 2] == (byte)(crc & 0xFF)
                   && frame[frame.Length - 1] == (byte)(crc >> 8);
        }
    }
}
