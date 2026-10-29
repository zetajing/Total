using System;
using System.Runtime.InteropServices;
using System.Text;
using InduLink.Exceptions;
using InduLink.Protocols.Ads;
using NUnit.Framework;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class AdsArrayTypeTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-3)]
        public void ArrayLengthIsElementCountRegardlessOfLowerBound(int lowerBound)
        {
            var array = new ArrayType(new PrimitiveType("DINT", typeof(int)), lowerBound, 5);
            Assert.That(AdsArrayTypeValidator.GetLength("MAIN.Targets", array, typeof(int)), Is.EqualTo(5));
        }

        [Test]
        public void WrongCountExplainsSymbolDeclarationAndActualCount()
        {
            var array = new ArrayType(new PrimitiveType("DINT", typeof(int)), 1, 100);
            var error = Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Targets", array, typeof(int), 80));
            Assert.That(error.Message, Does.Contain("MAIN.Targets").And.Contain("ARRAY[1..100] OF DINT")
                .And.Contain("需要 100").And.Contain("实际传入 80"));
        }

        [TestCase("DINT", typeof(int), typeof(float))]
        [TestCase("REAL", typeof(float), typeof(int))]
        [TestCase("BOOL", typeof(bool), typeof(byte))]
        [TestCase("DINT", typeof(int), typeof(short))]
        [TestCase("ULINT", typeof(ulong), typeof(TimeSpan))]
        public void ElementTypesAreCheckedByMeaningNotOnlyByteSize(string plcName, Type plcType, Type actualType)
        {
            var array = new ArrayType(new PrimitiveType(plcName, plcType), 1, 3);
            var error = Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Values", array, actualType, 3));
            Assert.That(error.Message, Does.Contain("MAIN.Values").And.Contain(plcName).And.Contain(actualType.Name));
        }

        [Test]
        public void BoolArrayUsesItsOfficialManagedType()
        {
            var array = new ArrayType(new PrimitiveType("BOOL", typeof(bool)), 0, 3);
            // BOOL 按官方映射检查，不使用 Marshal.SizeOf<bool>() 的默认四字节大小。
            Assert.That(AdsArrayTypeValidator.GetLength("MAIN.Flags", array, typeof(bool), 3), Is.EqualTo(3));
        }

        [Test]
        public void AliasArrayAndAliasElementsResolveToTheirBaseTypes()
        {
            var element = new AliasType("Position", new PrimitiveType("DINT", typeof(int)));
            var array = new ArrayType(element, 1, 3);
            var alias = new AliasType("Positions", array);
            Assert.That(AdsArrayTypeValidator.GetLength("MAIN.Positions", alias, typeof(int), 3), Is.EqualTo(3));
        }

        [Test]
        public void PlcAndClrEnumsUseTheirUnderlyingType()
        {
            var plcEnum = new EnumType<int>("MotorState", new PrimitiveType("DINT", typeof(int)), new EnumValueCollection<int>());
            var array = new ArrayType(plcEnum, 0, 2);
            Assert.That(AdsArrayTypeValidator.GetLength("MAIN.States", array, typeof(MotorState), 2), Is.EqualTo(2));
            Assert.Throws<InduLinkDataConversionException>(() => AdsArrayTypeValidator.GetLength("MAIN.States", array, typeof(short), 2));
        }

        [Test]
        public void ScalarAndMissingSymbolsCannotBeTreatedAsArrays()
        {
            Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Value", new PrimitiveType("DINT", typeof(int)), typeof(int), 1));
            Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Missing", null, typeof(int), 1));
        }

        [Test]
        public void MultidimensionalAndJaggedArraysRequireTheExplicitAnyApi()
        {
            var element = new PrimitiveType("DINT", typeof(int));
            var dimensions = new DimensionCollection(new[] { new Dimension(1, 2), new Dimension(1, 3) });
            Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Matrix", new ArrayType(element, dimensions), typeof(int), 6));
            Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Jagged", new ArrayType(new ArrayType(element, 3), 2), typeof(int), 6));
        }

        [Test]
        public void StructArrayAcceptsExplicitLayoutAndRejectsWrongSize()
        {
            var array = new ArrayType(new SizedStructType(5), 0, 2);
            Assert.That(AdsArrayTypeValidator.GetLength("MAIN.Motors", array, typeof(PackedMotor), 2), Is.EqualTo(2));
            var error = Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Motors", array, typeof(UnpackedMotor), 2));
            Assert.That(error.Message, Does.Contain("PLC 元素占 5 字节").And.Contain("C# 元素占"));
            Assert.Throws<InduLinkDataConversionException>(() => AdsArrayTypeValidator.GetLength("MAIN.Motors", array, typeof(int), 2));
        }

        [Test]
        public void StringArrayRequiresExplicitStringMarshalling()
        {
            var array = new ArrayType(new StringType(20, Encoding.ASCII), 0, 2);
            var error = Assert.Throws<InduLinkDataConversionException>(() =>
                AdsArrayTypeValidator.GetLength("MAIN.Labels", array, typeof(string), 2));
            Assert.That(error.Message, Does.Contain("ReadAnyAsync/WriteAnyAsync"));
        }

        private enum MotorState { Stopped, Running }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PackedMotor
        {
            [MarshalAs(UnmanagedType.I1)] public bool Running;
            public int Count;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnpackedMotor
        {
            public bool Running;
            public int Count;
        }

        private sealed class SizedStructType : StructType
        {
            // 无需 PLC，用真实官方类型构造可控大小的结构体元数据。
            public SizedStructType(int byteSize) : base("ST_Motor") { Size = byteSize; }
        }
    }
}
