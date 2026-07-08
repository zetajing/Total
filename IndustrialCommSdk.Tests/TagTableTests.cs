using IndustrialCommSdk.Abstractions;
using NUnit.Framework;

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
    }
}
