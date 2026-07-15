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
using IndustrialCommSdk.Storage.MySql;

namespace IndustrialCommDemo.Views
{
    /// <summary>演示采集数据缓冲写库、历史查询和 CSV 导出。</summary>
    public partial class DatabaseTab : UserControl
    {
        private const int MaxHistoryRowCount = 500;
        private const int HistoryQueryBatchSize = 200;
        private const string SqlServerProvider = "sqlserver";
        private const string MySqlProvider = "mysql";
        private const string DefaultSqlServerConnectionString = "Server=localhost;Database=UpperComputerDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        private const string DefaultSqlServerTableName = "dbo.IndustrialDataHistory";
        private const string DefaultMySqlConnectionString = "Server=localhost;Port=3306;Database=upper_computer;User ID=root;Password=;SslMode=Preferred;DateTimeKind=Utc;";
        private const string DefaultMySqlTableName = "IndustrialDataHistory";

        private DemoAppContext _ctx;
        private BufferedIndustrialDataRecorder _recorder;
        private CancellationTokenSource _historyCts;
        private Task _historyTask;
        private IIndustrialHistoryStore _managementStore;
        private string _selectedProvider = SqlServerProvider;
        private string _sqlServerConnectionString = DefaultSqlServerConnectionString;
        private string _sqlServerTableName = DefaultSqlServerTableName;
        private string _mySqlConnectionString = DefaultMySqlConnectionString;
        private string _mySqlTableName = DefaultMySqlTableName;
        private bool _changingProvider;
        private int _queryPage = 1;
        private long _queryTotal;

        private readonly SemaphoreSlim _databaseOperationGate = new SemaphoreSlim(1, 1);
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
            await _databaseOperationGate.WaitAsync();
            try
            {
                await StopRecorderCoreAsync();
            }
            finally
            {
                _databaseOperationGate.Release();
            }
        }

        private async Task StopRecorderCoreAsync()
        {
            var recorder = _recorder;
            _recorder = null;
            var managementStore = _managementStore;
            _managementStore = null;
            _ctx.DatabaseRecordingEnabled = 0;
            if (ReferenceEquals(_ctx.DatabaseRecorder, recorder)) _ctx.DatabaseRecorder = null;

            var cts = _historyCts;
            var task = _historyTask;
            _historyCts = null;
            _historyTask = null;
            try
            {
                if (cts != null)
                {
                    cts.Cancel();
                    try { if (task != null) await task; } finally { cts.Dispose(); }
                }
            }
            finally
            {
                try
                {
                    if (recorder != null) await recorder.StopAsync(CancellationToken.None);
                }
                finally
                {
                    if (recorder != null) recorder.Dispose();
                    if (managementStore != null) managementStore.Dispose();
                }
            }
        }

        // ── Start / Stop ──

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await _databaseOperationGate.WaitAsync(0)) return;
            SetDatabaseOperationBusy(true);
            try
            {
                await StopRecorderCoreAsync();
                var settings = CaptureSelectedStoreSettings();
                var store = CreateStore(settings);
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
                _managementStore = CreateStore(settings);
                _ctx.DatabaseRecorder = recorder;
                _ctx.DatabaseRecordingEnabled = 1;
                _historyRows.Clear();
                HistoryStatusTextBlock.Text = "正在读取最近的历史数据...";
                HistoryStatusTextBlock.Foreground = Brushes.SteelBlue;
                _historyCts = new CancellationTokenSource();
                _historyTask = RefreshHistoryAsync(_managementStore, _historyCts.Token);
                await RefreshFilterOptionsAsync();
                await RefreshLatestAsync();
                EnabledCheckBox.IsChecked = true;
                StatusTextBlock.Text = "已连接，正在后台记录读取和轮询数据。";
                StatusTextBlock.Foreground = Brushes.ForestGreen;
                _ctx.SetHeaderStatus("数据库记录已启用", Brushes.LightGreen);
                _ctx.DemoLogger.Info(settings.DisplayName + " 历史数据记录已启用，数据表=" + settings.TableName + "。");
            }
            catch (Exception ex)
            {
                try { await StopRecorderCoreAsync(); }
                catch (Exception cleanupEx) { _ctx.DemoLogger.Error("数据库启动失败后的清理也失败。", cleanupEx); }
                EnabledCheckBox.IsChecked = false;
                _ctx.DatabaseRecordingEnabled = 0;
                StatusTextBlock.Text = "连接失败：" + ex.Message;
                StatusTextBlock.Foreground = Brushes.IndianRed;
                _ctx.HandleError("数据库连接失败。", ex, true);
            }
            finally
            {
                _databaseOperationGate.Release();
                SetDatabaseOperationBusy(false);
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await _databaseOperationGate.WaitAsync(0)) return;
            SetDatabaseOperationBusy(true);
            try
            {
                await StopRecorderCoreAsync();
                EnabledCheckBox.IsChecked = false;
                StatusTextBlock.Text = "已停止";
                StatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                HistoryStatusTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "刷新已停止，保留当前 {0} 条", _historyRows.Count);
                HistoryStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                UpdateMetrics();
                _ctx.DemoLogger.Info("数据库历史数据记录已停止。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("停止数据库记录失败。", ex, true);
            }
            finally
            {
                _databaseOperationGate.Release();
                SetDatabaseOperationBusy(false);
            }
        }

        private DatabaseStoreSettings CaptureSelectedStoreSettings()
        {
            SaveCurrentProviderDraft();
            return new DatabaseStoreSettings
            {
                Provider = _selectedProvider,
                ConnectionString = ParseHelper.RequireText(ConnectionStringTextBox.Text, "数据库连接字符串"),
                TableName = ParseHelper.RequireText(TableNameTextBox.Text, "历史表名"),
            };
        }

        private static IIndustrialHistoryStore CreateStore(DatabaseStoreSettings settings)
        {
            if (settings.Provider == MySqlProvider)
            {
                return new MySqlIndustrialDataStore(new MySqlDataStoreOptions
                {
                    ConnectionString = settings.ConnectionString,
                    TableName = settings.TableName,
                    CommandTimeoutSeconds = 15,
                });
            }

            return new SqlServerIndustrialDataStore(new SqlServerDataStoreOptions
            {
                ConnectionString = settings.ConnectionString,
                TableName = settings.TableName,
                CommandTimeoutSeconds = 15,
            });
        }

        private void SetDatabaseOperationBusy(bool busy)
        {
            StartButton.IsEnabled = !busy;
            StopButton.IsEnabled = !busy;
            var configurationEditable = !busy && _recorder == null;
            DatabaseProviderComboBox.IsEnabled = configurationEditable;
            ConnectionStringTextBox.IsReadOnly = !configurationEditable;
            TableNameTextBox.IsReadOnly = !configurationEditable;
        }

        private sealed class DatabaseStoreSettings
        {
            public string Provider { get; set; }
            public string ConnectionString { get; set; }
            public string TableName { get; set; }
            public string DisplayName => Provider == MySqlProvider ? "MySQL" : "SQL Server";
        }
    }
}
