using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Transport
{
    /// <summary>定义 TCP 业务帧的编码和从累计接收缓冲区提取完整帧的规则。</summary>
    public interface ITcpMessageFramer
    {
        int MaximumFrameLength { get; }
        byte[] Encode(byte[] payload);
        bool TryExtractFrame(IList<byte> buffer, out byte[] payload);
    }

    /// <summary>每帧固定为指定字节数，不添加额外帧头。</summary>
    public sealed class FixedLengthMessageFramer : ITcpMessageFramer
    {
        private readonly int _frameLength;
        public FixedLengthMessageFramer(int frameLength)
        {
            if (frameLength <= 0) throw new ArgumentOutOfRangeException(nameof(frameLength));
            _frameLength = frameLength;
        }
        public int MaximumFrameLength { get { return _frameLength; } }
        public byte[] Encode(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length != _frameLength) throw new IndustrialProtocolException("Fixed-length payload size does not match the configured frame length.");
            return (byte[])payload.Clone();
        }
        public bool TryExtractFrame(IList<byte> buffer, out byte[] payload)
        {
            ValidateBuffer(buffer);
            if (buffer.Count < _frameLength) { payload = null; return false; }
            payload = RemovePrefix(buffer, _frameLength);
            return true;
        }
        internal static byte[] RemovePrefix(IList<byte> buffer, int count)
        {
            var result = new byte[count];
            for (var index = 0; index < count; index++) result[index] = buffer[index];
            for (var index = 0; index < count; index++) buffer.RemoveAt(0);
            return result;
        }
        internal static void ValidateBuffer(IList<byte> buffer) { if (buffer == null) throw new ArgumentNullException(nameof(buffer)); }
    }

    /// <summary>使用一个或多个分隔字节终止每个业务帧；返回的 payload 不包含分隔符。</summary>
    public sealed class DelimiterMessageFramer : ITcpMessageFramer
    {
        private readonly byte[] _delimiter;
        public DelimiterMessageFramer(byte[] delimiter, int maximumFrameLength = 1024 * 1024)
        {
            if (delimiter == null || delimiter.Length == 0) throw new ArgumentException("Delimiter cannot be empty.", nameof(delimiter));
            if (maximumFrameLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));
            _delimiter = (byte[])delimiter.Clone();
            MaximumFrameLength = maximumFrameLength;
        }
        public int MaximumFrameLength { get; private set; }
        public byte[] Encode(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaximumFrameLength) throw new IndustrialProtocolException("TCP frame exceeds the configured maximum length.");
            var result = new byte[payload.Length + _delimiter.Length];
            Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
            Buffer.BlockCopy(_delimiter, 0, result, payload.Length, _delimiter.Length);
            return result;
        }
        public bool TryExtractFrame(IList<byte> buffer, out byte[] payload)
        {
            FixedLengthMessageFramer.ValidateBuffer(buffer);
            for (var start = 0; start <= buffer.Count - _delimiter.Length; start++)
            {
                var matches = true;
                for (var offset = 0; offset < _delimiter.Length; offset++) if (buffer[start + offset] != _delimiter[offset]) { matches = false; break; }
                if (!matches) continue;
                if (start > MaximumFrameLength) throw new IndustrialProtocolException("TCP frame exceeds the configured maximum length.");
                payload = FixedLengthMessageFramer.RemovePrefix(buffer, start);
                FixedLengthMessageFramer.RemovePrefix(buffer, _delimiter.Length);
                return true;
            }
            if (buffer.Count > MaximumFrameLength + _delimiter.Length - 1) throw new IndustrialProtocolException("TCP frame exceeds the configured maximum length.");
            payload = null;
            return false;
        }
    }

    /// <summary>使用 2 或 4 字节大端无符号长度头，长度值表示后续 payload 字节数。</summary>
    public sealed class LengthPrefixMessageFramer : ITcpMessageFramer
    {
        private readonly int _prefixLength;
        public LengthPrefixMessageFramer(int prefixLength, int maximumFrameLength = 1024 * 1024)
        {
            if (prefixLength != 2 && prefixLength != 4) throw new ArgumentOutOfRangeException(nameof(prefixLength), "Length prefix must be 2 or 4 bytes.");
            if (maximumFrameLength <= 0 || (prefixLength == 2 && maximumFrameLength > ushort.MaxValue)) throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));
            _prefixLength = prefixLength;
            MaximumFrameLength = maximumFrameLength;
        }
        public int MaximumFrameLength { get; private set; }
        public byte[] Encode(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaximumFrameLength) throw new IndustrialProtocolException("TCP frame exceeds the configured maximum length.");
            var result = new byte[_prefixLength + payload.Length];
            var length = (uint)payload.Length;
            for (var index = 0; index < _prefixLength; index++) result[_prefixLength - 1 - index] = (byte)(length >> (index * 8));
            Buffer.BlockCopy(payload, 0, result, _prefixLength, payload.Length);
            return result;
        }
        public bool TryExtractFrame(IList<byte> buffer, out byte[] payload)
        {
            FixedLengthMessageFramer.ValidateBuffer(buffer);
            if (buffer.Count < _prefixLength) { payload = null; return false; }
            uint length = 0;
            for (var index = 0; index < _prefixLength; index++) length = (length << 8) | buffer[index];
            if (length > MaximumFrameLength) throw new IndustrialProtocolException("TCP length prefix exceeds the configured maximum frame length.");
            if (buffer.Count < _prefixLength + length) { payload = null; return false; }
            FixedLengthMessageFramer.RemovePrefix(buffer, _prefixLength);
            payload = FixedLengthMessageFramer.RemovePrefix(buffer, (int)length);
            return true;
        }
    }

    /// <summary>在 TcpTransportClient 之上提供有边界的业务帧收发，并保留粘包中的后续帧。</summary>
    public sealed class FramedTcpClient : IDisposable
    {
        private readonly TcpTransportClient _transport;
        private readonly ITcpMessageFramer _framer;
        private readonly List<byte> _receiveBuffer = new List<byte>();
        private readonly SemaphoreSlim _receiveGate = new SemaphoreSlim(1, 1);
        private int _disposed;

        public FramedTcpClient(TcpTransportOptions options, ITcpMessageFramer framer)
        {
            _transport = new TcpTransportClient(options ?? throw new ArgumentNullException(nameof(options)));
            _framer = framer ?? throw new ArgumentNullException(nameof(framer));
        }
        public bool IsConnected { get { return _transport.IsConnected; } }
        public Task ConnectAsync(CancellationToken cancellationToken) { ThrowIfDisposed(); return _transport.ConnectAsync(cancellationToken); }
        public Task DisconnectAsync(CancellationToken cancellationToken) { ThrowIfDisposed(); return _transport.DisconnectAsync(cancellationToken); }
        public Task SendFrameAsync(byte[] payload, CancellationToken cancellationToken) { ThrowIfDisposed(); return _transport.SendAsync(_framer.Encode(payload), cancellationToken); }
        public async Task<byte[]> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] payload;
                while (!_framer.TryExtractFrame(_receiveBuffer, out payload))
                {
                    var chunk = await _transport.ReceiveAsync(8192, cancellationToken).ConfigureAwait(false);
                    _receiveBuffer.AddRange(chunk);
                }
                return payload;
            }
            finally { _receiveGate.Release(); }
        }
        private void ThrowIfDisposed() { if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(FramedTcpClient)); }
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; _transport.Dispose(); _receiveGate.Dispose(); }
    }
}
