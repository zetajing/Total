using System;
using System.IO;
using System.Linq;
using IndustrialCommDemo.Services;
using IndustrialCommSdk;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class JsonConfigurationValidationTests
    {
        private string _configDirectory;
        private IndustrialSdk _sdk;
        private JsonConfigurationValidationService _service;

        [SetUp]
        public void SetUp()
        {
            _configDirectory = FindConfigDirectory();
            _sdk = IndustrialSdk.CreateDefault();
            _service = new JsonConfigurationValidationService(_sdk, _configDirectory);
        }

        [Test]
        public void CurrentConfigurationFiles_Validate()
        {
            Assert.IsTrue(_service.ValidateFile(
                JsonConfigurationDocument.Devices,
                Path.Combine(_configDirectory, "devices.json")).IsValid);
            Assert.IsTrue(_service.ValidateFile(
                JsonConfigurationDocument.Points,
                Path.Combine(_configDirectory, "points", "plc1.json")).IsValid);
            Assert.IsTrue(_service.ValidateFile(
                JsonConfigurationDocument.Points,
                Path.Combine(_configDirectory, "points", "s7plc.json")).IsValid);
            Assert.IsTrue(_service.ValidateFile(
                JsonConfigurationDocument.ModbusProfiles,
                Path.Combine(_configDirectory, "modbus-profiles.json")).IsValid);
            Assert.IsTrue(_service.ValidateFile(
                JsonConfigurationDocument.NetworkServices,
                Path.Combine(_configDirectory, "network-services.json")).IsValid);
        }

        [Test]
        public void Templates_ValidateWithoutWritingFiles()
        {
            foreach (var document in Enum.GetValues(typeof(JsonConfigurationDocument)).Cast<JsonConfigurationDocument>())
            {
                var json = _service.LoadTemplate(document);
                var result = _service.Validate(
                    document,
                    json,
                    _configDirectory,
                    document != JsonConfigurationDocument.Devices);
                Assert.IsTrue(result.IsValid, document + ": " + result.ToDisplayText());
            }
        }

        [Test]
        public void UnknownPointProperty_IsRejectedBySchema()
        {
            var result = _service.Validate(
                JsonConfigurationDocument.Points,
                "{\"tags\":[],\"unexpected\":true}",
                _configDirectory,
                false);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(error => error.IsSchemaError));
        }

        [Test]
        public void NetworkConfiguration_SemanticErrorIsReported()
        {
            var json = File.ReadAllText(Path.Combine(_configDirectory, "network-services.json"));
            json = json.Replace("\"useTls\": true", "\"useTls\": false");

            var result = _service.Validate(
                JsonConfigurationDocument.NetworkServices,
                json,
                _configDirectory,
                false);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("allowInsecureFtp", result.ToDisplayText());
        }

        [Test]
        public void ValidateAll_UsesCurrentPointTextWithoutWriting()
        {
            var devicePath = Path.Combine(_configDirectory, "devices.json");
            var pointPath = Path.Combine(_configDirectory, "points", "plc1.json");
            var deviceJson = File.ReadAllText(devicePath);
            var pointJson = File.ReadAllText(pointPath);
            var result = _service.ValidateAll(deviceJson, pointPath, pointJson);

            Assert.IsTrue(result.IsValid, result.ToDisplayText());
            StringAssert.Contains("\"tags\"", File.ReadAllText(pointPath));
        }

        [Test]
        public void PointConfigStore_InvalidDraftDoesNotOverwriteExistingFile()
        {
            var pointPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "invalid-point-draft-" + Guid.NewGuid().ToString("N") + ".json");
            const string original = "{\"tags\":[]}";
            File.WriteAllText(pointPath, original);
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    JsonPointConfigStore.Save(_service, pointPath, "{\"tags\":["));
                Assert.AreEqual(original, File.ReadAllText(pointPath));
            }
            finally
            {
                if (File.Exists(pointPath)) File.Delete(pointPath);
            }
        }

        private static string FindConfigDirectory()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "IndustrialCommDemo", "Config");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("IndustrialCommDemo Config directory was not found.");
        }
    }
}
