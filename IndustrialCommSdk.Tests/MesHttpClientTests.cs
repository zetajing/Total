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
                Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await client.SendJsonAsync("events/upload", "{}", CancellationToken.None));

            Assert.That(handler.RequestCount, Is.EqualTo(3));
            Assert.That(handler.IsDisposed, Is.True);
        }

        [Test]
        public async Task ServerErrorCanRecoverOnRetry()
        {
            var handler = new QueueHandler(
                Response(HttpStatusCode.ServiceUnavailable, "temporary"),
                Response(HttpStatusCode.OK, "{\"ok\":true}"));
            var options = CreateOptions();
            options.MaxRetries = 1;

            MesJsonResponse result;
            using (var client = new MesHttpClient(options, handler, false))
                result = await client.SendJsonAsync("events/upload", "{}", CancellationToken.None);

            Assert.That(result.StatusCode, Is.EqualTo(200));
            Assert.That(result.Body, Is.EqualTo("{\"ok\":true}"));
            Assert.That(handler.RequestCount, Is.EqualTo(2));
            Assert.That(handler.IsDisposed, Is.False);
            handler.Dispose();
        }

        [Test]
        public async Task ClientErrorReturnsOriginalResponseWithoutRetry()
        {
            var handler = new QueueHandler(Response((HttpStatusCode)422, "{\"error\":\"invalid\"}"));
            var options = CreateOptions();
            options.MaxRetries = 3;

            MesJsonResponse result;
            using (var client = new MesHttpClient(options, handler, true))
                result = await client.SendJsonAsync("custom/report", "{}", CancellationToken.None);

            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(result.StatusCode, Is.EqualTo(422));
            Assert.That(result.Body, Is.EqualTo("{\"error\":\"invalid\"}"));
        }

        [Test]
        public async Task UploadPreservesJsonAndUsesJsonHeaders()
        {
            const string json = "{\r\n  \"custom\": \"value\", \"order\": 2\r\n}";
            var handler = new InspectingHandler(Response(HttpStatusCode.OK, "{}"));
            using (var client = new MesHttpClient(CreateOptions(), handler, true))
                await client.SendJsonAsync("custom/report", json, CancellationToken.None);

            Assert.That(handler.Body, Is.EqualTo(json));
            Assert.That(handler.ContentType, Is.EqualTo("application/json"));
            Assert.That(handler.CharSet, Is.EqualTo("utf-8").IgnoreCase);
            Assert.That(handler.Accept, Does.Contain("application/json"));
        }

        [TestCase("upload", "/api/upload", "")]
        [TestCase("/custom/report", "/api/custom/report", "")]
        [TestCase("v2/quality?line=1", "/api/v2/quality", "?line=1")]
        public async Task SupportsArbitraryRelativeEndpoints(string endpoint, string expectedPath, string expectedQuery)
        {
            var handler = new InspectingHandler(Response(HttpStatusCode.OK, "{}"));
            MesJsonResponse result;
            using (var client = new MesHttpClient(CreateOptions(), handler, true))
                result = await client.SendJsonAsync(endpoint, "{}", CancellationToken.None);

            Assert.That(handler.RequestUri.AbsolutePath, Is.EqualTo(expectedPath));
            Assert.That(handler.RequestUri.Query, Is.EqualTo(expectedQuery));
            Assert.That(result.Endpoint, Is.EqualTo("/" + endpoint.TrimStart('/')));
        }

        [TestCase("")]
        [TestCase("[]")]
        [TestCase("{not-json}")]
        public void InvalidJsonIsRejectedBeforeSending(string json)
        {
            var handler = new InspectingHandler(Response(HttpStatusCode.OK, "{}"));
            using (var client = new MesHttpClient(CreateOptions(), handler, true))
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await client.SendJsonAsync("upload", json, CancellationToken.None));
            Assert.That(handler.RequestCount, Is.EqualTo(0));
        }

        [TestCase("")]
        [TestCase("https://other.example/upload")]
        [TestCase("../admin")]
        [TestCase("%2e%2e/admin")]
        [TestCase("//other.example/upload")]
        public void UnsafeEndpointIsRejectedBeforeSending(string endpoint)
        {
            var handler = new InspectingHandler(Response(HttpStatusCode.OK, "{}"));
            using (var client = new MesHttpClient(CreateOptions(), handler, true))
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await client.SendJsonAsync(endpoint, "{}", CancellationToken.None));
            Assert.That(handler.RequestCount, Is.EqualTo(0));
        }

        [Test]
        public void OversizedResponseIsRejectedWithoutRetry()
        {
            var handler = new QueueHandler(Response(HttpStatusCode.OK, "{\"message\":\"too large\"}"));
            var options = CreateOptions();
            options.MaxResponseContentBytes = 8;
            options.MaxRetries = 3;
            using (var client = new MesHttpClient(options, handler, true))
                Assert.ThrowsAsync<System.IO.InvalidDataException>(async () =>
                    await client.SendJsonAsync("upload", "{}", CancellationToken.None));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
        }

        [Test]
        public void TimeoutRetriesOnlyToConfiguredLimit()
        {
            var handler = new DelayedHandler();
            var options = CreateOptions();
            options.TimeoutMilliseconds = 20;
            options.MaxRetries = 1;
            using (var client = new MesHttpClient(options, handler, true))
                Assert.ThrowsAsync<TaskCanceledException>(async () =>
                    await client.SendJsonAsync("upload", "{}", CancellationToken.None));
            Assert.That(handler.RequestCount, Is.EqualTo(2));
        }

        [Test]
        public void CallerCancellationDoesNotSendOrRetry()
        {
            var handler = new QueueHandler(Response(HttpStatusCode.OK, "{}"));
            using (var client = new MesHttpClient(CreateOptions(), handler, true))
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await client.SendJsonAsync("upload", "{}", new CancellationToken(true)));
            Assert.That(handler.RequestCount, Is.EqualTo(0));
        }

        [Test]
        public async Task NetworkFailureCanRecoverOnRetry()
        {
            var handler = new NetworkRecoveryHandler();
            var options = CreateOptions();
            options.MaxRetries = 1;
            using (var client = new MesHttpClient(options, handler, true))
            {
                var result = await client.SendJsonAsync("upload", "{}", CancellationToken.None);
                Assert.That(result.StatusCode, Is.EqualTo(200));
            }
            Assert.That(handler.RequestCount, Is.EqualTo(2));
        }

        [Test]
        public async Task ExternalHttpClientRemainsUsableAfterMesClientIsDisposed()
        {
            var handler = new QueueHandler(
                Response(HttpStatusCode.OK, "{}"),
                Response(HttpStatusCode.OK, "still-alive"));
            using (var httpClient = new HttpClient(handler))
            {
                var client = new MesHttpClient(CreateOptions(), httpClient, null);
                await client.SendJsonAsync("upload", "{}", CancellationToken.None);
                client.Dispose();
                using (var response = await httpClient.GetAsync("http://localhost/health"))
                    Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("still-alive"));
            }
            Assert.That(handler.IsDisposed, Is.True);
        }

        private static MesHttpClientOptions CreateOptions()
        {
            return new MesHttpClientOptions
            {
                BaseUrl = "http://localhost/api",
                TimeoutMilliseconds = 2000,
                RetryDelayMilliseconds = 1,
            };
        }

        private static HttpResponseMessage Response(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }

        private sealed class QueueHandler : HttpMessageHandler
        {
            private readonly Queue<HttpResponseMessage> _responses;
            public QueueHandler(params HttpResponseMessage[] responses) { _responses = new Queue<HttpResponseMessage>(responses); }
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

        private sealed class InspectingHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;
            public InspectingHandler(HttpResponseMessage response) { _response = response; }
            public int RequestCount { get; private set; }
            public Uri RequestUri { get; private set; }
            public string ContentType { get; private set; }
            public string CharSet { get; private set; }
            public string Accept { get; private set; }
            public string Body { get; private set; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                RequestUri = request.RequestUri;
                ContentType = request.Content.Headers.ContentType.MediaType;
                CharSet = request.Content.Headers.ContentType.CharSet;
                Accept = request.Headers.Accept.ToString();
                Body = await request.Content.ReadAsStringAsync();
                return _response;
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) _response.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class DelayedHandler : HttpMessageHandler
        {
            public int RequestCount { get; private set; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException();
            }
        }

        private sealed class NetworkRecoveryHandler : HttpMessageHandler
        {
            public int RequestCount { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                if (RequestCount == 1) throw new HttpRequestException("temporary network failure");
                return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
            }
        }
    }
}
