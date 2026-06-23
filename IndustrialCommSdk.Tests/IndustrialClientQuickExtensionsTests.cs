using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class IndustrialClientQuickExtensionsTests
    {
        [Test]
        public async Task ReadInt16Async_Should_Build_Request_And_Return_Typed_Value()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int16, (short)123, null, QualityStatus.Good, DateTimeOffset.UtcNow, null)
            };

            var value = await client.ReadInt16Async("D100");

            Assert.That(value, Is.EqualTo(123));
            Assert.That(client.LastReadRequest, Is.Not.Null);
            Assert.That(client.LastReadRequest.DeviceId, Is.EqualTo("fake-1"));
            Assert.That(client.LastReadRequest.Address, Is.EqualTo("D100"));
            Assert.That(client.LastReadRequest.DataType, Is.EqualTo(DataType.Int16));
        }

        [Test]
        public void ReadValueAsync_Should_Throw_When_Quality_Is_Bad()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int16, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, "boom")
            };

            var ex = Assert.ThrowsAsync<IndustrialProtocolException>(async () => await client.ReadInt16Async("D100"));

            Assert.That(ex.Message, Is.EqualTo("boom"));
        }

        [Test]
        public void ReadValueAsync_Should_Throw_When_Type_Cannot_Be_Converted()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int16, "abc", null, QualityStatus.Good, DateTimeOffset.UtcNow, null)
            };

            Assert.ThrowsAsync<IndustrialDataConversionException>(async () => await client.ReadInt16Async("D100"));
        }

        [Test]
        public async Task WriteAsync_Bool_Should_Build_Write_Request()
        {
            var client = new FakeIndustrialClient();

            await client.WriteAsync("M10", true);

            Assert.That(client.LastWriteRequest, Is.Not.Null);
            Assert.That(client.LastWriteRequest.DeviceId, Is.EqualTo("fake-1"));
            Assert.That(client.LastWriteRequest.Address, Is.EqualTo("M10"));
            Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.Bool));
            Assert.That(client.LastWriteRequest.Value, Is.EqualTo(true));
        }

        [Test]
        public async Task WriteStringAsync_Should_Use_Provided_Length()
        {
            var client = new FakeIndustrialClient();

            await client.WriteStringAsync("D200", "ABC", 2);

            Assert.That(client.LastWriteRequest.Length, Is.EqualTo(2));
            Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.String));
        }

        private sealed class FakeIndustrialClient : IIndustrialClient
        {
            public string DeviceId { get; } = "fake-1";
            public ProtocolKind Kind { get; } = ProtocolKind.ModbusTcp;
            public bool IsConnected { get; } = true;
            public ReadRequest LastReadRequest { get; private set; }
            public WriteRequest LastWriteRequest { get; private set; }
            public DataValue ReadResult { get; set; }

            public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                LastReadRequest = request;
                return Task.FromResult(ReadResult);
            }

            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                LastWriteRequest = request;
                return Task.CompletedTask;
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
                return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null);
            }

            public void Dispose()
            {
            }
        }
    }
}
