using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MitsubishiMcClientResponseTests
    {
        [Test]
        public async Task WriteAsync_ReceivesNoAdditionalBytesWhenResponseHasNoPayload()
        {
            var transport = new RecordingTransport(BuildResponsePrefix(2), new byte[0]);
            using (var client = new MitsubishiMcClient("mc-1", transport))
            {
                await client.WriteAsync(
                    new WriteRequest("mc-1", "D100", DataType.UInt16, (ushort)123),
                    CancellationToken.None);
            }

            Assert.That(transport.RequestedReceiveLengths, Is.EqualTo(new[] { 11, 0 }));
        }

        [Test]
        public async Task ReadAsync_ReceivesOnlyPayloadBytesAndParsesValue()
        {
            var payload = new byte[] { 0x34, 0x12, 0x78, 0x56 };
            var transport = new RecordingTransport(BuildResponsePrefix(6), payload);
            DataValue result;
            using (var client = new MitsubishiMcClient("mc-1", transport))
            {
                result = await client.ReadAsync(
                    new ReadRequest("mc-1", "D100", DataType.UInt32, 2),
                    CancellationToken.None);
            }

            Assert.That(transport.RequestedReceiveLengths, Is.EqualTo(new[] { 11, 4 }));
            Assert.That(result.Quality, Is.EqualTo(QualityStatus.Good));
            Assert.That(result.Value, Is.EqualTo(0x12345678u));
        }

        [Test]
        public void WriteAsync_RejectsDataLengthSmallerThanEndCodeWithoutSecondReceive()
        {
            var transport = new RecordingTransport(BuildResponsePrefix(1));
            using (var client = new MitsubishiMcClient("mc-1", transport))
            {
                Assert.ThrowsAsync<IndustrialProtocolException>(async () =>
                    await client.WriteAsync(
                        new WriteRequest("mc-1", "D100", DataType.UInt16, (ushort)123),
                        CancellationToken.None));
            }

            Assert.That(transport.RequestedReceiveLengths, Is.EqualTo(new[] { 11 }));
        }

        [Test]
        public void ParseResponse_RejectsTruncatedFrameWithProtocolException()
        {
            var truncated = BuildResponsePrefix(6);

            var exception = Assert.Throws<IndustrialProtocolException>(() => McFrame3E.ParseResponse(truncated));

            Assert.That(exception.Message, Does.Contain("Expected 15 bytes"));
        }

        private static byte[] BuildResponsePrefix(ushort dataLength)
        {
            return new[]
            {
                (byte)0xD0, (byte)0x00,
                (byte)0x00,
                (byte)0xFF,
                (byte)0xFF, (byte)0x03,
                (byte)0x00,
                (byte)dataLength, (byte)(dataLength >> 8),
                (byte)0x00, (byte)0x00,
            };
        }

        private sealed class RecordingTransport : ITransportClient
        {
            private readonly Queue<byte[]> _responses;

            public RecordingTransport(params byte[][] responses)
            {
                _responses = new Queue<byte[]>(responses);
            }

            public List<int> RequestedReceiveLengths { get; } = new List<int>();
            public bool IsConnected { get { return true; } }
            public EndPoint RemoteEndPoint { get { return null; } }

            public Task ConnectAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<byte[]> ReceiveExactAsync(int length, CancellationToken cancellationToken)
            {
                RequestedReceiveLengths.Add(length);
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException("No queued response is available.");
                }

                var response = _responses.Dequeue();
                if (response.Length != length)
                {
                    throw new InvalidOperationException(string.Format(
                        "Expected a receive request for {0} bytes, but received {1}.",
                        response.Length,
                        length));
                }

                return Task.FromResult(response);
            }

            public void Dispose()
            {
            }
        }
    }
}
