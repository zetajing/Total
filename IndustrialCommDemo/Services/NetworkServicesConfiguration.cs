using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IndustrialCommSdk.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace IndustrialCommDemo.Services
{
    public sealed class NetworkServicesConfiguration
    {
        public int Version { get; set; } = 1;
        public MqttBrokerConfiguration MqttBroker { get; set; } = new MqttBrokerConfiguration();
        public WebGatewayConfiguration WebGateway { get; set; } = new WebGatewayConfiguration();
        public FtpConnectionConfiguration Ftp { get; set; } = new FtpConnectionConfiguration();
    }

    public sealed class MqttBrokerConfiguration
    {
        public bool AutoStart { get; set; }
        public string BindAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 1883;
        public bool UseTls { get; set; }
        public int TlsPort { get; set; } = 8883;
        public string CertificateThumbprint { get; set; }
        public string Username { get; set; } = "industrial";
        public string PasswordSecretName { get; set; } = "mqtt.broker.password";
    }

    public sealed class WebGatewayConfiguration
    {
        public bool AutoStart { get; set; }
        public string ListenPrefix { get; set; } = "http://127.0.0.1:8088/";
        public string ApiKeySecretName { get; set; } = "web.api-key";
        public bool EnableRemoteWrites { get; set; }
        public bool AllowRawAddressReads { get; set; }
        public bool ExposeRawAddresses { get; set; }
        public List<string> AllowedOrigins { get; set; } = new List<string>();
    }

    public sealed class FtpConnectionConfiguration
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 21;
        public string Username { get; set; } = "industrial";
        public string PasswordSecretName { get; set; } = "ftp.password";
        public bool UseTls { get; set; } = true;
        public bool AllowInsecureFtp { get; set; }
        public bool PassiveMode { get; set; } = true;
        public string RemoteRoot { get; set; } = "/";
    }

    public sealed class NetworkServicesConfigurationStore
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        public NetworkServicesConfigurationStore(string filePath = null)
        {
            FilePath = Path.GetFullPath(filePath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Config",
                "network-services.json"));
            SecretsDirectory = Path.Combine(StoragePathProvider.StateRoot, "network-secrets");
        }

        public string FilePath { get; }
        public string SecretsDirectory { get; }

        public NetworkServicesConfiguration Load()
        {
            if (!File.Exists(FilePath))
            {
                var defaults = Normalize(new NetworkServicesConfiguration());
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var configuration = JsonConvert.DeserializeObject<NetworkServicesConfiguration>(json, JsonSettings);
            return Normalize(configuration ?? new NetworkServicesConfiguration());
        }

        public void Save(NetworkServicesConfiguration configuration)
        {
            configuration = Normalize(configuration ?? throw new ArgumentNullException(nameof(configuration)));
            Validate(configuration);
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(configuration, JsonSettings), new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    var backup = FilePath + ".bak";
                    File.Replace(temporary, FilePath, backup, true);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                else
                {
                    File.Move(temporary, FilePath);
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static void Validate(NetworkServicesConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            ValidatePort(configuration.MqttBroker.Port, "MQTT port");
            ValidatePort(configuration.MqttBroker.TlsPort, "MQTT TLS port");
            ValidatePort(configuration.Ftp.Port, "FTP port");
            if (string.IsNullOrWhiteSpace(configuration.MqttBroker.BindAddress)) throw new InvalidOperationException("MQTT bindAddress cannot be empty.");
            if (string.IsNullOrWhiteSpace(configuration.MqttBroker.Username)) throw new InvalidOperationException("MQTT username cannot be empty.");
            if (string.IsNullOrWhiteSpace(configuration.MqttBroker.PasswordSecretName)) throw new InvalidOperationException("MQTT passwordSecretName cannot be empty.");
            if (string.IsNullOrWhiteSpace(configuration.WebGateway.ListenPrefix)) throw new InvalidOperationException("Web listenPrefix cannot be empty.");
            if (!Uri.TryCreate(configuration.WebGateway.ListenPrefix, UriKind.Absolute, out var webUri) ||
                (webUri.Scheme != Uri.UriSchemeHttp && webUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Web listenPrefix must be an absolute HTTP or HTTPS URI.");
            if (!configuration.WebGateway.ListenPrefix.EndsWith("/", StringComparison.Ordinal))
                throw new InvalidOperationException("Web listenPrefix must end with '/'.");
            if (string.IsNullOrWhiteSpace(configuration.WebGateway.ApiKeySecretName)) throw new InvalidOperationException("Web apiKeySecretName cannot be empty.");
            if (string.IsNullOrWhiteSpace(configuration.Ftp.Host)) throw new InvalidOperationException("FTP host cannot be empty.");
            if (string.IsNullOrWhiteSpace(configuration.Ftp.Username)) throw new InvalidOperationException("FTP username cannot be empty.");
            if (string.IsNullOrWhiteSpace(configuration.Ftp.PasswordSecretName)) throw new InvalidOperationException("FTP passwordSecretName cannot be empty.");
            if (!configuration.Ftp.UseTls && !configuration.Ftp.AllowInsecureFtp)
                throw new InvalidOperationException("Plain FTP requires allowInsecureFtp=true.");
        }

        private static NetworkServicesConfiguration Normalize(NetworkServicesConfiguration configuration)
        {
            configuration.MqttBroker = configuration.MqttBroker ?? new MqttBrokerConfiguration();
            configuration.WebGateway = configuration.WebGateway ?? new WebGatewayConfiguration();
            configuration.Ftp = configuration.Ftp ?? new FtpConnectionConfiguration();
            configuration.WebGateway.AllowedOrigins = configuration.WebGateway.AllowedOrigins == null
                ? new List<string>()
                : configuration.WebGateway.AllowedOrigins
                    .Where(origin => !string.IsNullOrWhiteSpace(origin))
                    .Select(origin => origin.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            if (configuration.Version <= 0) configuration.Version = 1;
            return configuration;
        }

        private static void ValidatePort(int port, string name)
        {
            if (port < 1 || port > 65535) throw new InvalidOperationException(name + " must be between 1 and 65535.");
        }
    }
}
