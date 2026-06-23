using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommDemo.SocketDebug
{
    /// <summary>
    /// 基于行的 TCP 服务端。以 <c>\r\n</c> 作为行结束符，支持多个客户端会话的并发管理，
    /// 提供广播、单播发送以及客户端连接/断开事件通知。
    /// 实现 <see cref="IDisposable"/> 接口以释放底层资源。
    /// </summary>
    internal sealed class LineBasedTcpServer : IDisposable
    {
        /// <summary>
        /// 当前所有活跃会话的字典，以会话 GUID 为键，<see cref="ServerSession"/> 实例为值。
        /// </summary>
        private readonly ConcurrentDictionary<Guid, ServerSession> _sessions = new ConcurrentDictionary<Guid, ServerSession>();

        /// <summary>
        /// 底层的 <see cref="TcpListener"/>，用于监听传入的 TCP 连接。
        /// </summary>
        private TcpListener _listener;

        /// <summary>
        /// 取消令牌源，用于取消接受循环等异步操作。
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// 后台接受客户端连接循环任务。
        /// </summary>
        private Task _acceptLoopTask;

        /// <summary>
        /// 获取一个值，指示服务端是否正在运行（监听中）。
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 获取当前连接的客户端会话数量。
        /// </summary>
        public int SessionCount
        {
            get { return _sessions.Count; }
        }

        /// <summary>
        /// 在有新客户端成功连接时引发。
        /// 事件参数为 <see cref="SocketSessionEventArgs"/>，包含新会话的 ID、远程终结点和当前总会话数。
        /// </summary>
        public event EventHandler<SocketSessionEventArgs> ClientConnected;

        /// <summary>
        /// 在客户端断开连接时引发。
        /// 事件参数为 <see cref="SocketSessionEventArgs"/>，包含已断开会话的 ID、远程终结点和当前剩余会话数。
        /// </summary>
        public event EventHandler<SocketSessionEventArgs> ClientDisconnected;

        /// <summary>
        /// 在从任意客户端接收到一行完整的文本消息（以 <c>\r\n</c> 结尾）时引发。
        /// 事件参数为 <see cref="SocketTextMessageEventArgs"/>，包含消息内容及来源会话的信息。
        /// </summary>
        public event EventHandler<SocketTextMessageEventArgs> MessageReceived;

        /// <summary>
        /// 异步启动 TCP 服务端，在指定的 IP 地址和端口上开始监听连接。
        /// 启动后后台运行接受客户端连接的循环。
        /// </summary>
        /// <param name="address">要绑定的本地 IP 地址。</param>
        /// <param name="port">要监听的本地端口号。</param>
        /// <param name="cancellationToken">用于取消启动操作的取消令牌。</param>
        /// <returns>表示异步启动操作的任务。</returns>
        public Task StartAsync(IPAddress address, int port, CancellationToken cancellationToken)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(address, port);
            _listener.Start();
            IsRunning = true;
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 异步停止 TCP 服务端。停止监听、取消所有正在进行的异步操作、
        /// 关闭并清理所有客户端会话以及监听器。
        /// </summary>
        /// <param name="cancellationToken">用于取消停止操作的取消令牌。</param>
        /// <returns>表示异步停止操作的任务。</returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;

            var linkedSource = _cts;
            _cts = null;

            if (linkedSource != null)
            {
                linkedSource.Cancel();
            }

            try
            {
                _listener.Stop();
            }
            catch
            {
            }

            var sessions = new List<ServerSession>(_sessions.Values);
            foreach (var session in sessions)
            {
                session.Dispose();
            }

            _sessions.Clear();

            if (_acceptLoopTask != null)
            {
                try
                {
                    await _acceptLoopTask.ConfigureAwait(false);
                }
                catch
                {
                }
                finally
                {
                    _acceptLoopTask = null;
                }
            }

            if (linkedSource != null)
            {
                linkedSource.Dispose();
            }

            _listener = null;
        }

        /// <summary>
        /// 异步将指定的消息广播给所有已连接的客户端。
        /// 如果消息为空或当前没有活跃会话，则直接返回。
        /// 每个客户端发送独立并行执行。
        /// </summary>
        /// <param name="message">要广播的文本消息。不能为 null 或空白。</param>
        /// <param name="cancellationToken">用于取消广播操作的取消令牌。</param>
        /// <returns>表示异步广播操作的任务。</returns>
        public async Task BroadcastAsync(string message, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message) || _sessions.IsEmpty)
            {
                return;
            }

            var payload = EncodeLine(message);
            var tasks = new List<Task>();
            foreach (var session in _sessions.Values)
            {
                tasks.Add(session.SendAsync(payload, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步向指定会话 ID 的客户端发送一条消息。
        /// 如果指定的会话不存在或消息为空，则直接返回。
        /// </summary>
        /// <param name="sessionId">目标客户端会话的唯一标识（GUID）。</param>
        /// <param name="message">要发送的文本消息。</param>
        /// <param name="cancellationToken">用于取消发送操作的取消令牌。</param>
        /// <returns>表示异步发送操作的任务。</returns>
        public Task SendToAsync(Guid sessionId, string message, CancellationToken cancellationToken)
        {
            ServerSession session;
            if (!_sessions.TryGetValue(sessionId, out session) || string.IsNullOrWhiteSpace(message))
            {
                return Task.CompletedTask;
            }

            return session.SendAsync(EncodeLine(message), cancellationToken);
        }

        /// <summary>
        /// 释放当前实例占用的所有资源。同步调用 <see cref="StopAsync"/>。
        /// </summary>
        public void Dispose()
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 后台接受客户端连接循环。持续等待客户端连接，每收到一个新连接就创建一个 <see cref="ServerSession"/> 实例，
        /// 将其加入到会话字典中，并触发 <see cref="ClientConnected"/> 事件。
        /// 然后启动一个后台任务运行该会话的接收循环。
        /// 如果因监听器停止或取消令牌触发而退出循环，则正常结束。
        /// </summary>
        /// <param name="cancellationToken">用于取消接受循环的取消令牌。</param>
        /// <returns>表示异步接受循环的任务。</returns>
        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    continue;
                }

                var session = new ServerSession(client);
                if (_sessions.TryAdd(session.SessionId, session))
                {
                    ClientConnected?.Invoke(this, new SocketSessionEventArgs(session.SessionId, session.RemoteEndPoint, SessionCount));
                    _ = Task.Run(() => RunSessionAsync(session, cancellationToken), cancellationToken);
                }
                else
                {
                    session.Dispose();
                }
            }
        }

        /// <summary>
        /// 运行单个客户端会话的接收循环。当接收循环结束（客户端断开或出错）时，
        /// 将会话从字典中移除，释放会话资源，并触发 <see cref="ClientDisconnected"/> 事件。
        /// </summary>
        /// <param name="session">要运行的 <see cref="ServerSession"/> 实例。</param>
        /// <param name="cancellationToken">用于取消接收操作的取消令牌。</param>
        /// <returns>表示异步会话运行任务。</returns>
        private async Task RunSessionAsync(ServerSession session, CancellationToken cancellationToken)
        {
            try
            {
                await session.ReceiveLoopAsync(
                    (currentSession, message) => MessageReceived?.Invoke(this, new SocketTextMessageEventArgs(currentSession.SessionId, currentSession.RemoteEndPoint, message)),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ServerSession removed;
                _sessions.TryRemove(session.SessionId, out removed);
                session.Dispose();
                ClientDisconnected?.Invoke(this, new SocketSessionEventArgs(session.SessionId, session.RemoteEndPoint, SessionCount));
            }
        }

        /// <summary>
        /// 将文本消息编码为 UTF-8 字节数组，并追加 <c>\r\n</c> 行结束符。
        /// </summary>
        /// <param name="message">要编码的文本消息。若为 null 则编码空字符串。</param>
        /// <returns>编码后的字节数组，末尾包含 <c>\r\n</c>。</returns>
        private static byte[] EncodeLine(string message)
        {
            return Encoding.UTF8.GetBytes((message ?? string.Empty) + "\r\n");
        }

        /// <summary>
        /// 表示服务端中的一个客户端会话。封装了单个 TCP 连接，负责从该连接接收数据、
        /// 向该连接发送数据，以及管理接收缓冲区和行解析逻辑。
        /// </summary>
        private sealed class ServerSession : IDisposable
        {
            /// <summary>
            /// 底层 TCP 客户端实例。
            /// </summary>
            private readonly TcpClient _client;

            /// <summary>
            /// 与该客户端关联的网络流。
            /// </summary>
            private readonly NetworkStream _stream;

            /// <summary>
            /// 发送操作专用的信号量锁，保证同一时间只有一个发送操作进行。
            /// </summary>
            private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

            /// <summary>
            /// 接收字节缓冲区，用于暂存尚未拼成完整行的数据。
            /// </summary>
            private readonly List<byte> _pendingBytes = new List<byte>();

            /// <summary>
            /// 初始化 <see cref="ServerSession"/> 类的新实例。
            /// </summary>
            /// <param name="client">已接受的 <see cref="TcpClient"/> 实例。不能为 null。</param>
            /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时引发。</exception>
            public ServerSession(TcpClient client)
            {
                _client = client ?? throw new ArgumentNullException(nameof(client));
                _client.NoDelay = true;
                _stream = _client.GetStream();
                SessionId = Guid.NewGuid();
            }

            /// <summary>
            /// 获取当前会话的唯一标识（GUID）。
            /// </summary>
            public Guid SessionId { get; private set; }

            /// <summary>
            /// 获取远程终结点的字符串表示形式。如果不可用则返回 "(unknown)"。
            /// </summary>
            public string RemoteEndPoint
            {
                get { return _client.Client.RemoteEndPoint == null ? "(unknown)" : _client.Client.RemoteEndPoint.ToString(); }
            }

            /// <summary>
            /// 后台接收循环。持续从网络流中读取数据，并通过回调委托将解析出的完整行消息传递给调用方。
            /// 当流关闭、读取返回 0 或取消请求时退出循环。
            /// </summary>
            /// <param name="onMessage">接收到一行完整消息时的回调委托。参数为当前会话和消息文本。</param>
            /// <param name="cancellationToken">用于取消接收循环的取消令牌。</param>
            /// <returns>表示异步接收循环的任务。</returns>
            public async Task ReceiveLoopAsync(Action<ServerSession, string> onMessage, CancellationToken cancellationToken)
            {
                var buffer = new byte[4096];
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

                    AppendAndDispatch(buffer, read, onMessage);
                }
            }

            /// <summary>
            /// 异步将编码后的字节数据发送给客户端。
            /// 通过 <see cref="_sendLock"/> 保证同一时间只有一个发送操作。
            /// </summary>
            /// <param name="payload">要发送的已编码字节数组（包含行结束符）。</param>
            /// <param name="cancellationToken">用于取消发送操作的取消令牌。</param>
            /// <returns>表示异步发送操作的任务。</returns>
            public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
            {
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
            /// 释放当前会话占用的所有资源，包括网络流、TCP 客户端和发送信号量。
            /// 每个对象的释放都捕获并忽略所有异常。
            /// </summary>
            public void Dispose()
            {
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

                _sendLock.Dispose();
            }

            /// <summary>
            /// 将接收到的字节追加到 <see cref="_pendingBytes"/> 缓冲区，并尝试从中提取完整的行。
            /// 找到 <c>\r\n</c> 分隔符时，提取该行数据并通过 <paramref name="onMessage"/> 回调通知。
            /// 重复此过程直到缓冲区中不再包含完整的行。
            /// </summary>
            /// <param name="buffer">从网络流读取到的字节数组。</param>
            /// <param name="count">本次读取的有效字节数。</param>
            /// <param name="onMessage">接收到一行完整消息时的回调委托。</param>
            private void AppendAndDispatch(byte[] buffer, int count, Action<ServerSession, string> onMessage)
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
                    onMessage?.Invoke(this, Encoding.UTF8.GetString(lineBytes));
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
}
