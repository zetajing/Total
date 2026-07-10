using System;

namespace IndustrialCommSdk.Mes
{
    /// <summary>MES HTTP 客户端选项。</summary>
    public sealed class MesHttpClientOptions
    {
        /// <summary>MES API 基地址，如 "http://mes-server:8080/api"。</summary>
        public string BaseUrl { get; set; }

        /// <summary>设备编号。</summary>
        public string DeviceNo { get; set; }

        /// <summary>设备名称。</summary>
        public string DeviceName { get; set; }

        /// <summary>设备 IP 地址。</summary>
        public string DeviceIp { get; set; }

        /// <summary>设备 MAC 地址。</summary>
        public string DeviceMac { get; set; }

        /// <summary>单次 HTTP 请求超时，单位为毫秒。默认 5000。</summary>
        public int TimeoutMilliseconds { get; set; } = 5000;

        /// <summary>请求失败时最大重试次数（仅对 5xx/超时/网络异常重试）。默认 2。</summary>
        public int MaxRetries { get; set; } = 2;
    }
}
