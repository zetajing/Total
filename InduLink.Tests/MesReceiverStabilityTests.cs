using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Mes;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class MesReceiverStabilityTests
    {
        [Test]
        public void MaxConcurrentRequestsMustBePositive()
        {
            var options = CreateOptions();
            options.MaxConcurrentRequests = 0;

            Assert.Throws<ArgumentOutOfRangeException>(() => new MesJsonReceiver(
                options,
                (request, token) => Task.FromResult(new MesJsonReceiveResponse())));
        }

        [Test]
        public void RequestBodyTimeoutMustBePositive()
        {
            var options = CreateOptions();
            options.RequestBodyTimeoutMilliseconds = 0;

            Assert.Throws<ArgumentOutOfRangeException>(() => new MesJsonReceiver(
                options,
                (request, token) => Task.FromResult(new MesJsonReceiveResponse())));
        }

        [Test]
        public void DemoReceiverConfigurationDefaultsLegacyBodyTimeoutToFiveSeconds()
        {
            var demoJsonType = Type.GetType(
                "InduLinkDemo.Helpers.MesDemoJson, InduLinkDemo",
                true);
            var createDefault = demoJsonType.GetMethod(
                "CreateDefaultReceiverConfiguration",
                BindingFlags.Static | BindingFlags.Public);
            var parse = demoJsonType.GetMethod(
                "ParseReceiverConfiguration",
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(createDefault, Is.Not.Null);
            Assert.That(parse, Is.Not.Null);

            var template = (string)createDefault.Invoke(null, null);
            Assert.That(template, Does.Contain("\"requestBodyTimeoutMilliseconds\": 5000"));

            const string legacyConfiguration =
                "{\"listenPrefix\":\"http://127.0.0.1:8081/mes/\"," +
                "\"maxConcurrentRequests\":1,\"maxRequestContentBytes\":1024," +
                "\"handlerTimeoutMilliseconds\":1000,\"responseStatusCode\":200," +
                "\"responseJson\":{}}";
            var parsed = parse.Invoke(null, new object[] { legacyConfiguration });
            var optionsProperty = parsed.GetType().GetProperty("Options");
            Assert.That(optionsProperty, Is.Not.Null);
            var parsedOptions = (MesJsonReceiverOptions)optionsProperty.GetValue(parsed, null);
            Assert.That(parsedOptions.RequestBodyTimeoutMilliseconds, Is.EqualTo(5000));
        }

        [Test]
        public async Task SlowRequestBodyTimesOutAndReleasesItsRequestCapacity()
        {
            var options = CreateOptions();
            options.MaxConcurrentRequests = 1;
            options.RequestBodyTimeoutMilliseconds = 250;
            var handlerCalls = 0;

            using (var receiver = new MesJsonReceiver(options, (request, token) =>
            {
                Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(new MesJsonReceiveResponse());
            }))
            {
                await receiver.StartAsync(CancellationToken.None);
                var prefix = new Uri(options.ListenPrefix);

                using (var client = new TcpClient())
                {
                    await CompleteWithinAsync(client.ConnectAsync(IPAddress.Loopback, prefix.Port), 3000);
                    var stream = client.GetStream();
                    var rawRequest = string.Format(
                        "POST {0}slow-body HTTP/1.1\r\n" +
                        "Host: 127.0.0.1:{1}\r\n" +
                        "Content-Type: application/json\r\n" +
                        "Content-Length: 64\r\n" +
                        "Connection: close\r\n\r\n" +
                        "{{\"partial\":",
                        prefix.AbsolutePath,
                        prefix.Port);
                    var requestBytes = Encoding.ASCII.GetBytes(rawRequest);
                    await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
                    await stream.FlushAsync();

                    var trickleTask = Task.Run(async () =>
                    {
                        try
                        {
                            var oneByte = new[] { (byte)' ' };
                            for (var index = 0; index < 10; index++)
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                                await stream.WriteAsync(oneByte, 0, oneByte.Length).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex) when (
                            ex is IOException || ex is ObjectDisposedException || ex is SocketException)
                        {
                        }
                    });
                    var stopwatch = Stopwatch.StartNew();
                    var rawResponse = await ReadUntilAsync(
                        stream,
                        "request_body_timeout",
                        3000);
                    stopwatch.Stop();
                    Assert.That(rawResponse, Does.StartWith("HTTP/1.1 408"));
                    Assert.That(rawResponse, Does.Contain("request_body_timeout"));
                    Assert.That(
                        stopwatch.Elapsed,
                        Is.LessThan(TimeSpan.FromMilliseconds(900)),
                        "The body timeout was reset by trickled bytes instead of using one absolute deadline.");
                    await CompleteWithinAsync(trickleTask, 3000);
                }

                Assert.That(Volatile.Read(ref handlerCalls), Is.Zero);
                using (var response = await CompleteWithinAsync(
                    PostJsonAsync(options.ListenPrefix + "after-body-timeout"), 3000))
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                }

                Assert.That(Volatile.Read(ref handlerCalls), Is.EqualTo(1));
                await EventuallyAsync(
                    () => GetPrivateCollectionCount(receiver, "_activeRequests") == 0,
                    3000);
            }
        }

        [Test]
        public async Task StopImmediatelyInterruptsAnIncompleteRequestBody()
        {
            var options = CreateOptions();
            options.RequestBodyTimeoutMilliseconds = 15000;
            options.HandlerTimeoutMilliseconds = 5000;
            var handlerCalls = 0;

            using (var receiver = new MesJsonReceiver(options, (request, token) =>
            {
                Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(new MesJsonReceiveResponse());
            }))
            {
                await receiver.StartAsync(CancellationToken.None);
                var prefix = new Uri(options.ListenPrefix);
                using (var client = new TcpClient())
                {
                    await CompleteWithinAsync(client.ConnectAsync(IPAddress.Loopback, prefix.Port), 3000);
                    var rawRequest =
                        "POST " + prefix.AbsolutePath + "stop-body HTTP/1.1\r\n" +
                        "Host: 127.0.0.1:" + prefix.Port + "\r\n" +
                        "Content-Type: application/json\r\n" +
                        "Content-Length: 64\r\n\r\n" +
                        "{\"partial\":";
                    var requestBytes = Encoding.ASCII.GetBytes(rawRequest);
                    await client.GetStream().WriteAsync(requestBytes, 0, requestBytes.Length);
                    await EventuallyAsync(
                        () => GetPrivateCollectionCount(receiver, "_activeRequests") == 1,
                        3000);

                    var stopwatch = Stopwatch.StartNew();
                    await CompleteWithinAsync(receiver.StopAsync(CancellationToken.None), 2000);
                    stopwatch.Stop();
                    Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
                    Assert.That(receiver.IsRunning, Is.False);
                    Assert.That(GetPrivateCollectionCount(receiver, "_activeRequests"), Is.Zero);
                    Assert.That(Volatile.Read(ref handlerCalls), Is.Zero);
                }
            }
        }

        [Test]
        public async Task SynchronouslyBlockingHandlerTimesOutAndRetainsItsCapacityUntilItActuallyEnds()
        {
            var entered = NewSignal();
            var exited = NewSignal();
            var releaseHandler = new ManualResetEventSlim(false);
            var options = CreateOptions();
            options.MaxConcurrentRequests = 1;
            options.HandlerTimeoutMilliseconds = 150;
            MesJsonReceiver receiver = null;

            try
            {
                receiver = new MesJsonReceiver(options, (request, token) =>
                {
                    entered.TrySetResult(true);
                    releaseHandler.Wait();
                    exited.TrySetResult(true);
                    return Task.FromResult(new MesJsonReceiveResponse());
                });
                await receiver.StartAsync(CancellationToken.None);

                var firstPost = PostJsonAsync(options.ListenPrefix + "blocking");
                await CompleteWithinAsync(entered.Task, 3000);
                using (var firstResponse = await CompleteWithinAsync(firstPost, 3000))
                {
                    Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.GatewayTimeout));
                    Assert.That(await firstResponse.Content.ReadAsStringAsync(), Does.Contain("handler_timeout"));
                }

                var stopwatch = Stopwatch.StartNew();
                using (var rejected = await CompleteWithinAsync(
                    PostJsonAsync(options.ListenPrefix + "overloaded"), 2000))
                {
                    stopwatch.Stop();
                    Assert.That((int)rejected.StatusCode, Is.EqualTo(429));
                    Assert.That(await rejected.Content.ReadAsStringAsync(), Does.Contain("too_many_requests"));
                    Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
                }

                await CompleteWithinAsync(receiver.StopAsync(CancellationToken.None), 2000);
                Assert.That(receiver.IsRunning, Is.False);

                releaseHandler.Set();
                await CompleteWithinAsync(exited.Task, 3000);
            }
            finally
            {
                releaseHandler.Set();
                receiver?.Dispose();
                releaseHandler.Dispose();
            }
        }

        [Test]
        public async Task HandlerCanAwaitStopWithoutWaitingForItsOwnRequest()
        {
            var stopReturned = NewSignal();
            var options = CreateOptions();
            options.HandlerTimeoutMilliseconds = 1000;
            MesJsonReceiver receiver = null;

            try
            {
                receiver = new MesJsonReceiver(options, async (request, token) =>
                {
                    await receiver.StopAsync(CancellationToken.None);
                    stopReturned.TrySetResult(true);
                    return new MesJsonReceiveResponse();
                });
                await receiver.StartAsync(CancellationToken.None);

                var post = PostJsonAsync(options.ListenPrefix + "stop-from-handler");
                await CompleteWithinAsync(stopReturned.Task, 3000);
                Assert.That(receiver.IsRunning, Is.False);
                await ObserveCompletionAsync(post, 3000);
            }
            finally
            {
                receiver?.Dispose();
            }
        }

        [Test]
        public async Task DisposePreventsAStartThatAlreadyPassedTheFastDisposedCheck()
        {
            var receiver = new MesJsonReceiver(
                CreateOptions(),
                (request, token) => Task.FromResult(new MesJsonReceiveResponse()));
            using (receiver)
            {
                var lifecycleGate = GetPrivateField<SemaphoreSlim>(receiver, "_lifecycleGate");
                lifecycleGate.Wait();
                Task startTask;
                Task disposeTask;
                try
                {
                    // Start passes its first disposed check, then waits on the gate held by this
                    // test. Dispose marks the instance disposed before that gate is released.
                    startTask = receiver.StartAsync(CancellationToken.None);
                    disposeTask = Task.Run(() => receiver.Dispose());
                    Assert.That(
                        SpinWait.SpinUntil(() => GetPrivateInt32(receiver, "_disposed") != 0, 2000),
                        Is.True,
                        "Dispose did not enter the disposed state before the deadline.");
                }
                finally
                {
                    lifecycleGate.Release();
                }

                Assert.ThrowsAsync<ObjectDisposedException>(async () => await startTask.ConfigureAwait(false));
                await CompleteWithinAsync(disposeTask, 3000);
                Assert.That(receiver.IsRunning, Is.False);
            }
        }

        [Test]
        public async Task OverloadBurstKeepsTrackedLongLivedRequestsWithinConfiguredLimit()
        {
            var entered = NewSignal();
            var releaseHandler = new TaskCompletionSource<MesJsonReceiveResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var options = CreateOptions();
            options.MaxConcurrentRequests = 1;
            options.HandlerTimeoutMilliseconds = 15000;

            using (var receiver = new MesJsonReceiver(options, (request, token) =>
            {
                entered.TrySetResult(true);
                return releaseHandler.Task;
            }))
            {
                await receiver.StartAsync(CancellationToken.None);
                var admitted = PostJsonAsync(options.ListenPrefix + "admitted");
                await CompleteWithinAsync(entered.Task, 3000);

                try
                {
                    var rejected = new Task<HttpResponseMessage>[32];
                    for (var index = 0; index < rejected.Length; index++)
                        rejected[index] = PostJsonAsync(options.ListenPrefix + "overload-" + index);

                    for (var index = 0; index < rejected.Length; index++)
                    {
                        using (var response = await CompleteWithinAsync(rejected[index], 3000))
                        {
                            Assert.That((int)response.StatusCode, Is.EqualTo(429));
                            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("too_many_requests"));
                        }
                    }

                    Assert.That(GetPrivateCollectionCount(receiver, "_activeRequests"), Is.LessThanOrEqualTo(1));
                }
                finally
                {
                    releaseHandler.TrySetResult(new MesJsonReceiveResponse());
                }

                using (var response = await CompleteWithinAsync(admitted, 3000))
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }
        }

        [Test]
        public async Task UnexpectedAcceptLoopExitTransitionsToStoppedAndCanRestart()
        {
            var options = CreateOptions();
            using (var receiver = new MesJsonReceiver(
                options,
                (request, token) => Task.FromResult(new MesJsonReceiveResponse())))
            {
                await receiver.StartAsync(CancellationToken.None);
                var listener = GetPrivateField<HttpListener>(receiver, "_listener");

                // Closing the listener directly simulates an accept-loop failure that did not go
                // through StopAsync and therefore did not pre-transition the lifecycle state.
                listener.Close();
                await EventuallyAsync(() => !receiver.IsRunning, 3000);
                Assert.That(receiver.IsRunning, Is.False);

                await CompleteWithinAsync(receiver.StartAsync(CancellationToken.None), 3000);
                Assert.That(receiver.IsRunning, Is.True);
                await CompleteWithinAsync(receiver.StopAsync(CancellationToken.None), 3000);
            }
        }

        private static async Task<HttpResponseMessage> PostJsonAsync(string url)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            using (var content = new StringContent("{}", Encoding.UTF8, "application/json"))
                return await client.PostAsync(url, content).ConfigureAwait(false);
        }

        private static async Task<string> ReadUntilAsync(
            NetworkStream stream,
            string expectedText,
            int timeoutMilliseconds)
        {
            var result = new StringBuilder();
            var stopwatch = Stopwatch.StartNew();
            var buffer = new byte[1024];
            while (result.ToString().IndexOf(expectedText, StringComparison.Ordinal) < 0)
            {
                var remaining = timeoutMilliseconds - checked((int)stopwatch.ElapsedMilliseconds);
                Assert.That(remaining, Is.GreaterThan(0), "Response did not arrive before the deadline.");
                var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
                var deadlineTask = Task.Delay(remaining);
                Assert.That(
                    await Task.WhenAny(readTask, deadlineTask),
                    Is.SameAs(readTask),
                    "Response did not arrive before the deadline.");
                var read = await readTask.ConfigureAwait(false);
                if (read == 0) break;
                result.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            return result.ToString();
        }

        private static async Task ObserveCompletionAsync(Task task, int timeoutMilliseconds)
        {
            var deadline = Task.Delay(timeoutMilliseconds);
            Assert.That(await Task.WhenAny(task, deadline), Is.SameAs(task), "Operation did not complete before the deadline.");
            try { await task.ConfigureAwait(false); }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
        }

        private static async Task CompleteWithinAsync(Task task, int timeoutMilliseconds)
        {
            var deadline = Task.Delay(timeoutMilliseconds);
            Assert.That(await Task.WhenAny(task, deadline), Is.SameAs(task), "Operation did not complete before the deadline.");
            await task.ConfigureAwait(false);
        }

        private static async Task<T> CompleteWithinAsync<T>(Task<T> task, int timeoutMilliseconds)
        {
            var deadline = Task.Delay(timeoutMilliseconds);
            Assert.That(await Task.WhenAny(task, deadline), Is.SameAs(task), "Operation did not complete before the deadline.");
            return await task.ConfigureAwait(false);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
            where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Private field was not found: " + fieldName);
            return (T)field.GetValue(instance);
        }

        private static int GetPrivateInt32(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Private field was not found: " + fieldName);
            return (int)field.GetValue(instance);
        }

        private static int GetPrivateCollectionCount(object instance, string fieldName)
        {
            var collection = GetPrivateField<object>(instance, fieldName);
            var count = collection.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(count, Is.Not.Null, "Collection Count property was not found: " + fieldName);
            return (int)count.GetValue(collection, null);
        }

        private static async Task EventuallyAsync(Func<bool> condition, int timeoutMilliseconds)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition() && deadline.ElapsedMilliseconds < timeoutMilliseconds)
                await Task.Delay(10).ConfigureAwait(false);
            Assert.That(condition(), Is.True, "Condition did not become true before the deadline.");
        }

        private static MesJsonReceiverOptions CreateOptions()
        {
            return new MesJsonReceiverOptions
            {
                ListenPrefix = "http://127.0.0.1:" + GetFreePort() + "/mes/",
                HandlerTimeoutMilliseconds = 2000,
            };
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }
    }
}
