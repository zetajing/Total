using System;
using System.IO;
using InduLink.Abstractions;
using InduLink.Diagnostics;
using InduLink.Protocols.Ads;
using InduLink.Protocols.Mc;
using InduLink.Protocols.Modbus;
using InduLink.Protocols.Mqtt;
using InduLink.Protocols.OpcUa;
using InduLink.Protocols.Redis;
using InduLink.Protocols.S7;
using InduLink.Runtime;
using InduLink.Runtime.Configuration;

namespace InduLink
{
    public sealed class InduLinkSdk
    {
        private readonly IInduLinkLogger _logger;

        public InduLinkSdk(InduLinkProtocolRegistry protocols, IInduLinkLogger logger = null)
        {
            Protocols = protocols ?? throw new ArgumentNullException(nameof(protocols));
            _logger = logger ?? NullInduLinkLogger.Instance;
            Configuration = new InduLinkConfigurationSerializer(protocols);
        }

        public InduLinkProtocolRegistry Protocols { get; }
        public InduLinkConfigurationSerializer Configuration { get; }

        public static InduLinkSdk CreateDefault(IInduLinkLogger logger = null)
        {
            return new InduLinkSdk(CreateDefaultRegistry(), logger);
        }

        private static InduLinkProtocolRegistry CreateDefaultRegistry()
        {
            return new InduLinkProtocolRegistry()
                .Register(new ModbusTcpProtocolProvider())
                .Register(new ModbusRtuProtocolProvider())
                .Register(new SiemensS7ProtocolProvider())
                .Register(new MitsubishiMcProtocolProvider())
                .Register(new OpcUaProtocolProvider())
                .Register(new MqttProtocolProvider())
                .Register(new RedisProtocolProvider())
                .Register(new AdsProtocolProvider());
        }

        public InduLinkSdkConfig LoadConfiguration(string filePath)
        {
            return Configuration.Load(filePath);
        }

        public InduLinkSdkConfig ParseConfiguration(string json)
        {
            return Configuration.Parse(json);
        }

        public string SerializeConfiguration(InduLinkSdkConfig config)
        {
            return Configuration.Serialize(config);
        }

        public void SaveConfiguration(InduLinkSdkConfig config, string filePath)
        {
            Configuration.Save(config, filePath);
        }

        public IInduLinkClient CreateClient(InduLinkDeviceConfig device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            if (device.Runtime == null)
            {
                throw new ArgumentException("Device runtime cannot be null.", nameof(device));
            }

            if (device.Runtime.OperationTimeoutMilliseconds <= 0)
            {
                throw new ArgumentException("operationTimeoutMilliseconds must be greater than zero.", nameof(device));
            }

            return Protocols.Get(device.Protocol).CreateClient(device, _logger);
        }

        public InduLinkConfiguredClient Open(string configFilePath, string deviceName)
        {
            var fullPath = GetFullConfigPath(configFilePath);
            var config = LoadConfiguration(fullPath);
            var device = config.FindDevice(deviceName);
            var configDirectory = Path.GetDirectoryName(fullPath);
            var tags = TagTable.Load(device.ResolvePointsFile(configDirectory));
            return new InduLinkConfiguredClient(device.Name, CreateClient(device), tags);
        }

        public InduLinkDeviceHost CreateDeviceHost(string configFilePath)
        {
            var fullPath = GetFullConfigPath(configFilePath);
            return CreateDeviceHost(LoadConfiguration(fullPath), Path.GetDirectoryName(fullPath));
        }

        public InduLinkDeviceHost CreateDeviceHost(InduLinkSdkConfig config, string configDirectory)
        {
            return new InduLinkDeviceHost(config, configDirectory, CreateClient, _logger);
        }

        private static string GetFullConfigPath(string configFilePath)
        {
            if (string.IsNullOrWhiteSpace(configFilePath))
            {
                throw new ArgumentException("Config path cannot be empty.", nameof(configFilePath));
            }

            return Path.GetFullPath(configFilePath);
        }
    }
}
