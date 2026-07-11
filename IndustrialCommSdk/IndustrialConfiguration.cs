using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace IndustrialCommSdk
{
    /// <summary>表示部署配置的离线校验结果。</summary>
    public sealed class IndustrialConfigValidationResult
    {
        internal IndustrialConfigValidationResult(IReadOnlyList<string> errors)
        {
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        /// <summary>获取配置是否通过全部校验。</summary>
        public bool IsValid { get { return Errors.Count == 0; } }

        /// <summary>获取所有校验错误；为空表示配置可以用于部署。</summary>
        public IReadOnlyList<string> Errors { get; private set; }
    }

    /// <summary>
    /// 表示从 devices.json 读取的 SDK 部署配置。
    /// </summary>
    [DataContract]
    public sealed class IndustrialSdkConfig
    {
        /// <summary>获取或设置全部设备配置。</summary>
        [DataMember(Name = "devices")]
        public List<IndustrialDeviceConfig> Devices { get; set; } = new List<IndustrialDeviceConfig>();

        /// <summary>从 UTF-8 JSON 文件加载设备配置。</summary>
        public static IndustrialSdkConfig Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Config file path cannot be null or empty.", nameof(filePath));

            using (var stream = File.OpenRead(filePath))
            {
                var config = (IndustrialSdkConfig)new DataContractJsonSerializer(typeof(IndustrialSdkConfig)).ReadObject(stream);
                if (config == null)
                {
                    throw new InvalidOperationException("SDK config file is empty.");
                }

                if (config.Devices == null)
                {
                    config.Devices = new List<IndustrialDeviceConfig>();
                }

                return config;
            }
        }

        /// <summary>从 JSON 文本解析设备配置。</summary>
        public static IndustrialSdkConfig FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Config JSON cannot be null or empty.", nameof(json));

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var config = (IndustrialSdkConfig)new DataContractJsonSerializer(typeof(IndustrialSdkConfig)).ReadObject(stream);
                if (config == null)
                {
                    throw new InvalidOperationException("SDK config JSON is empty.");
                }

                if (config.Devices == null)
                {
                    config.Devices = new List<IndustrialDeviceConfig>();
                }

                return config;
            }
        }

        /// <summary>将设备配置序列化为便于人工维护的 UTF-8 JSON 文本。</summary>
        public string ToJson()
        {
            using (var stream = new MemoryStream())
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true))
            {
                new DataContractJsonSerializer(typeof(IndustrialSdkConfig)).WriteObject(writer, this);
                writer.Flush();
                return Encoding.UTF8.GetString(stream.ToArray()).Replace("\\/", "/");
            }
        }

        /// <summary>将设备配置保存为 UTF-8 JSON 文件；目标目录不存在时自动创建。</summary>
        public void Save(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Config file path cannot be null or empty.", nameof(filePath));

            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, ToJson(), new UTF8Encoding(false));
        }

        /// <summary>按名称查找设备，名称匹配不区分大小写。</summary>
        public IndustrialDeviceConfig FindDevice(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Device name cannot be null or empty.", nameof(name));

            foreach (var device in Devices)
            {
                if (device != null && string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            throw new KeyNotFoundException(string.Format("Device '{0}' was not found in SDK config.", name));
        }

        /// <summary>
        /// 在不连接设备的情况下校验部署配置、协议参数和全部关联点位表。
        /// configDirectory 应为 devices.json 所在目录，用于解析相对 pointsFile 路径。
        /// </summary>
        public IndustrialConfigValidationResult Validate(string configDirectory)
        {
            var errors = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                errors.Add("配置目录不能为空。");
                return new IndustrialConfigValidationResult(errors.AsReadOnly());
            }

            if (Devices == null || Devices.Count == 0)
            {
                errors.Add("devices 至少需要配置一台设备。");
                return new IndustrialConfigValidationResult(errors.AsReadOnly());
            }

            for (var i = 0; i < Devices.Count; i++)
            {
                var device = Devices[i];
                var label = string.Format("devices[{0}]", i);
                if (device == null)
                {
                    errors.Add(label + " 不能为空。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(device.Name))
                {
                    errors.Add(label + ".name 不能为空。");
                }
                else if (!names.Add(device.Name))
                {
                    errors.Add(string.Format("设备名 '{0}' 重复。", device.Name));
                }

                if (!device.Enabled.GetValueOrDefault(true))
                {
                    continue;
                }

                if (device.PollingIntervalMilliseconds.HasValue && device.PollingIntervalMilliseconds.Value <= 0)
                {
                    errors.Add(string.Format("设备 '{0}' 的 pollingIntervalMilliseconds 必须大于 0。", device.Name ?? label));
                }

                if (device.ReconnectDelayMilliseconds.HasValue && device.ReconnectDelayMilliseconds.Value <= 0)
                {
                    errors.Add(string.Format("设备 '{0}' 的 reconnectDelayMilliseconds 必须大于 0。", device.Name ?? label));
                }

                if (device.OperationTimeoutMilliseconds.HasValue && device.OperationTimeoutMilliseconds.Value <= 0)
                {
                    errors.Add(string.Format("设备 '{0}' 的 operationTimeoutMilliseconds 必须大于 0。", device.Name ?? label));
                }

                try
                {
                    using (IndustrialClientFactory.FromConfig(device))
                    {
                        // 仅创建客户端以复用协议参数校验，不建立网络连接。
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format("设备 '{0}' 协议配置错误：{1}", device.Name ?? label, ex.Message));
                }

                try
                {
                    var pointFile = device.ResolvePointsFile(configDirectory);
                    if (!File.Exists(pointFile))
                    {
                        errors.Add(string.Format("设备 '{0}' 的点位文件不存在：{1}", device.Name ?? label, pointFile));
                    }
                    else
                    {
                        TagTable.Load(pointFile);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format("设备 '{0}' 点位表错误：{1}", device.Name ?? label, ex.Message));
                }
            }

            return new IndustrialConfigValidationResult(errors.AsReadOnly());
        }
    }

    /// <summary>
    /// 描述一台工业设备的协议、连接参数及关联点位表。
    /// 未使用的协议字段可以省略。
    /// </summary>
    [DataContract]
    public sealed class IndustrialDeviceConfig
    {
        /// <summary>获取或设置配置中用于查找设备的唯一名称。</summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>获取或设置协议名称，例如 modbus-tcp、modbus-rtu、siemens-s7 或 mitsubishi-mc。</summary>
        [DataMember(Name = "protocol")]
        public string Protocol { get; set; }

        /// <summary>获取或设置日志和诊断中使用的设备标识；为空时使用 Name。</summary>
        [DataMember(Name = "deviceId", EmitDefaultValue = false)]
        public string DeviceId { get; set; }

        /// <summary>获取或设置 TCP 设备的 IP 地址或主机名。</summary>
        [DataMember(Name = "host", EmitDefaultValue = false)]
        public string Host { get; set; }

        /// <summary>获取或设置 TCP 端口；为空时使用对应协议默认值。</summary>
        [DataMember(Name = "port", EmitDefaultValue = false)]
        public int? Port { get; set; }

        /// <summary>获取或设置 Modbus 从站地址。</summary>
        [DataMember(Name = "slaveId", EmitDefaultValue = false)]
        public byte? SlaveId { get; set; }

        /// <summary>获取或设置 Modbus RTU 串口名称，例如 COM3。</summary>
        [DataMember(Name = "portName", EmitDefaultValue = false)]
        public string PortName { get; set; }

        /// <summary>获取或设置串口波特率。</summary>
        [DataMember(Name = "baudRate", EmitDefaultValue = false)]
        public int? BaudRate { get; set; }

        /// <summary>获取或设置串口数据位。</summary>
        [DataMember(Name = "dataBits", EmitDefaultValue = false)]
        public int? DataBits { get; set; }

        /// <summary>获取或设置串口校验方式，例如 None、Even 或 Odd。</summary>
        [DataMember(Name = "parity", EmitDefaultValue = false)]
        public string Parity { get; set; }

        /// <summary>获取或设置串口停止位，例如 One 或 Two。</summary>
        [DataMember(Name = "stopBits", EmitDefaultValue = false)]
        public string StopBits { get; set; }

        /// <summary>获取或设置串口读取超时，单位为毫秒。</summary>
        [DataMember(Name = "readTimeout", EmitDefaultValue = false)]
        public int? ReadTimeout { get; set; }

        /// <summary>获取或设置串口写入超时，单位为毫秒。</summary>
        [DataMember(Name = "writeTimeout", EmitDefaultValue = false)]
        public int? WriteTimeout { get; set; }

        /// <summary>获取或设置建立连接的超时时间，单位为毫秒。</summary>
        [DataMember(Name = "connectTimeoutMilliseconds", EmitDefaultValue = false)]
        public int? ConnectTimeoutMilliseconds { get; set; }

        /// <summary>获取或设置单次协议读写的默认总超时，单位为毫秒；省略时为 5000。</summary>
        [DataMember(Name = "operationTimeoutMilliseconds", EmitDefaultValue = false)]
        public int? OperationTimeoutMilliseconds { get; set; }

        /// <summary>获取或设置发送超时时间，单位为毫秒。</summary>
        [DataMember(Name = "sendTimeoutMilliseconds", EmitDefaultValue = false)]
        public int? SendTimeoutMilliseconds { get; set; }

        /// <summary>获取或设置接收超时时间，单位为毫秒。</summary>
        [DataMember(Name = "receiveTimeoutMilliseconds", EmitDefaultValue = false)]
        public int? ReceiveTimeoutMilliseconds { get; set; }

        /// <summary>获取或设置通信失败后的重试次数。</summary>
        [DataMember(Name = "retries", EmitDefaultValue = false)]
        public int? Retries { get; set; }

        /// <summary>获取或设置两次重试之间的等待时间，单位为毫秒。</summary>
        [DataMember(Name = "waitToRetryMilliseconds", EmitDefaultValue = false)]
        public int? WaitToRetryMilliseconds { get; set; }

        /// <summary>获取或设置 Modbus 地址映射配置，例如 generic 或 inovance-easyplc。</summary>
        [DataMember(Name = "deviceProfile", EmitDefaultValue = false)]
        public string DeviceProfile { get; set; }

        /// <summary>获取或设置该设备的点位表路径；相对路径以 devices.json 所在目录为基准。</summary>
        [DataMember(Name = "pointsFile")]
        public string PointsFile { get; set; }

        /// <summary>获取或设置设备是否由 DeviceHost 自动启动；省略时默认为 true。</summary>
        [DataMember(Name = "enabled", EmitDefaultValue = false)]
        public bool? Enabled { get; set; }

        /// <summary>获取或设置自动批量读取周期，单位为毫秒；省略时 DeviceHost 不创建轮询订阅。</summary>
        [DataMember(Name = "pollingIntervalMilliseconds", EmitDefaultValue = false)]
        public int? PollingIntervalMilliseconds { get; set; }

        /// <summary>获取或设置轮询是否只在值或质量变化时上报；省略时默认为 false。</summary>
        [DataMember(Name = "reportOnChangeOnly", EmitDefaultValue = false)]
        public bool? ReportOnChangeOnly { get; set; }

        /// <summary>获取或设置断线后重连检查周期，单位为毫秒；省略时使用 3000。</summary>
        [DataMember(Name = "reconnectDelayMilliseconds", EmitDefaultValue = false)]
        public int? ReconnectDelayMilliseconds { get; set; }

        /// <summary>获取或设置 Siemens S7 CPU 型号，例如 S71200 或 S71500。</summary>
        [DataMember(Name = "cpuType", EmitDefaultValue = false)]
        public string CpuType { get; set; }

        /// <summary>获取或设置 Siemens S7 机架号。</summary>
        [DataMember(Name = "rack", EmitDefaultValue = false)]
        public short? Rack { get; set; }

        /// <summary>获取或设置 Siemens S7 插槽号。</summary>
        [DataMember(Name = "slot", EmitDefaultValue = false)]
        public short? Slot { get; set; }

        /// <summary>获取或设置 OPC UA 端点 URL，例如 opc.tcp://127.0.0.1:4840。</summary>
        [DataMember(Name = "endpointUrl", EmitDefaultValue = false)]
        public string EndpointUrl { get; set; }

        /// <summary>获取或设置 OPC UA 用户名；为空时使用匿名身份。</summary>
        [DataMember(Name = "username", EmitDefaultValue = false)]
        public string Username { get; set; }

        /// <summary>获取或设置 OPC UA 密码。</summary>
        [DataMember(Name = "password", EmitDefaultValue = false)]
        public string Password { get; set; }

        /// <summary>是否使用 OPC UA 安全端点；省略时为 false。</summary>
        [DataMember(Name = "useSecurity", EmitDefaultValue = false)]
        public bool? UseSecurity { get; set; }

        /// <summary>MQTT 客户端标识；为空时自动生成。</summary>
        [DataMember(Name = "clientId", EmitDefaultValue = false)]
        public string ClientId { get; set; }

        /// <summary>MQTT QoS 等级（0、1 或 2）。</summary>
        [DataMember(Name = "qos", EmitDefaultValue = false)]
        public int? Qos { get; set; }

        /// <summary>MQTT 发布时是否保留消息。</summary>
        [DataMember(Name = "retain", EmitDefaultValue = false)]
        public bool? Retain { get; set; }

        /// <summary>Redis 数据库编号。</summary>
        [DataMember(Name = "database", EmitDefaultValue = false)]
        public int? Database { get; set; }

        /// <summary>MQTT 或 Redis 是否启用 TLS。</summary>
        [DataMember(Name = "ssl", EmitDefaultValue = false)]
        public bool? Ssl { get; set; }

        /// <summary>将 pointsFile 转换为可直接读取的绝对路径。</summary>
        public string ResolvePointsFile(string configDirectory)
        {
            if (string.IsNullOrWhiteSpace(PointsFile))
            {
                throw new InvalidOperationException(string.Format("Device '{0}' does not define pointsFile.", Name));
            }

            if (Path.IsPathRooted(PointsFile))
            {
                return Path.GetFullPath(PointsFile);
            }

            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                throw new ArgumentException("Config directory cannot be null or empty.", nameof(configDirectory));
            }

            return Path.GetFullPath(Path.Combine(configDirectory, PointsFile));
        }
    }
}
