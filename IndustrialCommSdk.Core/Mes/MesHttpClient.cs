using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Mes
{
    /// <summary>开放 MES JSON HTTP 客户端，不内置任何业务消息类型。</summary>
    public sealed class MesHttpClient : IMesHttpClient
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient(CreateDefaultHandler(), true);
        private readonly MesHttpClientOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly HttpClient _httpClient;
        private readonly bool _disposeHttpClient;
        private int _disposed;
        private volatile bool _lastRequestSuccess;

        public MesHttpClient(MesHttpClientOptions options, IIndustrialLogger logger = null)
            : this(options, SharedHttpClient, false, logger) { }

        public MesHttpClient(
            MesHttpClientOptions options,
            HttpMessageHandler handler,
            bool disposeHandler,
            IIndustrialLogger logger = null)
            : this(options, CreateHttpClient(handler, disposeHandler), true, logger) { }

        public MesHttpClient(MesHttpClientOptions options, HttpClient httpClient, IIndustrialLogger logger)
            : this(options, httpClient, false, logger) { }

        private MesHttpClient(
            MesHttpClientOptions options,
            HttpClient httpClient,
            bool disposeHttpClient,
            IIndustrialLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(options);
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _disposeHttpClient = disposeHttpClient;
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public bool IsConnected => !IsDisposed() && _lastRequestSuccess;

        public Task<MesJsonResponse> SendJsonAsync(
            string endpoint,
            string json,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateJsonObject(json);
            return SendCoreAsync(NormalizeEndpoint(endpoint), json, cancellationToken);
        }

        private async Task<MesJsonResponse> SendCoreAsync(
            string endpoint,
            string json,
            CancellationToken cancellationToken)
        {
            var url = CombineUrl(_options.BaseUrl, endpoint);
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("MES HTTP TX begin | Endpoint={0} | Url={1} | Body={2}",
                endpoint, url, TruncateBody(json)));

            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    using (var request = CreateJsonRequest(url, json))
                    {
                        timeoutSource.CancelAfter(_options.TimeoutMilliseconds);
                        using (var response = await _httpClient.SendAsync(
                            request,
                            HttpCompletionOption.ResponseHeadersRead,
                            timeoutSource.Token).ConfigureAwait(false))
                        {
                            var responseBody = await ReadResponseBodyAsync(
                                response.Content,
                                _options.MaxResponseContentBytes,
                                timeoutSource.Token).ConfigureAwait(false);

                            _logger.Info(string.Format(
                                "MES HTTP RX | Endpoint={0} | Status={1} | Body={2} | Elapsed={3}ms",
                                endpoint,
                                (int)response.StatusCode,
                                TruncateBody(responseBody),
                                stopwatch.ElapsedMilliseconds));

                            if (response.IsSuccessStatusCode)
                            {
                                _lastRequestSuccess = true;
                                return CreateResponse(endpoint, response, responseBody);
                            }

                            if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                            {
                                _lastRequestSuccess = false;
                                return CreateResponse(endpoint, response, responseBody);
                            }

                            throw new HttpRequestException(string.Format(
                                "MES HTTP server error. Status={0}, Body={1}",
                                (int)response.StatusCode,
                                TruncateBody(responseBody)));
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _lastRequestSuccess = false;
                    throw;
                }
                catch (Exception ex) when (retries < _options.MaxRetries && IsRetryable(ex, cancellationToken))
                {
                    retries++;
                    _logger.Warn(string.Format("MES HTTP retry {0}/{1} | Endpoint={2} | Error={3}",
                        retries, _options.MaxRetries, endpoint, ex.Message));
                    await Task.Delay(
                        CalculateRetryDelay(_options.RetryDelayMilliseconds, retries),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _lastRequestSuccess = false;
                    _logger.Error(string.Format("MES HTTP failed | Endpoint={0} | Error={1}", endpoint, ex.Message), ex);
                    throw;
                }
            }
        }

        private static MesJsonResponse CreateResponse(
            string endpoint,
            HttpResponseMessage response,
            string body)
        {
            return new MesJsonResponse
            {
                Endpoint = "/" + endpoint,
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                ContentType = response.Content?.Headers.ContentType?.ToString(),
                Body = body,
            };
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("MES HTTP endpoint is required.", nameof(endpoint));

            endpoint = endpoint.Trim();
            if (endpoint.StartsWith("//", StringComparison.Ordinal) ||
                endpoint.IndexOf('\\') >= 0 || endpoint.IndexOf('#') >= 0 ||
                Uri.TryCreate(endpoint, UriKind.Absolute, out _))
                throw new ArgumentException("MES HTTP endpoint must be a relative path.", nameof(endpoint));

            endpoint = endpoint.TrimStart('/');
            var path = endpoint.Split('?')[0];
            if (path.Length == 0)
                throw new ArgumentException("MES HTTP endpoint path is required.", nameof(endpoint));
            foreach (var segment in path.Split('/'))
            {
                var decoded = Uri.UnescapeDataString(segment);
                if (decoded == "." || decoded == "..")
                    throw new ArgumentException("MES HTTP endpoint cannot contain relative traversal segments.", nameof(endpoint));
            }
            return endpoint;
        }

        private static string CombineUrl(string baseUrl, string endpoint)
        {
            return baseUrl.TrimEnd('/') + "/" + endpoint;
        }

        private static HttpMessageHandler CreateDefaultHandler()
        {
            return new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 32 };
        }

        private static HttpClient CreateHttpClient(HttpMessageHandler handler, bool disposeHandler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var client = new HttpClient(handler, disposeHandler);
            client.DefaultRequestHeaders.ConnectionClose = false;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IndustrialCommSdk/1.0");
            return client;
        }

        private static HttpRequestMessage CreateJsonRequest(string url, string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private static async Task<string> ReadResponseBodyAsync(
            HttpContent content,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (content == null) return null;
            if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value > maximumBytes)
                throw new InvalidDataException("MES HTTP JSON response exceeds the configured maximum size.");

            using (var input = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    if (output.Length + read > maximumBytes)
                        throw new InvalidDataException("MES HTTP JSON response exceeds the configured maximum size.");
                    output.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
            }
        }

        private static void ValidateJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("MES HTTP JSON body is required.", nameof(json));
            if (json.TrimStart()[0] != '{')
                throw new ArgumentException("MES HTTP JSON body must have an object root.", nameof(json));
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                    stream, Encoding.UTF8, XmlDictionaryReaderQuotas.Max, null))
                {
                    while (reader.Read()) { }
                }
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is SerializationException)
            {
                throw new ArgumentException("MES HTTP body is not valid JSON.", nameof(json), ex);
            }
        }

        private static bool IsRetryable(Exception exception, CancellationToken callerToken)
        {
            return exception is HttpRequestException ||
                (exception is OperationCanceledException && !callerToken.IsCancellationRequested);
        }

        private static int CalculateRetryDelay(int baseDelayMilliseconds, int retryNumber)
        {
            return (int)Math.Min((long)baseDelayMilliseconds * Math.Min(retryNumber, 60), 30000L);
        }

        private static string TruncateBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return "(empty)";
            return body.Length <= 500 ? body : body.Substring(0, 497) + "...";
        }

        private static void ValidateOptions(MesHttpClientOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
                throw new ArgumentException("MES HTTP BaseUrl is required.", nameof(options));
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("MES HTTP BaseUrl must be an absolute HTTP or HTTPS URL.", nameof(options));
            if (options.TimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.TimeoutMilliseconds));
            if (options.MaxRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxRetries));
            if (options.RetryDelayMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(options.RetryDelayMilliseconds));
            if (options.MaxResponseContentBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxResponseContentBytes));
        }

        private bool IsDisposed() => Volatile.Read(ref _disposed) != 0;
        private void ThrowIfDisposed()
        {
            if (IsDisposed()) throw new ObjectDisposedException(nameof(MesHttpClient));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_disposeHttpClient) _httpClient.Dispose();
        }
    }
}
