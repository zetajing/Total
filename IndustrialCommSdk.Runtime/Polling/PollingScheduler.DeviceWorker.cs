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

namespace IndustrialCommSdk.Polling
{
    /// <summary>
    /// 按设备合并轮询的调度器。同一客户端仅运行一个后台循环，多个订阅到期时会合并重复点位，
    /// 减少重复请求，并使用固定节拍推进下一次轮询时间，避免“读取耗时 + Interval”造成累计漂移。
    /// </summary>
    /// <summary>单设备轮询、批处理和事件分发。</summary>
    public sealed partial class PollingScheduler : IPollingScheduler
    {        private sealed class DeviceWorker : IDisposable
        {
            private readonly IIndustrialClient _client;
            private readonly IBatchOperationPlanner _planner;
            private readonly ProtocolCapabilities _capabilities;
            private readonly IIndustrialLogger _logger;
            private readonly Action<string, DeviceWorker> _onStopped;
            private readonly ConcurrentDictionary<string, SubscriptionRegistration> _registrations =
                new ConcurrentDictionary<string, SubscriptionRegistration>(StringComparer.OrdinalIgnoreCase);
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private readonly SemaphoreSlim _wakeSignal = new SemaphoreSlim(0, 1);
            private readonly Task _loopTask;
            private int _disposed;

            public DeviceWorker(
                IIndustrialClient client,
                ProtocolCapabilities capabilities,
                IIndustrialLogger logger,
                Action<string, DeviceWorker> onStopped)
            {
                _client = client;
                _planner = client as IBatchOperationPlanner;
                _capabilities = capabilities ?? ProtocolCapabilities.ForProtocol(client.Kind);
                _logger = logger;
                _onStopped = onStopped;
                _loopTask = Task.Run(LoopAsync);
            }

            public bool TryAdd(IIndustrialClient client, SubscriptionRegistration registration)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return false;

                if (!ReferenceEquals(_client, client))
                    throw new InvalidOperationException(
                        string.Format(
                            "Device '{0}' already has an active polling worker for a different client instance.",
                            client.DeviceId));

                if (!_registrations.TryAdd(registration.Request.SubscriptionKey, registration))
                    throw new InvalidOperationException("Subscription is already registered on this device worker.");

                if (Volatile.Read(ref _disposed) != 0)
                {
                    SubscriptionRegistration ignored;
                    _registrations.TryRemove(registration.Request.SubscriptionKey, out ignored);
                    return false;
                }

                Wake();
                return true;
            }

            public void Remove(string subscriptionId)
            {
                SubscriptionRegistration ignored;
                _registrations.TryRemove(subscriptionId, out ignored);
                Wake();
            }

            private async Task LoopAsync()
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var snapshot = _registrations.Values.ToArray();
                        if (snapshot.Length == 0)
                        {
                            await WaitForWakeAsync(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
                            continue;
                        }

                        var now = DateTimeOffset.UtcNow;
                        var due = snapshot.Where(item => item.IsDue(now)).ToArray();
                        if (due.Length == 0)
                        {
                            var nextDue = snapshot.Min(item => item.GetNextDueUtc());
                            var delay = nextDue - now;
                            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                            await WaitForWakeAsync(delay).ConfigureAwait(false);
                            continue;
                        }

                        await PollDueSubscriptionsAsync(due, now).ConfigureAwait(false);
                        foreach (var registration in due)
                            registration.AdvanceSchedule(now);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logger.Error("Device polling worker terminated unexpectedly | Device=" + _client.DeviceId, ex);
                }
                finally
                {
                    if (_onStopped != null)
                        _onStopped(_client.DeviceId, this);
                }
            }

            private async Task PollDueSubscriptionsAsync(
                IReadOnlyCollection<SubscriptionRegistration> due,
                DateTimeOffset now)
            {
                var mergedRequests = new List<ReadRequest>();
                var requestIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var registration in due)
                {
                    foreach (var request in registration.Request.Items)
                    {
                        var key = CreateRequestKey(request);
                        if (!requestIndexes.ContainsKey(key))
                        {
                            requestIndexes[key] = mergedRequests.Count;
                            mergedRequests.Add(request);
                        }
                    }
                }

                if (mergedRequests.Count == 0)
                    return;

                var valuesByKey = await ExecutePollingReadsAsync(mergedRequests).ConfigureAwait(false);

                foreach (var registration in due)
                {
                    var values = new List<DataValue>(registration.Request.Items.Count);
                    foreach (var request in registration.Request.Items)
                    {
                        DataValue value;
                        if (valuesByKey.TryGetValue(CreateRequestKey(request), out value))
                        {
                            values.Add(value);
                        }
                        else
                        {
                            values.Add(new DataValue(
                                request.Address,
                                request.DataType,
                                null,
                                null,
                                QualityStatus.Bad,
                                DateTimeOffset.UtcNow,
                                "Polling batch result did not contain the requested item."));
                        }
                    }

                    if (!registration.ShouldReport(values))
                        continue;

                    try
                    {
                        var handler = registration.Handler;
                        if (handler != null)
                            handler(_client, new SubscriptionEvent(registration.Request.SubscriptionKey, values, now));
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Subscription handler failed | Key=" + registration.Request.SubscriptionKey, ex);
                    }
                }
            }

            private async Task<Dictionary<string, DataValue>> ExecutePollingReadsAsync(IReadOnlyList<ReadRequest> mergedRequests)
            {
                var valuesByKey = new Dictionary<string, DataValue>(StringComparer.OrdinalIgnoreCase);
                var batches = CreatePollingReadBatches(mergedRequests);

                for (var i = 0; i < batches.Count; i++)
                {
                    var batch = batches[i];
                    if (batch.Count == 0) continue;

                    try
                    {
                        var result = await _client.ReadManyAsync(batch, _cts.Token).ConfigureAwait(false);
                        MapBatchResults(batch, result, valuesByKey);
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Polling read batch failed | Device={0} | Protocol={1} | Batch={2}/{3} | Requests={4}",
                                _client.DeviceId,
                                _capabilities.DisplayName,
                                i + 1,
                                batches.Count,
                                batch.Count),
                            ex);
                        MapFailedBatch(batch, valuesByKey, ex.Message);
                    }
                }

                return valuesByKey;
            }

            private IReadOnlyList<IReadOnlyList<ReadRequest>> CreatePollingReadBatches(IReadOnlyList<ReadRequest> mergedRequests)
            {
                if (_planner != null)
                {
                    try
                    {
                        var options = new BatchReadOptions(
                            maxItemsPerBatch: _capabilities.MaxReadItems,
                            maxAddressSpan: _capabilities.MaxAddressSpan,
                            maxPduBytes: _capabilities.MaxPduBytes > 0 ? (int?)_capabilities.MaxPduBytes : null);
                        var plan = _planner.PlanRead(mergedRequests, options, _capabilities);
                        var planned = ExtractReadBatches(plan);
                        if (planned.Count > 0)
                        {
                            _logger.Info(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    "Polling batch plan | Device={0} | Protocol={1} | Planner={2} | OriginalRequests={3} | PlannedBatches={4} | SavedCalls={5}",
                                    _client.DeviceId,
                                    _capabilities.DisplayName,
                                    _planner.GetType().Name,
                                    plan.OriginalRequestCount,
                                    plan.PlannedRequestCount,
                                    plan.SavedRequestCount));
                            return planned;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Polling batch planner failed; using fallback split | Device={0} | Protocol={1}",
                                _client.DeviceId,
                                _capabilities.DisplayName),
                            ex);
                    }
                }

                return CreateFallbackReadBatches(mergedRequests);
            }

            private IReadOnlyList<IReadOnlyList<ReadRequest>> ExtractReadBatches(BatchSplitPlan plan)
            {
                var batches = new List<IReadOnlyList<ReadRequest>>();
                if (plan == null || plan.Groups == null || plan.Groups.Count == 0)
                    return batches;

                foreach (var group in plan.Groups.OrderBy(item => item.Sequence))
                {
                    if (group.ReadRequests == null || group.ReadRequests.Count == 0)
                        continue;
                    batches.Add(group.ReadRequests.ToList());
                }
                return batches;
            }

            private IReadOnlyList<IReadOnlyList<ReadRequest>> CreateFallbackReadBatches(IReadOnlyList<ReadRequest> mergedRequests)
            {
                var maxItems = Math.Max(1, _capabilities.MaxReadItems);
                var batches = new List<IReadOnlyList<ReadRequest>>();

                if (mergedRequests.Count <= maxItems)
                {
                    batches.Add(mergedRequests.ToList());
                    return batches;
                }

                _logger.Warn(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Polling read split by capability | Device={0} | Protocol={1} | Requests={2} | MaxReadItems={3}",
                        _client.DeviceId,
                        _capabilities.DisplayName,
                        mergedRequests.Count,
                        maxItems));

                for (var offset = 0; offset < mergedRequests.Count; offset += maxItems)
                {
                    batches.Add(mergedRequests.Skip(offset).Take(maxItems).ToList());
                }

                return batches;
            }

            private static void MapBatchResults(
                IReadOnlyList<ReadRequest> batch,
                BatchReadResult result,
                IDictionary<string, DataValue> valuesByKey)
            {
                var values = result == null || result.Values == null ? new DataValue[0] : result.Values;
                for (var i = 0; i < batch.Count; i++)
                {
                    var request = batch[i];
                    valuesByKey[CreateRequestKey(request)] = i < values.Count
                        ? values[i]
                        : new DataValue(
                            request.Address,
                            request.DataType,
                            null,
                            null,
                            QualityStatus.Bad,
                            DateTimeOffset.UtcNow,
                            "Polling batch returned fewer values than requested.");
                }
            }

            private static void MapFailedBatch(
                IReadOnlyList<ReadRequest> batch,
                IDictionary<string, DataValue> valuesByKey,
                string error)
            {
                foreach (var request in batch)
                {
                    valuesByKey[CreateRequestKey(request)] = new DataValue(
                        request.Address,
                        request.DataType,
                        null,
                        null,
                        QualityStatus.Bad,
                        DateTimeOffset.UtcNow,
                        error);
                }
            }

            private async Task WaitForWakeAsync(TimeSpan delay)
            {
                if (delay == Timeout.InfiniteTimeSpan)
                {
                    await _wakeSignal.WaitAsync(_cts.Token).ConfigureAwait(false);
                    return;
                }

                if (delay <= TimeSpan.Zero)
                    return;

                var delayTask = Task.Delay(delay, _cts.Token);
                var wakeTask = _wakeSignal.WaitAsync(_cts.Token);
                await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);
                _cts.Token.ThrowIfCancellationRequested();
            }

            private void Wake()
            {
                try
                {
                    if (_wakeSignal.CurrentCount == 0)
                        _wakeSignal.Release();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SemaphoreFullException)
                {
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                _cts.Cancel();
                Wake();
                try
                {
                    _loopTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
                _wakeSignal.Dispose();
                _cts.Dispose();
            }
        }
    }
}

