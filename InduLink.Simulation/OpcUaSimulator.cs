using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using DataType = InduLink.Abstractions.DataType;

// This small in-memory address space uses the reference stack's synchronous node manager.
#pragma warning disable CS0618
namespace InduLink.Simulation
{
    /// <summary>Browsable OPC UA variables supporting actual client reads, writes and monitored items.</summary>
    public sealed class OpcUaSimulator : ProtocolSimulator
    {
        private const string NamespaceUri = "urn:indulink:simulator:points";
        private readonly string _endpoint;
        private readonly string _listenEndpoint;
        private readonly string _pkiDirectory;
        private readonly bool _useSecurity;
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, NodeId> _nodeIds = new Dictionary<string, NodeId>(StringComparer.Ordinal);
        private readonly object _valuesSync = new object();
        private SimulatorServer _server;
        private SimulatorNodes _nodes;

        public OpcUaSimulator(IEnumerable<SimulationPoint> points,
            string endpoint = "opc.tcp://localhost:4840/InduLinkSimulator", bool useSecurity = false,
            string certificateStoreDirectory = null, TimeSpan? interval = null) : base(points, StringComparer.Ordinal, interval)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "opc.tcp" || uri.Port < 1 || uri.Port > 65535)
                throw new ArgumentException("A valid opc.tcp endpoint with a port is required.", nameof(endpoint));
            _endpoint = endpoint;
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                _listenEndpoint = new UriBuilder(uri) { Host = IPAddress.Loopback.ToString() }.Uri.AbsoluteUri;
            else if (IPAddress.TryParse(uri.DnsSafeHost, out _))
                _listenEndpoint = endpoint;
            else throw new ArgumentException("Use localhost or a numeric local IP address for the OPC UA simulator.", nameof(endpoint));
            _useSecurity = useSecurity;
            _pkiDirectory = certificateStoreDirectory ?? DefaultCertificateStoreDirectory;
            var identifiers = new HashSet<NodeId>();
            foreach (var point in Points)
            {
                var nodeId = point.Address.StartsWith("ns=", StringComparison.Ordinal)
                    ? NodeId.Parse(point.Address) : new NodeId(point.Address, 2);
                if (nodeId.NamespaceIndex != 2 || nodeId.IdType != IdType.String || string.IsNullOrEmpty((string)nodeId.Identifier))
                    throw new ArgumentException("Simulator nodes require ns=2;s=<name> or a plain string name: " + point.Address);
                if (!identifiers.Add(nodeId)) throw new ArgumentException("Duplicate OPC UA node: " + nodeId);
                _nodeIds.Add(point.Address, nodeId);
                _values.Add(point.Address, point.ParseValue(point.InitialValue));
            }
        }

        public override string Endpoint => _endpoint;
        public static string DefaultCertificateStoreDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InduLink", "simulation", "opcua", "pki");
        public string CertificateStoreDirectory => _pkiDirectory;
        public string GetNodeId(string address) => _nodeIds[address].ToString();

        protected override async Task StartTransportAsync(CancellationToken cancellationToken)
        {
            var config = new ApplicationConfiguration
            {
                ApplicationName = "InduLink Simulator",
                ApplicationUri = "urn:" + Utils.GetHostName() + ":InduLinkSimulator",
                ProductUri = "urn:indulink:simulator",
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = Path.Combine(_pkiDirectory, "own"), SubjectName = "CN=InduLink Simulator" },
                    TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(_pkiDirectory, "trusted") },
                    TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(_pkiDirectory, "issuers") },
                    RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(_pkiDirectory, "rejected") },
                    AutoAcceptUntrustedCertificates = false,
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = 5000 },
                ServerConfiguration = new ServerConfiguration
                {
                    // The reference TCP stack binds DNS hostnames to all interfaces.
                    // Keep the advertised localhost alias while binding a numeric loopback address.
                    BaseAddresses = new StringCollection { _listenEndpoint },
                    AlternateBaseAddresses = new StringCollection { _endpoint },
                    SecurityPolicies = new ServerSecurityPolicyCollection
                    {
                        new ServerSecurityPolicy
                        {
                            SecurityMode = _useSecurity ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.None,
                            SecurityPolicyUri = _useSecurity ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None,
                        },
                    },
                    UserTokenPolicies = new UserTokenPolicyCollection { new UserTokenPolicy(UserTokenType.Anonymous) },
                    MinPublishingInterval = 50,
                },
            };
            await config.ValidateAsync(ApplicationType.Server, cancellationToken).ConfigureAwait(false);
            var application = new ApplicationInstance((ITelemetryContext)null) { ApplicationConfiguration = config };
            if (!await application.CheckApplicationInstanceCertificatesAsync(true, null, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Could not create the OPC UA simulator certificate.");
            _server = new SimulatorServer(this);
            await _server.StartAsync(config, cancellationToken).ConfigureAwait(false);
        }

        protected override async Task StopTransportAsync()
        {
            if (_server == null) return;
            try { await _server.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            finally
            {
                lock (_valuesSync)
                {
                    if (_nodes != null)
                        foreach (var point in Points) _values[point.Address] = _nodes.Read(point.Address);
                    _nodes = null;
                    _server.Dispose();
                    _server = null;
                }
            }
        }

        protected override object ReadPointValue(SimulationPoint point)
        {
            lock (_valuesSync) return _nodes == null ? _values[point.Address] : _nodes.Read(point.Address);
        }
        protected override void SetPointValue(SimulationPoint point, object value)
        {
            lock (_valuesSync)
            {
                _values[point.Address] = value;
                _nodes?.Write(point.Address, value);
            }
        }
        protected override void AdvancePoint(SimulationPoint point)
        {
            lock (_valuesSync)
            {
                if (_nodes == null) return;
                _nodes.Advance(point);
            }
        }

        private sealed class SimulatorServer : StandardServer
        {
            private readonly OpcUaSimulator _owner;
            public SimulatorServer(OpcUaSimulator owner) { _owner = owner; }
            protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
            {
                var nodes = new SimulatorNodes(server, configuration, _owner);
                return new MasterNodeManager(server, configuration, null, nodes);
            }
            protected override ServerProperties LoadServerProperties() => new ServerProperties
            {
                ManufacturerName = "InduLink", ProductName = "InduLink Protocol Simulator", ProductUri = "urn:indulink:simulator",
                SoftwareVersion = "1.0", BuildNumber = "1", BuildDate = DateTime.UtcNow,
            };
        }

        private sealed class SimulatorNodes : CustomNodeManager2
        {
            private readonly OpcUaSimulator _owner;
            private readonly Dictionary<string, BaseDataVariableState> _variables = new Dictionary<string, BaseDataVariableState>(StringComparer.Ordinal);
            public SimulatorNodes(IServerInternal server, ApplicationConfiguration config, OpcUaSimulator owner)
                : base(server, config, NamespaceUri) { _owner = owner; }

            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                if (NamespaceIndex != 2) throw new InvalidOperationException("Simulator node namespace was not assigned index 2.");
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                lock (_owner._valuesSync)
                {
                    foreach (var point in _owner.Points)
                    {
                        var nodeId = _owner._nodeIds[point.Address];
                        var node = new BaseDataVariableState(null)
                        {
                            NodeId = nodeId, BrowseName = new QualifiedName((string)nodeId.Identifier, NamespaceIndex), DisplayName = (string)nodeId.Identifier,
                            TypeDefinitionId = VariableTypeIds.BaseDataVariableType, DataType = TypeId(point.DataType), ValueRank = ValueRanks.Scalar,
                            AccessLevel = AccessLevels.CurrentReadOrWrite, UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                            Value = _owner._values[point.Address], StatusCode = StatusCodes.Good, Timestamp = DateTime.UtcNow,
                        };
                        node.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                        references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                        AddPredefinedNode(SystemContext, node);
                        _variables.Add(point.Address, node);
                    }
                    _owner._nodes = this;
                }
            }

            public object Read(string address) { lock (Lock) return _variables[address].Value; }
            public void Write(string address, object value) { lock (Lock) Update(_variables[address], value); }
            public void Advance(SimulationPoint point)
            {
                lock (Lock)
                {
                    var node = _variables[point.Address];
                    Update(node, point.NextValue(node.Value));
                }
            }
            private void Update(BaseDataVariableState node, object value)
            {
                node.Value = value;
                node.Timestamp = DateTime.UtcNow;
                node.StatusCode = StatusCodes.Good;
                node.ClearChangeMasks(SystemContext, false);
            }
            private static NodeId TypeId(DataType type)
            {
                switch (type)
                {
                    case DataType.Bool: return DataTypeIds.Boolean;
                    case DataType.Int16: return DataTypeIds.Int16;
                    case DataType.UInt16: return DataTypeIds.UInt16;
                    case DataType.Int32: return DataTypeIds.Int32;
                    case DataType.UInt32: return DataTypeIds.UInt32;
                    case DataType.Float: return DataTypeIds.Float;
                    case DataType.Double: return DataTypeIds.Double;
                    case DataType.String: return DataTypeIds.String;
                    default: throw new ArgumentOutOfRangeException(nameof(type));
                }
            }
        }
    }
}
