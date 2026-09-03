using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Web.WebSockets;

namespace InduLink.Web.Gateway
{
    public interface IIndustrialWebGateway : IDisposable
    {
        IndustrialWebGatewayOptions Options { get; }
        bool IsRunning { get; }
        IReadOnlyCollection<WebSocketSessionInfo> WebSocketSessions { get; }
        event EventHandler<IndustrialWebRequestEventArgs> RequestCompleted;
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    /// <summary>不包含认证头或请求正文的脱敏 Web 请求审计事件。</summary>
    public sealed class IndustrialWebRequestEventArgs : EventArgs
    {
        internal IndustrialWebRequestEventArgs(string method, string path, int statusCode, string remoteEndpoint, DateTimeOffset timestampUtc)
        {
            Method = method;
            Path = path;
            StatusCode = statusCode;
            RemoteEndpoint = remoteEndpoint;
            TimestampUtc = timestampUtc;
        }

        public string Method { get; private set; }
        public string Path { get; private set; }
        public int StatusCode { get; private set; }
        public string RemoteEndpoint { get; private set; }
        public DateTimeOffset TimestampUtc { get; private set; }
    }
}
