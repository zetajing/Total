using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Web.WebSockets;

namespace InduLink.Web.Internal
{
    internal sealed class ManagedWebSocket : IDisposable
    {
        private readonly WebSocket _socket;
        private readonly int _maxMessageBytes;
        private readonly int _receiveBufferBytes;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private int _disposed;

        internal ManagedWebSocket(WebSocket socket, int maxMessageBytes, int receiveBufferBytes)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _maxMessageBytes = maxMessageBytes;
            _receiveBufferBytes = receiveBufferBytes;
        }

        internal WebSocketState State { get { return _socket.State; } }
        internal WebSocketCloseStatus? CloseStatus { get { return _socket.CloseStatus; } }
        internal string CloseStatusDescription { get { return _socket.CloseStatusDescription; } }

        internal async Task SendAsync(byte[] payload, WebSocketMessageType messageType, CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (messageType != WebSocketMessageType.Text && messageType != WebSocketMessageType.Binary)
                throw new ArgumentOutOfRangeException(nameof(messageType));
            ThrowIfDisposed();
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_socket.State != WebSocketState.Open) throw new InvalidOperationException("WebSocket is not open.");
                await _socket.SendAsync(new ArraySegment<byte>(payload), messageType, true, cancellationToken).ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        internal async Task<ReceivedWebSocketMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var buffer = new byte[_receiveBufferBytes];
            using (var output = new MemoryStream())
            {
                WebSocketMessageType? type = null;
                while (true)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return ReceivedWebSocketMessage.Close(result.CloseStatus, result.CloseStatusDescription);
                    if (type.HasValue && type.Value != result.MessageType)
                        throw new WebSocketException("A fragmented WebSocket message changed message type.");
                    type = result.MessageType;
                    if (output.Length + result.Count > _maxMessageBytes)
                        throw new WebSocketMessageTooLargeException(_maxMessageBytes);
                    output.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                        return ReceivedWebSocketMessage.Data(type.Value, output.ToArray());
                }
            }
        }

        internal async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                    await _socket.CloseAsync(status, description, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException) { }
            finally { _sendGate.Release(); }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ManagedWebSocket));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _socket.Dispose();
        }
    }

    internal sealed class ReceivedWebSocketMessage
    {
        private ReceivedWebSocketMessage(WebSocketMessageType type, byte[] payload, WebSocketCloseStatus? status, string description)
        {
            MessageType = type;
            Payload = payload;
            CloseStatus = status;
            CloseDescription = description;
        }

        internal WebSocketMessageType MessageType { get; private set; }
        internal byte[] Payload { get; private set; }
        internal WebSocketCloseStatus? CloseStatus { get; private set; }
        internal string CloseDescription { get; private set; }
        internal bool IsClose { get { return MessageType == WebSocketMessageType.Close; } }

        internal static ReceivedWebSocketMessage Data(WebSocketMessageType type, byte[] payload)
        {
            return new ReceivedWebSocketMessage(type, payload, null, null);
        }

        internal static ReceivedWebSocketMessage Close(WebSocketCloseStatus? status, string description)
        {
            return new ReceivedWebSocketMessage(WebSocketMessageType.Close, new byte[0], status, description);
        }
    }

}
