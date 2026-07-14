namespace IndustrialCommSdk.Mes
{
    /// <summary>MES HTTP JSON 接收器选项。</summary>
    public sealed class MesJsonReceiverOptions
    {
        /// <summary>
        /// HttpListener 前缀，必须以斜杠结尾。默认仅监听本机：
        /// http://127.0.0.1:8081/mes/。
        /// </summary>
        public string ListenPrefix { get; set; } = "http://127.0.0.1:8081/mes/";

        /// <summary>允许接收的最大 JSON 正文字节数。默认 1 MiB。</summary>
        public int MaxRequestContentBytes { get; set; } = 1024 * 1024;

        /// <summary>业务处理器超时，单位毫秒。默认 5000。</summary>
        public int HandlerTimeoutMilliseconds { get; set; } = 5000;

        /// <summary>
        /// 可选的 Authorization 请求头完整值。为空时不校验；生产环境对外监听时建议配置。
        /// </summary>
        public string RequiredAuthorizationHeaderValue { get; set; }
    }
}
