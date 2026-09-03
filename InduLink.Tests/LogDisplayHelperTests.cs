using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using InduLink.Storage;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class LogDisplayHelperTests
    {
        [Test]
        public void StructuredEntry_WritesToChannelAndKeepsExceptionDetails()
        {
            var root = CreateTempDirectory();
            var timestamp = new DateTimeOffset(2026, 9, 2, 9, 48, 28, 124, TimeSpan.FromHours(8));
            var exception = new InvalidOperationException("outer", new ArgumentException("inner"));
            var engine = CreateEngine(root, timestamp);

            try
            {
                Assert.That(engine.TryWrite(new LogDisplayEntry(
                    timestamp,
                    "SDK",
                    LogDisplayLevel.Error,
                    "连接失败",
                    exception)), Is.True);
                Assert.That(engine.Shutdown(TimeSpan.FromSeconds(5)), Is.True);

                var file = Path.Combine(root, "SDK", "20260902_09.log");
                var text = File.ReadAllText(file);
                StringAssert.Contains("[2026-09-02 09:48:28.124] [SDK] [ERROR] 连接失败", text);
                StringAssert.Contains("System.InvalidOperationException: outer", text);
                StringAssert.Contains("System.ArgumentException: inner", text);
            }
            finally
            {
                engine.Shutdown(TimeSpan.FromSeconds(1));
                DeleteTempDirectory(root);
            }
        }

        [Test]
        public void LegacyEntry_MapsDemoAliasToAppDirectoryWithoutReformatting()
        {
            var root = CreateTempDirectory();
            var timestamp = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(8));
            var engine = CreateEngine(root, timestamp);

            try
            {
                Assert.That(engine.TryWrite(new LogDisplayEntry(
                    timestamp,
                    "Demo",
                    LogDisplayLevel.Info,
                    "[09:00:00] [APP] [INFO] 旧格式消息",
                    null,
                    true)), Is.True);
                Assert.That(engine.Shutdown(TimeSpan.FromSeconds(5)), Is.True);

                var file = Path.Combine(root, "APP", "20260902_10.log");
                var text = File.ReadAllText(file);
                Assert.That(text.Trim(), Is.EqualTo("[09:00:00] [APP] [INFO] 旧格式消息"));
            }
            finally
            {
                engine.Shutdown(TimeSpan.FromSeconds(1));
                DeleteTempDirectory(root);
            }
        }

        [Test]
        public void QueueOverflow_DropsOldestAndRaisesOneWarning()
        {
            var root = CreateTempDirectory();
            var providerEntered = new ManualResetEventSlim(false);
            var releaseProvider = new ManualResetEventSlim(false);
            var warnings = new ConcurrentBag<LogDisplayWarningEventArgs>();
            var engine = new LogDisplayEngine(
                () =>
                {
                    providerEntered.Set();
                    releaseProvider.Wait(TimeSpan.FromSeconds(10));
                    return root;
                },
                () => DateTimeOffset.Now,
                warning => warnings.Add(warning));

            try
            {
                Assert.That(engine.TryWrite(new LogDisplayEntry(
                    DateTimeOffset.Now,
                    "APP",
                    LogDisplayLevel.Info,
                    "first")), Is.True);
                Assert.That(providerEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

                for (var index = 0; index < LogDisplayEngine.QueueCapacity + 100; index++)
                {
                    engine.TryWrite(new LogDisplayEntry(
                        DateTimeOffset.Now,
                        "APP",
                        LogDisplayLevel.Trace,
                        "message-" + index));
                }

                Assert.That(warnings.Count(item => item.Kind == LogDisplayWarningKind.QueueOverflow), Is.EqualTo(1));
            }
            finally
            {
                releaseProvider.Set();
                engine.Shutdown(TimeSpan.FromSeconds(5));
                DeleteTempDirectory(root);
                releaseProvider.Dispose();
                providerEntered.Dispose();
            }
        }

        [Test]
        public void Shutdown_RejectsNewEntriesAndIsIdempotent()
        {
            var root = CreateTempDirectory();
            var engine = CreateEngine(root, DateTimeOffset.Now);

            try
            {
                Assert.That(engine.Shutdown(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(engine.Shutdown(TimeSpan.FromSeconds(1)), Is.True);
                Assert.That(engine.TryWrite(new LogDisplayEntry(
                    DateTimeOffset.Now,
                    "SDK",
                    LogDisplayLevel.Info,
                    "after-shutdown")), Is.False);
            }
            finally
            {
                engine.Shutdown(TimeSpan.FromSeconds(1));
                DeleteTempDirectory(root);
            }
        }

        [Test]
        public void WriteFailure_RaisesWarningAndDoesNotEscapeProducer()
        {
            var warnings = new ConcurrentBag<LogDisplayWarningEventArgs>();
            var engine = new LogDisplayEngine(
                () => throw new IOException("测试写入失败"),
                () => DateTimeOffset.Now,
                warning => warnings.Add(warning));

            try
            {
                Assert.That(() => engine.TryWrite(new LogDisplayEntry(
                    DateTimeOffset.Now,
                    "APP",
                    LogDisplayLevel.Info,
                    "write-failure")), Throws.Nothing);
                Assert.That(engine.Shutdown(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(warnings.Any(item => item.Kind == LogDisplayWarningKind.WriteFailure), Is.True);
            }
            finally
            {
                engine.Shutdown(TimeSpan.FromSeconds(1));
            }
        }

        private static LogDisplayEngine CreateEngine(string root, DateTimeOffset timestamp)
        {
            return new LogDisplayEngine(() => root, () => timestamp, null);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "InduLinkLogs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTempDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
