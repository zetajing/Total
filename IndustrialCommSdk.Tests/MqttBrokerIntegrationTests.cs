using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Protocols.Mqtt;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MqttBrokerIntegrationTests
    {
        [Test]
        [Timeout(15000)]
        public async Task Broker_AuthenticatedClientReceivesInjectedMessage()
        {
            var port = GetFreeTcpPort();
            var brokerOptions = new MqttBrokerOptions
            {
                BindAddress = IPAddress.Loopback.ToString(),
                Port = port,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sdk-user"] = "sdk-password",
                },
            };

            using (var broker = new MqttBrokerService(brokerOptions))
            using (var client = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-integration",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "sdk-user",
                Password = "sdk-password",
                ConnectTimeoutMilliseconds = 3000,
                QualityOfService = 1,
            }))
            {
                var received = new TaskCompletionSource<MqttMessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.MessageReceived += (sender, args) =>
                {
                    if (args.Topic == "industrial/test/value") received.TrySetResult(args);
                };

                await broker.StartAsync(CancellationToken.None);
                await client.ConnectAsync(CancellationToken.None);
                await client.SubscribeTopicAsync("industrial/test/#", CancellationToken.None);
                await broker.PublishAsync("industrial/test/value", Encoding.UTF8.GetBytes("42"), 1, true, CancellationToken.None);

                var completed = await Task.WhenAny(received.Task, Task.Delay(5000));
                Assert.AreSame(received.Task, completed, "The MQTT client did not receive the broker-injected message.");
                var message = await received.Task;
                Assert.AreEqual("42", Encoding.UTF8.GetString(message.Payload));
                Assert.AreEqual(1, message.QualityOfService);
                Assert.IsFalse(message.Retain, "Live deliveries of a retained publication must clear the MQTT retain flag.");

                var clients = await broker.GetClientsAsync(CancellationToken.None);
                Assert.AreEqual(1, clients.Count);
                Assert.AreEqual("sdk-user", clients[0].Username);
                await client.DisconnectAsync(CancellationToken.None);
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        [Timeout(15000)]
        public async Task Broker_RejectsInvalidCredentials()
        {
            var port = GetFreeTcpPort();
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                Port = port,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sdk-user"] = "correct-password",
                },
            }))
            using (var client = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-auth-failure",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "sdk-user",
                Password = "wrong-password",
                ConnectTimeoutMilliseconds = 3000,
            }))
            {
                await broker.StartAsync(CancellationToken.None);
                try
                {
                    await client.ConnectAsync(CancellationToken.None);
                    Assert.Fail("The broker accepted invalid MQTT credentials.");
                }
                catch (IndustrialConnectionException)
                {
                    Assert.IsFalse(client.IsConnected);
                }
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        [Timeout(20000)]
        public async Task Client_AutoReconnectsAndRestoresSubscriptionsAfterBrokerRestart()
        {
            var port = GetFreeTcpPort();
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                Port = port,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sdk-user"] = "sdk-password",
                },
            }))
            using (var client = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-reconnect",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "sdk-user",
                Password = "sdk-password",
                AutoReconnect = true,
                ReconnectInitialDelayMilliseconds = 100,
                ReconnectMaxDelayMilliseconds = 500,
                ConnectTimeoutMilliseconds = 500,
                QualityOfService = 1,
            }))
            {
                await broker.StartAsync(CancellationToken.None);
                await client.ConnectAsync(CancellationToken.None);
                await client.SubscribeTopicAsync("industrial/reconnect/#", CancellationToken.None);

                var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var received = new TaskCompletionSource<MqttMessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.ConnectionChanged += (sender, args) =>
                {
                    if (args.IsConnected) reconnected.TrySetResult(true);
                };
                client.MessageReceived += (sender, args) =>
                {
                    if (args.Topic == "industrial/reconnect/value") received.TrySetResult(args);
                };

                await broker.StopAsync(CancellationToken.None);
                await Task.Delay(250);
                await broker.StartAsync(CancellationToken.None);

                var reconnectCompleted = await Task.WhenAny(reconnected.Task, Task.Delay(7000));
                Assert.AreSame(reconnected.Task, reconnectCompleted, "The MQTT client did not reconnect after the broker restarted.");
                await broker.PublishAsync("industrial/reconnect/value", Encoding.UTF8.GetBytes("restored"), 1, false, CancellationToken.None);
                var receiveCompleted = await Task.WhenAny(received.Task, Task.Delay(5000));
                Assert.AreSame(received.Task, receiveCompleted, "The MQTT subscription was not restored after reconnecting.");
                Assert.AreEqual("restored", Encoding.UTF8.GetString((await received.Task).Payload));

                await client.DisconnectAsync(CancellationToken.None);
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        public void Broker_NonLoopbackPlainTextEndpointIsRejected()
        {
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                BindAddress = IPAddress.Any.ToString(),
                Port = GetFreeTcpPort(),
                UseTls = false,
            }))
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await broker.StartAsync(CancellationToken.None));
            }
        }

        [Test]
        [Timeout(15000)]
        public async Task Broker_TopicAuthorizersRejectPublishAndSubscribe()
        {
            var port = GetFreeTcpPort();
            string publishIdentity = null;
            string subscribeIdentity = null;
            var deniedPublishWasProcessed = false;
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                Port = port,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["acl-user"] = "acl-password",
                },
                PublishAuthorizer = (username, clientId, topic) =>
                {
                    publishIdentity = username;
                    return !topic.StartsWith("denied/", StringComparison.Ordinal);
                },
                SubscribeAuthorizer = (username, clientId, topic) =>
                {
                    subscribeIdentity = username;
                    return !topic.StartsWith("denied/", StringComparison.Ordinal);
                },
            }))
            using (var client = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-acl",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "acl-user",
                Password = "acl-password",
                ConnectTimeoutMilliseconds = 3000,
                QualityOfService = 1,
            }))
            {
                broker.MessageReceived += (sender, args) =>
                {
                    if (args.Topic == "denied/value") deniedPublishWasProcessed = true;
                };
                await broker.StartAsync(CancellationToken.None);
                await client.ConnectAsync(CancellationToken.None);

                Assert.ThrowsAsync<IndustrialProtocolException>(async () =>
                    await client.SubscribeTopicAsync("denied/#", CancellationToken.None));
                // MQTT 3.1.1 PUBACK has no authorization reason code, so the client completes while the broker drops it.
                await client.WriteAsync(new WriteRequest("mqtt-acl", "denied/value", DataType.String, "blocked"), CancellationToken.None);
                Assert.AreEqual("acl-user", subscribeIdentity);
                Assert.AreEqual("acl-user", publishIdentity);
                Assert.IsFalse(deniedPublishWasProcessed);

                await client.DisconnectAsync(CancellationToken.None);
                await broker.StopAsync(CancellationToken.None);
            }
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }
    }
}
