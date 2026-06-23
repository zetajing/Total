using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Transport
{
    /// <summary>
    /// TCP 传输会话。代表服务器与一个客户端之间建立的单一 TCP 连接会话，提供数据收发、会话生命周期管理以及事件通知功能。
    /// 每个会话拥有唯一的标识符，并运行独立的数据接收循环。
    /// </summary>
    public sealed class TcpTransportSession : IDisposable
    {
        /// <summary>
        /// 底层的 TCP 客户端实例，代表与客户端的网络连接。
        /// </summary>
        private readonly TcpClient _client;

        /// <summary>
        /// 用于控制会话生命周期和取消数据接收循环的取消令牌源。
        /// </summary>
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// 与 TCP 客户端关联的网络流，用于读写数据。
        /// </summary>
        private readonly NetworkStream _stream;

        /// <summary>
        /// 使用指定的 TCP 客户端初始化 <see cref="TcpTransportSession"/> 类的新实例。
        /// 自动获取网络流并生成唯一的会话标识符。
        /// </summary>
        /// <param name="client">已连接的 TCP 客户端实例。不能为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 <c>null</c> 时抛出。</exception>
        public TcpTransportSession(TcpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _stream = client.GetStream();
            SessionId = Guid.NewGuid();
        }

        /// <summary>
        /// 获取当前会话的唯一标识符。
        /// </summary>
        public Guid SessionId { get; private set; }

        /// <summary>
        /// 获取一个值，该值指示底层 TCP 客户端当前是否处于连接状态。
        /// </summary>
        public bool IsConnected { get { return _client.Connected; } }

        /// <summary>
        /// 当从对端接收到数据时触发。事件参数包含接收到的原始字节数据。
        /// </summary>
        public event EventHandler<byte[]> DataReceived;

        /// <summary>
        /// 当会话因连接关闭或发生错误而终止时触发。
        /// </summary>
        public event EventHandler Closed;

        /// <summary>
        /// 启动会话的数据接收循环。在后台任务中持续读取网络流中的数据并触发 <see cref="DataReceived"/> 事件。
        /// </summary>
        public void Start()
        {
            Task.Run(ReceiveLoopAsync);
        }

        /// <summary>
        /// 异步向对端发送数据负载。将数据写入网络流并刷新缓冲区。
        /// </summary>
        /// <param name="payload">要发送的二进制数据负载。</param>
        /// <param name="cancellationToken">用于取消发送操作的取消令牌。</param>
        /// <returns>表示异步发送操作的任务。</returns>
        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            await _stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步接收循环。持续从网络流中读取数据，每次读取最多 4096 字节，
        /// 将读取到的数据复制到新数组中并触发 <see cref="DataReceived"/> 事件。
        /// 当对端关闭连接或取消令牌被触发时退出循环，并在 finally 块中触发 <see cref="Closed"/> 事件。
        /// </summary>
        /// <returns>表示异步接收循环的任务。</returns>
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[4096];
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var read = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    var data = new byte[read];
                    Buffer.BlockCopy(buffer, 0, data, 0, read);
                    DataReceived?.Invoke(this, data);
                }
            }
            catch
            {
            }
            finally
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 释放会话使用的所有资源。取消接收循环，依次释放网络流和 TCP 客户端，最后释放取消令牌源。
        /// 每个资源的释放都包含异常保护，确保一个资源的释放失败不会影响其他资源的释放。
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _stream.Dispose();
            }
            catch
            {
            }

            try
            {
                _client.Close();
            }
            catch
            {
            }
            _cts.Dispose();
        }
    }
}
