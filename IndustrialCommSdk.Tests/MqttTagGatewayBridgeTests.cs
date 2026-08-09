using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MqttTagGatewayBridgeTests
    {
        [Test]
        public async Task BridgePublishesSnapshotChangesAndCommandResponses()
        {
            var broker = new FakeBroker();
            var gateway = new FakeTagGateway();
            using (var bridge = new MqttTagGatewayBridge(broker, gateway, new MqttTagGatewayOptions
            {
                HeartbeatInterval = TimeSpan.FromHours(1),
            }))
            {
                await bridge.StartAsync(CancellationToken.None);
                Assert.That(broker.Publications.Any(item => item.Topic == "industrial/v1/devices/plc/tags/Value" && item.Retain), Is.True);

                gateway.RaiseValue(8);
                await WaitUntilAsync(() => broker.Publications.Count(item => item.Topic.EndsWith("/tags/Value", StringComparison.Ordinal)) >= 2);
                var afterFirstChange = broker.Publications.Count;
                gateway.RaiseValue(8);
                await Task.Delay(100);
                Assert.AreEqual(afterFirstChange, broker.Publications.Count, "Unchanged values must not be published twice.");

                broker.RaiseMessage(
                    "client-one",
                    "industrial/v1/requests/client-one/read",
                    "{\"correlationId\":\"read-1\",\"items\":[{\"device\":\"plc\",\"tag\":\"Value\"}]}");
                await WaitUntilAsync(() => broker.Publications.Any(item => item.Topic == "industrial/v1/responses/client-one/read-1"));
                var response = broker.Publications.Last(item => item.Topic == "industrial/v1/responses/client-one/read-1");
                Assert.That(Encoding.UTF8.GetString(response.Payload), Does.Contain("readResult"));

                await bridge.StopAsync(CancellationToken.None);
                Assert.IsFalse(bridge.IsRunning);
            }
        }

        [Test]
        public void TopicAclRestrictsCommandsAndResponsesToTheSameClient()
        {
            Assert.IsTrue(MqttTagGatewayBridge.IsClientPublishAllowed("industrial/v1", "client one",
                "industrial/v1/requests/client%20one/read"));
            Assert.IsFalse(MqttTagGatewayBridge.IsClientPublishAllowed("industrial/v1", "client one",
                "industrial/v1/requests/other/write"));
            Assert.IsTrue(MqttTagGatewayBridge.IsClientSubscriptionAllowed("industrial/v1", "client one",
                "industrial/v1/responses/client%20one/#"));
            Assert.IsTrue(MqttTagGatewayBridge.IsClientSubscriptionAllowed("industrial/v1", "client one",
                "industrial/v1/devices/#"));
            Assert.IsFalse(MqttTagGatewayBridge.IsClientSubscriptionAllowed("industrial/v1", "client one",
                "industrial/v1/responses/other/#"));
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            var timeout = DateTime.UtcNow.AddSeconds(3);
            while (!predicate() && DateTime.UtcNow < timeout) await Task.Delay(20);
            Assert.IsTrue(predicate(), "Timed out waiting for the asynchronous bridge operation.");
        }

        private sealed class FakeBroker : IMqttBrokerService
        {
            public MqttBrokerOptions Options { get; } = new MqttBrokerOptions();
            public bool IsRunning { get; private set; } = true;
            public ConcurrentBag<Publication> Publications { get; } = new ConcurrentBag<Publication>();
            public event EventHandler Started;
            public event EventHandler Stopped;
            public event EventHandler<MqttBrokerClientEventArgs> ClientConnected;
            public event EventHandler<MqttBrokerClientEventArgs> ClientDisconnected;
            public event EventHandler<MqttBrokerMessageReceivedEventArgs> MessageReceived;
            public Task StartAsync(CancellationToken cancellationToken) { IsRunning = true; Started?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
            public Task StopAsync(CancellationToken cancellationToken) { IsRunning = false; Stopped?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
            public Task PublishAsync(string topic, byte[] payload, int qualityOfService, bool retain, CancellationToken cancellationToken)
            {
                Publications.Add(new Publication(topic, payload, retain));
                return Task.CompletedTask;
            }
            public Task<IReadOnlyList<MqttBrokerClientSession>> GetClientsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult((IReadOnlyList<MqttBrokerClientSession>)new MqttBrokerClientSession[0]);
            }
            public void RaiseMessage(string clientId, string topic, string payload)
            {
                MessageReceived?.Invoke(this, new MqttBrokerMessageReceivedEventArgs(clientId, topic, Encoding.UTF8.GetBytes(payload), 1, false));
            }
            public void Dispose() { }
#pragma warning disable CS0067
            private void KeepCompilerHappy()
            {
                ClientConnected?.Invoke(this, null);
                ClientDisconnected?.Invoke(this, null);
            }
#pragma warning restore CS0067
        }

        private sealed class Publication
        {
            public Publication(string topic, byte[] payload, bool retain)
            {
                Topic = topic;
                Payload = payload;
                Retain = retain;
            }
            public string Topic { get; }
            public byte[] Payload { get; }
            public bool Retain { get; }
        }

        private sealed class FakeTagGateway : IIndustrialTagGateway
        {
            public IndustrialTagGatewayOptions Options { get; } = new IndustrialTagGatewayOptions();
            public IReadOnlyList<TagGatewayDevice> Devices { get; } = new[]
            {
                new TagGatewayDevice("plc", ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null),
            };
            public event EventHandler<TagGatewayValuesChangedEventArgs> ValuesChanged;
            public event EventHandler<TagGatewayDeviceStateChangedEventArgs> DeviceStateChanged;
            public IReadOnlyList<TagGatewayTag> GetTags(string deviceName)
            {
                return new[] { new TagGatewayTag("Value", DataType.Int16, 1, false, null) };
            }
            public Task<IReadOnlyList<TagGatewayValue>> ReadAsync(IReadOnlyCollection<TagGatewayReadItem> items, CancellationToken cancellationToken = default)
            {
                return Task.FromResult((IReadOnlyList<TagGatewayValue>)items.Select(item => Value(7)).ToList());
            }
            public Task<IReadOnlyList<TagGatewayWriteResult>> WriteAsync(IReadOnlyCollection<TagGatewayWriteItem> items, CancellationToken cancellationToken = default)
            {
                return Task.FromResult((IReadOnlyList<TagGatewayWriteResult>)items.Select(item =>
                    TagGatewayWriteResult.Failure(item.DeviceName, item.TagName, "disabled")).ToList());
            }
            public Task<TagGatewayValue> ReadAddressAsync(TagGatewayRawReadItem item, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
            public void RaiseValue(short value)
            {
                ValuesChanged?.Invoke(this, new TagGatewayValuesChangedEventArgs(new[] { Value(value) }, DateTimeOffset.UtcNow));
            }
            private static TagGatewayValue Value(short value)
            {
                return new TagGatewayValue("plc", "Value", DataType.Int16, value, QualityStatus.Good, DateTimeOffset.UtcNow, null, null);
            }
#pragma warning disable CS0067
            private void KeepCompilerHappy()
            {
                DeviceStateChanged?.Invoke(this, null);
            }
#pragma warning restore CS0067
        }
    }
}
