using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Configuration;
using IndustrialCommSdk.Runtime.Security;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class IndustrialTagGatewayTests
    {
        [Test]
        public void TagTable_PreservesWritableAndDefaultsOldFilesToFalse()
        {
            var oldTable = TagTable.FromJson("{\"tags\":[{\"name\":\"Old\",\"address\":\"D0\",\"type\":\"Int16\"}]}");
            Assert.IsFalse(oldTable.Get("Old").Writable);

            var table = TagTable.FromJson("{\"tags\":[{\"name\":\"Setpoint\",\"address\":\"D1\",\"type\":\"Int16\",\"writable\":true}]}");
            Assert.IsTrue(table.Get("Setpoint").Writable);
            Assert.IsTrue(TagTable.FromJson(table.ToJson()).Get("Setpoint").Writable);

            var csv = TagTable.ParseCsv("Name,Address,Type,Length,Writable\r\nSetpoint,D1,Int16,1,yes");
            Assert.IsTrue(csv.Get("Setpoint").Writable);
        }

        [Test]
        public async Task Gateway_UsesNamedTagsAndEnforcesBothWriteSwitches()
        {
            var directory = CreateDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "points.json"),
                    "{\"tags\":[" +
                    "{\"name\":\"ReadOnly\",\"address\":\"D0\",\"type\":\"Int16\"}," +
                    "{\"name\":\"Setpoint\",\"address\":\"D1\",\"type\":\"Int16\",\"writable\":true}," +
                    "{\"address\":\"D2\",\"type\":\"Int16\"}]}");
                var fake = new GatewayFakeClient("device");
                using (var host = CreateHost(directory, fake))
                using (var gateway = new IndustrialTagGateway(host))
                {
                    Assert.That(gateway.GetTags("device").Select(tag => tag.Name), Is.EqualTo(new[] { "ReadOnly", "Setpoint" }));
                    Assert.That(gateway.GetTags("device").All(tag => tag.Address == null), Is.True);

                    var reads = await gateway.ReadAsync(new[]
                    {
                        new TagGatewayReadItem("device", "ReadOnly"),
                        new TagGatewayReadItem("device", "Setpoint"),
                    });
                    Assert.That(reads.All(value => value.Quality == QualityStatus.Good), Is.True);
                    Assert.AreEqual(2, fake.LastReadBatchSize);

                    var disabled = await gateway.WriteAsync(new[] { new TagGatewayWriteItem("device", "Setpoint", 12L) });
                    Assert.IsFalse(disabled[0].Succeeded);
                    Assert.That(disabled[0].ErrorMessage, Does.Contain("disabled"));

                    gateway.Options.EnableRemoteWrites = true;
                    var readOnly = await gateway.WriteAsync(new[] { new TagGatewayWriteItem("device", "ReadOnly", 12) });
                    Assert.IsFalse(readOnly[0].Succeeded);

                    var written = await gateway.WriteAsync(new[] { new TagGatewayWriteItem("device", "Setpoint", 12L) });
                    Assert.IsTrue(written[0].Succeeded);
                    Assert.AreEqual((short)12, fake.LastWriteValue);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public async Task Gateway_RawReadsAreExplicitlyEnabled()
        {
            var directory = CreateDirectory();
            try
            {
                File.WriteAllText(Path.Combine(directory, "points.json"),
                    "{\"tags\":[{\"name\":\"Value\",\"address\":\"D0\",\"type\":\"Int16\"}]}");
                using (var host = CreateHost(directory, new GatewayFakeClient("device")))
                using (var gateway = new IndustrialTagGateway(host))
                {
                    var request = new TagGatewayRawReadItem("device", "D99", DataType.Int16);
                    Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.ReadAddressAsync(request));
                    gateway.Options.AllowRawAddressReads = true;
                    gateway.Options.ExposeRawAddresses = true;
                    var value = await gateway.ReadAddressAsync(request);
                    Assert.AreEqual("D99", value.Address);
                    Assert.AreEqual(QualityStatus.Good, value.Quality);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void DpapiSecretStore_RoundTripsRemovesAndRejectsCorruptFiles()
        {
            var directory = CreateDirectory();
            try
            {
                using (var store = new DpapiSecretStore(directory))
                {
                    store.Set("web.api-key", "sensitive-value");
                    Assert.AreEqual("sensitive-value", store.Get("web.api-key"));
                    Assert.IsTrue(store.TryGet("web.api-key", out var value));
                    Assert.AreEqual("sensitive-value", value);

                    var file = Directory.GetFiles(directory, "*.secret").Single();
                    File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
                    Assert.Throws<InvalidDataException>(() => store.Get("web.api-key"));
                    Assert.IsTrue(store.Remove("web.api-key"));
                    Assert.IsFalse(store.TryGet("web.api-key", out _));
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "IndustrialCommSdk.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static IndustrialDeviceHost CreateHost(string directory, IIndustrialClient client)
        {
            var config = new IndustrialSdkConfig
            {
                Devices = new List<IndustrialDeviceConfig>
                {
                    new IndustrialDeviceConfig
                    {
                        Name = "device",
                        DeviceId = "device",
                        Protocol = "modbus-tcp",
                        PointsFile = "points.json",
                        Enabled = true,
                        Runtime = new IndustrialDeviceRuntimeOptions(),
                    },
                },
            };
            return new IndustrialDeviceHost(config, directory, _ => client);
        }

        private sealed class GatewayFakeClient : IIndustrialClient
        {
            public GatewayFakeClient(string deviceId) { DeviceId = deviceId; }
            public string DeviceId { get; }
            public ProtocolKind Kind => ProtocolKind.ModbusTcp;
            public bool IsConnected { get; private set; }
            public int LastReadBatchSize { get; private set; }
            public object LastWriteValue { get; private set; }
            public Task ConnectAsync(CancellationToken cancellationToken) { IsConnected = true; return Task.CompletedTask; }
            public Task DisconnectAsync(CancellationToken cancellationToken) { IsConnected = false; return Task.CompletedTask; }
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(Good(request.Address));
            }
            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                LastReadBatchSize = requests.Count;
                return Task.FromResult(new BatchReadResult(requests.Select(request => Good(request.Address)).ToList()));
            }
            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                LastWriteValue = request.Value;
                return Task.CompletedTask;
            }
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
            {
                LastWriteValue = requests.Single().Value;
                return Task.CompletedTask;
            }
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                return Task.FromResult(request.SubscriptionKey);
            }
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken) { return Task.CompletedTask; }
            public HealthSnapshot GetHealth()
            {
                return new HealthSnapshot(IsConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected, null, 0, null);
            }
            public void Dispose() { }

            private static DataValue Good(string address)
            {
                return new DataValue(address, DataType.Int16, (short)7, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
            }
        }
    }
}
