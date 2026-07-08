using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Mes
{
    /// <summary>定义 MES TCP 客户端的连接、发送和消息事件契约。</summary>
    public interface IMesClient : IDisposable
    {
        /// <summary>获取底层连接当前是否可用。</summary>
        bool IsConnected { get; }
        /// <summary>获取当前连接状态。</summary>
        MesConnectionState State { get; }
        /// <summary>连接状态变化时触发。</summary>
        event EventHandler<MesConnectionStateChangedEventArgs> ConnectionStateChanged;
        /// <summary>收到 FACHECK 消息时触发。</summary>
        event EventHandler<MesMessageEventArgs<FaCheckMessage>> FaCheckReceived;
        /// <summary>收到 FANUM 消息时触发。</summary>
        event EventHandler<MesMessageEventArgs<FaNumMessage>> FaNumReceived;
        /// <summary>发送或接收原始报文时触发。</summary>
        event EventHandler<MesRawMessageEventArgs> RawMessage;
        /// <summary>收到无法解析的协议报文时触发。</summary>
        event EventHandler<MesProtocolErrorEventArgs> ProtocolError;
        /// <summary>连接 MES 服务。</summary>
        Task ConnectAsync(CancellationToken cancellationToken);
        /// <summary>主动断开 MES 服务并停止本次重连流程。</summary>
        Task DisconnectAsync(CancellationToken cancellationToken);
        /// <summary>发送设备上线报文。</summary>
        Task SendOnlineAsync(CancellationToken cancellationToken);
        /// <summary>发送 FATRACK 生产追踪报文。</summary>
        Task SendTrackAsync(FaTrackMessage message, CancellationToken cancellationToken);
    }
}
