using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class TransportStabilityTests
    {
        [Test]
        public async Task ReceiveFrameAsync_DoesNotJoinPartialFramesAcrossReconnects()
        {
            var listener = StartListener(out var port);
            var firstBytesSent = NewSignal();
            var server = Task.Run(async () =>
            {
                using (var first = await listener.AcceptTcpClientAsync())
                using (var stream = first.GetStream())
                {
                    var partial = Encoding.UTF8.GetBytes("old");
                    await stream.WriteAsync(partial, 0, partial.Length);
                    firstBytesSent.TrySetResult(true);
                }

                using (var second = await listener.AcceptTcpClientAsync())
                using (var stream = second.GetStream())
                {
                    var complete = Encoding.UTF8.GetBytes("new\n");
                    await stream.WriteAsync(complete, 0, complete.Length);
                }
            });

            try
            {
                using (var client = CreateFramedClient(port, true))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    await WithTimeout(firstBytesSent.Task);

                    byte[] frame;
                    try
                    {
                        // Depending on when the FIN is observed, the transport either reconnects
                        // inside this receive or reports the closed generation to the caller.
                        frame = await WithTimeout(client.ReceiveFrameAsync(CancellationToken.None));
                    }
                    catch (IndustrialConnectionException)
                    {
                        frame = await WithTimeout(client.ReceiveFrameAsync(CancellationToken.None));
                    }

                    Assert.AreEqual("new", Encoding.UTF8.GetString(frame));
                }

                await WithTimeout(server);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task DisconnectAsync_ClearsBufferedPartialFrameBeforeExplicitReconnect()
        {
            var listener = StartListener(out var port);
            var server = Task.Run(async () =>
            {
                using (var first = await listener.AcceptTcpClientAsync())
                using (var stream = first.GetStream())
                {
                    var sticky = Encoding.UTF8.GetBytes("one\ntail");
                    await stream.WriteAsync(sticky, 0, sticky.Length);
                    var probe = new byte[1];
                    await stream.ReadAsync(probe, 0, probe.Length);
                }

                using (var second = await listener.AcceptTcpClientAsync())
                using (var stream = second.GetStream())
                {
                    var complete = Encoding.UTF8.GetBytes("two\n");
                    await stream.WriteAsync(complete, 0, complete.Length);
                }
            });

            try
            {
                using (var client = CreateFramedClient(port, false))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    Assert.AreEqual("one", Encoding.UTF8.GetString(await WithTimeout(client.ReceiveFrameAsync(CancellationToken.None))));

                    await client.DisconnectAsync(CancellationToken.None);
                    await client.ConnectAsync(CancellationToken.None);
                    Assert.AreEqual("two", Encoding.UTF8.GetString(await WithTimeout(client.ReceiveFrameAsync(CancellationToken.None))));
                }

                await WithTimeout(server);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task FramingFailure_ClearsInvalidBytesBeforeNextReceive()
        {
            var listener = StartListener(out var port);
            var sendValidFrame = NewSignal();
            var server = Task.Run(async () =>
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                {
                    await stream.WriteAsync(new byte[] { 0, 11 }, 0, 2);
                    await sendValidFrame.Task;
                    var valid = new byte[] { 0, 2, (byte)'o', (byte)'k' };
                    await stream.WriteAsync(valid, 0, valid.Length);
                }
            });

            try
            {
                using (var client = new FramedTcpClient(
                    CreateOptions(port, false),
                    new LengthPrefixMessageFramer(2, 10)))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    var framingFailure = await CaptureAsync(client.ReceiveFrameAsync(CancellationToken.None));
                    Assert.That(framingFailure, Is.TypeOf<IndustrialProtocolException>());

                    sendValidFrame.TrySetResult(true);
                    var frame = await WithTimeout(client.ReceiveFrameAsync(CancellationToken.None));
                    Assert.AreEqual("ok", Encoding.UTF8.GetString(frame));
                }

                await WithTimeout(server);
            }
            finally
            {
                sendValidFrame.TrySetResult(true);
                listener.Stop();
            }
        }

        [Test]
        public async Task Dispose_UnblocksActiveAndQueuedFrameReceivesWithObjectDisposedException()
        {
            var listener = StartListener(out var port);
            var accepted = NewSignal();
            var server = Task.Run(async () =>
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                {
                    accepted.TrySetResult(true);
                    var probe = new byte[1];
                    await stream.ReadAsync(probe, 0, probe.Length);
                }
            });

            var client = CreateFramedClient(port, false);
            try
            {
                await client.ConnectAsync(CancellationToken.None);
                await WithTimeout(accepted.Task);

                var activeReceive = client.ReceiveFrameAsync(CancellationToken.None);
                await Task.Delay(50);
                var queuedReceive = client.ReceiveFrameAsync(CancellationToken.None);
                await Task.Delay(20);

                await WithTimeout(Task.Run(() => client.Dispose()));
                var activeFailure = await CaptureAsync(activeReceive);
                var queuedFailure = await CaptureAsync(queuedReceive);

                Assert.That(activeFailure, Is.TypeOf<ObjectDisposedException>());
                Assert.That(queuedFailure, Is.TypeOf<ObjectDisposedException>());
                await WithTimeout(server);
            }
            finally
            {
                client.Dispose();
                listener.Stop();
            }
        }

        [Test]
        public async Task CancellingQueuedReceive_DoesNotClearActiveReceiversPartialFrame()
        {
            var listener = StartListener(out var port);
            var sendTail = NewSignal();
            var partialObserved = NewSignal();
            var server = Task.Run(async () =>
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                {
                    var head = Encoding.UTF8.GetBytes("head");
                    await stream.WriteAsync(head, 0, head.Length);
                    await sendTail.Task;
                    var tail = Encoding.UTF8.GetBytes("tail\n");
                    await stream.WriteAsync(tail, 0, tail.Length);
                }
            });

            try
            {
                using (var client = new FramedTcpClient(
                    CreateOptions(port, false),
                    new ObservingDelimiterFramer(partialObserved)))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    var activeReceive = client.ReceiveFrameAsync(CancellationToken.None);
                    await WithTimeout(partialObserved.Task);

                    using (var cancellation = new CancellationTokenSource())
                    {
                        var queuedReceive = client.ReceiveFrameAsync(cancellation.Token);
                        cancellation.Cancel();
                        var queuedFailure = await CaptureAsync(queuedReceive);
                        Assert.That(queuedFailure, Is.InstanceOf<OperationCanceledException>());
                    }

                    sendTail.TrySetResult(true);
                    var frame = await WithTimeout(activeReceive);
                    Assert.AreEqual("headtail", Encoding.UTF8.GetString(frame));
                }

                await WithTimeout(server);
            }
            finally
            {
                sendTail.TrySetResult(true);
                listener.Stop();
            }
        }

        [Test]
        public async Task CancelledDisconnect_DoesNotClearBufferWhenConnectionWasNotDisconnected()
        {
            var listener = StartListener(out var port);
            var sendTail = NewSignal();
            var server = Task.Run(async () =>
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                {
                    var first = Encoding.UTF8.GetBytes("one\ntail");
                    await stream.WriteAsync(first, 0, first.Length);
                    await sendTail.Task;
                    var last = Encoding.UTF8.GetBytes("end\n");
                    await stream.WriteAsync(last, 0, last.Length);
                }
            });

            try
            {
                using (var client = CreateFramedClient(port, false))
                {
                    await client.ConnectAsync(CancellationToken.None);
                    Assert.AreEqual("one", Encoding.UTF8.GetString(
                        await WithTimeout(client.ReceiveFrameAsync(CancellationToken.None))));

                    var transportGate = GetTransportGate(client);
                    await transportGate.WaitAsync();
                    try
                    {
                        using (var cancellation = new CancellationTokenSource())
                        {
                            var disconnect = client.DisconnectAsync(cancellation.Token);
                            cancellation.Cancel();
                            var failure = await CaptureAsync(disconnect);
                            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
                        }
                    }
                    finally
                    {
                        transportGate.Release();
                    }

                    sendTail.TrySetResult(true);
                    var frame = await WithTimeout(client.ReceiveFrameAsync(CancellationToken.None));
                    Assert.AreEqual("tailend", Encoding.UTF8.GetString(frame));
                }

                await WithTimeout(server);
            }
            finally
            {
                sendTail.TrySetResult(true);
                listener.Stop();
            }
        }

        [Test]
        public async Task TcpTransportDispose_DoesNotExposeNullReferenceToConcurrentReceives()
        {
            var listener = StartListener(out var port);
            var accepted = NewSignal();
            var server = Task.Run(async () =>
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                {
                    accepted.TrySetResult(true);
                    var probe = new byte[1];
                    await stream.ReadAsync(probe, 0, probe.Length);
                }
            });

            var client = new TcpTransportClient(CreateOptions(port, false));
            try
            {
                await client.ConnectAsync(CancellationToken.None);
                await WithTimeout(accepted.Task);

                var receives = new Task<byte[]>[6];
                for (var index = 0; index < receives.Length; index++)
                {
                    receives[index] = client.ReceiveAsync(1, CancellationToken.None);
                }

                await Task.Delay(50);
                await WithTimeout(Task.Run(() => client.Dispose()));
                for (var index = 0; index < receives.Length; index++)
                {
                    var failure = await CaptureAsync(receives[index]);
                    Assert.That(failure, Is.Not.Null);
                    Assert.That(failure, Is.Not.TypeOf<NullReferenceException>());
                }

                await WithTimeout(server);
            }
            finally
            {
                client.Dispose();
                listener.Stop();
            }
        }

        private static FramedTcpClient CreateFramedClient(int port, bool autoReconnect)
        {
            return new FramedTcpClient(
                CreateOptions(port, autoReconnect),
                new DelimiterMessageFramer(new byte[] { 10 }, 32));
        }

        private static TcpTransportOptions CreateOptions(int port, bool autoReconnect)
        {
            return new TcpTransportOptions
            {
                Host = "127.0.0.1",
                Port = port,
                AutoReconnect = autoReconnect,
                ConnectTimeoutMilliseconds = 2000,
                SendTimeoutMilliseconds = 2000,
                ReceiveTimeoutMilliseconds = 2000
            };
        }

        private static TcpListener StartListener(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return listener;
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static SemaphoreSlim GetTransportGate(FramedTcpClient client)
        {
            var transportField = typeof(FramedTcpClient).GetField(
                "_transport",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(transportField);
            var transport = transportField.GetValue(client);
            var gateField = typeof(TcpTransportClient).GetField(
                "_gate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(gateField);
            return (SemaphoreSlim)gateField.GetValue(transport);
        }

        private static async Task<Exception> CaptureAsync(Task task)
        {
            try
            {
                await WithTimeout(task);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static async Task WithTimeout(Task task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(5000));
            if (completed != task)
            {
                throw new TimeoutException("The loopback test operation did not complete in time.");
            }

            await task;
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(5000));
            if (completed != task)
            {
                throw new TimeoutException("The loopback test operation did not complete in time.");
            }

            return await task;
        }

        private sealed class ObservingDelimiterFramer : ITcpMessageFramer
        {
            private readonly DelimiterMessageFramer _inner =
                new DelimiterMessageFramer(new byte[] { 10 }, 32);
            private readonly TaskCompletionSource<bool> _partialObserved;

            public ObservingDelimiterFramer(TaskCompletionSource<bool> partialObserved)
            {
                _partialObserved = partialObserved;
            }

            public int MaximumFrameLength { get { return _inner.MaximumFrameLength; } }
            public byte[] Encode(byte[] payload) { return _inner.Encode(payload); }

            public bool TryExtractFrame(IList<byte> buffer, out byte[] payload)
            {
                var extracted = _inner.TryExtractFrame(buffer, out payload);
                if (!extracted && buffer.Count == 4)
                {
                    _partialObserved.TrySetResult(true);
                }

                return extracted;
            }
        }
    }
}
