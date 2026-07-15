using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime.Configuration;
using IndustrialCommSdk.Runtime.Polling;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Storage;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class RuntimeAndStorageTests
    {
        [Test]
        public void PollingScheduler_RejectsMismatchedDeviceAndConflictingClient()
        {
            using (var scheduler = new PollingScheduler())
            using (var first = new FakeClient("device"))
            using (var second = new FakeClient("device"))
            {
                var mismatch = Request("sub-0", "other");
                Assert.ThrowsAsync<ArgumentException>(() => scheduler.SubscribeAsync(first, mismatch, null, CancellationToken.None));

                scheduler.SubscribeAsync(first, Request("sub-1", "device"), null, CancellationToken.None).GetAwaiter().GetResult();
                Assert.ThrowsAsync<InvalidOperationException>(() =>
                    scheduler.SubscribeAsync(second, Request("sub-2", "device"), null, CancellationToken.None));
            }
        }

        [Test]
        public async Task PollingScheduler_IsolatesCallbackFailuresAndMergesDuplicateReads()
        {
            using (var scheduler = new PollingScheduler())
            using (var client = new FakeClient("device"))
            {
                var received = new TaskCompletionSource<bool>();
                await scheduler.SubscribeAsync(client, Request("throws", "device"), (sender, args) =>
                {
                    throw new InvalidOperationException("callback failure");
                }, CancellationToken.None);
                await scheduler.SubscribeAsync(client, Request("works", "device"), (sender, args) =>
                {
                    received.TrySetResult(true);
                }, CancellationToken.None);

                var completed = await Task.WhenAny(received.Task, Task.Delay(2000));
                Assert.AreSame(received.Task, completed);
                Assert.That(client.LastBatchSize, Is.EqualTo(1), "相同点位应合并成一次批量读取。");
            }
        }

        [Test]
        public void PollingScheduler_HonorsCancellationAndDispose()
        {
            var scheduler = new PollingScheduler();
            using (var client = new FakeClient("device"))
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(() =>
                    scheduler.SubscribeAsync(client, Request("cancelled", "device"), null, source.Token));
                scheduler.Dispose();
                Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    scheduler.SubscribeAsync(client, Request("disposed", "device"), null, CancellationToken.None));
            }
        }

        [Test]
        public void DeviceHost_SkipsDisabledDevicesAndRejectsInvalidPointFiles()
        {
            var disabled = new IndustrialSdkConfig
            {
                Devices = new List<IndustrialDeviceConfig>
                {
                    Device("disabled", false, "missing.json"),
                },
            };
            using (var host = new IndustrialDeviceHost(disabled, Path.GetTempPath(), item => new FakeClient(item.EffectiveDeviceId)))
                Assert.That(host.Devices, Is.Empty);

            var enabled = new IndustrialSdkConfig { Devices = new List<IndustrialDeviceConfig> { Device("enabled", true, "missing.json") } };
            Assert.Throws<FileNotFoundException>(() =>
                new IndustrialDeviceHost(enabled, Path.GetTempPath(), item => new FakeClient(item.EffectiveDeviceId)));
        }

        [Test]
        public async Task DeviceHost_ReconnectsAfterSubscriptionFailureAndStopsCleanly()
        {
            var directory = Path.Combine(Path.GetTempPath(), "IndustrialCommSdk.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "points.json"),
                    "{\"tags\":[{\"name\":\"Value\",\"address\":\"D0\",\"type\":\"Int16\"}]}");
                var config = new IndustrialSdkConfig
                {
                    Devices = new List<IndustrialDeviceConfig>
                    {
                        Device("device", true, "points.json"),
                    },
                };
                config.Devices[0].Runtime.ReconnectDelayMilliseconds = 20;
                var client = new FakeClient("device") { SubscribeFailuresRemaining = 1 };
                using (var host = new IndustrialDeviceHost(config, directory, item => client))
                {
                    await host.StartAsync(CancellationToken.None);
                    var timeout = DateTime.UtcNow.AddSeconds(2);
                    while (client.SubscribeCalls < 2 && DateTime.UtcNow < timeout) await Task.Delay(20);
                    Assert.That(client.SubscribeCalls, Is.GreaterThanOrEqualTo(2));
                    Assert.That(client.ConnectCalls, Is.GreaterThanOrEqualTo(2));
                    await host.StopAsync(CancellationToken.None);
                    Assert.IsFalse(client.IsConnected);
                    Assert.AreEqual(1, client.UnsubscribeCalls);
                }

                Assert.Throws<InvalidOperationException>(() =>
                    new IndustrialDeviceHost(config, directory, item => throw new InvalidOperationException("provider failed")));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public async Task BufferedRecorder_RetriesAndFlushesAcceptedRecordsOnStop()
        {
            var store = new FakeStore { FailuresRemaining = 1 };
            using (var recorder = new BufferedIndustrialDataRecorder(store,
                new BufferedDataRecorderOptions { BatchSize = 10, QueueCapacity = 2, RetryCount = 1 }))
            {
                await recorder.StartAsync(CancellationToken.None);
                Assert.IsTrue(recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { GoodValue("D0", 7) }));
                await recorder.StopAsync(CancellationToken.None);
                var snapshot = recorder.GetSnapshot();
                Assert.AreEqual(2, store.WriteCalls);
                Assert.AreEqual(1, snapshot.WrittenRecordCount);
                Assert.AreEqual(1, snapshot.WriteFailureCount);
                Assert.AreEqual(0, snapshot.DroppedRecordCount);
            }
        }

        [Test]
        public async Task BufferedRecorder_UsesBoundedQueueAndCallerCancellationOnlyCancelsWaiting()
        {
            var store = new FakeStore { BlockWrites = true };
            using (var recorder = new BufferedIndustrialDataRecorder(store,
                new BufferedDataRecorderOptions { BatchSize = 1, QueueCapacity = 1, RetryCount = 0 }))
            {
                await recorder.StartAsync(CancellationToken.None);
                Assert.IsTrue(recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { GoodValue("D0", 1) }));
                await Task.WhenAny(store.WriteStarted.Task, Task.Delay(2000));
                Assert.IsTrue(store.WriteStarted.Task.IsCompleted);
                Assert.IsTrue(recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { GoodValue("D1", 2) }));
                Assert.IsFalse(recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { GoodValue("D2", 3) }));

                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    Assert.ThrowsAsync<OperationCanceledException>(() => recorder.StopAsync(cancelled.Token));
                }

                store.ReleaseWrites.TrySetResult(true);
                await recorder.StopAsync(CancellationToken.None);
                var snapshot = recorder.GetSnapshot();
                Assert.AreEqual(2, snapshot.AcceptedRecordCount);
                Assert.AreEqual(2, snapshot.WrittenRecordCount);
                Assert.AreEqual(1, snapshot.DroppedRecordCount);
            }
        }

        private static SubscriptionRequest Request(string key, string deviceId)
        {
            return new SubscriptionRequest(key, deviceId,
                new[] { new ReadRequest(deviceId, "D0", DataType.Int16, 1) },
                TimeSpan.FromMilliseconds(100), false);
        }

        private static IndustrialDeviceConfig Device(string name, bool enabled, string pointsFile)
        {
            return new IndustrialDeviceConfig
            {
                Name = name,
                DeviceId = name,
                Protocol = "modbus-tcp",
                Enabled = enabled,
                PointsFile = pointsFile,
                Runtime = new IndustrialDeviceRuntimeOptions(),
            };
        }

        private static DataValue GoodValue(string address, object value)
        {
            return new DataValue(address, DataType.Int16, value, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        private sealed class FakeClient : IIndustrialClient
        {
            public FakeClient(string deviceId) { DeviceId = deviceId; }
            public string DeviceId { get; private set; }
            public ProtocolKind Kind { get { return ProtocolKind.ModbusTcp; } }
            public bool IsConnected { get; private set; }
            public int LastBatchSize { get; private set; }
            public int ConnectCalls { get; private set; }
            public int SubscribeCalls { get; private set; }
            public int UnsubscribeCalls { get; private set; }
            public int SubscribeFailuresRemaining { get; set; }
            public Task ConnectAsync(CancellationToken token) { ConnectCalls++; IsConnected = true; return Task.CompletedTask; }
            public Task DisconnectAsync(CancellationToken token) { IsConnected = false; return Task.CompletedTask; }
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken token) { return Task.FromResult(GoodValue(request.Address, 1)); }
            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken token)
            {
                LastBatchSize = requests.Count;
                return Task.FromResult(new BatchReadResult(requests.Select(item => GoodValue(item.Address, 1)).ToArray()));
            }
            public Task WriteAsync(WriteRequest request, CancellationToken token) { return Task.CompletedTask; }
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken token) { return Task.CompletedTask; }
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken token)
            {
                SubscribeCalls++;
                if (SubscribeFailuresRemaining-- > 0) throw new IOException("temporary subscription failure");
                return Task.FromResult(request.SubscriptionKey);
            }
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken token) { UnsubscribeCalls++; return Task.CompletedTask; }
            public HealthSnapshot GetHealth() { return new HealthSnapshot(IsConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected, null, 0, null); }
            public void Dispose() { }
        }

        private sealed class FakeStore : IIndustrialDataStore
        {
            public int FailuresRemaining { get; set; }
            public int WriteCalls { get; private set; }
            public bool BlockWrites { get; set; }
            public TaskCompletionSource<bool> WriteStarted { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> ReleaseWrites { get; } = new TaskCompletionSource<bool>();
            public Task InitializeAsync(CancellationToken token) { return Task.CompletedTask; }
            public async Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken token)
            {
                WriteCalls++;
                if (FailuresRemaining-- > 0) throw new IOException("temporary failure");
                WriteStarted.TrySetResult(true);
                if (BlockWrites) await ReleaseWrites.Task;
            }
            public Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(HistoryQueryFilter filter, CancellationToken token)
            {
                return Task.FromResult((IReadOnlyList<IndustrialDataRecord>)new IndustrialDataRecord[0]);
            }
            public Task<int> DeleteAsync(HistoryQueryFilter filter, CancellationToken token) { return Task.FromResult(0); }
            public void Dispose() { }
        }
    }
}
