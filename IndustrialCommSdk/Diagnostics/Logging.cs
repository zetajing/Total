using System;

namespace IndustrialCommSdk.Diagnostics
{
    public interface IIndustrialLogger
    {
        void Trace(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception);
    }

    public sealed class NullIndustrialLogger : IIndustrialLogger
    {
        public static readonly NullIndustrialLogger Instance = new NullIndustrialLogger();

        private NullIndustrialLogger()
        {
        }

        public void Trace(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception exception)
        {
        }
    }

    public sealed class TraceIndustrialLogger : IIndustrialLogger
    {
        public void Trace(string message) { System.Diagnostics.Trace.TraceInformation(message); }
        public void Info(string message) { System.Diagnostics.Trace.TraceInformation(message); }
        public void Warn(string message) { System.Diagnostics.Trace.TraceWarning(message); }
        public void Error(string message, Exception exception) { System.Diagnostics.Trace.TraceError("{0} {1}", message, exception); }
    }
}
