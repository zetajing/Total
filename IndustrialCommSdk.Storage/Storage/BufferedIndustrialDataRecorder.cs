using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Storage
{
    public sealed class BufferedDataRecorderOptions
    {
        /// <summary>
        /// 单次数据库事务期望写入的最大记录数。
        /// 数值过小会增加事务次数，数值过大会让单次事务持续更久。
        /// </summary>
        public int BatchSize { get; set; } = 100;
        /// <summary>
        /// 内存中最多排队的读取批次数。
        /// 使用上限可以防止数据库长期不可用时队列无限增长，最终耗尽上位机内存。
        /// </summary>
        public int QueueCapacity { get; set; } = 1000;
        /// <summary>单批失败后的重试次数。重试仍失败时记录错误并放弃该批数据。</summary>
        public int RetryCount { get; set; } = 2;
    }

    public sealed class BufferedRecorderSnapshot
    {
        public bool IsRunning { get; set; }
        public int QueuedBatchCount { get; set; }
        public long AcceptedRecordCount { get; set; }
        public long WrittenRecordCount { get; set; }
        public long DroppedRecordCount { get; set; }
        public long WriteFailureCount { get; set; }
        public DateTimeOffset? LastSuccessfulWrite { get; set; }
        public string LastError { get; set; }
    }

    /// <summary>
    /// 把通信线程产生的读取结果放入有界队列，再由单独后台任务批量写库。
    /// <para>完整数据流如下：</para>
    /// <para>PLC 读取回调 → <see cref="TryRecord"/> 快速入队 → 后台任务合并批次 → 配置的数据库。</para>
    /// <para>
    /// 这种结构叫“生产者/消费者”：通信线程是生产者，数据库线程是消费者。
    /// 数据库变慢或短暂断线时不会阻塞设备通信；队列满时会丢弃最新批次并记录警告，
    /// 以保证实时通信优先于历史数据完整性。
    /// </para>
    /// </summary>
    public sealed class BufferedIndustrialDataRecorder : IDisposable
    {
        private readonly IIndustrialDataStore _store;
        private readonly BufferedDataRecorderOptions _options;
        private readonly IIndustrialLogger _logger;
        // BlockingCollection 同时提供线程安全队列、容量限制和“完成添加”通知，
        // 很适合 .NET Framework 4.7.2 下实现简单可靠的生产者/消费者模型。
        private readonly BlockingCollection<IReadOnlyCollection<IndustrialDataRecord>> _queue;

        // 该取消源只属于后台工作任务。正常停止使用 CompleteAdding 排空队列，
        // Dispose 才会取消仍在等待的任务。
        private readonly CancellationTokenSource _stopSource = new CancellationTokenSource();
        private Task _worker;
        private readonly object _stopGate = new object();
        private Task _stopTask;

        // 使用整数配合 Interlocked/Volatile 代替普通 bool，确保多线程能看到一致的启动状态。
        private int _started;
        private long _acceptedRecordCount;
        private long _writtenRecordCount;
        private long _droppedRecordCount;
        private long _writeFailureCount;
        private long _lastSuccessfulWriteUtcTicks;
        private string _lastError;
        private int _disposed;

        /// <summary>
        /// 创建后台记录器，但此时还没有连接数据库或启动后台任务。
        /// 调用方必须继续调用 <see cref="StartAsync"/>。
        /// </summary>
        public BufferedIndustrialDataRecorder(
            IIndustrialDataStore store,
            BufferedDataRecorderOptions options = null,
            IIndustrialLogger logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _options = options ?? new BufferedDataRecorderOptions();

            // 配置错误属于编程错误，应在启动前立即报告，而不是让后台线程静默失败。
            if (_options.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options), "BatchSize 必须大于 0。");
            if (_options.QueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(options), "QueueCapacity 必须大于 0。");
            if (_options.RetryCount < 0) throw new ArgumentOutOfRangeException(nameof(options), "RetryCount 不能小于 0。");
            _logger = logger ?? NullIndustrialLogger.Instance;
            _queue = new BlockingCollection<IReadOnlyCollection<IndustrialDataRecord>>(_options.QueueCapacity);
        }

        /// <summary>
        /// 检查数据库、创建表并启动后台写入任务。
        /// 只有初始化成功后才启动消费者，避免把数据放入一个永远无法写出的队列。
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(BufferedIndustrialDataRecorder));
            // CompareExchange 保证并发调用 StartAsync 时只有第一个调用真正执行启动逻辑。
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            try
            {
                await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info(string.Format(CultureInfo.InvariantCulture, "DATABASE recorder started | QueueCapacity={0} | BatchSize={1} | Retries={2}", _options.QueueCapacity, _options.BatchSize, _options.RetryCount));

                // Task.Run 把持续运行的队列消费者放到线程池，不占用 WPF UI 线程。
                _worker = Task.Run(() => ProcessQueueAsync(_stopSource.Token));
            }
            catch
            {
                // 初始化失败后恢复未启动状态，调用方可以修正连接字符串并创建新记录器重试。
                Interlocked.Exchange(ref _started, 0);
                throw;
            }
        }

        /// <summary>
        /// 非阻塞地把一批读取结果加入写库队列。
        /// <para>
        /// “非阻塞”是这里最重要的约束：该方法可能直接运行在 PLC 轮询回调中，
        /// 因此不能等待数据库，也不能在队列满时停住通信线程。
        /// </para>
        /// </summary>
        public bool TryRecord(ProtocolKind protocol, string deviceId, IReadOnlyCollection<DataValue> values)
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _started) == 0 || values == null || values.Count == 0 || _queue.IsAddingCompleted)
            {
                return false;
            }

            // 入队前转换并复制可变数据，确保后台线程读取的是调用时刻的稳定快照。
            var records = values.Select(value => IndustrialDataRecord.FromDataValue(protocol, deviceId, value)).ToArray();
            if (_queue.TryAdd(records))
            {
                Interlocked.Add(ref _acceptedRecordCount, records.Length);
                _logger.Trace(string.Format(CultureInfo.InvariantCulture, "DATABASE batch queued | Records={0} | QueuedBatches={1}", records.Length, _queue.Count));
                return true;
            }

            // TryAdd 不等待空间。队列满说明数据库速度已明显落后，优先保护通信和内存。
            _logger.Warn("数据库写入队列已满，本批采集数据已丢弃。");
            Interlocked.Add(ref _droppedRecordCount, records.Length);
            return false;
        }

        public BufferedRecorderSnapshot GetSnapshot()
        {
            var ticks = Interlocked.Read(ref _lastSuccessfulWriteUtcTicks);
            return new BufferedRecorderSnapshot {
                IsRunning = Volatile.Read(ref _started) != 0,
                QueuedBatchCount = _queue.Count,
                AcceptedRecordCount = Interlocked.Read(ref _acceptedRecordCount), WrittenRecordCount = Interlocked.Read(ref _writtenRecordCount),
                DroppedRecordCount = Interlocked.Read(ref _droppedRecordCount), WriteFailureCount = Interlocked.Read(ref _writeFailureCount),
                LastSuccessfulWrite = ticks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero), LastError = Volatile.Read(ref _lastError) };
        }

        /// <summary>
        /// 停止接收新数据，并等待队列中的记录写完。
        /// 这叫“优雅停止”：应用退出时先禁止入队，再把已经采集的数据排空，减少停机丢数。
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task stopTask;
            lock (_stopGate)
            {
                if (_stopTask == null)
                {
                    if (Interlocked.Exchange(ref _started, 0) == 0)
                    {
                        return;
                    }

                    // 取消调用方的等待不会取消实际排空过程；后续 StopAsync 或 Dispose
                    // 仍可取得同一个任务并继续等待，避免队列处于“已停止但无人收尾”的状态。
                    _queue.CompleteAdding();
                    _stopTask = CompleteStopAsync();
                }
                stopTask = _stopTask;
            }

            var completed = await Task.WhenAny(stopTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            if (completed != stopTask) cancellationToken.ThrowIfCancellationRequested();
            await stopTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // 即使先前 StopAsync 的“等待”被取消，实际排空任务仍保存在 _stopTask 中，
            // Dispose 必须重新等待它，不能直接释放队列和存储对象。
            try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger.Error("停止数据库记录器失败。", ex); }

            // 释放顺序：通知任务取消 → 释放队列/令牌源 → 释放具体数据库存储。
            _stopSource.Cancel();
            _queue.Dispose();
            _stopSource.Dispose();
            _store.Dispose();
            Interlocked.Exchange(ref _started, 0);
        }

        private async Task CompleteStopAsync()
        {
            if (_worker != null)
            {
                await _worker.ConfigureAwait(false);
            }
            _logger.Info(string.Format(CultureInfo.InvariantCulture, "DATABASE recorder stopped | Accepted={0} | Written={1} | Dropped={2} | Failures={3}", Interlocked.Read(ref _acceptedRecordCount), Interlocked.Read(ref _writtenRecordCount), Interlocked.Read(ref _droppedRecordCount), Interlocked.Read(ref _writeFailureCount)));
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            // 复用 List 缓冲区，避免每一轮消费者循环都创建新的可增长集合。
            var batch = new List<IndustrialDataRecord>(_options.BatchSize);
            while (!_queue.IsCompleted)
            {
                IReadOnlyCollection<IndustrialDataRecord> first;
                try
                {
                    // 没有数据时 Take 会等待，不会让后台线程空转并持续占用 CPU。
                    first = _queue.Take(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // CompleteAdding 且队列已空时，Take 会抛出该异常，表示可以正常结束消费者。
                    break;
                }

                batch.Clear();
                batch.AddRange(first);
                IReadOnlyCollection<IndustrialDataRecord> next;

                // 将队列中已经到达的小批次尽量合并，减少数据库事务和网络往返次数。
                while (batch.Count < _options.BatchSize && _queue.TryTake(out next))
                {
                    batch.AddRange(next);
                }

                await WriteWithRetryAsync(batch.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task WriteWithRetryAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    await _store.WriteAsync(records, cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref _writtenRecordCount, records.Count);
                    Interlocked.Exchange(ref _lastSuccessfulWriteUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
                    Volatile.Write(ref _lastError, null);
                    _logger.Trace(string.Format(CultureInfo.InvariantCulture, "DATABASE batch written | Records={0} | Attempt={1} | Elapsed={2}ms", records.Count, attempt + 1, stopwatch.ElapsedMilliseconds));
                    return;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && attempt < _options.RetryCount)
                {
                    Interlocked.Increment(ref _writeFailureCount);
                    Volatile.Write(ref _lastError, ex.Message);
                    _logger.Warn(string.Format(CultureInfo.InvariantCulture, "数据库写入失败，准备第 {0} 次重试：{1}", attempt + 1, ex.Message));

                    // 重试间隔逐次增加，避免数据库刚恢复时大量客户端同时立即重试形成冲击。
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // 达到重试上限后放弃本批。这里不能让异常结束消费者，否则后续数据库恢复也无法继续记录。
                    _logger.Error("数据库写入失败，本批数据已放弃。", ex);
                    Interlocked.Increment(ref _writeFailureCount);
                    Interlocked.Add(ref _droppedRecordCount, records.Count);
                    Volatile.Write(ref _lastError, ex.Message);
                    return;
                }
            }
        }
    }
}
