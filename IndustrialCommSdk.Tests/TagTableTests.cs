using IndustrialCommSdk.Abstractions;
using NUnit.Framework;
using System;
using System.IO;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class TagTableTests
    {
        [Test]
        public void ParseCsv_Should_Load_Tags()
        {
            var table = TagTable.ParseCsv(
                "Name,Address,Type,Length\r\n" +
                "Speed,D100,Int16,1\r\n" +
                "Title,D200,String,12\r\n");

            Assert.That(table.Tags.Count, Is.EqualTo(2));
            Assert.That(table.Get("Speed").Address, Is.EqualTo("D100"));
            Assert.That(table.Get("Speed").DataType, Is.EqualTo(DataType.Int16));
            Assert.That(table.Get("Title").Length, Is.EqualTo(12));
            Assert.That(table.GetByAddress("D200").Name, Is.EqualTo("Title"));
        }

        [Test]
        public void FromJson_Should_Load_Tags()
        {
            var table = TagTable.FromJson(@"
{
  ""tags"": [
    { ""name"": ""Speed"", ""address"": ""D100"", ""type"": ""Int16"" },
    { ""name"": ""Running"", ""address"": ""M10"", ""type"": ""Bool"", ""length"": 1 }
  ]
}");

            Assert.That(table.Tags.Count, Is.EqualTo(2));
            Assert.That(table.Get("Speed").DataType, Is.EqualTo(DataType.Int16));
            Assert.That(table.Get("Running").DataType, Is.EqualTo(DataType.Bool));
        }

        [Test]
        public void ParseCsv_Should_Handle_Quoted_Commas()
        {
            var table = TagTable.ParseCsv(
                "Name,Address,Type,Length\r\n" +
                "\"Speed, Main\",D100,Int16,1\r\n");

            Assert.That(table.Get("Speed, Main").Address, Is.EqualTo("D100"));
        }

        [Test]
        public void FromJson_Should_Show_Clear_Message_When_Comma_Is_Missing()
        {
            var ex = Assert.Throws<FormatException>(() => TagTable.FromJson(@"
{
  ""tags"": [
    { ""name"": ""Speed"", ""address"": ""D100"", ""type"": ""Int16"" }
    { ""name"": ""Running"", ""address"": ""M10"", ""type"": ""Bool"" }
  ]
}"));

            Assert.That(ex.Message, Does.Contain("每个点位对象之间是否有英文逗号"));
        }

        [Test]
        public void LoadForDevice_Should_Load_Linked_PointTable()
        {
            var directory = Path.Combine(Path.GetTempPath(), "IndustrialCommSdkTests", Guid.NewGuid().ToString("N"));
            var pointsDirectory = Path.Combine(directory, "points");
            Directory.CreateDirectory(pointsDirectory);

            try
            {
                var configPath = Path.Combine(directory, "devices.json");
                File.WriteAllText(configPath, @"{ ""devices"": [{ ""name"": ""plc1"", ""pointsFile"": ""points/plc1.json"" }] }");
                File.WriteAllText(Path.Combine(pointsDirectory, "plc1.json"), @"{ ""tags"": [{ ""name"": ""Speed"", ""address"": ""D100"", ""type"": ""Int16"" }] }");

                var table = TagTable.LoadForDevice(configPath, "plc1");

                Assert.That(table.Get("Speed").Address, Is.EqualTo("D100"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
