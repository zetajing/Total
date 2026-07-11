using System;
using System.Collections.Generic;
using System.Text;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class TcpMessageFramingTests
    {
        [Test]
        public void FixedLength_HandlesPartialAndStickyFrames()
        {
            var framer = new FixedLengthMessageFramer(3);
            var buffer = new List<byte> { 1, 2 };
            byte[] frame;
            Assert.False(framer.TryExtractFrame(buffer, out frame));
            buffer.AddRange(new byte[] { 3, 4, 5, 6 });
            Assert.True(framer.TryExtractFrame(buffer, out frame));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, frame);
            Assert.True(framer.TryExtractFrame(buffer, out frame));
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, frame);
        }

        [Test]
        public void Delimiter_HandlesEmptyAndMultipleFrames()
        {
            var framer = new DelimiterMessageFramer(new byte[] { 13, 10 }, 8);
            var buffer = new List<byte>(new byte[] { 13, 10, 65, 13, 10 });
            byte[] frame;
            Assert.True(framer.TryExtractFrame(buffer, out frame));
            Assert.AreEqual(0, frame.Length);
            Assert.True(framer.TryExtractFrame(buffer, out frame));
            CollectionAssert.AreEqual(new byte[] { 65 }, frame);
        }

        [TestCase(2)]
        [TestCase(4)]
        public void LengthPrefix_RoundTripsAndPreservesNextFrame(int prefixLength)
        {
            var framer = new LengthPrefixMessageFramer(prefixLength, 100);
            var first = framer.Encode(Encoding.UTF8.GetBytes("one"));
            var second = framer.Encode(Encoding.UTF8.GetBytes("two"));
            var buffer = new List<byte>(); buffer.AddRange(first); buffer.AddRange(second);
            byte[] frame;
            Assert.True(framer.TryExtractFrame(buffer, out frame)); Assert.AreEqual("one", Encoding.UTF8.GetString(frame));
            Assert.True(framer.TryExtractFrame(buffer, out frame)); Assert.AreEqual("two", Encoding.UTF8.GetString(frame));
        }

        [Test]
        public void LengthPrefix_RejectsInvalidLengthImmediately()
        {
            var framer = new LengthPrefixMessageFramer(2, 10);
            var buffer = new List<byte> { 0, 11 };
            byte[] frame;
            Assert.Throws<IndustrialProtocolException>(() => framer.TryExtractFrame(buffer, out frame));
        }

        [Test]
        public void Delimiter_RejectsOversizedUnterminatedFrame()
        {
            var framer = new DelimiterMessageFramer(new byte[] { 10 }, 3);
            var buffer = new List<byte> { 1, 2, 3, 4 };
            byte[] frame;
            Assert.Throws<IndustrialProtocolException>(() => framer.TryExtractFrame(buffer, out frame));
        }
    }
}
