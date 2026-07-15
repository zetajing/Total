using System;

namespace IndustrialCommSdk.Storage.MySql
{
    /// <summary>MySQL 8.0+ 历史数据存储配置。</summary>
    public sealed class MySqlDataStoreOptions
    {
        /// <summary>MySQL 连接字符串。</summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// 历史表名。可使用 <c>table</c> 或 <c>database.table</c> 格式；
        /// 标识符只允许字母、数字和下划线。
        /// </summary>
        public string TableName { get; set; } = "IndustrialDataHistory";

        /// <summary>SQL 命令超时秒数。</summary>
        public int CommandTimeoutSeconds { get; set; } = 15;

        internal MySqlIndustrialDataStore.MySqlTableIdentifier ValidateAndGetTable()
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new InvalidOperationException("MySQL 连接字符串不能为空。");
            }

            if (CommandTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("SQL 命令超时必须大于 0 秒。");
            }

            return MySqlIndustrialDataStore.MySqlTableIdentifier.Parse(TableName);
        }
    }
}
