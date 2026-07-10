using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Polling;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 <see cref="PollingScheduler" /> 轮询调度器的单元测试。
    /// </summary>
    [TestFixture]
    public class PollingSchedulerTests
    {
        [Test]
        public async Task PollingScheduler_Should_Raise_Subscription_Event()
        {
            using (var scheduler = new PollingScheduler())
            {
                var client = new FakeIndustrialClient("dev-1");
                var request = new SubscriptionRequest("sub-1", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "addr-1", DataType.UInt16)
                }, TimeSpan.FromMilliseconds(50), false);

                var tcs = new TaskCompletionSource<SubscriptionEvent>();
                await scheduler.SubscribeAsync(client, request, (sender, args) => tcs.TrySetResult(args), CancellationToken.None);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
                await scheduler.UnsubscribeAsync("sub-1", CancellationToken.None);

                Assert.That(completed, Is.EqualTo(tcs.Task));
                Assert.That(tcs.Task.Result.Values.Count, Is.EqualTo(1));
                Assert.That(tcs.Task.Result.Values[0].Value, Is.EqualTo(42));
            }
        }

        [Test]
        public async Task PollingScheduler_Should_Detect_ByteArray_Content_Changes()
        {
            using (var scheduler = new PollingScheduler())
            {
                var readCount = 0;
                var client = new FakeIndustrialClient("dev-1", () =>
                {
                    readCount++;
                    return readCount < 3 ? new byte[] { 0x01 } : new byte[] { 0x02 };
                });
                var request = new SubscriptionRequest("bytes", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "addr-1", DataType.ByteArray)
                }, TimeSpan.FromMilliseconds(20), true);

                var reports = 0;
                var changed = new TaskCompletionSource<byte[]>();
                await scheduler.SubscribeAsync(client, request, (sender, args) =>
                {
                    reports++;
                    var bytes = (byte[])args.Values[0].Value;
                    if (bytes[0] == 0x02) changed.TrySetResult(bytes);
                }, CancellationToken.None);

                var completed = await Task.WhenAny(changed.Task, Task.Delay(1000));
                await scheduler.UnsubscribeAsync("bytes", CancellationToken.None);

                Assert.That(completed, Is.EqualTo(changed.Task));
                Assert.That(reports, Is.EqualTo(2));
            }
        }

        [Test]
        public void PollingScheduler_Should_Reject_Subscription_When_DeviceId_Does_Not_Match_Client()
        {
            using (var scheduler = new PollingScheduler())
            {
                var client = new FakeIndustrialClient("actual-device");
                var request = new SubscriptionRequest("bad-device", "requested-device", new[]
                {
                    new ReadRequest("requested-device", "D100", DataType.Int16)
                }, TimeSpan.FromMilliseconds(100), false);

                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await scheduler.SubscribeAsync(client, request, null, CancellationToken.None));
            }
        }

        [Test]
        public void PollingScheduler_Should_Reject_Subscription_When_Protocol_Does_Not_Support_Subscriptions()
        {
            using (var scheduler = new PollingScheduler())
            {
                var client = new RawSocketClient("socket-1");
                var request = new SubscriptionRequest("raw", "socket-1", new[]
                {
                    new ReadRequest("socket-1", "payload", DataType.ByteArray)
                }, TimeSpan.FromSeconds(1), false);

                Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await scheduler.SubscribeAsync(client, request, null, CancellationToken.None));
            }
        }

        [Test]
        public void PollingScheduler_Should_Reject_Subscription_Below_Recommended_Interval()
        {
            using (var scheduler = new PollingScheduler())
            {
                var client = new FakeIndustrialClient("dev-1", recommendedMinPollingInterval: TimeSpan.FromMilliseconds(100));
                var request = new SubscriptionRequest("too-fast", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "D100", DataType.Int16)
                }, TimeSpan.FromMilliseconds(10), false);

                Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                    await scheduler.SubscribeAsync(client, request, null, CancellationToken.None));
            }
        }

        [Test]
        public async Task PollingScheduler_Should_Reject_Different_Client_Instance_For_Same_Device()
        {
            using (var scheduler = new PollingScheduler())
            {
                var firstClient = new FakeIndustrialClient("dev-1");
                var secondClient = new FakeIndustrialClient("dev-1");
                var firstRequest = new SubscriptionRequest("sub-a", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "D100", DataType.Int16)
                }, TimeSpan.FromSeconds(5), false);
                var secondRequest = new SubscriptionRequest("sub-b", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "D101", DataType.Int16)
                }, TimeSpan.FromSeconds(5), false);

                await scheduler.SubscribeAsync(firstClient, firstRequest, null, CancellationToken.None);

                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await scheduler.SubscribeAsync(secondClient, secondRequest, null, CancellationToken.None));
            }
        }

        [Test]
        public async Task PollingScheduler_Should_Merge_Duplicate_Requests_For_Due_Subscriptions()
        {
            using (var scheduler = new PollingScheduler())
            {
                var client = new FakeIndustrialClient("dev-1");
                var firstRequest = new SubscriptionRequest("sub-a", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "D100", DataType.Int16)
                }, TimeSpan.FromMilliseconds(100), false);
                var secondRequest = new SubscriptionRequest("sub-b", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "D100", DataType.Int16)
                }, TimeSpan.FromMilliseconds(100), false);

                var first = new TaskCompletionSource<SubscriptionEvent>();
                var second = new TaskCompletionSource<SubscriptionEvent>();
                await scheduler.SubscribeAsync(client, firstRequest, (sender, args) => first.TrySetResult(args), CancellationToken.None);
                await scheduler.SubscribeAsync(client, secondRequest, (sender, args) => second.TrySetResult(args), CancellationToken.None);

                var both = Task.WhenAll(first.Task, second.Task);
                var completed = await Task.WhenAny(both, Task.Delay(1000));
                await scheduler.UnsubscribeAsync("sub-a", CancellationToken.None);
                await scheduler.UnsubscribeAsync("sub-b", CancellationToken.None);

                Assert.That(completed, Is.EqualTo(both));
                Assert.That(client.SmallestBatchSizeSeen, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task PollingScheduler_Should_Split_Reads_By_MaxReadItems_When_No_Planner()
        {
            using (var scheduler = new PollingScheduler())
            {
                var client = new FakeIndustrialClient("dev-1", maxReadItems: 1);
                var request = new SubscriptionRequest("split", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "D100", DataType.Int16),
                    new ReadRequest("dev-1", "D101", DataType.Int16),
                    new ReadRequest("dev-1", "D102", DataType.Int16),
                }, TimeSpan.FromMilliseconds(100), false);

                var tcs = new TaskCompletionSource<SubscriptionEvent>();
                await scheduler.SubscribeAsync(client, request, (sender, args) => tcs.TrySetResult(args), CancellationToken.None);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
                await scheduler.UnsubscribeAsync("split", CancellationToken.None);

                Assert.That(completed, Is.EqualTo(tcs.Task));
                Assert.That(tcs.Task.Result.Values.Count, Is.EqualTo(3));
                Assert.That(client.ReadManyCallCount, Is.GreaterThanOrEqualTo(3));
                Assert.That(client.LargestBatchSizeSeen, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task PollingScheduler_Should_Use_Batch_Planner_When_Available()
        {
            using (var scheduler = new PollingScheduler())
            {
                var client = new PlanningIndustrialClient("dev-1");
                var request = new SubscriptionRequest("planned", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "D100", DataType.Int16),
                    new ReadRequest("dev-1", "D101", DataType.Int16),
                    new ReadRequest("dev-1", "D102", DataType.Int16),
                }, TimeSpan.FromMilliseconds(100), false);

                var tcs = new TaskCompletionSource<SubscriptionEvent>();
                await scheduler.SubscribeAsync(client, request, (sender, args) => tcs.TrySetResult(args), CancellationToken.None);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
                await scheduler.UnsubscribeAsync("planned", CancellationToken.None);

                Assert.That(completed, Is.EqualTo(tcs.Task));
                Assert.That(tcs.Task.Result.Values.Count, Is.EqualTo(3));
                Assert.That(client.PlanReadCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(client.LargestBatchSizeSeen, Is.EqualTo(1));
            }
        }

        private class FakeIndustrialClient : IIndustrialClient, IProtocolCapabilityProvider
        {
            private readonly Func<object> _valueFactory;
            private readonly TimeSpan _recommendedMinPollingInterval;
            private readonly int _maxReadItems;
            private int _smallestBatchSizeSeen = int.MaxValue;
            private int _largestBatchSizeSeen;
            private int _readManyCallCount;

            public FakeIndustrialClient(
                string deviceId,
                Func<object> valueFactory = null,
                TimeSpan? recommendedMinPollingInterval = null,
                int maxReadItems = 256)
            {
                DeviceId = deviceId;
                _valueFactory = valueFactory ?? (() => 42);
                _recommendedMinPollingInterval = recommendedMinPollingInterval ?? TimeSpan.FromMilliseconds(1);
                _maxReadItems = maxReadItems;
            }

            public string DeviceId { get; private set; }
            public ProtocolKind Kind { get { return ProtocolKind.ModbusTcp; } }
            public bool IsConnected { get { return true; } }
            public int SmallestBatchSizeSeen { get { return _smallestBatchSizeSeen == int.MaxValue ? 0 : _smallestBatchSizeSeen; } }
            public int LargestBatchSizeSeen { get { return _largestBatchSizeSeen; } }
            public int ReadManyCallCount { get { return _readManyCallCount; } }
            public ProtocolCapabilities Capabilities
            {
                get
                {
                    return new ProtocolCapabilities(
                        ProtocolKind.ModbusTcp,
                        "Fake Polling Protocol",
                        supportsSubscriptions: true,
                        supportsBitAddress: true,
                        supportsByteArray: true,
                        maxReadItems: _maxReadItems,
                        maxWriteItems: 256,
                        recommendedMinPollingInterval: _recommendedMinPollingInterval);
                }
            }

            public Task ConnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task DisconnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public void Dispose() { }
            public HealthSnapshot GetHealth() { return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null); }

            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new DataValue(request.Address, request.DataType, _valueFactory(), new byte[] { 0x00, 0x2A }, QualityStatus.Good, DateTimeOffset.UtcNow, null));
            }

            public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                _readManyCallCount++;
                if (requests.Count < _smallestBatchSizeSeen)
                    _smallestBatchSizeSeen = requests.Count;
                if (requests.Count > _largestBatchSizeSeen)
                    _largestBatchSizeSeen = requests.Count;

                var values = new List<DataValue>();
                foreach (var request in requests)
                    values.Add(await ReadAsync(request, cancellationToken));
                return new BatchReadResult(values);
            }

            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken) { return Task.CompletedTask; }
        }

        private sealed class PlanningIndustrialClient : FakeIndustrialClient, IBatchOperationPlanner
        {
            public PlanningIndustrialClient(string deviceId) : base(deviceId, maxReadItems: 256)
            {
            }

            public int PlanReadCount { get; private set; }

            public BatchSplitPlan PlanRead(IReadOnlyCollection<ReadRequest> requests, BatchReadOptions options, ProtocolCapabilities capabilities)
            {
                PlanReadCount++;
                var list = new List<ReadRequest>(requests);
                var groups = new List<BatchRequestGroup>();
                for (var i = 0; i < list.Count; i++)
                {
                    groups.Add(BatchRequestGroup.ForRead(i, "planned", i, i, list[i].DataType, new[] { list[i] }));
                }
                return new BatchSplitPlan(ProtocolKind.ModbusTcp, BatchOperationKind.Read, groups, list.Count);
            }

            public BatchSplitPlan PlanWrite(IReadOnlyCollection<WriteRequest> requests, BatchWriteOptions options, ProtocolCapabilities capabilities)
            {
                return BatchSplitPlan.SingleWriteGroup(ProtocolKind.ModbusTcp, requests);
            }
        }

        private sealed class RawSocketClient : IIndustrialClient
        {
            public RawSocketClient(string deviceId)
            {
                DeviceId = deviceId;
            }

            public string DeviceId { get; private set; }
            public ProtocolKind Kind { get { return ProtocolKind.TcpSocket; } }
            public bool IsConnected { get { return true; } }

            public Task ConnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task DisconnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public void Dispose() { }
            public HealthSnapshot GetHealth() { return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null); }
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new DataValue(request.Address, request.DataType, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, null));
            }
            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                return Task.FromResult(new BatchReadResult(new DataValue[0]));
            }
            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken) { throw new NotSupportedException(); }
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken) { throw new NotSupportedException(); }
        }
    }
}
