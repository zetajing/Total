using System;

namespace IndustrialCommSdk.Diagnostics
{
    /// <summary>
    ///     工业通信库的日志记录接口。
    ///     定义了一组标准日志级别的方法，用于在整个工业通信 SDK 中记录诊断信息。
    /// </summary>
    public interface IIndustrialLogger
    {
        /// <summary>
        ///     记录一条详细的跟踪日志。
        ///     通常用于开发调试阶段，输出最细粒度的诊断信息，在生产环境中可能被禁用。
        /// </summary>
        /// <param name="message">要记录的跟踪消息文本。</param>
        void Trace(string message);

        /// <summary>
        ///     记录一条信息级别的日志。
        ///     用于记录应用程序的正常运行事件，例如连接成功、数据发送完成等。
        /// </summary>
        /// <param name="message">要记录的信息消息文本。</param>
        void Info(string message);

        /// <summary>
        ///     记录一条警告级别的日志。
        ///     用于指示可能的问题或需要注意的情况，但不会影响程序的继续执行。
        /// </summary>
        /// <param name="message">要记录的警告消息文本。</param>
        void Warn(string message);

        /// <summary>
        ///     记录一条错误级别的日志。
        ///     用于记录可恢复或不可恢复的运行时错误，通常附带异常详细信息。
        /// </summary>
        /// <param name="message">描述错误的文本消息。</param>
        /// <param name="exception">与错误关联的异常实例，包含堆栈跟踪和内部异常等详细信息。</param>
        void Error(string message, Exception exception);
    }

    /// <summary>
    ///     空日志记录器的实现。
    ///     所有日志方法均为空操作，不产生任何输出；适用于禁用日志记录或单元测试场景。
    /// </summary>
    public sealed class NullIndustrialLogger : IIndustrialLogger
    {
        /// <summary>
        ///     获取 <see cref="NullIndustrialLogger"/> 的全局单例实例。
        /// </summary>
        public static readonly NullIndustrialLogger Instance = new NullIndustrialLogger();

        /// <summary>
        ///     私有构造函数，防止外部直接实例化。
        ///     应通过 <see cref="Instance"/> 属性获取单例。
        /// </summary>
        private NullIndustrialLogger()
        {
        }

        /// <summary>
        ///     跟踪日志 — 不执行任何操作。
        /// </summary>
        /// <param name="message">被忽略的跟踪消息。</param>
        public void Trace(string message)
        {
        }

        /// <summary>
        ///     信息日志 — 不执行任何操作。
        /// </summary>
        /// <param name="message">被忽略的信息消息。</param>
        public void Info(string message)
        {
        }

        /// <summary>
        ///     警告日志 — 不执行任何操作。
        /// </summary>
        /// <param name="message">被忽略的警告消息。</param>
        public void Warn(string message)
        {
        }

        /// <summary>
        ///     错误日志 — 不执行任何操作。
        /// </summary>
        /// <param name="message">被忽略的错误消息。</param>
        /// <param name="exception">被忽略的异常实例。</param>
        public void Error(string message, Exception exception)
        {
        }
    }

    /// <summary>
    ///     基于 <see cref="System.Diagnostics.Trace"/> 的日志记录器实现。
    ///     将日志消息委托给 .NET 的 <see cref="System.Diagnostics.Trace"/> 类输出，
    ///     适用于已配置 Trace 侦听器的桌面、服务或 Web 应用程序。
    /// </summary>
    public sealed class TraceIndustrialLogger : IIndustrialLogger
    {
        /// <summary>
        ///     记录跟踪日志。
        ///     委托给 <see cref="System.Diagnostics.Trace.TraceInformation(string)"/>。
        /// </summary>
        /// <param name="message">要记录的跟踪消息。</param>
        public void Trace(string message) { System.Diagnostics.Trace.TraceInformation(message); }

        /// <summary>
        ///     记录信息日志。
        ///     委托给 <see cref="System.Diagnostics.Trace.TraceInformation(string)"/>。
        /// </summary>
        /// <param name="message">要记录的信息消息。</param>
        public void Info(string message) { System.Diagnostics.Trace.TraceInformation(message); }

        /// <summary>
        ///     记录警告日志。
        ///     委托给 <see cref="System.Diagnostics.Trace.TraceWarning(string)"/>。
        /// </summary>
        /// <param name="message">要记录的警告消息。</param>
        public void Warn(string message) { System.Diagnostics.Trace.TraceWarning(message); }

        /// <summary>
        ///     记录错误日志，包含异常的详细信息。
        ///     委托给 <see cref="System.Diagnostics.Trace.TraceError(string, object[])"/>，
        ///     将消息与异常组合输出，便于后续排查问题。
        /// </summary>
        /// <param name="message">描述错误的文本消息。</param>
        /// <param name="exception">与错误关联的异常实例。</param>
        public void Error(string message, Exception exception) { System.Diagnostics.Trace.TraceError("{0} {1}", message, exception); }
    }
}
