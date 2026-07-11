using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class TimeoutAndDiagnosticsTests
    {
        [Test]
        public async Task DefaultTimeout_ReturnsBadValueAndUpdatesDiagnostics()
        {
            using (var client = new DelayedClient(40, 200))
            {
                var result = await client.ReadAsync(new ReadRequest(client.DeviceId, "A", DataType.Int16), CancellationToken.None);
                Assert.AreEqual(QualityStatus.Bad, result.Quality);
                var snapshot = client.GetDiagnosticSnapshot();
                Assert.AreEqual(1, snapshot.TimeoutCount);
                Assert.AreEqual(IndustrialFailureCategory.Timeout, snapshot.LastFailureCategory);
            }
        }

        [Test]
        public async Task RequestTimeout_OverridesClientDefault()
        {
            using (var client = new DelayedClient(500, 200))
            {
                var request = new ReadRequest(client.DeviceId, "A", DataType.Int16, timeout: TimeSpan.FromMilliseconds(20));
                var result = await client.ReadAsync(request, CancellationToken.None);
                Assert.AreEqual(QualityStatus.Bad, result.Quality);
                Assert.AreEqual(1, client.GetDiagnosticSnapshot().TimeoutCount);
            }
        }

        [Test]
        public void ExternalCancellation_RemainsCancellation()
        {
            using (var client = new DelayedClient(500, 200))
            using (var cts = new CancellationTokenSource(20))
            {
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await client.ReadAsync(new ReadRequest(client.DeviceId, "A", DataType.Int16), cts.Token));
                Assert.AreEqual(0, client.GetDiagnosticSnapshot().TimeoutCount);
            }
        }

        [Test]
        public void WriteTimeout_ThrowsIndustrialTimeout()
        {
            using (var client = new DelayedClient(30, 200))
            {
                Assert.ThrowsAsync<IndustrialTimeoutException>(async () =>
                    await client.WriteAsync(new WriteRequest(client.DeviceId, "A", DataType.Int16, (short)1), CancellationToken.None));
            }
        }

        [Test]
        public async Task DiagnosticSnapshot_CanBeReadConcurrently()
        {
            using (var client = new DelayedClient(500, 10))
            {
                await client.ReadAsync(new ReadRequest(client.DeviceId, "A", DataType.Int16), CancellationToken.None);
                System.Threading.Tasks.Parallel.For(0, 1000, _ =>
                {
                    var snapshot = client.GetDiagnosticSnapshot();
                    Assert.GreaterOrEqual(snapshot.TotalOperations, 1);
                });
            }
        }

        private sealed class DelayedClient : IndustrialClientBase
        {
            private readonly int _delay;
            public DelayedClient(int operationTimeoutMilliseconds, int delay)
                : base("test", ProtocolKind.TcpSocket, new PollingScheduler(), NullIndustrialLogger.Instance, operationTimeoutMilliseconds) { _delay = delay; }
            public override bool IsConnected { get { return true; } }
            protected override Task ConnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            protected override Task DisconnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                // 模拟不消费 CancellationToken 的第三方协议库，验证公共基类仍能按时返回。
                await Task.Delay(_delay);
                return new DataValue(request.Address, request.DataType, (short)1, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
            }
            protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken) { await Task.Delay(_delay); }
            protected override void DisposeCore() { }
        }
    }
}
