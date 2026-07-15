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
    public abstract partial class IndustrialClientBase : IIndustrialClient, IProtocolCapabilityProvider, IIndustrialDiagnosticsProvider
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
    }
}
