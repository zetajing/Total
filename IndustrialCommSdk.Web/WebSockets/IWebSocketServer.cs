using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Web.WebSockets
{
    public interface IWebSocketServer : IDisposable
    {
        bool IsRunning { get; }
        IReadOnlyCollection<WebSocketSessionInfo> Sessions { get; }
        event EventHandler<WebSocketSessionEventArgs> SessionConnected;
        event EventHandler<WebSocketMessageEventArgs> MessageReceived;
        event EventHandler<WebSocketClosedEventArgs> SessionClosed;
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task SendTextAsync(string sessionId, string text, CancellationToken cancellationToken);
        Task SendBinaryAsync(string sessionId, byte[] payload, CancellationToken cancellationToken);
        Task BroadcastTextAsync(string text, CancellationToken cancellationToken);
        Task BroadcastBinaryAsync(byte[] payload, CancellationToken cancellationToken);
        Task CloseSessionAsync(string sessionId, CancellationToken cancellationToken);
    }
}
