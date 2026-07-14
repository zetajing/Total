using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;

namespace IndustrialCommDemo.Views
{
    /// <summary>演示采集数据缓冲写库、历史查询和 CSV 导出。</summary>
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

    }
}
