using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Polling;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class PollingAndTimeoutStabilityTests
    {
        [Test]
        public async Task PollingWake_AfterTimedWait_IsNotConsumedByAnOldWaiter()
        {
            using (var scheduler = new PollingScheduler())
            using (var client = new PollingClient("wake-device"))
            {
                var secondTick = NewSignal();
                var secondSubscriptionTick = NewSignal();
                var tickCount = 0;

                await scheduler.SubscribeAsync(
                    client,
                    Request("slow", client.DeviceId, TimeSpan.FromMilliseconds(800)),
                    (sender, args) =>
                    {
                        if (Interlocked.Increment(ref tickCount) == 2)
                            secondTick.TrySetResult(true);
                    },
                    CancellationToken.None);

                await CompletesWithin(secondTick.Task, 2500);

                // The worker has just completed a timed wait. Give it enough time to enter
                // the next wait, then add an immediately-due subscription. A stale loser
                // waiter would consume this wake and delay the callback until the slow tick.
                await Task.Delay(40);
                await scheduler.SubscribeAsync(
                    client,
                    Request("new", client.DeviceId, TimeSpan.FromMilliseconds(800)),
                    (sender, args) => secondSubscriptionTick.TrySetResult(true),
                    CancellationToken.None);

                await CompletesWithin(secondSubscriptionTick.Task, 400);
            }
        }

        [Test]
        public async Task RemovingLastSubscription_StopsWorkerAndAllowsReplacementClient()
        {
            using (var scheduler = new PollingScheduler())
            using (var first = new PollingClient("replace-device"))
            using (var second = new PollingClient("replace-device"))
            {
                await scheduler.SubscribeAsync(
                    first,
                    Request("first", first.DeviceId, TimeSpan.FromMilliseconds(100)),
                    null,
                    CancellationToken.None);
                await scheduler.UnsubscribeAsync("first", CancellationToken.None);

                // Remove transitions the empty worker to stopping before returning, so an
                // immediate replacement never conflicts with the old client instance.
                await scheduler.SubscribeAsync(
                    second,
                    Request("second", second.DeviceId, TimeSpan.FromMilliseconds(100)),
                    null,
                    CancellationToken.None);
                await scheduler.UnsubscribeAsync("second", CancellationToken.None);

                await EventuallyAsync(() => GetWorkerCount(scheduler) == 0, 1000);
                Assert.AreEqual(0, GetWorkerCount(scheduler));
            }
        }

        [Test]
        public async Task Dispose_WaitsForInFlightReadAndSuppressesCallback()
        {
            var scheduler = new PollingScheduler();
            using (var client = new PollingClient("dispose-device") { BlockReads = true })
            {
                var callbacks = 0;
                await scheduler.SubscribeAsync(
                    client,
                    Request("blocked", client.DeviceId, TimeSpan.FromMilliseconds(100)),
                    (sender, args) => Interlocked.Increment(ref callbacks),
                    CancellationToken.None);
                await CompletesWithin(client.ReadStarted.Task, 1000);

                var disposeTask = Task.Run(() => scheduler.Dispose());
                await Task.Delay(75);
                Assert.IsFalse(disposeTask.IsCompleted, "Dispose must not release worker resources while a read still uses them.");

                client.ReleaseRead.TrySetResult(true);
                await CompletesWithin(disposeTask, 1000);
                Assert.AreEqual(0, Volatile.Read(ref callbacks), "Cancellation after the read must suppress event dispatch.");
            }
        }

        [Test]
        public async Task Dispose_WaitsForRetiringWorkerAfterClientReplacement()
        {
            var scheduler = new PollingScheduler();
            using (var first = new PollingClient("retiring-device") { BlockReads = true })
            using (var replacement = new PollingClient("retiring-device"))
            {
                await scheduler.SubscribeAsync(
                    first,
                    Request("retiring", first.DeviceId, TimeSpan.FromMilliseconds(100)),
                    null,
                    CancellationToken.None);
                await CompletesWithin(first.ReadStarted.Task, 1000);

                await scheduler.UnsubscribeAsync("retiring", CancellationToken.None);
                await scheduler.SubscribeAsync(
                    replacement,
                    Request("replacement", replacement.DeviceId, TimeSpan.FromMilliseconds(100)),
                    null,
                    CancellationToken.None);

                var disposeTask = Task.Run(() => scheduler.Dispose());
                await Task.Delay(75);
                Assert.IsFalse(disposeTask.IsCompleted,
                    "A stopping worker removed during replacement must still participate in scheduler disposal.");

                first.ReleaseRead.TrySetResult(true);
                await CompletesWithin(disposeTask, 1000);
            }
        }

        [Test]
        public async Task Dispose_FromSubscriptionHandler_DoesNotDeadlock()
        {
            var scheduler = new PollingScheduler();
            using (var client = new PollingClient("self-dispose-device"))
            {
                var returned = NewSignal();
                await scheduler.SubscribeAsync(
                    client,
                    Request("self-dispose", client.DeviceId, TimeSpan.FromMilliseconds(100)),
                    (sender, args) =>
                    {
                        scheduler.Dispose();
                        returned.TrySetResult(true);
                    },
                    CancellationToken.None);

                await CompletesWithin(returned.Task, 1000);
            }
        }

        [Test]
        public async Task Timeout_KeepsFollowingOperationOutUntilCoreTaskActuallyEnds()
        {
            var logger = new RecordingLogger();
            using (var client = new NonCooperativeClient(logger, 35))
            {
                var first = await client.ReadAsync(
                    new ReadRequest(client.DeviceId, "first", DataType.Int16),
                    CancellationToken.None);
                Assert.AreEqual(QualityStatus.Bad, first.Quality);
                Assert.AreEqual(1, client.TimeoutCleanupCalls);

                var secondTask = client.ReadAsync(
                    new ReadRequest(client.DeviceId, "second", DataType.Int16),
                    CancellationToken.None);

                await Task.Delay(100);
                Assert.IsFalse(client.SecondReadEntered.Task.IsCompleted,
                    "The next operation must remain outside the core while the timed-out task is still running.");
                Assert.AreEqual(1, client.MaximumConcurrentCoreCalls);

                client.FirstRead.TrySetException(new InvalidOperationException("late failure"));
                var second = await CompletesWithin(secondTask, 1000);

                Assert.AreEqual(QualityStatus.Good, second.Quality);
                Assert.AreEqual(1, client.MaximumConcurrentCoreCalls);
                await CompletesWithin(logger.LateFailureLogged.Task, 1000);
                StringAssert.Contains("Late read core task failed", logger.LastErrorMessage);
            }
        }

        private static SubscriptionRequest Request(string key, string deviceId, TimeSpan interval)
        {
            return new SubscriptionRequest(
                key,
                deviceId,
                new[] { new ReadRequest(deviceId, "D0", DataType.Int16) },
                interval,
                false);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task CompletesWithin(Task task, int milliseconds)
        {
            var completed = await Task.WhenAny(task, Task.Delay(milliseconds));
            Assert.AreSame(task, completed, "Operation did not complete within {0}ms.", milliseconds);
            await task;
        }

        private static async Task<T> CompletesWithin<T>(Task<T> task, int milliseconds)
        {
            await CompletesWithin((Task)task, milliseconds);
            return await task;
        }

        private static async Task EventuallyAsync(Func<bool> condition, int milliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(10);
        }

        private static int GetWorkerCount(PollingScheduler scheduler)
        {
            var field = typeof(PollingScheduler).GetField("_workers", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var workers = field.GetValue(scheduler);
            var count = workers.GetType().GetProperty("Count");
            Assert.NotNull(count);
            return (int)count.GetValue(workers, null);
        }

        private sealed class PollingClient : IIndustrialClient, IProtocolCapabilityProvider
        {
            public PollingClient(string deviceId)
            {
                DeviceId = deviceId;
                Capabilities = new ProtocolCapabilities(
                    ProtocolKind.ModbusTcp,
                    "Polling test",
                    maxReadItems: 100,
                    maxWriteItems: 100,
                    maxAddressSpan: 100,
                    recommendedMinPollingInterval: TimeSpan.FromMilliseconds(10));
            }

            public string DeviceId { get; private set; }
            public ProtocolKind Kind { get { return ProtocolKind.ModbusTcp; } }
            public bool IsConnected { get { return true; } }
            public ProtocolCapabilities Capabilities { get; private set; }
            public bool BlockReads { get; set; }
            public TaskCompletionSource<bool> ReadStarted { get; } = NewSignal();
            public TaskCompletionSource<bool> ReleaseRead { get; } = NewSignal();

            public Task ConnectAsync(CancellationToken token) { return Task.CompletedTask; }
            public Task DisconnectAsync(CancellationToken token) { return Task.CompletedTask; }
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken token)
            {
                return Task.FromResult(Good(request.Address));
            }

            public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken token)
            {
                ReadStarted.TrySetResult(true);
                if (BlockReads)
                    await ReleaseRead.Task.ConfigureAwait(false);

                var values = new List<DataValue>();
                foreach (var request in requests)
                    values.Add(Good(request.Address));
                return new BatchReadResult(values);
            }

            public Task WriteAsync(WriteRequest request, CancellationToken token) { return Task.CompletedTask; }
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken token) { return Task.CompletedTask; }
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken token)
            {
                return Task.FromResult(request.SubscriptionKey);
            }
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken token) { return Task.CompletedTask; }
            public HealthSnapshot GetHealth() { return new HealthSnapshot(ConnectionStatus.Connected, null, 0, null); }
            public void Dispose() { ReleaseRead.TrySetResult(true); }
        }

        private sealed class NonCooperativeClient : IndustrialClientBase
        {
            private int _readCalls;
            private int _activeCoreCalls;
            private int _maximumConcurrentCoreCalls;

            public NonCooperativeClient(IIndustrialLogger logger, int timeoutMilliseconds)
                : base("timeout-device", ProtocolKind.ModbusTcp, new PollingScheduler(), logger, timeoutMilliseconds)
            {
            }

            public TaskCompletionSource<bool> FirstRead { get; } = NewSignal();
            public TaskCompletionSource<bool> SecondReadEntered { get; } = NewSignal();
            public int TimeoutCleanupCalls { get; private set; }
            public int MaximumConcurrentCoreCalls { get { return Volatile.Read(ref _maximumConcurrentCoreCalls); } }
            public override bool IsConnected { get { return true; } }

            protected override Task ConnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            protected override Task DisconnectCoreAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }

            protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                var active = Interlocked.Increment(ref _activeCoreCalls);
                UpdateMaximum(active);
                try
                {
                    if (Interlocked.Increment(ref _readCalls) == 1)
                        await FirstRead.Task.ConfigureAwait(false);
                    else
                        SecondReadEntered.TrySetResult(true);

                    return Good(request.Address);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeCoreCalls);
                }
            }

            protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            protected override void OnOperationTimeout()
            {
                TimeoutCleanupCalls++;
            }

            private void UpdateMaximum(int value)
            {
                while (true)
                {
                    var current = Volatile.Read(ref _maximumConcurrentCoreCalls);
                    if (current >= value || Interlocked.CompareExchange(ref _maximumConcurrentCoreCalls, value, current) == current)
                        return;
                }
            }
        }

        private sealed class RecordingLogger : IIndustrialLogger
        {
            public TaskCompletionSource<bool> LateFailureLogged { get; } = NewSignal();
            public string LastErrorMessage { get; private set; }
            public void Trace(string message) { }
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception exception)
            {
                if (message != null && message.IndexOf("Late ", StringComparison.Ordinal) >= 0)
                {
                    LastErrorMessage = message;
                    LateFailureLogged.TrySetResult(true);
                }
            }
        }

        private static DataValue Good(string address)
        {
            return new DataValue(address, DataType.Int16, (short)1, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }
    }
}
