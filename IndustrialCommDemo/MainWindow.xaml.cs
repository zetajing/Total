using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LogHelper;
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
        private DemoUiState _uiState;
        private bool _closeCleanupStarted;
        private bool _closeCleanupCompleted;

        public MainWindow()
        {
            InitializeComponent();

            // Create loggers
            _demoLogger = new AppLogger(Dispatcher, AppendLogBatch, "DEMO");
            _sdkLogger = new AppLogger(Dispatcher, AppendSdkLogBatch, "SDK");

            // Load persisted UI state
            _uiStateStore = new UiStateStore();
            _uiState = _uiStateStore.Load();

            // Create shared app context
            _ctx = new DemoAppContext(
                Dispatcher, _demoLogger, _sdkLogger, _uiStateStore, _uiState,
                SetHeaderStatus, () => this);

            // Initialize all tab UserControls
            ModbusControlTag.Initialize(_ctx);
            S7ControlTag.Initialize(_ctx);
            McControlTag.Initialize(_ctx);
            SocketControlTag.Initialize(_ctx);
            MesControlTag.Initialize(_ctx);
            DatabaseControlTag.Initialize(_ctx);
            NetworkControlTag.Initialize(_ctx);
            StorageControlTag.Initialize(_ctx);

            // Wire up network tab loaded event
            ProtocolTabControl.SelectionChanged += (s, e) =>
            {
                if (ProtocolTabControl.SelectedItem is TabItem tab && tab.Content == NetworkControlTag)
                    _ = NetworkControlTag.OnTabLoadedAsync();
            };

            SetHeaderStatus("就绪", Brushes.LightGreen);
            _demoLogger.Info("工业通讯演示已就绪。");
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
                SaveAllUiState();
                await ModbusControlTag.ResetClientAsync();
                await S7ControlTag.ResetClientAsync();
                await McControlTag.ResetClientAsync();
                await SocketControlTag.ResetAllAsync();
                await MesControlTag.ResetClientAsync();
                await DatabaseControlTag.StopRecorderAsync();

                if (_ctx.DatabaseRecorder != null) { _ctx.DatabaseRecorder.Dispose(); _ctx.DatabaseRecorder = null; }
            }
            catch (Exception ex)
            {
                try { _demoLogger.Error("程序关闭清理失败。", ex); } catch { }
            }
            finally
            {
                try { _demoLogger.Dispose(); } catch { }
                try { _sdkLogger.Dispose(); } catch { }
                _closeCleanupCompleted = true;
                _ = Dispatcher.BeginInvoke(new Action(() => Close()));
            }
        }

        private void SaveAllUiState()
        {
            ModbusControlTag.SaveState();
            S7ControlTag.SaveState();
            McControlTag.SaveState();
            SocketControlTag.SaveState();
            MesControlTag.SaveState();
            DatabaseControlTag.SaveState();
            _uiStateStore.Save(_uiState);
        }

        // ── Header status ──

        private void SetHeaderStatus(string text, Brush foreground)
        {
            HeaderStatusTextBlock.Text = text;
            HeaderStatusTextBlock.Foreground = foreground;
        }

        // ── Log panel ──

        private void AppendLogBatch(IReadOnlyList<string> messages)
        {
            foreach (var msg in messages) LogTextBox.AppendText(msg + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private void AppendSdkLogBatch(IReadOnlyList<string> messages)
        {
            foreach (var msg in messages) SdkLogTextBox.AppendText(msg + Environment.NewLine);
            SdkLogTextBox.ScrollToEnd();
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (LogTabControl.SelectedIndex == 1)
            {
                SdkLogTextBox.Clear();
                _sdkLogger.Info("SDK 日志已清空。");
            }
            else
            {
                LogTextBox.Clear();
                _demoLogger.Info("Demo 日志已清空。");
            }
        }
    }
}
