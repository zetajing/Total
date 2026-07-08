using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IndustrialCommSdk
{
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
        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        /// <summary>获取或设置 TCP 设备的 IP 地址或主机名。</summary>
        [DataMember(Name = "host")]
        public string Host { get; set; }

        /// <summary>获取或设置 TCP 端口；为空时使用对应协议默认值。</summary>
        [DataMember(Name = "port")]
        public int? Port { get; set; }

        /// <summary>获取或设置 Modbus 从站地址。</summary>
        [DataMember(Name = "slaveId")]
        public byte? SlaveId { get; set; }

        /// <summary>获取或设置 Modbus RTU 串口名称，例如 COM3。</summary>
        [DataMember(Name = "portName")]
        public string PortName { get; set; }

        /// <summary>获取或设置串口波特率。</summary>
        [DataMember(Name = "baudRate")]
        public int? BaudRate { get; set; }

        /// <summary>获取或设置串口数据位。</summary>
        [DataMember(Name = "dataBits")]
        public int? DataBits { get; set; }

        /// <summary>获取或设置串口校验方式，例如 None、Even 或 Odd。</summary>
        [DataMember(Name = "parity")]
        public string Parity { get; set; }

        /// <summary>获取或设置串口停止位，例如 One 或 Two。</summary>
        [DataMember(Name = "stopBits")]
        public string StopBits { get; set; }

        /// <summary>获取或设置串口读取超时，单位为毫秒。</summary>
        [DataMember(Name = "readTimeout")]
        public int? ReadTimeout { get; set; }

        /// <summary>获取或设置串口写入超时，单位为毫秒。</summary>
        [DataMember(Name = "writeTimeout")]
        public int? WriteTimeout { get; set; }

        /// <summary>获取或设置建立连接的超时时间，单位为毫秒。</summary>
        [DataMember(Name = "connectTimeoutMilliseconds")]
        public int? ConnectTimeoutMilliseconds { get; set; }

        /// <summary>获取或设置发送超时时间，单位为毫秒。</summary>
        [DataMember(Name = "sendTimeoutMilliseconds")]
        public int? SendTimeoutMilliseconds { get; set; }

        /// <summary>获取或设置接收超时时间，单位为毫秒。</summary>
        [DataMember(Name = "receiveTimeoutMilliseconds")]
        public int? ReceiveTimeoutMilliseconds { get; set; }

        /// <summary>获取或设置通信失败后的重试次数。</summary>
        [DataMember(Name = "retries")]
        public int? Retries { get; set; }

        /// <summary>获取或设置两次重试之间的等待时间，单位为毫秒。</summary>
        [DataMember(Name = "waitToRetryMilliseconds")]
        public int? WaitToRetryMilliseconds { get; set; }

        /// <summary>获取或设置 Modbus 地址映射配置，例如 generic 或 inovance-easyplc。</summary>
        [DataMember(Name = "deviceProfile")]
        public string DeviceProfile { get; set; }

        /// <summary>获取或设置该设备的点位表路径；相对路径以 devices.json 所在目录为基准。</summary>
        [DataMember(Name = "pointsFile")]
        public string PointsFile { get; set; }

        /// <summary>获取或设置 Siemens S7 CPU 型号，例如 S71200 或 S71500。</summary>
        [DataMember(Name = "cpuType")]
        public string CpuType { get; set; }

        /// <summary>获取或设置 Siemens S7 机架号。</summary>
        [DataMember(Name = "rack")]
        public short? Rack { get; set; }

        /// <summary>获取或设置 Siemens S7 插槽号。</summary>
        [DataMember(Name = "slot")]
        public short? Slot { get; set; }

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
