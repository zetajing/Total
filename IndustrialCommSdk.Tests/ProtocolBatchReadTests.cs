using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Transport;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class ProtocolBatchReadTests
    {
        [Test]
        public void SiemensS7PlanRead_MergesAdjacentDifferentDataTypes()
        {
            using (var client = new SiemensS7Client(new SiemensS7ClientOptions
            {
                DeviceId = "s7-test",
                Host = "127.0.0.1"
            }))
            {
                var requests = new[]
                {
                    new ReadRequest("s7-test", "DB1.DBD0", DataType.Float),
                    new ReadRequest("s7-test", "DB1.DBW4", DataType.Int16)
                };

                var plan = client.PlanRead(requests, new BatchReadOptions(maxItemsPerBatch: 8, maxAddressSpan: 16), client.Capabilities);

                Assert.AreEqual(1, plan.Groups.Count);
                Assert.IsNull(plan.Groups[0].DataType);
                Assert.AreEqual(0, plan.Groups[0].StartOffset);
                Assert.AreEqual(5, plan.Groups[0].EndOffset);
            }
        }

        [Test]
        public void SiemensS7BatchDecoder_DecodesBigEndianValuesFromSharedPayload()
        {
            var request = new ReadRequest("s7-test", "DB1.DBD0", DataType.Float);
            var address = new S7Address(S7Area.Db, 1, 0, -1, "DB1.DBD0");

            var value = S7BatchValueDecoder.Decode(
                request,
                address,
                new byte[] { 0x41, 0x20, 0x00, 0x00, 0x00, 0x2A },
                0);

            Assert.AreEqual(QualityStatus.Good, value.Quality);
            Assert.AreEqual(10f, (float)value.Value, 0.0001f);
        }

        [Test]
        public async Task MitsubishiMcReadMany_MergesDifferentWordTypesIntoOneFrame()
        {
            var response = BuildWordResponse(10, 20);
            var transport = new FakeTransport(response);
            using (var client = new MitsubishiMcClient("mc-test", transport))
            {
                await client.ConnectAsync(CancellationToken.None);
                var result = await client.ReadManyAsync(new[]
                {
                    new ReadRequest("mc-test", "D0", DataType.Int16),
                    new ReadRequest("mc-test", "D1", DataType.UInt16)
                }, CancellationToken.None);

                Assert.AreEqual(1, transport.SendCount);
                Assert.AreEqual(2, result.Values.Count);
                Assert.AreEqual((short)10, (short)result.Values[0].Value);
                Assert.AreEqual((ushort)20, (ushort)result.Values[1].Value);
                Assert.That(result.Values.All(value => value.Quality == QualityStatus.Good), Is.True);
            }
        }

        [Test]
        public async Task MitsubishiMcRead_DefaultInt32RequestsTwoWordsAndDecodesValue()
        {
            var transport = new FakeTransport(BuildWordResponse(0x1122, 0x3344));
            using (var client = new MitsubishiMcClient("mc-test", transport))
            {
                await client.ConnectAsync(CancellationToken.None);
                var request = new ReadRequest("mc-test", "D0", DataType.Int32);

                var value = await client.ReadAsync(request, CancellationToken.None);

                Assert.AreEqual(1, request.Length);
                Assert.AreEqual(1, transport.SendCount);
                Assert.IsNotNull(transport.LastSentPayload);
                Assert.AreEqual(2, transport.LastSentPayload[19] | (transport.LastSentPayload[20] << 8));
                Assert.AreEqual(QualityStatus.Good, value.Quality);
                Assert.AreEqual(0x11223344, (int)value.Value);
            }
        }

        [Test]
        public async Task MitsubishiMcRead_ShortInt32ResponseIsBadInsteadOfZeroPaddedGood()
        {
            var transport = new FakeTransport(BuildWordResponse(0x1234));
            using (var client = new MitsubishiMcClient("mc-test", transport))
            {
                await client.ConnectAsync(CancellationToken.None);

                var value = await client.ReadAsync(
                    new ReadRequest("mc-test", "D0", DataType.Int32),
                    CancellationToken.None);

                Assert.AreEqual(QualityStatus.Bad, value.Quality);
                Assert.That(value.ErrorMessage, Does.Contain("Expected 4 bytes"));
            }
        }

        [Test]
        public async Task MitsubishiMcReadMany_DefaultInt32PlanAndSliceUseTwoWords()
        {
            var transport = new FakeTransport(BuildWordResponse(0x1122, 0x3344));
            using (var client = new MitsubishiMcClient("mc-test", transport))
            {
                var request = new ReadRequest("mc-test", "D0", DataType.Int32);
                var plan = client.PlanRead(
                    new[] { request },
                    new BatchReadOptions(maxItemsPerBatch: 8, maxAddressSpan: 16),
                    client.Capabilities);

                Assert.AreEqual(1, plan.Groups.Count);
                Assert.AreEqual(0, plan.Groups[0].StartOffset);
                Assert.AreEqual(1, plan.Groups[0].EndOffset);

                await client.ConnectAsync(CancellationToken.None);
                var result = await client.ReadManyAsync(new[] { request }, CancellationToken.None);

                Assert.AreEqual(1, transport.SendCount);
                Assert.IsNotNull(transport.LastSentPayload);
                Assert.AreEqual(2, transport.LastSentPayload[19] | (transport.LastSentPayload[20] << 8));
                Assert.AreEqual(1, result.Values.Count);
                Assert.AreEqual(QualityStatus.Good, result.Values[0].Quality);
                Assert.AreEqual(0x11223344, (int)result.Values[0].Value);
            }
        }

        private static byte[] BuildWordResponse(params ushort[] values)
        {
            var payload = new byte[values.Length * 2];
            for (var i = 0; i < values.Length; i++)
            {
                payload[i * 2] = (byte)(values[i] & 0xFF);
                payload[i * 2 + 1] = (byte)(values[i] >> 8);
            }

            var dataLength = (ushort)(2 + payload.Length);
            var header = new byte[11]
            {
                0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
                (byte)(dataLength & 0xFF), (byte)(dataLength >> 8), 0x00, 0x00
            };
            return McFrame3E.Combine(header, payload);
        }

        private sealed class FakeTransport : ITransportClient
        {
            private readonly byte[] _response;
            private bool _connected;

            public FakeTransport(byte[] response)
            {
                _response = response;
            }

            public int SendCount { get; private set; }
            public byte[] LastSentPayload { get; private set; }
            public bool IsConnected { get { return _connected; } }
            public EndPoint RemoteEndPoint { get { return null; } }

            public Task ConnectAsync(CancellationToken cancellationToken)
            {
                _connected = true;
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                _connected = false;
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] payload, CancellationToken cancellationToken)
            {
                SendCount++;
                LastSentPayload = payload == null ? null : payload.ToArray();
                return Task.CompletedTask;
            }

            public Task<byte[]> ReceiveExactAsync(int length, CancellationToken cancellationToken)
            {
                if (length == 11)
                    return Task.FromResult(_response.Take(11).ToArray());
                return Task.FromResult(_response.Skip(11).Take(length).ToArray());
            }

            public void Dispose()
            {
                _connected = false;
            }
        }
    }
}
