using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Polling;
using IndustrialCommSdk.Protocols.Common;
using StackExchange.Redis;

namespace IndustrialCommSdk.Protocols.Redis
{
    public sealed class RedisClientOptions
    {
        public string DeviceId { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 6379;
        public string Username { get; set; }
        public string Password { get; set; }
        public int Database { get; set; }
        public bool Ssl { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
    }

    /// <summary>Redis 键值客户端。工业地址直接映射为 Redis key。</summary>
    public sealed class RedisClient : IndustrialClientBase
    {
        private readonly RedisClientOptions _options;
        private readonly IRedisConnectionProvider _connectionProvider;
        private ConnectionMultiplexer _connection;
        private IDatabase _database;

        public RedisClient(RedisClientOptions options, IIndustrialLogger logger = null,
            IPollingScheduler pollingScheduler = null, IRedisConnectionProvider connectionProvider = null)
            : base(GetDeviceId(options), ProtocolKind.Redis, pollingScheduler ?? new PollingScheduler(logger),
                logger ?? NullIndustrialLogger.Instance, options.OperationTimeoutMilliseconds)
        {
            _options = options;
            _connectionProvider = connectionProvider ?? RedisConnectionProvider.Shared;
            if (string.IsNullOrWhiteSpace(options.Host)) throw new ArgumentException("Redis host is required.", nameof(options));
            if (options.Port <= 0 || options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));
            if (options.Database < 0) throw new ArgumentOutOfRangeException(nameof(options.Database));
            if (options.ConnectTimeoutMilliseconds <= 0 || options.OperationTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Timeouts must be positive.");
        }

        private static string GetDeviceId(RedisClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceId)) throw new ArgumentException("Device ID is required.", nameof(options));
            return options.DeviceId;
        }

        public override bool IsConnected { get { return _connection != null && _connection.IsConnected; } }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            DetachConnection();
            var configuration = new ConfigurationOptions
            {
                // Let the multiplexer reconnect internally while the host remains the single
                // owner of the higher-level client reconnect lifecycle.
                AbortOnConnectFail = false, ConnectTimeout = _options.ConnectTimeoutMilliseconds,
                SyncTimeout = _options.OperationTimeoutMilliseconds, AsyncTimeout = _options.OperationTimeoutMilliseconds,
                User = _options.Username, Password = _options.Password, Ssl = _options.Ssl
            };
            configuration.EndPoints.Add(_options.Host, _options.Port);
            try
            {
                var connection = await _connectionProvider.GetOrCreateAsync(
                    BuildConnectionKey(_options),
                    () => ConnectionMultiplexer.ConnectAsync(configuration),
                    cancellationToken).ConfigureAwait(false);
                _connection = connection;
                _database = connection.GetDatabase(_options.Database);
            }
            catch (OperationCanceledException) { DetachConnection(); throw; }
            catch (Exception ex) { DetachConnection(); throw new IndustrialConnectionException("Failed to connect Redis.", ex); }
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); DetachConnection(); return Task.CompletedTask; }

        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var value = await _database.StringGetAsync(request.Address).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (value.IsNull) return new DataValue(request.Address, request.DataType, null, null,
                QualityStatus.Bad, DateTimeOffset.UtcNow, "Redis key does not exist.");
            var bytes = (byte[])value;
            return new DataValue(request.Address, request.DataType, TextValueCodec.Decode(request.DataType, bytes), bytes,
                QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        protected override async Task<BatchReadResult> ReadManyCoreAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var list = requests.ToList();
            var values = await _database.StringGetAsync(list.Select(x => (RedisKey)x.Address).ToArray()).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var result = new List<DataValue>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var request = list[i]; var value = values[i];
                if (value.IsNull) result.Add(new DataValue(request.Address, request.DataType, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, "Redis key does not exist."));
                else { var bytes = (byte[])value; result.Add(new DataValue(request.Address, request.DataType, TextValueCodec.Decode(request.DataType, bytes), bytes, QualityStatus.Good, DateTimeOffset.UtcNow, null)); }
            }
            return new BatchReadResult(result);
        }

        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            if (!await _database.StringSetAsync(request.Address, TextValueCodec.Encode(request.DataType, request.Value)).ConfigureAwait(false))
                throw new IndustrialProtocolException("Redis SET returned false.");
            cancellationToken.ThrowIfCancellationRequested();
        }

        protected override async Task WriteManyCoreAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var entries = requests.Select(x => new KeyValuePair<RedisKey, RedisValue>(x.Address, TextValueCodec.Encode(x.DataType, x.Value))).ToArray();
            if (!await _database.StringSetAsync(entries).ConfigureAwait(false)) throw new IndustrialProtocolException("Redis batch SET returned false.");
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void EnsureConnected() { if (!IsConnected || _database == null) throw new IndustrialConnectionException("Redis client is not connected."); }
        protected override void OnOperationTimeout()
        {
            Logger.Warn("Redis operation timed out; keeping the shared ConnectionMultiplexer for its reconnect loop.");
        }

        protected override void DisposeCore() { DetachConnection(); }

        private void DetachConnection()
        {
            _database = null;
            Interlocked.Exchange(ref _connection, null);
            // The provider owns the shared multiplexer. A client disconnect must not
            // tear down connections used by other RedisClient instances.
        }

        private static string BuildConnectionKey(RedisClientOptions options)
        {
            using (var sha = SHA256.Create())
            {
                var passwordBytes = Encoding.UTF8.GetBytes(options.Password ?? string.Empty);
                var passwordHash = Convert.ToBase64String(sha.ComputeHash(passwordBytes));
                return string.Format(
                    "{0}:{1}|{2}|ssl={3}|connect={4}|operation={5}|password={6}",
                    options.Host,
                    options.Port,
                    options.Username ?? string.Empty,
                    options.Ssl,
                    options.ConnectTimeoutMilliseconds,
                    options.OperationTimeoutMilliseconds,
                    passwordHash);
            }
        }
    }
}
