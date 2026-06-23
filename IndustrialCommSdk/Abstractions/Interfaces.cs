using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Abstractions
{
    public interface IIndustrialClient : IDisposable
    {
        string DeviceId { get; }
        ProtocolKind Kind { get; }
        bool IsConnected { get; }

        Task ConnectAsync(CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
        Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken);
        Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken);
        Task WriteAsync(WriteRequest request, CancellationToken cancellationToken);
        Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken);
        Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken);
        Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken);
        HealthSnapshot GetHealth();
    }

    public interface IAddressParser
    {
        object Parse(string address);
    }

    public interface IPollingScheduler : IDisposable
    {
        Task<string> SubscribeAsync(IIndustrialClient client, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken);
        Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken);
    }
}
