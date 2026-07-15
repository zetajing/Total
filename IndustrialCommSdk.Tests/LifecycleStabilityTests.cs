using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Configuration;
using IndustrialCommSdk.Storage;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class LifecycleStabilityTests
    {
        [Test]
        public async Task DeviceHost_CancelledStartRollsBackAndCanStartAgain()
        {
            var directory = CreatePointDirectory();
            try
            {
                var first = new LifecycleClient("first");
                var second = new LifecycleClient("second") { BlockConnect = true };
                var config = Config(Device("first"), Device("second"));

                using (var host = new IndustrialDeviceHost(
                    config,
                    directory,
                    device => device.Name == "first" ? first : second))
                using (var cancellation = new CancellationTokenSource())
                {
                    var start = host.StartAsync(cancellation.Token);
                    Assert.AreSame(second.ConnectEntered.Task, await Task.WhenAny(second.ConnectEntered.Task, Task.Delay(2000)));
                    cancellation.Cancel();
                    try
                    {
                        await start;
                        Assert.Fail("取消启动后应抛出取消异常。");
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    Assert.IsFalse(first.IsConnected, "已经启动的设备必须在启动回滚时断开。");
                    Assert.IsFalse(host.Get("first").IsStarted);
                    Assert.IsFalse(host.Get("second").IsStarted);

                    second.BlockConnect = false;
                    await host.StartAsync(CancellationToken.None);
                    Assert.IsTrue(first.IsConnected);
                    Assert.IsTrue(second.IsConnected);
                    await host.StopAsync(CancellationToken.None);
                    Assert.IsFalse(first.IsConnected);
                    Assert.IsFalse(second.IsConnected);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public async Task DeviceHost_FailedStopCanBeRetriedWithoutLosingSubscriptionId()
        {
            var directory = CreatePointDirectory();
            try
            {
                var client = new LifecycleClient("device") { UnsubscribeFailuresRemaining = 1 };
                using (var host = new IndustrialDeviceHost(Config(Device("device")), directory, _ => client))
                {
                    await host.StartAsync(CancellationToken.None);
                    Assert.ThrowsAsync<AggregateException>(() => host.StopAsync(CancellationToken.None));
                    Assert.IsTrue(host.Get("device").IsStarted);

                    await host.StopAsync(CancellationToken.None);
                    Assert.AreEqual(2, client.UnsubscribeCalls);
                    Assert.IsFalse(client.IsConnected);
                    Assert.IsFalse(host.Get("device").IsStarted);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void DeviceHost_ConstructorDisposesClientsCreatedBeforeLaterFailure()
        {
            var directory = CreatePointDirectory();
            try
            {
                var first = new LifecycleClient("first");
                Assert.Throws<InvalidOperationException>(() => new IndustrialDeviceHost(
                    Config(Device("first"), Device("second")),
                    directory,
                    device => device.Name == "first"
                        ? first
                        : throw new InvalidOperationException("provider failed")));
                Assert.IsTrue(first.Disposed);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public async Task BufferedRecorder_SerializesStartAndStopAndRejectsRestart()
        {
            var store = new LifecycleStore { BlockInitialize = true };
            using (var recorder = new BufferedIndustrialDataRecorder(store))
            {
                var start = recorder.StartAsync(CancellationToken.None);
                Assert.AreSame(store.InitializeEntered.Task, await Task.WhenAny(store.InitializeEntered.Task, Task.Delay(2000)));
                Assert.IsFalse(recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { GoodValue() }));

                var stop = recorder.StopAsync(CancellationToken.None);
                store.ReleaseInitialize.TrySetResult(true);
                await start;
                await stop;

                Assert.IsFalse(recorder.GetSnapshot().IsRunning);
                Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StartAsync(CancellationToken.None));
            }
        }

        [Test]
        public async Task BufferedRecorder_ConcurrentProducersDoNotThrowWhileStopping()
        {
            var failures = new List<Exception>();
            var failureSync = new object();
            using (var recorder = new BufferedIndustrialDataRecorder(
                new LifecycleStore(),
                new BufferedDataRecorderOptions { BatchSize = 10, QueueCapacity = 32, RetryCount = 0 }))
            {
                await recorder.StartAsync(CancellationToken.None);
                var producers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
                {
                    try
                    {
                        for (var i = 0; i < 2000; i++)
                            recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { GoodValue() });
                    }
                    catch (Exception ex)
                    {
                        lock (failureSync) failures.Add(ex);
                    }
                })).ToArray();

                await Task.Delay(10);
                await recorder.StopAsync(CancellationToken.None);
                await Task.WhenAll(producers);

                Assert.That(failures, Is.Empty);
                Assert.IsFalse(recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { GoodValue() }));
            }
        }

        [Test]
        public async Task BufferedRecorder_DisposeDuringInitializeDoesNotStartWorkerAfterDispose()
        {
            var store = new LifecycleStore { BlockInitialize = true };
            var recorder = new BufferedIndustrialDataRecorder(store);
            var start = recorder.StartAsync(CancellationToken.None);
            Assert.AreSame(store.InitializeEntered.Task, await Task.WhenAny(store.InitializeEntered.Task, Task.Delay(2000)));

            var dispose = Task.Run(() => recorder.Dispose());
            Assert.IsTrue(
                SpinWait.SpinUntil(() => IsRecorderDisposed(recorder), TimeSpan.FromSeconds(2)),
                "Dispose 任务未及时进入已释放状态。");
            store.ReleaseInitialize.TrySetResult(true);

            Assert.ThrowsAsync<ObjectDisposedException>(async () => await start);
            await dispose;
            Assert.IsTrue(store.Disposed);
        }

        [Test]
        public async Task BufferedRecorder_CancelledDuringInitializeDoesNotStartWorkerAndCanRetry()
        {
            var store = new LifecycleStore { BlockInitialize = true };
            using (var recorder = new BufferedIndustrialDataRecorder(store))
            using (var cancellation = new CancellationTokenSource())
            {
                var start = recorder.StartAsync(cancellation.Token);
                Assert.AreSame(store.InitializeEntered.Task, await Task.WhenAny(
                    store.InitializeEntered.Task,
                    Task.Delay(2000)));

                cancellation.Cancel();
                store.ReleaseInitialize.TrySetResult(true);
                try
                {
                    await start;
                    Assert.Fail("初始化期间取消后不应启动后台记录器。");
                }
                catch (OperationCanceledException)
                {
                }

                Assert.IsFalse(recorder.GetSnapshot().IsRunning);
                await recorder.StartAsync(CancellationToken.None);
                Assert.IsTrue(recorder.GetSnapshot().IsRunning);
                await recorder.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        public void SubscriptionRequest_RejectsNullItemsBeforeTheyReachPollingWorker()
        {
            Assert.Throws<ArgumentException>(() => new SubscriptionRequest(
                "invalid-null-item",
                "device",
                new ReadRequest[] { null },
                TimeSpan.FromMilliseconds(100),
                false));
        }

        private static bool IsRecorderDisposed(BufferedIndustrialDataRecorder recorder)
        {
            var field = typeof(BufferedIndustrialDataRecorder).GetField(
                "_disposed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && (int)field.GetValue(recorder) != 0;
        }

        private static string CreatePointDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "IndustrialCommSdk.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "points.json"),
                "{\"tags\":[{\"name\":\"Value\",\"address\":\"D0\",\"type\":\"Int16\"}]}");
            return directory;
        }

        private static IndustrialSdkConfig Config(params IndustrialDeviceConfig[] devices)
        {
            return new IndustrialSdkConfig { Devices = devices.ToList() };
        }

        private static IndustrialDeviceConfig Device(string name)
        {
            return new IndustrialDeviceConfig
            {
                Name = name,
                DeviceId = name,
                Protocol = "modbus-tcp",
                Enabled = true,
                PointsFile = "points.json",
                Runtime = new IndustrialDeviceRuntimeOptions
                {
                    PollingIntervalMilliseconds = 100,
                    ReconnectDelayMilliseconds = 100,
                    OperationTimeoutMilliseconds = 1000,
                },
            };
        }

        private static DataValue GoodValue()
        {
            return new DataValue("D0", DataType.Int16, 1, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        private sealed class LifecycleClient : IIndustrialClient
        {
            public LifecycleClient(string deviceId) { DeviceId = deviceId; }

            public string DeviceId { get; private set; }
            public ProtocolKind Kind { get { return ProtocolKind.ModbusTcp; } }
            public bool IsConnected { get; private set; }
            public bool BlockConnect { get; set; }
            public bool Disposed { get; private set; }
            public int UnsubscribeFailuresRemaining { get; set; }
            public int UnsubscribeCalls { get; private set; }
            public TaskCompletionSource<bool> ConnectEntered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task ConnectAsync(CancellationToken cancellationToken)
            {
                ConnectEntered.TrySetResult(true);
                if (BlockConnect) await Task.Delay(Timeout.Infinite, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                IsConnected = true;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(GoodValue());
            }

            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                return Task.FromResult(new BatchReadResult(requests.Select(_ => GoodValue()).ToList()));
            }

            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken) { return Task.CompletedTask; }

            public Task<string> SubscribeAsync(
                SubscriptionRequest request,
                EventHandler<SubscriptionEvent> handler,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(request.SubscriptionKey);
            }

            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
            {
                UnsubscribeCalls++;
                cancellationToken.ThrowIfCancellationRequested();
                if (UnsubscribeFailuresRemaining-- > 0) throw new IOException("unsubscribe failed");
                return Task.CompletedTask;
            }

            public HealthSnapshot GetHealth()
            {
                return new HealthSnapshot(
                    IsConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected,
                    null,
                    0,
                    null);
            }

            public void Dispose()
            {
                Disposed = true;
                IsConnected = false;
            }
        }

        private sealed class LifecycleStore : IIndustrialDataStore
        {
            public bool BlockInitialize { get; set; }
            public bool Disposed { get; private set; }
            public TaskCompletionSource<bool> InitializeEntered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> ReleaseInitialize { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task InitializeAsync(CancellationToken cancellationToken)
            {
                InitializeEntered.TrySetResult(true);
                if (BlockInitialize) await ReleaseInitialize.Task;
            }

            public Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
            {
                return Task.FromResult((IReadOnlyList<IndustrialDataRecord>)new IndustrialDataRecord[0]);
            }

            public Task<int> DeleteAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public void Dispose() { Disposed = true; }
        }
    }
}
