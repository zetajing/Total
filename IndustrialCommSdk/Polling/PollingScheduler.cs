using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Polling
{
    public sealed class PollingScheduler : IPollingScheduler
    {
        private readonly ConcurrentDictionary<string, SubscriptionWorker> _subscriptions = new ConcurrentDictionary<string, SubscriptionWorker>(StringComparer.OrdinalIgnoreCase);
        private readonly IIndustrialLogger _logger;

        public PollingScheduler(IIndustrialLogger logger = null)
        {
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public Task<string> SubscribeAsync(IIndustrialClient client, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var worker = new SubscriptionWorker(client, request, handler, _logger);
            if (!_subscriptions.TryAdd(request.SubscriptionKey, worker))
            {
                throw new InvalidOperationException(string.Format("Subscription '{0}' already exists.", request.SubscriptionKey));
            }

            worker.Start();
            return Task.FromResult(request.SubscriptionKey);
        }

        public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubscriptionWorker worker;
            if (_subscriptions.TryRemove(subscriptionId, out worker))
            {
                worker.Dispose();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (var worker in _subscriptions.Values)
            {
                worker.Dispose();
            }
            _subscriptions.Clear();
        }

        private sealed class SubscriptionWorker : IDisposable
        {
            private readonly IIndustrialClient _client;
            private readonly SubscriptionRequest _request;
            private readonly EventHandler<SubscriptionEvent> _handler;
            private readonly IIndustrialLogger _logger;
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private Task _loopTask;
            private string _lastFingerprint;

            public SubscriptionWorker(IIndustrialClient client, SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, IIndustrialLogger logger)
            {
                _client = client;
                _request = request;
                _handler = handler;
                _logger = logger;
            }

            public void Start()
            {
                _loopTask = Task.Run(LoopAsync);
            }

            private async Task LoopAsync()
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _client.ReadManyAsync(_request.Items, _cts.Token).ConfigureAwait(false);
                        var values = result.Values.ToList();
                        var fingerprint = BuildFingerprint(values);

                        if (!_request.ReportOnChangeOnly || !string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal))
                        {
                            _lastFingerprint = fingerprint;
                            var args = new SubscriptionEvent(_request.SubscriptionKey, values, DateTimeOffset.UtcNow);
                            _handler?.Invoke(_client, args);
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

            private static string BuildFingerprint(IEnumerable<DataValue> values)
            {
                return string.Join("|", values.Select(v => string.Format("{0}:{1}:{2}", v.Address, v.Quality, v.Value ?? "<null>")));
            }

            public void Dispose()
            {
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
