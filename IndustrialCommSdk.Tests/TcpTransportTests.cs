using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 TCP 传输层组件（<see cref="TcpTransportServer" /> 和 <see cref="TcpTransportClient" />）的集成测试。
    ///     验证客户端与服务器之间能否基于原始 TCP 连接完成发送与接收的往返通信，
    ///     包括服务端回显模式和客户端精确字节数接收的功能。
    /// </summary>
    [TestFixture]
    public class TcpTransportTests
    {
        /// <summary>
        ///     验证 TCP 传输层的完整通信链路：
        ///     启动一个本地回环（loopback）服务器（端口 19092），服务器将收到的数据原样返回（回显模式）；
        ///     客户端连接后发送 "ping"，并通过 <c>ReceiveExactAsync</c> 精确接收相同长度的响应数据。
        ///     预期：响应内容与发送内容完全一致（"ping"）。
        /// </summary>
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
