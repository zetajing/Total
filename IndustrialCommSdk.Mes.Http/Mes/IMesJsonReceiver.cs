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

        /// <summary>
        /// 停止监听，并在配置的处理器时限内等待正在处理的请求退出。
        /// 不响应取消的处理器不会阻塞停止，但会继续受到跟踪且保留其并发容量，直到真正结束。
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken);
    }
}
