using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Runtime
{
    /// <summary>工业客户端公共基类，统一处理操作串行化、超时、健康状态和轮询订阅。</summary>
    public abstract partial class IndustrialClientBase
    {        protected void RecordSuccess(long elapsedMilliseconds = 0)
        {
            Interlocked.Increment(ref _totalOperations);
            Interlocked.Increment(ref _successfulOperations);
            Interlocked.Exchange(ref _lastOperationElapsedMilliseconds, elapsedMilliseconds);
            lock (_diagnosticSync) { _lastFailureCategory = IndustrialFailureCategory.None; _lastOperationUtc = DateTimeOffset.UtcNow; }
            _lastSuccessUtc = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            _lastError = null;
            _status = IsConnected ? ConnectionStatus.Connected : _status;
        }

        protected void RecordFailure(Exception ex)
        {
            RecordFailure(ex, IsConnectionFailure(ex));
        }

        private void RecordReadResult(DataValue value, long elapsedMilliseconds)
        {
            if (value != null && value.Quality == QualityStatus.Good)
                RecordSuccess(elapsedMilliseconds);
            else
                RecordFailure(new IndustrialCommunicationException(value == null ? "Read returned no value." : value.ErrorMessage ?? "Read returned bad quality."), false, elapsedMilliseconds);
        }

        private void RecordBatchResult(BatchReadResult result, long elapsedMilliseconds)
        {
            if (result == null || result.Values == null || result.Values.Count == 0)
            {
                RecordFailure(new IndustrialCommunicationException("Batch read returned no values."), false, elapsedMilliseconds);
                return;
            }

            var goodCount = result.Values.Count(x => x.Quality == QualityStatus.Good);
            if (goodCount > 0)
            {
                Interlocked.Increment(ref _totalOperations);
                Interlocked.Increment(ref _successfulOperations);
                Interlocked.Exchange(ref _lastOperationElapsedMilliseconds, elapsedMilliseconds);
                lock (_diagnosticSync) { _lastOperationUtc = DateTimeOffset.UtcNow; _lastFailureCategory = IndustrialFailureCategory.None; }
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
            else
            {
                RecordFailure(new IndustrialCommunicationException("Batch read returned only bad quality values."), false, elapsedMilliseconds);
            }
        }

        private void RecordFailure(Exception ex, bool connectionFailure, long elapsedMilliseconds = 0)
        {
            Interlocked.Increment(ref _totalOperations);
            Interlocked.Increment(ref _failedOperations);
            if (ex is IndustrialTimeoutException) Interlocked.Increment(ref _timeoutCount);
            Interlocked.Exchange(ref _lastOperationElapsedMilliseconds, elapsedMilliseconds);
            lock (_diagnosticSync) { _lastFailureCategory = ClassifyFailure(ex); _lastOperationUtc = DateTimeOffset.UtcNow; }
            Interlocked.Increment(ref _consecutiveFailures);
            _lastError = ex == null ? null : ex.Message;
            if (connectionFailure) _status = ConnectionStatus.Faulted;
            _logger.Error(string.Format("Operation failed | Device={0} | Protocol={1}", DeviceId, Kind), ex);
        }

        public IndustrialDiagnosticSnapshot GetDiagnosticSnapshot()
        {
            lock (_diagnosticSync)
            {
                return new IndustrialDiagnosticSnapshot(DeviceId, Kind, Interlocked.Read(ref _totalOperations),
                    Interlocked.Read(ref _successfulOperations), Interlocked.Read(ref _failedOperations),
                    Interlocked.Read(ref _timeoutCount), Volatile.Read(ref _consecutiveFailures),
                    Interlocked.Read(ref _lastOperationElapsedMilliseconds), _lastFailureCategory, _lastError, _lastOperationUtc,
                    Interlocked.Read(ref _serialPortOpenFailureCount), Interlocked.Read(ref _responseTimeoutCount),
                    Interlocked.Read(ref _frameErrorCount));
            }
        }

        protected void RecordSerialPortOpenFailure() { Interlocked.Increment(ref _serialPortOpenFailureCount); }
        protected void RecordResponseTimeout() { Interlocked.Increment(ref _responseTimeoutCount); }
        protected void RecordFrameError() { Interlocked.Increment(ref _frameErrorCount); }

        private static IndustrialFailureCategory ClassifyFailure(Exception ex)
        {
            if (ex == null) return IndustrialFailureCategory.Unknown;
            if (ex is IndustrialTimeoutException || ex is TimeoutException) return IndustrialFailureCategory.Timeout;
            if (ex is IndustrialAddressParseException) return IndustrialFailureCategory.Address;
            if (ex is IndustrialDataConversionException) return IndustrialFailureCategory.DataConversion;
            if (ex is IndustrialProtocolException) return IndustrialFailureCategory.Protocol;
            if (ex is IndustrialConnectionException || ex is System.IO.IOException || ex is System.Net.Sockets.SocketException) return IndustrialFailureCategory.Connection;
            return ex.InnerException == null ? IndustrialFailureCategory.Unknown : ClassifyFailure(ex.InnerException);
        }

        private static bool IsConnectionFailure(Exception ex)
        {
            if (ex == null) return false;
            return ex is IndustrialConnectionException ||
                   ex is IndustrialTimeoutException ||
                   ex is System.IO.IOException ||
                   ex is System.Net.Sockets.SocketException ||
                   IsConnectionFailure(ex.InnerException);
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

        private static TimeSpan? GetShortestTimeout(IEnumerable<TimeSpan?> timeouts)
        {
            TimeSpan? shortest = null;
            foreach (var timeout in timeouts)
            {
                if (timeout.HasValue && (!shortest.HasValue || timeout.Value < shortest.Value)) shortest = timeout;
            }
            return shortest;
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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _pollingScheduler.Dispose();
            _operationLock.Wait();
            try
            {
                DisposeCore();
                _status = ConnectionStatus.Disconnected;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        protected virtual void DisposeCore() { }
    }
}
