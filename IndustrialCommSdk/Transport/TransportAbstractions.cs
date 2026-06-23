using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Transport
{
    /// <summary>
    /// TCP 传输选项。提供用于配置 TCP 客户端连接行为的参数，包括主机地址、端口号以及各种超时设置。
    /// </summary>
    public sealed class TcpTransportOptions
    {
        /// <summary>
        /// 获取或设置远程主机的 IP 地址或主机名。
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 获取或设置远程主机的端口号。
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 获取或设置连接超时时间（毫秒）。默认值为 3000 毫秒。
        /// </summary>
        public int ConnectTimeoutMilliseconds { get; set; } = 3000;

        /// <summary>
        /// 获取或设置发送超时时间（毫秒）。默认值为 3000 毫秒。
        /// </summary>
        public int SendTimeoutMilliseconds { get; set; } = 3000;

        /// <summary>
        /// 获取或设置接收超时时间（毫秒）。默认值为 5000 毫秒。
        /// </summary>
        public int ReceiveTimeoutMilliseconds { get; set; } = 5000;

        /// <summary>
        /// 获取或设置一个值，该值指示在连接断开时是否自动重新连接。默认值为 <c>true</c>。
        /// </summary>
        public bool AutoReconnect { get; set; } = true;
    }

    /// <summary>
    /// 传输客户端接口。定义与远程端点进行 TCP 通信的基本操作，包括连接、断开、发送和接收数据。
    /// </summary>
    public interface ITransportClient : IDisposable
    {
        /// <summary>
        /// 获取一个值，该值指示客户端当前是否已连接到远程端点。
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 获取远程端点的网络地址。
        /// </summary>
        EndPoint RemoteEndPoint { get; }

        /// <summary>
        /// 异步连接到远程端点。
        /// </summary>
        /// <param name="cancellationToken">用于取消连接操作的取消令牌。</param>
        /// <returns>表示异步连接操作的任务。</returns>
        Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 异步断开与远程端点的连接。
        /// </summary>
        /// <param name="cancellationToken">用于取消断开操作的取消令牌。</param>
        /// <returns>表示异步断开操作的任务。</returns>
        Task DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 异步向远程端点发送数据负载。
        /// </summary>
        /// <param name="payload">要发送的二进制数据负载。</param>
        /// <param name="cancellationToken">用于取消发送操作的取消令牌。</param>
        /// <returns>表示异步发送操作的任务。</returns>
        Task SendAsync(byte[] payload, CancellationToken cancellationToken);

        /// <summary>
        /// 异步从远程端点接收指定长度的数据。
        /// </summary>
        /// <param name="length">要接收的确切字节数。</param>
        /// <param name="cancellationToken">用于取消接收操作的取消令牌。</param>
        /// <returns>表示异步接收操作的任务，结果包含接收到的字节数组。</returns>
        Task<byte[]> ReceiveExactAsync(int length, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 传输服务器接口。定义 TCP 服务器的基本操作，包括启动、停止监听以及会话连接、关闭和数据接收事件。
    /// </summary>
    public interface ITransportServer : IDisposable
    {
        /// <summary>
        /// 获取一个值，该值指示服务器当前是否正在运行并接受客户端连接。
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 当有新客户端会话建立连接时触发。
        /// </summary>
        event EventHandler<TransportSessionEventArgs> SessionConnected;

        /// <summary>
        /// 当已有客户端会话关闭连接时触发。
        /// </summary>
        event EventHandler<TransportSessionEventArgs> SessionClosed;

        /// <summary>
        /// 当从某个客户端会话接收到数据时触发。
        /// </summary>
        event EventHandler<TransportDataReceivedEventArgs> DataReceived;

        /// <summary>
        /// 异步启动服务器，开始监听并接受客户端连接。
        /// </summary>
        /// <param name="cancellationToken">用于取消启动操作的取消令牌。</param>
        /// <returns>表示异步启动操作的任务。</returns>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 异步停止服务器，关闭监听并释放所有客户端会话资源。
        /// </summary>
        /// <param name="cancellationToken">用于取消停止操作的取消令牌。</param>
        /// <returns>表示异步停止操作的任务。</returns>
        Task StopAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 传输会话事件参数。包含与传输会话关联的事件数据，用于标识发生事件的会话实例。
    /// </summary>
    public sealed class TransportSessionEventArgs : EventArgs
    {
        /// <summary>
        /// 使用指定的会话实例初始化 <see cref="TransportSessionEventArgs"/> 类的新实例。
        /// </summary>
        /// <param name="session">与此事件关联的 TCP 传输会话。</param>
        public TransportSessionEventArgs(TcpTransportSession session)
        {
            Session = session;
        }

        /// <summary>
        /// 获取与此事件关联的传输会话实例。
        /// </summary>
        public TcpTransportSession Session { get; private set; }
    }

    /// <summary>
    /// 传输数据接收事件参数。包含从传输会话接收数据的相关事件数据，包括发送数据的会话和接收到的负载。
    /// </summary>
    public sealed class TransportDataReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// 使用指定的会话和负载数据初始化 <see cref="TransportDataReceivedEventArgs"/> 类的新实例。
        /// </summary>
        /// <param name="session">发送数据的传输会话。</param>
        /// <param name="payload">接收到的二进制数据负载。</param>
        public TransportDataReceivedEventArgs(TcpTransportSession session, byte[] payload)
        {
            Session = session;
            Payload = payload;
        }

        /// <summary>
        /// 获取发送数据的传输会话实例。
        /// </summary>
        public TcpTransportSession Session { get; private set; }

        /// <summary>
        /// 获取接收到的二进制数据负载。
        /// </summary>
        public byte[] Payload { get; private set; }
    }
}
