using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IndustrialCommSdk.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IndustrialCommSdk.Runtime.Configuration
{
    public sealed class IndustrialConfigValidationResult
    {
        internal IndustrialConfigValidationResult(IReadOnlyList<string> errors) { Errors = errors; }
        public bool IsValid { get { return Errors.Count == 0; } }
        public IReadOnlyList<string> Errors { get; private set; }
    }

    public sealed class IndustrialDeviceRuntimeOptions
    {
        public int PollingIntervalMilliseconds { get; set; } = 1000;
        public int ReconnectDelayMilliseconds { get; set; } = 3000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
        public bool ReportOnChangeOnly { get; set; }
    }

    public sealed class IndustrialDeviceConfig
    {
        public string Name { get; set; }
        public string Protocol { get; set; }
        public string DeviceId { get; set; }
        public string PointsFile { get; set; }
        public bool Enabled { get; set; } = true;
        public IndustrialDeviceRuntimeOptions Runtime { get; set; } = new IndustrialDeviceRuntimeOptions();
        public IProtocolSettings Settings { get; set; }

        public string EffectiveDeviceId
        {
            get { return string.IsNullOrWhiteSpace(DeviceId) ? Name : DeviceId; }
        }

        public string ResolvePointsFile(string configDirectory)
        {
            if (string.IsNullOrWhiteSpace(configDirectory)) throw new ArgumentException("Config directory cannot be empty.", nameof(configDirectory));
            if (string.IsNullOrWhiteSpace(PointsFile)) throw new InvalidOperationException("pointsFile cannot be empty.");
            return Path.GetFullPath(Path.IsPathRooted(PointsFile) ? PointsFile : Path.Combine(configDirectory, PointsFile));
        }
    }

    public sealed class IndustrialSdkConfig
    {
        public List<IndustrialDeviceConfig> Devices { get; set; } = new List<IndustrialDeviceConfig>();

        public IndustrialDeviceConfig FindDevice(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Device name cannot be empty.", nameof(name));
            var device = Devices.FirstOrDefault(item => item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (device == null) throw new KeyNotFoundException("Device was not found: " + name);
            return device;
        }

        public IndustrialConfigValidationResult Validate(
            string configDirectory,
            IndustrialProtocolRegistry registry,
            IIndustrialLogger logger = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var errors = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Devices == null || Devices.Count == 0)
                errors.Add("devices 至少需要配置一台设备。");

            for (var index = 0; Devices != null && index < Devices.Count; index++)
            {
                var device = Devices[index];
                var label = "devices[" + index + "]";
                if (device == null) { errors.Add(label + " 不能为空。"); continue; }
                if (string.IsNullOrWhiteSpace(device.Name)) errors.Add(label + ".name 不能为空。");
                else if (!names.Add(device.Name)) errors.Add("设备名重复：" + device.Name);
                if (device.Runtime == null) errors.Add(label + ".runtime 不能为空。");
                else
                {
                    if (device.Runtime.PollingIntervalMilliseconds <= 0) errors.Add(label + ".runtime.pollingIntervalMilliseconds 必须大于 0。");
                    if (device.Runtime.ReconnectDelayMilliseconds <= 0) errors.Add(label + ".runtime.reconnectDelayMilliseconds 必须大于 0。");
                    if (device.Runtime.OperationTimeoutMilliseconds <= 0) errors.Add(label + ".runtime.operationTimeoutMilliseconds 必须大于 0。");
                }

                IIndustrialProtocolProvider provider;
                if (!registry.TryGet(device.Protocol, out provider))
                {
                    errors.Add(label + ".protocol 不受支持：" + device.Protocol);
                }
                else if (device.Settings == null)
                {
                    errors.Add(label + ".settings 不能为空。");
                }
                else if (device.Settings.GetType() != provider.SettingsType)
                {
                    errors.Add(label + ".settings 类型与协议不匹配。");
                }
                else
                {
                    foreach (var error in provider.Validate(device.Settings)) errors.Add(label + ".settings: " + error);
                }

                try
                {
                    var pointsPath = device.ResolvePointsFile(configDirectory);
                    if (!File.Exists(pointsPath)) errors.Add("设备 '" + device.Name + "' 的点位文件不存在：" + pointsPath);
                    else TagTable.Load(pointsPath);
                }
                catch (Exception ex) { errors.Add("设备 '" + device.Name + "' 点位表错误：" + ex.Message); }
            }
            return new IndustrialConfigValidationResult(errors.AsReadOnly());
        }
    }

    public sealed class IndustrialConfigurationSerializer
    {
        private readonly IndustrialProtocolRegistry _registry;
        private readonly JsonSerializer _serializer;

        public IndustrialConfigurationSerializer(IndustrialProtocolRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Converters = new List<JsonConverter> { new StringEnumConverter() },
            });
        }

        public IndustrialSdkConfig Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Config path cannot be empty.", nameof(filePath));
            return Parse(File.ReadAllText(filePath, Encoding.UTF8));
        }

        public IndustrialSdkConfig Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Config JSON cannot be empty.", nameof(json));
            var root = JObject.Parse(json);
            var devicesToken = root["devices"] as JArray;
            if (devicesToken == null) throw new JsonSerializationException("Root property 'devices' must be an array.");
            var result = new IndustrialSdkConfig();
            foreach (var token in devicesToken.OfType<JObject>()) result.Devices.Add(ParseDevice(token));
            if (result.Devices.Count != devicesToken.Count) throw new JsonSerializationException("devices cannot contain null or non-object values.");
            return result;
        }

        public string Serialize(IndustrialSdkConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var devices = new JArray();
            foreach (var device in config.Devices ?? new List<IndustrialDeviceConfig>()) devices.Add(SerializeDevice(device));
            return new JObject { ["devices"] = devices }.ToString(Formatting.Indented);
        }

        public void Save(IndustrialSdkConfig config, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Config path cannot be empty.", nameof(filePath));
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, Serialize(config), new UTF8Encoding(false));
        }

        public string SerializeSettings(IProtocolSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return JObject.FromObject(settings, _serializer).ToString(Formatting.Indented);
        }

        public IProtocolSettings ParseSettings(string protocol, string json)
        {
            var provider = _registry.Get(protocol);
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Settings JSON cannot be empty.", nameof(json));
            var token = JObject.Parse(json);
            return DeserializeSettings(provider, token);
        }

        private IndustrialDeviceConfig ParseDevice(JObject token)
        {
            var protocol = RequiredText(token, "protocol");
            var provider = _registry.Get(protocol);
            var runtimeToken = token["runtime"] as JObject;
            var settingsToken = token["settings"] as JObject;
            if (runtimeToken == null) throw new JsonSerializationException("Device runtime must be an object.");
            if (settingsToken == null) throw new JsonSerializationException("Device settings must be an object.");
            return new IndustrialDeviceConfig
            {
                Name = RequiredText(token, "name"),
                Protocol = protocol,
                DeviceId = OptionalText(token, "deviceId"),
                PointsFile = RequiredText(token, "pointsFile"),
                Enabled = token.Value<bool?>("enabled") ?? true,
                Runtime = runtimeToken.ToObject<IndustrialDeviceRuntimeOptions>(_serializer) ?? new IndustrialDeviceRuntimeOptions(),
                Settings = DeserializeSettings(provider, settingsToken),
            };
        }

        private JObject SerializeDevice(IndustrialDeviceConfig device)
        {
            if (device == null) throw new JsonSerializationException("devices cannot contain null values.");
            var provider = _registry.Get(device.Protocol);
            EnsureSettingsType(provider, device.Settings);
            var result = new JObject
            {
                ["name"] = device.Name,
                ["protocol"] = device.Protocol,
            };
            if (!string.IsNullOrWhiteSpace(device.DeviceId)) result["deviceId"] = device.DeviceId;
            result["pointsFile"] = device.PointsFile;
            result["enabled"] = device.Enabled;
            result["runtime"] = JObject.FromObject(device.Runtime ?? new IndustrialDeviceRuntimeOptions(), _serializer);
            result["settings"] = JObject.FromObject(device.Settings, _serializer);
            return result;
        }

        private IProtocolSettings DeserializeSettings(IIndustrialProtocolProvider provider, JObject token)
        {
            var settings = token.ToObject(provider.SettingsType, _serializer) as IProtocolSettings;
            EnsureSettingsType(provider, settings);
            return settings;
        }

        private static void EnsureSettingsType(IIndustrialProtocolProvider provider, IProtocolSettings settings)
        {
            if (settings == null || settings.GetType() != provider.SettingsType)
                throw new JsonSerializationException("Settings type does not match protocol '" + provider.Protocol + "'.");
        }

        private static string RequiredText(JObject token, string name)
        {
            var value = OptionalText(token, name);
            if (string.IsNullOrWhiteSpace(value)) throw new JsonSerializationException("Device property '" + name + "' is required.");
            return value;
        }

        private static string OptionalText(JObject token, string name)
        {
            var value = token.Value<string>(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
