using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Exceptions;

namespace InduLink.Runtime
{
    /// <summary>统一执行单点和批量读写，并维护超时、锁和诊断状态。</summary>
    public abstract partial class InduLinkClientBase
    {
        public async Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateDeviceId(request.DeviceId);
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            var releaseOperationLock = true;
            Task<DataValue> coreTask = null;
            try
            {
                ThrowIfDisposed();
                var timeout = request.Timeout ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(timeout, cancellationToken))
                {
                    try
                    {
                        coreTask = ReadCoreAsync(request, operationCts.Token);
                        var value = await AwaitWithCancellation(coreTask, operationCts.Token).ConfigureAwait(false);
                        RecordReadResult(value, stopwatch.ElapsedMilliseconds);
                        return value;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var exception = new InduLinkTimeoutException("Industrial read operation timed out.");
                        HandleOperationTimeoutSafely();
                        releaseOperationLock = !RetainOperationLockUntilCoreCompletes(coreTask, "read");
                        RecordFailure(exception, true, stopwatch.ElapsedMilliseconds);
                        return BadValue(request, exception.Message);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        releaseOperationLock = !RetainOperationLockUntilCoreCompletes(coreTask, "read");
                        throw;
                    }
                    catch (Exception exception)
                    {
                        RecordFailure(exception, IsConnectionFailure(exception), stopwatch.ElapsedMilliseconds);
                        return BadValue(request, exception.Message);
                    }
                }
            }
            finally
            {
                if (releaseOperationLock)
                {
                    _operationLock.Release();
                }
            }
        }

        public async Task<BatchReadResult> ReadManyAsync(
            IReadOnlyCollection<ReadRequest> requests,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            if (requests.Count == 0)
            {
                return new BatchReadResult(new List<DataValue>());
            }

            ValidateRequests(requests.Select(request => request.DeviceId));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            var releaseOperationLock = true;
            Task<BatchReadResult> coreTask = null;
            try
            {
                ThrowIfDisposed();
                var timeout = GetShortestTimeout(requests.Select(request => request.Timeout))
                    ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(timeout, cancellationToken))
                {
                    try
                    {
                        coreTask = ReadManyCoreAsync(requests, operationCts.Token);
                        var result = await AwaitWithCancellation(coreTask, operationCts.Token).ConfigureAwait(false);
                        RecordBatchResult(result, stopwatch.ElapsedMilliseconds);
                        return result;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var exception = new InduLinkTimeoutException("Industrial batch read operation timed out.");
                        HandleOperationTimeoutSafely();
                        releaseOperationLock = !RetainOperationLockUntilCoreCompletes(coreTask, "batch read");
                        RecordFailure(exception, true, stopwatch.ElapsedMilliseconds);
                        return CreateBadBatch(requests, exception.Message);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        releaseOperationLock = !RetainOperationLockUntilCoreCompletes(coreTask, "batch read");
                        throw;
                    }
                    catch (Exception exception)
                    {
                        RecordFailure(exception, IsConnectionFailure(exception), stopwatch.ElapsedMilliseconds);
                        return CreateBadBatch(requests, exception.Message);
                    }
                }
            }
            finally
            {
                if (releaseOperationLock)
                {
                    _operationLock.Release();
                }
            }
        }

        public async Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateDeviceId(request.DeviceId);
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            var releaseOperationLock = true;
            Task coreTask = null;
            try
            {
                ThrowIfDisposed();
                var timeout = request.Timeout ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(timeout, cancellationToken))
                {
                    try
                    {
                        coreTask = WriteCoreAsync(request, operationCts.Token);
                        await AwaitWithCancellation(coreTask, operationCts.Token).ConfigureAwait(false);
                        RecordSuccess(stopwatch.ElapsedMilliseconds);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var timeoutException = new InduLinkTimeoutException("Industrial write operation timed out.");
                        var exception = new InduLinkWriteUncertainException(
                            "Industrial write outcome is unknown; the write was not replayed.",
                            timeoutException);
                        HandleOperationTimeoutSafely();
                        releaseOperationLock = !RetainOperationLockUntilCoreCompletes(coreTask, "write");
                        RecordFailure(exception, true, stopwatch.ElapsedMilliseconds);
                        throw exception;
                    }
                    catch (Exception exception)
                    {
                        releaseOperationLock = ShouldReleaseWriteLock(coreTask, exception, cancellationToken, "write");
                        var reported = GetReportedWriteException(
                            coreTask,
                            exception,
                            "Industrial write outcome is unknown; the write was not replayed.");
                        RecordFailure(reported, IsConnectionFailure(reported), stopwatch.ElapsedMilliseconds);
                        throw reported;
                    }
                }
            }
            finally
            {
                if (releaseOperationLock)
                {
                    _operationLock.Release();
                }
            }
        }

        public async Task WriteManyAsync(
            IReadOnlyCollection<WriteRequest> requests,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }

            if (requests.Count == 0)
            {
                return;
            }

            ValidateRequests(requests.Select(request => request.DeviceId));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            var releaseOperationLock = true;
            Task coreTask = null;
            try
            {
                ThrowIfDisposed();
                var timeout = GetShortestTimeout(requests.Select(request => request.Timeout))
                    ?? _defaultOperationTimeout;
                using (var operationCts = CreateOperationCancellation(timeout, cancellationToken))
                {
                    try
                    {
                        coreTask = WriteManyCoreAsync(requests, operationCts.Token);
                        await AwaitWithCancellation(coreTask, operationCts.Token).ConfigureAwait(false);
                        RecordSuccess(stopwatch.ElapsedMilliseconds);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var timeoutException = new InduLinkTimeoutException("Industrial batch write operation timed out.");
                        var exception = new InduLinkWriteUncertainException(
                            "Industrial batch write outcome is unknown; the writes were not replayed.",
                            timeoutException);
                        HandleOperationTimeoutSafely();
                        releaseOperationLock = !RetainOperationLockUntilCoreCompletes(coreTask, "batch write");
                        RecordFailure(exception, true, stopwatch.ElapsedMilliseconds);
                        throw exception;
                    }
                    catch (Exception exception)
                    {
                        releaseOperationLock = ShouldReleaseWriteLock(coreTask, exception, cancellationToken, "batch write");
                        var reported = GetReportedWriteException(
                            coreTask,
                            exception,
                            "Industrial batch write outcome is unknown; the writes were not replayed.");
                        RecordFailure(reported, IsConnectionFailure(reported), stopwatch.ElapsedMilliseconds);
                        throw reported;
                    }
                }
            }
            finally
            {
                if (releaseOperationLock)
                {
                    _operationLock.Release();
                }
            }
        }

        private bool ShouldReleaseWriteLock(
            Task coreTask,
            Exception exception,
            CancellationToken cancellationToken,
            string operationName)
        {
            var cancelled = exception as OperationCanceledException;
            var retainLock = cancelled != null &&
                cancellationToken.IsCancellationRequested &&
                RetainOperationLockUntilCoreCompletes(coreTask, operationName);
            return !retainLock;
        }

        private static Exception GetReportedWriteException(Task coreTask, Exception exception, string uncertainMessage)
        {
            var cancelled = exception as OperationCanceledException;
            var outcomeIsUncertain = (cancelled != null && coreTask != null) ||
                (cancelled == null && IsWriteOutcomeUncertain(exception));

            return outcomeIsUncertain
                ? new InduLinkWriteUncertainException(uncertainMessage, exception)
                : exception;
        }
    }
}
