using System;
using System.IO;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Protocols.OpcUa;
using IndustrialCommSdk.Protocols.Redis;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Runtime;

namespace IndustrialCommSdk
{
    public sealed class IndustrialSdk
    {
        private readonly IIndustrialLogger _logger;

        public IndustrialSdk(IndustrialProtocolRegistry protocols, IIndustrialLogger logger = null)
        {
            Protocols = protocols ?? throw new ArgumentNullException(nameof(protocols));
            _logger = logger ?? NullIndustrialLogger.Instance;
            Configuration = new IndustrialConfigurationSerializer(protocols);
        }

        public IndustrialProtocolRegistry Protocols { get; private set; }
        public IndustrialConfigurationSerializer Configuration { get; private set; }

        public static IndustrialSdk CreateDefault(IIndustrialLogger logger = null)
        {
            return new IndustrialSdk(new IndustrialProtocolRegistry()
                .Register(new ModbusTcpProtocolProvider())
                .Register(new ModbusRtuProtocolProvider())
                .Register(new SiemensS7ProtocolProvider())
                .Register(new MitsubishiMcProtocolProvider())
                .Register(new OpcUaProtocolProvider())
                .Register(new MqttProtocolProvider())
                .Register(new RedisProtocolProvider()), logger);
        }

        public IndustrialSdkConfig LoadConfiguration(string filePath) { return Configuration.Load(filePath); }
        public IndustrialSdkConfig ParseConfiguration(string json) { return Configuration.Parse(json); }
        public string SerializeConfiguration(IndustrialSdkConfig config) { return Configuration.Serialize(config); }
        public void SaveConfiguration(IndustrialSdkConfig config, string filePath) { Configuration.Save(config, filePath); }

        public IIndustrialClient CreateClient(IndustrialDeviceConfig device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (device.Runtime == null) throw new ArgumentException("Device runtime cannot be null.", nameof(device));
            if (device.Runtime.OperationTimeoutMilliseconds <= 0)
                throw new ArgumentException("operationTimeoutMilliseconds must be greater than zero.", nameof(device));
            return Protocols.Get(device.Protocol).CreateClient(device, _logger);
        }

        public IndustrialConfiguredClient Open(string configFilePath, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(configFilePath)) throw new ArgumentException("Config path cannot be empty.", nameof(configFilePath));
            var fullPath = Path.GetFullPath(configFilePath);
            var config = LoadConfiguration(fullPath);
            var device = config.FindDevice(deviceName);
            var tags = TagTable.Load(device.ResolvePointsFile(Path.GetDirectoryName(fullPath)));
            return new IndustrialConfiguredClient(device.Name, CreateClient(device), tags);
        }

        public IndustrialDeviceHost CreateDeviceHost(string configFilePath)
        {
            if (string.IsNullOrWhiteSpace(configFilePath)) throw new ArgumentException("Config path cannot be empty.", nameof(configFilePath));
            var fullPath = Path.GetFullPath(configFilePath);
            return CreateDeviceHost(LoadConfiguration(fullPath), Path.GetDirectoryName(fullPath));
        }

        public IndustrialDeviceHost CreateDeviceHost(IndustrialSdkConfig config, string configDirectory)
        {
            return new IndustrialDeviceHost(config, configDirectory, CreateClient, _logger);
        }
    }
}
