using System;
using System.Collections.Generic;
using System.Net.WebSockets;

namespace InduLink.Web.WebSockets
{
    public sealed class IndustrialWebSocketClientOptions
    {
        public IndustrialWebSocketClientOptions()
        {
            Uri = new Uri("ws://127.0.0.1:8088/ws/v1/tags");
            MaxMessageBytes = 1024 * 1024;
            ReceiveBufferBytes = 8192;
            KeepAliveInterval = TimeSpan.FromSeconds(20);
            ConnectTimeout = TimeSpan.FromSeconds(10);
            AutoReconnect = true;
            InitialReconnectDelay = TimeSpan.FromSeconds(1);
            MaxReconnectDelay = TimeSpan.FromSeconds(30);
            CloseHandshakeTimeout = TimeSpan.FromSeconds(3);
            SendTimeout = TimeSpan.FromSeconds(5);
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SubProtocols = new List<string>();
        }

        public Uri Uri { get; set; }
        public string ApiKey { get; set; }
        public string Origin { get; set; }
        public int MaxMessageBytes { get; set; }
        public int ReceiveBufferBytes { get; set; }
        public TimeSpan KeepAliveInterval { get; set; }
        public TimeSpan ConnectTimeout { get; set; }
        public bool AutoReconnect { get; set; }
        public TimeSpan InitialReconnectDelay { get; set; }
        public TimeSpan MaxReconnectDelay { get; set; }
        public TimeSpan CloseHandshakeTimeout { get; set; }
        public TimeSpan SendTimeout { get; set; }
        public IDictionary<string, string> Headers { get; private set; }
        public IList<string> SubProtocols { get; private set; }

        internal IndustrialWebSocketClientOptions Clone()
        {
            var clone = new IndustrialWebSocketClientOptions
            {
                Uri = Uri,
                ApiKey = ApiKey,
                Origin = Origin,
                MaxMessageBytes = MaxMessageBytes,
                ReceiveBufferBytes = ReceiveBufferBytes,
                KeepAliveInterval = KeepAliveInterval,
                ConnectTimeout = ConnectTimeout,
                AutoReconnect = AutoReconnect,
                InitialReconnectDelay = InitialReconnectDelay,
                MaxReconnectDelay = MaxReconnectDelay,
                CloseHandshakeTimeout = CloseHandshakeTimeout,
                SendTimeout = SendTimeout,
            };
            foreach (var header in Headers) clone.Headers[header.Key] = header.Value;
            foreach (var protocol in SubProtocols) clone.SubProtocols.Add(protocol);
            return clone;
        }
    }

    public sealed class IndustrialWebSocketServerOptions
    {
        public IndustrialWebSocketServerOptions()
        {
            ListenPrefix = "http://127.0.0.1:8090/";
            WebSocketPath = "/ws/";
            RequireApiKey = true;
            MaxMessageBytes = 1024 * 1024;
            ReceiveBufferBytes = 8192;
            MaxSessions = 100;
            ShutdownTimeout = TimeSpan.FromSeconds(5);
            CloseHandshakeTimeout = TimeSpan.FromSeconds(3);
            SendTimeout = TimeSpan.FromSeconds(5);
            AllowedOrigins = new List<string>();
            SubProtocols = new List<string>();
        }

        public string ListenPrefix { get; set; }
        public string WebSocketPath { get; set; }
        public bool RequireApiKey { get; set; }
        public string ApiKey { get; set; }
        public int MaxMessageBytes { get; set; }
        public int ReceiveBufferBytes { get; set; }
        public int MaxSessions { get; set; }
        public TimeSpan ShutdownTimeout { get; set; }
        public TimeSpan CloseHandshakeTimeout { get; set; }
        public TimeSpan SendTimeout { get; set; }
        public IList<string> AllowedOrigins { get; private set; }
        public string SubProtocol { get; set; }
        public IList<string> SubProtocols { get; private set; }

        internal IndustrialWebSocketServerOptions Clone()
        {
            var clone = new IndustrialWebSocketServerOptions
            {
                ListenPrefix = ListenPrefix,
                WebSocketPath = WebSocketPath,
                RequireApiKey = RequireApiKey,
                ApiKey = ApiKey,
                MaxMessageBytes = MaxMessageBytes,
                ReceiveBufferBytes = ReceiveBufferBytes,
                MaxSessions = MaxSessions,
                ShutdownTimeout = ShutdownTimeout,
                CloseHandshakeTimeout = CloseHandshakeTimeout,
                SendTimeout = SendTimeout,
                SubProtocol = SubProtocol,
            };
            foreach (var origin in AllowedOrigins) clone.AllowedOrigins.Add(origin);
            foreach (var protocol in SubProtocols) clone.SubProtocols.Add(protocol);
            return clone;
        }
    }

    public sealed class WebSocketSessionInfo
    {
        internal WebSocketSessionInfo(string id, string remoteEndpoint, DateTimeOffset connectedUtc)
        {
            Id = id;
            RemoteEndpoint = remoteEndpoint;
            ConnectedUtc = connectedUtc;
        }

        public string Id { get; private set; }
        public string RemoteEndpoint { get; private set; }
        public DateTimeOffset ConnectedUtc { get; private set; }
    }

    public sealed class WebSocketMessageEventArgs : EventArgs
    {
        internal WebSocketMessageEventArgs(string sessionId, WebSocketMessageType messageType, byte[] payload)
        {
            SessionId = sessionId;
            MessageType = messageType;
            Payload = payload ?? new byte[0];
        }

        public string SessionId { get; private set; }
        public WebSocketMessageType MessageType { get; private set; }
        public byte[] Payload { get; private set; }
        public string Text { get { return MessageType == WebSocketMessageType.Text ? System.Text.Encoding.UTF8.GetString(Payload) : null; } }
    }

    public sealed class WebSocketSessionEventArgs : EventArgs
    {
        internal WebSocketSessionEventArgs(WebSocketSessionInfo session) { Session = session; }
        public WebSocketSessionInfo Session { get; private set; }
    }

    public sealed class WebSocketClosedEventArgs : EventArgs
    {
        internal WebSocketClosedEventArgs(string sessionId, WebSocketCloseStatus? status, string description, Exception exception)
        {
            SessionId = sessionId;
            CloseStatus = status;
            Description = description;
            Exception = exception;
        }

        public string SessionId { get; private set; }
        public WebSocketCloseStatus? CloseStatus { get; private set; }
        public string Description { get; private set; }
        public Exception Exception { get; private set; }
    }
}
