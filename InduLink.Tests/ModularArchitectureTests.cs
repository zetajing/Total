using System;
using System.Collections.Generic;
using System.Linq;
using InduLink.Abstractions;
using InduLink.Runtime.Configuration;
using InduLink.Diagnostics;
using InduLink.Protocols.Ads;
using InduLink.Protocols.Mc;
using InduLink.Protocols.Modbus;
using InduLink.Protocols.Mqtt;
using InduLink.Protocols.OpcUa;
using InduLink.Protocols.Redis;
using InduLink.Protocols.S7;
using InduLink.Runtime;
using InduLink.Storage;
using InduLink.Storage.MySql;
using Newtonsoft.Json;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class ModularArchitectureTests
    {
        private static readonly string[] CanonicalProtocols =
        {
            "ads", "mitsubishi-mc", "modbus-rtu", "modbus-tcp", "mqtt", "opc-ua", "redis", "siemens-s7",
        };

        [Test]
        public void DefaultSdk_RegistersExactlyTheCanonicalProtocols()
        {
            var sdk = InduLinkSdk.CreateDefault();
            CollectionAssert.AreEqual(CanonicalProtocols, sdk.Protocols.Providers.Select(item => item.Protocol).ToArray());
            foreach (var provider in sdk.Protocols.Providers)
            {
                var settings = provider.CreateDefaultSettings();
                Assert.AreEqual(provider.SettingsType, settings.GetType());
                Assert.AreEqual(provider.SettingsType,
                    sdk.Configuration.ParseSettings(provider.Protocol, sdk.Configuration.SerializeSettings(settings)).GetType());
            }
            Assert.Throws<KeyNotFoundException>(() => sdk.Protocols.Get("s7"));
            Assert.Throws<KeyNotFoundException>(() => sdk.Protocols.Get("mc"));
            Assert.Throws<KeyNotFoundException>(() => sdk.Protocols.Get("opcua"));
        }

        [TestCase(typeof(InduLinkConfiguredClient), "InduLink.Runtime")]
        [TestCase(typeof(BufferedInduLinkDataRecorder), "InduLink.Storage")]
        [TestCase(typeof(MySqlInduLinkDataStore), "InduLink.Storage.MySql")]
        public void ModulePublicTypes_StayInsideTheAssemblyNamespace(Type representativeType, string expectedNamespace)
        {
            var misplaced = representativeType.Assembly.GetExportedTypes()
                .Where(type => type.Namespace == null ||
                    (!string.Equals(type.Namespace, expectedNamespace, StringComparison.Ordinal) &&
                     !type.Namespace.StartsWith(expectedNamespace + ".", StringComparison.Ordinal)))
                .Select(type => type.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.That(misplaced, Is.Empty);
        }

        [Test]
        public void Registry_RejectsDuplicateProtocolAndWrongSettingsType()
        {
            var registry = new InduLinkProtocolRegistry();
            registry.Register(new StubProvider("test-protocol"));
            Assert.Throws<InvalidOperationException>(() => registry.Register(new StubProvider("test-protocol")));

            var provider = registry.Get("test-protocol");
            CollectionAssert.IsNotEmpty(provider.Validate(new OtherSettings()));
            Assert.Throws<ArgumentException>(() => provider.CreateClient(new InduLinkDeviceConfig
            {
                Name = "device",
                Protocol = "test-protocol",
                Runtime = new InduLinkDeviceRuntimeOptions(),
                Settings = new OtherSettings(),
            }, NullInduLinkLogger.Instance));
        }

        [TestCaseSource(nameof(ValidSettings))]
        public void Configuration_RoundTripsStronglyTypedSettings(string protocol, IProtocolSettings settings)
        {
            var sdk = InduLinkSdk.CreateDefault();
            var config = new InduLinkSdkConfig
            {
                Devices = new List<InduLinkDeviceConfig>
                {
                    new InduLinkDeviceConfig
                    {
                        Name = "device-1",
                        Protocol = protocol,
                        DeviceId = "device-1",
                        PointsFile = "points/device-1.json",
                        Runtime = new InduLinkDeviceRuntimeOptions(),
                        Settings = settings,
                    },
                },
            };

            var parsed = sdk.ParseConfiguration(sdk.SerializeConfiguration(config));
            Assert.AreEqual(protocol, parsed.Devices[0].Protocol);
            Assert.AreEqual(settings.GetType(), parsed.Devices[0].Settings.GetType());
            Assert.AreEqual(sdk.Configuration.SerializeSettings(settings),
                sdk.Configuration.SerializeSettings(parsed.Devices[0].Settings));
        }

        [Test]
        public void Configuration_RejectsUnknownProtocolMissingSettingsAndInvalidSettings()
        {
            var sdk = InduLinkSdk.CreateDefault();
            const string runtime = "\"runtime\":{\"pollingIntervalMilliseconds\":1000,\"reconnectDelayMilliseconds\":3000,\"operationTimeoutMilliseconds\":5000}";
            Assert.Throws<KeyNotFoundException>(() => sdk.ParseConfiguration(
                "{\"devices\":[{\"name\":\"x\",\"protocol\":\"unknown\",\"pointsFile\":\"p.json\"," + runtime + ",\"settings\":{}}]}"));
            Assert.Throws<JsonSerializationException>(() => sdk.ParseConfiguration(
                "{\"devices\":[{\"name\":\"x\",\"protocol\":\"modbus-tcp\",\"pointsFile\":\"p.json\"," + runtime + "}]}"));

            var errors = sdk.Protocols.Get("modbus-tcp").Validate(new ModbusTcpSettings()).ToArray();
            Assert.That(errors, Has.Some.Contains("host"));
        }

        [TestCase(typeof(ModbusTcpClient))]
        [TestCase(typeof(SiemensS7Client))]
        [TestCase(typeof(MitsubishiMcClient))]
        [TestCase(typeof(AdsClient))]
        [TestCase(typeof(OpcUaClient))]
        [TestCase(typeof(MqttClient))]
        [TestCase(typeof(RedisClient))]
        public void ProtocolAssemblies_DoNotReferenceAggregateOrOtherProtocolAssemblies(Type clientType)
        {
            var ownAssembly = clientType.Assembly.GetName().Name;
            var references = clientType.Assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
            CollectionAssert.DoesNotContain(references, "InduLink");
            Assert.That(references.Where(name => name.StartsWith("InduLink.Protocols.", StringComparison.Ordinal))
                .Where(name => name != "InduLink.Protocols.Common" && name != ownAssembly), Is.Empty);
        }

        [Test]
        public void ThirdPartyDrivers_AreReferencedOnlyByTheirOwningProtocolAssembly()
        {
            var owners = new Dictionary<Type, string[]>
            {
                [typeof(ModbusTcpClient)] = new[] { "NModbus" },
                [typeof(SiemensS7Client)] = new[] { "S7.Net" },
                [typeof(OpcUaClient)] = new[] { "Opc.Ua.Client", "Opc.Ua.Core", "Opc.Ua.Types" },
                [typeof(MqttClient)] = new[] { "MQTTnet" },
                [typeof(RedisClient)] = new[] { "StackExchange.Redis" },
                [typeof(MitsubishiMcClient)] = new string[0],
                [typeof(AdsClient)] = new[] { "TwinCAT.Ads" },
            };
            var allDrivers = owners.Values.SelectMany(item => item).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var owner in owners)
            {
                var references = owner.Key.Assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
                CollectionAssert.IsSubsetOf(owner.Value, references);
                Assert.That(references.Intersect(allDrivers).Except(owner.Value), Is.Empty,
                    owner.Key.Assembly.GetName().Name + " 引用了其他协议的第三方驱动。");
            }
        }

        [Test]
        public void MySqlConnector_IsReferencedOnlyByTheMySqlStorageProvider()
        {
            var providerReferences = typeof(MySqlInduLinkDataStore).Assembly
                .GetReferencedAssemblies().Select(item => item.Name).ToArray();

            CollectionAssert.Contains(providerReferences, "MySqlConnector");
            CollectionAssert.Contains(providerReferences, "InduLink.Storage");
            CollectionAssert.DoesNotContain(providerReferences, "InduLink");

            var nonOwners = new[]
            {
                typeof(InduLinkSdk).Assembly,
                typeof(InduLinkConfiguredClient).Assembly,
                typeof(SqlServerInduLinkDataStore).Assembly,
                typeof(ModbusTcpClient).Assembly,
                typeof(SiemensS7Client).Assembly,
                typeof(MitsubishiMcClient).Assembly,
                typeof(AdsClient).Assembly,
                typeof(OpcUaClient).Assembly,
                typeof(MqttClient).Assembly,
                typeof(RedisClient).Assembly,
            }.Distinct();
            foreach (var assembly in nonOwners)
            {
                CollectionAssert.DoesNotContain(
                    assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray(),
                    "MySqlConnector",
                    assembly.GetName().Name + " 不应直接引用 MySQL 驱动。");
            }
        }

        [Test]
        public void Redis_RemainsIndependentFromRelationalHistoryStorage()
        {
            var references = typeof(RedisClient).Assembly
                .GetReferencedAssemblies().Select(item => item.Name).ToArray();

            CollectionAssert.DoesNotContain(references, "InduLink.Storage");
            CollectionAssert.DoesNotContain(references, "InduLink.Storage.MySql");
            Assert.IsFalse(typeof(IInduLinkHistoryStore).IsAssignableFrom(typeof(RedisClient)));
        }

        private static IEnumerable<TestCaseData> ValidSettings()
        {
            yield return new TestCaseData("modbus-tcp", new ModbusTcpSettings { Host = "127.0.0.1" });
            yield return new TestCaseData("modbus-rtu", new ModbusRtuSettings { PortName = "COM1" });
            yield return new TestCaseData("siemens-s7", new SiemensS7Settings { Host = "127.0.0.1" });
            yield return new TestCaseData("mitsubishi-mc", new MitsubishiMcSettings { Host = "127.0.0.1" });
            yield return new TestCaseData("ads", new AdsSettings { AmsNetId = "127.0.0.1.1.1" });
            yield return new TestCaseData("opc-ua", new OpcUaSettings { EndpointUrl = "opc.tcp://127.0.0.1:4840" });
            yield return new TestCaseData("mqtt", new MqttSettings { Host = "127.0.0.1" });
            yield return new TestCaseData("redis", new RedisSettings { Host = "127.0.0.1" });
        }

        private sealed class StubSettings : IProtocolSettings { }
        private sealed class OtherSettings : IProtocolSettings { }

        private sealed class StubProvider : InduLinkProtocolProvider<StubSettings>
        {
            private readonly string _protocol;
            public StubProvider(string protocol) { _protocol = protocol; }
            public override string Protocol { get { return _protocol; } }
            protected override IReadOnlyList<string> Validate(StubSettings settings) { return new string[0]; }
            protected override IInduLinkClient CreateClient(InduLinkDeviceConfig device, StubSettings settings, IInduLinkLogger logger)
            {
                throw new NotSupportedException();
            }
        }
    }
}
