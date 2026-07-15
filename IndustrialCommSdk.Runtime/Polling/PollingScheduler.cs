using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Runtime.Polling
{
    /// <summary>
    /// 按设备合并轮询的调度器。同一客户端仅运行一个后台循环，多个订阅到期时会合并重复点位，
    /// 减少重复请求，并使用固定节拍推进下一次轮询时间，避免“读取耗时 + Interval”造成累计漂移。
    /// </summary>
    public sealed partial class PollingScheduler : IPollingScheduler
    {
        private readonly ConcurrentDictionary<string, SubscriptionRegistration> _subscriptions =
            new ConcurrentDictionary<string, SubscriptionRegistration>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, DeviceWorker> _workers =
            new ConcurrentDictionary<string, DeviceWorker>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<DeviceWorker, byte> _retiringWorkers =
            new ConcurrentDictionary<DeviceWorker, byte>();

        private readonly IIndustrialLogger _logger;
        private readonly object _lifecycleSync = new object();
        private int _disposed;

        public PollingScheduler(IIndustrialLogger logger = null)
        {
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public Task<string> SubscribeAsync(
            IIndustrialClient client,
            SubscriptionRequest request,
            EventHandler<SubscriptionEvent> handler,
            CancellationToken cancellationToken)
        {
            lock (_lifecycleSync)
            {
                ThrowIfDisposed();
                if (client == null) throw new ArgumentNullException(nameof(client));
                if (request == null) throw new ArgumentNullException(nameof(request));
                if (!string.Equals(client.DeviceId, request.DeviceId, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Subscription device ID does not match the client device ID.", nameof(request));
                if (request.Items == null || request.Items.Count == 0)
                    throw new ArgumentException("Subscription must contain at least one read request.", nameof(request));

                var capabilities = IndustrialCommSdk.Runtime.IndustrialClientPlatformExtensions.GetCapabilities(client);
                if (!capabilities.SupportsSubscriptions)
                    throw new NotSupportedException(string.Format("Protocol '{0}' does not support polling subscriptions.", capabilities.DisplayName));
                if (request.Interval < capabilities.RecommendedMinPollingInterval)
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Subscription interval {0}ms is below the recommended minimum {1}ms for {2}.",
                            request.Interval.TotalMilliseconds,
                            capabilities.RecommendedMinPollingInterval.TotalMilliseconds,
                            capabilities.DisplayName));

                cancellationToken.ThrowIfCancellationRequested();

                var registration = new SubscriptionRegistration(request, handler);
                if (!_subscriptions.TryAdd(request.SubscriptionKey, registration))
                    throw new InvalidOperationException(string.Format("Subscription '{0}' already exists.", request.SubscriptionKey));

                try
                {
                    while (true)
                    {
                        ThrowIfDisposed();
                        cancellationToken.ThrowIfCancellationRequested();

                        var worker = _workers.GetOrAdd(
                            client.DeviceId,
                            _ => new DeviceWorker(client, capabilities, _logger, RemoveStoppedWorker));

                        if (worker.TryAdd(client, registration))
                        {
                            _logger.Info(string.Format(
                                "SUBSCRIPTION started | Key={0} | Device={1} | Items={2} | Interval={3}ms | Protocol={4}",
                                request.SubscriptionKey,
                                client.DeviceId,
                                request.Items.Count,
                                request.Interval.TotalMilliseconds,
                                capabilities.DisplayName));
                            return Task.FromResult(request.SubscriptionKey);
                        }

                        // The worker was already stopping between GetOrAdd and TryAdd. Remove that exact
                        // instance and retry so the subscription is not attached to a dead loop.
                        RemoveStoppedWorker(client.DeviceId, worker);
                    }
                }
                catch
                {
                    SubscriptionRegistration ignored;
                    _subscriptions.TryRemove(request.SubscriptionKey, out ignored);
                    throw;
                }
            }
        }

        public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            lock (_lifecycleSync)
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(subscriptionId))
                    throw new ArgumentException("Subscription ID is required.", nameof(subscriptionId));

                SubscriptionRegistration registration;
                if (_subscriptions.TryRemove(subscriptionId, out registration))
                {
                    DeviceWorker worker;
                    if (_workers.TryGetValue(registration.Request.DeviceId, out worker))
                        worker.Remove(subscriptionId);

                    _logger.Info("SUBSCRIPTION stopped | Key=" + subscriptionId);
                }

                return Task.CompletedTask;
            }
        }

        public void Dispose()
        {
            DeviceWorker[] workers;
            lock (_lifecycleSync)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                workers = _workers.Values.Concat(_retiringWorkers.Keys).Distinct().ToArray();
            }

            foreach (var worker in workers)
                worker.Dispose();

            _workers.Clear();
            _retiringWorkers.Clear();
            _subscriptions.Clear();
        }

        private void RemoveStoppedWorker(string deviceId, DeviceWorker worker)
        {
            DeviceWorker current;
            if (_workers.TryGetValue(deviceId, out current) && ReferenceEquals(current, worker))
            {
                DeviceWorker removed;
                _workers.TryRemove(deviceId, out removed);
            }

            if (worker.IsStopped)
            {
                byte ignored;
                _retiringWorkers.TryRemove(worker, out ignored);
                return;
            }

            _retiringWorkers.TryAdd(worker, 0);
            if (worker.IsStopped)
            {
                byte ignored;
                _retiringWorkers.TryRemove(worker, out ignored);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(GetType().FullName);
        }

        private static string CreateRequestKey(ReadRequest request)
        {
            return string.Concat(
                request.DeviceId, "|",
                request.Address, "|",
                request.DataType.ToString(), "|",
                request.Length.ToString(CultureInfo.InvariantCulture), "|",
                request.Timeout.HasValue ? request.Timeout.Value.Ticks.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }

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
    }
}
