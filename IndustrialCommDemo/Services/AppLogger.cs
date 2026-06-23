using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndustrialCommSdk.Diagnostics;
using LogHelper;

namespace IndustrialCommDemo.Services
{
    /// <summary>
    /// 应用程序日志记录器，实现 <see cref="IIndustrialLogger"/> 接口和 <see cref="IDisposable"/> 模式。
    /// 提供 Trace、Info、Warn、Error 四个级别的日志记录功能，并支持将日志消息批量刷新到 UI 线程。
    /// </summary>
    internal sealed class AppLogger : IIndustrialLogger, IDisposable
    {
        /// <summary>
        /// UI 刷新延迟时间（毫秒）。在接收到日志消息后，等待此时间再将批量消息派发给 UI 线程。
        /// </summary>
        private const int UiFlushDelayMilliseconds = 100;

        /// <summary>
        /// 用于在 UI 线程上执行操作的 <see cref="Dispatcher"/> 实例。
        /// </summary>
        private readonly Dispatcher _dispatcher;

        /// <summary>
        /// 将批量日志消息追加到 UI 显示的回调委托。
        /// </summary>
        private readonly Action<IReadOnlyList<string>> _appendBatch;

        /// <summary>
        /// 线程安全的队列，用于暂存待刷新到 UI 的日志消息行。
        /// </summary>
        private readonly ConcurrentQueue<string> _pendingUiMessages = new ConcurrentQueue<string>();

        /// <summary>
        /// 标记是否已安排了 UI 刷新任务。0 表示未安排，1 表示已安排。
        /// 使用 <see cref="Interlocked"/> 操作保证线程安全。
        /// </summary>
        private int _flushScheduled;

        /// <summary>
        /// 指示当前实例是否已被释放。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 初始化 <see cref="AppLogger"/> 类的新实例。
        /// </summary>
        /// <param name="dispatcher">用于在 UI 线程上执行操作的 <see cref="Dispatcher"/> 对象。不能为 null。</param>
        /// <param name="appendBatch">将批量日志消息追加到 UI 显示的回调委托。不能为 null。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="dispatcher"/> 或 <paramref name="appendBatch"/> 为 null 时引发。</exception>
        public AppLogger(Dispatcher dispatcher, Action<IReadOnlyList<string>> appendBatch)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _appendBatch = appendBatch ?? throw new ArgumentNullException(nameof(appendBatch));
        }

        /// <summary>
        /// 记录一条 TRACE 级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息文本。</param>
        public void Trace(string message)
        {
            Write("TRACE", message, null);
        }

        /// <summary>
        /// 记录一条 INFO 级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息文本。</param>
        public void Info(string message)
        {
            Write("INFO ", message, null);
        }

        /// <summary>
        /// 记录一条 WARN 级别的日志消息。
        /// </summary>
        /// <param name="message">日志消息文本。</param>
        public void Warn(string message)
        {
            Write("WARN ", message, null);
        }

        /// <summary>
        /// 记录一条 ERROR 级别的日志消息，包含可选的异常信息。
        /// 如果提供了异常，则将异常消息附加到日志文本中。
        /// </summary>
        /// <param name="message">日志消息文本。</param>
        /// <param name="exception">与日志关联的 <see cref="Exception"/> 对象；可为 null。</param>
        public void Error(string message, Exception exception)
        {
            var detail = exception == null ? message : string.Format("{0} | {1}", message, exception.Message);
            Write("ERROR", detail, exception);
        }

        /// <summary>
        /// 释放当前实例所占用的资源。在释放前会执行最后一次 UI 队列刷新，
        /// 确保所有待处理的日志消息都被显示。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            FlushUiQueue();
        }

        /// <summary>
        /// 写入一条格式化后的日志行到待处理队列中，并安排 UI 刷新。
        /// 如果实例已释放，则直接返回。
        /// </summary>
        /// <param name="level">日志级别字符串（如 "TRACE"、"INFO "、"WARN "、"ERROR"）。</param>
        /// <param name="message">日志消息文本。</param>
        /// <param name="exception">与日志关联的异常；可为 null。若提供，会附加其堆栈跟踪信息。</param>
        private void Write(string level, string message, Exception exception)
        {
            if (_disposed)
            {
                return;
            }

            var line = string.Format("[{0:HH:mm:ss}] {1} {2}", DateTime.Now, level, message ?? string.Empty);
            if (exception != null && !string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                line = line + Environment.NewLine + exception.StackTrace;
            }

            _pendingUiMessages.Enqueue(line);
            LogDisplayHelper.ShowMsg(line);
            ScheduleUiFlush();
        }

        /// <summary>
        /// 安排一次 UI 刷新操作。使用 <see cref="Interlocked.Exchange"/> 确保同一时间只安排一个刷新任务。
        /// 刷新任务在延迟 <see cref="UiFlushDelayMilliseconds"/> 毫秒后通过 <see cref="Dispatcher.BeginInvoke(Action)"/>
        /// 在 UI 线程上执行 <see cref="FlushUiQueue"/>。
        /// </summary>
        private void ScheduleUiFlush()
        {
            if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(UiFlushDelayMilliseconds).ConfigureAwait(false);
                    _ = _dispatcher.BeginInvoke(new Action(FlushUiQueue));
                }
                catch
                {
                    // 如果延迟或派发过程中发生异常，重置刷新标记以允许后续重试
                    Interlocked.Exchange(ref _flushScheduled, 0);
                }
            });
        }

        /// <summary>
        /// 在 UI 线程上执行的刷新方法。将 <see cref="_pendingUiMessages"/> 队列中的所有待处理消息
        /// 取出组成一个批次，通过 <see cref="_appendBatch"/> 回调传递给 UI 显示。
        /// 如果在刷新过程中又有新消息入队且实例尚未释放，则重新安排一次 UI 刷新。
        /// </summary>
        private void FlushUiQueue()
        {
            var batch = new List<string>();
            string line;
            while (_pendingUiMessages.TryDequeue(out line))
            {
                batch.Add(line);
            }

            Interlocked.Exchange(ref _flushScheduled, 0);

            if (batch.Count > 0)
            {
                _appendBatch(batch);
            }

            if (!_pendingUiMessages.IsEmpty && !_disposed)
            {
                ScheduleUiFlush();
            }
        }
    }
}
