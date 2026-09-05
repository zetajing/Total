using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Protocols.S7;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class S7LifecycleRegressionTests
    {
        [Test]
        public async Task NativeStringRead_UsesConfiguredOperationTimeout()
        {
            using var fixture = await ConnectedFixture.CreateAsync(100);
            using var cancellation = new CancellationTokenSource();
            var read = fixture.Client.ReadDbStringAsync("DB1.DBX0.0", 20, cancellation.Token);
            try
            {
                Assert.ThrowsAsync<InduLink.Exceptions.IndustrialTimeoutException>(async () =>
                    await read.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.IsFalse(fixture.Client.IsConnected);
            }
            finally
            {
                cancellation.Cancel();
                try { await read; } catch { }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelledInFlightRead_DiscardsConnection(bool batch)
        {
            using var fixture = await ConnectedFixture.CreateAsync();
            using var cancellation = new CancellationTokenSource();
            var request = new ReadRequest(fixture.Client.DeviceId, "DB1.DBW0", DataType.Int16);
            Task read = batch
                ? fixture.Client.ReadManyAsync(new[] { request }, cancellation.Token)
                : fixture.Client.ReadAsync(request, cancellation.Token);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var buffer = new byte[1024];
            Assert.Greater(await fixture.Peer.GetStream().ReadAsync(buffer, deadline.Token), 0,
                "The request must reach the peer before cancellation.");
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await read.WaitAsync(deadline.Token));
            Assert.IsTrue(SpinWait.SpinUntil(() => !fixture.Client.IsConnected, 2000),
                "A cancelled receive must not leave a stream available for a late response.");
        }

        [Test]
        public async Task CancelledBeforeRead_PreservesConnection()
        {
            using var fixture = await ConnectedFixture.CreateAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await fixture.Client.ReadAsync(
                new ReadRequest(fixture.Client.DeviceId, "DB1.DBW0", DataType.Int16), cancellation.Token));
            Assert.IsTrue(fixture.Client.IsConnected);
            Assert.AreEqual(0, fixture.Peer.Available);
        }

        // Inject an established S7.Net transport to exercise real request/receive cancellation
        // without a PLC or port 102. This fixture deliberately does not test S7 negotiation.
        private sealed class ConnectedFixture : IDisposable
        {
            public SiemensS7Client Client { get; private set; }
            public TcpClient Peer { get; private set; }

            public static async Task<ConnectedFixture> CreateAsync(int timeoutMilliseconds = 10000)
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var transport = new TcpClient();
                await transport.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                var peer = await listener.AcceptTcpClientAsync();
                var plc = new S7.Net.Plc(S7.Net.CpuType.S71200, "127.0.0.1", 0, 1);
                typeof(S7.Net.Plc).GetField("tcpClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(plc, transport);
                typeof(S7.Net.Plc).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(plc, transport.GetStream());
                var client = new SiemensS7Client(new SiemensS7ClientOptions
                {
                    DeviceId = "s7-cancel-test", Host = "127.0.0.1", OperationTimeoutMilliseconds = timeoutMilliseconds
                });
                typeof(SiemensS7Client).GetField("_plc", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(client, plc);
                return new ConnectedFixture { Client = client, Peer = peer };
            }

            public void Dispose() { Peer.Dispose(); Client.Dispose(); }
        }
    }
}
