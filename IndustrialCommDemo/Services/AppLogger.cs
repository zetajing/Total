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
    internal sealed class AppLogger : IIndustrialLogger, IDisposable
    {
        private const int UiFlushDelayMilliseconds = 100;

        private readonly Dispatcher _dispatcher;
        private readonly Action<IReadOnlyList<string>> _appendBatch;
        private readonly ConcurrentQueue<string> _pendingUiMessages = new ConcurrentQueue<string>();
        private int _flushScheduled;
        private bool _disposed;

        public AppLogger(Dispatcher dispatcher, Action<IReadOnlyList<string>> appendBatch)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _appendBatch = appendBatch ?? throw new ArgumentNullException(nameof(appendBatch));
        }

        public void Trace(string message)
        {
            Write("TRACE", message, null);
        }

        public void Info(string message)
        {
            Write("INFO ", message, null);
        }

        public void Warn(string message)
        {
            Write("WARN ", message, null);
        }

        public void Error(string message, Exception exception)
        {
            var detail = exception == null ? message : string.Format("{0} | {1}", message, exception.Message);
            Write("ERROR", detail, exception);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            FlushUiQueue();
        }

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
                    Interlocked.Exchange(ref _flushScheduled, 0);
                }
            });
        }

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
