using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;

namespace IndustrialCommDemo.Views
{
    public partial class DatabaseTab
    {
        private async void QueryButton_Click(object sender, RoutedEventArgs e) { _queryPage = 1; await ExecuteQueryAsync(true); }
        private async void PreviousPageButton_Click(object sender, RoutedEventArgs e) { if (_queryPage > 1) { _queryPage--; await ExecuteQueryAsync(false); } }
        private async void NextPageButton_Click(object sender, RoutedEventArgs e) { if (_queryPage * GetPageSize() < _queryTotal) { _queryPage++; await ExecuteQueryAsync(false); } }

        private async Task ExecuteQueryAsync(bool refreshOptions)
        {
            try
            {
                var store = RequireManagementStore();
                var filter = BuildFilter();
                var pageSize = GetPageSize();
                var page = await store.QueryPageAsync(new HistoryPageRequest { Filter = filter, PageNumber = _queryPage, PageSize = pageSize }, CancellationToken.None);
                var summary = await store.GetSummaryAsync(filter, CancellationToken.None);
                _queryRows.Clear();
                foreach (var r in page.Records) _queryRows.Add(DatabaseHistoryDisplayRow.FromRecord(r));
                _queryTotal = page.TotalCount;
                QueryStatusTextBlock.Text = string.Format(CultureInfo.InvariantCulture,
                    "第 {0}/{1} 页 · 共 {2} 条 · Good {3} · Bad {4} · Stale {5} · Unknown {6} · {7} ～ {8}",
                    page.PageNumber, Math.Max(1, (page.TotalCount + pageSize - 1) / pageSize), page.TotalCount,
                    summary.GoodCount, summary.BadCount, summary.StaleCount, summary.UnknownCount,
                    summary.EarliestTimestamp.HasValue ? summary.EarliestTimestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "--",
                    summary.LatestTimestamp.HasValue ? summary.LatestTimestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "--");
                if (refreshOptions) await RefreshFilterOptionsAsync();
            }
            catch (Exception ex) { _ctx.HandleError("历史数据查询失败。", ex, true); }
        }

        private void QueryResetButton_Click(object sender, RoutedEventArgs e)
        {
            QueryDeviceComboBox.Text = QueryAddressTextBox.Text = QueryFromTextBox.Text = QueryToTextBox.Text = string.Empty;
            QueryContainsCheckBox.IsChecked = false;
            QueryProtocolComboBox.SelectedIndex = QueryDataTypeComboBox.SelectedIndex = QueryQualityComboBox.SelectedIndex = 0;
            _queryPage = 1;
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = "industrial-history-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv" };
                if (dialog.ShowDialog() != true) return;
                var store = RequireManagementStore();
                var filter = BuildFilter();
                if (!filter.ToTime.HasValue) filter.ToTime = DateTimeOffset.UtcNow;
                const int limit = 50000;
                var exported = 0; var page = 1;
                var summary = await store.GetSummaryAsync(filter, CancellationToken.None);
                var truncated = summary.TotalCount > limit;
                using (var stream = File.Create(dialog.FileName))
                {
                    while (exported < limit)
                    {
                        var result = await store.QueryPageAsync(new HistoryPageRequest { Filter = filter, PageNumber = page++, PageSize = 1000 }, CancellationToken.None);
                        await CsvHistoryExporter.WriteBatchAsync(result.Records, stream, exported == 0, CancellationToken.None);
                        exported += result.Records.Count;
                        if (result.Records.Count < 1000) break;
                    }
                }
                MessageBox.Show(string.Format("已导出 {0} 条。{1}", exported, truncated ? "结果超过 50,000 条，已截断。" : string.Empty), "导出完成");
            }
            catch (Exception ex) { _ctx.HandleError("导出历史数据失败。", ex, true); }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var store = RequireManagementStore();
                var filter = BuildFilter();
                EnsureDestructiveFilter(filter);
                var summary = await store.GetSummaryAsync(filter, CancellationToken.None);
                if (summary.TotalCount == 0) { MessageBox.Show("没有匹配记录。"); return; }
                if (MessageBox.Show("确定删除匹配的 " + summary.TotalCount + " 条记录？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var removed = await store.DeleteAsync(filter, CancellationToken.None);
                MessageBox.Show("已删除 " + removed + " 条记录。");
                await ExecuteQueryAsync(true);
                await RefreshLatestAsync();
            }
            catch (Exception ex) { _ctx.HandleError("删除历史数据失败。", ex, true); }
        }

        private async void CleanupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = RetentionComboBox.SelectedItem as ComboBoxItem;
                var days = int.Parse(Convert.ToString(item.Tag), CultureInfo.InvariantCulture);
                var filter = new HistoryQueryFilter { ToTime = DateTimeOffset.Now.AddDays(-days) };
                var store = RequireManagementStore();
                var summary = await store.GetSummaryAsync(filter, CancellationToken.None);
                if (summary.TotalCount == 0) { MessageBox.Show("没有需要清理的数据。"); return; }
                if (MessageBox.Show(string.Format("确定删除 {0} 天以前的 {1} 条记录？", days, summary.TotalCount), "确认清理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var removed = await store.DeleteAsync(filter, CancellationToken.None);
                MessageBox.Show("已清理 " + removed + " 条记录。");
                await ExecuteQueryAsync(true);
            }
            catch (Exception ex) { _ctx.HandleError("清理历史数据失败。", ex, true); }
        }

        private SqlServerIndustrialDataStore RequireManagementStore()
        {
            if (_managementStore == null) throw new InvalidOperationException("请先测试并启用数据库连接。");
            return _managementStore;
        }

        private HistoryQueryFilter BuildFilter()
        {
            return new HistoryQueryFilter
            {
                DeviceId = NormalizeFilterText(QueryDeviceComboBox.Text),
                Address = NormalizeFilterText(QueryAddressTextBox.Text),
                AddressMatchMode = QueryContainsCheckBox.IsChecked == true ? HistoryAddressMatchMode.Contains : HistoryAddressMatchMode.Exact,
                Protocol = GetFilterValue<ProtocolKind>(QueryProtocolComboBox),
                DataType = GetFilterValue<DataType>(QueryDataTypeComboBox),
                Quality = GetFilterValue<QualityStatus>(QueryQualityComboBox),
                FromTime = ParseOptionalTime(QueryFromTextBox.Text),
                ToTime = ParseOptionalTime(QueryToTextBox.Text),
            };
        }

        private static T? GetFilterValue<T>(ComboBox combo) where T : struct
        {
            var item = combo.SelectedItem as ComboBoxItem;
            return item != null && item.Tag is T ? (T?)item.Tag : null;
        }

        private static string NormalizeFilterText(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        private static DateTimeOffset? ParseOptionalTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            DateTimeOffset value;
            if (!DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value))
                throw new InvalidOperationException("时间格式无效，请使用 yyyy-MM-dd HH:mm:ss。");
            return value;
        }

        private int GetPageSize()
        {
            var item = QueryPageSizeComboBox.SelectedItem as ComboBoxItem;
            return int.Parse(Convert.ToString(item.Content), CultureInfo.InvariantCulture);
        }

        private static void EnsureDestructiveFilter(HistoryQueryFilter filter)
        {
            if (string.IsNullOrWhiteSpace(filter.DeviceId) && string.IsNullOrWhiteSpace(filter.Address) &&
                !filter.Protocol.HasValue && !filter.DataType.HasValue && !filter.Quality.HasValue &&
                !filter.FromTime.HasValue && !filter.ToTime.HasValue)
                throw new InvalidOperationException("删除操作必须至少设置一个筛选条件。");
        }
    }
}
