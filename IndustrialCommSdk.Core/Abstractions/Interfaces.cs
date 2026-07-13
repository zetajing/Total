using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>
    /// 工业设备客户端的统一接口，封装连接、读写、订阅和健康检查能力。
    /// 所有协议客户端实现均遵循此接口，提供一致的工业通讯抽象。
    /// </summary>
    public interface IIndustrialClient : IDisposable
    {
        /// <summary>获取客户端所对应的设备标识。</summary>
        string DeviceId { get; }
        /// <summary>获取客户端使用的通信协议类型。</summary>
        ProtocolKind Kind { get; }
        /// <summary>获取底层连接当前是否可用。</summary>
        bool IsConnected { get; }

        /// <summary>异步连接设备。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步连接操作的任务。</returns>
        Task ConnectAsync(CancellationToken cancellationToken);
        /// <summary>异步断开设备连接。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步断开操作的任务。</returns>
        Task DisconnectAsync(CancellationToken cancellationToken);
        /// <summary>读取单个地址；通信失败时实现可返回质量为 Bad 的值。</summary>
        /// <param name="request">读取请求。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>读取结果，含数据值和质量信息。</returns>
        Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken);
        /// <summary>批量读取多个地址。</summary>
        /// <param name="requests">读取请求集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>批量读取结果。</returns>
        Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken);
        /// <summary>向单个地址写入数据。</summary>
        /// <param name="request">写入请求。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        Task WriteAsync(WriteRequest request, CancellationToken cancellationToken);
        /// <summary>批量写入多个地址。</summary>
        /// <param name="requests">写入请求集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步批量写入操作的任务。</returns>
        Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken);
        /// <summary>按指定周期轮询一组地址，并返回订阅标识。</summary>
        /// <param name="request">订阅请求。</param>
        /// <param name="handler">数据到达时的事件处理程序。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>订阅标识字符串。</returns>
        Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken);
        /// <summary>停止指定订阅。</summary>
        /// <param name="subscriptionId">订阅标识。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步取消订阅操作的任务。</returns>
        Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken);
        /// <summary>获取客户端最近一次操作形成的健康快照。</summary>
        /// <returns>包含连接状态和错误信息的快照。</returns>
        HealthSnapshot GetHealth();
    }

    /// <summary>
    /// 将用户输入的协议地址字符串解析为协议专用地址对象的接口。
    /// </summary>
    public interface IAddressParser
    {
        /// <summary>解析地址；地址格式无效时应抛出地址解析异常。</summary>
        /// <param name="address">用户输入的地址字符串。</param>
        /// <returns>协议专用地址对象。</returns>
        object Parse(string address);
    }

    /// <summary>
    /// 管理周期性读取任务的轮询调度器接口。
    /// </summary>
    public interface IPollingScheduler : IDisposable
    {
        /// <summary>创建并启动一个轮询订阅。</summary>
        /// <param name="client">被轮询的客户端。</param>
        /// <param name="request">订阅配置。</param>
        /// <param name="handler">数据回调。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>订阅标识。</returns>
        Task<string> SubscribeAsync(IIndustrialClient client, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken);
        /// <summary>停止并移除指定轮询订阅。</summary>
        /// <param name="subscriptionId">订阅标识。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步取消订阅操作的任务。</returns>
        Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken);
    }
}
