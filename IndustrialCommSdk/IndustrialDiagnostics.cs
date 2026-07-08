using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk
{
    public sealed class IndustrialConnectionTestResult
    {
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

        public string DeviceId { get; private set; }
        public ProtocolKind Protocol { get; private set; }
        public bool IsSuccess { get; private set; }
        public long ElapsedMilliseconds { get; private set; }
        public HealthSnapshot Health { get; private set; }
        public string ErrorMessage { get; private set; }
        public Exception Exception { get; private set; }

        public override string ToString()
        {
            return IsSuccess
                ? string.Format("{0} {1} connection OK in {2}ms.", Protocol, DeviceId, ElapsedMilliseconds)
                : string.Format("{0} {1} connection failed in {2}ms: {3}", Protocol, DeviceId, ElapsedMilliseconds, ErrorMessage);
        }
    }

    public static class IndustrialDiagnosticsExtensions
    {
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
