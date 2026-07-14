using System;

namespace IndustrialCommSdk.Exceptions
{
    /// <summary>
    ///     工业通信异常的基类。
    ///     所有工业通信 SDK 自定义异常的基类，继承自 <see cref="Exception"/>。
    ///     此类型可用于捕获所有由工业通信库抛出的特定异常。
    /// </summary>
    public class IndustrialCommunicationException : Exception
    {
        /// <summary>
        ///     使用指定的错误消息初始化 <see cref="IndustrialCommunicationException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的消息文本。</param>
        public IndustrialCommunicationException(string message) : base(message) { }

        /// <summary>
        ///     使用指定的错误消息和对导致此异常的内部异常的引用来初始化
        ///     <see cref="IndustrialCommunicationException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述错误的消息文本。</param>
        /// <param name="innerException">导致当前异常的异常；如果未指定内部异常，则为 null。</param>
        public IndustrialCommunicationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    ///     工业通信连接异常。
    ///     当与工业设备或远程通信端点的连接建立失败、连接意外断开或握手无效时抛出。
    /// </summary>
    public sealed class IndustrialConnectionException : IndustrialCommunicationException
    {
        /// <summary>
        ///     使用指定的错误消息和内部异常初始化 <see cref="IndustrialConnectionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述连接错误的文本消息。</param>
        /// <param name="innerException">导致当前连接异常的内部异常。</param>
        public IndustrialConnectionException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        ///     使用指定的错误消息初始化 <see cref="IndustrialConnectionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述连接错误的文本消息。</param>
        public IndustrialConnectionException(string message) : base(message) { }
    }

    /// <summary>
    ///     工业通信超时异常。
    ///     当通信操作（如读取、写入或握手）在预期的时间限制内未能完成时抛出。
    /// </summary>
    public sealed class IndustrialTimeoutException : IndustrialCommunicationException
    {
        /// <summary>
        ///     使用指定的错误消息和内部异常初始化 <see cref="IndustrialTimeoutException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述超时错误的文本消息。</param>
        /// <param name="innerException">导致当前超时异常的内部异常。</param>
        public IndustrialTimeoutException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        ///     使用指定的错误消息初始化 <see cref="IndustrialTimeoutException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述超时错误的文本消息。</param>
        public IndustrialTimeoutException(string message) : base(message) { }
    }

    /// <summary>
    ///     工业通信协议异常。
    ///     当从工业设备接收到的数据不符合预期的协议格式、校验码错误或命令代码无效时抛出。
    /// </summary>
    public sealed class IndustrialProtocolException : IndustrialCommunicationException
    {
        /// <summary>
        ///     使用指定的错误消息和内部异常初始化 <see cref="IndustrialProtocolException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述协议错误的文本消息。</param>
        /// <param name="innerException">导致当前协议异常的内部异常。</param>
        public IndustrialProtocolException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        ///     使用指定的错误消息初始化 <see cref="IndustrialProtocolException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述协议错误的文本消息。</param>
        public IndustrialProtocolException(string message) : base(message) { }
    }

    /// <summary>
    ///     工业通信地址解析异常。
    ///     当通信地址字符串的格式无效、解析失败或地址超出设备支持的范围时抛出。
    /// </summary>
    public sealed class IndustrialAddressParseException : IndustrialCommunicationException
    {
        /// <summary>
        ///     使用指定的错误消息和内部异常初始化 <see cref="IndustrialAddressParseException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述地址解析错误的文本消息。</param>
        /// <param name="innerException">导致当前解析异常的内部异常。</param>
        public IndustrialAddressParseException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        ///     使用指定的错误消息初始化 <see cref="IndustrialAddressParseException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述地址解析错误的文本消息。</param>
        public IndustrialAddressParseException(string message) : base(message) { }
    }

    /// <summary>
    ///     工业通信数据转换异常。
    ///     当原始字节数据无法转换为目标类型（如 Int32、Float、Boolean 等），
    ///     或目标值超出目标类型的有效范围时抛出。
    /// </summary>
    public sealed class IndustrialDataConversionException : IndustrialCommunicationException
    {
        /// <summary>
        ///     使用指定的错误消息和内部异常初始化 <see cref="IndustrialDataConversionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述数据转换错误的文本消息。</param>
        /// <param name="innerException">导致当前转换异常的内部异常。</param>
        public IndustrialDataConversionException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        ///     使用指定的错误消息初始化 <see cref="IndustrialDataConversionException"/> 类的新实例。
        /// </summary>
        /// <param name="message">描述数据转换错误的文本消息。</param>
        public IndustrialDataConversionException(string message) : base(message) { }
    }
}
