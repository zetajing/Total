using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Runtime;
using InduLink.Web.Gateway;
using InduLink.Web.Http;
using InduLink.Web.WebSockets;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class WebGatewayTests
    {
        [Test]
        public async Task WebApi_RequiresApiKey_AndReadsConfiguredTag()
        {
            var port = ReserveTcpPort();
            var fake = new FakeTagGateway();
            var options = CreateOptions(port);
            using (var gateway = new IndustrialWebGateway(fake, options))
            using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }))
            {
                await gateway.StartAsync(CancellationToken.None);
                try
                {
                    var unauthorized = await http.GetAsync(options.ListenPrefix + "api/v1/devices");
                    Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

                    using (var request = new HttpRequestMessage(HttpMethod.Post, options.ListenPrefix + "api/v1/read"))
                    {
                        request.Headers.TryAddWithoutValidation("X-Industrial-Api-Key", options.ApiKey);
                        request.Content = new StringContent(
                            "{\"correlationId\":\"test-1\",\"items\":[{\"device\":\"plc-1\",\"tag\":\"temperature\"}]}",
                            Encoding.UTF8,
                            "application/json");
                        var response = await http.SendAsync(request);
                        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
                        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                        Assert.That((string)body["correlationId"], Is.EqualTo("test-1"));
                        Assert.That((double)body["items"][0]["value"], Is.EqualTo(42.5d));
                        Assert.That((string)body["items"][0]["quality"], Is.EqualTo("good"));
                    }

                    using (var write = new HttpRequestMessage(HttpMethod.Post, options.ListenPrefix + "api/v1/write"))
                    {
                        write.Headers.TryAddWithoutValidation("X-Industrial-Api-Key", options.ApiKey);
                        write.Content = new StringContent(
                            "{\"items\":[{\"device\":\"plc-1\",\"tag\":\"temperature\",\"value\":99}]}",
                            Encoding.UTF8,
                            "application/json");
                        Assert.That((await http.SendAsync(write)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                    }

                    using (var rawRead = new HttpRequestMessage(HttpMethod.Post, options.ListenPrefix + "api/v1/read-address"))
                    {
                        rawRead.Headers.TryAddWithoutValidation("X-Industrial-Api-Key", options.ApiKey);
                        rawRead.Content = new StringContent(
                            "{\"items\":[{\"device\":\"plc-1\",\"address\":\"DB1.DBD0\",\"dataType\":\"double\",\"length\":1}]}",
                            Encoding.UTF8,
                            "application/json");
                        Assert.That((await http.SendAsync(rawRead)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                    }
                }
                finally { await gateway.StopAsync(CancellationToken.None); }
            }
        }

        [Test]
        public async Task WebSocket_SubscribeSendsSnapshot_ThenOnlyChangedValues()
        {
            var port = ReserveTcpPort();
            var fake = new FakeTagGateway();
            var options = CreateOptions(port);
            options.HeartbeatInterval = TimeSpan.FromSeconds(30);
            using (var gateway = new IndustrialWebGateway(fake, options))
            using (var client = new IndustrialWebSocketClient(new IndustrialWebSocketClientOptions
            {
                Uri = new Uri("ws://127.0.0.1:" + port + "/ws/v1/tags"),
                ApiKey = options.ApiKey,
                AutoReconnect = false,
            }))
            {
                var snapshot = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
                var change = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.MessageReceived += (sender, args) =>
                {
                    var message = JObject.Parse(args.Text);
                    if ((string)message["type"] == "snapshot") snapshot.TrySetResult(message);
                    if ((string)message["type"] == "change") change.TrySetResult(message);
                };

                await gateway.StartAsync(CancellationToken.None);
                try
                {
                    await client.ConnectAsync(CancellationToken.None);
                    await client.SendTextAsync(
                        "{\"type\":\"subscribe\",\"correlationId\":\"sub-1\",\"items\":[{\"device\":\"plc-1\",\"tag\":\"temperature\"}]}",
                        CancellationToken.None);

                    var first = await WithTimeout(snapshot.Task, TimeSpan.FromSeconds(5));
                    Assert.That((double)first["items"][0]["value"], Is.EqualTo(42.5d));

                    fake.PublishValue(42.5d);
                    await Task.Delay(200);
                    Assert.That(change.Task.IsCompleted, Is.False, "An unchanged value must not be pushed.");

                    fake.PublishValue(43.0d);
                    var changed = await WithTimeout(change.Task, TimeSpan.FromSeconds(5));
                    Assert.That((double)changed["items"][0]["value"], Is.EqualTo(43.0d));
                }
                finally
                {
                    await client.CloseAsync(CancellationToken.None);
                    await gateway.StopAsync(CancellationToken.None);
                }
            }
        }

        [Test]
        public void HttpApiClient_RejectsResponseAboveConfiguredLimit()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[32]),
            };
            using (var http = new HttpClient(new StubHttpHandler(response)))
            using (var client = new HttpApiClient(http, new HttpApiClientOptions { MaxResponseContentBytes = 8 }))
            {
                Assert.ThrowsAsync<HttpResponseTooLargeException>(() => client.SendAsync(
                    new HttpApiRequest(HttpMethod.Get, new Uri("http://127.0.0.1/test")),
                    CancellationToken.None));
            }
        }

        [Test]
        public void Gateway_RejectsInsecureNonLoopbackListener()
        {
            var options = new IndustrialWebGatewayOptions
            {
                ListenPrefix = "http://192.0.2.10:8088/",
                ApiKey = "test-key",
            };
            options.AllowedOrigins.Add("https://example.test");
            Assert.Throws<ArgumentException>(() => new IndustrialWebGateway(new FakeTagGateway(), options));
        }

        [Test]
        public void Gateway_RequiresApiKeyEvenOnLoopback()
        {
            var options = new IndustrialWebGatewayOptions
            {
                ListenPrefix = "http://127.0.0.1:8088/",
                RequireApiKey = false,
            };
            options.AllowedOrigins.Add("https://trusted.example");

            Assert.Throws<ArgumentException>(() => new IndustrialWebGateway(new FakeTagGateway(), options));
        }

        [Test]
        public async Task StandaloneWebSocketServer_ReceivesAndBroadcastsMessages()
        {
            var port = ReserveTcpPort();
            var serverOptions = new IndustrialWebSocketServerOptions
            {
                ListenPrefix = "http://127.0.0.1:" + port + "/",
                WebSocketPath = "/ws/",
                ApiKey = "server-test-key",
                ShutdownTimeout = TimeSpan.FromSeconds(2),
            };
            using (var server = new IndustrialWebSocketServer(serverOptions))
            using (var client = new IndustrialWebSocketClient(new IndustrialWebSocketClientOptions
            {
                Uri = new Uri("ws://127.0.0.1:" + port + "/ws/"),
                ApiKey = serverOptions.ApiKey,
                AutoReconnect = false,
            }))
            {
                var receivedByServer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var receivedByClient = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                server.MessageReceived += (sender, args) => receivedByServer.TrySetResult(args.Text);
                client.MessageReceived += (sender, args) =>
                {
                    if (args.MessageType == System.Net.WebSockets.WebSocketMessageType.Binary)
                        receivedByClient.TrySetResult(args.Payload);
                };

                await server.StartAsync(CancellationToken.None);
                try
                {
                    await client.ConnectAsync(CancellationToken.None);
                    await client.SendTextAsync("hello-server", CancellationToken.None);
                    Assert.That(await WithTimeout(receivedByServer.Task, TimeSpan.FromSeconds(5)), Is.EqualTo("hello-server"));

                    await server.BroadcastBinaryAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
                    CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await WithTimeout(receivedByClient.Task, TimeSpan.FromSeconds(5)));
                }
                finally
                {
                    await client.CloseAsync(CancellationToken.None);
                    await server.StopAsync(CancellationToken.None);
                }
            }
        }

        [Test]
        public async Task StandaloneWebSocketServer_OriginOnlyModeRejectsMissingOrigin()
        {
            var port = ReserveTcpPort();
            var serverOptions = new IndustrialWebSocketServerOptions
            {
                ListenPrefix = "http://127.0.0.1:" + port + "/",
                WebSocketPath = "/ws/",
                RequireApiKey = false,
                ShutdownTimeout = TimeSpan.FromSeconds(2),
            };
            serverOptions.AllowedOrigins.Add("https://trusted.example");

            using (var server = new IndustrialWebSocketServer(serverOptions))
            using (var missingOriginClient = new IndustrialWebSocketClient(new IndustrialWebSocketClientOptions
            {
                Uri = new Uri("ws://127.0.0.1:" + port + "/ws/"),
                AutoReconnect = false,
            }))
            using (var allowedOriginClient = new IndustrialWebSocketClient(new IndustrialWebSocketClientOptions
            {
                Uri = new Uri("ws://127.0.0.1:" + port + "/ws/"),
                Origin = "https://trusted.example",
                AutoReconnect = false,
            }))
            {
                await server.StartAsync(CancellationToken.None);
                try
                {
                    Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(
                        () => missingOriginClient.ConnectAsync(CancellationToken.None));

                    await allowedOriginClient.ConnectAsync(CancellationToken.None);
                    Assert.That(allowedOriginClient.IsConnected, Is.True);
                }
                finally
                {
                    await allowedOriginClient.CloseAsync(CancellationToken.None);
                    await server.StopAsync(CancellationToken.None);
                }
            }
        }

        private static IndustrialWebGatewayOptions CreateOptions(int port)
        {
            return new IndustrialWebGatewayOptions
            {
                ListenPrefix = "http://127.0.0.1:" + port + "/",
                ApiKey = "unit-test-api-key",
                ShutdownTimeout = TimeSpan.FromSeconds(2),
            };
        }

        private static int ReserveTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("The expected asynchronous result was not received.");
            return await task;
        }

        private sealed class StubHttpHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;
            internal StubHttpHandler(HttpResponseMessage response) { _response = response; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_response);
            }
        }

        private sealed class FakeTagGateway : IIndustrialTagGateway
        {
            private double _value = 42.5d;

            internal FakeTagGateway()
            {
                Options = new IndustrialTagGatewayOptions();
                Devices = new[]
                {
                    new TagGatewayDevice("plc-1", ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null),
                };
            }

            public IndustrialTagGatewayOptions Options { get; private set; }
            public IReadOnlyList<TagGatewayDevice> Devices { get; private set; }
            public event EventHandler<TagGatewayValuesChangedEventArgs> ValuesChanged;
            public event EventHandler<TagGatewayDeviceStateChangedEventArgs> DeviceStateChanged
            {
                add { }
                remove { }
            }

            public IReadOnlyList<TagGatewayTag> GetTags(string deviceName)
            {
                if (!string.Equals(deviceName, "plc-1", StringComparison.OrdinalIgnoreCase))
                    throw new KeyNotFoundException("Device was not found: " + deviceName);
                return new[] { new TagGatewayTag("temperature", DataType.Double, 1, false, null) };
            }

            public Task<IReadOnlyList<TagGatewayValue>> ReadAsync(IReadOnlyCollection<TagGatewayReadItem> items, CancellationToken cancellationToken = default(CancellationToken))
            {
                IReadOnlyList<TagGatewayValue> values = items.Select(item => CreateValue(_value)).ToList();
                return Task.FromResult(values);
            }

            public Task<IReadOnlyList<TagGatewayWriteResult>> WriteAsync(IReadOnlyCollection<TagGatewayWriteItem> items, CancellationToken cancellationToken = default(CancellationToken))
            {
                IReadOnlyList<TagGatewayWriteResult> results = items.Select(item => TagGatewayWriteResult.Failure(item.DeviceName, item.TagName, "Read-only test tag.")).ToList();
                return Task.FromResult(results);
            }

            public Task<TagGatewayValue> ReadAddressAsync(TagGatewayRawReadItem item, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(CreateValue(_value));
            }

            internal void PublishValue(double value)
            {
                _value = value;
                ValuesChanged?.Invoke(this, new TagGatewayValuesChangedEventArgs(new[] { CreateValue(value) }, DateTimeOffset.UtcNow));
            }

            private static TagGatewayValue CreateValue(double value)
            {
                return new TagGatewayValue("plc-1", "temperature", DataType.Double, value, QualityStatus.Good, DateTimeOffset.UtcNow, null, null);
            }
        }
    }
}
