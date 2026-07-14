using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Mes
{
    /// <summary>接收 MES 主动 POST 的开放式 HTTP JSON 服务。</summary>
    public interface IMesJsonReceiver : IDisposable
    {
        /// <summary>获取接收器是否正在监听。</summary>
        bool IsRunning { get; }

        /// <summary>开始监听。</summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>停止监听并等待正在处理的请求退出。</summary>
        Task StopAsync(CancellationToken cancellationToken);
    }
}
