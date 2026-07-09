using System;
using System.IO;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class IndustrialConfigurationTests
    {
        [Test]
        public void Validate_Should_Pass_For_Deployable_Device_And_PointTable()
        {
            var directory = CreateTempDirectory();
            try
            {
                var pointsDirectory = Path.Combine(directory, "points");
                Directory.CreateDirectory(pointsDirectory);
                File.WriteAllText(Path.Combine(pointsDirectory, "plc1.json"), @"{ ""tags"": [{ ""name"": ""Speed"", ""address"": ""HR0"", ""type"": ""Int16"" }] }");

                var config = IndustrialSdkConfig.FromJson(@"
{
  ""devices"": [
    {
      ""name"": ""plc1"",
      ""protocol"": ""modbus-tcp"",
      ""host"": ""127.0.0.1"",
      ""deviceProfile"": ""generic"",
      ""pointsFile"": ""points/plc1.json""
    }
  ]
}");

                var result = config.Validate(directory);

                Assert.That(result.IsValid, Is.True);
                Assert.That(result.Errors, Is.Empty);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Validate_Should_Report_Duplicate_Names_And_Missing_PointFiles()
        {
            var directory = CreateTempDirectory();
            try
            {
                var config = IndustrialSdkConfig.FromJson(@"
{
  ""devices"": [
    { ""name"": ""plc1"", ""protocol"": ""modbus-tcp"", ""host"": ""127.0.0.1"", ""pointsFile"": ""points/missing.json"", ""pollingIntervalMilliseconds"": 0 },
    { ""name"": ""plc1"", ""protocol"": ""modbus-tcp"", ""host"": ""127.0.0.1"", ""pointsFile"": ""points/missing2.json"" }
  ]
}");

                var result = config.Validate(directory);

                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Errors, Has.Some.Contains("重复"));
                Assert.That(result.Errors, Has.Some.Contains("点位文件不存在"));
                Assert.That(result.Errors, Has.Some.Contains("pollingIntervalMilliseconds"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "IndustrialCommSdkTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
