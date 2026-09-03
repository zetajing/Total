using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace InduLink.Storage
{
    /// <summary>日志级别。</summary>
    public enum LogDisplayLevel
    {
        Trace,
        Info,
        Warn,
        Error
    }

    /// <summary>一条待显示和落盘的结构化日志。</summary>
    public sealed class LogDisplayEntry
    {
        public LogDisplayEntry(
            DateTimeOffset timestamp,
            string channel,
            LogDisplayLevel level,
            string message,
            Exception exception = null)
            : this(timestamp, channel, level, message, exception, false)
        {
        }

        internal LogDisplayEntry(
            DateTimeOffset timestamp,
            string channel,
            LogDisplayLevel level,
            string message,
            Exception exception,
            bool legacyText)
        {
            Timestamp = timestamp;
            Channel = channel ?? string.Empty;
            Level = level;
            Message = message ?? string.Empty;
            Exception = exception;
            IsLegacyText = legacyText;
        }

        public DateTimeOffset Timestamp { get; }
        public string Channel { get; }
        public LogDisplayLevel Level { get; }
        public string Message { get; }
        public Exception Exception { get; }
        internal bool IsLegacyText { get; }

        /// <summary>生成文件和界面共用的文本格式。</summary>
        public string FormatText()
        {
            if (IsLegacyText)
            {
                return Message;
            }

            var builder = new StringBuilder();
            builder.Append('[')
                .Append(Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append("] [")
                .Append(Channel)
                .Append("] [")
                .Append(Level.ToString().ToUpperInvariant())
                .Append("] ")
                .Append(Message);

            if (Exception != null)
            {
                builder.AppendLine();
                builder.Append(Exception);
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            return FormatText();
        }
    }

    /// <summary>日志系统向宿主报告的告警类型。</summary>
    public enum LogDisplayWarningKind
    {
        QueueOverflow,
        WriteFailure,
        ShutdownTimeout,
        WriteAfterShutdown
    }

    /// <summary>日志系统内部告警事件参数。</summary>
    public sealed class LogDisplayWarningEventArgs : EventArgs
    {
        public LogDisplayWarningEventArgs(
            LogDisplayWarningKind kind,
            string message,
            Exception exception = null,
            long affectedCount = 0)
        {
            Kind = kind;
            Message = message ?? string.Empty;
            Exception = exception;
            AffectedCount = affectedCount;
        }

        public LogDisplayWarningKind Kind { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public long AffectedCount { get; }
    }

    /// <summary>
    ///     日志显示与落盘兼容门面。
    ///     该类不依赖第三方日志包，文件写入由一个有界队列和单一后台消费者完成。
    /// </summary>
    public static class LogDisplayHelper
    {
        private static readonly Lazy<LogDisplayEngine> Engine =
            new Lazy<LogDisplayEngine>(
                () => new LogDisplayEngine(
                    () => StoragePathProvider.LogsRoot,
                    () => DateTimeOffset.Now,
                    RaiseWarning),
                LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>日志系统无法写入或队列溢出时触发。事件处理器不应执行磁盘写入。</summary>
        public static event EventHandler<LogDisplayWarningEventArgs> WarningRaised;

        /// <summary>兼容旧调用，默认写入 APP 通道。</summary>
        public static void ShowMsg(string message)
        {
            ShowMsg("APP", message);
        }

        /// <summary>
        ///     兼容旧调用。旧文本被视为已经格式化的原始日志，不重复添加时间和级别。
        /// </summary>
        public static void ShowMsg(string channel, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            TryWrite(new LogDisplayEntry(
                DateTimeOffset.Now,
                NormalizeChannel(channel),
                LogDisplayLevel.Info,
                message,
                null,
                true));
        }

        /// <summary>将结构化日志非阻塞地加入后台写入队列。</summary>
        public static bool TryWrite(LogDisplayEntry entry)
        {
            return entry != null && !string.IsNullOrWhiteSpace(entry.Message) && Engine.Value.TryWrite(entry);
        }

        /// <summary>关闭日志系统并等待最多 5 秒排空队列。</summary>
        public static void Shutdown()
        {
            Shutdown(TimeSpan.FromSeconds(5));
        }

        /// <summary>关闭日志系统，返回是否在指定时间内完整落盘。</summary>
        public static bool Shutdown(TimeSpan timeout)
        {
            return Engine.Value.Shutdown(timeout);
        }

        /// <summary>获取指定通道的日志目录。</summary>
        public static string GetLogDirectory(string channel)
        {
            return Path.Combine(StoragePathProvider.LogsRoot, NormalizeChannel(channel));
        }

        internal static string NormalizeChannel(string channel)
        {
            if (string.Equals(channel, "SDK", StringComparison.OrdinalIgnoreCase))
            {
                return "SDK";
            }

            return "APP";
        }

        private static void RaiseWarning(LogDisplayWarningEventArgs args)
        {
            var handler = WarningRaised;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(null, args);
            }
            catch (Exception exception)
            {
                Trace.TraceError("日志告警处理器执行失败：{0}", exception);
            }
        }
    }

    /// <summary>可注入路径和时钟的日志写入引擎，便于隔离静态门面进行测试。</summary>
    internal sealed class LogDisplayEngine
    {
        internal const int QueueCapacity = 10000;
        internal const int MaxBatchSize = 200;
        internal const int BatchWindowMilliseconds = 100;

        private readonly Channel<LogDisplayEntry> _queue;
        private readonly Func<string> _logsRootProvider;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Action<LogDisplayWarningEventArgs> _warningSink;
        private readonly object _lifecycleGate = new object();
        private readonly object _warningGate = new object();
        private readonly HashSet<LogDisplayWarningKind> _activeWarnings = new HashSet<LogDisplayWarningKind>();
        private readonly Dictionary<string, ActiveLogWriter> _writers =
            new Dictionary<string, ActiveLogWriter>(StringComparer.OrdinalIgnoreCase);
        private readonly Task _workerTask;

        private long _queuedCount;
        private bool _accepting = true;
        private bool _shutdownStarted;
        private bool _shutdownCompleted;

        internal LogDisplayEngine(
            Func<string> logsRootProvider,
            Func<DateTimeOffset> clock,
            Action<LogDisplayWarningEventArgs> warningSink)
        {
            _logsRootProvider = logsRootProvider ?? throw new ArgumentNullException(nameof(logsRootProvider));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _warningSink = warningSink;

            _queue = Channel.CreateBounded<LogDisplayEntry>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            });

            _workerTask = Task.Run(ProcessLoopAsync);
        }

        internal bool TryWrite(LogDisplayEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Message))
            {
                return false;
            }

            var normalized = NormalizeEntry(entry);
            LogDisplayWarningEventArgs warning = null;
            var clearOverflowWarning = false;

            lock (_lifecycleGate)
            {
                if (!_accepting)
                {
                    warning = new LogDisplayWarningEventArgs(
                        LogDisplayWarningKind.WriteAfterShutdown,
                        "日志系统已经关闭，新的日志不会再接收。" );
                }
                else if (_queue.Writer.TryWrite(normalized))
                {
                    if (_queuedCount >= QueueCapacity)
                    {
                        warning = new LogDisplayWarningEventArgs(
                            LogDisplayWarningKind.QueueOverflow,
                            string.Format("日志队列已满，已淘汰最早日志。当前容量：{0}。", QueueCapacity),
                            affectedCount: 1);
                    }
                    else
                    {
                        _queuedCount++;
                        clearOverflowWarning = _queuedCount < QueueCapacity;
                    }
                }
                else
                {
                    warning = new LogDisplayWarningEventArgs(
                        LogDisplayWarningKind.WriteAfterShutdown,
                        "日志系统已经关闭，新的日志不会再接收。" );
                }
            }

            if (clearOverflowWarning)
            {
                ClearWarning(LogDisplayWarningKind.QueueOverflow);
            }

            if (warning != null)
            {
                ReportWarning(warning);
                return warning.Kind != LogDisplayWarningKind.WriteAfterShutdown;
            }

            return true;
        }

        internal bool Shutdown(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            Task workerTask;
            lock (_lifecycleGate)
            {
                if (_shutdownCompleted)
                {
                    return true;
                }

                if (!_shutdownStarted)
                {
                    _shutdownStarted = true;
                    _accepting = false;
                    _queue.Writer.TryComplete();
                }

                workerTask = _workerTask;
            }

            if (Task.CurrentId.HasValue && Task.CurrentId.Value == workerTask.Id)
            {
                ReportWarning(new LogDisplayWarningEventArgs(
                    LogDisplayWarningKind.ShutdownTimeout,
                    "日志后台线程不能等待自身关闭。" ));
                return false;
            }

            try
            {
                if (!workerTask.Wait(timeout))
                {
                    ReportWarning(new LogDisplayWarningEventArgs(
                        LogDisplayWarningKind.ShutdownTimeout,
                        string.Format("日志在 {0} 秒内未完成落盘，剩余约 {1} 条。", timeout.TotalSeconds, Volatile.Read(ref _queuedCount)),
                        affectedCount: Volatile.Read(ref _queuedCount)));
                    return false;
                }
            }
            catch (AggregateException exception)
            {
                ReportWarning(new LogDisplayWarningEventArgs(
                    LogDisplayWarningKind.WriteFailure,
                    "日志后台线程异常退出。",
                    exception.GetBaseException(),
                    Volatile.Read(ref _queuedCount)));
                return false;
            }

            lock (_lifecycleGate)
            {
                _shutdownCompleted = true;
            }

            ClearWarning(LogDisplayWarningKind.ShutdownTimeout);
            return true;
        }

        private LogDisplayEntry NormalizeEntry(LogDisplayEntry entry)
        {
            var timestamp = entry.Timestamp == default(DateTimeOffset) ? _clock() : entry.Timestamp;
            return new LogDisplayEntry(
                timestamp,
                LogDisplayHelper.NormalizeChannel(entry.Channel),
                entry.Level,
                entry.Message,
                entry.Exception,
                entry.IsLegacyText);
        }

        private async Task ProcessLoopAsync()
        {
            try
            {
                while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    var batch = await ReadBatchAsync().ConfigureAwait(false);
                    if (batch.Count > 0)
                    {
                        WriteBatch(batch);
                    }
                }
            }
            catch (Exception exception)
            {
                ReportWarning(new LogDisplayWarningEventArgs(
                    LogDisplayWarningKind.WriteFailure,
                    "日志后台写入线程异常退出。",
                    exception,
                    Volatile.Read(ref _queuedCount)));
            }
            finally
            {
                DisposeWriters();
            }
        }

        private async Task<List<LogDisplayEntry>> ReadBatchAsync()
        {
            var batch = new List<LogDisplayEntry>(MaxBatchSize);
            LogDisplayEntry entry;
            var deadline = Stopwatch.GetTimestamp() +
                           (long)(Stopwatch.Frequency * (BatchWindowMilliseconds / 1000.0));

            while (batch.Count < MaxBatchSize && TryRead(out entry))
            {
                batch.Add(entry);
            }

            while (batch.Count < MaxBatchSize)
            {
                var remainingTicks = deadline - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0)
                {
                    break;
                }

                var remainingMilliseconds = Math.Max(
                    1,
                    (int)Math.Ceiling(remainingTicks * 1000.0 / Stopwatch.Frequency));
                var readyTask = _queue.Reader.WaitToReadAsync().AsTask();
                var delayTask = Task.Delay(remainingMilliseconds);
                var completed = await Task.WhenAny(readyTask, delayTask).ConfigureAwait(false);
                if (completed == delayTask || !await readyTask.ConfigureAwait(false))
                {
                    break;
                }

                while (batch.Count < MaxBatchSize && TryRead(out entry))
                {
                    batch.Add(entry);
                }
            }

            return batch;
        }

        private bool TryRead(out LogDisplayEntry entry)
        {
            lock (_lifecycleGate)
            {
                if (_queue.Reader.TryRead(out entry))
                {
                    _queuedCount--;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private void WriteBatch(IReadOnlyList<LogDisplayEntry> batch)
        {
            var groups = new List<LogBatchGroup>();
            var groupMap = new Dictionary<string, LogBatchGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in batch)
            {
                var channel = LogDisplayHelper.NormalizeChannel(entry.Channel);
                var hour = entry.Timestamp.ToLocalTime().ToString("yyyyMMdd_HH");
                var key = channel + "|" + hour;
                if (!groupMap.TryGetValue(key, out var group))
                {
                    group = new LogBatchGroup(channel, hour);
                    groupMap.Add(key, group);
                    groups.Add(group);
                }

                group.Entries.Add(entry);
            }

            var failed = false;
            foreach (var group in groups)
            {
                try
                {
                    var writer = GetWriter(group.Channel, group.Hour);
                    foreach (var entry in group.Entries)
                    {
                        writer.WriteLine(entry.FormatText());
                    }

                    writer.Flush();
                }
                catch (Exception exception)
                {
                    failed = true;
                    RemoveWriter(group.Channel);
                    ReportWarning(new LogDisplayWarningEventArgs(
                        LogDisplayWarningKind.WriteFailure,
                        string.Format("写入日志文件失败：通道 {0}，时段 {1}。", group.Channel, group.Hour),
                        exception,
                        group.Entries.Count));
                }
            }

            if (!failed)
            {
                ClearWarning(LogDisplayWarningKind.WriteFailure);
            }
        }

        private ActiveLogWriter GetWriter(string channel, string hour)
        {
            var filePath = GetFilePath(channel, hour);
            if (_writers.TryGetValue(channel, out var existing) &&
                string.Equals(existing.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }

            RemoveWriter(channel);
            var writer = new ActiveLogWriter(filePath);
            _writers[channel] = writer;
            return writer;
        }

        private string GetFilePath(string channel, string hour)
        {
            var directory = Path.Combine(_logsRootProvider(), LogDisplayHelper.NormalizeChannel(channel));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, hour + ".log");
        }

        private void RemoveWriter(string channel)
        {
            if (_writers.TryGetValue(channel, out var writer))
            {
                _writers.Remove(channel);
                try
                {
                    writer.Dispose();
                }
                catch (Exception exception)
                {
                    ReportWarning(new LogDisplayWarningEventArgs(
                        LogDisplayWarningKind.WriteFailure,
                        "关闭日志文件失败。",
                        exception));
                }
            }
        }

        private void DisposeWriters()
        {
            foreach (var writer in _writers.Values)
            {
                try
                {
                    writer.Flush();
                    writer.Dispose();
                }
                catch (Exception exception)
                {
                    ReportWarning(new LogDisplayWarningEventArgs(
                        LogDisplayWarningKind.WriteFailure,
                        "关闭日志文件失败。",
                        exception));
                }
            }

            _writers.Clear();
        }

        private void ReportWarning(LogDisplayWarningEventArgs warning)
        {
            var shouldReport = false;
            lock (_warningGate)
            {
                shouldReport = _activeWarnings.Add(warning.Kind);
            }

            if (shouldReport)
            {
                if (warning.Kind == LogDisplayWarningKind.QueueOverflow ||
                    warning.Kind == LogDisplayWarningKind.ShutdownTimeout)
                {
                    Trace.TraceWarning("日志系统告警：{0}", warning.Message);
                }
                else
                {
                    Trace.TraceError("日志系统告警：{0} {1}", warning.Message, warning.Exception);
                }

                _warningSink?.Invoke(warning);
            }
        }

        private void ClearWarning(LogDisplayWarningKind kind)
        {
            lock (_warningGate)
            {
                _activeWarnings.Remove(kind);
            }
        }

        private sealed class LogBatchGroup
        {
            internal LogBatchGroup(string channel, string hour)
            {
                Channel = channel;
                Hour = hour;
            }

            internal string Channel { get; }
            internal string Hour { get; }
            internal List<LogDisplayEntry> Entries { get; } = new List<LogDisplayEntry>();
        }

        private sealed class ActiveLogWriter : IDisposable
        {
            private readonly StreamWriter _writer;

            internal ActiveLogWriter(string filePath)
            {
                FilePath = filePath;
                var stream = new FileStream(
                    filePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    4096,
                    FileOptions.SequentialScan);
                _writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, false);
            }

            internal string FilePath { get; }

            internal void WriteLine(string line)
            {
                _writer.WriteLine(line);
            }

            internal void Flush()
            {
                _writer.Flush();
            }

            public void Dispose()
            {
                _writer.Dispose();
            }
        }
    }
}
