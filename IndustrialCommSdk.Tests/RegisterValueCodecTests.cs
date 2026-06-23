using System.Text;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Protocols.Common;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 <see cref="RegisterValueCodec" /> 寄存器编解码工具的单元测试。
    ///     验证字符串类型数据在读写操作中的寄存器长度计算、字节编码（含填充与超长拒绝）、
    ///     以及从寄存器解码时能否正确处理尾部零填充等场景。
    /// </summary>
    [TestFixture]
    public class RegisterValueCodecTests
    {
        /// <summary>
        ///     验证 <c>GetRequiredRegisterLength</c> 方法能为字符串 "ABC"（3 字节）
        ///     正确计算出所需寄存器数量（每个寄存器 2 字节，3 字节需要 2 个寄存器）。
        /// </summary>
        [Test]
        public void GetRequiredRegisterLength_Should_Calculate_String_Register_Count()
        {
            var length = RegisterValueCodec.GetRequiredRegisterLength(DataType.String, "ABC");

            Assert.That(length, Is.EqualTo(2));
        }

        /// <summary>
        ///     验证 <c>EncodeBytes</c> 方法在写入字符串时能将数据填充到寄存器长度的整数倍。
        ///     字符串 "ABC" 在 2 个寄存器（共 4 字节）中应被填充一个零字节，结果为 [65, 66, 67, 0]。
        /// </summary>
        [Test]
        public void EncodeBytes_Should_Pad_String_To_Register_Length()
        {
            var request = new WriteRequest("modbus-1", "D100", DataType.String, "ABC", 2);

            var bytes = RegisterValueCodec.EncodeBytes(request);

            Assert.That(bytes, Is.EqualTo(new byte[] { 65, 66, 67, 0 }));
        }

        /// <summary>
        ///     验证 <c>EncodeBytes</c> 方法在字符串长度超出寄存器容量时抛出 <see cref="IndustrialDataConversionException" />。
        ///     字符串 "ABCDE"（5 字节）无法放入 2 个寄存器（4 字节）中。
        /// </summary>
        [Test]
        public void EncodeBytes_Should_Reject_String_That_Exceeds_Register_Length()
        {
            var request = new WriteRequest("modbus-1", "D100", DataType.String, "ABCDE", 2);

            Assert.Throws<IndustrialDataConversionException>(() => RegisterValueCodec.EncodeBytes(request));
        }

        /// <summary>
        ///     验证 <c>ToDataValue</c> 方法能从包含尾部零填充的寄存器数据中正确还原原始字符串。
        ///     寄存器内容为 "ABC\0"，解码后应得到 "ABC"（尾部零被去除）。
        /// </summary>
        [Test]
        public void ToDataValue_Should_Decode_Padded_String()
        {
            var request = new ReadRequest("modbus-1", "D100", DataType.String, 2);
            var registers = RegisterValueCodec.GetRegistersFromBytes(Encoding.ASCII.GetBytes("ABC\0"));

            var value = RegisterValueCodec.ToDataValue(request, registers);

            Assert.That(value.Value, Is.EqualTo("ABC"));
        }
    }
}
