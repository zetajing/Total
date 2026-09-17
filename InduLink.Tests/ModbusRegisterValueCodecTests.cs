using InduLink.Abstractions;
using InduLink.Protocols.Common;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class ModbusRegisterValueCodecTests
    {
        [Test]
        public void BoolRegisterRoundTripTreatsAnyNonZeroByteAsTrue()
        {
            var write = new WriteRequest("device", "D0", DataType.Bool, true);
            CollectionAssert.AreEqual(new ushort[] { 0x0100 }, RegisterValueCodec.EncodeRegisters(write));

            var read = new ReadRequest("device", "D0", DataType.Bool);
            Assert.IsTrue((bool)RegisterValueCodec.ToDataValue(read, new ushort[] { 0x0100 }).Value);
        }
    }
}
