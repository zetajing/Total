using System;
using System.Collections.Generic;
using System.Linq;
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
        private ConnectionMultiplexer _connection;
        private IDatabase _database;

        public RedisClient(RedisClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null)
            : base(GetDeviceId(options), ProtocolKind.Redis, pollingScheduler ?? new PollingScheduler(logger),
                logger ?? NullIndustrialLogger.Instance, options.OperationTimeoutMilliseconds)
        {
            _options = options;
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
            CloseConnection();
            var configuration = new ConfigurationOptions
            {
                AbortOnConnectFail = true, ConnectTimeout = _options.ConnectTimeoutMilliseconds,
                SyncTimeout = _options.OperationTimeoutMilliseconds, AsyncTimeout = _options.OperationTimeoutMilliseconds,
                User = _options.Username, Password = _options.Password, Ssl = _options.Ssl
            };
            configuration.EndPoints.Add(_options.Host, _options.Port);
            try
            {
                _connection = await ConnectionMultiplexer.ConnectAsync(configuration).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _database = _connection.GetDatabase(_options.Database);
            }
            catch (OperationCanceledException) { CloseConnection(); throw; }
            catch (Exception ex) { CloseConnection(); throw new IndustrialConnectionException("Failed to connect Redis.", ex); }
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); CloseConnection(); return Task.CompletedTask; }

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
        protected override void OnOperationTimeout() { CloseConnection(); }
        protected override void DisposeCore() { CloseConnection(); }
        private void CloseConnection()
        {
            _database = null; var connection = Interlocked.Exchange(ref _connection, null);
            if (connection != null) connection.Dispose();
        }
    }
}
