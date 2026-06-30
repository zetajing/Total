using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Transport
{
    /// <summary>
    /// TCP 传输客户端。实现 <see cref="ITransportClient"/> 接口，提供基于 TCP 协议的客户端连接、断开、发送和接收数据的完整功能。
    /// 支持自动重连、连接超时和并发访问控制。
    /// </summary>
    public sealed class TcpTransportClient : ITransportClient
    {
        /// <summary>
        /// TCP 传输选项，包含主机地址、端口号及各种超时设置。
        /// </summary>
        private readonly TcpTransportOptions _options;

        /// <summary>
        /// 用于控制并发访问的信号量，确保同一时刻只有一个操作在执行。
        /// </summary>
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 底层的 TCP 客户端实例。
        /// </summary>
        private TcpClient _client;

        /// <summary>
        /// 与 TCP 客户端关联的网络流，用于发送和接收数据。
        /// </summary>
        private NetworkStream _stream;

        /// <summary>
        /// 使用指定的传输选项初始化 <see cref="TcpTransportClient"/> 类的新实例。
        /// </summary>
        /// <param name="options">TCP 传输选项，包含连接和超时配置。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 <c>null</c> 时抛出。</exception>
        public TcpTransportClient(TcpTransportOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (_options.ConnectTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Connect timeout must be greater than zero.");
            if (_options.SendTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Send timeout must be greater than zero.");
            if (_options.ReceiveTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Receive timeout must be greater than zero.");
        }

        /// <summary>
        /// 获取一个值，该值指示客户端当前是否已连接到远程端点。
        /// 通过检查底层 TCP 连接的轮询状态来判断连接的实时可用性。
        /// </summary>
        public bool IsConnected
        {
            get
            {
                var client = _client;
                if (client == null || !client.Connected)
                {
                    return false;
                }

                try
                {
                    return !(client.Client.Poll(1, SelectMode.SelectRead) && client.Client.Available == 0);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取远程端点的网络地址。如果客户端未连接，则返回 <c>null</c>。
        /// </summary>
        public EndPoint RemoteEndPoint
        {
            get { return _client == null ? null : _client.Client.RemoteEndPoint; }
        }

        /// <summary>
        /// 异步连接到远程端点。如果当前已连接，则直接返回。
        /// 连接操作受配置的超时时间限制，超时后将抛出 <see cref="IndustrialTimeoutException"/>。
        /// </summary>
        /// <param name="cancellationToken">用于取消连接操作的取消令牌。</param>
        /// <returns>表示异步连接操作的任务。</returns>
        /// <exception cref="IndustrialTimeoutException">连接操作超时时抛出。</exception>
        /// <exception cref="IndustrialConnectionException">TCP 连接失败时抛出，内部包含原始的 <see cref="SocketException"/>。</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    return;
                }

                DisposeClient();
                _client = new TcpClient();
                _client.SendTimeout = _options.SendTimeoutMilliseconds;
                _client.ReceiveTimeout = _options.ReceiveTimeoutMilliseconds;

                var connectTask = _client.ConnectAsync(_options.Host, _options.Port);
                var timeoutTask = Task.Delay(_options.ConnectTimeoutMilliseconds, cancellationToken);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completedTask != connectTask)
                {
                    DisposeClient();
                    try
                    {
                        await connectTask.ConfigureAwait(false);
                    }
                    catch
                    {
                        // 关闭尚在连接的套接字会使连接任务失败；下面保留原始取消或超时语义。
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IndustrialTimeoutException("TCP connect timeout.");
                }

                await connectTask.ConfigureAwait(false);
                _stream = _client.GetStream();
            }
            catch (SocketException ex)
            {
                throw new IndustrialConnectionException("TCP connect failed.", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 异步断开与远程端点的连接。释放底层 TCP 客户端和网络流资源。
        /// </summary>
        /// <param name="cancellationToken">用于取消断开操作的取消令牌。</param>
        /// <returns>表示异步断开操作的任务。</returns>
        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DisposeClient();
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 异步向远程端点发送数据负载。发送前会自动确保连接处于可用状态。
        /// </summary>
        /// <param name="payload">要发送的二进制数据负载。不能为 <c>null</c>。</param>
        /// <param name="cancellationToken">用于取消发送操作的取消令牌。</param>
        /// <returns>表示异步发送操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="payload"/> 为 <c>null</c> 时抛出。</exception>
        /// <exception cref="IndustrialConnectionException">客户端未连接且自动重连失败时抛出。</exception>
        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            await AwaitIoAsync(
                _stream.WriteAsync(payload, 0, payload.Length, cancellationToken),
                _options.SendTimeoutMilliseconds,
                "TCP send timeout.",
                cancellationToken).ConfigureAwait(false);
            await AwaitIoAsync(
                _stream.FlushAsync(cancellationToken),
                _options.SendTimeoutMilliseconds,
                "TCP flush timeout.",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步从远程端点接收指定长度的数据。如果在接收过程中连接被对端关闭，则抛出 <see cref="IndustrialConnectionException"/>。
        /// </summary>
        /// <param name="length">要接收的确切字节数。不能为负数。</param>
        /// <param name="cancellationToken">用于取消接收操作的取消令牌。</param>
        /// <returns>表示异步接收操作的任务，结果包含接收到的字节数组。</returns>
        /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="length"/> 为负数时抛出。</exception>
        /// <exception cref="IndustrialConnectionException">客户端未连接、自动重连失败或远程对端关闭连接时抛出。</exception>
        public async Task<byte[]> ReceiveExactAsync(int length, CancellationToken cancellationToken)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await AwaitIoAsync(
                    _stream.ReadAsync(buffer, offset, length - offset, cancellationToken),
                    _options.ReceiveTimeoutMilliseconds,
                    "TCP receive timeout.",
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    DisposeClient();
                    throw new IndustrialConnectionException("Remote peer closed the connection.");
                }
                offset += read;
            }
            return buffer;
        }

        private async Task AwaitIoAsync(Task operation, int timeoutMilliseconds, string timeoutMessage, CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(timeoutMilliseconds, cancellationToken);
            var completed = await Task.WhenAny(operation, timeoutTask).ConfigureAwait(false);
            if (completed == operation)
            {
                await operation.ConfigureAwait(false);
                return;
            }

            DisposeClient();
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch
            {
                // 超时后关闭连接会使挂起的异步 I/O 以异常结束；对外统一报告超时。
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new IndustrialTimeoutException(timeoutMessage);
        }

        private async Task<T> AwaitIoAsync<T>(Task<T> operation, int timeoutMilliseconds, string timeoutMessage, CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(timeoutMilliseconds, cancellationToken);
            var completed = await Task.WhenAny(operation, timeoutTask).ConfigureAwait(false);
            if (completed == operation)
            {
                return await operation.ConfigureAwait(false);
            }

            DisposeClient();
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch
            {
                // 超时后关闭连接会使挂起的异步 I/O 以异常结束；对外统一报告超时。
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new IndustrialTimeoutException(timeoutMessage);
        }

        /// <summary>
        /// 确保客户端处于连接状态。如果已连接则直接返回；否则根据 <see cref="TcpTransportOptions.AutoReconnect"/> 配置决定是否自动重新连接。
        /// </summary>
        /// <param name="cancellationToken">用于取消连接操作的取消令牌。</param>
        /// <returns>表示异步确保连接操作的任务。</returns>
        /// <exception cref="IndustrialConnectionException">客户端未连接且自动重连已禁用时抛出。</exception>
        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (IsConnected)
            {
                return;
            }

            if (!_options.AutoReconnect)
            {
                throw new IndustrialConnectionException("TCP client is not connected.");
            }

            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 释放底层 TCP 客户端和网络流资源。将网络流和客户端对象置为 <c>null</c>。
        /// </summary>
        private void DisposeClient()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_client != null)
            {
                _client.Close();
                _client = null;
            }
        }

        /// <summary>
        /// 释放客户端使用的所有托管资源，包括 TCP 客户端、网络流和并发信号量。
        /// </summary>
        public void Dispose()
        {
            DisposeClient();
            _gate.Dispose();
        }
    }
}
