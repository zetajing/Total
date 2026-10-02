using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Exceptions;
using InduLink.Runtime;
using InduLink.Runtime.Configuration;
using Newtonsoft.Json;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class RuntimeBoundaryTests
    {
        [TestCase(ProtocolKind.Redis)]
        [TestCase(ProtocolKind.Mqtt)]
        [TestCase(ProtocolKind.OpcUa)]
        [TestCase(ProtocolKind.TwinCatAds)]
        public async Task CaseSensitiveAddressesRemainDistinct(ProtocolKind kind)
        {
            var client = new BoundaryClient { Kind = kind };
            var tags = new[] { new InduLinkTag("Line/Speed", DataType.Int32, name: "A"), new InduLinkTag("line/speed", DataType.Int32, name: "B") };
            var table = new TagTable(tags, ProtocolAddressComparer.ForProtocol(kind));
            Assert.That(table.GetByAddress("line/speed"), Is.SameAs(tags[1]));
            var map = await client.ReadManyAsync<int>(new[] { "Line/Speed", "line/speed" });
            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(map["Line/Speed"], Is.EqualTo(11));
            Assert.That(map["line/speed"], Is.EqualTo(22));
            var result = await client.ReadManyAsync(tags);
            Assert.That(result.Get<int>("Line/Speed"), Is.EqualTo(11));
            Assert.That(result.Get<int>("line/speed"), Is.EqualTo(22));
            var json = table.ToJson();
            Assert.That(TagTable.FromJson(json).Tags.Count, Is.EqualTo(2));
        }

        [TestCase(ProtocolKind.ModbusTcp)]
        [TestCase(ProtocolKind.ModbusRtu)]
        [TestCase(ProtocolKind.SiemensS7)]
        public void PlcAddressComparisonRemainsCaseInsensitive(ProtocolKind kind)
        {
            var tags = new[] { new InduLinkTag("D1", DataType.Int32, name: "A") };
            var table = new TagTable(tags, ProtocolAddressComparer.ForProtocol(kind));
            Assert.That(table.GetByAddress("d1"), Is.SameAs(tags[0]));
            Assert.Throws<ArgumentException>(() => new TagTable(new[] { tags[0], new InduLinkTag("d1", DataType.Int32) }, ProtocolAddressComparer.ForProtocol(kind)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GatewayPreservesEarlierSuccessAndStopsAfterFailedWrite(bool uncertain)
        {
            using var fixture = new HostFixture(new BoundaryClient { WriteError = uncertain
                ? new InduLinkWriteUncertainException("private address D1") : new InduLinkProtocolException("private address D1") });
            using var gateway = new InduLinkTagGateway(fixture.Host, new InduLinkTagGatewayOptions { EnableRemoteWrites = true });
            var results = await gateway.WriteAsync(new[] { new TagGatewayWriteItem("plc", "A", 1), new TagGatewayWriteItem("plc", "B", 2), new TagGatewayWriteItem("plc", "C", 3) });
            Assert.That(results.Select(r => r.Status), Is.EqualTo(new[] { TagGatewayWriteStatus.Succeeded,
                uncertain ? TagGatewayWriteStatus.Uncertain : TagGatewayWriteStatus.Failed, TagGatewayWriteStatus.NotAttempted }));
            Assert.That(results[0].Succeeded, Is.True);
            Assert.That(fixture.Client.WriteCalls, Is.EqualTo(2));
            Assert.That(results[1].ErrorMessage, Does.Not.Contain("D1"));
            Assert.That(JsonConvert.SerializeObject(results), Does.Contain(uncertain ? "Uncertain" : "Failed"));
        }

        [Test]
        public async Task CancellationDuringWritePreservesUncertainOutcomeAndEarlierSuccess()
        {
            using var cancellation = new CancellationTokenSource();
            using var fixture = new HostFixture(new BoundaryClient
            {
                WriteError = new InduLinkWriteUncertainException("in-flight write cancelled"),
                CancelOnSecondWrite = cancellation,
            });
            using var gateway = new InduLinkTagGateway(fixture.Host, new InduLinkTagGatewayOptions { EnableRemoteWrites = true });
            var results = await gateway.WriteAsync(new[] { new TagGatewayWriteItem("plc", "A", 1), new TagGatewayWriteItem("plc", "B", 2), new TagGatewayWriteItem("plc", "C", 3) }, cancellation.Token);
            Assert.That(results.Select(r => r.Status), Is.EqualTo(new[] { TagGatewayWriteStatus.Succeeded, TagGatewayWriteStatus.Uncertain, TagGatewayWriteStatus.NotAttempted }));
            Assert.That(fixture.Client.WriteCalls, Is.EqualTo(2));
        }

        [Test]
        public async Task StopHonorsCallerCancellationWhileReconnectIgnoresCancellation()
        {
            using var fixture = new HostFixture(new BoundaryClient());
            await fixture.Host.StartAsync();
            fixture.Client.IsConnected = false;
            await fixture.Client.ReconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var cancellation = new CancellationTokenSource(50);
            try
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await fixture.Host.StopAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.That(fixture.Client.Disposed, Is.False);
            }
            finally { fixture.Client.ReleaseReconnect.TrySetResult(true); }
            await fixture.Host.StopAsync();
            Assert.That(fixture.Host.Get("plc").IsStarted, Is.False);
        }

        [Test]
        public async Task DisposeReturnsWithinBudgetAndDefersResourcesUntilReconnectExits()
        {
            using var fixture = new HostFixture(new BoundaryClient(), TimeSpan.FromMilliseconds(80));
            await fixture.Host.StartAsync();
            fixture.Client.IsConnected = false;
            await fixture.Client.ReconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                Assert.ThrowsAsync<TimeoutException>(async () => await fixture.Host.StopAsync().WaitAsync(TimeSpan.FromSeconds(2)));
                await fixture.Host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(fixture.Client.Disposed, Is.False);
            }
            finally { fixture.Client.ReleaseReconnect.TrySetResult(true); }
            await fixture.Client.DisposeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(fixture.Client.Disposed, Is.True);
        }

        private sealed class HostFixture : IDisposable
        {
            private readonly string _directory = Path.Combine(Path.GetTempPath(), "indulink-boundary-" + Guid.NewGuid().ToString("N"));
            public BoundaryClient Client { get; }
            public InduLinkDeviceHost Host { get; }
            public HostFixture(BoundaryClient client, TimeSpan? timeout = null)
            {
                Client = client;
                Directory.CreateDirectory(_directory);
                File.WriteAllText(Path.Combine(_directory, "points.json"), "{\"tags\":[{\"name\":\"A\",\"address\":\"D0\",\"type\":\"Int32\",\"writable\":true},{\"name\":\"B\",\"address\":\"D1\",\"type\":\"Int32\",\"writable\":true},{\"name\":\"C\",\"address\":\"D2\",\"type\":\"Int32\",\"writable\":true}]}");
                var config = new InduLinkSdkConfig { Devices = new List<InduLinkDeviceConfig> { new InduLinkDeviceConfig { Name = "plc", DeviceId = "plc", Protocol = "modbus-tcp", PointsFile = "points.json", Runtime = new InduLinkDeviceRuntimeOptions { ReconnectDelayMilliseconds = 20 } } } };
                Host = new InduLinkDeviceHost(config, _directory, _ => client, options: new InduLinkDeviceHostOptions { ShutdownTimeout = timeout ?? TimeSpan.FromSeconds(2) });
            }
            public void Dispose()
            {
                Client.ReleaseReconnect.TrySetResult(true);
                Host.Dispose();
                Directory.Delete(_directory, true);
            }
        }

        private sealed class BoundaryClient : IInduLinkClient
        {
            public string DeviceId => "plc";
            public ProtocolKind Kind { get; set; } = ProtocolKind.ModbusTcp;
            public bool IsConnected { get; set; }
            public bool Disposed { get; private set; }
            public int WriteCalls { get; private set; }
            public Exception WriteError { get; set; }
            public CancellationTokenSource CancelOnSecondWrite { get; set; }
            private int _connectCalls;
            public TaskCompletionSource<bool> ReconnectEntered { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> ReleaseReconnect { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> DisposeCompleted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task ConnectAsync(CancellationToken token)
            {
                if (++_connectCalls > 1) { ReconnectEntered.TrySetResult(true); await ReleaseReconnect.Task; }
                token.ThrowIfCancellationRequested();
                IsConnected = true;
            }
            public Task DisconnectAsync(CancellationToken token) { IsConnected = false; return Task.CompletedTask; }
            public Task<DataValue> ReadAsync(ReadRequest r, CancellationToken token) => Task.FromResult(Good(r.Address));
            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken token) => Task.FromResult(new BatchReadResult(requests.Select(r => Good(r.Address)).ToList()));
            private static DataValue Good(string address) => new DataValue(address, DataType.Int32, address == "Line/Speed" ? 11 : 22, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
            public Task WriteAsync(WriteRequest request, CancellationToken token)
            {
                if (++WriteCalls == 2)
                {
                    CancelOnSecondWrite?.Cancel();
                    if (WriteError != null) return Task.FromException(WriteError);
                }
                return Task.CompletedTask;
            }
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken token) => throw new NotSupportedException();
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken token) => Task.FromResult(request.SubscriptionKey);
            public Task UnsubscribeAsync(string id, CancellationToken token) => Task.CompletedTask;
            public HealthSnapshot GetHealth() => new HealthSnapshot(IsConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected, null, 0, null);
            public void Dispose() { Disposed = true; IsConnected = false; DisposeCompleted.TrySetResult(true); }
        }
    }
}
