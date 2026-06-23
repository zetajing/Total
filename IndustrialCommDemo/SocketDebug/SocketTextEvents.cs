using System;

namespace IndustrialCommDemo.SocketDebug
{
    internal sealed class SocketTextMessageEventArgs : EventArgs
    {
        public SocketTextMessageEventArgs(Guid sessionId, string remoteEndPoint, string message)
        {
            SessionId = sessionId;
            RemoteEndPoint = remoteEndPoint ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public Guid SessionId { get; private set; }
        public string RemoteEndPoint { get; private set; }
        public string Message { get; private set; }
    }

    internal sealed class SocketSessionEventArgs : EventArgs
    {
        public SocketSessionEventArgs(Guid sessionId, string remoteEndPoint, int sessionCount)
        {
            SessionId = sessionId;
            RemoteEndPoint = remoteEndPoint ?? string.Empty;
            SessionCount = sessionCount;
        }

        public Guid SessionId { get; private set; }
        public string RemoteEndPoint { get; private set; }
        public int SessionCount { get; private set; }
    }
}
