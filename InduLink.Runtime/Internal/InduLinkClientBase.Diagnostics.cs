using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Diagnostics;
using InduLink.Exceptions;

namespace InduLink.Runtime
{
    /// <summary>工业客户端公共基类，统一处理操作串行化、超时、健康状态和轮询订阅。</summary>
    public abstract partial class InduLinkClientBase
    {
        private static readonly TimeSpan DefaultDisposeWaitTimeout = TimeSpan.FromSeconds(10);

        protected void RecordSuccess(long elapsedMilliseconds = 0)
        {
            Interlocked.Increment(ref _totalOperations);
            Interlocked.Increment(ref _successfulOperations);
            Interlocked.Exchange(ref _lastOperationElapsedMilliseconds, elapsedMilliseconds);

            lock (_diagnosticSync)
            {
                _lastFailureCategory = InduLinkFailureCategory.None;
                _lastOperationUtc = DateTimeOffset.UtcNow;
            }

            lock (_diagnosticSync)
            {
                _lastSuccessUtc = DateTimeOffset.UtcNow;
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                _lastError = null;
                _status = IsConnected ? ConnectionStatus.Connected : _status;
            }
        }

        protected void RecordFailure(Exception ex)
        {
            RecordFailure(ex, IsConnectionFailure(ex));
        }

        private void RecordReadResult(DataValue value, long elapsedMilliseconds)
        {
            if (value != null && value.Quality == QualityStatus.Good)
            {
                RecordSuccess(elapsedMilliseconds);
            }
            else
            {
                var message = value == null
                    ? "Read returned no value."
                    : value.ErrorMessage ?? "Read returned bad quality.";
                RecordFailure(new InduLinkCommunicationException(message), false, elapsedMilliseconds);
            }
        }

        private void RecordBatchResult(BatchReadResult result, long elapsedMilliseconds)
        {
            if (result == null || result.Values == null || result.Values.Count == 0)
            {
                RecordFailure(new InduLinkCommunicationException("Batch read returned no values."), false, elapsedMilliseconds);
                return;
            }

            var goodCount = result.Values.Count(x => x.Quality == QualityStatus.Good);
            if (goodCount > 0)
            {
                Interlocked.Increment(ref _totalOperations);
                Interlocked.Increment(ref _successfulOperations);
                Interlocked.Exchange(ref _lastOperationElapsedMilliseconds, elapsedMilliseconds);
                lock (_diagnosticSync)
                {
                    _lastOperationUtc = DateTimeOffset.UtcNow;
                    _lastFailureCategory = InduLinkFailureCategory.None;
                }

                lock (_diagnosticSync)
                {
                    _lastSuccessUtc = DateTimeOffset.UtcNow;
                    _status = IsConnected ? ConnectionStatus.Connected : _status;
                    if (goodCount == result.Values.Count)
                    {
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        _lastError = null;
                    }
                    else
                    {
                        Interlocked.Increment(ref _consecutiveFailures);
                        _lastError = "Batch read completed with partial bad quality values.";
                    }
                }
            }
            else
            {
                RecordFailure(new InduLinkCommunicationException("Batch read returned only bad quality values."), false, elapsedMilliseconds);
            }
        }

        private void RecordFailure(Exception ex, bool connectionFailure, long elapsedMilliseconds = 0)
        {
            Interlocked.Increment(ref _totalOperations);
            Interlocked.Increment(ref _failedOperations);
            if (IsTimeoutFailure(ex))
            {
                Interlocked.Increment(ref _timeoutCount);
            }

            Interlocked.Exchange(ref _lastOperationElapsedMilliseconds, elapsedMilliseconds);
            lock (_diagnosticSync)
            {
                _lastFailureCategory = ClassifyFailure(ex);
                _lastOperationUtc = DateTimeOffset.UtcNow;
            }

            lock (_diagnosticSync)
            {
                Interlocked.Increment(ref _consecutiveFailures);
                _lastError = ex?.Message;
                if (connectionFailure)
                {
                    _status = ConnectionStatus.Faulted;
                }
            }

            _logger.Error(string.Format("Operation failed | Device={0} | Protocol={1}", DeviceId, Kind), ex);
        }

        public InduLinkDiagnosticSnapshot GetDiagnosticSnapshot()
        {
            lock (_diagnosticSync)
            {
                return new InduLinkDiagnosticSnapshot(
                    DeviceId,
                    Kind,
                    Interlocked.Read(ref _totalOperations),
                    Interlocked.Read(ref _successfulOperations),
                    Interlocked.Read(ref _failedOperations),
                    Interlocked.Read(ref _timeoutCount),
                    Volatile.Read(ref _consecutiveFailures),
                    Interlocked.Read(ref _lastOperationElapsedMilliseconds),
                    _lastFailureCategory,
                    _lastError,
                    _lastOperationUtc,
                    Interlocked.Read(ref _serialPortOpenFailureCount),
                    Interlocked.Read(ref _responseTimeoutCount),
                    Interlocked.Read(ref _frameErrorCount));
            }
        }

        protected void RecordSerialPortOpenFailure()
        {
            Interlocked.Increment(ref _serialPortOpenFailureCount);
        }

        protected void RecordResponseTimeout()
        {
            Interlocked.Increment(ref _responseTimeoutCount);
        }

        protected void RecordFrameError()
        {
            Interlocked.Increment(ref _frameErrorCount);
        }

        private static InduLinkFailureCategory ClassifyFailure(Exception ex)
        {
            if (ex == null) return InduLinkFailureCategory.Unknown;
            if (ex is InduLinkTimeoutException || ex is TimeoutException) return InduLinkFailureCategory.Timeout;
            if (ex is InduLinkAddressParseException) return InduLinkFailureCategory.Address;
            if (ex is InduLinkDataConversionException) return InduLinkFailureCategory.DataConversion;
            if (ex is InduLinkWriteUncertainException) return InduLinkFailureCategory.Connection;
            if (ex is InduLinkProtocolException) return InduLinkFailureCategory.Protocol;
            if (ex is InduLinkConnectionException || ex is System.IO.IOException || ex is System.Net.Sockets.SocketException) return InduLinkFailureCategory.Connection;
            return ex.InnerException == null ? InduLinkFailureCategory.Unknown : ClassifyFailure(ex.InnerException);
        }

        private static bool IsConnectionFailure(Exception ex)
        {
            if (ex == null) return false;
            return ex is InduLinkConnectionException ||
                   ex is InduLinkWriteUncertainException ||
                   ex is InduLinkTimeoutException ||
                   ex is System.IO.IOException ||
                   ex is System.Net.Sockets.SocketException ||
                   IsConnectionFailure(ex.InnerException);
        }

        private static bool IsTimeoutFailure(Exception ex)
        {
            if (ex == null) return false;
            return ex is InduLinkTimeoutException || ex is TimeoutException || IsTimeoutFailure(ex.InnerException);
        }

        private static bool IsWriteOutcomeUncertain(Exception ex)
        {
            if (ex == null) return false;
            if (ex is InduLinkWriteUncertainException) return false;
            if (ex is InduLinkTimeoutException || ex is TimeoutException ||
                ex is InduLinkConnectionException || ex is System.IO.IOException ||
                ex is System.Net.Sockets.SocketException)
                return true;
            return IsWriteOutcomeUncertain(ex.InnerException);
        }

        private void ValidateDeviceId(string requestDeviceId)
        {
            if (!string.Equals(DeviceId, requestDeviceId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(string.Format("Request device '{0}' does not match client device '{1}'.", requestDeviceId, DeviceId));
        }

        private void ValidateRequests(IEnumerable<string> deviceIds)
        {
            foreach (var deviceId in deviceIds) ValidateDeviceId(deviceId);
        }

        private static TimeSpan GetBatchTimeout(IEnumerable<TimeSpan?> timeouts, TimeSpan defaultTimeout)
        {
            // Each request keeps its own timeout in ReadManyCoreAsync/WriteManyCoreAsync.
            // The outer budget must cover the slowest explicit request instead of being
            // shortened by an unrelated fast request in the same batch.
            var longest = defaultTimeout;
            foreach (var timeout in timeouts)
            {
                if (timeout.HasValue && timeout.Value > longest) longest = timeout.Value;
            }
            return longest;
        }

        private static CancellationTokenSource CreateOperationCancellation(TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout.HasValue) source.CancelAfter(timeout.Value);
            return source;
        }

        private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (task.IsCompleted) return await task.ConfigureAwait(false);
            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellation.TrySetCanceled()))
            {
                var completed = await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false);
                if (completed != task) cancellationToken.ThrowIfCancellationRequested();
                return await task.ConfigureAwait(false);
            }
        }

        private static async Task AwaitWithCancellation(Task task, CancellationToken cancellationToken)
        {
            await AwaitWithCancellation(WrapTask(task), cancellationToken).ConfigureAwait(false);
        }

        private bool RetainOperationLockUntilCoreCompletes(Task coreTask, string operationName)
        {
            if (coreTask == null)
                return false;

            coreTask.ContinueWith(
                completed =>
                {
                    try
                    {
                        if (completed.IsFaulted)
                        {
                            var aggregate = completed.Exception;
                            var flattened = aggregate == null ? null : aggregate.Flatten();
                            Exception error = flattened;
                            if (flattened != null && flattened.InnerExceptions.Count == 1)
                                error = flattened.InnerExceptions[0];

                            _logger.Error(
                                string.Format(
                                    "Late {0} core task failed after its caller stopped waiting | Device={1} | Protocol={2}",
                                    operationName,
                                    DeviceId,
                                    Kind),
                                error);
                        }
                    }
                    finally
                    {
                        // Ownership of the semaphore was transferred by the timed-out or
                        // cancelled caller. Do not admit a new transport operation until the
                        // non-cooperative core task has actually stopped touching the connection.
                        _operationLock.Release();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return true;
        }

        private static async Task<bool> WrapTask(Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            await task.ConfigureAwait(false);
            return true;
        }

        private static DataValue BadValue(ReadRequest request, string message)
        {
            return new DataValue(request.Address, request.DataType, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, message);
        }

        private static BatchReadResult CreateBadBatch(IEnumerable<ReadRequest> requests, string message)
        {
            return new BatchReadResult(requests.Select(x => BadValue(x, message)).ToList());
        }

        protected void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        /// 获取释放时等待轮询器和当前操作的最长时间。非协作协议实现超时后，真正的协议资源释放会延后到当前操作结束。
        /// </summary>
        protected virtual TimeSpan DisposeWaitTimeout
        {
            get { return DefaultDisposeWaitTimeout; }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            var disposeTimeout = DisposeWaitTimeout;
            if (disposeTimeout <= TimeSpan.Zero)
                disposeTimeout = DefaultDisposeWaitTimeout;

            if (_pollingScheduler is IAsyncDisposable asyncPollingScheduler)
            {
                try
                {
                    await asyncPollingScheduler.DisposeAsync().AsTask().WaitAsync(disposeTimeout).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    _logger.Error(
                        string.Format("Polling scheduler disposal exceeded the configured budget | Device={0} | Protocol={1}", DeviceId, Kind),
                        ex);
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        string.Format("Polling scheduler disposal failed | Device={0} | Protocol={1}", DeviceId, Kind),
                        ex);
                }
            }
            else
            {
                _pollingScheduler.Dispose();
            }

            if (!await _operationLock.WaitAsync(disposeTimeout).ConfigureAwait(false))
            {
                _logger.Error(
                    string.Format("Client disposal deferred because an operation did not release the connection lock | Device={0} | Protocol={1}", DeviceId, Kind),
                    new TimeoutException("The client operation lock was not released before disposal timed out."));
                _ = FinishDisposeAfterOperationAsync();
                return;
            }

            try
            {
                DisposeCore();
                lock (_diagnosticSync)
                {
                    _status = ConnectionStatus.Disconnected;
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private async Task FinishDisposeAfterOperationAsync()
        {
            try
            {
                await _operationLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    DisposeCore();
                    lock (_diagnosticSync)
                    {
                        _status = ConnectionStatus.Disconnected;
                    }
                }
                finally
                {
                    _operationLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(
                    string.Format("Deferred client disposal failed | Device={0} | Protocol={1}", DeviceId, Kind),
                    ex);
            }
        }

        protected virtual void DisposeCore() { }
    }
}
