using System;

namespace InduLink.Web.WebSockets
{
    public sealed class WebSocketMessageTooLargeException : InvalidOperationException
    {
        public WebSocketMessageTooLargeException(int limit)
            : base(string.Format("WebSocket message exceeded the configured limit of {0} bytes.", limit))
        {
            Limit = limit;
        }

        public int Limit { get; private set; }
    }
}
