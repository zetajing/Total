using System;
using System.IO.Ports;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Protocols.OpcUa;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Protocols.Redis;
using S7.Net;

namespace IndustrialCommSdk
{
    /// <summary>
    /// 提供内置工业协议客户端的快捷创建和 JSON 配置创建入口。
    /// </summary>
    public static class IndustrialClientFactory
    {
        /// <summary>创建 MQTT 客户端。</summary>
        public static MqttClient Mqtt(string host, int port = 1883, string deviceId = null,
            IIndustrialLogger logger = null, string clientId = null, string username = null,
            string password = null, bool useTls = false, int qos = 0, bool retain = false,
            int connectTimeoutMilliseconds = 5000, int operationTimeoutMilliseconds = 5000)
        {
            ValidateHost(host); ValidatePort(port, nameof(port));
            return new MqttClient(new MqttClientOptions { DeviceId = CoalesceDeviceId(deviceId, "mqtt", host, port),
                Host = host, Port = port, ClientId = clientId, Username = username, Password = password,
                UseTls = useTls, QualityOfService = qos, Retain = retain,
                ConnectTimeoutMilliseconds = connectTimeoutMilliseconds, OperationTimeoutMilliseconds = operationTimeoutMilliseconds }, logger);
        }

        /// <summary>创建 Redis 客户端。</summary>
        public static RedisClient Redis(string host, int port = 6379, string deviceId = null,
            IIndustrialLogger logger = null, string username = null, string password = null,
            int database = 0, bool ssl = false, int connectTimeoutMilliseconds = 5000,
            int operationTimeoutMilliseconds = 5000)
        {
            ValidateHost(host); ValidatePort(port, nameof(port));
            return new RedisClient(new RedisClientOptions { DeviceId = CoalesceDeviceId(deviceId, "redis", host, port, database),
                Host = host, Port = port, Username = username, Password = password, Database = database, Ssl = ssl,
                ConnectTimeoutMilliseconds = connectTimeoutMilliseconds, OperationTimeoutMilliseconds = operationTimeoutMilliseconds }, logger);
        }

        public static IIndustrialClient CreateMqtt(MqttClientOptions options, IIndustrialLogger logger = null) { return new MqttClient(options, logger); }
        public static IIndustrialClient CreateRedis(RedisClientOptions options, IIndustrialLogger logger = null) { return new RedisClient(options, logger); }

        /// <summary>创建 OPC UA 客户端。</summary>
        public static OpcUaClient OpcUa(string endpointUrl, string deviceId = null,
            IIndustrialLogger logger = null, string username = null, string password = null,
            bool useSecurity = false, int connectTimeoutMilliseconds = 10000,
            int operationTimeoutMilliseconds = 5000)
        {
            ValidateText(endpointUrl, nameof(endpointUrl));
            ValidatePositive(connectTimeoutMilliseconds, nameof(connectTimeoutMilliseconds));
            ValidatePositive(operationTimeoutMilliseconds, nameof(operationTimeoutMilliseconds));
            return new OpcUaClient(new OpcUaClientOptions
            {
                DeviceId = string.IsNullOrWhiteSpace(deviceId) ? "opc-ua-" + endpointUrl : deviceId,
                EndpointUrl = endpointUrl,
                Username = username,
                Password = password,
                UseSecurity = useSecurity,
                ConnectTimeoutMilliseconds = connectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            }, logger);
        }

        /// <summary>根据完整选项创建 OPC UA 客户端。</summary>
        public static IIndustrialClient CreateOpcUa(OpcUaClientOptions options, IIndustrialLogger logger = null)
        {
            return new OpcUaClient(options, logger);
        }
        /// <summary>使用常用参数创建 Modbus TCP 客户端。</summary>
        public static ModbusTcpClient ModbusTcp(
            string host,
            int port = 502,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int connectTimeoutMilliseconds = 3000,
            int operationTimeoutMilliseconds = 5000)
        {
            ValidateHost(host);
            ValidatePort(port, nameof(port));
            ValidatePositive(connectTimeoutMilliseconds, nameof(connectTimeoutMilliseconds));
            ValidatePositive(operationTimeoutMilliseconds, nameof(operationTimeoutMilliseconds));

            return new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "modbus-tcp", host, port, slaveId),
                Host = host,
                Port = port,
                SlaveId = slaveId,
                DeviceProfile = deviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc,
                ConnectTimeoutMilliseconds = connectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            }, logger);
        }

        /// <summary>使用常用串口参数创建 Modbus RTU 客户端。</summary>
        public static ModbusRtuClient ModbusRtu(
            string portName,
            int baudRate = 9600,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int dataBits = 8,
            Parity parity = Parity.Even,
            StopBits stopBits = StopBits.One,
            int readTimeout = 3000,
            int writeTimeout = 3000,
            int retries = 2,
            int waitToRetryMilliseconds = 100,
            int operationTimeoutMilliseconds = 5000)
        {
            ValidateText(portName, nameof(portName));
            ValidatePositive(baudRate, nameof(baudRate));
            ValidatePositive(dataBits, nameof(dataBits));
            ValidatePositive(readTimeout, nameof(readTimeout));
            ValidatePositive(writeTimeout, nameof(writeTimeout));
            ValidateNonNegative(retries, nameof(retries));
            ValidateNonNegative(waitToRetryMilliseconds, nameof(waitToRetryMilliseconds));
            ValidatePositive(operationTimeoutMilliseconds, nameof(operationTimeoutMilliseconds));

            return new ModbusRtuClient(new ModbusRtuClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "modbus-rtu", portName, baudRate, slaveId),
                PortName = portName,
                BaudRate = baudRate,
                SlaveId = slaveId,
                DeviceProfile = deviceProfile ?? ModbusDeviceProfiles.Generic,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                ReadTimeout = readTimeout,
                WriteTimeout = writeTimeout,
                Retries = retries,
                WaitToRetryMilliseconds = waitToRetryMilliseconds,
                OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            }, logger);
        }

        /// <summary>使用常用参数创建 Siemens S7 客户端。</summary>
        public static SiemensS7Client SiemensS7(
            string host,
            CpuType cpuType = CpuType.S71200,
            short rack = 0,
            short slot = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            int connectTimeoutMilliseconds = 5000,
            int operationTimeoutMilliseconds = 5000)
        {
            ValidateHost(host);
            ValidatePositive(connectTimeoutMilliseconds, nameof(connectTimeoutMilliseconds));
            ValidatePositive(operationTimeoutMilliseconds, nameof(operationTimeoutMilliseconds));

            return new SiemensS7Client(new SiemensS7ClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "siemens-s7", host, rack, slot),
                Host = host,
                CpuType = cpuType,
                Rack = rack,
                Slot = slot,
                ConnectTimeoutMilliseconds = connectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            }, logger);
        }

        /// <summary>使用常用参数创建 Mitsubishi MC 客户端。</summary>
        public static MitsubishiMcClient MitsubishiMc(
            string host,
            int port = 5000,
            string deviceId = null,
            IIndustrialLogger logger = null,
            int sendTimeoutMilliseconds = 3000,
            int receiveTimeoutMilliseconds = 5000,
            int operationTimeoutMilliseconds = 5000)
        {
            ValidateHost(host);
            ValidatePort(port, nameof(port));
            ValidatePositive(sendTimeoutMilliseconds, nameof(sendTimeoutMilliseconds));
            ValidatePositive(receiveTimeoutMilliseconds, nameof(receiveTimeoutMilliseconds));
            ValidatePositive(operationTimeoutMilliseconds, nameof(operationTimeoutMilliseconds));

            return new MitsubishiMcClient(new MitsubishiMcClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "mitsubishi-mc", host, port),
                Host = host,
                Port = port,
                SendTimeoutMilliseconds = sendTimeoutMilliseconds,
                ReceiveTimeoutMilliseconds = receiveTimeoutMilliseconds,
                OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
            }, logger);
        }

        /// <summary>兼容旧调用名称，创建 Modbus TCP 客户端。</summary>
        public static IIndustrialClient CreateModbus(
            string host,
            int port = 502,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int connectTimeoutMilliseconds = 3000)
        {
            return ModbusTcp(host, port, slaveId, deviceId, logger, deviceProfile, connectTimeoutMilliseconds);
        }

        /// <summary>创建 Modbus TCP 客户端。</summary>
        public static IIndustrialClient CreateModbusTcp(
            string host,
            int port = 502,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int connectTimeoutMilliseconds = 3000)
        {
            return ModbusTcp(host, port, slaveId, deviceId, logger, deviceProfile, connectTimeoutMilliseconds);
        }

        /// <summary>根据完整选项创建 Modbus TCP 客户端。</summary>
        public static IIndustrialClient CreateModbusTcp(
            ModbusTcpClientOptions options,
            IIndustrialLogger logger = null)
        {
            return CreateModbus(options, logger);
        }

        /// <summary>兼容旧调用名称，根据完整选项创建 Modbus TCP 客户端。</summary>
        public static IIndustrialClient CreateModbus(
            ModbusTcpClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new ModbusTcpClient(options, logger);
        }

        /// <summary>使用常用串口参数创建 Modbus RTU 客户端。</summary>
        public static IIndustrialClient CreateModbusRtu(
            string portName,
            int baudRate = 9600,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int dataBits = 8,
            Parity parity = Parity.Even,
            StopBits stopBits = StopBits.One,
            int readTimeout = 3000,
            int writeTimeout = 3000,
            int retries = 2,
            int waitToRetryMilliseconds = 100)
        {
            return ModbusRtu(portName, baudRate, slaveId, deviceId, logger, deviceProfile, dataBits, parity, stopBits, readTimeout, writeTimeout, retries, waitToRetryMilliseconds);
        }

        /// <summary>根据完整选项创建 Modbus RTU 客户端。</summary>
        public static IIndustrialClient CreateModbusRtu(
            ModbusRtuClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new ModbusRtuClient(options, logger);
        }

        /// <summary>使用常用参数创建 Siemens S7 客户端。</summary>
        public static SiemensS7Client CreateSiemensS7(
            string host,
            CpuType cpuType = CpuType.S71200,
            short rack = 0,
            short slot = 1,
            string deviceId = null,
            IIndustrialLogger logger = null)
        {
            return SiemensS7(host, cpuType, rack, slot, deviceId, logger);
        }

        /// <summary>根据完整选项创建 Siemens S7 客户端。</summary>
        public static IIndustrialClient CreateSiemensS7(
            SiemensS7ClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new SiemensS7Client(options, logger);
        }

        /// <summary>使用常用参数创建 Mitsubishi MC 客户端。</summary>
        public static MitsubishiMcClient CreateMitsubishiMc(
            string host,
            int port = 5000,
            string deviceId = null,
            IIndustrialLogger logger = null,
            int sendTimeoutMilliseconds = 3000,
            int receiveTimeoutMilliseconds = 5000)
        {
            return MitsubishiMc(host, port, deviceId, logger, sendTimeoutMilliseconds, receiveTimeoutMilliseconds);
        }

        /// <summary>根据完整选项创建 Mitsubishi MC 客户端。</summary>
        public static IIndustrialClient CreateMitsubishiMc(
            MitsubishiMcClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new MitsubishiMcClient(options, logger);
        }

        /// <summary>从 devices.json 中按设备名创建客户端。</summary>
        public static IIndustrialClient FromConfig(
            string filePath,
            string deviceName,
            IIndustrialLogger logger = null)
        {
            return FromConfig(IndustrialSdkConfig.Load(filePath), deviceName, logger);
        }

        /// <summary>从已解析的 SDK 配置中按设备名创建客户端。</summary>
        public static IIndustrialClient FromConfig(
            IndustrialSdkConfig config,
            string deviceName,
            IIndustrialLogger logger = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            return FromConfig(config.FindDevice(deviceName), logger);
        }

        /// <summary>根据单台设备配置自动选择协议并创建客户端。</summary>
        public static IIndustrialClient FromConfig(
            IndustrialDeviceConfig device,
            IIndustrialLogger logger = null)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            ValidateText(device.Protocol, nameof(device.Protocol));

            var protocol = NormalizeToken(device.Protocol);
            switch (protocol)
            {
                case "modbustcp":
                    return ModbusTcp(
                        device.Host,
                        device.Port.GetValueOrDefault(502),
                        device.SlaveId.GetValueOrDefault(1),
                        CoalesceConfigDeviceId(device.DeviceId, device.Name, "modbus-tcp", device.Host, device.Port.GetValueOrDefault(502), device.SlaveId.GetValueOrDefault(1)),
                        logger,
                        ResolveModbusProfile(device.DeviceProfile, ModbusDeviceProfiles.InovanceEasyPlc),
                        device.ConnectTimeoutMilliseconds.GetValueOrDefault(3000),
                        device.OperationTimeoutMilliseconds.GetValueOrDefault(5000));
                case "modbusrtu":
                    return ModbusRtu(
                        device.PortName,
                        device.BaudRate.GetValueOrDefault(9600),
                        device.SlaveId.GetValueOrDefault(1),
                        CoalesceConfigDeviceId(device.DeviceId, device.Name, "modbus-rtu", device.PortName, device.BaudRate.GetValueOrDefault(9600), device.SlaveId.GetValueOrDefault(1)),
                        logger,
                        ResolveModbusProfile(device.DeviceProfile, ModbusDeviceProfiles.Generic),
                        device.DataBits.GetValueOrDefault(8),
                        ParseEnum(device.Parity, Parity.Even, "parity"),
                        ParseEnum(device.StopBits, StopBits.One, "stopBits"),
                        device.ReadTimeout.GetValueOrDefault(3000),
                        device.WriteTimeout.GetValueOrDefault(3000),
                        device.Retries.GetValueOrDefault(2),
                        device.WaitToRetryMilliseconds.GetValueOrDefault(100),
                        device.OperationTimeoutMilliseconds.GetValueOrDefault(5000));
                case "siemenss7":
                case "s7":
                    return SiemensS7(
                        device.Host,
                        ParseCpuType(device.CpuType),
                        device.Rack.GetValueOrDefault(0),
                        device.Slot.GetValueOrDefault(1),
                        CoalesceConfigDeviceId(device.DeviceId, device.Name, "siemens-s7", device.Host, device.Rack.GetValueOrDefault(0), device.Slot.GetValueOrDefault(1)),
                        logger,
                        device.ConnectTimeoutMilliseconds.GetValueOrDefault(5000),
                        device.OperationTimeoutMilliseconds.GetValueOrDefault(5000));
                case "mitsubishimc":
                case "mc":
                    return MitsubishiMc(
                        device.Host,
                        device.Port.GetValueOrDefault(5000),
                        CoalesceConfigDeviceId(device.DeviceId, device.Name, "mitsubishi-mc", device.Host, device.Port.GetValueOrDefault(5000)),
                        logger,
                        device.SendTimeoutMilliseconds.GetValueOrDefault(3000),
                        device.ReceiveTimeoutMilliseconds.GetValueOrDefault(5000),
                        device.OperationTimeoutMilliseconds.GetValueOrDefault(5000));
                case "opcua":
                    return OpcUa(
                        string.IsNullOrWhiteSpace(device.EndpointUrl)
                            ? string.Format("opc.tcp://{0}:{1}", device.Host, device.Port.GetValueOrDefault(4840))
                            : device.EndpointUrl,
                        CoalesceConfigDeviceId(device.DeviceId, device.Name, "opc-ua", device.Host, device.Port.GetValueOrDefault(4840)),
                        logger,
                        device.Username,
                        device.Password,
                        device.UseSecurity.GetValueOrDefault(false),
                        device.ConnectTimeoutMilliseconds.GetValueOrDefault(10000),
                        device.OperationTimeoutMilliseconds.GetValueOrDefault(5000));
                case "mqtt":
                    return Mqtt(device.Host, device.Port.GetValueOrDefault(device.Ssl.GetValueOrDefault(false) ? 8883 : 1883),
                        CoalesceConfigDeviceId(device.DeviceId, device.Name, "mqtt", device.Host, device.Port.GetValueOrDefault(1883)),
                        logger, device.ClientId, device.Username, device.Password, device.Ssl.GetValueOrDefault(false),
                        device.Qos.GetValueOrDefault(0), device.Retain.GetValueOrDefault(false),
                        device.ConnectTimeoutMilliseconds.GetValueOrDefault(5000), device.OperationTimeoutMilliseconds.GetValueOrDefault(5000));
                case "redis":
                    return Redis(device.Host, device.Port.GetValueOrDefault(6379),
                        CoalesceConfigDeviceId(device.DeviceId, device.Name, "redis", device.Host, device.Port.GetValueOrDefault(6379), device.Database.GetValueOrDefault(0)),
                        logger, device.Username, device.Password, device.Database.GetValueOrDefault(0), device.Ssl.GetValueOrDefault(false),
                        device.ConnectTimeoutMilliseconds.GetValueOrDefault(5000), device.OperationTimeoutMilliseconds.GetValueOrDefault(5000));
                default:
                    throw new ArgumentException(string.Format("Unsupported protocol: {0}", device.Protocol), nameof(device));
            }
        }

        private static string CoalesceDeviceId(string deviceId, string prefix, params object[] parts)
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return deviceId;
            }

            return prefix + "-" + string.Join("-", parts);
        }

        private static string CoalesceConfigDeviceId(string deviceId, string name, string prefix, params object[] parts)
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return deviceId;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return CoalesceDeviceId(null, prefix, parts);
        }

        private static void ValidateHost(string host)
        {
            ValidateText(host, nameof(host));
        }

        private static void ValidateText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(parameterName + " cannot be null or empty.", parameterName);
            }
        }

        private static void ValidatePort(int port, string parameterName)
        {
            if (port <= 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Port must be in the range 1-65535.");
            }
        }

        private static void ValidatePositive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Value must be greater than zero.");
            }
        }

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
            }
        }

        private static IModbusDeviceProfile ResolveModbusProfile(string key, IModbusDeviceProfile defaultProfile)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return defaultProfile;
            }

            return ModbusDeviceProfiles.Find(key)
                ?? throw new ArgumentException(string.Format("Unsupported Modbus device profile: {0}", key), nameof(key));
        }

        private static CpuType ParseCpuType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return CpuType.S71200;
            }

            var normalized = NormalizeToken(value).ToUpperInvariant();
            if (normalized == "S71200") return CpuType.S71200;
            if (normalized == "S71500") return CpuType.S71500;
            if (normalized == "S7300") return CpuType.S7300;
            if (normalized == "S7400") return CpuType.S7400;
            if (normalized == "S7200") return CpuType.S7200;

            CpuType parsed;
            if (Enum.TryParse(value, true, out parsed))
            {
                return parsed;
            }

            throw new ArgumentException(string.Format("Unsupported S7 CPU type: {0}", value), nameof(value));
        }

        private static TEnum ParseEnum<TEnum>(string value, TEnum defaultValue, string parameterName)
            where TEnum : struct
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            TEnum parsed;
            if (Enum.TryParse(value, true, out parsed))
            {
                return parsed;
            }

            throw new ArgumentException(string.Format("Unsupported {0}: {1}", parameterName, value), parameterName);
        }

        private static string NormalizeToken(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();
        }
    }
}
