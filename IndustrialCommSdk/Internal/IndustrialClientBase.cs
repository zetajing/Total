using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Internal
{
    public abstract class IndustrialClientBase : IIndustrialClient
    {
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly IPollingScheduler _pollingScheduler;
        private readonly IIndustrialLogger _logger;
        private DateTimeOffset? _lastSuccessUtc;
        private int _consecutiveFailures;
        private string _lastError;
        private ConnectionStatus _status;
        private bool _disposed;

        protected IndustrialClientBase(string deviceId, ProtocolKind kind, IPollingScheduler pollingScheduler, IIndustrialLogger logger)
        {
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            Kind = kind;
            _pollingScheduler = pollingScheduler ?? throw new ArgumentNullException(nameof(pollingScheduler));
            _logger = logger ?? NullIndustrialLogger.Instance;
            _status = ConnectionStatus.Disconnected;
        }

        public string DeviceId { get; private set; }
        public ProtocolKind Kind { get; private set; }
        public abstract bool IsConnected { get; }
        protected IIndustrialLogger Logger { get { return _logger; } }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                _status = ConnectionStatus.Connecting;
                await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                _status = ConnectionStatus.Connected;
                _logger.Info(string.Format("{0} connected.", DeviceId));
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                _status = ConnectionStatus.Faulted;
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
                await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
                _status = ConnectionStatus.Disconnected;
                _logger.Info(string.Format("{0} disconnected.", DeviceId));
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var value = await ReadCoreAsync(request, cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                return value;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                return new DataValue(request.Address, request.DataType, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, ex.Message);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await ReadManyCoreAsync(requests, cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                var badValues = new List<DataValue>(requests.Count);
                foreach (var r in requests)
                    badValues.Add(new DataValue(r.Address, r.DataType, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, ex.Message));
                return new BatchReadResult(badValues);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WriteCoreAsync(request, cancellationToken).ConfigureAwait(false);
                RecordSuccess();
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

        public async Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WriteManyCoreAsync(requests, cancellationToken).ConfigureAwait(false);
                RecordSuccess();
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

        public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
        {
            return _pollingScheduler.SubscribeAsync(this, request, handler, cancellationToken);
        }

        public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            return _pollingScheduler.UnsubscribeAsync(subscriptionId, cancellationToken);
        }

        public HealthSnapshot GetHealth()
        {
            return new HealthSnapshot(_status, _lastSuccessUtc, _consecutiveFailures, _lastError);
        }

        protected abstract Task ConnectCoreAsync(CancellationToken cancellationToken);
        protected abstract Task DisconnectCoreAsync(CancellationToken cancellationToken);
        protected abstract Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken);
        protected abstract Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken);

        protected virtual async Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            var values = new List<DataValue>(requests.Count);
            foreach (var request in requests)
                values.Add(await ReadCoreAsync(request, cancellationToken).ConfigureAwait(false));
            return new BatchReadResult(values);
        }

        protected virtual async Task WriteManyCoreAsync(
            IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            foreach (var request in requests)
                await WriteCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }

        protected void RecordSuccess()
        {
            _lastSuccessUtc = DateTimeOffset.UtcNow;
            _consecutiveFailures = 0;
            _lastError = null;
            _status = ConnectionStatus.Connected;
        }

        protected void RecordFailure(Exception ex)
        {
            _consecutiveFailures++;
            _lastError = ex == null ? null : ex.Message;
            _status = ConnectionStatus.Faulted;
            _logger.Error("Operation failed.", ex);
        }

        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pollingScheduler.Dispose();
            _operationLock.Dispose();
            DisposeCore();
        }

        protected virtual void DisposeCore()
        {
        }
    }
}
