using System;
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

                using (var server = new Snap7ServerHost(db1))
                {
                    server.Start(options.Address, options.Port);
                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Snap7Server 已启动：{0}:{1}，DB1 大小={2}，DB1.DBX0.0={3}，DB1.DBD8={4}",
                            options.Address,
                            options.Port,
                            db1.Length,
                            options.BoolValue,
                            options.FloatValue));

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
            var mask = (byte)(1 << bitOffset);
            if (value)
                buffer[byteOffset] |= mask;
            else
                buffer[byteOffset] &= (byte)~mask;
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
        }

        private sealed class ServerOptions
        {
            public string Address { get; private set; } = "0.0.0.0";
            public int Port { get; private set; } = 102;
            public int DbSize { get; private set; } = 256;
            public float FloatValue { get; private set; } = 10.0f;
            public bool BoolValue { get; private set; } = true;

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
            }
        }
    }
}
