using System;
using System.IO;
using System.Linq;
using InduLink;
using InduLink.Runtime.Configuration;
using InduLink.Protocols.Modbus;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class ConfigurationTests
    {
        [Test]
        public void OperationTimeout_RoundTripsThroughJson()
        {
            var sdk = IndustrialSdk.CreateDefault();
            var config = sdk.ParseConfiguration("{\"devices\":[{\"name\":\"plc\",\"protocol\":\"modbus-tcp\",\"pointsFile\":\"points.json\",\"enabled\":true,\"runtime\":{\"pollingIntervalMilliseconds\":1000,\"reconnectDelayMilliseconds\":3000,\"operationTimeoutMilliseconds\":1234},\"settings\":{\"host\":\"127.0.0.1\"}}]}");
            Assert.AreEqual(1234, config.Devices[0].Runtime.OperationTimeoutMilliseconds);
            StringAssert.Contains("operationTimeoutMilliseconds", sdk.SerializeConfiguration(config));
        }

        [Test]
        public void ClientOptions_DefaultToFiveSecondsAndRejectInvalidValue()
        {
            Assert.AreEqual(5000, new ModbusTcpClientOptions().OperationTimeoutMilliseconds);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = "plc",
                Host = "127.0.0.1",
                OperationTimeoutMilliseconds = 0,
                DeviceProfile = ModbusDeviceProfiles.Generic,
            }));
        }

        [Test]
        public void DirectClient_UsesSelectedModbusProfile()
        {
            using (var client = new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = "plc",
                Host = "127.0.0.1",
                DeviceProfile = ModbusDeviceProfiles.MitsubishiModbusTcp,
            }))
            {
                Assert.AreEqual("mitsubishi-modbus-tcp", client.Profile.Key);
                Assert.AreEqual(ModbusArea.HoldingRegister, client.Profile.ParseAddress("D100").Area);
            }
        }

        [Test]
        public void GenericProfile_ParsesStandardModbusAreas()
        {
            var holding = ModbusDeviceProfiles.Generic.ParseAddress("40001");
            var input = ModbusDeviceProfiles.Generic.ParseAddress("IR3");
            var coil = ModbusDeviceProfiles.Generic.ParseAddress("00005");
            var discreteInput = ModbusDeviceProfiles.Generic.ParseAddress("DI7");

            Assert.AreEqual(ModbusArea.HoldingRegister, holding.Area);
            Assert.AreEqual((ushort)0, holding.ZeroBasedAddress);
            Assert.AreEqual(ModbusArea.InputRegister, input.Area);
            Assert.AreEqual((ushort)3, input.ZeroBasedAddress);
            Assert.AreEqual(ModbusArea.Coil, coil.Area);
            Assert.AreEqual((ushort)4, coil.ZeroBasedAddress);
            Assert.AreEqual(ModbusArea.DiscreteInput, discreteInput.Area);
            Assert.AreEqual((ushort)7, discreteInput.ZeroBasedAddress);
        }

        [Test]
        public void JsonProfile_CanBeLoadedAndSelected()
        {
            var key = "test-custom-profile-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, key + ".json");
            File.WriteAllText(path, "{\"profiles\":[{\"key\":\"" + key + "\",\"displayName\":\"测试自定义 PLC\",\"defaultAddress\":\"D0\",\"exampleAddresses\":\"D0, M0\",\"addressPattern\":\"^([A-Z]+)(\\\\d+)$\",\"lowWordFirst\":false,\"mappings\":[{\"prefix\":\"D\",\"area\":\"HoldingRegister\",\"base\":100,\"max\":10,\"radix\":\"decimal\"},{\"prefix\":\"M\",\"area\":\"Coil\",\"base\":20,\"max\":10,\"radix\":\"decimal\"}]}]}");
            try
            {
                ModbusDeviceProfiles.LoadJsonProfiles(path);
                var profile = ModbusDeviceProfiles.GetRequired(key);

                Assert.AreEqual("测试自定义 PLC", profile.DisplayName);
                Assert.AreEqual(ModbusArea.HoldingRegister, profile.ParseAddress("D3").Area);
                Assert.AreEqual((ushort)103, profile.ParseAddress("D3").ZeroBasedAddress);
                Assert.AreEqual(ModbusArea.Coil, profile.ParseAddress("M2").Area);
                Assert.AreEqual((ushort)22, profile.ParseAddress("M2").ZeroBasedAddress);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void JsonProfile_RejectsInvalidCoreDefinitions()
        {
            var missingKey = CreateProfileDefinition("valid-key");
            missingKey.Key = " ";
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(missingKey));

            var missingPattern = CreateProfileDefinition("missing-pattern");
            missingPattern.AddressPattern = null;
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(missingPattern));

            var insufficientGroups = CreateProfileDefinition("insufficient-groups");
            insufficientGroups.AddressPattern = "^([A-Z]+)\\d+$";
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(insufficientGroups));

            var missingMappings = CreateProfileDefinition("missing-mappings");
            missingMappings.Mappings.Clear();
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(missingMappings));

            var duplicatePrefix = CreateProfileDefinition("duplicate-prefix");
            duplicatePrefix.Mappings.Add(new ModbusProfileMapping
            {
                Prefix = "d",
                Area = "Coil",
                Base = 20,
                Max = 10,
                Radix = "decimal",
            });
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(duplicatePrefix));
        }

        [Test]
        public void JsonProfile_RejectsInvalidAreaRadixAndAddressRange()
        {
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(
                CreateProfileDefinition("invalid-area", "UnknownArea", "decimal", 0, 10)));
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(
                CreateProfileDefinition("invalid-radix", "HoldingRegister", "binary", 0, 10)));
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(
                CreateProfileDefinition("invalid-max", "HoldingRegister", "decimal", 0, 0)));
            Assert.Catch<ArgumentException>(() => new JsonModbusProfile(
                CreateProfileDefinition("overflow", "HoldingRegister", "decimal", 65530, 7)));

            var boundary = new JsonModbusProfile(
                CreateProfileDefinition("boundary", "HoldingRegister", "decimal", 65535, 1));
            Assert.AreEqual((ushort)65535, boundary.ParseAddress("D0").ZeroBasedAddress);
        }

        [Test]
        public void LoadJsonProfiles_InvalidEntryDoesNotPartiallyRegisterFile()
        {
            var validKey = "atomic-valid-" + Guid.NewGuid().ToString("N");
            var invalidKey = "atomic-invalid-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, validKey + ".json");
            File.WriteAllText(path,
                "{\"profiles\":[" +
                CreateProfileJson(validKey, "Atomic Valid") + "," +
                CreateProfileJson(invalidKey, "Atomic Invalid", "UnknownArea") +
                "]}");

            try
            {
                Assert.Catch<ArgumentException>(() => ModbusDeviceProfiles.LoadJsonProfiles(path));
                Assert.IsNull(ModbusDeviceProfiles.Find(validKey));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void LoadJsonProfiles_SameKeyOverridesLookupButNotStaticProfile()
        {
            // 先完成可选默认文件加载，避免它在本测试的显式覆盖之后再次写入注册表。
            var ignored = ModbusDeviceProfiles.All;
            var builtIn = ModbusDeviceProfiles.MitsubishiModbusTcp;
            var displayName = "JSON Mitsubishi Override " + Guid.NewGuid().ToString("N");
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, displayName + ".json");
            File.WriteAllText(path,
                "{\"profiles\":[" +
                CreateProfileJson(builtIn.Key, displayName, "HoldingRegister", "decimal", 321, 10) +
                "]}");

            try
            {
                ModbusDeviceProfiles.LoadJsonProfiles(path);
                var selected = ModbusDeviceProfiles.GetRequired(builtIn.Key);
                var allMatches = ModbusDeviceProfiles.All
                    .Where(profile => string.Equals(profile.Key, builtIn.Key, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                Assert.IsInstanceOf<JsonModbusProfile>(selected);
                Assert.AreEqual(displayName, selected.DisplayName);
                Assert.AreEqual((ushort)321, selected.ParseAddress("D0").ZeroBasedAddress);
                Assert.AreSame(builtIn, ModbusDeviceProfiles.MitsubishiModbusTcp);
                Assert.AreEqual((ushort)0, builtIn.ParseAddress("D0").ZeroBasedAddress);
                Assert.AreEqual(1, allMatches.Length);
                Assert.AreSame(selected, allMatches[0]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void All_ReturnsUniqueStableSnapshots()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var firstKey = "aaa-snapshot-" + suffix;
            var lastKey = "zzz-snapshot-" + suffix;
            var laterKey = "later-snapshot-" + suffix;
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, suffix + ".json");

            try
            {
                File.WriteAllText(path,
                    "{\"profiles\":[" +
                    CreateProfileJson(lastKey, "Last Snapshot") + "," +
                    CreateProfileJson(firstKey, "First Snapshot") +
                    "]}");
                ModbusDeviceProfiles.LoadJsonProfiles(path);
                var snapshot = ModbusDeviceProfiles.All;

                File.WriteAllText(path,
                    "{\"profiles\":[" + CreateProfileJson(laterKey, "Later Snapshot") + "]}");
                ModbusDeviceProfiles.LoadJsonProfiles(path);
                var updated = ModbusDeviceProfiles.All;
                var repeated = ModbusDeviceProfiles.All;
                var updatedKeys = updated.Select(profile => profile.Key).ToArray();

                Assert.IsFalse(snapshot.Any(profile => profile.Key == laterKey));
                Assert.IsTrue(updated.Any(profile => profile.Key == laterKey));
                Assert.Less(
                    Array.FindIndex(updatedKeys, key => key == firstKey),
                    Array.FindIndex(updatedKeys, key => key == lastKey));
                Assert.AreEqual(
                    updatedKeys.Length,
                    updatedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                CollectionAssert.AreEqual(
                    updatedKeys,
                    repeated.Select(profile => profile.Key).ToArray());
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void GetRequired_RejectsUnknownProfile()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => ModbusDeviceProfiles.GetRequired("unknown-device"));
        }

        private static ModbusProfileDefinition CreateProfileDefinition(
            string key,
            string area = "HoldingRegister",
            string radix = "decimal",
            ushort baseAddress = 0,
            int max = 10)
        {
            var definition = new ModbusProfileDefinition
            {
                Key = key,
                DisplayName = key,
                DefaultAddress = "D0",
                ExampleAddresses = "D0",
                AddressPattern = "^([A-Z]+)(\\d+)$",
                LowWordFirst = false,
            };
            definition.Mappings.Add(new ModbusProfileMapping
            {
                Prefix = "D",
                Area = area,
                Base = baseAddress,
                Max = max,
                Radix = radix,
            });
            return definition;
        }

        private static string CreateProfileJson(
            string key,
            string displayName,
            string area = "HoldingRegister",
            string radix = "decimal",
            int baseAddress = 0,
            int max = 10)
        {
            return "{\"key\":\"" + key +
                   "\",\"displayName\":\"" + displayName +
                   "\",\"defaultAddress\":\"D0\",\"exampleAddresses\":\"D0\"," +
                   "\"addressPattern\":\"^([A-Z]+)(\\\\d+)$\",\"lowWordFirst\":false," +
                   "\"mappings\":[{\"prefix\":\"D\",\"area\":\"" + area +
                   "\",\"base\":" + baseAddress +
                   ",\"max\":" + max +
                   ",\"radix\":\"" + radix + "\"}]}";
        }
    }
}
