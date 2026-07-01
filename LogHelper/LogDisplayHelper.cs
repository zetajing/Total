using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace LogHelper
{
    /// <summary>
    ///     日志显示辅助静态类。
    ///     提供异步、批量写入日志文件的后台工作线程机制，支持日志缓存、自动刷新和旧日志清理功能。
    ///     所有待写入消息通过线程安全的 <see cref="ConcurrentQueue{T}" /> 缓冲，由专用后台线程批量落盘，
    ///     避免频繁 I/O 对业务线程造成阻塞。
    /// </summary>
    public static class LogDisplayHelper
    {
        /// <summary>
        ///     待写入日志消息的线程安全队列。
        /// </summary>
        private static readonly ConcurrentQueue<LogEntry> PendingMessages = new ConcurrentQueue<LogEntry>();

        /// <summary>
        ///     用于通知工作线程有新的待处理消息的信号量。
        /// </summary>
        private static readonly AutoResetEvent FlushSignal = new AutoResetEvent(false);

        /// <summary>
        ///     用于同步关闭逻辑的锁对象，确保关闭请求仅被处理一次。
        /// </summary>
        private static readonly object SyncRoot = new object();

        /// <summary>
        ///     后台日志写入工作线程。
        /// </summary>
        private static readonly Thread WorkerThread;

        /// <summary>
        ///     指示是否已请求关闭后台工作线程的 volatile 标志。
        /// </summary>
        private static volatile bool _shutdownRequested;

        /// <summary>
        ///     累计写入批次数，用于触发定期的旧日志清理操作（每 120 次批量写入执行一次清理）。
        /// </summary>
        private static int _cleanupTick;

        /// <summary>
        ///     静态构造函数，初始化并启动后台工作线程。
        ///     工作线程设置为后台线程，名称为 "LogDisplayHelperWorker"。
        /// </summary>
        static LogDisplayHelper()
        {
            WorkerThread = new Thread(ProcessLoop)
            {
                IsBackground = true,
                Name = "LogDisplayHelperWorker"
            };
            WorkerThread.Start();
        }

        /// <summary>
        ///     向日志队列中添加一条消息并触发刷新信号。
        ///     空字符串或仅含空白字符的消息将被忽略。
        /// </summary>
        /// <param name="message">要记录的日志消息内容。</param>
        public static void ShowMsg(string message)
        {
            ShowMsg("Demo", message);
        }

        /// <summary>将日志写入指定的独立通道目录。</summary>
        public static void ShowMsg(string channel, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            PendingMessages.Enqueue(new LogEntry(NormalizeChannel(channel), message));
            FlushSignal.Set();
        }

        /// <summary>
        ///     关闭日志辅助系统。
        ///     设置关闭标志、通知工作线程退出，等待工作线程结束（最多 2 秒），
        ///     最后将队列中剩余的所有未写入消息强制刷新到磁盘。
        ///     可安全多次调用，仅首次执行实际的关闭操作。
        /// </summary>
        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                if (_shutdownRequested)
                {
                    return;
                }

                _shutdownRequested = true;
            }

            FlushSignal.Set();

            if (Thread.CurrentThread != WorkerThread && WorkerThread.IsAlive)
            {
                WorkerThread.Join(2000);
            }

            FlushPendingMessages();
        }

        /// <summary>
        ///     后台工作线程主循环。
        ///     等待刷新信号（超时 1 秒），然后执行一次待处理消息的批量写入。
        ///     当关闭标志已设置且队列为空时退出循环，终止工作线程。
        /// </summary>
        private static void ProcessLoop()
        {
            while (true)
            {
                FlushSignal.WaitOne(1000);
                FlushPendingMessages();

                if (_shutdownRequested && PendingMessages.IsEmpty)
                {
                    return;
                }
            }
        }

        /// <summary>
        ///     将当前队列中所有待处理消息分批写入日志文件。
        ///     每批最多 200 条消息，分批调用 <see cref="WriteBatch" /> 写入磁盘。
        /// </summary>
        private static void FlushPendingMessages()
        {
            var batches = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            LogEntry entry;
            var count = 0;
            while (count < 200 && PendingMessages.TryDequeue(out entry))
            {
                List<string> batch;
                if (!batches.TryGetValue(entry.Channel, out batch))
                {
                    batch = new List<string>();
                    batches.Add(entry.Channel, batch);
                }
                batch.Add(entry.Message);
                count++;
            }

            foreach (var pair in batches) WriteBatch(pair.Key, pair.Value);
            if (!PendingMessages.IsEmpty) FlushSignal.Set();
        }

        /// <summary>
        ///     将一批日志消息写入到按小时轮转的日志文件中。
        ///     日志文件路径为：{StoragePathProvider.DataRoot}/Logs/{channel}/{yyyyMMdd_HH}.log。
        ///     每写入 120 批消息后，自动触发一次旧日志清理（保留最近 14 天）。
        ///     所有 I/O 异常被静默吞噬，不影响业务线程。
        /// </summary>
        /// <param name="batch">待写入的日志消息集合。</param>
        private static void WriteBatch(string channel, IReadOnlyCollection<string> batch)
        {
            if (batch == null || batch.Count == 0)
            {
                return;
            }

            try
            {
                var logDirectory = GetLogDirectory(channel);
                Directory.CreateDirectory(logDirectory);

                var logFile = Path.Combine(logDirectory, DateTime.Now.ToString("yyyyMMdd_HH") + ".log");
                File.AppendAllLines(logFile, batch, new UTF8Encoding(false));

                if (Interlocked.Increment(ref _cleanupTick) % 120 == 0)
                {
                    CleanupOldLogs(logDirectory);
                }
            }
            catch
            {
                // 忽略所有 I/O 异常，确保日志写入失败不会影响主业务流程
            }
        }

        /// <summary>
        ///     获取日志文件存储目录。
        ///     优先使用入口程序集的名称，若无法获取则回退为 "IndustrialCommDemo"。
        ///     目录路径为：{StoragePathProvider.DataRoot}/Logs。
        /// </summary>
        /// <returns>日志目录的完整路径字符串。</returns>
        public static string GetLogDirectory(string channel)
        {
            return Path.Combine(StoragePathProvider.LogsRoot, NormalizeChannel(channel));
        }

        private static string NormalizeChannel(string channel)
        {
            if (string.Equals(channel, "SDK", StringComparison.OrdinalIgnoreCase)) return "SDK";
            return "Demo";
        }

        private sealed class LogEntry
        {
            public LogEntry(string channel, string message) { Channel = channel; Message = message; }
            public string Channel { get; private set; }
            public string Message { get; private set; }
        }

        /// <summary>
        ///     清理指定日志目录中超过 14 天未修改的旧日志文件（*.log）。
        ///     所有 I/O 异常被静默吞噬，不影响正常日志写入。
        /// </summary>
        /// <param name="logDirectory">要执行清理操作的日志目录路径。</param>
        private static void CleanupOldLogs(string logDirectory)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-14);
                foreach (var file in Directory.GetFiles(logDirectory, "*.log"))
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff)
                    {
                        info.Delete();
                    }
                }
            }
            catch
            {
                // 忽略清理过程中的异常，不影响日志系统正常运行
            }
        }
    }
}
