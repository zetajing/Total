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

        /// <summary>
        /// 同时占用接收器处理容量的最大请求数。默认 32。
        /// 已超时但尚未真正退出的业务处理器仍占用容量，避免不响应取消的处理器造成无界任务累积。
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 32;

        /// <summary>允许接收的最大 JSON 正文字节数。默认 1 MiB。</summary>
        public int MaxRequestContentBytes { get; set; } = 1024 * 1024;

        /// <summary>
        /// 业务处理器超时，单位毫秒。默认 5000。
        /// 停止接收器时也使用该时限等待不响应取消的处理器；超时后处理器继续受跟踪并占用并发容量。
        /// </summary>
        public int HandlerTimeoutMilliseconds { get; set; } = 5000;

        /// <summary>
        /// 可选的 Authorization 请求头完整值。为空时不校验；生产环境对外监听时建议配置。
        /// </summary>
        public string RequiredAuthorizationHeaderValue { get; set; }
    }
}
