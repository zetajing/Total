using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Media;

namespace IndustrialCommDemo.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels in the demo.
    /// Provides INotifyPropertyChanged and access to DemoAppContext services.
    /// </summary>
    internal abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        private bool _disposed;

        protected DemoAppContext Ctx { get; }

        protected ViewModelBase(DemoAppContext ctx)
        {
            Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Set a property and raise PropertyChanged if the value changed.
        /// </summary>
        protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Raise PropertyChanged for the given property.
        /// </summary>
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Marshal an action to the UI thread via the shared Dispatcher.
        /// </summary>
        protected void RunOnUi(Action action) => Ctx.RunOnUi(action);

        /// <summary>
        /// Set header status + log error. Conditionally shows MessageBox.
        /// </summary>
        protected void HandleError(string summary, Exception exception, bool showDialog) =>
            Ctx.HandleError(summary, exception, showDialog);

        /// <summary>
        /// Log an informational message to the demo logger.
        /// </summary>
        protected void LogInfo(string message) => Ctx.DemoLogger.Info(message);

        /// <summary>
        /// Log an error message to the demo logger.
        /// </summary>
        protected void LogError(string message, Exception ex) => Ctx.DemoLogger.Error(message, ex);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing) { }
    }
}
