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
    /// <summary>工业客户端公共基类，统一处理操作串行化、健康状态记录和轮询订阅调度。</summary>
    public abstract class IndustrialClientBase : IIndustrialClient
    {
        /// <summary>用于串行化所有异步操作的信号量，确保同一时刻只有一个核心操作在执行。</summary>
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        /// <summary>轮询调度器实例，负责管理订阅的轮询生命周期。</summary>
        private readonly IPollingScheduler _pollingScheduler;
        /// <summary>日志记录器实例，用于记录操作日志和异常信息。</summary>
        private readonly IIndustrialLogger _logger;
        /// <summary>最近一次成功操作的世界协调时（UTC），用于健康状态快照。</summary>
        private DateTimeOffset? _lastSuccessUtc;
        /// <summary>连续失败次数，用于健康状态监控。</summary>
        private int _consecutiveFailures;
        /// <summary>最近一次失败的错误消息。</summary>
        private string _lastError;
        /// <summary>当前连接状态。</summary>
        private ConnectionStatus _status;
        /// <summary>指示当前实例是否已释放。</summary>
        private int _disposed;

        /// <summary>使用指定的设备标识、协议类型、轮询调度器和日志记录器初始化工业客户端基类。</summary>
        /// <param name="deviceId">设备标识。</param>
        /// <param name="kind">协议类型。</param>
        /// <param name="pollingScheduler">轮询调度器实例。</param>
        /// <param name="logger">日志记录器实例；若为 null 则使用空日志记录器。</param>
        /// <exception cref="ArgumentNullException"><paramref name="deviceId"/> 或 <paramref name="pollingScheduler"/> 为 null。</exception>
        protected IndustrialClientBase(string deviceId, ProtocolKind kind, IPollingScheduler pollingScheduler, IIndustrialLogger logger)
        {
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            Kind = kind;
            _pollingScheduler = pollingScheduler ?? throw new ArgumentNullException(nameof(pollingScheduler));
            _logger = logger ?? NullIndustrialLogger.Instance;
            _status = ConnectionStatus.Disconnected;
        }

        /// <summary>获取设备标识。</summary>
        public string DeviceId { get; private set; }
        /// <summary>获取协议类型。</summary>
        public ProtocolKind Kind { get; private set; }
        /// <summary>获取一个值，指示当前是否已连接到设备。</summary>
        public abstract bool IsConnected { get; }
        /// <summary>获取日志记录器实例。</summary>
        protected IIndustrialLogger Logger { get { return _logger; } }

        /// <summary>异步连接到设备。</summary>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <exception cref="ObjectDisposedException">实例已被释放。</exception>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("CONNECT begin | Device={0} | Protocol={1}", DeviceId, Kind));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                _status = ConnectionStatus.Connecting;
                await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                _status = ConnectionStatus.Connected;
                _logger.Info(string.Format("CONNECT completed | Device={0} | Protocol={1} | Elapsed={2}ms", DeviceId, Kind, stopwatch.ElapsedMilliseconds));
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

        /// <summary>异步断开与设备的连接。</summary>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("DISCONNECT begin | Device={0} | Protocol={1}", DeviceId, Kind));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
                _status = ConnectionStatus.Disconnected;
                _logger.Info(string.Format("DISCONNECT completed | Device={0} | Protocol={1} | Elapsed={2}ms", DeviceId, Kind, stopwatch.ElapsedMilliseconds));
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>从设备异步读取一个数据点。</summary>
        /// <param name="request">读请求，包含地址和数据类型等信息。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>包含读取结果的数据值；读取失败时返回质量状态为 Bad 的数据值。</returns>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public async Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("READ begin | Device={0} | Protocol={1} | Address={2} | Type={3} | Length={4}", DeviceId, Kind, request.Address, request.DataType, request.Length));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var operationCts = CreateOperationCancellation(request.Timeout, cancellationToken))
                {
                    try
                    {
                        var value = await ReadCoreAsync(request, operationCts.Token).ConfigureAwait(false);
                        RecordSuccess();
                        _logger.Info(string.Format("READ completed | Device={0} | Address={1} | Quality={2} | RawBytes={3} | Elapsed={4}ms", DeviceId, request.Address, value.Quality, value.RawData == null ? 0 : value.RawData.Length, stopwatch.ElapsedMilliseconds));
                        return value;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && request.Timeout.HasValue)
                    {
                        var timeoutException = new IndustrialTimeoutException("Industrial read operation timed out.");
                        RecordFailure(timeoutException);
                        _logger.Warn(string.Format("READ timeout | Device={0} | Address={1} | Elapsed={2}ms", DeviceId, request.Address, stopwatch.ElapsedMilliseconds));
                        return new DataValue(request.Address, request.DataType, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, timeoutException.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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

        /// <summary>从设备异步批量读取多个数据点。</summary>
        /// <param name="requests">读请求集合，每个请求包含地址和数据类型等信息。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>批量读取结果，包含每个请求对应的数据值；单个请求失败时对应数据值质量为 Bad。</returns>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("READ MANY begin | Device={0} | Protocol={1} | Count={2} | Addresses=[{3}]", DeviceId, Kind, requests.Count, FormatAddresses(requests.Select(item => item.Address))));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await ReadManyCoreAsync(requests, cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                _logger.Info(string.Format("READ MANY completed | Device={0} | Count={1} | Good={2} | Bad={3} | Elapsed={4}ms", DeviceId, result.Values.Count, result.Values.Count(item => item.Quality == QualityStatus.Good), result.Values.Count(item => item.Quality != QualityStatus.Good), stopwatch.ElapsedMilliseconds));
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

        /// <summary>向设备异步写入一个数据点。</summary>
        /// <param name="request">写请求，包含地址和要写入的值等信息。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public async Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("WRITE begin | Device={0} | Protocol={1} | Address={2} | Type={3} | Length={4}", DeviceId, Kind, request.Address, request.DataType, request.Length));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var operationCts = CreateOperationCancellation(request.Timeout, cancellationToken))
                {
                    try
                    {
                        await WriteCoreAsync(request, operationCts.Token).ConfigureAwait(false);
                        RecordSuccess();
                        _logger.Info(string.Format("WRITE completed | Device={0} | Address={1} | Elapsed={2}ms", DeviceId, request.Address, stopwatch.ElapsedMilliseconds));
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && request.Timeout.HasValue)
                    {
                        throw new IndustrialTimeoutException("Industrial write operation timed out.");
                    }
                }
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

        /// <summary>向设备异步批量写入多个数据点。</summary>
        /// <param name="requests">写请求集合，每个请求包含地址和要写入的值等信息。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public async Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("WRITE MANY begin | Device={0} | Protocol={1} | Count={2} | Addresses=[{3}]", DeviceId, Kind, requests.Count, FormatAddresses(requests.Select(item => item.Address))));
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WriteManyCoreAsync(requests, cancellationToken).ConfigureAwait(false);
                RecordSuccess();
                _logger.Info(string.Format("WRITE MANY completed | Device={0} | Count={1} | Elapsed={2}ms", DeviceId, requests.Count, stopwatch.ElapsedMilliseconds));
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

        /// <summary>订阅设备数据变化通知。</summary>
        /// <param name="request">订阅请求，包含地址等订阅参数。</param>
        /// <param name="handler">数据变化事件处理程序。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务，任务结果为订阅标识。</returns>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
        {
            _logger.Info(string.Format("SUBSCRIBE requested | Device={0} | Key={1} | Items={2} | Interval={3}ms | ChangeOnly={4}", DeviceId, request.SubscriptionKey, request.Items.Count, request.Interval.TotalMilliseconds, request.ReportOnChangeOnly));
            return _pollingScheduler.SubscribeAsync(this, request, handler, cancellationToken);
        }

        /// <summary>取消指定的数据变化订阅。</summary>
        /// <param name="subscriptionId">要取消的订阅标识。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <exception cref="OperationCanceledException">操作已被取消。</exception>
        public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            _logger.Info(string.Format("UNSUBSCRIBE requested | Device={0} | Subscription={1}", DeviceId, subscriptionId));
            return _pollingScheduler.UnsubscribeAsync(subscriptionId, cancellationToken);
        }

        private static string FormatAddresses(IEnumerable<string> addresses)
        {
            var items = addresses.Take(12).ToArray();
            var text = string.Join(",", items);
            return text.Length > 240 ? text.Substring(0, 240) + "..." : text;
        }

        /// <summary>
        /// 供派生类执行额外的独占异步操作。
        /// 这类操作会复用客户端已有的串行化、释放检查和健康状态记录逻辑，
        /// 适合协议特有但不属于通用读写抽象的扩展 API。
        /// </summary>
        /// <param name="operation">实际要执行的异步操作。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        protected Task ExecuteExclusiveAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            return ExecuteExclusiveAsync<object>(
                async token =>
                {
                    await operation(token).ConfigureAwait(false);
                    return null;
                },
                cancellationToken);
        }

        /// <summary>
        /// 供派生类执行带返回值的独占异步操作。
        /// </summary>
        /// <typeparam name="TResult">返回值类型。</typeparam>
        /// <param name="operation">实际要执行的异步操作。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>异步操作的返回值。</returns>
        protected async Task<TResult> ExecuteExclusiveAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

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

        /// <summary>获取当前客户端的健康状态快照。</summary>
        /// <returns>包含连接状态、最后成功时间、连续失败次数和最后错误消息的健康快照。</returns>
        public HealthSnapshot GetHealth()
        {
            return new HealthSnapshot(_status, _lastSuccessUtc, _consecutiveFailures, _lastError);
        }

        /// <summary>由派生类实现的异步连接核心逻辑。</summary>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        protected abstract Task ConnectCoreAsync(CancellationToken cancellationToken);
        /// <summary>由派生类实现的异步断开连接核心逻辑。</summary>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        protected abstract Task DisconnectCoreAsync(CancellationToken cancellationToken);
        /// <summary>由派生类实现的异步读取核心逻辑。</summary>
        /// <param name="request">读请求，包含地址和数据类型等信息。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>读取到的数据值。</returns>
        protected abstract Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken);
        /// <summary>由派生类实现的异步写入核心逻辑。</summary>
        /// <param name="request">写请求，包含地址和要写入的值等信息。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        protected abstract Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken);

        /// <summary>批量读取多个数据点的核心逻辑。默认实现为逐一调用 <see cref="ReadCoreAsync"/>。</summary>
        /// <param name="requests">读请求集合。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>批量读取结果。</returns>
        protected virtual async Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            var values = new List<DataValue>(requests.Count);
            foreach (var request in requests)
                values.Add(await ReadCoreAsync(request, cancellationToken).ConfigureAwait(false));
            return new BatchReadResult(values);
        }

        /// <summary>批量写入多个数据点的核心逻辑。默认实现为逐一调用 <see cref="WriteCoreAsync"/>。</summary>
        /// <param name="requests">写请求集合。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        protected virtual async Task WriteManyCoreAsync(
            IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            foreach (var request in requests)
                await WriteCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private static CancellationTokenSource CreateOperationCancellation(TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout.HasValue)
            {
                source.CancelAfter(timeout.Value);
            }
            return source;
        }

        /// <summary>记录一次成功操作：更新最后成功时间、重置连续失败计数和错误消息，并将状态设为已连接。</summary>
        protected void RecordSuccess()
        {
            _lastSuccessUtc = DateTimeOffset.UtcNow;
            _consecutiveFailures = 0;
            _lastError = null;
            _status = ConnectionStatus.Connected;
        }

        /// <summary>记录一次失败操作：递增连续失败计数、记录错误消息、将状态设为故障，并通过日志记录器输出错误。</summary>
        /// <param name="ex">操作的异常信息；若为 null 则仅递增失败计数。</param>
        protected void RecordFailure(Exception ex)
        {
            _consecutiveFailures++;
            _lastError = ex == null ? null : ex.Message;
            _status = ConnectionStatus.Faulted;
            _logger.Error(string.Format("Operation failed | Device={0} | Protocol={1}", DeviceId, Kind), ex);
        }

        /// <summary>检查实例是否已被释放，若是则抛出 <see cref="ObjectDisposedException"/>。</summary>
        /// <exception cref="ObjectDisposedException">当前实例已被释放。</exception>
        protected void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        /// <summary>执行托管资源的释放操作。可被多次调用，只有首次调用执行实际释放逻辑。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // 先停止轮询，阻止它继续排队新的客户端操作；再等待当前独占操作退出，
            // 避免在读写过程中关闭底层 Socket/串口或释放仍会在 finally 中 Release 的锁。
            _pollingScheduler.Dispose();
            _operationLock.Wait();
            try
            {
                DisposeCore();
            }
            finally
            {
                _operationLock.Release();
            }

            // SemaphoreSlim 不在这里 Dispose：调用 Dispose 前已经排队的操作仍需获得锁、
            // 观察 disposed 状态并安全退出。它不持有非托管资源。
        }

        /// <summary>由派生类实现的额外的资源释放逻辑。基类实现为空。</summary>
        protected virtual void DisposeCore()
        {
        }
    }
}
