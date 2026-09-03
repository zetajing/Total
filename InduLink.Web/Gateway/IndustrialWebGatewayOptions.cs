using System;
using System.Collections.Generic;

namespace InduLink.Web.Gateway
{
    /// <summary>工业 Tag WebAPI 与 WebSocket 网关选项。</summary>
    public sealed class IndustrialWebGatewayOptions
    {
        public IndustrialWebGatewayOptions()
        {
            ListenPrefix = "http://127.0.0.1:8088/";
            WebSocketPath = "/ws/v1/tags";
            RequireApiKey = true;
            MaxRequestContentBytes = 1024 * 1024;
            MaxWebSocketMessageBytes = 1024 * 1024;
            ReceiveBufferBytes = 8192;
            MaxConcurrentRequests = 32;
            MaxWebSocketSessions = 100;
            MaxBatchItems = 200;
            HeartbeatInterval = TimeSpan.FromSeconds(15);
            ShutdownTimeout = TimeSpan.FromSeconds(5);
            RequestTimeout = TimeSpan.FromSeconds(30);
            WebSocketSendTimeout = TimeSpan.FromSeconds(5);
            CloseHandshakeTimeout = TimeSpan.FromSeconds(3);
            MaxSubscriptionsPerSession = 500;
            AllowedOrigins = new List<string>();
        }

        public string ListenPrefix { get; set; }
        public string WebSocketPath { get; set; }
        public bool RequireApiKey { get; set; }
        public string ApiKey { get; set; }
        public int MaxRequestContentBytes { get; set; }
        public int MaxWebSocketMessageBytes { get; set; }
        public int ReceiveBufferBytes { get; set; }
        public int MaxConcurrentRequests { get; set; }
        public int MaxWebSocketSessions { get; set; }
        public int MaxBatchItems { get; set; }
        public TimeSpan HeartbeatInterval { get; set; }
        public TimeSpan ShutdownTimeout { get; set; }
        public TimeSpan RequestTimeout { get; set; }
        public TimeSpan WebSocketSendTimeout { get; set; }
        public TimeSpan CloseHandshakeTimeout { get; set; }
        public int MaxSubscriptionsPerSession { get; set; }
        public IList<string> AllowedOrigins { get; private set; }

        internal IndustrialWebGatewayOptions Clone()
        {
            var clone = new IndustrialWebGatewayOptions
            {
                ListenPrefix = ListenPrefix,
                WebSocketPath = WebSocketPath,
                RequireApiKey = RequireApiKey,
                ApiKey = ApiKey,
                MaxRequestContentBytes = MaxRequestContentBytes,
                MaxWebSocketMessageBytes = MaxWebSocketMessageBytes,
                ReceiveBufferBytes = ReceiveBufferBytes,
                MaxConcurrentRequests = MaxConcurrentRequests,
                MaxWebSocketSessions = MaxWebSocketSessions,
                MaxBatchItems = MaxBatchItems,
                HeartbeatInterval = HeartbeatInterval,
                ShutdownTimeout = ShutdownTimeout,
                RequestTimeout = RequestTimeout,
                WebSocketSendTimeout = WebSocketSendTimeout,
                CloseHandshakeTimeout = CloseHandshakeTimeout,
                MaxSubscriptionsPerSession = MaxSubscriptionsPerSession,
            };
            foreach (var origin in AllowedOrigins) clone.AllowedOrigins.Add(origin);
            return clone;
        }
    }
}
