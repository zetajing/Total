using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Mes
{
    public interface IMesClient : IDisposable
    {
        bool IsConnected { get; }
        MesConnectionState State { get; }
        event EventHandler<MesConnectionStateChangedEventArgs> ConnectionStateChanged;
        event EventHandler<MesMessageEventArgs<FaCheckMessage>> FaCheckReceived;
        event EventHandler<MesMessageEventArgs<FaNumMessage>> FaNumReceived;
        event EventHandler<MesRawMessageEventArgs> RawMessage;
        event EventHandler<MesProtocolErrorEventArgs> ProtocolError;
        Task ConnectAsync(CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
        Task SendOnlineAsync(CancellationToken cancellationToken);
        Task SendTrackAsync(FaTrackMessage message, CancellationToken cancellationToken);
    }
}
