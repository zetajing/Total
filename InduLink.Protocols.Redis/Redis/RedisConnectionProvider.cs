using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace InduLink.Protocols.Redis
{
    /// <summary>
    /// Provides a shared StackExchange.Redis connection for an endpoint/configuration key.
    /// ConnectionMultiplexer is thread-safe and is intentionally owned by this provider,
    /// not by an individual RedisClient instance.
    /// </summary>
    public interface IRedisConnectionProvider : IDisposable
    {
        Task<ConnectionMultiplexer> GetOrCreateAsync(
            string key,
            Func<Task<ConnectionMultiplexer>> connectAsync,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Reuses one ConnectionMultiplexer per Redis endpoint and connection configuration.
    /// Failed connection tasks are removed so a later connect can retry cleanly.
    /// </summary>
    public sealed class RedisConnectionProvider : IRedisConnectionProvider
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<ConnectionMultiplexer>>> _connections =
            new ConcurrentDictionary<string, Lazy<Task<ConnectionMultiplexer>>>(StringComparer.Ordinal);
        private int _disposed;

        /// <summary>
        /// Process-wide provider for applications that do not need an explicit lifetime owner.
        /// </summary>
        public static RedisConnectionProvider Shared { get; } = new RedisConnectionProvider();

        public async Task<ConnectionMultiplexer> GetOrCreateAsync(
            string key,
            Func<Task<ConnectionMultiplexer>> connectAsync,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Connection key is required.", nameof(key));
            if (connectAsync == null) throw new ArgumentNullException(nameof(connectAsync));
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var lazy = _connections.GetOrAdd(
                key,
                _ => new Lazy<Task<ConnectionMultiplexer>>(
                    connectAsync,
                    LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                var connection = await lazy.Value.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return connection;
            }
            catch
            {
                RemoveIfCurrent(key, lazy);
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            foreach (var pair in _connections)
            {
                var lazy = pair.Value;
                if (!lazy.IsValueCreated) continue;

                var task = lazy.Value;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    try { task.GetAwaiter().GetResult().Dispose(); }
                    catch { }
                }
                else if (!task.IsCompleted)
                {
                    task.ContinueWith(
                        completed =>
                        {
                            try { completed.Result.Dispose(); }
                            catch { }
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }

            _connections.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(RedisConnectionProvider));
        }

        private void RemoveIfCurrent(string key, Lazy<Task<ConnectionMultiplexer>> value)
        {
            var entries = (ICollection<KeyValuePair<string, Lazy<Task<ConnectionMultiplexer>>>>)_connections;
            entries.Remove(new KeyValuePair<string, Lazy<Task<ConnectionMultiplexer>>>(key, value));
        }
    }
}
