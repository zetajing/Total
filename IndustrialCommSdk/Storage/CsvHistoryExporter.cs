using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Storage
{
    /// <summary>
    /// 将历史记录导出为 CSV 文件。
    /// <para>
    /// CSV 格式兼容 Excel 直接打开，字段使用 UTF-8 BOM 编码，
    /// 包含表头行和逗号分隔的值，含逗号/引号的字段会自动加引号转义。
    /// </para>
    /// </summary>
    public static class CsvHistoryExporter
    {
        private static readonly string[] Headers =
        {
            "Id", "Protocol", "DeviceId", "Address", "DataType",
            "ValueText", "Quality", "Timestamp", "ErrorMessage",
        };

        /// <summary>
        /// 将一组历史记录异步写入 CSV 文件流。
        /// </summary>
        /// <param name="records">待导出的记录集合。</param>
        /// <param name="stream">目标可写流（不负责关闭）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static async Task ExportAsync(
            IEnumerable<IndustrialDataRecord> records,
            Stream stream,
            CancellationToken cancellationToken)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            await WriteBatchAsync(records, stream, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>向同一流分批追加记录；首批设置 includeHeader=true，后续批次设置 false。</summary>
        public static async Task WriteBatchAsync(IEnumerable<IndustrialDataRecord> records, Stream stream, bool includeHeader, CancellationToken cancellationToken)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (includeHeader)
            {
                var bom = new byte[] { 0xEF, 0xBB, 0xBF };
                await stream.WriteAsync(bom, 0, bom.Length, cancellationToken).ConfigureAwait(false);
            }

            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, true))
            {
                if (includeHeader) await writer.WriteLineAsync(string.Join(",", Headers)).ConfigureAwait(false);

                // 逐行写记录
                foreach (var record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                        record.Id,
                        Escape(record.Protocol.ToString()),
                        Escape(record.DeviceId ?? string.Empty),
                        Escape(record.Address ?? string.Empty),
                        Escape(record.DataType.ToString()),
                        Escape(record.ValueText ?? string.Empty),
                        Escape(record.Quality.ToString()),
                        Escape(record.Timestamp.ToString("o", CultureInfo.InvariantCulture)),
                        Escape(record.ErrorMessage ?? string.Empty));

                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }

                await writer.FlushAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 同步导出为 CSV 字节（适合小数据集或内存操作）。
        /// </summary>
        public static byte[] ExportToBytes(IEnumerable<IndustrialDataRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));

            using (var stream = new MemoryStream())
            {
                // 同步版用于测试和简单场景。
                ExportAsync(records, stream, CancellationToken.None).GetAwaiter().GetResult();
                return stream.ToArray();
            }
        }

        /// <summary>
        /// CSV 字段转义：包含逗号、引号或换行的字段用双引号包裹，内部双引号加倍。
        /// </summary>
        private static string Escape(string value)
        {
            if (value == null) return string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
