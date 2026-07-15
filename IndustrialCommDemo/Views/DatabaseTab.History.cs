using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;

namespace IndustrialCommDemo.Views
{
    public partial class DatabaseTab
    {
        private async Task RefreshHistoryAsync(IIndustrialHistoryStore store, CancellationToken ct)
        {
            var lastId = 0L;
            var initialLoad = true;
            var errorLogged = false;

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var records = initialLoad
                            ? await store.ReadLatestAsync(MaxHistoryRowCount, ct).ConfigureAwait(false)
                            : await store.ReadAfterAsync(lastId, HistoryQueryBatchSize, ct).ConfigureAwait(false);

                        if (initialLoad)
                        {
                            initialLoad = false;
                            if (records.Count > 0) lastId = records.Max(r => r.Id);
                            _ctx.RunOnUi(() => ReplaceHistoryRows(records));
                        }
                        else if (records.Count > 0)
                        {
                            lastId = records[records.Count - 1].Id;
                            _ctx.RunOnUi(() => PrependHistoryRows(records));
                        }
                        else
                        {
                            _ctx.RunOnUi(UpdateHistoryStatus);
                        }
                        _ctx.RunOnUi(UpdateMetrics);

                        errorLogged = false;
                        if (records.Count < HistoryQueryBatchSize)
                            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        if (!errorLogged)
                        {
                            _ctx.DemoLogger.Error("数据库实时表格刷新失败，将自动重试。", ex);
                            errorLogged = true;
                        }
                        _ctx.RunOnUi(() => { HistoryStatusTextBlock.Text = "刷新失败，将自动重试：" + ex.Message; HistoryStatusTextBlock.Foreground = Brushes.IndianRed; });
                        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }

        private void ReplaceHistoryRows(System.Collections.Generic.IReadOnlyList<IndustrialDataRecord> records)
        {
            _historyRows.Clear();
            foreach (var r in records) _historyRows.Add(DatabaseHistoryDisplayRow.FromRecord(r));
            UpdateHistoryStatus();
        }

        private void PrependHistoryRows(System.Collections.Generic.IReadOnlyList<IndustrialDataRecord> records)
        {
            foreach (var r in records) _historyRows.Insert(0, DatabaseHistoryDisplayRow.FromRecord(r));
            while (_historyRows.Count > MaxHistoryRowCount) _historyRows.RemoveAt(_historyRows.Count - 1);
            UpdateHistoryStatus();
        }

        private void UpdateHistoryStatus()
        {
            HistoryStatusTextBlock.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "已显示 {0} 条 · {1}", _historyRows.Count, DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            HistoryStatusTextBlock.Foreground = Brushes.ForestGreen;
        }

        private void UpdateMetrics()
        {
            var snapshot = _recorder?.GetSnapshot();
            MetricsTextBlock.Text = snapshot == null
                ? "队列 0 · 已接收 0 · 已写入 0 · 丢弃 0 · 失败 0"
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "队列 {0} · 已接收 {1} · 已写入 {2} · 丢弃 {3} · 失败 {4} · 最近写入 {5}{6}",
                    snapshot.QueuedBatchCount, snapshot.AcceptedRecordCount, snapshot.WrittenRecordCount,
                    snapshot.DroppedRecordCount, snapshot.WriteFailureCount,
                    snapshot.LastSuccessfulWrite.HasValue ? snapshot.LastSuccessfulWrite.Value.ToLocalTime().ToString("HH:mm:ss") : "--",
                    string.IsNullOrWhiteSpace(snapshot.LastError) ? string.Empty : " · " + snapshot.LastError);
        }

        private void InitializeFilterComboBoxes()
        {
            AddFilterComboItem(QueryProtocolComboBox, "全部", null);
            foreach (ProtocolKind value in Enum.GetValues(typeof(ProtocolKind))) AddFilterComboItem(QueryProtocolComboBox, value.ToString(), value);
            AddFilterComboItem(QueryDataTypeComboBox, "全部", null);
            foreach (DataType value in Enum.GetValues(typeof(DataType))) AddFilterComboItem(QueryDataTypeComboBox, value.ToString(), value);
            AddFilterComboItem(QueryQualityComboBox, "全部", null);
            foreach (QualityStatus value in Enum.GetValues(typeof(QualityStatus))) AddFilterComboItem(QueryQualityComboBox, value.ToString(), value);
            QueryProtocolComboBox.SelectedIndex = QueryDataTypeComboBox.SelectedIndex = QueryQualityComboBox.SelectedIndex = 0;
        }

        private static void AddFilterComboItem(ComboBox combo, string text, object value)
        {
            combo.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        }

        private async void LatestRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try { await RefreshLatestAsync(); } catch (Exception ex) { _ctx.HandleError("刷新最新值失败。", ex, true); }
        }

        private async Task RefreshLatestAsync()
        {
            var store = RequireManagementStore();
            var filter = new HistoryQueryFilter { DeviceId = NormalizeFilterText(LatestDeviceComboBox.Text) };
            var records = await store.GetLatestValuesAsync(filter, 500, CancellationToken.None);
            _latestRows.Clear();
            foreach (var r in records) _latestRows.Add(DatabaseHistoryDisplayRow.FromRecord(r));
        }

        private async Task RefreshFilterOptionsAsync()
        {
            var store = _managementStore; if (store == null) return;
            var options = await store.GetFilterOptionsAsync(null, 200, CancellationToken.None);
            LatestDeviceComboBox.ItemsSource = new[] { string.Empty }.Concat(options.DeviceIds).ToArray();
            QueryDeviceComboBox.ItemsSource = new[] { string.Empty }.Concat(options.DeviceIds).ToArray();
        }
    }
}
