using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Exceptions;
using InduLink.Protocols.Modbus;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class ModbusTcpLifecycleRegressionTests
    {
        [TestCase("read")]
        [TestCase("batch")]
        [TestCase("write")]
        [TestCase("batch-write")]
        public async Task CancelledTransaction_AllowsDisconnectAndFreshRead(string operation)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = "modbus-cancel", Host = "127.0.0.1",
                Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                DeviceProfile = ModbusDeviceProfiles.Generic, OperationTimeoutMilliseconds = 10000
            });
            await client.ConnectAsync(CancellationToken.None);
            using var peer = await listener.AcceptTcpClientAsync();
            using var cancellation = new CancellationTokenSource();
            var request = new ReadRequest(client.DeviceId, "HR0", DataType.UInt16);
            Task pending = operation == "batch-write"
                ? client.WriteManyAsync(new[] { new WriteRequest(client.DeviceId, "HR0", DataType.UInt16, (ushort)7) }, cancellation.Token)
                : operation == "write"
                ? client.WriteAsync(new WriteRequest(client.DeviceId, "HR0", DataType.UInt16, (ushort)7), cancellation.Token)
                : operation == "batch"
                    ? client.ReadManyAsync(new[] { request }, cancellation.Token)
                    : client.ReadAsync(request, cancellation.Token);
            try
            {
                var bytes = new byte[12];
                await peer.GetStream().ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                cancellation.Cancel();
                if (operation == "write" || operation == "batch-write")
                    Assert.ThrowsAsync<IndustrialWriteUncertainException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));
                else
                    Assert.CatchAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2)));

                await client.DisconnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.IsFalse(client.IsConnected);
                await client.ConnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
                using var replacement = await listener.AcceptTcpClientAsync();
                var next = client.ReadAsync(request, CancellationToken.None);
                await replacement.GetStream().ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                var response = new byte[] { bytes[0], bytes[1], 0, 0, 0, 5, bytes[6], 3, 2, 0, 42 };
                await replacement.GetStream().WriteAsync(response);
                var result = await next.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.AreEqual(QualityStatus.Good, result.Quality);
                Assert.AreEqual((ushort)42, result.Value);
            }
            finally
            {
                cancellation.Cancel();
                peer.Dispose();
                try { await pending; } catch { }
            }
        }
    }
}
