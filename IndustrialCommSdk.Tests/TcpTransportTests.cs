using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class TcpTransportTests
    {
        [Test]
        public async Task TcpTransport_Should_Exchange_Data_With_Server()
        {
            using (var server = new TcpTransportServer(IPAddress.Loopback, 19092))
            {
                server.DataReceived += async (sender, args) =>
                {
                    await args.Session.SendAsync(args.Payload, CancellationToken.None);
                };

                await server.StartAsync(CancellationToken.None);

                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = 19092,
                    AutoReconnect = false
                }))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    var payload = Encoding.ASCII.GetBytes("ping");
                    await client.SendAsync(payload, CancellationToken.None);
                    var response = await client.ReceiveExactAsync(payload.Length, CancellationToken.None);

                    Assert.That(Encoding.ASCII.GetString(response), Is.EqualTo("ping"));
                }

                await server.StopAsync(CancellationToken.None);
            }
        }
    }
}
