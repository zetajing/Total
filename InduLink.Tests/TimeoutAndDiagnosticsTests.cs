using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Diagnostics;
using InduLink.Exceptions;
using InduLink.Runtime;
using InduLink.Runtime.Polling;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class TimeoutAndDiagnosticsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void WriteCancelledBeforeDispatch_RemainsCancellation(bool batch)
        {
            using var client = new DelayedClient(500, 10);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var request = new WriteRequest(client.DeviceId, "A", DataType.Int16, (short)1);
            Assert.CatchAsync<OperationCanceledException>(async () =>
            {
                if (batch) await client.WriteManyAsync(new[] { request }, cancellation.Token);
                else await client.WriteAsync(request, cancellation.Token);
            });
            Assert.AreEqual(0, client.GetDiagnosticSnapshot().TotalOperations);
        }

        [Test]
        public async Task DefaultTimeout_ReturnsBadValueAndUpdatesDiagnostics()
        {
            using (var client = new DelayedClient(40, 200))
            {
                var result = await client.ReadAsync(new ReadRequest(client.DeviceId, "A", DataType.Int16), CancellationToken.None);
                Assert.AreEqual(QualityStatus.Bad, result.Quality);
                var snapshot = client.GetDiagnosticSnapshot();
                Assert.AreEqual(1, snapshot.TimeoutCount);
                Assert.AreEqual(InduLinkFailureCategory.Timeout, snapshot.LastFailureCategory);
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
        public void WriteTimeout_ThrowsUncertainWriteAndCountsTimeout()
        {
            using (var client = new DelayedClient(30, 200))
            {
                Assert.ThrowsAsync<InduLinkWriteUncertainException>(async () =>
                    await client.WriteAsync(new WriteRequest(client.DeviceId, "A", DataType.Int16, (short)1), CancellationToken.None));
                Assert.AreEqual(1, client.GetDiagnosticSnapshot().TimeoutCount);
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

        [Test]
        public async Task BatchRead_UsesLongestExplicitTimeoutAsOverallBudget()
        {
            using (var client = new ControlledBatchClient())
            {
                var requests = new[]
                {
                    new ReadRequest(client.DeviceId, "A", DataType.Int16, timeout: TimeSpan.FromMilliseconds(20)),
                    new ReadRequest(client.DeviceId, "B", DataType.Int16, timeout: TimeSpan.FromMilliseconds(500)),
                };

                var operation = client.ReadManyAsync(requests, CancellationToken.None);
                await client.CoreEntered.Task.ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);

                client.Release.TrySetResult(true);
                var result = await operation.ConfigureAwait(false);
                Assert.That(result.Values, Has.All.Property(nameof(DataValue.Quality)).EqualTo(QualityStatus.Good));
            }
        }

        [Test]
        public async Task BatchWrite_UsesLongestExplicitTimeoutAsOverallBudget()
        {
            using (var client = new ControlledBatchClient())
            {
                var requests = new[]
                {
                    new WriteRequest(client.DeviceId, "A", DataType.Int16, (short)1, timeout: TimeSpan.FromMilliseconds(20)),
                    new WriteRequest(client.DeviceId, "B", DataType.Int16, (short)2, timeout: TimeSpan.FromMilliseconds(500)),
                };

                var operation = client.WriteManyAsync(requests, CancellationToken.None);
                await client.CoreEntered.Task.ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);

                client.Release.TrySetResult(true);
                await operation.ConfigureAwait(false);
            }
        }

        private sealed class DelayedClient : InduLinkClientBase
        {
            private readonly int _delay;
            public DelayedClient(int operationTimeoutMilliseconds, int delay)
                : base("test", ProtocolKind.TcpSocket, new PollingScheduler(), NullInduLinkLogger.Instance, operationTimeoutMilliseconds) { _delay = delay; }
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

        private sealed class ControlledBatchClient : InduLinkClientBase
        {
            public ControlledBatchClient()
                : base("batch-test", ProtocolKind.TcpSocket, new PollingScheduler(), NullInduLinkLogger.Instance, 25) { }

            public TaskCompletionSource<bool> CoreEntered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool IsConnected { get { return true; } }

            protected override Task ConnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            protected override Task DisconnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }

            protected override Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new DataValue(
                    request.Address,
                    request.DataType,
                    (short)1,
                    null,
                    QualityStatus.Good,
                    DateTimeOffset.UtcNow,
                    null));
            }

            protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            protected override async Task<BatchReadResult> ReadManyCoreAsync(
                IReadOnlyCollection<ReadRequest> requests,
                CancellationToken cancellationToken)
            {
                CoreEntered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                var values = new List<DataValue>();
                foreach (var request in requests)
                {
                    values.Add(await ReadCoreAsync(request, cancellationToken).ConfigureAwait(false));
                }

                return new BatchReadResult(values);
            }

            protected override async Task WriteManyCoreAsync(
                IReadOnlyCollection<WriteRequest> requests,
                CancellationToken cancellationToken)
            {
                CoreEntered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            protected override void DisposeCore() { }
        }
    }
}
