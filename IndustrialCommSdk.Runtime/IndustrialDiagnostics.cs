using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Runtime
{
    /// <summary>表示一次设备连接诊断的耗时、健康状态和错误信息。</summary>
    public sealed class IndustrialConnectionTestResult
    {
        /// <summary>创建连接诊断结果。</summary>
        public IndustrialConnectionTestResult(
            string deviceId,
            ProtocolKind protocol,
            bool isSuccess,
            long elapsedMilliseconds,
            HealthSnapshot health,
            string errorMessage,
            Exception exception)
        {
            DeviceId = deviceId;
            Protocol = protocol;
            IsSuccess = isSuccess;
            ElapsedMilliseconds = elapsedMilliseconds;
            Health = health;
            ErrorMessage = errorMessage;
            Exception = exception;
        }

        /// <summary>获取设备标识。</summary>
        public string DeviceId { get; private set; }
        /// <summary>获取设备协议。</summary>
        public ProtocolKind Protocol { get; private set; }
        /// <summary>获取连接诊断是否成功。</summary>
        public bool IsSuccess { get; private set; }
        /// <summary>获取诊断耗时，单位为毫秒。</summary>
        public long ElapsedMilliseconds { get; private set; }
        /// <summary>获取诊断完成时的客户端健康快照。</summary>
        public HealthSnapshot Health { get; private set; }
        /// <summary>获取失败原因。</summary>
        public string ErrorMessage { get; private set; }
        /// <summary>获取诊断期间捕获的异常。</summary>
        public Exception Exception { get; private set; }

        /// <summary>返回适合日志和界面显示的诊断摘要。</summary>
        public override string ToString()
        {
            return IsSuccess
                ? string.Format("{0} {1} connection OK in {2}ms.", Protocol, DeviceId, ElapsedMilliseconds)
                : string.Format("{0} {1} connection failed in {2}ms: {3}", Protocol, DeviceId, ElapsedMilliseconds, ErrorMessage);
        }
    }

    /// <summary>提供设备连接自检扩展方法。</summary>
    public static class IndustrialDiagnosticsExtensions
    {
        /// <summary>执行连接、读取健康状态和断开连接的完整诊断。</summary>
        public static async Task<IndustrialConnectionTestResult> TestAsync(
            this IIndustrialClient client,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            var stopwatch = Stopwatch.StartNew();
            var wasConnected = client.IsConnected;
            try
            {
                if (!wasConnected)
                {
                    await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                stopwatch.Stop();
                return new IndustrialConnectionTestResult(
                    client.DeviceId,
                    client.Kind,
                    true,
                    stopwatch.ElapsedMilliseconds,
                    client.GetHealth(),
                    null,
                    null);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new IndustrialConnectionTestResult(
                    client.DeviceId,
                    client.Kind,
                    false,
                    stopwatch.ElapsedMilliseconds,
                    client.GetHealth(),
                    ex.Message,
                    ex);
            }
            finally
            {
                if (!wasConnected && client.IsConnected)
                {
                    try
                    {
                        await client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
