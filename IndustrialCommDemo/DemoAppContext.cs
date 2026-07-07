using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IndustrialCommSdk.Storage;
using IndustrialCommDemo.Services;

namespace IndustrialCommDemo
{
    /// <summary>
    /// Shared application context that provides cross-cutting services to all tabs.
    /// Created once by MainWindow and passed to each UserControl.
    /// </summary>
    public sealed class DemoAppContext
    {
        public DemoAppContext(
            Dispatcher dispatcher,
            AppLogger demoLogger,
            AppLogger sdkLogger,
            UiStateStore uiStateStore,
            DemoUiState uiState,
            Action<string, Brush> setHeaderStatus,
            Func<Window> getWindow)
        {
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            DemoLogger = demoLogger ?? throw new ArgumentNullException(nameof(demoLogger));
            SdkLogger = sdkLogger ?? throw new ArgumentNullException(nameof(sdkLogger));
            UiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
            UiState = uiState ?? throw new ArgumentNullException(nameof(uiState));
            SetHeaderStatus = setHeaderStatus ?? throw new ArgumentNullException(nameof(setHeaderStatus));
            _getWindow = getWindow ?? throw new ArgumentNullException(nameof(getWindow));
        }

        public Dispatcher Dispatcher { get; }
        public AppLogger DemoLogger { get; }
        public AppLogger SdkLogger { get; }
        public UiStateStore UiStateStore { get; }
        public DemoUiState UiState { get; }
        public Action<string, Brush> SetHeaderStatus { get; }

        /// <summary>
        /// Shared database recorder. Set by the Database tab when user enables recording.
        /// Protocol tabs call <see cref="QueueDatabaseValues"/> to push readings into the background queue.
        /// </summary>
        public BufferedIndustrialDataRecorder DatabaseRecorder { get; set; }

        /// <summary>
        /// Shared thread-safe flag indicating whether database recording is enabled.
        /// Protocol tabs check this before enqueuing values.
        /// </summary>
        public int DatabaseRecordingEnabled
        {
            get => Volatile.Read(ref _databaseRecordingEnabled);
            set => Interlocked.Exchange(ref _databaseRecordingEnabled, value != 0 ? 1 : 0);
        }
        private int _databaseRecordingEnabled;

        private readonly Func<Window> _getWindow;

        /// <summary>
        /// Marshal an action to the UI thread. Silently drops if cleanup has started.
        /// </summary>
        public void RunOnUi(Action action)
        {
            if (action == null) return;
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }
            _ = Dispatcher.BeginInvoke(action);
        }

        /// <summary>
        /// Unified error handler: log, update header, optionally show dialog.
        /// </summary>
        public void HandleError(string summary, Exception exception, bool showDialog)
        {
            DemoLogger.Error(summary, exception);
            SetHeaderStatus(summary, Brushes.OrangeRed);
            if (showDialog)
            {
                var window = _getWindow();
                if (window != null)
                {
                    MessageBox.Show(window, exception?.Message ?? summary, summary,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Thread-safe enqueue of device readings into the background database recorder.
        /// Does nothing if database recording is not enabled.
        /// </summary>
        public void QueueDatabaseValues(
            IndustrialCommSdk.Abstractions.IIndustrialClient client,
            System.Collections.Generic.IReadOnlyCollection<IndustrialCommSdk.Abstractions.DataValue> values)
        {
            var recorder = DatabaseRecorder;
            if (recorder == null || DatabaseRecordingEnabled == 0 || client == null || values == null || values.Count == 0)
                return;

            try
            {
                recorder.TryRecord(client.Kind, client.DeviceId, values);
            }
            catch (Exception ex)
            {
                DemoLogger.Error("采集结果加入数据库队列失败。", ex);
            }
        }
    }
}
