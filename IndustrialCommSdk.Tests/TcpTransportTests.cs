using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Exceptions;
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
            var port = GetAvailablePort();
            using (var server = new TcpTransportServer(IPAddress.Loopback, port))
            {
                server.DataReceivedAsync += async (sender, args) =>
                {
                    await args.Session.SendAsync(args.Payload, CancellationToken.None);
                };

                await server.StartAsync(CancellationToken.None);

                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
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

        [Test]
        public async Task TcpTransport_Should_Preserve_Concurrent_Message_Writes()
        {
            var port = GetAvailablePort();
            using (var server = new TcpTransportServer(IPAddress.Loopback, port))
            {
                server.DataReceivedAsync += async (sender, args) =>
                    await args.Session.SendAsync(args.Payload, CancellationToken.None);
                await server.StartAsync(CancellationToken.None);

                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AutoReconnect = false
                }))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    var sends = new List<Task>();
                    for (var index = 0; index < 100; index++)
                    {
                        sends.Add(client.SendAsync(Encoding.ASCII.GetBytes(index.ToString("D8")), CancellationToken.None));
                    }

                    await Task.WhenAll(sends);
                    var response = await client.ReceiveExactAsync(800, CancellationToken.None);
                    var received = new HashSet<string>();
                    for (var offset = 0; offset < response.Length; offset += 8)
                    {
                        received.Add(Encoding.ASCII.GetString(response, offset, 8));
                    }

                    Assert.That(received.Count, Is.EqualTo(100));
                    for (var index = 0; index < 100; index++)
                    {
                        Assert.That(received, Does.Contain(index.ToString("D8")));
                    }
                }

                await server.StopAsync(CancellationToken.None);
                Assert.That(server.IsRunning, Is.False);
                Assert.That(server.SessionCount, Is.Zero);
            }
        }

        [Test]
        public void TcpTransport_Should_Reject_Invalid_Endpoint()
        {
            Assert.Throws<ArgumentException>(() => new TcpTransportClient(new TcpTransportOptions { Host = " ", Port = 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransportClient(new TcpTransportOptions { Host = "localhost", Port = 0 }));
        }

        [Test]
        public async Task TcpTransport_Should_Receive_Server_Push_Without_Known_Length()
        {
            var port = GetAvailablePort();
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            var serverTask = Task.Run(async () =>
            {
                using (var accepted = await listener.AcceptTcpClientAsync())
                {
                    var payload = Encoding.UTF8.GetBytes("server-push");
                    await accepted.GetStream().WriteAsync(payload, 0, payload.Length);
                }
            });

            try
            {
                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AutoReconnect = false
                }))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    var received = await client.ReceiveAsync(1024, CancellationToken.None);
                    Assert.That(Encoding.UTF8.GetString(received), Is.EqualTo("server-push"));
                }

                await serverTask;
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task TcpTransportServer_Should_Isolate_Event_Handler_Failures()
        {
            var port = GetAvailablePort();
            using (var server = new TcpTransportServer(IPAddress.Loopback, port))
            {
                server.SessionConnected += (sender, args) => throw new InvalidOperationException("subscriber failure");
                server.DataReceived += (sender, args) => throw new InvalidOperationException("subscriber failure");
                server.DataReceivedAsync += async (sender, args) =>
                    await args.Session.SendAsync(args.Payload, CancellationToken.None);
                await server.StartAsync(CancellationToken.None);

                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AutoReconnect = false
                }))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    await client.SendAsync(new byte[] { 0x2A }, CancellationToken.None);
                    var response = await client.ReceiveExactAsync(1, CancellationToken.None);
                    Assert.That(response[0], Is.EqualTo(0x2A));
                }
            }
        }

        [Test]
        public async Task TcpTransportServer_Should_Isolate_Async_Event_Handler_Failures()
        {
            var port = GetAvailablePort();
            using (var server = new TcpTransportServer(IPAddress.Loopback, port))
            {
                server.DataReceivedAsync += async (sender, args) =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("async subscriber failure");
                };
                server.DataReceivedAsync += (sender, args) =>
                    args.Session.SendAsync(args.Payload, CancellationToken.None);
                await server.StartAsync(CancellationToken.None);

                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AutoReconnect = false
                }))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    await client.SendAsync(new byte[] { 0x55 }, CancellationToken.None);
                    var response = await client.ReceiveExactAsync(1, CancellationToken.None);
                    Assert.That(response[0], Is.EqualTo(0x55));
                }
            }
        }

        [Test]
        public async Task TcpTransportClient_Dispose_Interrupts_And_Waits_For_Pending_Receive()
        {
            var port = GetAvailablePort();
            using (var server = new TcpTransportServer(IPAddress.Loopback, port))
            {
                await server.StartAsync(CancellationToken.None);
                var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AutoReconnect = false,
                    ReceiveTimeoutMilliseconds = 5000
                });
                await client.ConnectAsync(CancellationToken.None);

                var receive = client.ReceiveAsync(1, CancellationToken.None);
                var dispose = Task.Run(() => client.Dispose());
                Assert.That(await Task.WhenAny(dispose, Task.Delay(2000)), Is.SameAs(dispose));
                Assert.CatchAsync<Exception>(async () => await receive);
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await client.SendAsync(new byte[] { 1 }, CancellationToken.None));
            }
        }

        [Test]
        public async Task ReceiveExactAsync_Should_Apply_Timeout_To_Whole_Message()
        {
            var port = GetAvailablePort();
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            var serverTask = Task.Run(async () =>
            {
                using (var accepted = await listener.AcceptTcpClientAsync())
                {
                    var stream = accepted.GetStream();
                    await stream.WriteAsync(new byte[] { 1 }, 0, 1);
                    await Task.Delay(150);
                    await stream.WriteAsync(new byte[] { 2 }, 0, 1);
                    await Task.Delay(150);
                    try
                    {
                        await stream.WriteAsync(new byte[] { 3 }, 0, 1);
                    }
                    catch
                    {
                    }
                }
            });

            try
            {
                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    ReceiveTimeoutMilliseconds = 200,
                    AutoReconnect = false
                }))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    Assert.ThrowsAsync<IndustrialTimeoutException>(async () =>
                        await client.ReceiveExactAsync(3, CancellationToken.None));
                }

                await serverTask;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
