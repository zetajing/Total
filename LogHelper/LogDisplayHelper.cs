using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace LogHelper
{
    public static class LogDisplayHelper
    {
        private static readonly ConcurrentQueue<string> PendingMessages = new ConcurrentQueue<string>();
        private static readonly AutoResetEvent FlushSignal = new AutoResetEvent(false);
        private static readonly object SyncRoot = new object();
        private static readonly Thread WorkerThread;
        private static volatile bool _shutdownRequested;
        private static int _cleanupTick;

        static LogDisplayHelper()
        {
            WorkerThread = new Thread(ProcessLoop)
            {
                IsBackground = true,
                Name = "LogDisplayHelperWorker"
            };
            WorkerThread.Start();
        }

        public static void ShowMsg(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            PendingMessages.Enqueue(message);
            FlushSignal.Set();
        }

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

        private static void FlushPendingMessages()
        {
            var batch = new List<string>();
            string message;
            while (PendingMessages.TryDequeue(out message))
            {
                batch.Add(message);
                if (batch.Count >= 200)
                {
                    WriteBatch(batch);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                WriteBatch(batch);
            }
        }

        private static void WriteBatch(IReadOnlyCollection<string> batch)
        {
            if (batch == null || batch.Count == 0)
            {
                return;
            }

            try
            {
                var logDirectory = GetLogDirectory();
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
            }
        }

        private static string GetLogDirectory()
        {
            var appName = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Name ?? "IndustrialCommDemo";
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName,
                "Logs");
        }

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
            }
        }
    }
}
