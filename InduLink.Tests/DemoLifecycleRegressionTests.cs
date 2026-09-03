using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using InduLinkDemo.Services;
using InduLinkDemo.SocketDebug;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class DemoLifecycleRegressionTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void AppLogger_DisposeFlushesPendingMessages()
        {
            var received = new List<string>();
            var logger = new AppLogger(
                Dispatcher.CurrentDispatcher,
                messages => received.AddRange(messages));

            logger.Info("pending-before-dispose");
            logger.Dispose();

            Assert.That(received, Has.Count.EqualTo(1));
            StringAssert.Contains("pending-before-dispose", received[0]);
        }

        [Test]
        public void LineBasedTcpClient_AlreadyCanceledConnectReportsCancellation()
        {
            using (var client = new LineBasedTcpClient())
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await client.ConnectAsync("127.0.0.1", 1, cancellation.Token));
            }
        }
    }
}
