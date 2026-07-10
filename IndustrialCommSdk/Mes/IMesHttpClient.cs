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

        /// <summary>发送设备上线信息（POST {BaseUrl}/online）。</summary>
        Task<MesOnlineResponse> SendOnlineAsync(CancellationToken cancellationToken);

        /// <summary>发送工序检查 FACHECK（POST {BaseUrl}/facheck）。</summary>
        Task<MesFaCheckResponse> SendFaCheckAsync(MesFaCheckRequest request, CancellationToken cancellationToken);

        /// <summary>发送生产追踪 FATRACK（POST {BaseUrl}/fatrack）。</summary>
        Task<MesFaTrackResponse> SendFaTrackAsync(MesFaTrackRequest request, CancellationToken cancellationToken);
    }
}
