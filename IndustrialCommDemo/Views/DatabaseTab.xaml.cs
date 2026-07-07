using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;

namespace IndustrialCommDemo.Views
{
    public partial class DatabaseTab : UserControl
    {
        private const int MaxHistoryRowCount = 500;
        private const int HistoryQueryBatchSize = 200;

        private DemoAppContext _ctx;
        private BufferedIndustrialDataRecorder _recorder;
        private CancellationTokenSource _historyCts;
        private Task _historyTask;
        private SqlServerIndustrialDataStore _managementStore;
        private int _queryPage = 1;
        private long _queryTotal;

        private readonly ObservableCollection<DatabaseHistoryDisplayRow> _historyRows = new ObservableCollection<DatabaseHistoryDisplayRow>();
        private readonly ObservableCollection<DatabaseHistoryDisplayRow> _latestRows = new ObservableCollection<DatabaseHistoryDisplayRow>();
        private readonly ObservableCollection<DatabaseHistoryDisplayRow> _queryRows = new ObservableCollection<DatabaseHistoryDisplayRow>();

        public DatabaseTab()
        {
            InitializeComponent();
            HistoryDataGrid.ItemsSource = _historyRows;
            LatestDataGrid.ItemsSource = _latestRows;
            QueryDataGrid.ItemsSource = _queryRows;
            InitializeFilterComboBoxes();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            ApplySavedState();
        }

        public async Task StopRecorderAsync()
        {
            var recorder = _recorder;
            _recorder = null;
            _ctx.DatabaseRecordingEnabled = 0;

            var cts = _historyCts;
            var task = _historyTask;
            _historyCts = null;
            _historyTask = null;
            if (cts != null)
            {
                cts.Cancel();
                try { if (task != null) await task; } finally { cts.Dispose(); }
            }

            if (recorder == null) return;
            try { await recorder.StopAsync(CancellationToken.None); } finally { recorder.Dispose(); }
        }

        // ── Start / Stop ──

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StopRecorderAsync();
                if (_managementStore != null) { _managementStore.Dispose(); _managementStore = null; }

                var options = new SqlServerDataStoreOptions
                {
                    ConnectionString = ParseHelper.RequireText(ConnectionStringTextBox.Text, "数据库连接字符串"),
                    TableName = ParseHelper.RequireText(TableNameTextBox.Text, "历史表名"),
                    CommandTimeoutSeconds = 15,
                };
                var store = new SqlServerIndustrialDataStore(options);
                var recorder = new BufferedIndustrialDataRecorder(
                    store,
                    new BufferedDataRecorderOptions
                    {
                        BatchSize = 100,
                        QueueCapacity = 1000,
                        RetryCount = 2,
                    },
                    _ctx.SdkLogger);

                try
                {
                    await recorder.StartAsync(CancellationToken.None);
                }
                catch
                {
                    recorder.Dispose();
                    throw;
                }

                _recorder = recorder;
                _managementStore = new SqlServerIndustrialDataStore(options);
                _ctx.DatabaseRecorder = recorder;
                _ctx.DatabaseRecordingEnabled = 1;
                _historyRows.Clear();
                HistoryStatusTextBlock.Text = "正在读取最近的历史数据...";
                HistoryStatusTextBlock.Foreground = Brushes.SteelBlue;
                _historyCts = new CancellationTokenSource();
                _historyTask = RefreshHistoryAsync(store, _historyCts.Token);
                await RefreshFilterOptionsAsync();
                await RefreshLatestAsync();
                EnabledCheckBox.IsChecked = true;
                StatusTextBlock.Text = "已连接，正在后台记录读取和轮询数据。";
                StatusTextBlock.Foreground = Brushes.ForestGreen;
                _ctx.SetHeaderStatus("数据库记录已启用", Brushes.LightGreen);
                _ctx.DemoLogger.Info("SQL Server 历史数据记录已启用，数据表=" + options.TableName + "。");
            }
            catch (Exception ex)
            {
                EnabledCheckBox.IsChecked = false;
                _ctx.DatabaseRecordingEnabled = 0;
                StatusTextBlock.Text = "连接失败：" + ex.Message;
                StatusTextBlock.Foreground = Brushes.IndianRed;
                _ctx.HandleError("数据库连接失败。", ex, true);
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StopRecorderAsync();
                EnabledCheckBox.IsChecked = false;
                StatusTextBlock.Text = "已停止";
                StatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                HistoryStatusTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "刷新已停止，保留当前 {0} 条", _historyRows.Count);
                HistoryStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                UpdateMetrics();
                _ctx.DemoLogger.Info("SQL Server 历史数据记录已停止。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("停止数据库记录失败。", ex, true);
            }
        }

        // ── History refresh loop ──

        private async Task RefreshHistoryAsync(SqlServerIndustrialDataStore store, CancellationToken ct)
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
            HistoryStatusTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "已显示 {0} 条 · {1}", _historyRows.Count, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            HistoryStatusTextBlock.Foreground = Brushes.ForestGreen;
        }

        private void UpdateMetrics()
        {
            var snapshot = _recorder?.GetSnapshot();
            MetricsTextBlock.Text = snapshot == null
                ? "队列 0 · 已接收 0 · 已写入 0 · 丢弃 0 · 失败 0"
                : string.Format(CultureInfo.InvariantCulture, "队列 {0} · 已接收 {1} · 已写入 {2} · 丢弃 {3} · 失败 {4} · 最近写入 {5}{6}",
                    snapshot.QueuedBatchCount, snapshot.AcceptedRecordCount, snapshot.WrittenRecordCount,
                    snapshot.DroppedRecordCount, snapshot.WriteFailureCount,
                    snapshot.LastSuccessfulWrite.HasValue ? snapshot.LastSuccessfulWrite.Value.ToLocalTime().ToString("HH:mm:ss") : "--",
                    string.IsNullOrWhiteSpace(snapshot.LastError) ? string.Empty : " · " + snapshot.LastError);
        }

        // ── Filter combo init ──

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

        // ── Latest values ──

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

        // ── Query / Export / Delete / Cleanup ──

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

        // ── Helpers ──

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

        // ── State ──

        public void SaveState()
        {
            _ctx.UiState.Database.ConnectionString = ConnectionStringTextBox.Text;
            _ctx.UiState.Database.TableName = TableNameTextBox.Text;
            _ctx.UiState.Database.QueryDeviceId = QueryDeviceComboBox.Text;
            _ctx.UiState.Database.QueryAddress = QueryAddressTextBox.Text;
            _ctx.UiState.Database.AddressContains = QueryContainsCheckBox.IsChecked == true;
            _ctx.UiState.Database.FromTime = QueryFromTextBox.Text;
            _ctx.UiState.Database.ToTime = QueryToTextBox.Text;
            _ctx.UiState.Database.PageSize = GetPageSize();
            var retention = RetentionComboBox.SelectedItem as ComboBoxItem;
            _ctx.UiState.Database.RetentionDays = retention == null ? 30 : int.Parse(Convert.ToString(retention.Tag), CultureInfo.InvariantCulture);
            _ctx.UiState.Database.Protocol = GetComboTagText(QueryProtocolComboBox);
            _ctx.UiState.Database.DataType = GetComboTagText(QueryDataTypeComboBox);
            _ctx.UiState.Database.Quality = GetComboTagText(QueryQualityComboBox);
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Database ?? new Services.DatabaseUiState();
            ComboHelper.SetIfNotEmpty(ConnectionStringTextBox, state.ConnectionString);
            ComboHelper.SetIfNotEmpty(TableNameTextBox, state.TableName);
            QueryDeviceComboBox.Text = state.QueryDeviceId ?? string.Empty;
            QueryAddressTextBox.Text = state.QueryAddress ?? string.Empty;
            QueryContainsCheckBox.IsChecked = state.AddressContains;
            QueryFromTextBox.Text = state.FromTime ?? string.Empty;
            QueryToTextBox.Text = state.ToTime ?? string.Empty;
            ComboHelper.SelectComboBoxByContent(QueryPageSizeComboBox, state.PageSize <= 0 ? "100" : state.PageSize.ToString(CultureInfo.InvariantCulture));
            ComboHelper.SelectComboBoxByTag(QueryProtocolComboBox, state.Protocol);
            ComboHelper.SelectComboBoxByTag(QueryDataTypeComboBox, state.DataType);
            ComboHelper.SelectComboBoxByTag(QueryQualityComboBox, state.Quality);
            foreach (var item in RetentionComboBox.Items.OfType<ComboBoxItem>())
                if (Convert.ToString(item.Tag) == (state.RetentionDays <= 0 ? 30 : state.RetentionDays).ToString(CultureInfo.InvariantCulture))
                { RetentionComboBox.SelectedItem = item; break; }
            EnabledCheckBox.IsChecked = false;
            StatusTextBlock.Text = "未启用";
        }

        private static string GetComboTagText(ComboBox combo)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            return item?.Tag?.ToString();
        }
    }
}
