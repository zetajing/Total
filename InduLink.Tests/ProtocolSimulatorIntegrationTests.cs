using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Exceptions;
using InduLink.Protocols.Modbus;
using InduLink.Protocols.OpcUa;
using InduLink.Runtime;
using InduLink.Simulation;
using NUnit.Framework;
using Opc.Ua.Client;
using Ua = Opc.Ua;

namespace InduLink.Tests
{
    [TestFixture, NonParallelizable, Category("SimulatorIntegration")]
    public sealed class ProtocolSimulatorIntegrationTests
    {
        [Test]
        public async Task ModbusAllAreasAndNumericWidthsRoundTripAndRestartReleasesPort()
        {
            var points = new[]
            {
                new SimulationPoint("C0", DataType.Bool, "true"),
                new SimulationPoint("DI0", DataType.Bool, "false"),
                new SimulationPoint("HR0", DataType.Int16, "-123"),
                new SimulationPoint("HR1", DataType.UInt16, "65535"),
                new SimulationPoint("HR2", DataType.Int32, "-1234567"),
                new SimulationPoint("HR4", DataType.UInt32, "4000000000"),
                new SimulationPoint("HR6", DataType.Float, "12.5"),
                new SimulationPoint("HR8", DataType.Double, "-23.125"),
                new SimulationPoint("IR0", DataType.UInt16, "42"),
            };
            var port = FreePort();
            await using var simulator = new ModbusTcpSimulator(points, port: port);
            await simulator.StartAsync();
            using var client = ModbusClient(port);
            await client.ConnectAsync();
            foreach (var point in points)
            {
                var value = await client.ReadAsync(new ReadRequest("modbus", point.Address, point.DataType), CancellationToken.None);
                Assert.That(value.Quality, Is.EqualTo(QualityStatus.Good), point.Address);
                Assert.That(value.Value, Is.EqualTo(point.ParseValue(point.InitialValue)), point.Address);
            }
            await client.WriteAsync(new WriteRequest("modbus", "HR6", DataType.Float, -31.25f), CancellationToken.None);
            await client.WriteAsync(new WriteRequest("modbus", "HR8", DataType.Double, 9876.5d), CancellationToken.None);
            await client.WriteAsync(new WriteRequest("modbus", "C0", DataType.Bool, false), CancellationToken.None);
            Assert.That(simulator.ReadValues().Single(value => value.Address == "HR6").Value, Is.EqualTo(-31.25f));
            Assert.That(simulator.ReadValues().Single(value => value.Address == "HR8").Value, Is.EqualTo(9876.5d));
            Assert.That(simulator.ReadValues().Single(value => value.Address == "C0").Value, Is.False);
            simulator.SetValue("IR0", "321");
            Assert.That(await client.ReadUInt16Async("IR0"), Is.EqualTo(321));
            await client.DisconnectAsync();
            await simulator.StopAsync();
            AssertPortReleased(port);
            await simulator.StartAsync();
            await client.ConnectAsync();
            Assert.That(await client.ReadFloatAsync("HR6"), Is.EqualTo(12.5f));
            await client.DisconnectAsync();
        }

        [Test]
        public async Task ModbusGeneratorsChangeRealReadsAndDisposedServerReleasesConnectedClients()
        {
            var simulator = new ModbusTcpSimulator(new[]
            {
                new SimulationPoint("HR0", DataType.Int32, "0", SimulationBehavior.Increment, 2),
                new SimulationPoint("C0", DataType.Bool, "false", SimulationBehavior.Toggle),
            }, port: 0, interval: TimeSpan.FromMilliseconds(50));
            try
            {
                await simulator.StartAsync();
                var port = simulator.Port;
                using var client = ModbusClient(port);
                await client.ConnectAsync();
                await WaitUntilAsync(async () => await client.ReadInt32Async("HR0") >= 2);
                var observed = new HashSet<bool>();
                await WaitUntilAsync(async () => { observed.Add(await client.ReadBoolAsync("C0")); return observed.Count == 2; });
                await simulator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                Assert.That(simulator.IsRunning, Is.False);
                AssertPortReleased(port);
                Assert.ThrowsAsync<ObjectDisposedException>(() => simulator.StartAsync());
            }
            finally { await simulator.DisposeAsync(); }
        }

        [Test]
        public async Task OpcUaTypedNodesWritesAndGeneratedSubscriptionChangesWorkAfterRestart()
        {
            var directory = TemporaryPki();
            var port = FreePort();
            try
            {
                await using var simulator = new OpcUaSimulator(new[]
                {
                    new SimulationPoint("ns=2;s=Demo/Counter", DataType.Int32, "0", SimulationBehavior.Increment),
                    new SimulationPoint("ns=2;s=Line/Speed", DataType.Float, "12.5"),
                    new SimulationPoint("ns=2;s=line/speed", DataType.Float, "22.5"),
                    new SimulationPoint("ns=2;s=Demo/Status", DataType.String, "Ready"),
                    new SimulationPoint("ns=2;s=Demo/Running", DataType.Bool, "true"),
                }, endpoint: "opc.tcp://localhost:" + port + "/Simulator", certificateStoreDirectory: directory, interval: TimeSpan.FromMilliseconds(100));
                await simulator.StartAsync();
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Where(listener => listener.Port == port).ToArray();
                Assert.That(listeners, Is.Not.Empty);
                Assert.That(listeners.All(listener => IPAddress.IsLoopback(listener.Address)), Is.True, "Default OPC UA endpoint must only listen on loopback.");
                await AssertNodesBrowsableAsync(simulator.Endpoint, directory);
                using var client = new OpcUaClient(new OpcUaClientOptions { DeviceId = "ua", EndpointUrl = simulator.Endpoint, UseSecurity = false });
                await client.ConnectAsync();
                Assert.That(await client.ReadFloatAsync("ns=2;s=Line/Speed"), Is.EqualTo(12.5f));
                Assert.That(await client.ReadFloatAsync("ns=2;s=line/speed"), Is.EqualTo(22.5f));
                Assert.That(await client.ReadBoolAsync("ns=2;s=Demo/Running"), Is.True);
                await client.WriteAsync(new WriteRequest("ua", "ns=2;s=Demo/Status", DataType.String, "Written"), CancellationToken.None);
                Assert.That(simulator.ReadValues().Single(value => value.Address == "ns=2;s=Demo/Status").Value, Is.EqualTo("Written"));
                var subscriptionValues = new HashSet<int>();
                var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var subscription = await client.SubscribeAsync(new SubscriptionRequest("generated", "ua", new[]
                {
                    new ReadRequest("ua", "ns=2;s=Demo/Counter", DataType.Int32),
                }, TimeSpan.FromMilliseconds(100), true), (_, args) =>
                {
                    lock (subscriptionValues)
                        foreach (var value in args.Values.Where(value => value.Quality == QualityStatus.Good))
                        {
                            subscriptionValues.Add(Convert.ToInt32(value.Value));
                            if (subscriptionValues.Count >= 2) changed.TrySetResult(true);
                        }
                }, CancellationToken.None);
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await client.UnsubscribeAsync(subscription, CancellationToken.None);
                simulator.SetValue("ns=2;s=Line/Speed", "88.25");
                Assert.That(await client.ReadFloatAsync("ns=2;s=Line/Speed"), Is.EqualTo(88.25f));
                await client.DisconnectAsync();
                await simulator.StopAsync();
                AssertPortReleased(port);
                await simulator.StartAsync();
                await client.ConnectAsync();
                Assert.That(await client.ReadFloatAsync("ns=2;s=Line/Speed"), Is.EqualTo(12.5f));
                await simulator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                AssertPortReleased(port);
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public async Task SecureOpcUaRequiresExplicitMutualCertificateTrust()
        {
            var directory = TemporaryPki();
            try
            {
                var serverPki = Path.Combine(directory, "server");
                var clientPki = Path.Combine(directory, "client");
                await using var simulator = new OpcUaSimulator(new[] { new SimulationPoint("Speed", DataType.Int16, "123") },
                    "opc.tcp://localhost:" + FreePort() + "/SecureSimulator", true, serverPki);
                await simulator.StartAsync();
                using var client = new OpcUaClient(new OpcUaClientOptions
                {
                    DeviceId = "secure", EndpointUrl = simulator.Endpoint, UseSecurity = true, CertificateStoreDirectory = clientPki,
                });
                Assert.ThrowsAsync<InduLinkConnectionException>(() => client.ConnectAsync());
                await simulator.StopAsync();
                TrustCertificates(serverPki, clientPki);
                TrustCertificates(clientPki, serverPki);
                await simulator.StartAsync();
                await client.ConnectAsync();
                Assert.That(await client.ReadInt16Async(simulator.GetNodeId("Speed")), Is.EqualTo(123));
                await client.DisconnectAsync();
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public async Task PortConflictAndCancellationLeaveSimulatorRestartable()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await using var simulator = new ModbusTcpSimulator(new[] { new SimulationPoint("HR0", DataType.Int16, "7") }, port: port);
            Assert.ThrowsAsync<SocketException>(() => simulator.StartAsync());
            Assert.That(simulator.IsRunning, Is.False);
            listener.Stop();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => simulator.StartAsync(cancelled.Token));
            await simulator.StartAsync();
            using var client = ModbusClient(port);
            await client.ConnectAsync();
            Assert.That(await client.ReadInt16Async("HR0"), Is.EqualTo(7));
            await client.DisconnectAsync();
        }

        [Test]
        public async Task OpcUaPortConflictLeavesServiceRestartable()
        {
            var directory = TemporaryPki();
            try
            {
                using var listener = new TcpListener(IPAddress.IPv6Any, 0);
                listener.Server.DualMode = true;
                listener.ExclusiveAddressUse = true;
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                await using var simulator = new OpcUaSimulator(new[] { new SimulationPoint("Value", DataType.Int16, "7") },
                    "opc.tcp://localhost:" + port + "/Conflict", certificateStoreDirectory: directory);
                Assert.CatchAsync<Exception>(() => simulator.StartAsync());
                Assert.That(simulator.IsRunning, Is.False);
                listener.Stop();
                await simulator.StartAsync();
                using var client = new OpcUaClient(new OpcUaClientOptions { DeviceId = "ua", EndpointUrl = simulator.Endpoint, UseSecurity = false });
                await client.ConnectAsync();
                Assert.That(await client.ReadInt16Async(simulator.GetNodeId("Value")), Is.EqualTo(7));
                await client.DisconnectAsync();
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void OverlappingAliasesAndInvalidGeneratorsAreRejectedBeforeListening()
        {
            Assert.Throws<ArgumentException>(() => new ModbusTcpSimulator(new[]
            {
                new SimulationPoint("HR0", DataType.Int32, "1"), new SimulationPoint("40002", DataType.Int16, "2"),
            }));
            Assert.Throws<ArgumentException>(() => new ModbusTcpSimulator(new[] { new SimulationPoint("HR65535", DataType.Double, "1") }));
            Assert.Throws<ArgumentException>(() => new OpcUaSimulator(new[]
            {
                new SimulationPoint("Name", DataType.Int16, "1"), new SimulationPoint("ns=2;s=Name", DataType.Int16, "2"),
            }));
            Assert.Throws<ArgumentException>(() => new SimulationPoint("HR0", DataType.Int16, "1", SimulationBehavior.Increment, 0.5));
            Assert.Throws<ArgumentException>(() => new SimulationPoint("C0", DataType.Bool, "false", SimulationBehavior.Increment));
        }

        private static async Task AssertNodesBrowsableAsync(string endpoint, string directory)
        {
            var pki = Path.Combine(directory, "browser");
            var config = new Ua.ApplicationConfiguration
            {
                ApplicationName = "InduLink Browse Test", ApplicationUri = "urn:localhost:InduLinkBrowseTest", ApplicationType = Ua.ApplicationType.Client,
                SecurityConfiguration = new Ua.SecurityConfiguration
                {
                    ApplicationCertificate = new Ua.CertificateIdentifier { StoreType = "Directory", StorePath = Path.Combine(pki, "own"), SubjectName = "CN=InduLink Browse Test" },
                    TrustedPeerCertificates = new Ua.CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "trusted") },
                    TrustedIssuerCertificates = new Ua.CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "issuers") },
                    RejectedCertificateStore = new Ua.CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "rejected") },
                },
                TransportQuotas = new Ua.TransportQuotas { OperationTimeout = 5000 },
                ClientConfiguration = new Ua.ClientConfiguration { DefaultSessionTimeout = 10000 },
            };
            await config.ValidateAsync(Ua.ApplicationType.Client);
            var selected = await CoreClientUtils.SelectEndpointAsync(config, endpoint, false, 5000, null, CancellationToken.None);
            using var session = await new DefaultSessionFactory(null).CreateAsync(config,
                new Ua.ConfiguredEndpoint(null, selected, Ua.EndpointConfiguration.Create(config)), false, false,
                "Browser", 10000, new Ua.UserIdentity(new Ua.AnonymousIdentityToken()), null, CancellationToken.None);
            var response = await session.BrowseAsync(null, null, 0, new Ua.BrowseDescriptionCollection
            {
                new Ua.BrowseDescription { NodeId = Ua.ObjectIds.ObjectsFolder, BrowseDirection = Ua.BrowseDirection.Forward,
                    ReferenceTypeId = Ua.ReferenceTypeIds.Organizes, IncludeSubtypes = true, NodeClassMask = (uint)Ua.NodeClass.Variable,
                    ResultMask = (uint)Ua.BrowseResultMask.All },
            }, CancellationToken.None);
            Assert.That(response.Results[0].References.Select(reference => reference.NodeId.ToString()),
                Does.Contain("ns=2;s=Demo/Counter").And.Contain("ns=2;s=Line/Speed").And.Contain("ns=2;s=line/speed"));
            await session.CloseAsync(CancellationToken.None);
        }

        private static ModbusTcpClient ModbusClient(int port) => new ModbusTcpClient(new ModbusTcpClientOptions
        {
            DeviceId = "modbus", Host = "127.0.0.1", Port = port, DeviceProfile = ModbusDeviceProfiles.Generic,
        });
        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        private static void AssertPortReleased(int port)
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            Assert.DoesNotThrow(() => listener.Start());
        }
        private static string TemporaryPki()
        {
            var directory = Path.Combine(Path.GetTempPath(), "indulink-simulator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
        private static void TrustCertificates(string sourcePki, string destinationPki)
        {
            var trusted = Path.Combine(destinationPki, "trusted", "certs");
            Directory.CreateDirectory(trusted);
            foreach (var certificate in Directory.GetFiles(Path.Combine(sourcePki, "own", "certs"), "*.der"))
                File.Copy(certificate, Path.Combine(trusted, Path.GetFileName(certificate)), true);
        }
        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!await condition()) await Task.Delay(20, timeout.Token);
        }
    }
}
