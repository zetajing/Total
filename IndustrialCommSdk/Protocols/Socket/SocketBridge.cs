using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using IndustrialCommSdk.Transport;

namespace IndustrialCommSdk.Protocols.Socket
{
    /// <summary>
    /// Socket 协议适配器接口。定义通过传输层客户端执行读写操作的方法。
    /// 用于自定义 TCP Socket 通信中的协议解析逻辑。
    /// </summary>
    public interface ISocketProtocolAdapter
    {
        /// <summary>
        /// 通过指定的传输层客户端从远程设备读取数据。
        /// </summary>
        /// <param name="transportClient">传输层客户端实例，用于发送和接收数据。</param>
        /// <param name="request">读取请求，包含地址等参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>包含读取结果的数据值。</returns>
        Task<DataValue> ReadAsync(ITransportClient transportClient, ReadRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// 通过指定的传输层客户端向远程设备写入数据。
        /// </summary>
        /// <param name="transportClient">传输层客户端实例，用于发送和接收数据。</param>
        /// <param name="request">写入请求，包含地址和待写入的值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task WriteAsync(ITransportClient transportClient, WriteRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Socket 桥接客户端的配置选项。
    /// </summary>
    public sealed class SocketBridgeClientOptions
    {
        /// <summary>获取或设置设备标识符。</summary>
        public string DeviceId { get; set; }
        /// <summary>获取或设置 TCP 传输层配置选项。</summary>
        public TcpTransportOptions Transport { get; set; }
    }

    /// <summary>
    /// Socket 桥接客户端。通过 TCP 套接字与远程设备通信，读写操作委托给 <see cref="ISocketProtocolAdapter"/> 实现。
    /// 适用于需要通过原始 TCP Socket 与自定义协议设备通信的场景。
    /// </summary>
    public sealed class SocketBridgeClient : IndustrialClientBase
    {
        private readonly TcpTransportClient _transport;
        private readonly ISocketProtocolAdapter _adapter;

        /// <summary>
        /// 初始化 <see cref="SocketBridgeClient"/> 的新实例。
        /// </summary>
        /// <param name="options">Socket 桥接客户端配置选项。</param>
        /// <param name="adapter">Socket 协议适配器，负责具体的协议解析。</param>
        /// <param name="logger">可选的日志记录器实例。</param>
        /// <param name="pollingScheduler">可选的轮询调度器实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 或 <paramref name="adapter"/> 为 null 时引发。</exception>
        public SocketBridgeClient(SocketBridgeClientOptions options, ISocketProtocolAdapter adapter, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null)
            : base(GetDeviceId(options), ProtocolKind.TcpSocket, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _transport = new TcpTransportClient(options.Transport);
        }

        private static string GetDeviceId(SocketBridgeClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return options.DeviceId;
        }

        /// <summary>
        /// 获取一个值，指示客户端是否已成功连接到远程设备。
        /// </summary>
        public override bool IsConnected
        {
            get { return _transport.IsConnected; }
        }

        /// <summary>
        /// 建立与远程设备的 TCP 连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.ConnectAsync(cancellationToken);
        }

        /// <summary>
        /// 断开与远程设备的 TCP 连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.DisconnectAsync(cancellationToken);
        }

        /// <summary>
        /// 从远程设备读取数据。实际读取逻辑委托给 <see cref="ISocketProtocolAdapter.ReadAsync"/>。
        /// </summary>
        /// <param name="request">读取请求，包含地址等参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>包含读取结果的数据值。</returns>
        protected override Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            return _adapter.ReadAsync(_transport, request, cancellationToken);
        }

        /// <summary>
        /// 向远程设备写入数据。实际写入逻辑委托给 <see cref="ISocketProtocolAdapter.WriteAsync"/>。
        /// </summary>
        /// <param name="request">写入请求，包含地址和待写入的值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            return _adapter.WriteAsync(_transport, request, cancellationToken);
        }

        /// <summary>
        /// 释放客户端占用的资源（关闭 TCP 传输层连接）。
        /// </summary>
        protected override void DisposeCore()
        {
            _transport.Dispose();
        }
    }

    /// <summary>
    /// TCP Socket 服务器主机。监听指定 IP 地址和端口，管理客户端会话连接。
    /// 提供会话连接、断开和数据接收的事件通知。
    /// </summary>
    public sealed class TcpSocketServerHost : IDisposable
    {
        private readonly TcpTransportServer _server;

        /// <summary>
        /// 初始化 <see cref="TcpSocketServerHost"/> 的新实例。
        /// </summary>
        /// <param name="address">要绑定的本地 IP 地址。</param>
        /// <param name="port">要绑定的本地端口号。</param>
        public TcpSocketServerHost(IPAddress address, int port)
        {
            _server = new TcpTransportServer(address, port);
        }

        /// <summary>
        /// 当有新的客户端会话连接时触发。
        /// </summary>
        public event EventHandler<TransportSessionEventArgs> SessionConnected
        {
            add { _server.SessionConnected += value; }
            remove { _server.SessionConnected -= value; }
        }

        /// <summary>
        /// 当客户端会话关闭时触发。
        /// </summary>
        public event EventHandler<TransportSessionEventArgs> SessionClosed
        {
            add { _server.SessionClosed += value; }
            remove { _server.SessionClosed -= value; }
        }

        /// <summary>
        /// 当收到客户端会话的数据时触发。
        /// </summary>
        public event EventHandler<TransportDataReceivedEventArgs> DataReceived
        {
            add { _server.DataReceived += value; }
            remove { _server.DataReceived -= value; }
        }

        /// <summary>
        /// 启动 TCP 服务器，开始监听客户端连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _server.StartAsync(cancellationToken);
        }

        /// <summary>
        /// 停止 TCP 服务器，关闭所有客户端会话并停止监听。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _server.StopAsync(cancellationToken);
        }

        /// <summary>
        /// 释放服务器占用的资源，停止监听并关闭所有会话。
        /// </summary>
        public void Dispose()
        {
            _server.Dispose();
        }
    }
}
