using System;

namespace IndustrialCommSdk.Mes
{
    /// <summary>MES HTTP 客户端选项。</summary>
    public sealed class MesHttpClientOptions
    {
        /// <summary>MES API 基地址，如 "http://mes-server:8080/api"。</summary>
        public string BaseUrl { get; set; }

        /// <summary>单次 HTTP 请求超时，单位为毫秒。默认 5000。</summary>
        public int TimeoutMilliseconds { get; set; } = 5000;

        /// <summary>请求失败时最大重试次数（仅对 5xx/超时/网络异常重试）。默认 2。</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>首次重试等待时间，后续按倍数退避，单位为毫秒。默认 500。</summary>
        public int RetryDelayMilliseconds { get; set; } = 500;

        /// <summary>允许读取的最大 JSON 响应体字节数。默认 1 MiB。</summary>
        public int MaxResponseContentBytes { get; set; } = 1024 * 1024;
    }
}
