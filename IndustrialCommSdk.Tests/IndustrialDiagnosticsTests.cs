using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class IndustrialDiagnosticsTests
    {
        [Test]
        public async Task TestAsync_Should_Return_Success_And_Disconnect_When_Initially_Disconnected()
        {
            var client = new FakeClient();

            var result = await client.TestAsync();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.DeviceId, Is.EqualTo("fake"));
            Assert.That(client.ConnectCount, Is.EqualTo(1));
            Assert.That(client.DisconnectCount, Is.EqualTo(1));
            Assert.That(client.IsConnected, Is.False);
        }

        [Test]
        public async Task TestAsync_Should_Return_Failure_When_Connect_Fails()
        {
            var client = new FakeClient { ConnectException = new InvalidOperationException("boom") };

            var result = await client.TestAsync();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("boom"));
            Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
        }

        private sealed class FakeClient : IIndustrialClient
        {
            public string DeviceId { get; } = "fake";
            public ProtocolKind Kind { get; } = ProtocolKind.ModbusTcp;
            public bool IsConnected { get; private set; }
            public int ConnectCount { get; private set; }
            public int DisconnectCount { get; private set; }
            public Exception ConnectException { get; set; }

            public Task ConnectAsync(CancellationToken cancellationToken)
            {
                ConnectCount++;
                if (ConnectException != null)
                {
                    throw ConnectException;
                }

                IsConnected = true;
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                DisconnectCount++;
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public HealthSnapshot GetHealth()
            {
                return new HealthSnapshot(IsConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected, null, 0, null);
            }

            public void Dispose()
            {
            }
        }
    }
}
