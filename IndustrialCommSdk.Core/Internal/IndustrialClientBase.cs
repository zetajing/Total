using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Internal
{
    /// <summary>工业客户端公共基类，统一处理操作串行化、超时、健康状态和轮询订阅。</summary>
    public abstract class IndustrialClientBase : IIndustrialClient, IProtocolCapabilityProvider, IIndustrialDiagnosticsProvider
    {
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly IPollingScheduler _pollingScheduler;
        private readonly IIndustrialLogger _logger;
        private DateTimeOffset? _lastSuccessUtc;
        private int _consecutiveFailures;
        private string _lastError;
        private ConnectionStatus _status;
        private int _disposed;
        private readonly TimeSpan _defaultOperationTimeout;
        private readonly object _diagnosticSync = new object();
        private long _totalOperations;
        private long _successfulOperations;
        private long _failedOperations;
        private long _timeoutCount;
        private long _lastOperationElapsedMilliseconds;
        private IndustrialFailureCategory _lastFailureCategory;
        private DateTimeOffset? _lastOperationUtc;
        private long _serialPortOpenFailureCount;
        private long _responseTimeoutCount;
        private long _frameErrorCount;

        protected IndustrialClientBase(string deviceId, ProtocolKind kind, IPollingScheduler pollingScheduler, IIndustrialLogger logger, int operationTimeoutMilliseconds = 5000)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Device ID cannot be null or empty.", nameof(deviceId));
            DeviceId = deviceId;
            Kind = kind;
            _pollingScheduler = pollingScheduler ?? throw new ArgumentNullException(nameof(pollingScheduler));
            _logger = logger ?? NullIndustrialLogger.Instance;
            _status = ConnectionStatus.Disconnected;
            if (operationTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(operationTimeoutMilliseconds));
            _defaultOperationTimeout = TimeSpan.FromMilliseconds(operationTimeoutMilliseconds);
        }

        public string DeviceId { get; private set; }
        public ProtocolKind Kind { get; private set; }
        public abstract bool IsConnected { get; }

        /// <summary>
        /// Gets protocol capabilities for this client. Protocol clients can override this when their runtime options
        /// change limits or supported features; the default is the built-in capability matrix for <see cref="Kind" />.
        /// </summary>
        public virtual ProtocolCapabilities Capabilities
        {
            get { return ProtocolCapabilities.ForProtocol(Kind); }
        }

        protected IIndustrialLogger Logger { get { return _logger; } }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                _status = ConnectionStatus.Connecting;
                _logger.Info(string.Format("CONNECT begin | Device={0} | Protocol={1}", DeviceId, Kind));
                await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
                RecordSuccess(stopwatch.ElapsedMilliseconds);
                _logger.Info(string.Format("CONNECT completed | Device={0} | Protocol={1} | Elapsed={2}ms", DeviceId, Kind, stopwatch.ElapsedMilliseconds));
            }
            catch (Exception ex)
            {
                RecordFailure(ex, true, stopwatch.ElapsedMilliseconds);
                throw;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
                _status = ConnectionStatus.Disconnected;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateDeviceId(request.DeviceId);
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var effectiveTimeout = request.Timeout ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(effectiveTimeout, cancellationToken))
                {
                    try
                    {
                        var value = await AwaitWithCancellation(ReadCoreAsync(request, operationCts.Token), operationCts.Token).ConfigureAwait(false);
                        RecordReadResult(value, stopwatch.ElapsedMilliseconds);
                        return value;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var ex = new IndustrialTimeoutException("Industrial read operation timed out.");
                        HandleOperationTimeoutSafely();
                        RecordFailure(ex, true, stopwatch.ElapsedMilliseconds);
                        return BadValue(request, ex.Message);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(ex, IsConnectionFailure(ex), stopwatch.ElapsedMilliseconds);
                        return BadValue(request, ex.Message);
                    }
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (requests.Count == 0) return new BatchReadResult(new List<DataValue>());
            ValidateRequests(requests.Select(x => x.DeviceId));

            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var timeout = GetShortestTimeout(requests.Select(x => x.Timeout)) ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(timeout, cancellationToken))
                {
                    try
                    {
                        var result = await AwaitWithCancellation(ReadManyCoreAsync(requests, operationCts.Token), operationCts.Token).ConfigureAwait(false);
                        RecordBatchResult(result, stopwatch.ElapsedMilliseconds);
                        return result;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var ex = new IndustrialTimeoutException("Industrial batch read operation timed out.");
                        HandleOperationTimeoutSafely();
                        RecordFailure(ex, true, stopwatch.ElapsedMilliseconds);
                        return CreateBadBatch(requests, ex.Message);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(ex, IsConnectionFailure(ex), stopwatch.ElapsedMilliseconds);
                        return CreateBadBatch(requests, ex.Message);
                    }
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateDeviceId(request.DeviceId);
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var effectiveTimeout = request.Timeout ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(effectiveTimeout, cancellationToken))
                {
                    try
                    {
                        await AwaitWithCancellation(WriteCoreAsync(request, operationCts.Token), operationCts.Token).ConfigureAwait(false);
                        RecordSuccess(stopwatch.ElapsedMilliseconds);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var ex = new IndustrialTimeoutException("Industrial write operation timed out.");
                        HandleOperationTimeoutSafely();
                        RecordFailure(ex, true, stopwatch.ElapsedMilliseconds);
                        throw ex;
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(ex, IsConnectionFailure(ex), stopwatch.ElapsedMilliseconds);
                        throw;
                    }
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (requests.Count == 0) return;
            ValidateRequests(requests.Select(x => x.DeviceId));

            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var timeout = GetShortestTimeout(requests.Select(x => x.Timeout)) ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(timeout, cancellationToken))
                {
                    try
                    {
                        await AwaitWithCancellation(WriteManyCoreAsync(requests, operationCts.Token), operationCts.Token).ConfigureAwait(false);
                        RecordSuccess(stopwatch.ElapsedMilliseconds);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var ex = new IndustrialTimeoutException("Industrial batch write operation timed out.");
                        HandleOperationTimeoutSafely();
                        RecordFailure(ex, true, stopwatch.ElapsedMilliseconds);
                        throw ex;
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(ex, IsConnectionFailure(ex), stopwatch.ElapsedMilliseconds);
                        throw;
                    }
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateDeviceId(request.DeviceId);
            return _pollingScheduler.SubscribeAsync(this, request, handler, cancellationToken);
        }

        public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            return _pollingScheduler.UnsubscribeAsync(subscriptionId, cancellationToken);
        }

        protected Task ExecuteExclusiveAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            return ExecuteExclusiveAsync<object>(async token => { await operation(token).ConfigureAwait(false); return null; }, cancellationToken);
        }

        protected async Task<TResult> ExecuteExclusiveAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var result = await operation(cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public HealthSnapshot GetHealth()
        {
            return new HealthSnapshot(
                _status,
                _lastSuccessUtc,
                Volatile.Read(ref _consecutiveFailures),
                _lastError);
        }

        protected abstract Task ConnectCoreAsync(CancellationToken cancellationToken);
        protected abstract Task DisconnectCoreAsync(CancellationToken cancellationToken);
        protected abstract Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken);
        protected abstract Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken);
        protected virtual void OnOperationTimeout() { }

        private void HandleOperationTimeoutSafely()
        {
            try { OnOperationTimeout(); }
            catch (Exception ex) { _logger.Error(string.Format("Timeout cleanup failed | Device={0} | Protocol={1}", DeviceId, Kind), ex); }
        }

        protected virtual async Task<BatchReadResult> ReadManyCoreAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            var values = new List<DataValue>(requests.Count);
            foreach (var request in requests)
            {
                using (var requestCts = CreateOperationCancellation(request.Timeout, cancellationToken))
                {
                    try
                    {
                        values.Add(await ReadCoreAsync(request, requestCts.Token).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && request.Timeout.HasValue)
                    {
                        values.Add(BadValue(request, "Industrial read operation timed out."));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        values.Add(BadValue(request, ex.Message));
                    }
                }
            }
            return new BatchReadResult(values);
        }

        protected virtual async Task WriteManyCoreAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            foreach (var request in requests)
            {
                using (var requestCts = CreateOperationCancellation(request.Timeout, cancellationToken))
                {
                    await WriteCoreAsync(request, requestCts.Token).ConfigureAwait(false);
                }
            }
        }

        protected void RecordSuccess(long elapsedMilliseconds = 0)
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
