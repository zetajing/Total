namespace IndustrialCommSdk.Mes
{
    /// <summary>开放 MES JSON 请求的 HTTP 响应。</summary>
    public sealed class MesJsonResponse
    {
        /// <summary>本次请求使用的相对端点。</summary>
        public string Endpoint { get; internal set; }
        /// <summary>HTTP 数字状态码。</summary>
        public int StatusCode { get; internal set; }
        /// <summary>服务端返回的 HTTP 原因短语。</summary>
        public string ReasonPhrase { get; internal set; }
        /// <summary>服务端返回的 Content-Type 响应头。</summary>
        public string ContentType { get; internal set; }
        /// <summary>服务端返回的原始响应正文。</summary>
        public string Body { get; internal set; }
        /// <summary>状态码是否位于 200 到 299。</summary>
        public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode <= 299;
    }
}
