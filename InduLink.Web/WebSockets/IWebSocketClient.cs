using System;
using System.Threading;
using System.Threading.Tasks;

namespace InduLink.Web.WebSockets
{
    public interface IWebSocketClient : IDisposable
    {
        bool IsConnected { get; }
        event EventHandler Connected;
        event EventHandler<WebSocketMessageEventArgs> MessageReceived;
        event EventHandler<WebSocketClosedEventArgs> Closed;
        Task ConnectAsync(CancellationToken cancellationToken);
        Task SendTextAsync(string text, CancellationToken cancellationToken);
        Task SendBinaryAsync(byte[] payload, CancellationToken cancellationToken);
        Task CloseAsync(CancellationToken cancellationToken);
    }
}
