using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace IndustrialCommSdk.Web.Http
{
    /// <summary>通用 HTTP API 客户端选项。</summary>
    public sealed class HttpApiClientOptions
    {
        public HttpApiClientOptions()
        {
            Timeout = TimeSpan.FromSeconds(30);
            MaxResponseContentBytes = 4 * 1024 * 1024;
            UserAgent = "IndustrialCommSdk.Web/1.0";
        }

        public TimeSpan Timeout { get; set; }
        public long MaxResponseContentBytes { get; set; }
        public string UserAgent { get; set; }

        internal HttpApiClientOptions Clone()
        {
            return new HttpApiClientOptions
            {
                Timeout = Timeout,
                MaxResponseContentBytes = MaxResponseContentBytes,
                UserAgent = UserAgent,
            };
        }
    }

    /// <summary>一次 HTTP API 请求。</summary>
    public sealed class HttpApiRequest
    {
        public HttpApiRequest(HttpMethod method, Uri uri)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public HttpMethod Method { get; private set; }
        public Uri Uri { get; private set; }
        public IDictionary<string, string> Headers { get; private set; }
        public byte[] Content { get; set; }
        public string ContentType { get; set; }
        public long? MaxResponseContentBytes { get; set; }

        public static HttpApiRequest Json(HttpMethod method, Uri uri, object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new HttpApiRequest(method, uri)
            {
                Content = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value)),
                ContentType = "application/json; charset=utf-8",
            };
        }

        public static HttpApiRequest Text(HttpMethod method, Uri uri, string value, string contentType = "text/plain; charset=utf-8")
        {
            return new HttpApiRequest(method, uri)
            {
                Content = Encoding.UTF8.GetBytes(value ?? string.Empty),
                ContentType = contentType,
            };
        }

        public static HttpApiRequest Binary(HttpMethod method, Uri uri, byte[] value, string contentType = "application/octet-stream")
        {
            return new HttpApiRequest(method, uri)
            {
                Content = value ?? throw new ArgumentNullException(nameof(value)),
                ContentType = contentType,
            };
        }
    }

    /// <summary>包含完整状态、响应头和受限响应体的 HTTP API 响应。</summary>
    public sealed class HttpApiResponse
    {
        internal HttpApiResponse(HttpStatusCode statusCode, string reasonPhrase, IDictionary<string, string[]> headers, byte[] content)
        {
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase;
            Headers = headers;
            Content = content ?? new byte[0];
        }

        public HttpStatusCode StatusCode { get; private set; }
        public string ReasonPhrase { get; private set; }
        public IDictionary<string, string[]> Headers { get; private set; }
        public byte[] Content { get; private set; }
        public bool IsSuccessStatusCode { get { return (int)StatusCode >= 200 && (int)StatusCode <= 299; } }

        public string ReadText(Encoding encoding = null)
        {
            return (encoding ?? Encoding.UTF8).GetString(Content);
        }

        public T ReadJson<T>()
        {
            return JsonConvert.DeserializeObject<T>(ReadText());
        }
    }

    /// <summary>响应体超过调用方配置的安全上限。</summary>
    public sealed class HttpResponseTooLargeException : InvalidOperationException
    {
        public HttpResponseTooLargeException(long limit)
            : base(string.Format("HTTP response exceeded the configured limit of {0} bytes.", limit))
        {
            Limit = limit;
        }

        public long Limit { get; private set; }
    }
}
