using System;

namespace IndustrialCommDemo.SocketDebug
{
    /// <summary>
    /// 表示 Socket 文本消息事件的数据，继承自 <see cref="EventArgs"/>。
    /// 包含会话唯一标识、远程终结点字符串以及接收到的文本消息内容。
    /// </summary>
    internal sealed class SocketTextMessageEventArgs : EventArgs
    {
        /// <summary>
        /// 初始化 <see cref="SocketTextMessageEventArgs"/> 类的新实例。
        /// </summary>
        /// <param name="sessionId">产生此消息的会话唯一标识（GUID）。</param>
        /// <param name="remoteEndPoint">远程终结点字符串（IP:Port 格式）；若为 null 则替换为空字符串。</param>
        /// <param name="message">接收到的文本消息内容；若为 null 则替换为空字符串。</param>
        public SocketTextMessageEventArgs(Guid sessionId, string remoteEndPoint, string message)
        {
            SessionId = sessionId;
            RemoteEndPoint = remoteEndPoint ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// 获取产生此消息的会话唯一标识。
        /// </summary>
        public Guid SessionId { get; private set; }

        /// <summary>
        /// 获取远程终结点的字符串表示形式。
        /// </summary>
        public string RemoteEndPoint { get; private set; }

        /// <summary>
        /// 获取接收到的文本消息内容。
        /// </summary>
        public string Message { get; private set; }
    }

    /// <summary>
    /// 表示 Socket 会话连接或断开事件的数据，继承自 <see cref="EventArgs"/>。
    /// 包含会话唯一标识、远程终结点字符串以及当前的总会话数。
    /// </summary>
    internal sealed class SocketSessionEventArgs : EventArgs
    {
        /// <summary>
        /// 初始化 <see cref="SocketSessionEventArgs"/> 类的新实例。
        /// </summary>
        /// <param name="sessionId">当前会话的唯一标识（GUID）。</param>
        /// <param name="remoteEndPoint">远程终结点字符串（IP:Port 格式）；若为 null 则替换为空字符串。</param>
        /// <param name="sessionCount">当前服务端的总会话数量。</param>
        public SocketSessionEventArgs(Guid sessionId, string remoteEndPoint, int sessionCount)
        {
            SessionId = sessionId;
            RemoteEndPoint = remoteEndPoint ?? string.Empty;
            SessionCount = sessionCount;
        }

        /// <summary>
        /// 获取当前会话的唯一标识。
        /// </summary>
        public Guid SessionId { get; private set; }

        /// <summary>
        /// 获取远程终结点的字符串表示形式。
        /// </summary>
        public string RemoteEndPoint { get; private set; }

        /// <summary>
        /// 获取当前服务端的总会话数量。
        /// </summary>
        public int SessionCount { get; private set; }
    }
}
