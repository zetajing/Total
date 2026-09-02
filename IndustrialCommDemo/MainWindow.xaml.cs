using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Storage;
using IndustrialCommDemo.Services;
using IndustrialCommDemo.Helpers;

namespace IndustrialCommDemo
{
    public partial class MainWindow : Window
    {
        private DemoAppContext _ctx;
        private AppLogger _demoLogger;
        private AppLogger _sdkLogger;
        private UiStateStore _uiStateStore;
        private IndustrialApplicationRuntime _runtime;
        private NetworkServicesRuntime _networkServices;
        private DemoUiState _uiState;
        private bool _logPanelVisible = true;
        private bool _closeCleanupStarted;
        private bool _closeCleanupCompleted;
        private const int MaxVisibleLogMessages = 2000;
        private readonly Queue<string> _visibleAppLogMessages = new Queue<string>();
        private readonly Queue<string> _visibleSdkLogMessages = new Queue<string>();

        public MainWindow()
        {
            InitializeComponent();
            LogDisplayHelper.WarningRaised += LogDisplayHelper_WarningRaised;

            // Create loggers
            _demoLogger = new AppLogger(Dispatcher, AppendLogBatch, "APP");
            _sdkLogger = new AppLogger(Dispatcher, AppendSdkLogBatch, "SDK");
            _runtime = new IndustrialApplicationRuntime(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "devices.json"),
                _sdkLogger);
            _networkServices = new NetworkServicesRuntime(_runtime, _sdkLogger);

            // Load persisted UI state
            _uiStateStore = new UiStateStore();
            _uiState = _uiStateStore.Load();

            // Create shared app context
            _ctx = new DemoAppContext(
                Dispatcher, _demoLogger, _sdkLogger, _runtime, _networkServices, _uiStateStore, _uiState,
                SetHeaderStatus, () => this);
            _runtime.ValuesReceived += (sender, args) => _ctx.QueueDatabaseValues(args.Client, args.RawValues);

            // Initialize all tab UserControls
            RuntimeControlTag.Initialize(_ctx);
            ModbusControlTag.Initialize(_ctx);
            S7ControlTag.Initialize(_ctx);
            Snap7ServerControlTag.Initialize(_ctx);
            McControlTag.Initialize(_ctx);
            OpcUaControlTag.Initialize(_ctx);
            JsonConfigControlTag.Initialize(_ctx);
            SocketControlTag.Initialize(_ctx);
            MesControlTag.Initialize(_ctx);
            DatabaseControlTag.Initialize(_ctx);
            NetworkControlTag.Initialize(_ctx);
            StorageControlTag.Initialize(_ctx);
            NetworkServicesControlTag.Initialize(_ctx);

            // 网卡页面进入时再刷新，避免程序启动就执行系统管理查询。
            MaintenanceTabControl.SelectionChanged += (s, e) =>
            {
                if (MaintenanceTabControl.SelectedItem is TabItem tab && tab.Content == NetworkControlTag)
                    _ = NetworkControlTag.OnTabLoadedAsync();
            };

            _demoLogger.Info("工业设备运行中心已就绪。");
            _ = NetworkServicesControlTag.StartAutoServicesAsync();
        }

        // ── Window cleanup ──

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (_closeCleanupCompleted) { base.OnClosing(e); return; }
            e.Cancel = true;
            if (_closeCleanupStarted) return;
            _closeCleanupStarted = true;
            IsEnabled = false;

            try
            {
                await RunCleanupStepAsync("保存界面状态", () =>
                {
                    SaveAllUiState();
                    return System.Threading.Tasks.Task.CompletedTask;
                });
                await RunCleanupStepAsync("关闭 Modbus 客户端", () => ModbusControlTag.ResetClientAsync());
                await RunCleanupStepAsync("关闭 Siemens S7 客户端", () => S7ControlTag.ResetClientAsync());
                await RunCleanupStepAsync("关闭 Snap7 虚拟 PLC", () => Snap7ServerControlTag.ResetSnap7ServerAsync());
                await RunCleanupStepAsync("关闭 Mitsubishi MC 客户端", () => McControlTag.ResetClientAsync());
                await RunCleanupStepAsync("关闭 OPC UA 客户端", () => OpcUaControlTag.ResetClientAsync());
                await RunCleanupStepAsync("关闭 Socket 调试连接", () => SocketControlTag.ResetAllAsync());
                await RunCleanupStepAsync("关闭 MES 客户端和接收器", () => MesControlTag.ResetAllAsync());
                await RunCleanupStepAsync("关闭网络服务", () => NetworkServicesControlTag.ResetAsync());
                await RunCleanupStepAsync("停止历史数据记录", () => DatabaseControlTag.StopRecorderAsync());
                await RunCleanupStepAsync("停止工业通信运行时", () => _runtime.StopAsync());
                await RunCleanupStepAsync("释放历史数据记录器", () =>
                {
                    var recorder = _ctx.DatabaseRecorder;
                    _ctx.DatabaseRecorder = null;
                    if (recorder != null) recorder.Dispose();
                    return System.Threading.Tasks.Task.CompletedTask;
                });
                await RunCleanupStepAsync("释放网络服务运行时", () =>
                {
                    _networkServices.Dispose();
                    return System.Threading.Tasks.Task.CompletedTask;
                });
                await RunCleanupStepAsync("释放工业通信运行时", () =>
                {
                    _runtime.Dispose();
                    return System.Threading.Tasks.Task.CompletedTask;
                });
            }
            finally
            {
                try { _demoLogger.Dispose(); } catch { }
                try { _sdkLogger.Dispose(); } catch { }
                _closeCleanupCompleted = true;
                _ = Dispatcher.BeginInvoke(new Action(() => Close()));
            }
        }

        private async System.Threading.Tasks.Task RunCleanupStepAsync(
            string stepName,
            Func<System.Threading.Tasks.Task> cleanup)
        {
            try
            {
                await cleanup();
            }
            catch (Exception ex)
            {
                try { _demoLogger.Error(stepName + "失败。", ex); } catch { }
            }
        }

        private void SaveAllUiState()
        {
            ModbusControlTag.SaveState();
            S7ControlTag.SaveState();
            McControlTag.SaveState();
            OpcUaControlTag.SaveState();
            SocketControlTag.SaveState();
            MesControlTag.SaveState();
            DatabaseControlTag.SaveState();
            NetworkServicesControlTag.SaveState();
            _uiStateStore.Save(_uiState);
        }

        // ── Header status compatibility callback ──

        private void SetHeaderStatus(string text, Brush foreground)
        {
            // The visual header was intentionally removed. Keep this callback so
            // protocol pages and shared error handling do not need a behavior change.
        }

        // ── Log panel ──

        private void AppendLogBatch(IReadOnlyList<string> messages)
        {
            AppendLogBatch(LogTextBox, _visibleAppLogMessages, messages);
        }

        private void AppendSdkLogBatch(IReadOnlyList<string> messages)
        {
            AppendLogBatch(SdkLogTextBox, _visibleSdkLogMessages, messages);
        }

        private static void AppendLogBatch(
            TextBox textBox,
            Queue<string> visibleMessages,
            IReadOnlyList<string> messages)
        {
            var removedLength = 0;
            foreach (var message in messages)
            {
                var line = (message ?? string.Empty) + Environment.NewLine;
                visibleMessages.Enqueue(line);
                textBox.AppendText(line);
            }

            while (visibleMessages.Count > MaxVisibleLogMessages)
            {
                removedLength += visibleMessages.Dequeue().Length;
            }

            if (removedLength > 0)
            {
                var text = textBox.Text ?? string.Empty;
                textBox.Text = removedLength >= text.Length ? string.Empty : text.Substring(removedLength);
            }

            textBox.ScrollToEnd();
        }

        private void LogDisplayHelper_WarningRaised(object sender, LogDisplayWarningEventArgs e)
        {
            try
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    var entry = new LogDisplayEntry(
                        DateTimeOffset.Now,
                        "APP",
                        LogDisplayLevel.Warn,
                        "日志系统告警：" + e.Message,
                        e.Exception);
                    AppendLogBatch(new[] { entry.FormatText() });
                }));
            }
            catch
            {
                // 窗口关闭期间 Dispatcher 可能已经停止，不能让日志告警影响退出流程。
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (LogTabControl.SelectedIndex == 1)
            {
                SdkLogTextBox.Clear();
                _visibleSdkLogMessages.Clear();
                _sdkLogger.Info("SDK 日志已清空。");
            }
            else
            {
                LogTextBox.Clear();
                _visibleAppLogMessages.Clear();
                _demoLogger.Info("Demo 日志已清空。");
            }
        }

        private void ToggleLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logPanelVisible = !_logPanelVisible;
            LogPanelRow.Height = _logPanelVisible ? new GridLength(150) : GridLength.Auto;
            LogPanelSplitter.Visibility = _logPanelVisible ? Visibility.Visible : Visibility.Collapsed;
            LogTabControl.Visibility = _logPanelVisible ? Visibility.Visible : Visibility.Collapsed;
            ToggleLogButton.Content = _logPanelVisible ? "隐藏日志" : "显示日志";
        }
    }
}
