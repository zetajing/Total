using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Protocols.S7;
using InduLink.Runtime;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture, NonParallelizable, Category("Snap7Integration")]
    public sealed class Snap7ServerIntegrationTests
    {
        [Test]
        public async Task Snap7PerformsHandshakeReadsWritesAndReconnectsAfterRestart()
        {
            if (!OperatingSystem.IsWindows()) { Assert.Ignore("The Snap7 sidecar uses a Windows x86 library."); return; }
            var executable = Environment.GetEnvironmentVariable("INDULINK_TEST_SNAP7_SERVER");
            if (string.IsNullOrEmpty(executable)) Assert.Ignore("Set INDULINK_TEST_SNAP7_SERVER to a self-contained Snap7Server executable.");
            Assert.That(File.Exists(executable), Is.True, "The configured sidecar must exist.");
            var listener = new TcpListener(IPAddress.Loopback, 102);
            listener.Start(); // Fail rather than connect to an existing service or PLC.
            listener.Stop();
            Process server = null;
            using var client = new SiemensS7Client(new SiemensS7ClientOptions { DeviceId = "snap7", Host = "127.0.0.1", AutoReconnect = false, ConnectTimeoutMilliseconds = 2000, OperationTimeoutMilliseconds = 2000 });
            try
            {
                server = await StartAsync(executable);
                await client.ConnectAsync(CancellationToken.None);
                Assert.That(await client.ReadBoolAsync("DB1.DBX0.0"), Is.True);
                Assert.That(await client.ReadFloatAsync("DB1.DBD8"), Is.EqualTo(12.5f));
                await client.WriteAsync("DB1.DBW2", (short)1234);
                Assert.That(await client.ReadInt16Async("DB1.DBW2"), Is.EqualTo(1234));
                await client.WriteAsync("DB1.DBX0.1", true);
                Assert.That(await client.ReadBoolAsync("DB1.DBX0.1"), Is.True);
                server.Kill(true);
                await server.WaitForExitAsync();
                server.Dispose();
                var failed = await client.ReadAsync(new ReadRequest("snap7", "DB1.DBW2", DataType.Int16), CancellationToken.None);
                Assert.That(failed.Quality, Is.EqualTo(QualityStatus.Bad));
                server = await StartAsync(executable);
                await client.ConnectAsync(CancellationToken.None);
                Assert.That(await client.ReadFloatAsync("DB1.DBD8"), Is.EqualTo(12.5f));
            }
            finally
            {
                if (server != null) { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } server.Dispose(); }
            }
        }

        private static async Task<Process> StartAsync(string executable)
        {
            var process = Process.Start(new ProcessStartInfo(executable, "--address 127.0.0.1 --port 102 --float 12.5 --bool true")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            try
            {
                var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                if (ready == null || process.HasExited) throw new InvalidOperationException("Snap7Server failed to start: " + await process.StandardError.ReadToEndAsync());
                return process;
            }
            catch { if (!process.HasExited) process.Kill(true); process.Dispose(); throw; }
        }
    }
}
