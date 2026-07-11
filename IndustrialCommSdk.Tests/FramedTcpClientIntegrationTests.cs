using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class FramedTcpClientIntegrationTests
    {
        [Test]
        public async Task ReceiveFrameAsync_HandlesPartialAndStickyPacketsFromLoopbackServer()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                {
                    await stream.WriteAsync(new byte[] { (byte)'o' }, 0, 1);
                    await Task.Delay(20);
                    var remaining = Encoding.UTF8.GetBytes("ne\ntwo\n");
                    await stream.WriteAsync(remaining, 0, remaining.Length);
                }
            });

            try
            {
                using (var client = new FramedTcpClient(
                    new TcpTransportOptions { Host = "127.0.0.1", Port = port, AutoReconnect = false },
                    new DelimiterMessageFramer(new byte[] { 10 }, 32)))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    Assert.AreEqual("one", Encoding.UTF8.GetString(await client.ReceiveFrameAsync(CancellationToken.None)));
                    Assert.AreEqual("two", Encoding.UTF8.GetString(await client.ReceiveFrameAsync(CancellationToken.None)));
                }
                await server;
            }
            finally { listener.Stop(); }
        }
    }
}
