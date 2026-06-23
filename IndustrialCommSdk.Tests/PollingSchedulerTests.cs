using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Polling;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class PollingSchedulerTests
    {
        [Test]
        public async Task PollingScheduler_Should_Raise_Subscription_Event()
        {
            var scheduler = new PollingScheduler();
            var client = new FakeIndustrialClient();
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

        private sealed class FakeIndustrialClient : IIndustrialClient
        {
            public string DeviceId { get { return "fake"; } }
            public ProtocolKind Kind { get { return ProtocolKind.TcpSocket; } }
            public bool IsConnected { get { return true; } }

            public Task ConnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task DisconnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public void Dispose() { }
            public HealthSnapshot GetHealth() { return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null); }
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new DataValue(request.Address, request.DataType, 42, new byte[] { 0x00, 0x2A }, QualityStatus.Good, DateTimeOffset.UtcNow, null));
            }

            public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                var values = new List<DataValue>();
                foreach (var request in requests)
                {
                    values.Add(await ReadAsync(request, cancellationToken));
                }
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
    }
}
