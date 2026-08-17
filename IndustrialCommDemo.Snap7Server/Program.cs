using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace IndustrialCommDemo.Snap7Server
{
    internal static class Program
    {
        private static readonly ManualResetEventSlim Shutdown = new ManualResetEventSlim(false);

        private static int Main(string[] args)
        {
            try
            {
                var options = ServerOptions.Parse(args);
                var db1 = new byte[options.DbSize];
                SetBit(db1, 0, 0, options.BoolValue);
                WriteFloat(db1, 8, options.FloatValue);
                foreach (var point in options.PointSpecs)
                    ApplyPointSpec(db1, point);

                using (var server = new Snap7ServerHost(db1))
                {
                    server.Start(options.Address, options.Port);
                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Snap7Server 已启动：{0}:{1}，DB1 大小={2}，DB1.DBX0.0={3}，DB1.DBD8={4}，追加点位={5}",
                            options.Address,
                            options.Port,
                            db1.Length,
                            options.BoolValue,
                            options.FloatValue,
                            options.PointSpecs.Count));

                    Console.CancelKeyPress += OnCancelKeyPress;
                    while (!Shutdown.Wait(500)) { }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Snap7Server 启动失败：" + ex.Message);
                return 1;
            }
            finally
            {
                Shutdown.Dispose();
            }
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Shutdown.Set();
        }

        private static void SetBit(byte[] buffer, int byteOffset, int bitOffset, bool value)
        {
            EnsureRange(buffer, byteOffset, 1, "BOOL");
            if (bitOffset < 0 || bitOffset > 7)
                throw new ArgumentOutOfRangeException(nameof(bitOffset), "BOOL 位偏移必须在 0 到 7 之间。");

            var mask = (byte)(1 << bitOffset);
            if (value)
                buffer[byteOffset] |= mask;
            else
                buffer[byteOffset] &= (byte)~mask;
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            EnsureRange(buffer, offset, 4, "REAL");
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
        }

        private static void ApplyPointSpec(byte[] buffer, string specification)
        {
            var text = (specification ?? string.Empty).Trim();
            var equalsIndex = text.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex == text.Length - 1)
                throw new FormatException(
                    "点位格式错误：应为 REAL DB1.DBD12=20.0、INT DB1.DBW2=1500 或 BOOL DB1.DBX0.1=false。错误值：" + specification);

            var definition = text.Substring(0, equalsIndex).Trim();
            var valueText = text.Substring(equalsIndex + 1).Trim();
            var tokens = definition.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string type;
            string address;
            if (tokens.Length == 1)
            {
                address = tokens[0];
                type = InferPointType(address);
            }
            else if (tokens.Length == 2)
            {
                if (IsPointType(tokens[0]))
                {
                    type = NormalizePointType(tokens[0]);
                    address = tokens[1];
                }
                else if (IsPointType(tokens[1]))
                {
                    type = NormalizePointType(tokens[1]);
                    address = tokens[0];
                }
                else
                {
                    throw new FormatException(
                        "点位类型必须是 BOOL、INT、DINT 或 REAL：" + specification);
                }
            }
            else
            {
                throw new FormatException(
                    "点位格式错误：应为 REAL DB1.DBD12=20.0、INT DB1.DBW2=1500 或 BOOL DB1.DBX0.1=false。错误值：" + specification);
            }

            if (string.Equals(type, "REAL", StringComparison.Ordinal))
            {
                var offset = ParseDbByteOffset(address, "DBD");
                WriteFloat(buffer, offset, ParsePointFloat(address, valueText));
                return;
            }

            if (string.Equals(type, "INT", StringComparison.Ordinal))
            {
                var offset = ParseDbByteOffset(address, "DBW");
                WriteInt16(buffer, offset, ParsePointInt16(address, valueText));
                return;
            }

            if (string.Equals(type, "DINT", StringComparison.Ordinal))
            {
                var offset = ParseDbByteOffset(address, "DBD");
                WriteInt32(buffer, offset, ParsePointInt32(address, valueText));
                return;
            }

            var bitAddress = ParseDbBitAddress(address);
            SetBit(buffer, bitAddress.ByteOffset, bitAddress.BitOffset,
                ParsePointBool(address, valueText));
        }

        private static float ParsePointFloat(string address, string value)
        {
            float result;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                throw new FormatException("点位 " + address + " 的 REAL 值必须是数字：" + value);
            return result;
        }

        private static bool ParsePointBool(string address, string value)
        {
            bool result;
            if (!bool.TryParse(value, out result))
                throw new FormatException("点位 " + address + " 的 BOOL 值必须是 true 或 false：" + value);
            return result;
        }

        private static short ParsePointInt16(string address, string value)
        {
            short result;
            if (!short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw new FormatException("点位 " + address + " 的 INT 值必须是整数：" + value);
            return result;
        }

        private static int ParsePointInt32(string address, string value)
        {
            int result;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw new FormatException("点位 " + address + " 的 DINT 值必须是整数：" + value);
            return result;
        }

        private static void WriteInt16(byte[] buffer, int offset, short value)
        {
            EnsureRange(buffer, offset, 2, "INT");
            var raw = unchecked((ushort)value);
            buffer[offset] = (byte)(raw >> 8);
            buffer[offset + 1] = (byte)raw;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            EnsureRange(buffer, offset, 4, "DINT");
            var raw = unchecked((uint)value);
            buffer[offset] = (byte)(raw >> 24);
            buffer[offset + 1] = (byte)(raw >> 16);
            buffer[offset + 2] = (byte)(raw >> 8);
            buffer[offset + 3] = (byte)raw;
        }

        private static bool IsPointType(string value)
        {
            return string.Equals(value, "REAL", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "FLOAT", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "INT", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "INT16", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "DINT", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "INT32", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "BOOL", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "BOOLEAN", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePointType(string value)
        {
            if (string.Equals(value, "BOOL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "BOOLEAN", StringComparison.OrdinalIgnoreCase))
                return "BOOL";
            if (string.Equals(value, "INT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "INT16", StringComparison.OrdinalIgnoreCase))
                return "INT";
            if (string.Equals(value, "DINT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "INT32", StringComparison.OrdinalIgnoreCase))
                return "DINT";
            return "REAL";
        }

        private static string InferPointType(string address)
        {
            if (address.StartsWith("DB1.DBX", StringComparison.OrdinalIgnoreCase))
                return "BOOL";
            if (address.StartsWith("DB1.DBW", StringComparison.OrdinalIgnoreCase))
                return "INT";
            if (address.StartsWith("DB1.DBD", StringComparison.OrdinalIgnoreCase))
                return "REAL";
            throw new FormatException(
                "无法从地址推断点位类型，请显式写 BOOL、INT、DINT 或 REAL：" + address);
        }

        private static int ParseDbByteOffset(string address, string area)
        {
            var prefix = "DB1." + area;
            if (string.IsNullOrWhiteSpace(address) ||
                !address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("只支持 DB1." + area + " 地址：" + address);
            }

            var offsetText = address.Substring(prefix.Length);
            int offset;
            if (!int.TryParse(offsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset) || offset < 0)
                throw new FormatException("DB 字节偏移必须是非负整数：" + address);
            return offset;
        }

        private static DbBitAddress ParseDbBitAddress(string address)
        {
            const string prefix = "DB1.DBX";
            if (string.IsNullOrWhiteSpace(address) ||
                !address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("BOOL 只支持 DB1.DBX<byte>.<bit> 地址：" + address);
            }

            var suffix = address.Substring(prefix.Length);
            var separator = suffix.IndexOf('.');
            if (separator <= 0 || separator == suffix.Length - 1 || suffix.IndexOf('.', separator + 1) >= 0)
                throw new FormatException("BOOL 地址必须写成 DB1.DBX<byte>.<bit>：" + address);

            int byteOffset;
            int bitOffset;
            if (!int.TryParse(suffix.Substring(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out byteOffset) ||
                !int.TryParse(suffix.Substring(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out bitOffset) ||
                byteOffset < 0 || bitOffset < 0 || bitOffset > 7)
            {
                throw new FormatException("BOOL 字节偏移必须非负，位偏移必须在 0 到 7 之间：" + address);
            }

            return new DbBitAddress(byteOffset, bitOffset);
        }

        private static void EnsureRange(byte[] buffer, int offset, int length, string type)
        {
            if (offset < 0 || length < 0 || offset > buffer.Length - length)
                throw new ArgumentOutOfRangeException(
                    "address",
                    string.Format(CultureInfo.InvariantCulture,
                        "{0} 点位超出 DB1 大小 {1} 字节：offset={2}, length={3}。",
                        type,
                        buffer.Length,
                        offset,
                        length));
        }

        private struct DbBitAddress
        {
            public DbBitAddress(int byteOffset, int bitOffset)
            {
                ByteOffset = byteOffset;
                BitOffset = bitOffset;
            }

            public int ByteOffset { get; private set; }
            public int BitOffset { get; private set; }
        }

        private sealed class ServerOptions
        {
            public string Address { get; private set; } = "0.0.0.0";
            public int Port { get; private set; } = 102;
            public int DbSize { get; private set; } = 256;
            public float FloatValue { get; private set; } = 10.0f;
            public bool BoolValue { get; private set; } = true;
            public IList<string> PointSpecs { get; private set; } = new List<string>();

            public static ServerOptions Parse(string[] args)
            {
                var options = new ServerOptions();
                for (var i = 0; i < (args == null ? 0 : args.Length); i++)
                {
                    var name = args[i];
                    if (string.Equals(name, "--help", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "-h", StringComparison.OrdinalIgnoreCase))
                    {
                        PrintHelp();
                        Environment.Exit(0);
                    }

                    var value = i + 1 < args.Length ? args[++i] : null;
                    switch (name.ToLowerInvariant())
                    {
                        case "--address":
                            options.Address = RequireValue(name, value);
                            break;
                        case "--port":
                            options.Port = ParseInt(name, value);
                            break;
                        case "--db-size":
                            options.DbSize = ParseInt(name, value);
                            break;
                        case "--float":
                            options.FloatValue = ParseFloat(name, value);
                            break;
                        case "--bool":
                            options.BoolValue = ParseBool(name, value);
                            break;
                        case "--point":
                            options.PointSpecs.Add(RequireValue(name, value));
                            break;
                        default:
                            throw new ArgumentException("未知参数：" + name + "。使用 --help 查看用法。");
                    }
                }

                if (options.DbSize < 16)
                    throw new ArgumentOutOfRangeException("--db-size", "DB1 大小至少为 16 字节。");
                if (options.DbSize > 65535)
                    throw new ArgumentOutOfRangeException("--db-size", "DB1 大小不能超过 65535 字节。");
                if (options.Port < 1 || options.Port > 65535)
                    throw new ArgumentOutOfRangeException("--port", "端口必须在 1 到 65535 之间。");
                return options;
            }

            private static string RequireValue(string name, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException(name + " 需要一个值。");
                return value;
            }

            private static int ParseInt(string name, string value)
            {
                if (!int.TryParse(RequireValue(name, value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                    throw new ArgumentException(name + " 必须是整数。");
                return result;
            }

            private static float ParseFloat(string name, string value)
            {
                if (!float.TryParse(RequireValue(name, value), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                    throw new ArgumentException(name + " 必须是浮点数。");
                return result;
            }

            private static bool ParseBool(string name, string value)
            {
                if (!bool.TryParse(RequireValue(name, value), out var result))
                    throw new ArgumentException(name + " 必须是 true 或 false。");
                return result;
            }

            private static void PrintHelp()
            {
                Console.WriteLine("IndustrialCommDemo.Snap7Server");
                Console.WriteLine("  --address <ip>    监听地址，默认 0.0.0.0");
                Console.WriteLine("  --port <port>     监听端口，默认 102");
                Console.WriteLine("  --db-size <n>     DB1 字节数，默认 256");
                Console.WriteLine("  --float <value>   写入 DB1.DBD8 的 REAL，默认 10.0");
                Console.WriteLine("  --bool <true|false> 写入 DB1.DBX0.0，默认 true");
                Console.WriteLine("  --point <spec>    追加点位，例如 \"REAL DB1.DBD12=20.0\"、\"INT DB1.DBW2=1500\"；可重复");
            }
        }
    }
}
