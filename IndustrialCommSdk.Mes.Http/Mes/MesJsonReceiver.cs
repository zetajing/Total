using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
    /// <summary>基于 HttpListener 的开放式 MES HTTP JSON 接收器。</summary>
    public sealed class MesJsonReceiver : IMesJsonReceiver
    {
        private readonly MesJsonReceiverOptions _options;
        private readonly MesJsonReceiveHandler _handler;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, Task> _activeRequests = new ConcurrentDictionary<int, Task>();
        private HttpListener _listener;
        private CancellationTokenSource _stopSource;
        private Task _acceptLoopTask;
        private int _nextRequestId;
        private int _running;
        private int _disposed;

        public MesJsonReceiver(
            MesJsonReceiverOptions options,
            MesJsonReceiveHandler handler,
            IIndustrialLogger logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _logger = logger ?? NullIndustrialLogger.Instance;
            ValidateOptions(options);
        }

        public bool IsRunning => Volatile.Read(ref _running) != 0;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsRunning) return;

                var listener = new HttpListener();
                listener.Prefixes.Add(_options.ListenPrefix);
                try { listener.Start(); }
                catch
                {
                    listener.Close();
                    throw;
                }

                _stopSource?.Dispose();
                _stopSource = new CancellationTokenSource();
                _listener = listener;
                Volatile.Write(ref _running, 1);
                _acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, _stopSource.Token));
                _logger.Info("MES HTTP receiver started | Prefix=" + _options.ListenPrefix);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsRunning) return;
                Volatile.Write(ref _running, 0);
                _stopSource.Cancel();
                try { _listener.Close(); } catch { }

                if (_acceptLoopTask != null)
                {
                    try { await _acceptLoopTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    _acceptLoopTask = null;
                }

                var requests = _activeRequests.Values.ToArray();
                if (requests.Length > 0)
                {
                    try { await Task.WhenAll(requests).ConfigureAwait(false); }
                    catch { }
                }
                _activeRequests.Clear();
                _listener = null;
                _logger.Info("MES HTTP receiver stopped | Prefix=" + _options.ListenPrefix);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.Error("MES HTTP receiver accept failed.", ex);
                    continue;
                }

                var requestId = Interlocked.Increment(ref _nextRequestId);
                var task = HandleContextAsync(context, cancellationToken);
                _activeRequests[requestId] = task;
                _ = task.ContinueWith(
                    _ => { Task ignored; _activeRequests.TryRemove(requestId, out ignored); },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken stopToken)
        {
            var response = context.Response;
            try
            {
                if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    response.Headers["Allow"] = "POST";
                    await WriteJsonAsync(response, 405, "{\"error\":\"method_not_allowed\"}", stopToken).ConfigureAwait(false);
                    return;
                }

                if (!IsAuthorized(context.Request.Headers["Authorization"]))
                {
                    await WriteJsonAsync(response, 401, "{\"error\":\"unauthorized\"}", stopToken).ConfigureAwait(false);
                    return;
                }

                if (!IsJsonContentType(context.Request.ContentType))
                {
                    await WriteJsonAsync(response, 415, "{\"error\":\"content_type_must_be_json\"}", stopToken).ConfigureAwait(false);
                    return;
                }

                string body;
                try
                {
                    body = await ReadBodyAsync(
                        context.Request.InputStream,
                        context.Request.ContentLength64,
                        _options.MaxRequestContentBytes,
                        stopToken).ConfigureAwait(false);
                    ValidateJsonObject(body);
                }
                catch (RequestTooLargeException)
                {
                    await WriteJsonAsync(response, 413, "{\"error\":\"request_too_large\"}", stopToken).ConfigureAwait(false);
                    return;
                }
                catch (ArgumentException)
                {
                    await WriteJsonAsync(response, 400, "{\"error\":\"invalid_json_object\"}", stopToken).ConfigureAwait(false);
                    return;
                }

                var request = new MesJsonReceiveRequest
                {
                    Endpoint = GetRelativeEndpoint(context.Request),
                    Body = body,
                    ContentType = context.Request.ContentType,
                    RemoteEndPoint = context.Request.RemoteEndPoint?.ToString(),
                    Headers = CopyHeaders(context.Request),
                };

                _logger.Info(string.Format(
                    "MES HTTP receiver RX | Endpoint={0} | Remote={1} | BodyBytes={2}",
                    request.Endpoint,
                    request.RemoteEndPoint ?? "(unknown)",
                    Encoding.UTF8.GetByteCount(body)));

                MesJsonReceiveResponse result;
                using (var handlerTimeout = CancellationTokenSource.CreateLinkedTokenSource(stopToken))
                {
                    var handlerTask = _handler(request, handlerTimeout.Token);
                    if (handlerTask == null) throw new InvalidOperationException("MES JSON receive handler returned a null task.");
                    var deadlineTask = Task.Delay(_options.HandlerTimeoutMilliseconds, stopToken);
                    if (await Task.WhenAny(handlerTask, deadlineTask).ConfigureAwait(false) != handlerTask)
                    {
                        stopToken.ThrowIfCancellationRequested();
                        handlerTimeout.Cancel();
                        ObserveFault(handlerTask);
                        throw new HandlerTimeoutException();
                    }
                    result = await handlerTask.ConfigureAwait(false);
                }

                if (result == null) throw new InvalidOperationException("MES JSON receive handler returned null.");
                if (result.StatusCode < 200 || result.StatusCode > 599)
                    throw new InvalidOperationException("MES JSON receive response status code must be between 200 and 599.");
                ValidateJsonObject(result.Json);
                await WriteJsonAsync(response, result.StatusCode, result.Json, stopToken).ConfigureAwait(false);
            }
            catch (HandlerTimeoutException)
            {
                await TryWriteErrorAsync(response, 504, "{\"error\":\"handler_timeout\"}").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
            {
                await TryWriteErrorAsync(response, 504, "{\"error\":\"handler_timeout\"}").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                TryClose(response);
            }
            catch (Exception ex)
            {
                _logger.Error("MES HTTP receiver request failed.", ex);
                await TryWriteErrorAsync(response, 500, "{\"error\":\"internal_error\"}").ConfigureAwait(false);
            }
        }

        private bool IsAuthorized(string actual)
        {
            var required = _options.RequiredAuthorizationHeaderValue;
            if (string.IsNullOrEmpty(required)) return true;
            if (actual == null || actual.Length != required.Length) return false;
            var difference = 0;
            for (var i = 0; i < required.Length; i++) difference |= actual[i] ^ required[i];
            return difference == 0;
        }

        private static bool IsJsonContentType(string contentType)
        {
            MediaTypeHeaderValue parsed;
            if (!MediaTypeHeaderValue.TryParse(contentType, out parsed)) return false;
            var mediaType = parsed.MediaType;
            return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> ReadBodyAsync(
            Stream input,
            long contentLength,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            if (contentLength > maximumBytes) throw new RequestTooLargeException();
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    if (output.Length + read > maximumBytes) throw new RequestTooLargeException();
                    output.Write(buffer, 0, read);
                }
                return new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, checked((int)output.Length));
            }
        }

        private static void ValidateJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{')
                throw new ArgumentException("JSON root must be an object.", nameof(json));
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
                throw new ArgumentException("JSON is invalid.", nameof(json), ex);
            }
        }

        private string GetRelativeEndpoint(HttpListenerRequest request)
        {
            var rawUrl = request.RawUrl ?? request.Url.PathAndQuery;
            var basePath = new Uri(_options.ListenPrefix).AbsolutePath;
            var relative = rawUrl.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                ? rawUrl.Substring(basePath.Length)
                : rawUrl.TrimStart('/');
            return "/" + relative;
        }

        private static IReadOnlyDictionary<string, string> CopyHeaders(HttpListenerRequest request)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in request.Headers.AllKeys) headers[key] = request.Headers[key];
            return headers;
        }

        private static async Task WriteJsonAsync(
            HttpListenerResponse response,
            int statusCode,
            string json,
            CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            response.Close();
        }

        private static async Task TryWriteErrorAsync(HttpListenerResponse response, int statusCode, string json)
        {
            try { await WriteJsonAsync(response, statusCode, json, CancellationToken.None).ConfigureAwait(false); }
            catch { TryClose(response); }
        }

        private static void TryClose(HttpListenerResponse response)
        {
            try { response.Close(); } catch { }
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                completed => { var ignored = completed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void ValidateOptions(MesJsonReceiverOptions options)
        {
            Uri prefix;
            if (string.IsNullOrWhiteSpace(options.ListenPrefix) ||
                !Uri.TryCreate(options.ListenPrefix, UriKind.Absolute, out prefix) ||
                (prefix.Scheme != Uri.UriSchemeHttp && prefix.Scheme != Uri.UriSchemeHttps) ||
                !options.ListenPrefix.EndsWith("/", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(prefix.Query) || !string.IsNullOrEmpty(prefix.Fragment))
                throw new ArgumentException("MES receiver ListenPrefix must be an absolute HTTP/HTTPS prefix ending with '/'.", nameof(options));
            if (options.MaxRequestContentBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxRequestContentBytes));
            if (options.HandlerTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.HandlerTimeoutMilliseconds));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(MesJsonReceiver));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            _stopSource?.Dispose();
            _lifecycleGate.Dispose();
        }

        private sealed class RequestTooLargeException : Exception { }
        private sealed class HandlerTimeoutException : Exception { }
    }
}
