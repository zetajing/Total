using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Exceptions;
using InduLink.Protocols.OpcUa;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using ReadRequest = InduLink.Abstractions.ReadRequest;
using DataType = InduLink.Abstractions.DataType;

// The fixture uses the reference stack's synchronous node manager for two small nodes.
#pragma warning disable CS0618
namespace InduLink.Tests
{
    [TestFixture, NonParallelizable, Category("OpcUaIntegration")]
    public sealed class OpcUaServerIntegrationTests
    {
        [Test]
        public async Task SecureEndpointRejectsUntrustedCertificateAndAcceptsExplicitTrust()
        {
            await using var fixture = await LocalServer.CreateAsync();
            var options = fixture.ClientOptions();
            using var client = new OpcUaClient(options);
            Assert.ThrowsAsync<InduLinkConnectionException>(() => client.ConnectAsync(CancellationToken.None));
            Assert.That(client.IsConnected, Is.False);
            await fixture.TrustServerAsync(options.CertificateStoreDirectory);
            await client.ConnectAsync(CancellationToken.None);
            var result = await client.ReadAsync(new ReadRequest("ua", "ns=2;s=Line/Speed", DataType.Int32), CancellationToken.None);
            Assert.That(result.Quality, Is.EqualTo(QualityStatus.Good));
            Assert.That(result.Value, Is.EqualTo(11));
            var other = await client.ReadAsync(new ReadRequest("ua", "ns=2;s=line/speed", DataType.Int32), CancellationToken.None);
            Assert.That(other.Value, Is.EqualTo(22));
        }

        [Test]
        public async Task NativeSubscriptionIsRestoredAfterServerRestart()
        {
            await using var fixture = await LocalServer.CreateAsync();
            var options = fixture.ClientOptions();
            await fixture.TrustServerAsync(options.CertificateStoreDirectory);
            using var client = new OpcUaClient(options);
            await client.ConnectAsync(CancellationToken.None);
            var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var restored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new SubscriptionRequest("restart", "ua", new[] { new ReadRequest("ua", "ns=2;s=Line/Speed", DataType.Int32) }, TimeSpan.FromMilliseconds(100), true);
            var id = await client.SubscribeAsync(request, (_, e) =>
            {
                foreach (var value in e.Values)
                {
                    if (value.Quality != QualityStatus.Good) continue;
                    if (Convert.ToInt32(value.Value) == 11) first.TrySetResult(true);
                    if (Convert.ToInt32(value.Value) == 33) restored.TrySetResult(true);
                }
            }, CancellationToken.None);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.RestartAsync(33);
            await client.ConnectAsync(CancellationToken.None);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await client.UnsubscribeAsync(id, CancellationToken.None);
        }

        private sealed class LocalServer : IAsyncDisposable
        {
            private readonly string _directory = Path.Combine(Path.GetTempPath(), "indulink-opcua-" + Guid.NewGuid().ToString("N"));
            private ApplicationConfiguration _config;
            private TestServer _server;
            public string Url { get; private set; }

            public static async Task<LocalServer> CreateAsync()
            {
                var fixture = new LocalServer();
                try { await fixture.StartAsync(); return fixture; }
                catch { await fixture.DisposeAsync(); throw; }
            }

            private async Task StartAsync()
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                Url = "opc.tcp://localhost:" + port + "/InduLinkTest";
                _config = new ApplicationConfiguration
                {
                    ApplicationName = "InduLink Integration Server", ApplicationUri = "urn:localhost:InduLinkIntegration:" + Guid.NewGuid().ToString("N"), ApplicationType = ApplicationType.Server,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = Path.Combine(_directory, "server", "own"), SubjectName = "CN=InduLink Integration Server" },
                        TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(_directory, "server", "trusted") },
                        TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(_directory, "server", "issuers") },
                        RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(_directory, "server", "rejected") },
                        AutoAcceptUntrustedCertificates = true,
                    },
                    TransportQuotas = new TransportQuotas { OperationTimeout = 5000 },
                    ServerConfiguration = new ServerConfiguration
                    {
                        BaseAddresses = new StringCollection { Url },
                        SecurityPolicies = new ServerSecurityPolicyCollection { new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityPolicyUri = SecurityPolicies.Basic256Sha256 } },
                        UserTokenPolicies = new UserTokenPolicyCollection { new UserTokenPolicy(UserTokenType.Anonymous) },
                    },
                };
                await _config.ValidateAsync(ApplicationType.Server);
                var application = new ApplicationInstance { ApplicationConfiguration = _config };
                Assert.That(await application.CheckApplicationInstanceCertificatesAsync(true), Is.True);
                _server = new TestServer(11);
                await _server.StartAsync(_config, CancellationToken.None);
            }

            public OpcUaClientOptions ClientOptions() => new OpcUaClientOptions { DeviceId = "ua", EndpointUrl = Url, UseSecurity = true, CertificateStoreDirectory = Path.Combine(_directory, "client"), ConnectTimeoutMilliseconds = 5000 };

            public async Task TrustServerAsync(string clientPki)
            {
                var certificate = await _config.SecurityConfiguration.ApplicationCertificate.FindAsync(false);
                var directory = Path.Combine(clientPki, "trusted", "certs");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, certificate.Thumbprint + ".der"), certificate.RawData);
            }

            public async Task RestartAsync(int value)
            {
                await _server.StopAsync(CancellationToken.None);
                _server.Dispose();
                _server = new TestServer(value);
                await _server.StartAsync(_config, CancellationToken.None);
            }

            public async ValueTask DisposeAsync()
            {
                if (_server != null) { await _server.StopAsync(CancellationToken.None); _server.Dispose(); }
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
        }

        private sealed class TestServer : StandardServer
        {
            private readonly int _value;
            public TestServer(int value) { _value = value; }
            protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration) =>
                new MasterNodeManager(server, configuration, null, new TestNodes(server, configuration, _value));
            protected override ServerProperties LoadServerProperties() => new ServerProperties { ManufacturerName = "InduLink Tests", ProductName = "Local OPC UA fixture", ProductUri = "urn:indulink:test", SoftwareVersion = "1.0", BuildNumber = "1", BuildDate = DateTime.UtcNow };
        }

        private sealed class TestNodes : CustomNodeManager2
        {
            private readonly int _value;
            public TestNodes(IServerInternal server, ApplicationConfiguration config, int value) : base(server, config, "urn:indulink:integration:nodes") { _value = value; }
            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references)) externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                foreach (var name in new[] { "Line/Speed", "line/speed" })
                {
                    var node = new BaseDataVariableState(null)
                    {
                        NodeId = new NodeId(name, NamespaceIndex), BrowseName = new QualifiedName(name, NamespaceIndex), DisplayName = name,
                        TypeDefinitionId = VariableTypeIds.BaseDataVariableType, DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentReadOrWrite, UserAccessLevel = AccessLevels.CurrentReadOrWrite, Value = name == "Line/Speed" ? _value : 22,
                        StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow,
                    };
                    node.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                    references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                    AddPredefinedNode(SystemContext, node);
                }
            }
        }
    }
}
