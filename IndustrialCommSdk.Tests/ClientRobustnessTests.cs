using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Protocols.Socket;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class ClientRobustnessTests
    {
        [Test]
        public void ProtocolClients_RejectNullOptionsWithArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ModbusTcpClient(null));
            Assert.Throws<ArgumentNullException>(() => new SiemensS7Client(null));
            Assert.Throws<ArgumentNullException>(() => new MitsubishiMcClient(null));
            Assert.Throws<ArgumentNullException>(() => new SocketBridgeClient(null, null));
        }

        [Test]
        public async Task ReadAsync_UsesRequestTimeoutAndReturnsBadQuality()
        {
            using (var client = new WaitingClient())
            {
                var result = await client.ReadAsync(
                    new ReadRequest("test", "D0", DataType.UInt16, timeout: TimeSpan.FromMilliseconds(30)),
                    CancellationToken.None);

                Assert.That(result.Quality, Is.EqualTo(QualityStatus.Bad));
                Assert.That(result.ErrorMessage, Does.Contain("timed out"));
            }
        }

        [Test]
        public void WriteAsync_UsesRequestTimeoutAndThrowsTimeoutException()
        {
            using (var client = new WaitingClient())
            {
                Assert.ThrowsAsync<IndustrialTimeoutException>(async () =>
                    await client.WriteAsync(
                        new WriteRequest("test", "D0", DataType.UInt16, (ushort)1, timeout: TimeSpan.FromMilliseconds(30)),
                        CancellationToken.None));
            }
        }

        [Test]
        public void SubscribeAsync_RejectsNullRequestWithArgumentNullException()
        {
            using (var client = new WaitingClient())
            {
                Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    await client.SubscribeAsync(null, null, CancellationToken.None));
            }
        }

        [Test]
        public void ReadAsync_PreservesCallerCancellation()
        {
            using (var client = new WaitingClient())
            using (var cancellation = new CancellationTokenSource(30))
            {
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await client.ReadAsync(
                        new ReadRequest("test", "D0", DataType.UInt16),
                        cancellation.Token));
            }
        }

        [Test]
        public async Task TcpTransport_ReceiveTimeoutInterruptsPendingAsyncRead()
        {
            var port = GetAvailablePort();
            using (var server = new TcpTransportServer(IPAddress.Loopback, port))
            {
                await server.StartAsync(CancellationToken.None);
                using (var client = new TcpTransportClient(new TcpTransportOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    AutoReconnect = false,
                    ReceiveTimeoutMilliseconds = 100,
                }))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    Assert.ThrowsAsync<IndustrialTimeoutException>(async () =>
                        await client.ReceiveExactAsync(1, CancellationToken.None));
                }
                await server.StopAsync(CancellationToken.None);
            }
        }

        private static int GetAvailablePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        [Test]
        public async Task Dispose_Waits_For_InFlight_Client_Operation()
        {
            var client = new ControlledClient();
            var read = client.ReadAsync(
                new ReadRequest("test", "D0", DataType.UInt16),
                CancellationToken.None);
            await client.Started.Task;

            var dispose = Task.Run(() => client.Dispose());
            await Task.Delay(50);
            Assert.That(dispose.IsCompleted, Is.False);

            client.Release.TrySetResult(true);
            var value = await read;
            await dispose;

            Assert.That(value.Quality, Is.EqualTo(QualityStatus.Good));
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await client.ConnectAsync(CancellationToken.None));
        }

        private sealed class WaitingClient : IndustrialClientBase
        {
            public WaitingClient()
                : base("test", ProtocolKind.TcpSocket, new PollingScheduler(), NullIndustrialLogger.Instance)
            {
            }

            public override bool IsConnected { get { return true; } }

            protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }

            protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            protected override void DisposeCore()
            {
            }
        }

        private sealed class ControlledClient : IndustrialClientBase
        {
            public ControlledClient()
                : base("test", ProtocolKind.TcpSocket, new PollingScheduler(), NullIndustrialLogger.Instance)
            {
            }

            public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> Release { get; } = new TaskCompletionSource<bool>();
            public override bool IsConnected { get { return true; } }
            protected override Task ConnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            protected override Task DisconnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                await Release.Task.ConfigureAwait(false);
                return new DataValue(request.Address, request.DataType, (ushort)1, new byte[] { 0, 1 }, QualityStatus.Good, DateTimeOffset.UtcNow, null);
            }
            protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken) { return Task.CompletedTask; }
        }
    }
}
