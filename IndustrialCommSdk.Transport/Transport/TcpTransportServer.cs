using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Transport
{
    /// <summary>
    /// TCP 传输服务器。实现 <see cref="ITransportServer"/> 接口，提供基于 TCP 协议的服务器功能，
    /// 包括启动监听、接受客户端连接、管理会话生命周期以及转发接收到的数据。
    /// </summary>
    public sealed class TcpTransportServer : IAsyncTransportServer
    {
        /// <summary>
        /// 服务器监听的 IP 地址。
        /// </summary>
        private readonly IPAddress _address;

        /// <summary>
        /// 服务器监听的端口号。
        /// </summary>
        private readonly int _port;

        /// <summary>
        /// 当前已连接的所有客户端会话的并发字典，以会话 GUID 为键。
        /// </summary>
        private readonly ConcurrentDictionary<Guid, TcpTransportSession> _sessions = new ConcurrentDictionary<Guid, TcpTransportSession>();

        /// <summary>
        /// TCP 监听器，用于接受传入的客户端连接。
        /// </summary>
        private TcpListener _listener;

        /// <summary>
        /// 用于控制服务器生命周期和取消操作的取消令牌源。
        /// </summary>
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<CallbackContext> _callbackContext = new AsyncLocal<CallbackContext>();
        private Task _stopCompletion = Task.CompletedTask;
        private int _running;
        private int _disposed;

        /// <summary>
        /// 使用指定的 IP 地址和端口号初始化 <see cref="TcpTransportServer"/> 类的新实例。
        /// </summary>
        /// <param name="address">服务器绑定的 IP 地址。如果为 <c>null</c>，则使用 <see cref="IPAddress.Any"/>。</param>
        /// <param name="port">服务器监听的端口号。</param>
        public TcpTransportServer(IPAddress address, int port)
        {
            _address = address ?? IPAddress.Any;
            _port = port;
        }

        /// <summary>
        /// 获取一个值，该值指示服务器当前是否正在运行并接受客户端连接。
        /// </summary>
        public bool IsRunning { get { return Volatile.Read(ref _running) != 0; } }

        /// <summary>获取当前活动客户端会话数。</summary>
        public int SessionCount { get { return _sessions.Count; } }

        /// <summary>
        /// 当有新客户端会话建立连接时触发。事件参数包含新建立的会话实例。
        /// </summary>
        public event EventHandler<TransportSessionEventArgs> SessionConnected;

        /// <summary>
        /// 当已有客户端会话关闭连接时触发。事件参数包含已关闭的会话实例。
        /// </summary>
        public event EventHandler<TransportSessionEventArgs> SessionClosed;

        /// <summary>
        /// 当从某个客户端会话接收到数据时触发。事件参数包含发送数据的会话和接收到的负载。
        /// </summary>
        public event EventHandler<TransportDataReceivedEventArgs> DataReceived;

        /// <summary>
        /// 当收到数据时触发的可等待异步事件。需要 await 发送、写库等异步工作的订阅者应使用此事件，
        /// 避免把 async lambda 绑定到同步 <see cref="DataReceived"/> 后形成无法观察异常的 async void。
        /// </summary>
        public event TransportDataReceivedAsyncEventHandler DataReceivedAsync;

        /// <summary>
        /// 异步启动服务器。初始化 TCP 监听器并开始接受客户端连接。如果服务器已在运行，则直接返回。
        /// </summary>
        /// <param name="cancellationToken">用于取消启动操作的取消令牌。服务器内部会创建链接的取消令牌源。</param>
        /// <returns>表示异步启动操作的任务。</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                ThrowIfDisposed();
                Task pendingStop = null;
                await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    if (IsRunning) return;
                    if (_stopCompletion.Status != TaskStatus.RanToCompletion)
                    {
                        if (IsInServerCallback())
                        {
                            throw new InvalidOperationException(
                                "The TCP server cannot be restarted from one of its callbacks while the previous stop is still draining.");
                        }
                        pendingStop = _stopCompletion;
                    }
                    else
                    {
                        var source = new CancellationTokenSource();
                        var listener = new TcpListener(_address, _port);
                        try { listener.Start(); }
                        catch
                        {
                            listener.Stop();
                            source.Dispose();
                            throw;
                        }

                        _cts = source;
                        _listener = listener;
                        Volatile.Write(ref _running, 1);
                        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, source.Token));
                        return;
                    }
                }
                finally
                {
                    _lifecycleGate.Release();
                }

                await AwaitWithCancellationAsync(pendingStop, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 异步停止服务器。取消所有正在进行的操作，停止 TCP 监听器，释放所有客户端会话资源并清空会话集合。
        /// </summary>
        /// <param name="cancellationToken">用于取消停止操作的取消令牌。</param>
        /// <returns>表示异步停止操作的任务。</returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task stopCompletion;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            var calledFromCallback = IsInServerCallback();
            try
            {
                if (!IsRunning)
                {
                    stopCompletion = _stopCompletion;
                }
                else
                {
                    Volatile.Write(ref _running, 0);
                    var source = _cts;
                    var listener = _listener;
                    var acceptLoop = _acceptLoopTask;
                    _cts = null;
                    _listener = null;
                    _acceptLoopTask = null;

                    try { source?.Cancel(); } catch (ObjectDisposedException) { }
                    try { listener?.Stop(); } catch { }

                    stopCompletion = CompleteStopAsync(acceptLoop, source);
                    _stopCompletion = stopCompletion;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }

            // A receive callback is part of its own session Completion task. Waiting for the
            // shared drain here would make the callback wait for itself. Cleanup continues in
            // the background and completes as soon as this callback returns.
            if (calledFromCallback) return;
            await AwaitWithCancellationAsync(stopCompletion, cancellationToken).ConfigureAwait(false);
        }

        private async Task CompleteStopAsync(
            Task acceptLoop,
            CancellationTokenSource source)
        {
            Exception acceptFailure = null;
            try
            {
                try
                {
                    if (acceptLoop != null) await acceptLoop.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    acceptFailure = ex;
                }

                // Do not detach sessions until admission has completely stopped. This prevents
                // Stop from disposing a session between TryAdd and Start/SessionConnected.
                var sessions = DetachAndDisposeSessions();
                if (sessions.Count > 0)
                    await Task.WhenAll(sessions.Select(session => session.Completion)).ConfigureAwait(false);

                if (acceptFailure != null) throw acceptFailure;
            }
            finally
            {
                source?.Dispose();
            }
        }

        /// <summary>
        /// 异步接受循环。持续接受传入的 TCP 客户端连接，为每个连接创建 <see cref="TcpTransportSession"/> 实例，
        /// 注册事件处理程序，触发 <see cref="SessionConnected"/> 事件并启动会话的数据接收循环。
        /// 当取消请求发出或监听器被释放时退出循环。
        /// </summary>
        /// <param name="cancellationToken">用于取消接受循环的取消令牌。</param>
        /// <returns>表示异步接受循环的任务。</returns>
        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    continue;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    try { client.Close(); } catch { }
                    break;
                }

                TcpTransportSession session = null;
                var added = false;
                try
                {
                    session = new TcpTransportSession(client);
                    if (!_sessions.TryAdd(session.SessionId, session))
                    {
                        session.Dispose();
                        continue;
                    }

                    added = true;
                    session.DataReceivedAsync += OnSessionDataReceivedAsync;
                    session.Closed += OnSessionClosed;
                    InvokeInCallbackScope(
                        () => InvokeSafely(SessionConnected, new TransportSessionEventArgs(session)));

                    if (cancellationToken.IsCancellationRequested) break;
                    session.Start();
                }
                catch
                {
                    if (added)
                    {
                        TcpTransportSession removed;
                        _sessions.TryRemove(session.SessionId, out removed);
                        UnsubscribeAndDispose(session);
                    }
                    else if (session != null)
                    {
                        session.Dispose();
                    }
                    else
                    {
                        try { client.Close(); } catch { }
                    }

                    if (cancellationToken.IsCancellationRequested) break;
                }
            }
        }

        /// <summary>
        /// 处理客户端会话关闭事件。将会话从并发字典中移除，并触发 <see cref="SessionClosed"/> 事件。
        /// </summary>
        /// <param name="sender">触发事件的会话对象。</param>
        /// <param name="e">事件参数。</param>
        private void OnSessionClosed(object sender, EventArgs e)
        {
            var session = (TcpTransportSession)sender;
            try
            {
                InvokeInCallbackScope(
                    () => InvokeSafely(SessionClosed, new TransportSessionEventArgs(session)));
            }
            finally
            {
                session.DataReceivedAsync -= OnSessionDataReceivedAsync;
                session.Closed -= OnSessionClosed;
                session.Dispose();
                TcpTransportSession removed;
                _sessions.TryRemove(session.SessionId, out removed);
            }
        }

        /// <summary>
        /// 处理客户端会话数据接收事件。将接收到的数据转发给服务器的 <see cref="DataReceived"/> 事件订阅者。
        /// </summary>
        /// <param name="sender">触发事件的会话对象。</param>
        /// <param name="payload">从会话接收到的二进制数据负载。</param>
        private async Task OnSessionDataReceivedAsync(TcpTransportSession session, byte[] payload)
        {
            var previousContext = _callbackContext.Value;
            var context = new CallbackContext(previousContext);
            _callbackContext.Value = context;
            try
            {
                InvokeSafely(DataReceived, new TransportDataReceivedEventArgs(session, payload));
                await InvokeAsyncSafely(DataReceivedAsync, new TransportDataReceivedEventArgs(session, payload)).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref context.Active, 0);
                _callbackContext.Value = previousContext;
            }
        }

        /// <summary>
        /// 释放服务器使用的所有资源。停止服务器运行并释放取消令牌源。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            // Keep the gate alive so a StartAsync already queued before Dispose can re-enter,
            // observe the disposed flag, and unwind deterministically.
        }

        private IReadOnlyCollection<TcpTransportSession> DetachAndDisposeSessions()
        {
            var sessions = _sessions.Values.ToArray();
            foreach (var session in sessions)
            {
                TcpTransportSession removed;
                _sessions.TryRemove(session.SessionId, out removed);
                UnsubscribeAndDispose(session);
            }
            return sessions;
        }

        private static async Task AwaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
        {
            if (task == null) return;
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var cancellationCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(
                state => ((TaskCompletionSource<bool>)state).TrySetResult(true),
                cancellationCompletion))
            {
                if (await Task.WhenAny(task, cancellationCompletion.Task).ConfigureAwait(false) != task)
                    throw new OperationCanceledException(cancellationToken);
            }
            await task.ConfigureAwait(false);
        }

        private void InvokeInCallbackScope(Action callback)
        {
            var previousContext = _callbackContext.Value;
            var context = new CallbackContext(previousContext);
            _callbackContext.Value = context;
            try
            {
                callback();
            }
            finally
            {
                Volatile.Write(ref context.Active, 0);
                _callbackContext.Value = previousContext;
            }
        }

        private bool IsInServerCallback()
        {
            for (var context = _callbackContext.Value; context != null; context = context.Parent)
            {
                if (Volatile.Read(ref context.Active) != 0) return true;
            }
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(TcpTransportServer));
        }

        private sealed class CallbackContext
        {
            public CallbackContext(CallbackContext parent)
            {
                Parent = parent;
                Active = 1;
            }

            public readonly CallbackContext Parent;
            public int Active;
        }

        private void UnsubscribeAndDispose(TcpTransportSession session)
        {
            session.DataReceivedAsync -= OnSessionDataReceivedAsync;
            session.Closed -= OnSessionClosed;
            session.Dispose();
        }

        private void InvokeSafely<TEventArgs>(EventHandler<TEventArgs> handlers, TEventArgs args)
            where TEventArgs : EventArgs
        {
            if (handlers == null)
            {
                return;
            }

            foreach (EventHandler<TEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, args);
                }
                catch
                {
                    // 业务订阅者的同步异常不能终止监听或接收循环。
                }
            }
        }

        private async Task InvokeAsyncSafely(TransportDataReceivedAsyncEventHandler handlers, TransportDataReceivedEventArgs args)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (TransportDataReceivedAsyncEventHandler handler in handlers.GetInvocationList())
            {
                try { await handler(this, args).ConfigureAwait(false); }
                catch { /* 单个异步订阅者失败不能中断其他订阅者和接收循环。 */ }
            }
        }
    }
}
