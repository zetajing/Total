using System;
using System.Collections.Generic;
using System.Linq;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Protocols.OpcUa;
using IndustrialCommSdk.Protocols.Redis;
using IndustrialCommSdk.Protocols.S7;
using Newtonsoft.Json;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class ModularArchitectureTests
    {
        private static readonly string[] CanonicalProtocols =
        {
            "mitsubishi-mc", "modbus-rtu", "modbus-tcp", "mqtt", "opc-ua", "redis", "siemens-s7",
        };

        [Test]
        public void DefaultSdk_RegistersExactlyTheCanonicalProtocols()
        {
            var sdk = IndustrialSdk.CreateDefault();
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

        [Test]
        public void Registry_RejectsDuplicateProtocolAndWrongSettingsType()
        {
            var registry = new IndustrialProtocolRegistry();
            registry.Register(new StubProvider("test-protocol"));
            Assert.Throws<InvalidOperationException>(() => registry.Register(new StubProvider("test-protocol")));

            var provider = registry.Get("test-protocol");
            CollectionAssert.IsNotEmpty(provider.Validate(new OtherSettings()));
            Assert.Throws<ArgumentException>(() => provider.CreateClient(new IndustrialDeviceConfig
            {
                Name = "device",
                Protocol = "test-protocol",
                Runtime = new IndustrialDeviceRuntimeOptions(),
                Settings = new OtherSettings(),
            }, NullIndustrialLogger.Instance));
        }

        [TestCaseSource(nameof(ValidSettings))]
        public void Configuration_RoundTripsStronglyTypedSettings(string protocol, IProtocolSettings settings)
        {
            var sdk = IndustrialSdk.CreateDefault();
            var config = new IndustrialSdkConfig
            {
                Devices = new List<IndustrialDeviceConfig>
                {
                    new IndustrialDeviceConfig
                    {
                        Name = "device-1",
                        Protocol = protocol,
                        DeviceId = "device-1",
                        PointsFile = "points/device-1.json",
                        Runtime = new IndustrialDeviceRuntimeOptions(),
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
            var sdk = IndustrialSdk.CreateDefault();
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
        [TestCase(typeof(OpcUaClient))]
        [TestCase(typeof(MqttClient))]
        [TestCase(typeof(RedisClient))]
        public void ProtocolAssemblies_DoNotReferenceAggregateOrOtherProtocolAssemblies(Type clientType)
        {
            var ownAssembly = clientType.Assembly.GetName().Name;
            var references = clientType.Assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
            CollectionAssert.DoesNotContain(references, "IndustrialCommSdk");
            Assert.That(references.Where(name => name.StartsWith("IndustrialCommSdk.Protocols.", StringComparison.Ordinal))
                .Where(name => name != "IndustrialCommSdk.Protocols.Common" && name != ownAssembly), Is.Empty);
        }

        [Test]
        public void ThirdPartyDrivers_AreReferencedOnlyByTheirOwningProtocolAssembly()
        {
            var owners = new Dictionary<Type, string[]>
            {
                [typeof(ModbusTcpClient)] = new[] { "NModbus4" },
                [typeof(SiemensS7Client)] = new[] { "S7.Net" },
                [typeof(OpcUaClient)] = new[] { "Opc.Ua.Client", "Opc.Ua.Core", "Opc.Ua.Types" },
                [typeof(MqttClient)] = new[] { "MQTTnet" },
                [typeof(RedisClient)] = new[] { "StackExchange.Redis" },
                [typeof(MitsubishiMcClient)] = new string[0],
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

        private static IEnumerable<TestCaseData> ValidSettings()
        {
            yield return new TestCaseData("modbus-tcp", new ModbusTcpSettings { Host = "127.0.0.1" });
            yield return new TestCaseData("modbus-rtu", new ModbusRtuSettings { PortName = "COM1" });
            yield return new TestCaseData("siemens-s7", new SiemensS7Settings { Host = "127.0.0.1" });
            yield return new TestCaseData("mitsubishi-mc", new MitsubishiMcSettings { Host = "127.0.0.1" });
            yield return new TestCaseData("opc-ua", new OpcUaSettings { EndpointUrl = "opc.tcp://127.0.0.1:4840" });
            yield return new TestCaseData("mqtt", new MqttSettings { Host = "127.0.0.1" });
            yield return new TestCaseData("redis", new RedisSettings { Host = "127.0.0.1" });
        }

        private sealed class StubSettings : IProtocolSettings { }
        private sealed class OtherSettings : IProtocolSettings { }

        private sealed class StubProvider : IndustrialProtocolProvider<StubSettings>
        {
            private readonly string _protocol;
            public StubProvider(string protocol) { _protocol = protocol; }
            public override string Protocol { get { return _protocol; } }
            protected override IReadOnlyList<string> Validate(StubSettings settings) { return new string[0]; }
            protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, StubSettings settings, IIndustrialLogger logger)
            {
                throw new NotSupportedException();
            }
        }
    }
}
