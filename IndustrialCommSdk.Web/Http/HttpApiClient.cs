using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Web.Http
{
    /// <summary>支持 JSON、文本和二进制请求，并限制响应大小的通用 HTTP/HTTPS 客户端。</summary>
    public sealed class HttpApiClient : IHttpApiClient
    {
        private readonly HttpApiClientOptions _options;
        private readonly HttpClient _client;
        private readonly bool _ownsClient;
        private int _disposed;

        public HttpApiClient(HttpApiClientOptions options = null, HttpMessageHandler handler = null)
        {
            _options = (options ?? new HttpApiClientOptions()).Clone();
            ValidateOptions(_options);
            if (handler == null)
            {
                handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                };
            }

            _client = new HttpClient(handler, true);
            _ownsClient = true;
            _client.Timeout = Timeout.InfiniteTimeSpan;
            if (!string.IsNullOrWhiteSpace(_options.UserAgent))
                _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }

        public HttpApiClient(HttpClient client, HttpApiClientOptions options = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = (options ?? new HttpApiClientOptions()).Clone();
            ValidateOptions(_options);
            _ownsClient = false;
        }

        public async Task<HttpApiResponse> SendAsync(HttpApiRequest request, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!request.Uri.IsAbsoluteUri) throw new ArgumentException("HTTP request URI must be absolute.", nameof(request));
            if (request.Uri.Scheme != Uri.UriSchemeHttp && request.Uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("HTTP request URI must use HTTP or HTTPS.", nameof(request));

            var maxBytes = request.MaxResponseContentBytes ?? _options.MaxResponseContentBytes;
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(request.MaxResponseContentBytes));

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var message = BuildMessage(request))
            {
                timeoutSource.CancelAfter(_options.Timeout);
                using (var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false))
                {
                    if (response.Content != null && response.Content.Headers.ContentLength.HasValue &&
                        response.Content.Headers.ContentLength.Value > maxBytes)
                        throw new HttpResponseTooLargeException(maxBytes);

                    var bytes = response.Content == null
                        ? new byte[0]
                        : await ReadLimitedAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), maxBytes, timeoutSource.Token).ConfigureAwait(false);
                    var headers = response.Headers.Concat(response.Content == null
                            ? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()
                            : response.Content.Headers)
                        .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.SelectMany(pair => pair.Value).ToArray(), StringComparer.OrdinalIgnoreCase);
                    return new HttpApiResponse(response.StatusCode, response.ReasonPhrase, headers, bytes);
                }
            }
        }

        private static HttpRequestMessage BuildMessage(HttpApiRequest request)
        {
            var message = new HttpRequestMessage(request.Method, request.Uri);
            try
            {
                if (request.Content != null)
                {
                    message.Content = new ByteArrayContent(request.Content);
                    if (!string.IsNullOrWhiteSpace(request.ContentType))
                        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
                }

                foreach (var header in request.Headers)
                {
                    if (string.IsNullOrWhiteSpace(header.Key)) continue;
                    if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        if (message.Content == null) message.Content = new ByteArrayContent(new byte[0]);
                        message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
                return message;
            }
            catch
            {
                message.Dispose();
                throw;
            }
        }

        private static async Task<byte[]> ReadLimitedAsync(Stream stream, long limit, CancellationToken cancellationToken)
        {
            using (stream)
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read == 0) return output.ToArray();
                    if (output.Length + read > limit) throw new HttpResponseTooLargeException(limit);
                    output.Write(buffer, 0, read);
                }
            }
        }

        private static void ValidateOptions(HttpApiClientOptions options)
        {
            if (options.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.Timeout));
            if (options.MaxResponseContentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxResponseContentBytes));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(HttpApiClient));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_ownsClient) _client.Dispose();
        }
    }
}
