using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Mes;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MesHttpClientTests
    {
        [Test]
        public void ServerErrorsRetryOnlyUpToConfiguredLimit()
        {
            var handler = new QueueHandler(
                Response(HttpStatusCode.InternalServerError, "error-1"),
                Response(HttpStatusCode.BadGateway, "error-2"),
                Response(HttpStatusCode.ServiceUnavailable, "error-3"));
            var options = CreateOptions();
            options.MaxRetries = 2;

            using (var client = new MesHttpClient(options, handler, true))
            {
                Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await client.SendOnlineAsync(CancellationToken.None));
            }

            Assert.That(handler.RequestCount, Is.EqualTo(3));
            Assert.That(handler.IsDisposed, Is.True);
        }

        [Test]
        public async Task ServerErrorCanRecoverOnRetry()
        {
            var handler = new QueueHandler(
                Response(HttpStatusCode.ServiceUnavailable, "temporary"),
                Response(HttpStatusCode.OK, "{\"code\":\"0\",\"message\":\"ok\"}"));
            var options = CreateOptions();
            options.MaxRetries = 1;

            MesOnlineResponse result;
            using (var client = new MesHttpClient(options, handler, false))
            {
                result = await client.SendOnlineAsync(CancellationToken.None);
                Assert.That(client.IsConnected, Is.True);
            }

            Assert.That(result.Code, Is.EqualTo("0"));
            Assert.That(handler.RequestCount, Is.EqualTo(2));
            Assert.That(handler.IsDisposed, Is.False);
            handler.Dispose();
        }

        [Test]
        public async Task ClientErrorReturnsWithoutRetry()
        {
            var handler = new QueueHandler(
                Response(HttpStatusCode.BadRequest, "{\"code\":\"INVALID\",\"message\":\"bad request\"}"));
            var options = CreateOptions();
            options.MaxRetries = 3;

            MesOnlineResponse result;
            using (var client = new MesHttpClient(options, handler, true))
                result = await client.SendOnlineAsync(CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo("INVALID"));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExternalHttpClientRemainsUsableAfterMesClientIsDisposed()
        {
            var handler = new QueueHandler(
                Response(HttpStatusCode.OK, "{\"code\":\"0\",\"message\":\"ok\"}"),
                Response(HttpStatusCode.OK, "still-alive"));
            using (var httpClient = new HttpClient(handler))
            {
                var client = new MesHttpClient(CreateOptions(), httpClient, null);
                await client.SendOnlineAsync(CancellationToken.None);
                client.Dispose();

                using (var response = await httpClient.GetAsync("http://localhost/health"))
                    Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("still-alive"));
            }

            Assert.That(handler.RequestCount, Is.EqualTo(2));
            Assert.That(handler.IsDisposed, Is.True);
        }

        private static MesHttpClientOptions CreateOptions()
        {
            return new MesHttpClientOptions
            {
                BaseUrl = "http://localhost/api",
                DeviceNo = "D1",
                DeviceName = "Device",
                DeviceIp = "127.0.0.1",
                DeviceMac = "00-00-00-00-00-00",
                TimeoutMilliseconds = 2000,
            };
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }

        private sealed class QueueHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;

            public QueueHandler(params HttpResponseMessage[] responses)
            {
                _responses = new Queue<HttpResponseMessage>(responses);
            }

            public int RequestCount { get; private set; }
            public bool IsDisposed { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                if (_responses.Count == 0) throw new InvalidOperationException("No queued response.");
                return Task.FromResult(_responses.Dequeue());
            }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                while (_responses.Count > 0) _responses.Dequeue().Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
