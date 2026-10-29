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
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class NamedTagReadTests
    {
        [Test]
        public async Task SelectedNamesReadOnlyRequestedTagsInRequestedOrder()
        {
            var client = new RecordingClient();
            using var device = CreateDevice(client);

            var result = await device.ReadManyAsync("Temperature", "speed");

            Assert.That(client.Requests.Select(request => request.Address), Is.EqualTo(new[] { "D2", "D0" }));
            Assert.That(result.Tags.Select(tag => tag.Name), Is.EqualTo(new[] { "Temperature", "Speed" }));
            Assert.That(result.GetByName<float>("SPEED"), Is.EqualTo(42.5f));
            Assert.That(result.GetByName<float>("Temperature"), Is.EqualTo(23.25f));
            Assert.That(result.TryGetByName<float>("speed", out var speed), Is.True);
            Assert.That(speed, Is.EqualTo(42.5f));
        }

        [Test]
        public async Task DuplicateSelectionsReadThePointOnlyOnce()
        {
            var client = new RecordingClient();
            using var device = CreateDevice(client);

            var result = await device.ReadManyAsync("Speed", "speed", "SPEED");

            Assert.That(client.Requests.Count, Is.EqualTo(1));
            Assert.That(result.Tags.Count, Is.EqualTo(1));
            Assert.That(result.GetByName<float>("Speed"), Is.EqualTo(42.5f));
        }

        [Test]
        public void UnknownOrEmptyNamesFailBeforeSendingAnyRead()
        {
            var client = new RecordingClient();
            using var device = CreateDevice(client);

            var error = Assert.Throws<KeyNotFoundException>(() => device.ReadManyAsync("Speed", "Missing"));
            Assert.That(error.Message, Does.Contain("Missing"));
            Assert.Throws<ArgumentException>(() => device.ReadManyAsync("Speed", " "));
            Assert.Throws<ArgumentException>(() => device.ReadManyAsync(Array.Empty<string>(), CancellationToken.None));
            Assert.That(client.ReadCalls, Is.Zero);
        }

        [Test]
        public async Task NamedReadForwardsCancellationAndRejectsAlreadyCancelledCall()
        {
            var client = new RecordingClient();
            using var device = CreateDevice(client);
            using var cancellation = new CancellationTokenSource();

            await device.ReadManyAsync(new[] { "Speed" }, cancellation.Token);
            Assert.That(client.Token, Is.EqualTo(cancellation.Token));
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => device.ReadManyAsync(new[] { "Speed" }, cancellation.Token));
            Assert.That(client.ReadCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task NoNamesStillReadsTheWholeTable()
        {
            var client = new RecordingClient();
            using var device = CreateDevice(client);

            var result = await device.ReadManyAsync();
            var withDefaultToken = await device.ReadManyAsync(default);

            Assert.That(client.Requests.Count, Is.EqualTo(3));
            Assert.That(result.GetByName<int>("Count"), Is.EqualTo(10));
            Assert.That(withDefaultToken.GetByName<float>("Speed"), Is.EqualTo(42.5f));
        }

        [Test]
        public async Task HostedDeviceForwardsNamedReadsAndCancellationToken()
        {
            var directory = Path.Combine(Path.GetTempPath(), "indulink-named-read-" + Guid.NewGuid().ToString("N"));
            var pointsFile = Path.Combine(directory, "points.json");
            Directory.CreateDirectory(directory);
            try
            {
                new TagTable(new[] { Tag.Float("D0", "Speed"), Tag.Float("D2", "Temperature") }).SaveJson(pointsFile);
                var config = new InduLinkSdkConfig
                {
                    Devices = new List<InduLinkDeviceConfig>
                    {
                        new InduLinkDeviceConfig { Name = "line", DeviceId = "line", Protocol = "modbus-tcp", PointsFile = "points.json" },
                    },
                };
                var client = new RecordingClient();
                using var host = new InduLinkDeviceHost(config, directory, _ => client);
                var device = host.Get("line");
                var result = await device.ReadManyAsync("Speed", "Temperature");
                Assert.That(result.GetByName<float>("Speed"), Is.EqualTo(42.5f));
                Assert.That(client.Requests.Count, Is.EqualTo(2));

                using var cancellation = new CancellationTokenSource();
                var selected = await device.ReadManyAsync(new[] { "Temperature" }, cancellation.Token);
                Assert.That(client.Token, Is.EqualTo(cancellation.Token));
                Assert.That(client.Requests.Select(request => request.Address), Is.EqualTo(new[] { "D2" }));
                Assert.That(selected.GetByName<float>("Temperature"), Is.EqualTo(23.25f));
            }
            finally
            {
                File.Delete(pointsFile);
                Directory.Delete(directory);
            }
        }

        [Test]
        public void BusinessNameAndAddressLookupsRemainSeparate()
        {
            // 名称故意与另一个点位的地址相同，确认新入口没有改变原有 Get(address)。
            var tags = new[] { Tag.Float("D0", "D2"), Tag.Float("D2", "Other") };
            var result = new InduLinkTagReadResult(tags, new[] { Good("D0", 42.5f), Good("D2", 23.25f) });

            Assert.That(result.GetByName<float>("D2"), Is.EqualTo(42.5f));
            Assert.That(result.Get<float>("D2"), Is.EqualTo(23.25f));
            Assert.Throws<KeyNotFoundException>(() => result.Get<float>("Other"));
            Assert.That(result.TryGetByName<float>("D0", out _), Is.False);
        }

        [Test]
        public void BadQualityRemainsAvailableWhenNamedConversionFails()
        {
            var timestamp = DateTimeOffset.UtcNow;
            var bad = new DataValue("D0", DataType.Float, null, null, QualityStatus.Bad, timestamp, "PLC did not respond.");
            var result = new InduLinkTagReadResult(new[] { Tag.Float("D0", "Speed") }, new[] { bad });

            Assert.Throws<InduLinkProtocolException>(() => result.GetByName<float>("Speed"));
            Assert.That(result.TryGetByName<float>("Speed", out var speed), Is.False);
            Assert.That(speed, Is.Zero);
            Assert.That(result.Values[0], Is.SameAs(bad));
            Assert.That(result.Values[0].Timestamp, Is.EqualTo(timestamp));
            Assert.That(result.Values[0].ErrorMessage, Is.EqualTo("PLC did not respond."));
        }

        [Test]
        public void TryGetByNameReturnsFalseForMissingNullOrUnconvertibleValues()
        {
            var tags = new[] { Tag.Float("D0", "Speed"), Tag.Float("D2", "Temperature") };
            var result = new InduLinkTagReadResult(tags, new[] { Good("D0", "not-a-number"), Good("D2", null) });

            Assert.That(result.TryGetByName<float>(null, out _), Is.False);
            Assert.That(result.TryGetByName<float>("Missing", out _), Is.False);
            Assert.That(result.TryGetByName<float>("Speed", out _), Is.False);
            Assert.That(result.TryGetByName<float>("Temperature", out _), Is.False);
            Assert.Throws<InduLinkDataConversionException>(() => result.GetByName<float>("Speed"));
        }

        [Test]
        public void AmbiguousNamesDoNotChangeExistingAddressAccess()
        {
            var tags = new[] { Tag.Float("D0", "Speed"), Tag.Float("D2", "speed") };
            var result = new InduLinkTagReadResult(tags, new[] { Good("D0", 42.5f), Good("D2", 23.25f) });

            Assert.Throws<InvalidOperationException>(() => result.GetByName<float>("Speed"));
            Assert.That(result.TryGetByName<float>("Speed", out _), Is.False);
            Assert.That(result.Get<float>("D0"), Is.EqualTo(42.5f));
        }

        private static InduLinkConfiguredClient CreateDevice(RecordingClient client)
        {
            return new InduLinkConfiguredClient("line", client, new TagTable(new InduLinkTag[]
            {
                Tag.Float("D0", "Speed"), Tag.Float("D2", "Temperature"), Tag.Int32("D4", "Count"),
            }));
        }

        private static DataValue Good(string address, object value) =>
            new DataValue(address, DataType.Float, value, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);

        private sealed class RecordingClient : IInduLinkClient
        {
            public string DeviceId => "line";
            public ProtocolKind Kind => ProtocolKind.ModbusTcp;
            public bool IsConnected => true;
            public int ReadCalls { get; private set; }
            public IReadOnlyList<ReadRequest> Requests { get; private set; }
            public CancellationToken Token { get; private set; }

            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadCalls++;
                Requests = requests.ToArray();
                Token = cancellationToken;
                return Task.FromResult(new BatchReadResult(requests.Select(request => new DataValue(
                    request.Address, request.DataType,
                    request.Address == "D0" ? 42.5f : request.Address == "D2" ? 23.25f : 10,
                    null, QualityStatus.Good, DateTimeOffset.UtcNow, null)).ToArray()));
            }

            public Task ConnectAsync(CancellationToken token) => Task.CompletedTask;
            public Task DisconnectAsync(CancellationToken token) => Task.CompletedTask;
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task WriteAsync(WriteRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken token) => throw new NotSupportedException();
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken token) => throw new NotSupportedException();
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken token) => throw new NotSupportedException();
            public HealthSnapshot GetHealth() => new HealthSnapshot(ConnectionStatus.Connected, null, 0, null);
            public void Dispose() { }
        }
    }
}
