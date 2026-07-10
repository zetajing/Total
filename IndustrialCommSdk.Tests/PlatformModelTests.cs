using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class PlatformModelTests
    {
        [Test]
        public void ProtocolCapabilities_ForModbusTcp_ShouldExposeOptimizedBatching()
        {
            var capabilities = ProtocolCapabilities.ForProtocol(ProtocolKind.ModbusTcp);

            Assert.That(capabilities.DisplayName, Is.EqualTo("Modbus TCP"));
            Assert.That(capabilities.SupportsBatchRead, Is.True);
            Assert.That(capabilities.SupportsOptimizedBatchRead, Is.True);
            Assert.That(capabilities.SupportsBitAddress, Is.True);
            Assert.That(capabilities.MaxAddressSpan, Is.GreaterThan(1));
        }

        [Test]
        public void ProtocolCapabilities_ForTcpSocket_ShouldRepresentRawTransport()
        {
            var capabilities = ProtocolCapabilities.ForProtocol(ProtocolKind.TcpSocket);

            Assert.That(capabilities.SupportsRawTransport, Is.True);
            Assert.That(capabilities.SupportsBatchRead, Is.False);
            Assert.That(capabilities.SupportsSubscriptions, Is.False);
        }

        [Test]
        public void IndustrialAddress_ShouldPreserveNormalizedShape()
        {
            var address = new IndustrialAddress("%DB1.DBX0.1", "DB1.DBX0.1", "DB", 0, 1);

            Assert.That(address.Original, Is.EqualTo("%DB1.DBX0.1"));
            Assert.That(address.Normalized, Is.EqualTo("DB1.DBX0.1"));
            Assert.That(address.Area, Is.EqualTo("DB"));
            Assert.That(address.Offset, Is.EqualTo(0));
            Assert.That(address.Bit, Is.EqualTo(1));
            Assert.That(address.IsBitAddress, Is.True);
            Assert.That(address.ToString(), Is.EqualTo("DB1.DBX0.1"));
        }

        [Test]
        public void BatchOptions_ShouldRejectInvalidLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BatchReadOptions(maxItemsPerBatch: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BatchWriteOptions(totalTimeout: TimeSpan.Zero));
        }

        [Test]
        public void BatchSplitPlan_ShouldCalculateSavedRequestCount()
        {
            var requests = new[]
            {
                new ReadRequest("dev", "D100", DataType.Int16),
                new ReadRequest("dev", "D101", DataType.Int16),
                new ReadRequest("dev", "D102", DataType.Int16),
            };

            var group = BatchRequestGroup.ForRead(0, "D", 100, 102, DataType.Int16, requests);
            var plan = new BatchSplitPlan(ProtocolKind.ModbusTcp, BatchOperationKind.Read, new[] { group }, requests.Length);

            Assert.That(plan.OriginalRequestCount, Is.EqualTo(3));
            Assert.That(plan.PlannedRequestCount, Is.EqualTo(1));
            Assert.That(plan.SavedRequestCount, Is.EqualTo(2));
            Assert.That(plan.Groups[0].ReadRequests.Count, Is.EqualTo(3));
        }

        [Test]
        public void GetCapabilities_ShouldUseProviderOverride()
        {
            var client = new CapabilityClient();
            var capabilities = IndustrialCommSdk.IndustrialClientPlatformExtensions.GetCapabilities(client);

            Assert.That(capabilities.DisplayName, Is.EqualTo("Custom Protocol"));
            Assert.That(capabilities.MaxReadItems, Is.EqualTo(9));
        }

        [Test]
        public void GetCapabilities_ShouldFallbackToProtocolDefaults()
        {
            var client = new PlainClient();
            var capabilities = IndustrialCommSdk.IndustrialClientPlatformExtensions.GetCapabilities(client);

            Assert.That(capabilities.Kind, Is.EqualTo(ProtocolKind.SiemensS7));
            Assert.That(capabilities.SupportsBitAddress, Is.True);
        }

        private sealed class CapabilityClient : PlainClient, IProtocolCapabilityProvider
        {
            public ProtocolCapabilities Capabilities
            {
                get { return new ProtocolCapabilities(ProtocolKind.TcpSocket, "Custom Protocol", maxReadItems: 9, maxWriteItems: 9); }
            }
        }

        private class PlainClient : IIndustrialClient
        {
            public string DeviceId { get { return "dev"; } }
            public ProtocolKind Kind { get { return ProtocolKind.SiemensS7; } }
            public bool IsConnected { get { return true; } }

            public Task ConnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task DisconnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
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
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                return Task.FromResult("sub");
            }
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken) { return Task.CompletedTask; }
            public HealthSnapshot GetHealth() { return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null); }
            public void Dispose() { }
        }
    }
}
