using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Polling
{
    /// <summary>
    /// 轮询调度器。实现 <see cref="IPollingScheduler"/> 接口，管理多个订阅的轮询任务，
    /// 按指定的时间间隔从工业客户端读取数据并通过事件通知订阅者。支持仅在数据变化时报告。
    /// </summary>
    public sealed class PollingScheduler : IPollingScheduler
    {
        /// <summary>
        /// 所有活跃订阅的并发字典，以订阅键（不区分大小写）为键，对应的 <see cref="SubscriptionWorker"/> 为值。
        /// </summary>
        private readonly ConcurrentDictionary<string, SubscriptionWorker> _subscriptions = new ConcurrentDictionary<string, SubscriptionWorker>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 用于记录日志的日志记录器实例。
        /// </summary>
        private readonly IIndustrialLogger _logger;

        /// <summary>
        /// 使用可选的日志记录器初始化 <see cref="PollingScheduler"/> 类的新实例。
        /// </summary>
        /// <param name="logger">用于记录日志的 <see cref="IIndustrialLogger"/> 实例。如果为 <c>null</c>，则使用 <see cref="NullIndustrialLogger.Instance"/>。</param>
        public PollingScheduler(IIndustrialLogger logger = null)
        {
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        /// <summary>
        /// 异步创建一个新的订阅。为指定的客户端和请求创建轮询工作线程，并将其添加到订阅集合中。
        /// </summary>
        /// <param name="client">要轮询读取数据的工业客户端。</param>
        /// <param name="request">订阅请求，包含要读取的项列表、轮询间隔和订阅键。</param>
        /// <param name="handler">当轮询到数据时调用的事件处理程序。</param>
        /// <param name="cancellationToken">用于取消订阅操作的取消令牌。</param>
        /// <returns>表示异步订阅操作的任务，结果包含新创建的订阅的唯一标识符（即请求的 <see cref="SubscriptionRequest.SubscriptionKey"/>）。</returns>
        /// <exception cref="OperationCanceledException">取消令牌被触发时抛出。</exception>
        /// <exception cref="InvalidOperationException">具有相同 <see cref="SubscriptionRequest.SubscriptionKey"/> 的订阅已存在时抛出。</exception>
        public Task<string> SubscribeAsync(IIndustrialClient client, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();

            var worker = new SubscriptionWorker(client, request, handler, _logger);
            if (!_subscriptions.TryAdd(request.SubscriptionKey, worker))
            {
                throw new InvalidOperationException(string.Format("Subscription '{0}' already exists.", request.SubscriptionKey));
            }

            worker.Start();
            _logger.Info(string.Format("SUBSCRIPTION started | Key={0} | Device={1} | Items={2} | Interval={3}ms", request.SubscriptionKey, client.DeviceId, request.Items.Count, request.Interval.TotalMilliseconds));
            return Task.FromResult(request.SubscriptionKey);
        }

        /// <summary>
        /// 异步取消指定的订阅。从订阅集合中移除对应的轮询工作线程并释放其资源。
        /// </summary>
        /// <param name="subscriptionId">要取消的订阅标识符（即订阅键）。</param>
        /// <param name="cancellationToken">用于取消操作的取消令牌。</param>
        /// <returns>表示异步取消订阅操作的任务。</returns>
        /// <exception cref="OperationCanceledException">取消令牌被触发时抛出。</exception>
        public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubscriptionWorker worker;
            if (_subscriptions.TryRemove(subscriptionId, out worker))
            {
                worker.Dispose();
                _logger.Info("SUBSCRIPTION stopped | Key=" + subscriptionId);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 释放调度器使用的所有资源。释放所有活跃的订阅工作线程并清空订阅集合。
        /// </summary>
        public void Dispose()
        {
            foreach (var worker in _subscriptions.Values)
            {
                worker.Dispose();
            }
            _subscriptions.Clear();
        }

        /// <summary>
        /// 订阅工作线程。内部类，负责为单个订阅执行轮询循环，
        /// 按指定间隔从工业客户端读取数据，计算数据指纹以支持仅变化报告，并通过事件处理程序通知订阅者。
        /// </summary>
        private sealed class SubscriptionWorker : IDisposable
        {
            /// <summary>
            /// 用于执行数据读取操作的工业客户端。
            /// </summary>
            private readonly IIndustrialClient _client;

            /// <summary>
            /// 订阅请求配置，包含要读取的项列表、轮询间隔和订阅键。
            /// </summary>
            private readonly SubscriptionRequest _request;

            /// <summary>
            /// 当轮询到数据时调用的事件处理程序。
            /// </summary>
            private readonly EventHandler<SubscriptionEvent> _handler;

            /// <summary>
            /// 用于记录轮询过程中发生的错误和信息的日志记录器。
            /// </summary>
            private readonly IIndustrialLogger _logger;

            /// <summary>
            /// 用于控制轮询生命周期和取消操作的取消令牌源。
            /// </summary>
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();

            /// <summary>
            /// 轮询循环的后台任务引用，用于在释放时等待循环结束。
            /// </summary>
            private Task _loopTask;

            /// <summary>
            /// 上次轮询的数据指纹，用于在 <see cref="SubscriptionRequest.ReportOnChangeOnly"/> 启用时判断数据是否发生变化。
            /// </summary>
            private string _lastFingerprint;

            /// <summary>
            /// 使用指定的客户端、请求、处理程序和日志记录器初始化 <see cref="SubscriptionWorker"/> 类的新实例。
            /// </summary>
            /// <param name="client">要轮询读取数据的工业客户端。</param>
            /// <param name="request">订阅请求配置。</param>
            /// <param name="handler">数据到达时的事件处理程序。</param>
            /// <param name="logger">日志记录器实例。</param>
            public SubscriptionWorker(IIndustrialClient client, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, IIndustrialLogger logger)
            {
                _client = client;
                _request = request;
                _handler = handler;
                _logger = logger;
            }

            /// <summary>
            /// 启动轮询循环。在后台任务中运行 <see cref="LoopAsync"/> 方法。
            /// </summary>
            public void Start()
            {
                _logger.Trace("SUBSCRIPTION loop starting | Key=" + _request.SubscriptionKey);
                _loopTask = Task.Run(LoopAsync);
            }

            /// <summary>
            /// 异步轮询循环。在取消令牌被触发前持续执行以下操作：
            /// 读取数据、计算数据指纹、根据变化检测策略决定是否触发事件、等待指定的轮询间隔。
            /// 循环中的异常会被记录，但不会终止循环（<see cref="OperationCanceledException"/> 除外）。
            /// </summary>
            /// <returns>表示异步轮询循环的任务。</returns>
            private async Task LoopAsync()
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _client.ReadManyAsync(_request.Items, _cts.Token).ConfigureAwait(false);
                        var values = result.Values;
                        var fingerprint = BuildFingerprint(values);

                        if (!_request.ReportOnChangeOnly || !string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
                        {
                            _lastFingerprint = fingerprint;
                            var args = new SubscriptionEvent(_request.SubscriptionKey, values, DateTimeOffset.UtcNow);
                            _handler?.Invoke(_client, args);
                            _logger.Trace(string.Format("SUBSCRIPTION reported | Key={0} | Values={1}", _request.SubscriptionKey, values.Count));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(string.Format("Subscription loop failed: {0}", _request.SubscriptionKey), ex);
                    }

                    try
                    {
                        await Task.Delay(_request.Interval, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            /// <summary>
            /// 根据数据值集合构建指纹字符串。将每个数据值的地址、质量和值连接成以竖线分隔的字符串，
            /// 用于快速比较两组数据是否相同。空值显示为 "&lt;null&gt;"。
            /// </summary>
            /// <param name="values">要构建指纹的数据值集合。</param>
            /// <returns>表示数据快照的指纹字符串。</returns>
            private static string BuildFingerprint(IEnumerable<DataValue> values)
            {
                var builder = new StringBuilder();
                foreach (var value in values)
                {
                    if (builder.Length > 0) builder.Append('|');
                    builder.Append(value.Address).Append(':').Append(value.Quality).Append(':');
                    AppendValue(builder, value.Value);
                }
                return builder.ToString();
            }

            private static void AppendValue(StringBuilder builder, object value)
            {
                if (value == null)
                {
                    builder.Append("<null>");
                    return;
                }

                var bytes = value as byte[];
                if (bytes != null)
                {
                    builder.Append(Convert.ToBase64String(bytes));
                    return;
                }

                if (!(value is string) && value is IEnumerable sequence)
                {
                    builder.Append('[');
                    var first = true;
                    foreach (var item in sequence)
                    {
                        if (!first) builder.Append(',');
                        AppendValue(builder, item);
                        first = false;
                    }
                    builder.Append(']');
                    return;
                }

                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }

            /// <summary>
            /// 释放工作线程使用的所有资源。取消轮询循环，等待循环任务最多 1 秒后完成，然后释放取消令牌源。
            /// </summary>
            public void Dispose()
            {
                _logger.Trace("SUBSCRIPTION loop stopping | Key=" + _request.SubscriptionKey);
                _cts.Cancel();
                try
                {
                    _loopTask?.Wait(TimeSpan.FromSeconds(1));
                }
                catch
                {
                }
                _cts.Dispose();
            }
        }
    }
}
