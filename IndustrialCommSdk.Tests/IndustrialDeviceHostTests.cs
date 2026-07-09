using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class IndustrialDeviceHostTests
    {
        [Test]
        public async Task StartAsync_Should_Connect_And_Forward_Polling_Values()
        {
            var directory = CreateTempDirectory();
            try
            {
                WritePointTable(directory);
                var config = IndustrialSdkConfig.FromJson(@"{ ""devices"": [{
                    ""name"": ""plc1"", ""protocol"": ""modbus-tcp"", ""host"": ""127.0.0.1"",
                    ""pointsFile"": ""points/plc1.json"", ""pollingIntervalMilliseconds"": 10,
                    ""reconnectDelayMilliseconds"": 20
                }] }");
                var client = new HostClient();
                var received = new TaskCompletionSource<IndustrialDeviceValuesEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

                using (var host = new IndustrialDeviceHost(config, directory, _ => client))
                {
                    host.ValuesReceived += (sender, args) => received.TrySetResult(args);
                    await host.StartAsync();

                    var completed = await Task.WhenAny(received.Task, Task.Delay(1000));

                    Assert.That(completed, Is.EqualTo(received.Task));
                    Assert.That(received.Task.Result.DeviceName, Is.EqualTo("plc1"));
                    Assert.That(received.Task.Result.Tags[0].Name, Is.EqualTo("Speed"));
                    Assert.That(host.Get("PLC1").IsStarted, Is.True);
                    Assert.That(client.ConnectCount, Is.EqualTo(1));
                    Assert.That(client.SubscribeCount, Is.EqualTo(1));

                    await host.StopAsync();
                    Assert.That(client.DisconnectCount, Is.EqualTo(1));
                    Assert.That(client.UnsubscribeCount, Is.EqualTo(1));
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public async Task StartAsync_Should_Reconnect_After_Initial_Connection_Failure()
        {
            var directory = CreateTempDirectory();
            try
            {
                WritePointTable(directory);
                var config = IndustrialSdkConfig.FromJson(@"{ ""devices"": [{
                    ""name"": ""plc1"", ""protocol"": ""modbus-tcp"", ""host"": ""127.0.0.1"",
                    ""pointsFile"": ""points/plc1.json"", ""reconnectDelayMilliseconds"": 10
                }] }");
                var client = new HostClient { FailFirstConnection = true };
                var reconnected = new TaskCompletionSource<IndustrialDeviceStateChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

                using (var host = new IndustrialDeviceHost(config, directory, _ => client))
                {
                    host.DeviceStateChanged += (sender, args) =>
                    {
                        if (args.Health.Status == ConnectionStatus.Connected)
                        {
                            reconnected.TrySetResult(args);
                        }
                    };
                    await host.StartAsync();

                    var completed = await Task.WhenAny(reconnected.Task, Task.Delay(1000));

                    Assert.That(completed, Is.EqualTo(reconnected.Task));
                    Assert.That(client.ConnectCount, Is.EqualTo(2));
                    Assert.That(reconnected.Task.Result.ErrorMessage, Is.Null);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void WritePointTable(string directory)
        {
            var pointsDirectory = Path.Combine(directory, "points");
            Directory.CreateDirectory(pointsDirectory);
            File.WriteAllText(Path.Combine(pointsDirectory, "plc1.json"), @"{ ""tags"": [{ ""name"": ""Speed"", ""address"": ""HR0"", ""type"": ""Int16"" }] }");
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "IndustrialCommSdkTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private sealed class HostClient : IIndustrialClient
        {
            private EventHandler<SubscriptionEvent> _subscriptionHandler;
            private ConnectionStatus _status = ConnectionStatus.Disconnected;

            public bool FailFirstConnection { get; set; }
            public int ConnectCount { get; private set; }
            public int DisconnectCount { get; private set; }
            public int SubscribeCount { get; private set; }
            public int UnsubscribeCount { get; private set; }

            public string DeviceId { get { return "host-test"; } }
            public ProtocolKind Kind { get { return ProtocolKind.ModbusTcp; } }
            public bool IsConnected { get { return _status == ConnectionStatus.Connected; } }

            public Task ConnectAsync(CancellationToken cancellationToken)
            {
                ConnectCount++;
                if (FailFirstConnection && ConnectCount == 1)
                {
                    _status = ConnectionStatus.Faulted;
                    throw new InvalidOperationException("Initial connection failed.");
                }

                _status = ConnectionStatus.Connected;
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                DisconnectCount++;
                _status = ConnectionStatus.Disconnected;
                return Task.CompletedTask;
            }

            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new DataValue(request.Address, request.DataType, (short)100, null, QualityStatus.Good, DateTimeOffset.UtcNow, null));
            }

            public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                var values = new List<DataValue>();
                foreach (var request in requests)
                {
                    values.Add(await ReadAsync(request, cancellationToken));
                }

                return new BatchReadResult(values);
            }

            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                SubscribeCount++;
                _subscriptionHandler = handler;
                Task.Run(async () =>
                {
                    var result = await ReadManyAsync(request.Items, CancellationToken.None);
                    _subscriptionHandler?.Invoke(this, new SubscriptionEvent(request.SubscriptionKey, result.Values, DateTimeOffset.UtcNow));
                });
                return Task.FromResult(request.SubscriptionKey);
            }

            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
            {
                UnsubscribeCount++;
                _subscriptionHandler = null;
                return Task.CompletedTask;
            }

            public HealthSnapshot GetHealth()
            {
                return new HealthSnapshot(_status, null, _status == ConnectionStatus.Faulted ? 1 : 0, _status == ConnectionStatus.Faulted ? "Initial connection failed." : null);
            }

            public void Dispose()
            {
                _status = ConnectionStatus.Disconnected;
            }
        }
    }
}
