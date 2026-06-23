using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommDemo.SocketDebug
{
    /// <summary>
    /// 基于行的 TCP 客户端。以 <c>\r\n</c> 作为行结束符，支持异步连接、断开、发送和接收操作，
    /// 接收到的数据以行为单位通过 <see cref="MessageReceived"/> 事件对外通知。
    /// 实现 <see cref="IDisposable"/> 接口以释放底层资源。
    /// </summary>
    internal sealed class LineBasedTcpClient : IDisposable
    {
        /// <summary>
        /// 发送操作专用的信号量锁，保证同一时间只有一个发送操作进行。
        /// </summary>
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 接收字节缓冲区，用于暂存尚未拼成完整行的数据。
        /// </summary>
        private readonly List<byte> _pendingBytes = new List<byte>();

        /// <summary>
        /// 底层的 <see cref="TcpClient"/> 实例。
        /// </summary>
        private TcpClient _client;

        /// <summary>
        /// 与远程终结点关联的 <see cref="NetworkStream"/>，用于读写数据。
        /// </summary>
        private NetworkStream _stream;

        /// <summary>
        /// 取消令牌源，用于取消正在进行的接收循环等异步操作。
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// 后台接收循环任务。
        /// </summary>
        private Task _receiveLoopTask;

        /// <summary>
        /// 获取一个值，指示当前客户端是否已成功连接到远程终结点并处于可用状态。
        /// 通过检查底层 Socket 的轮询结果来判断连接是否真正存活。
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
        /// 获取远程终结点的字符串表示形式（IP:Port）。
        /// 如果客户端未连接或远程终结点不可用，则返回空字符串。
        /// </summary>
        public string RemoteEndPoint
        {
            get { return _client?.Client?.RemoteEndPoint == null ? string.Empty : _client.Client.RemoteEndPoint.ToString(); }
        }

        /// <summary>
        /// 在客户端成功连接到远程终结点时引发。
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// 在客户端与远程终结点断开连接时引发。
        /// </summary>
        public event EventHandler Disconnected;

        /// <summary>
        /// 在接收到一行完整的文本消息（以 <c>\r\n</c> 结尾）时引发。
        /// 事件参数为 <see cref="SocketTextMessageEventArgs"/>，包含消息内容及远程终结点信息。
        /// </summary>
        public event EventHandler<SocketTextMessageEventArgs> MessageReceived;

        /// <summary>
        /// 异步连接到指定的远程主机和端口。
        /// 内置 3 秒连接超时机制。连接成功后，启动后台接收循环。
        /// </summary>
        /// <param name="host">目标主机名或 IP 地址。</param>
        /// <param name="port">目标端口号。</param>
        /// <param name="cancellationToken">用于取消连接操作的取消令牌。</param>
        /// <returns>表示异步连接操作的任务。</returns>
        /// <exception cref="TimeoutException">连接操作在 3 秒内未完成时引发。</exception>
        /// <exception cref="OperationCanceledException">连接操作被 <paramref name="cancellationToken"/> 取消时引发。</exception>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

            var client = new TcpClient();
            client.NoDelay = true;

            try
            {
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(3000, cancellationToken);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completedTask != connectTask)
                {
                    client.Close();
                    throw new TimeoutException("TCP client connect timeout.");
                }

                await connectTask.ConfigureAwait(false);
                _client = client;
                _stream = client.GetStream();
                _cts = new CancellationTokenSource();
                _pendingBytes.Clear();
                _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                client.Close();
                throw;
            }
        }

        /// <summary>
        /// 异步断开与远程终结点的连接。
        /// 取消正在进行的接收循环，关闭底层客户端，并等待接收循环任务完成。
        /// </summary>
        /// <param name="cancellationToken">用于取消断开操作的取消令牌。</param>
        /// <returns>表示异步断开操作的任务。</returns>
        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            var source = _cts;
            _cts = null;

            if (source != null)
            {
                source.Cancel();
            }

            CloseClient();

            if (_receiveLoopTask != null)
            {
                try
                {
                    await _receiveLoopTask.ConfigureAwait(false);
                }
                catch
                {
                    // 忽略接收循环任务中的异常
                }
                finally
                {
                    _receiveLoopTask = null;
                }
            }

            if (source != null)
            {
                source.Dispose();
            }
        }

        /// <summary>
        /// 异步发送一行文本消息到远程终结点。发送时自动在消息末尾追加 <c>\r\n</c> 行结束符。
        /// 通过 <see cref="_sendLock"/> 保证同一时间只有一个发送操作。
        /// </summary>
        /// <param name="message">要发送的文本消息。若为 null 则发送空字符串。</param>
        /// <param name="cancellationToken">用于取消发送操作的取消令牌。</param>
        /// <returns>表示异步发送操作的任务。</returns>
        /// <exception cref="InvalidOperationException">客户端未连接时引发。</exception>
        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("TCP client is not connected.");
            }

            var payload = Encoding.UTF8.GetBytes((message ?? string.Empty) + "\r\n");

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 释放当前实例占用的所有资源。同步调用 <see cref="DisconnectAsync"/> 并释放发送信号量。
        /// </summary>
        public void Dispose()
        {
            DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
            _sendLock.Dispose();
        }

        /// <summary>
        /// 后台接收循环。持续从网络流中读取数据，并将数据交给 <see cref="AppendAndDispatch"/> 进行行解析。
        /// 当流关闭、读取返回 0 或取消请求时退出循环。
        /// 退出后关闭客户端并触发 <see cref="Disconnected"/> 事件。
        /// </summary>
        /// <param name="cancellationToken">用于取消接收循环的取消令牌。</param>
        /// <returns>表示异步接收循环的任务。</returns>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }

                    if (read <= 0)
                    {
                        break;
                    }

                    AppendAndDispatch(buffer, read);
                }
            }
            finally
            {
                CloseClient();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 将接收到的字节追加到 <see cref="_pendingBytes"/> 缓冲区，并尝试从中提取完整的行。
        /// 找到 <c>\r\n</c> 分隔符时，提取该行数据并通过 <see cref="MessageReceived"/> 事件通知。
        /// 重复此过程直到缓冲区中不再包含完整的行。
        /// </summary>
        /// <param name="buffer">从网络流读取到的字节数组。</param>
        /// <param name="count">本次读取的有效字节数。</param>
        private void AppendAndDispatch(byte[] buffer, int count)
        {
            for (var index = 0; index < count; index++)
            {
                _pendingBytes.Add(buffer[index]);
            }

            while (true)
            {
                var lineEndIndex = FindLineEnding(_pendingBytes);
                if (lineEndIndex < 0)
                {
                    return;
                }

                var lineBytes = _pendingBytes.GetRange(0, lineEndIndex).ToArray();
                _pendingBytes.RemoveRange(0, lineEndIndex + 2);
                MessageReceived?.Invoke(this, new SocketTextMessageEventArgs(Guid.Empty, RemoteEndPoint, Encoding.UTF8.GetString(lineBytes)));
            }
        }

        /// <summary>
        /// 关闭并清理底层的 <see cref="NetworkStream"/> 和 <see cref="TcpClient"/> 实例。
        /// 每个对象释放时捕获并忽略所有异常。
        /// </summary>
        private void CloseClient()
        {
            if (_stream != null)
            {
                try
                {
                    _stream.Dispose();
                }
                catch
                {
                }

                _stream = null;
            }

            if (_client != null)
            {
                try
                {
                    _client.Close();
                }
                catch
                {
                }

                _client = null;
            }
        }

        /// <summary>
        /// 在指定的字节列表中查找 <c>\r\n</c>（回车换行）行结束符的位置。
        /// </summary>
        /// <param name="buffer">要搜索的字节列表。</param>
        /// <returns>找到的行结束符起始索引（'\r' 的位置）；如果未找到则返回 -1。</returns>
        private static int FindLineEnding(List<byte> buffer)
        {
            for (var index = 0; index < buffer.Count - 1; index++)
            {
                if (buffer[index] == '\r' && buffer[index + 1] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
