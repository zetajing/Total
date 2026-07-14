using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Mes;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MesJsonReceiverTests
    {
        [Test]
        public async Task ReceivesRawJsonAndReturnsHandlerJson()
        {
            const string json = "{\r\n  \"serial\": \"A001\", \"value\": 7\r\n}";
            MesJsonReceiveRequest received = null;
            var options = CreateOptions();
            using (var receiver = new MesJsonReceiver(options, (request, token) =>
            {
                received = request;
                return Task.FromResult(new MesJsonReceiveResponse
                {
                    StatusCode = 202,
                    Json = "{\"accepted\":true}",
                });
            }))
            {
                await receiver.StartAsync(CancellationToken.None);
                using (var client = new HttpClient())
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var response = await client.PostAsync(options.ListenPrefix + "orders?line=1", content))
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
                    Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("{\"accepted\":true}"));
                    Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
                }
                await receiver.StopAsync(CancellationToken.None);
            }

            Assert.That(received, Is.Not.Null);
            Assert.That(received.Endpoint, Is.EqualTo("/orders?line=1"));
            Assert.That(received.Body, Is.EqualTo(json));
        }

        [Test]
        public async Task InvalidJsonObjectIsRejectedBeforeHandler()
        {
            var handlerCalls = 0;
            var options = CreateOptions();
            using (var receiver = new MesJsonReceiver(options, (request, token) =>
            {
                handlerCalls++;
                return Task.FromResult(new MesJsonReceiveResponse());
            }))
            {
                await receiver.StartAsync(CancellationToken.None);
                using (var client = new HttpClient())
                using (var content = new StringContent("[]", Encoding.UTF8, "application/json"))
                using (var response = await client.PostAsync(options.ListenPrefix + "invalid", content))
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            }
            Assert.That(handlerCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task OversizedJsonIsRejectedBeforeHandler()
        {
            var handlerCalls = 0;
            var options = CreateOptions();
            options.MaxRequestContentBytes = 8;
            using (var receiver = new MesJsonReceiver(options, (request, token) =>
            {
                handlerCalls++;
                return Task.FromResult(new MesJsonReceiveResponse());
            }))
            {
                await receiver.StartAsync(CancellationToken.None);
                using (var client = new HttpClient())
                using (var content = new StringContent("{\"value\":12345}", Encoding.UTF8, "application/json"))
                using (var response = await client.PostAsync(options.ListenPrefix + "large", content))
                    Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)413));
            }
            Assert.That(handlerCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task OptionalAuthorizationIsEnforced()
        {
            var handlerCalls = 0;
            var options = CreateOptions();
            options.RequiredAuthorizationHeaderValue = "Bearer test-token";
            using (var receiver = new MesJsonReceiver(options, (request, token) =>
            {
                handlerCalls++;
                return Task.FromResult(new MesJsonReceiveResponse());
            }))
            {
                await receiver.StartAsync(CancellationToken.None);
                using (var client = new HttpClient())
                {
                    using (var unauthorizedContent = new StringContent("{}", Encoding.UTF8, "application/json"))
                    using (var unauthorized = await client.PostAsync(options.ListenPrefix + "secure", unauthorizedContent))
                        Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
                    using (var authorizedContent = new StringContent("{}", Encoding.UTF8, "application/json"))
                    using (var authorized = await client.PostAsync(options.ListenPrefix + "secure", authorizedContent))
                        Assert.That(authorized.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                }
            }
            Assert.That(handlerCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task HandlerDeadlineReturnsGatewayTimeoutEvenWhenHandlerIgnoresCancellation()
        {
            var neverCompletes = new TaskCompletionSource<MesJsonReceiveResponse>();
            var options = CreateOptions();
            options.HandlerTimeoutMilliseconds = 20;
            using (var receiver = new MesJsonReceiver(options, (request, token) => neverCompletes.Task))
            {
                await receiver.StartAsync(CancellationToken.None);
                using (var client = new HttpClient())
                using (var content = new StringContent("{}", Encoding.UTF8, "application/json"))
                using (var response = await client.PostAsync(options.ListenPrefix + "slow", content))
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
                    Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("handler_timeout"));
                }
            }
        }

        private static MesJsonReceiverOptions CreateOptions()
        {
            return new MesJsonReceiverOptions
            {
                ListenPrefix = "http://127.0.0.1:" + GetFreePort() + "/mes/",
                HandlerTimeoutMilliseconds = 2000,
            };
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }
    }
}
