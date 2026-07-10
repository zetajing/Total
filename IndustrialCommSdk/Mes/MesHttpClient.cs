using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Mes
{
    /// <summary>MES HTTP 客户端，通过 REST API 与 MES 服务通信（无状态，无服务端推送）。</summary>
    public sealed class MesHttpClient : IMesHttpClient
    {
        private readonly MesHttpClientOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly HttpClient _httpClient;
        private int _disposed;
        private volatile bool _lastRequestSuccess;

        /// <summary>使用指定选项创建 HTTP 客户端。</summary>
        public MesHttpClient(MesHttpClientOptions options, IIndustrialLogger logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(options);
            _logger = logger ?? NullIndustrialLogger.Instance;

            var handler = new HttpClientHandler
            {
                // MES HTTP API 通常不需要自动重定向
                AllowAutoRedirect = false,
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
            };
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            // 某些 MES 服务可能检查 User-Agent
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IndustrialCommSdk/1.0");
        }

        public bool IsConnected => !IsDisposed() && _lastRequestSuccess;

        public async Task<MesOnlineResponse> SendOnlineAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var request = new MesOnlineRequest
            {
                DeviceNo = _options.DeviceNo,
                DeviceName = _options.DeviceName,
                DeviceIp = _options.DeviceIp,
                DeviceMac = _options.DeviceMac,
            };

            return await SendAsync<MesOnlineRequest, MesOnlineResponse>(
                "online", request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<MesFaCheckResponse> SendFaCheckAsync(MesFaCheckRequest request, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (request == null) throw new ArgumentNullException(nameof(request));

            return await SendAsync<MesFaCheckRequest, MesFaCheckResponse>(
                "facheck", request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<MesFaTrackResponse> SendFaTrackAsync(MesFaTrackRequest request, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (request == null) throw new ArgumentNullException(nameof(request));

            return await SendAsync<MesFaTrackRequest, MesFaTrackResponse>(
                "fatrack", request, cancellationToken).ConfigureAwait(false);
        }

        private async Task<TResponse> SendAsync<TRequest, TResponse>(
            string endpoint, TRequest body, CancellationToken cancellationToken)
            where TResponse : MesApiResponse, new()
        {
            var url = CombineUrl(_options.BaseUrl, endpoint);
            var json = MesProtocolCodec.Serialize(body);
            var stopwatch = Stopwatch.StartNew();

            _logger.Info(string.Format("MES HTTP TX begin | Endpoint={0} | Url={1} | Body={2}", endpoint, url, json));

            var retries = 0;
            var maxRetries = Math.Max(0, _options.MaxRetries);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string responseBody = null;

                try
                {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        var response = await _httpClient.PostAsync(url, content, cancellationToken)
                            .ConfigureAwait(false);

                        responseBody = await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);

                        _logger.Info(string.Format("MES HTTP RX | Endpoint={0} | Status={1} | Body={2} | Elapsed={3}ms",
                            endpoint, (int)response.StatusCode, TruncateBody(responseBody), stopwatch.ElapsedMilliseconds));

                        if (response.IsSuccessStatusCode)
                        {
                            var result = Deserialize<TResponse>(responseBody);
                            _lastRequestSuccess = true;
                            return result;
                        }

                        // 4xx 不重试（客户端错误）
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            _logger.Warn(string.Format("MES HTTP client error | Endpoint={0} | Status={1} | Body={2}",
                                endpoint, (int)response.StatusCode, TruncateBody(responseBody)));

                            var errorResult = DeserializeOrNull<TResponse>(responseBody);
                            if (errorResult != null)
                            {
                                _lastRequestSuccess = false;
                                return errorResult;
                            }

                            _lastRequestSuccess = false;
                            return new TResponse
                            {
                                Code = ((int)response.StatusCode).ToString(),
                                Message = responseBody ?? response.ReasonPhrase,
                            };
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("MES HTTP cancelled | Endpoint=" + endpoint);
                    _lastRequestSuccess = false;
                    throw;
                }
                catch (Exception ex) when (retries < maxRetries)
                {
                    // 仅 5xx / 超时 / 网络异常重试
                    _logger.Warn(string.Format("MES HTTP retry {0}/{1} | Endpoint={2} | Error={3}",
                        retries + 1, maxRetries, endpoint, ex.Message));
                    retries++;

                    // 退避：重试等待 500ms * retries
                    var delay = Math.Min(500 * retries, 5000);
                    try { await Task.Delay(delay, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.Error(string.Format("MES HTTP failed | Endpoint={0} | Error={1}", endpoint, ex.Message), ex);
                    _lastRequestSuccess = false;
                    throw;
                }
            }
        }

        private T Deserialize<T>(string json)
        {
            return MesProtocolCodec.Deserialize<T>(json);
        }

        private static T DeserializeOrNull<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return MesProtocolCodec.Deserialize<T>(json); }
            catch { return null; }
        }

        private static string CombineUrl(string baseUrl, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("MES HTTP BaseUrl is not configured.");

            baseUrl = baseUrl.TrimEnd('/');
            endpoint = endpoint.TrimStart('/');
            return baseUrl + "/" + endpoint;
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
            if (string.IsNullOrWhiteSpace(options.DeviceNo))
                throw new ArgumentException("MES HTTP device number is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceName))
                throw new ArgumentException("MES HTTP device name is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceIp))
                throw new ArgumentException("MES HTTP device IP is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceMac))
                throw new ArgumentException("MES HTTP device MAC is required.", nameof(options));
            if (options.TimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.TimeoutMilliseconds), "MES HTTP timeout must be positive.");
            if (options.MaxRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxRetries), "MES HTTP max retries must be >= 0.");
        }

        private bool IsDisposed() => Volatile.Read(ref _disposed) != 0;
        private void ThrowIfDisposed() { if (IsDisposed()) throw new ObjectDisposedException(nameof(MesHttpClient)); }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _httpClient.Dispose();
        }
    }
}
