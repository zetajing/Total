using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Mes
{
    /// <summary>基于 HttpListener 的开放式 MES HTTP JSON 接收器。</summary>
    public sealed partial class MesJsonReceiver : IMesJsonReceiver
    {
        private const int Stopped = 0;
        private const int Running = 1;
        private const int Stopping = 2;

        private readonly MesJsonReceiverOptions _options;
        private readonly MesJsonReceiveHandler _handler;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _requestSlots;
        private readonly ConcurrentDictionary<int, Task> _activeRequests = new ConcurrentDictionary<int, Task>();
        private readonly ConcurrentDictionary<int, Task> _activeHandlers = new ConcurrentDictionary<int, Task>();
        private readonly AsyncLocal<int?> _currentRequestId = new AsyncLocal<int?>();

        private HttpListener _listener;
        private CancellationTokenSource _stopSource;
        private Task _acceptLoopTask;
        private Task _stopCompletion = Task.CompletedTask;
        private int _nextRequestId;
        private int _lifecycleGeneration;
        private int _state;
        private int _disposed;

        public MesJsonReceiver(
            MesJsonReceiverOptions options,
            MesJsonReceiveHandler handler,
            IIndustrialLogger logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _logger = logger ?? NullIndustrialLogger.Instance;
            ValidateOptions(options);
            _requestSlots = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);
        }

        public bool IsRunning => Volatile.Read(ref _state) == Running;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                ThrowIfDisposed();
                Task pendingStop = null;
                await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // Start may have passed the fast disposed check and then waited behind a
                    // concurrent Dispose. Recheck while holding the lifecycle gate so a disposed
                    // receiver can never publish a new listener after Dispose has returned.
                    ThrowIfDisposed();
                    if (_state == Running) return;
                    if (_state == Stopping)
                    {
                        pendingStop = _stopCompletion;
                    }
                    else
                    {
                        var listener = new HttpListener();
                        listener.Prefixes.Add(_options.ListenPrefix);
                        try { listener.Start(); }
                        catch
                        {
                            listener.Close();
                            throw;
                        }

                        var stopSource = new CancellationTokenSource();
                        _listener = listener;
                        _stopSource = stopSource;
                        Volatile.Write(ref _state, Running);
                        _acceptLoopTask = AcceptLoopAsync(listener, stopSource.Token);
                        _logger.Info("MES HTTP receiver started | Prefix=" + _options.ListenPrefix);
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

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            var callerRequestId = _currentRequestId.Value;
            Task acceptLoop;
            Task stopCompletion;
            CancellationTokenSource sourceToCancel = null;
            TaskCompletionSource<bool> stopSignal = null;
            var stopGeneration = 0;

            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_state == Stopped) return;
                acceptLoop = _acceptLoopTask;
                if (_state == Running)
                {
                    Volatile.Write(ref _state, Stopping);
                    var listener = _listener;
                    sourceToCancel = _stopSource;
                    stopGeneration = ++_lifecycleGeneration;

                    try { listener?.Close(); } catch { }
                    _listener = null;

                    stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    stopCompletion = stopSignal.Task;
                    _stopCompletion = stopCompletion;
                }
                else
                {
                    stopCompletion = _stopCompletion;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }

            if (stopSignal != null)
            {
                // Start the bounded drain before dispatching cancellation callbacks. A callback
                // is then free to re-enter StopAsync without the lifecycle gate being held.
                _ = CompleteStopAndSignalAsync(
                    stopGeneration,
                    acceptLoop,
                    stopSignal);
                TryCancel(sourceToCancel);
                DisposeCancellationSourceWhenStopped(stopCompletion, sourceToCancel);
            }

            // A handler may stop its own receiver. It must not await the request/handler task
            // that is currently executing it; the background completion still drains it later.
            if (callerRequestId.HasValue)
            {
                await DrainTrackedTasksAsync(
                    acceptLoop,
                    callerRequestId,
                    _options.HandlerTimeoutMilliseconds,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await AwaitWithCancellationAsync(stopCompletion, cancellationToken).ConfigureAwait(false);
        }

        private async Task CompleteStopAndSignalAsync(
            int generation,
            Task acceptLoop,
            TaskCompletionSource<bool> stopSignal)
        {
            try
            {
                await CompleteStopAsync(generation, acceptLoop).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("MES HTTP receiver stop completion failed.", ex);
            }
            finally
            {
                stopSignal.TrySetResult(true);
            }
        }

        private async Task CompleteStopAsync(
            int generation,
            Task acceptLoop)
        {
            // StopAsync schedules this completion after changing state and closing the listener.
            // Yield once so cancellation dispatch and draining cannot run inline with that setup.
            await Task.Yield();
            try
            {
                var drained = await DrainTrackedTasksAsync(
                    acceptLoop,
                    null,
                    _options.HandlerTimeoutMilliseconds,
                    CancellationToken.None).ConfigureAwait(false);
                if (!drained)
                {
                    _logger.Warn(string.Format(
                        "MES HTTP receiver stop drain timed out; {0} request(s) and {1} handler(s) remain tracked.",
                        _activeRequests.Count,
                        _activeHandlers.Count));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("MES HTTP receiver stop drain failed.", ex);
            }
            finally
            {
                await _lifecycleGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_state == Stopping && generation == _lifecycleGeneration)
                    {
                        _acceptLoopTask = null;
                        _stopSource = null;
                        Volatile.Write(ref _state, Stopped);
                    }
                }
                finally
                {
                    _lifecycleGate.Release();
                }
                _logger.Info("MES HTTP receiver stopped | Prefix=" + _options.ListenPrefix);
            }
        }

        private static void DisposeCancellationSourceWhenStopped(
            Task stopCompletion,
            CancellationTokenSource stopSource)
        {
            if (stopSource == null) return;
            _ = stopCompletion.ContinueWith(
                completed =>
                {
                    try { stopSource.Dispose(); } catch { }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task<bool> DrainTrackedTasksAsync(
            Task acceptLoop,
            int? excludedRequestId,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tasks = new List<Task>();
                if (acceptLoop != null && !acceptLoop.IsCompleted) tasks.Add(acceptLoop);
                tasks.AddRange(_activeRequests
                    .Where(pair => !excludedRequestId.HasValue || pair.Key != excludedRequestId.Value)
                    .Select(pair => pair.Value));
                tasks.AddRange(_activeHandlers
                    .Where(pair => !excludedRequestId.HasValue || pair.Key != excludedRequestId.Value)
                    .Select(pair => pair.Value));
                tasks = tasks.Where(task => task != null && !task.IsCompleted).Distinct().ToList();
                if (tasks.Count == 0) return true;

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return false;
                var allTasks = Task.WhenAll(tasks);
                var delay = Task.Delay(remaining, cancellationToken);
                if (await Task.WhenAny(allTasks, delay).ConfigureAwait(false) != allTasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return false;
                }
                try { await allTasks.ConfigureAwait(false); }
                catch { }
                acceptLoop = null;
            }
        }

        private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
        {
            try
            {
                var backoffMilliseconds = 50;
                while (!cancellationToken.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().ConfigureAwait(false);
                        backoffMilliseconds = 50;
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (HttpListenerException ex)
                    {
                        if (cancellationToken.IsCancellationRequested || !IsListening(listener)) break;
                        _logger.Error("MES HTTP receiver accept failed; retrying with backoff.", ex);
                        if (!await DelayAfterAcceptFailureAsync(backoffMilliseconds, cancellationToken).ConfigureAwait(false)) break;
                        backoffMilliseconds = Math.Min(backoffMilliseconds * 2, 1000);
                        continue;
                    }
                    catch (InvalidOperationException ex)
                    {
                        if (cancellationToken.IsCancellationRequested || !IsListening(listener)) break;
                        _logger.Error("MES HTTP receiver accept failed; retrying with backoff.", ex);
                        if (!await DelayAfterAcceptFailureAsync(backoffMilliseconds, cancellationToken).ConfigureAwait(false)) break;
                        backoffMilliseconds = Math.Min(backoffMilliseconds * 2, 1000);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested || !IsListening(listener)) break;
                        _logger.Error("MES HTTP receiver accept failed; retrying with backoff.", ex);
                        if (!await DelayAfterAcceptFailureAsync(backoffMilliseconds, cancellationToken).ConfigureAwait(false)) break;
                        backoffMilliseconds = Math.Min(backoffMilliseconds * 2, 1000);
                        continue;
                    }

                    if (!_requestSlots.Wait(0))
                    {
                        // Do not create or track an asynchronous response task here. A slow client
                        // that does not read 429 responses must not bypass MaxConcurrentRequests by
                        // growing _activeRequests without bound.
                        RejectOverloaded(context);
                        continue;
                    }

                    var requestId = Interlocked.Increment(ref _nextRequestId);
                    var permit = new RequestPermit(_requestSlots);
                    TrackRequest(requestId, HandleContextAsync(context, requestId, permit, cancellationToken));
                }
            }
            finally
            {
                await HandleAcceptLoopExitAsync(listener).ConfigureAwait(false);
            }
        }

        private async Task HandleAcceptLoopExitAsync(HttpListener listener)
        {
            CancellationTokenSource sourceToCancel = null;
            TaskCompletionSource<bool> stopSignal = null;
            Task stopCompletion = null;
            var stopGeneration = 0;

            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // A normal Stop changes the state before closing the listener. Only an accept loop
                // that exits while it still owns a Running receiver needs to initiate recovery.
                if (_state != Running || !ReferenceEquals(_listener, listener)) return;

                Volatile.Write(ref _state, Stopping);
                sourceToCancel = _stopSource;
                stopGeneration = ++_lifecycleGeneration;
                _listener = null;

                stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                stopCompletion = stopSignal.Task;
                _stopCompletion = stopCompletion;
            }
            finally
            {
                _lifecycleGate.Release();
            }

            try { listener.Close(); } catch { }

            // The current accept task cannot drain itself. The shared stop completion still drains
            // every admitted request/handler and gives concurrent Stop/Start/Dispose callers one
            // stable lifecycle task to await.
            _ = CompleteStopAndSignalAsync(stopGeneration, null, stopSignal);
            TryCancel(sourceToCancel);
            DisposeCancellationSourceWhenStopped(stopCompletion, sourceToCancel);
            _logger.Warn("MES HTTP receiver accept loop exited unexpectedly; transitioning to stopped state.");
        }

        private void TrackRequest(int requestId, Task task)
        {
            _activeRequests[requestId] = task;
            _ = task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted) { var ignoredException = completed.Exception; }
                    Task ignoredTask;
                    _activeRequests.TryRemove(requestId, out ignoredTask);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void TrackHandler(int requestId, Task task, CancellationTokenSource handlerCancellation)
        {
            _activeHandlers[requestId] = task;
            _ = task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted) { var ignoredException = completed.Exception; }
                    Task ignoredTask;
                    _activeHandlers.TryRemove(requestId, out ignoredTask);
                    handlerCancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async Task<bool> DelayAfterAcceptFailureAsync(int delayMilliseconds, CancellationToken token)
        {
            try
            {
                await Task.Delay(delayMilliseconds, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static bool IsListening(HttpListener listener)
        {
            try { return listener.IsListening; }
            catch { return false; }
        }

        private static async Task AwaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
        {
            if (task == null) return;
            if (!cancellationToken.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var cancellation = Task.Delay(Timeout.Infinite, cancellationToken);
            if (await Task.WhenAny(task, cancellation).ConfigureAwait(false) != task)
                throw new OperationCanceledException(cancellationToken);
            await task.ConfigureAwait(false);
        }

        private static void ValidateOptions(MesJsonReceiverOptions options)
        {
            Uri prefix;
            if (string.IsNullOrWhiteSpace(options.ListenPrefix) ||
                !Uri.TryCreate(options.ListenPrefix, UriKind.Absolute, out prefix) ||
                (prefix.Scheme != Uri.UriSchemeHttp && prefix.Scheme != Uri.UriSchemeHttps) ||
                !options.ListenPrefix.EndsWith("/", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(prefix.Query) || !string.IsNullOrEmpty(prefix.Fragment))
                throw new ArgumentException("MES receiver ListenPrefix must be an absolute HTTP/HTTPS prefix ending with '/'.", nameof(options));
            if (options.MaxConcurrentRequests <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrentRequests));
            if (options.MaxRequestContentBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxRequestContentBytes));
            if (options.HandlerTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.HandlerTimeoutMilliseconds));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(MesJsonReceiver));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            // The gates intentionally remain undisposed: an uncooperative timed-out handler may
            // complete later and must still be able to release its bounded request permit safely.
        }

        private sealed class RequestPermit : IDisposable
        {
            private SemaphoreSlim _slots;

            public RequestPermit(SemaphoreSlim slots) { _slots = slots; }

            public void Dispose()
            {
                var slots = Interlocked.Exchange(ref _slots, null);
                slots?.Release();
            }
        }
    }
}
