using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Storage
{
    /// <summary>数据库历史记录中的一条设备采集值。</summary>
    public sealed class IndustrialDataRecord
    {
        /// <summary>通信协议。</summary>
        public ProtocolKind Protocol { get; set; }
        /// <summary>设备标识。</summary>
        public string DeviceId { get; set; }
        /// <summary>协议地址。</summary>
        public string Address { get; set; }
        /// <summary>数据类型。</summary>
        public DataType DataType { get; set; }
        /// <summary>使用不受区域设置影响的格式序列化后的值。</summary>
        public string ValueText { get; set; }
        /// <summary>设备返回的原始字节。</summary>
        public byte[] RawData { get; set; }
        /// <summary>数据质量。</summary>
        public QualityStatus Quality { get; set; }
        /// <summary>采集时间。</summary>
        public DateTimeOffset Timestamp { get; set; }
        /// <summary>读取失败时的错误信息。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>把 SDK 读取结果转换为可持久化的历史记录。</summary>
        public static IndustrialDataRecord FromDataValue(ProtocolKind protocol, string deviceId, DataValue value)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentNullException(nameof(deviceId));
            if (value == null) throw new ArgumentNullException(nameof(value));

            return new IndustrialDataRecord
            {
                Protocol = protocol,
                DeviceId = deviceId,
                Address = value.Address,
                DataType = value.DataType,
                ValueText = FormatValue(value.Value),
                RawData = value.RawData == null ? null : (byte[])value.RawData.Clone(),
                Quality = value.Quality,
                Timestamp = value.Timestamp,
                ErrorMessage = value.ErrorMessage,
            };
        }

        private static string FormatValue(object value)
        {
            if (value == null) return null;
            var bytes = value as byte[];
            if (bytes != null) return BitConverter.ToString(bytes);
            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>工业采集数据存储接口。实现不得参与 PLC 实时控制。</summary>
    public interface IIndustrialDataStore : IDisposable
    {
        /// <summary>检查连接并创建存储所需的数据表。</summary>
        Task InitializeAsync(CancellationToken cancellationToken);

        /// <summary>批量写入历史记录。</summary>
        Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken);
    }

    /// <summary>SQL Server 历史数据存储配置。</summary>
    public sealed class SqlServerDataStoreOptions
    {
        /// <summary>SQL Server 连接字符串。</summary>
        public string ConnectionString { get; set; }

        /// <summary>历史表名，格式为 schema.table。</summary>
        public string TableName { get; set; } = "dbo.IndustrialDataHistory";

        /// <summary>SQL 命令超时秒数。</summary>
        public int CommandTimeoutSeconds { get; set; } = 15;

        internal SqlServerIndustrialDataStore.SqlTableIdentifier ValidateAndGetTable()
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new InvalidOperationException("SQL Server 连接字符串不能为空。");
            }

            if (CommandTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("SQL 命令超时必须大于 0 秒。");
            }

            return SqlServerIndustrialDataStore.SqlTableIdentifier.Parse(TableName);
        }
    }

    /// <summary>使用 ADO.NET 将工业采集值写入 SQL Server。</summary>
    public sealed class SqlServerIndustrialDataStore : IIndustrialDataStore
    {
        private readonly SqlServerDataStoreOptions _options;
        private readonly SqlTableIdentifier _table;

        /// <summary>创建 SQL Server 存储。</summary>
        public SqlServerIndustrialDataStore(SqlServerDataStoreOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _table = options.ValidateAndGetTable();
        }

        /// <inheritdoc />
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"IF OBJECT_ID(@ObjectName, N'U') IS NULL
BEGIN
    CREATE TABLE {0}
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT {1} PRIMARY KEY,
        [Protocol] NVARCHAR(32) NOT NULL,
        [DeviceId] NVARCHAR(128) NOT NULL,
        [Address] NVARCHAR(128) NOT NULL,
        [DataType] NVARCHAR(32) NOT NULL,
        [ValueText] NVARCHAR(MAX) NULL,
        [RawData] VARBINARY(MAX) NULL,
        [Quality] NVARCHAR(32) NOT NULL,
        [Timestamp] DATETIMEOFFSET(7) NOT NULL,
        [ErrorMessage] NVARCHAR(2048) NULL
    );
    CREATE INDEX {2} ON {0} ([Timestamp] DESC);
    CREATE INDEX {3} ON {0} ([DeviceId], [Address], [Timestamp] DESC);
END;",
                _table.QuotedName,
                _table.QuoteGeneratedName("PK"),
                _table.QuoteGeneratedName("IX_Timestamp"),
                _table.QuoteGeneratedName("IX_Device_Address_Timestamp"));

            using (var connection = new SqlConnection(_options.ConnectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                command.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 257).Value = _table.UnquotedName;
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0) return;

            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"INSERT INTO {0}
([Protocol], [DeviceId], [Address], [DataType], [ValueText], [RawData], [Quality], [Timestamp], [ErrorMessage])
VALUES
(@Protocol, @DeviceId, @Address, @DataType, @ValueText, @RawData, @Quality, @Timestamp, @ErrorMessage);",
                _table.QuotedName);

            using (var connection = new SqlConnection(_options.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var transaction = connection.BeginTransaction())
                using (var command = CreateInsertCommand(connection, transaction, sql))
                {
                    try
                    {
                        foreach (var record in records)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SetParameterValues(command, record);
                            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private SqlCommand CreateInsertCommand(SqlConnection connection, SqlTransaction transaction, string sql)
        {
            var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds,
            };
            command.Parameters.Add("@Protocol", SqlDbType.NVarChar, 32);
            command.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 128);
            command.Parameters.Add("@Address", SqlDbType.NVarChar, 128);
            command.Parameters.Add("@DataType", SqlDbType.NVarChar, 32);
            command.Parameters.Add("@ValueText", SqlDbType.NVarChar, -1);
            command.Parameters.Add("@RawData", SqlDbType.VarBinary, -1);
            command.Parameters.Add("@Quality", SqlDbType.NVarChar, 32);
            command.Parameters.Add("@Timestamp", SqlDbType.DateTimeOffset);
            command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, 2048);
            return command;
        }

        private static void SetParameterValues(SqlCommand command, IndustrialDataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            command.Parameters["@Protocol"].Value = record.Protocol.ToString();
            command.Parameters["@DeviceId"].Value = record.DeviceId ?? string.Empty;
            command.Parameters["@Address"].Value = record.Address ?? string.Empty;
            command.Parameters["@DataType"].Value = record.DataType.ToString();
            command.Parameters["@ValueText"].Value = (object)record.ValueText ?? DBNull.Value;
            command.Parameters["@RawData"].Value = (object)record.RawData ?? DBNull.Value;
            command.Parameters["@Quality"].Value = record.Quality.ToString();
            command.Parameters["@Timestamp"].Value = record.Timestamp;
            command.Parameters["@ErrorMessage"].Value = (object)record.ErrorMessage ?? DBNull.Value;
        }

        internal sealed class SqlTableIdentifier
        {
            private SqlTableIdentifier(string schema, string table)
            {
                Schema = schema;
                Table = table;
            }

            public string Schema { get; private set; }
            public string Table { get; private set; }
            public string UnquotedName { get { return Schema + "." + Table; } }
            public string QuotedName { get { return Quote(Schema) + "." + Quote(Table); } }

            public string QuoteGeneratedName(string prefix)
            {
                var value = prefix + "_" + Table;
                return Quote(value.Length <= 128 ? value : value.Substring(0, 128));
            }

            public static SqlTableIdentifier Parse(string value)
            {
                var parts = (value ?? string.Empty).Split('.');
                if (parts.Length != 2 || !IsValidPart(parts[0]) || !IsValidPart(parts[1]))
                {
                    throw new InvalidOperationException("历史表名必须使用 schema.table 格式，且只能包含字母、数字和下划线。");
                }

                return new SqlTableIdentifier(parts[0], parts[1]);
            }

            private static bool IsValidPart(string value)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || (!char.IsLetter(value[0]) && value[0] != '_'))
                {
                    return false;
                }

                return value.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
            }

            private static string Quote(string value)
            {
                return "[" + value.Replace("]", "]]" ) + "]";
            }
        }
    }

    /// <summary>后台批量写库配置。</summary>
    public sealed class BufferedDataRecorderOptions
    {
        /// <summary>单次数据库事务最多写入的记录数。</summary>
        public int BatchSize { get; set; } = 100;
        /// <summary>内存中最多排队的读取批次数。</summary>
        public int QueueCapacity { get; set; } = 1000;
        /// <summary>单批失败后的重试次数。</summary>
        public int RetryCount { get; set; } = 2;
    }

    /// <summary>
    /// 把通信线程产生的读取结果放入有界队列，再由单独后台任务批量写库。
    /// 数据库变慢或短暂断线时不会阻塞设备通信；队列满时会丢弃最新批次并记录警告。
    /// </summary>
    public sealed class BufferedIndustrialDataRecorder : IDisposable
    {
        private readonly IIndustrialDataStore _store;
        private readonly BufferedDataRecorderOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly BlockingCollection<IReadOnlyCollection<IndustrialDataRecord>> _queue;
        private readonly CancellationTokenSource _stopSource = new CancellationTokenSource();
        private Task _worker;
        private int _started;

        /// <summary>创建后台记录器。</summary>
        public BufferedIndustrialDataRecorder(
            IIndustrialDataStore store,
            BufferedDataRecorderOptions options = null,
            IIndustrialLogger logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _options = options ?? new BufferedDataRecorderOptions();
            if (_options.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options), "BatchSize 必须大于 0。");
            if (_options.QueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(options), "QueueCapacity 必须大于 0。");
            if (_options.RetryCount < 0) throw new ArgumentOutOfRangeException(nameof(options), "RetryCount 不能小于 0。");
            _logger = logger ?? NullIndustrialLogger.Instance;
            _queue = new BlockingCollection<IReadOnlyCollection<IndustrialDataRecord>>(_options.QueueCapacity);
        }

        /// <summary>检查数据库、创建表并启动后台写入任务。</summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            try
            {
                await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _worker = Task.Run(() => ProcessQueueAsync(_stopSource.Token));
            }
            catch
            {
                Interlocked.Exchange(ref _started, 0);
                throw;
            }
        }

        /// <summary>非阻塞地把一批读取结果加入写库队列。</summary>
        public bool TryRecord(ProtocolKind protocol, string deviceId, IReadOnlyCollection<DataValue> values)
        {
            if (Volatile.Read(ref _started) == 0 || values == null || values.Count == 0 || _queue.IsAddingCompleted)
            {
                return false;
            }

            var records = values.Select(value => IndustrialDataRecord.FromDataValue(protocol, deviceId, value)).ToArray();
            if (_queue.TryAdd(records)) return true;
            _logger.Warn("数据库写入队列已满，本批采集数据已丢弃。");
            return false;
        }

        /// <summary>停止接收新数据，并等待队列中的记录写完。</summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _started, 0) == 0) return;
            _queue.CompleteAdding();
            if (_worker != null)
            {
                var completed = await Task.WhenAny(_worker, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
                if (completed != _worker) cancellationToken.ThrowIfCancellationRequested();
                await _worker.ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Volatile.Read(ref _started) != 0)
            {
                _queue.CompleteAdding();
                try { if (_worker != null) _worker.GetAwaiter().GetResult(); }
                catch (Exception ex) { _logger.Error("停止数据库记录器失败。", ex); }
            }

            _stopSource.Cancel();
            _queue.Dispose();
            _stopSource.Dispose();
            _store.Dispose();
            Interlocked.Exchange(ref _started, 0);
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            var batch = new List<IndustrialDataRecord>(_options.BatchSize);
            while (!_queue.IsCompleted)
            {
                IReadOnlyCollection<IndustrialDataRecord> first;
                try
                {
                    first = _queue.Take(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                batch.Clear();
                batch.AddRange(first);
                IReadOnlyCollection<IndustrialDataRecord> next;
                while (batch.Count < _options.BatchSize && _queue.TryTake(out next))
                {
                    batch.AddRange(next);
                }

                await WriteWithRetryAsync(batch.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task WriteWithRetryAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await _store.WriteAsync(records, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && attempt < _options.RetryCount)
                {
                    _logger.Warn(string.Format(CultureInfo.InvariantCulture, "数据库写入失败，准备第 {0} 次重试：{1}", attempt + 1, ex.Message));
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.Error("数据库写入失败，本批数据已放弃。", ex);
                    return;
                }
            }
        }
    }
}
