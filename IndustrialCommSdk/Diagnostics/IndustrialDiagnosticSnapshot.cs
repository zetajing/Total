using System;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Diagnostics
{
    /// <summary>工业通讯失败的稳定分类，便于 UI、日志和监控按原因聚合。</summary>
    public enum IndustrialFailureCategory
    {
        None = 0,
        Connection = 1,
        Timeout = 2,
        Address = 3,
        Protocol = 4,
        DataConversion = 5,
        Unknown = 6,
    }

    /// <summary>客户端从创建至当前时刻累计形成的只读诊断快照。</summary>
    public sealed class IndustrialDiagnosticSnapshot
    {
        public IndustrialDiagnosticSnapshot(string deviceId, ProtocolKind protocol, long totalOperations, long successfulOperations,
            long failedOperations, long timeoutCount, int consecutiveFailures, long lastOperationElapsedMilliseconds,
            IndustrialFailureCategory lastFailureCategory, string lastError, DateTimeOffset? lastOperationUtc,
            long serialPortOpenFailureCount = 0, long responseTimeoutCount = 0, long frameErrorCount = 0)
        {
            DeviceId = deviceId;
            Protocol = protocol;
            TotalOperations = totalOperations;
            SuccessfulOperations = successfulOperations;
            FailedOperations = failedOperations;
            TimeoutCount = timeoutCount;
            ConsecutiveFailures = consecutiveFailures;
            LastOperationElapsedMilliseconds = lastOperationElapsedMilliseconds;
            LastFailureCategory = lastFailureCategory;
            LastError = lastError;
            LastOperationUtc = lastOperationUtc;
            SerialPortOpenFailureCount = serialPortOpenFailureCount;
            ResponseTimeoutCount = responseTimeoutCount;
            FrameErrorCount = frameErrorCount;
        }

        public string DeviceId { get; private set; }
        public ProtocolKind Protocol { get; private set; }
        public long TotalOperations { get; private set; }
        public long SuccessfulOperations { get; private set; }
        public long FailedOperations { get; private set; }
        public long TimeoutCount { get; private set; }
        public int ConsecutiveFailures { get; private set; }
        public long LastOperationElapsedMilliseconds { get; private set; }
        public IndustrialFailureCategory LastFailureCategory { get; private set; }
        public string LastError { get; private set; }
        public DateTimeOffset? LastOperationUtc { get; private set; }
        public long SerialPortOpenFailureCount { get; private set; }
        public long ResponseTimeoutCount { get; private set; }
        public long FrameErrorCount { get; private set; }

        public static IndustrialDiagnosticSnapshot Empty(string deviceId = null, ProtocolKind protocol = 0)
        {
            return new IndustrialDiagnosticSnapshot(deviceId, protocol, 0, 0, 0, 0, 0, 0, IndustrialFailureCategory.None, null, null);
        }
    }

    /// <summary>由支持结构化诊断的客户端选择性实现，不影响 IIndustrialClient 兼容性。</summary>
    public interface IIndustrialDiagnosticsProvider
    {
        IndustrialDiagnosticSnapshot GetDiagnosticSnapshot();
    }

    public static class IndustrialDiagnosticsSnapshotExtensions
    {
        /// <summary>获取诊断快照；自定义客户端未实现诊断接口时返回空快照。</summary>
        public static IndustrialDiagnosticSnapshot GetDiagnosticSnapshot(this IIndustrialClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            var provider = client as IIndustrialDiagnosticsProvider;
            return provider == null ? IndustrialDiagnosticSnapshot.Empty(client.DeviceId, client.Kind) : provider.GetDiagnosticSnapshot();
        }
    }
}
