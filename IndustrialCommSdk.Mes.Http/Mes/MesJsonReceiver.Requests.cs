using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace IndustrialCommSdk.Mes
{
    public sealed partial class MesJsonReceiver
    {
        private static void RejectOverloaded(HttpListenerContext context)
        {
            var response = context.Response;
            try
            {
                var bytes = Encoding.UTF8.GetBytes("{\"error\":\"too_many_requests\"}");
                response.StatusCode = 429;
                response.Headers["Retry-After"] = "1";
                response.ContentType = "application/json; charset=utf-8";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = bytes.Length;

                // Queue the tiny rejection directly through HttpListener instead of retaining an
                // async managed task per rejected client. Admitted long-lived work remains strictly
                // bounded by MaxConcurrentRequests even when rejected clients stop reading.
                response.Close(bytes, false);
            }
            catch
            {
                TryClose(response);
            }
        }

        private async Task HandleContextAsync(
            HttpListenerContext context,
            int requestId,
            RequestPermit permit,
            CancellationToken stopToken)
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

                var handlerCancellation = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
                var handlerTask = RunHandlerAsync(requestId, request, handlerCancellation.Token);
                TrackHandler(requestId, handlerTask, handlerCancellation);

                using (var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(stopToken))
                {
                    var deadlineTask = Task.Delay(_options.HandlerTimeoutMilliseconds, deadlineCancellation.Token);
                    if (await Task.WhenAny(handlerTask, deadlineTask).ConfigureAwait(false) != handlerTask)
                    {
                        TryCancel(handlerCancellation);
                        if (!handlerTask.IsCompleted)
                        {
                            var retainedPermit = permit;
                            permit = null;
                            ReleasePermitWhenHandlerCompletes(handlerTask, retainedPermit);
                        }

                        stopToken.ThrowIfCancellationRequested();
                        throw new HandlerTimeoutException();
                    }
                    TryCancel(deadlineCancellation);
                }

                var result = await handlerTask.ConfigureAwait(false);
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
            finally
            {
                permit?.Dispose();
            }
        }

        private Task<MesJsonReceiveResponse> RunHandlerAsync(
            int requestId,
            MesJsonReceiveRequest request,
            CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                var previousRequestId = _currentRequestId.Value;
                _currentRequestId.Value = requestId;
                try
                {
                    var task = _handler(request, cancellationToken);
                    if (task == null)
                        throw new InvalidOperationException("MES JSON receive handler returned a null task.");
                    return await task.ConfigureAwait(false);
                }
                finally
                {
                    _currentRequestId.Value = previousRequestId;
                }
            }, CancellationToken.None);
        }

        private static void ReleasePermitWhenHandlerCompletes(Task handlerTask, RequestPermit permit)
        {
            _ = handlerTask.ContinueWith(
                completed => permit.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void TryCancel(CancellationTokenSource source)
        {
            if (source == null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException ex)
            {
                _logger.Error("MES HTTP receiver cancellation callback failed.", ex);
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
            return !string.IsNullOrEmpty(mediaType) &&
                (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                 mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
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

        private sealed class RequestTooLargeException : Exception { }
        private sealed class HandlerTimeoutException : Exception { }
    }
}
