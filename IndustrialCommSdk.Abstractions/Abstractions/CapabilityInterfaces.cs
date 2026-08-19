using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>Common lifecycle contract shared by protocol capability clients.</summary>
    public interface IIndustrialConnection : IDisposable
    {
        string DeviceId { get; }
        ProtocolKind Kind { get; }
        bool IsConnected { get; }
        Task ConnectAsync(CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
        HealthSnapshot GetHealth();
    }

    /// <summary>Register/bit address read and write capability for PLC-like protocols.</summary>
    public interface IRegisterClient : IIndustrialConnection
    {
        Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken);
        Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken);
        Task WriteAsync(WriteRequest request, CancellationToken cancellationToken);
        Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken);
    }

    /// <summary>Polling or native event subscription capability.</summary>
    public interface IEventSubscriptionClient : IIndustrialConnection
    {
        Task<string> SubscribeAsync(
            SubscriptionRequest request,
            EventHandler<SubscriptionEvent> handler,
            CancellationToken cancellationToken);

        Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Protocol-neutral byte key/value capability. Keys are not interpreted as PLC addresses,
    /// and values are not decoded into a PLC DataType.
    /// </summary>
    public interface IKeyValueClient : IIndustrialConnection
    {
        Task<KeyValueValue> GetAsync(string key, CancellationToken cancellationToken);
        Task<IReadOnlyList<KeyValueValue>> GetManyAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
        Task SetAsync(string key, byte[] value, CancellationToken cancellationToken);
        Task SetManyAsync(IReadOnlyCollection<KeyValueWrite> entries, CancellationToken cancellationToken);
    }

    /// <summary>Common lifecycle portion of a file-transfer client.</summary>
    public interface IFileTransferClient : IDisposable
    {
        bool IsConnected { get; }
        Task ConnectAsync(CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
