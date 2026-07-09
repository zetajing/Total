using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Modbus;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class IndustrialDeploymentTests
    {
        [Test]
        public void Open_Should_Bind_Configured_Client_And_PointTable()
        {
            var directory = CreateTempDirectory();
            try
            {
                var pointsDirectory = Path.Combine(directory, "points");
                Directory.CreateDirectory(pointsDirectory);
                File.WriteAllText(Path.Combine(directory, "devices.json"), @"{ ""devices"": [{ ""name"": ""plc1"", ""protocol"": ""modbus-tcp"", ""host"": ""127.0.0.1"", ""deviceProfile"": ""generic"", ""pointsFile"": ""points/plc1.json"" }] }");
                File.WriteAllText(Path.Combine(pointsDirectory, "plc1.json"), @"{ ""tags"": [{ ""name"": ""Speed"", ""address"": ""HR0"", ""type"": ""Int16"" }] }");

                using (var device = IndustrialDeployment.Open(Path.Combine(directory, "devices.json"), "plc1"))
                {
                    Assert.That(device.DeviceName, Is.EqualTo("plc1"));
                    Assert.That(device.Client, Is.InstanceOf<ModbusTcpClient>());
                    Assert.That(device.Tags.Get("Speed").Address, Is.EqualTo("HR0"));
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public async Task ConfiguredClient_Should_Read_And_Write_By_TagName()
        {
            var client = new RecordingClient();
            var tags = TagTable.FromJson(@"{ ""tags"": [
                { ""name"": ""Speed"", ""address"": ""D100"", ""type"": ""Int16"" },
                { ""name"": ""Running"", ""address"": ""M10"", ""type"": ""Bool"" }
            ] }");

            using (var device = new IndustrialConfiguredClient("plc1", client, tags))
            {
                var speed = await device.ReadAsync<short>("Speed");
                await device.WriteAsync("Running", true);
                await device.WriteManyAsync(new Dictionary<string, object>
                {
                    ["Speed"] = (short)120,
                    ["Running"] = false,
                });
                await device.ReadManyAsync();

                Assert.That(speed, Is.EqualTo(123));
                Assert.That(client.LastReadRequest.Address, Is.EqualTo("D100"));
                Assert.That(client.LastReadRequest.DataType, Is.EqualTo(DataType.Int16));
                Assert.That(client.LastWriteRequest.Address, Is.EqualTo("M10"));
                Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.Bool));
                Assert.That(client.LastWriteManyRequests.Count, Is.EqualTo(2));
                Assert.That(client.LastWriteManyRequests[0].Address, Is.EqualTo("D100"));
                Assert.That(client.LastReadManyRequests.Count, Is.EqualTo(2));
            }
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "IndustrialCommSdkTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private sealed class RecordingClient : IIndustrialClient
        {
            public string DeviceId { get { return "recording"; } }
            public ProtocolKind Kind { get { return ProtocolKind.ModbusTcp; } }
            public bool IsConnected { get; private set; }
            public ReadRequest LastReadRequest { get; private set; }
            public IReadOnlyList<ReadRequest> LastReadManyRequests { get; private set; }
            public WriteRequest LastWriteRequest { get; private set; }
            public IReadOnlyList<WriteRequest> LastWriteManyRequests { get; private set; }

            public Task ConnectAsync(CancellationToken cancellationToken)
            {
                IsConnected = true;
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                LastReadRequest = request;
                return Task.FromResult(new DataValue(request.Address, request.DataType, (short)123, null, QualityStatus.Good, DateTimeOffset.UtcNow, null));
            }

            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                LastReadManyRequests = new List<ReadRequest>(requests);
                var values = new List<DataValue>();
                foreach (var request in requests)
                {
                    values.Add(new DataValue(request.Address, request.DataType, request.DataType == DataType.Bool ? (object)true : (short)123, null, QualityStatus.Good, DateTimeOffset.UtcNow, null));
                }

                return Task.FromResult(new BatchReadResult(values));
            }

            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                LastWriteRequest = request;
                return Task.CompletedTask;
            }

            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
            {
                LastWriteManyRequests = new List<WriteRequest>(requests);
                return Task.CompletedTask;
            }

            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                return Task.FromResult("subscription");
            }

            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public HealthSnapshot GetHealth()
            {
                return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null);
            }

            public void Dispose()
            {
                IsConnected = false;
            }
        }
    }
}
