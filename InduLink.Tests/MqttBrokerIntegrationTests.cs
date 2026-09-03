using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Exceptions;
using InduLink.Protocols.Mqtt;
using NUnit.Framework;

namespace InduLink.Tests
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

        [Test]
        [Timeout(15000)]
        public async Task Broker_OptionsAreFrozenAndReturnedAsCopies()
        {
            var configuredPort = GetFreeTcpPort();
            var mutatedPort = GetFreeTcpPort();
            var aclCalls = 0;
            var sourceOptions = new MqttBrokerOptions
            {
                Port = configuredPort,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["frozen-user"] = "frozen-password",
                },
                PublishAuthorizer = (username, clientId, topic) =>
                {
                    Interlocked.Increment(ref aclCalls);
                    return !topic.StartsWith("frozen/denied", StringComparison.Ordinal);
                },
            };

            using (var broker = new MqttBrokerService(sourceOptions))
            using (var authenticated = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-frozen-authenticated",
                Host = IPAddress.Loopback.ToString(),
                Port = configuredPort,
                Username = "frozen-user",
                Password = "frozen-password",
                QualityOfService = 1,
                ConnectTimeoutMilliseconds = 3000,
            }))
            using (var anonymous = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-frozen-anonymous",
                Host = IPAddress.Loopback.ToString(),
                Port = configuredPort,
                ConnectTimeoutMilliseconds = 3000,
            }))
            {
                sourceOptions.Port = mutatedPort;
                sourceOptions.AllowAnonymous = true;
                sourceOptions.Credentials.Clear();
                sourceOptions.PublishAuthorizer = null;
                var returnedOptions = broker.Options;
                returnedOptions.Port = mutatedPort;
                returnedOptions.AllowAnonymous = true;
                returnedOptions.Credentials.Clear();
                returnedOptions.PublishAuthorizer = null;

                Assert.AreEqual(configuredPort, broker.Options.Port);
                Assert.IsFalse(broker.Options.AllowAnonymous);
                Assert.AreEqual(1, broker.Options.Credentials.Count);
                Assert.IsNotNull(broker.Options.PublishAuthorizer);

                var deniedMessageWasProcessed = false;
                broker.MessageReceived += (sender, args) =>
                {
                    if (args.Topic == "frozen/denied") deniedMessageWasProcessed = true;
                };
                await broker.StartAsync(CancellationToken.None);
                await authenticated.ConnectAsync(CancellationToken.None);
                Assert.ThrowsAsync<IndustrialConnectionException>(async () =>
                    await anonymous.ConnectAsync(CancellationToken.None));
                await authenticated.WriteAsync(
                    new WriteRequest("mqtt-frozen-authenticated", "frozen/denied", DataType.String, "blocked"),
                    CancellationToken.None);
                Assert.Greater(aclCalls, 0);
                Assert.IsFalse(deniedMessageWasProcessed);

                await authenticated.DisconnectAsync(CancellationToken.None);
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        [Timeout(15000)]
        public async Task Broker_RejectsApplicationMessagesAbovePayloadLimitBeforeRaisingEvent()
        {
            var port = GetFreeTcpPort();
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                Port = port,
                MaxApplicationMessagePayloadBytes = 4,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["limited-user"] = "limited-password",
                },
            }))
            using (var client = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-payload-limit",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "limited-user",
                Password = "limited-password",
                QualityOfService = 1,
                ConnectTimeoutMilliseconds = 3000,
            }))
            {
                var messageWasProcessed = false;
                broker.MessageReceived += (sender, args) => messageWasProcessed = true;
                await broker.StartAsync(CancellationToken.None);
                await client.ConnectAsync(CancellationToken.None);
                try
                {
                    await client.WriteAsync(
                        new WriteRequest("mqtt-payload-limit", "payload/too-large", DataType.String, "12345"),
                        CancellationToken.None);
                }
                catch (Exception)
                {
                    // MQTT 3.1.1 cannot carry the v5 rejection reason; the broker may close the connection instead.
                }
                await Task.Delay(100);
                Assert.IsFalse(messageWasProcessed);
                Assert.ThrowsAsync<ArgumentException>(async () => await broker.PublishAsync(
                    "payload/internal-too-large", new byte[5], 1, false, CancellationToken.None));
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        [Timeout(15000)]
        public async Task Client_RejectsOversizedOutboundMessagesBeforePublish()
        {
            var port = GetFreeTcpPort();
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                Port = port,
                MaxApplicationMessagePayloadBytes = 1024,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["outbound-limit-user"] = "outbound-limit-password",
                },
            }))
            using (var client = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-outbound-limit",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "outbound-limit-user",
                Password = "outbound-limit-password",
                MaxApplicationMessagePayloadBytes = 4,
                ConnectTimeoutMilliseconds = 3000,
            }))
            {
                var messageWasProcessed = false;
                broker.MessageReceived += (sender, args) => messageWasProcessed = true;
                await broker.StartAsync(CancellationToken.None);
                await client.ConnectAsync(CancellationToken.None);

                Assert.ThrowsAsync<ArgumentException>(async () => await client.WriteAsync(
                    new WriteRequest("mqtt-outbound-limit", "payload/outbound-too-large", DataType.String, "12345"),
                    CancellationToken.None));
                await Task.Delay(100);
                Assert.IsFalse(messageWasProcessed);

                await client.DisconnectAsync(CancellationToken.None);
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        [Timeout(15000)]
        public async Task Client_OptionsAndWillPayloadAreFrozenAtConstruction()
        {
            var port = GetFreeTcpPort();
            var willPayload = Encoding.UTF8.GetBytes("original-will");
            var sourceOptions = new MqttClientOptions
            {
                DeviceId = "mqtt-client-options-frozen",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "frozen-client",
                Password = "frozen-password",
                WillTopic = "client/frozen/will",
                WillPayload = willPayload,
                ConnectTimeoutMilliseconds = 3000,
            };
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                Port = port,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["frozen-client"] = "frozen-password",
                },
            }))
            using (var client = new MqttClient(sourceOptions))
            {
                sourceOptions.Port = GetFreeTcpPort();
                sourceOptions.Password = "mutated-password";
                sourceOptions.WillPayload[0] = (byte)'X';
                sourceOptions.WillPayload = new byte[] { 0 };

                var optionsField = typeof(MqttClient).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(optionsField);
                var frozenOptions = (MqttClientOptions)optionsField.GetValue(client);
                Assert.AreNotSame(willPayload, frozenOptions.WillPayload);
                Assert.AreEqual("original-will", Encoding.UTF8.GetString(frozenOptions.WillPayload));

                await broker.StartAsync(CancellationToken.None);
                await client.ConnectAsync(CancellationToken.None);
                Assert.IsTrue(client.IsConnected);
                await client.DisconnectAsync(CancellationToken.None);
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        [Timeout(15000)]
        public async Task Client_DropsOversizedMessagesAndEvictsOldCachedTopics()
        {
            var port = GetFreeTcpPort();
            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                Port = port,
                Credentials = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["limited-client"] = "limited-password",
                },
            }))
            using (var client = new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-client-limits",
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                Username = "limited-client",
                Password = "limited-password",
                MaxApplicationMessagePayloadBytes = 4,
                MaxCachedTopics = 1,
                ConnectTimeoutMilliseconds = 3000,
            }))
            {
                var topics = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
                var validMessagesReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.MessageReceived += (sender, args) =>
                {
                    topics[args.Topic] = true;
                    if (topics.ContainsKey("client-limit/one") && topics.ContainsKey("client-limit/two"))
                        validMessagesReceived.TrySetResult(true);
                };

                await broker.StartAsync(CancellationToken.None);
                await client.ConnectAsync(CancellationToken.None);
                await client.SubscribeTopicAsync("client-limit/#", CancellationToken.None);
                await broker.PublishAsync("client-limit/oversized", Encoding.UTF8.GetBytes("12345"), 0, false, CancellationToken.None);
                await broker.PublishAsync("client-limit/one", Encoding.UTF8.GetBytes("one"), 0, false, CancellationToken.None);
                await broker.PublishAsync("client-limit/two", Encoding.UTF8.GetBytes("two"), 0, false, CancellationToken.None);
                Assert.AreSame(validMessagesReceived.Task, await Task.WhenAny(validMessagesReceived.Task, Task.Delay(5000)));
                Assert.IsFalse(topics.ContainsKey("client-limit/oversized"));

                var newest = await client.ReadAsync(new ReadRequest(
                    "mqtt-client-limits", "client-limit/two", DataType.String, 1, TimeSpan.FromMilliseconds(200)),
                    CancellationToken.None);
                Assert.AreEqual(QualityStatus.Good, newest.Quality);
                Assert.AreEqual("two", newest.Value);
                var evicted = await client.ReadAsync(new ReadRequest(
                    "mqtt-client-limits", "client-limit/one", DataType.String, 1, TimeSpan.FromMilliseconds(150)),
                    CancellationToken.None);
                Assert.AreEqual(QualityStatus.Bad, evicted.Quality);

                await client.DisconnectAsync(CancellationToken.None);
                await broker.StopAsync(CancellationToken.None);
            }
        }

        [Test]
        public void TlsOptionsRejectLegacySslProtocols()
        {
            Assert.Throws<ArgumentException>(() => new MqttClient(new MqttClientOptions
            {
                DeviceId = "mqtt-legacy-tls",
                Host = "localhost",
                UseTls = true,
                TlsProtocols = SslProtocols.Ssl3,
            }));

            using (var broker = new MqttBrokerService(new MqttBrokerOptions
            {
                UseTls = true,
                TlsProtocols = SslProtocols.Ssl3,
            }))
            {
                Assert.ThrowsAsync<ArgumentException>(async () => await broker.StartAsync(CancellationToken.None));
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
