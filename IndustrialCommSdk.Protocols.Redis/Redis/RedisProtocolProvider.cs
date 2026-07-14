using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Protocols.Redis
{
    public sealed class RedisSettings : IProtocolSettings
    {
        public string Host { get; set; }
        public int Port { get; set; } = 6379;
        public string Username { get; set; }
        public string Password { get; set; }
        public int Database { get; set; }
        public bool Ssl { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
    }

    public sealed class RedisProtocolProvider : IndustrialProtocolProvider<RedisSettings>
    {
        public override string Protocol { get { return "redis"; } }

        protected override IReadOnlyList<string> Validate(RedisSettings settings)
        {
            return Errors(
                string.IsNullOrWhiteSpace(settings.Host) ? "host is required." : null,
                settings.Port < 1 || settings.Port > 65535 ? "port must be between 1 and 65535." : null,
                settings.Database < 0 ? "database cannot be negative." : null,
                settings.ConnectTimeoutMilliseconds <= 0 ? "connectTimeoutMilliseconds must be positive." : null);
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, RedisSettings settings, IIndustrialLogger logger)
        {
            return new RedisClient(new RedisClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                Host = settings.Host,
                Port = settings.Port,
                Username = settings.Username,
                Password = settings.Password,
                Database = settings.Database,
                Ssl = settings.Ssl,
                ConnectTimeoutMilliseconds = settings.ConnectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }
    }
}
