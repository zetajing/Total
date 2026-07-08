using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IndustrialCommSdk
{
    [DataContract]
    public sealed class IndustrialSdkConfig
    {
        [DataMember(Name = "devices")]
        public List<IndustrialDeviceConfig> Devices { get; set; } = new List<IndustrialDeviceConfig>();

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

    [DataContract]
    public sealed class IndustrialDeviceConfig
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "protocol")]
        public string Protocol { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "host")]
        public string Host { get; set; }

        [DataMember(Name = "port")]
        public int? Port { get; set; }

        [DataMember(Name = "slaveId")]
        public byte? SlaveId { get; set; }

        [DataMember(Name = "portName")]
        public string PortName { get; set; }

        [DataMember(Name = "baudRate")]
        public int? BaudRate { get; set; }

        [DataMember(Name = "dataBits")]
        public int? DataBits { get; set; }

        [DataMember(Name = "parity")]
        public string Parity { get; set; }

        [DataMember(Name = "stopBits")]
        public string StopBits { get; set; }

        [DataMember(Name = "readTimeout")]
        public int? ReadTimeout { get; set; }

        [DataMember(Name = "writeTimeout")]
        public int? WriteTimeout { get; set; }

        [DataMember(Name = "connectTimeoutMilliseconds")]
        public int? ConnectTimeoutMilliseconds { get; set; }

        [DataMember(Name = "sendTimeoutMilliseconds")]
        public int? SendTimeoutMilliseconds { get; set; }

        [DataMember(Name = "receiveTimeoutMilliseconds")]
        public int? ReceiveTimeoutMilliseconds { get; set; }

        [DataMember(Name = "retries")]
        public int? Retries { get; set; }

        [DataMember(Name = "waitToRetryMilliseconds")]
        public int? WaitToRetryMilliseconds { get; set; }

        [DataMember(Name = "deviceProfile")]
        public string DeviceProfile { get; set; }

        [DataMember(Name = "pointsFile")]
        public string PointsFile { get; set; }

        [DataMember(Name = "cpuType")]
        public string CpuType { get; set; }

        [DataMember(Name = "rack")]
        public short? Rack { get; set; }

        [DataMember(Name = "slot")]
        public short? Slot { get; set; }

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
