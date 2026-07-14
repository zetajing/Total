using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Mes
{
    /// <summary>定义 MES HTTP 客户端的请求-响应契约（无服务端推送）。</summary>
    public interface IMesHttpClient : IDisposable
    {
        /// <summary>获取客户端是否可用（基于最近一次请求是否成功，HTTP 无长连接状态）。</summary>
        bool IsConnected { get; }

        /// <summary>将 JSON 正文发送到 BaseUrl 下任意安全的相对端点。</summary>
        Task<MesJsonResponse> SendJsonAsync(
            string endpoint,
            string json,
            CancellationToken cancellationToken);
    }
}
