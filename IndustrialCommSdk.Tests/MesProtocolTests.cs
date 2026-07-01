using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Mes;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MesProtocolTests
    {
        [Test]
        public void OnlineMessage_UsesReferenceProtocolWithoutDelimiter()
        {
            var options = Options(9312);
            var value = MesProtocolCodec.CreateOnline(options,
                new DateTimeOffset(2026, 7, 1, 10, 20, 30, TimeSpan.Zero));
            StringAssert.StartsWith("START D01,Machine,D.IP,D:MAC,", value);
            StringAssert.EndsWith("STOP", value);
            Assert.That(value, Does.Not.Contain("\r").And.Not.Contain("\n"));
        }

        [Test]
        public void TrackMessage_RoundTripsSimpleParameterObject()
        {
            var message = new FaTrackMessage
            {
                DeviceNo = "D01", DeviceName = "Machine", DeviceIp = "10.0.0.2", DeviceMac = "AA",
                DeviceTime = "2026-07-01 10:00:00",
                Message = new FaTrackBody
                {
                    Process = "FAFAL0290M0", SerialNo = "SN-1", Number = "4",
                    Parameters = new Dictionary<string, string> { ["test"] = "value,with,{braces}" },
                },
            };
            var json = MesProtocolCodec.Serialize(message);
            var parsed = MesProtocolCodec.Deserialize<FaTrackMessage>(json);
            Assert.That(parsed.Type, Is.EqualTo("FATRACK"));
            Assert.That(parsed.Message.Parameters["test"], Is.EqualTo("value,with,{braces}"));
        }

        [Test]
        public void FrameParser_HandlesSplitConcatenatedAndEscapedJson()
        {
            var parser = new MesJsonFrameParser(4096);
            Assert.That(parser.Append("noise{\"type\":\"FAC"), Is.Empty);
            var frames = parser.Append("HECK\",\"message\":{\"msg\":\"a}\\\"b\"}}{\"type\":\"FANUM\",\"message\":{\"result\":\"OK\"}}");
            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(MesProtocolCodec.ReadType(frames[0]), Is.EqualTo("FACHECK"));
            Assert.That(MesProtocolCodec.ReadType(frames[1]), Is.EqualTo("FANUM"));
        }

        [Test]
        public async Task Client_UsesOneConnectionForOnlineCheckTrackAndResult()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var checkReceived = new TaskCompletionSource<FaCheckMessage>();
            var numReceived = new TaskCompletionSource<FaNumMessage>();
            string online = null;
            string track = null;

            var server = Task.Run(async () =>
            {
                using (var accepted = await listener.AcceptTcpClientAsync())
                using (var stream = accepted.GetStream())
                {
                    online = await ReadUntilAsync(stream, "STOP", 4096);
                    var check = "{\"type\":\"FACHECK\",\"deviceNo\":\"D01\",\"deviceName\":\"Machine\",\"deviceIp\":\"D.IP\",\"deviceMac\":\"D:MAC\",\"deviceTime\":\"2026-07-01 10:00:00\",\"message\":{\"process\":\"P1\",\"serialNo\":\"SN1\",\"result\":\"OK\",\"msg\":\"\"}}";
                    var payload = Encoding.UTF8.GetBytes(check);
                    await stream.WriteAsync(payload, 0, 13);
                    await stream.WriteAsync(payload, 13, payload.Length - 13);
                    track = await ReadJsonAsync(stream, 4096);
                    var result = Encoding.UTF8.GetBytes("{\"type\":\"FANUM\",\"deviceNo\":\"D01\",\"deviceName\":\"Machine\",\"deviceIp\":\"D.IP\",\"deviceMac\":\"D:MAC\",\"deviceTime\":\"2026-07-01 10:00:01\",\"message\":{\"result\":\"OK\"}}");
                    await stream.WriteAsync(result, 0, result.Length);
                    await Task.Delay(100);
                }
            });

            using (var client = new MesTcpClient(Options(port)))
            {
                client.FaCheckReceived += (s, e) => checkReceived.TrySetResult(e.Message);
                client.FaNumReceived += (s, e) => numReceived.TrySetResult(e.Message);
                await client.ConnectAsync(CancellationToken.None);
                Assert.That((await WithTimeout(checkReceived.Task)).Message.SerialNo, Is.EqualTo("SN1"));
                await client.SendTrackAsync(new FaTrackMessage { Message = new FaTrackBody { Process = "P1", SerialNo = "SN1", Number = "1" } }, CancellationToken.None);
                Assert.That((await WithTimeout(numReceived.Task)).Message.Result, Is.EqualTo("OK"));
                await client.DisconnectAsync(CancellationToken.None);
            }
            await server;
            listener.Stop();
            StringAssert.StartsWith("START D01,Machine,D.IP,D:MAC,", online);
            Assert.That(MesProtocolCodec.ReadType(track), Is.EqualTo("FATRACK"));
        }

        [Test]
        public async Task Client_ReconnectsAndSendsOnlineAgain()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var secondOnline = new TaskCompletionSource<string>();
            var server = Task.Run(async () =>
            {
                using (var first = await listener.AcceptTcpClientAsync())
                    await ReadUntilAsync(first.GetStream(), "STOP", 4096);
                using (var second = await listener.AcceptTcpClientAsync())
                    secondOnline.TrySetResult(await ReadUntilAsync(second.GetStream(), "STOP", 4096));
            });
            var options = Options(port);
            options.AutoReconnect = true;
            options.InitialReconnectDelayMilliseconds = 20;
            options.MaximumReconnectDelayMilliseconds = 50;
            using (var client = new MesTcpClient(options))
            {
                await client.ConnectAsync(CancellationToken.None);
                StringAssert.StartsWith("START D01", await WithTimeout(secondOnline.Task));
                await client.DisconnectAsync(CancellationToken.None);
            }
            await server;
            listener.Stop();
        }

        private static MesClientOptions Options(int port) => new MesClientOptions
        { Host = "127.0.0.1", Port = port, DeviceNo = "D01", DeviceName = "Machine", DeviceIp = "D.IP", DeviceMac = "D:MAC", AutoReconnect = false };

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(3000)) != task) throw new TimeoutException();
            return await task;
        }

        private static async Task<string> ReadUntilAsync(NetworkStream stream, string marker, int max)
        {
            var builder = new StringBuilder();
            var buffer = new byte[256];
            while (!builder.ToString().EndsWith(marker, StringComparison.Ordinal) && builder.Length < max)
                builder.Append(Encoding.UTF8.GetString(buffer, 0, await stream.ReadAsync(buffer, 0, buffer.Length)));
            return builder.ToString();
        }

        private static async Task<string> ReadJsonAsync(NetworkStream stream, int max)
        {
            var parser = new MesJsonFrameParser(max);
            var buffer = new byte[256];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                foreach (var frame in parser.Append(Encoding.UTF8.GetString(buffer, 0, read))) return frame;
            }
        }
    }
}
