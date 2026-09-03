using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using InduLink;
using InduLink.Diagnostics;
using InduLink.Protocols.Modbus;
using InduLink.Runtime;
using InduLink.Runtime.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NJsonSchema;

namespace InduLinkDemo.Services
{
    public enum JsonConfigurationDocument
    {
        Devices,
        Points,
        ModbusProfiles,
        NetworkServices,
    }

    public sealed class JsonConfigurationValidationError
    {
        internal JsonConfigurationValidationError(
            JsonConfigurationDocument document,
            string path,
            string message,
            bool schemaError)
        {
            Document = document;
            Path = string.IsNullOrWhiteSpace(path) ? "$" : path;
            Message = message ?? string.Empty;
            IsSchemaError = schemaError;
        }

        public JsonConfigurationDocument Document { get; private set; }
        public string Path { get; private set; }
        public string Message { get; private set; }
        public bool IsSchemaError { get; private set; }
    }

    public sealed class JsonConfigurationValidationResult
    {
        private readonly List<JsonConfigurationValidationError> _errors =
            new List<JsonConfigurationValidationError>();

        public bool IsValid { get { return _errors.Count == 0; } }
        public IReadOnlyList<JsonConfigurationValidationError> Errors { get { return _errors.AsReadOnly(); } }

        internal void Add(
            JsonConfigurationDocument document,
            string path,
            string message,
            bool schemaError)
        {
            _errors.Add(new JsonConfigurationValidationError(document, path, message, schemaError));
        }

        public void Merge(JsonConfigurationValidationResult other)
        {
            if (other == null) return;
            _errors.AddRange(other._errors);
        }

        public string ToDisplayText(int maxErrors = 12)
        {
            if (IsValid) return "配置校验通过。";
            var lines = Errors.Take(Math.Max(1, maxErrors))
                .Select(error => string.Format(
                    "[{0}] {1}: {2}",
                    error.Document,
                    error.Path,
                    error.Message));
            var text = string.Join(Environment.NewLine, lines);
            if (Errors.Count > maxErrors)
                text += Environment.NewLine + "... 其余 " + (Errors.Count - maxErrors) + " 个错误已省略。";
            return text;
        }
    }

    /// <summary>
    /// Demo 应用的四类 JSON 配置校验和模板访问入口。
    /// Schema 校验只由界面调用；运行时配置加载仍使用原有路径。
    /// </summary>
    public sealed class JsonConfigurationValidationService
    {
        private static readonly JsonSerializerSettings NetworkJsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        private readonly IndustrialSdk _sdk;
        private readonly IIndustrialLogger _logger;
        private readonly Dictionary<JsonConfigurationDocument, JsonSchema> _schemas =
            new Dictionary<JsonConfigurationDocument, JsonSchema>();

        public JsonConfigurationValidationService(
            IndustrialSdk sdk,
            string configDirectory,
            IIndustrialLogger logger = null)
        {
            _sdk = sdk ?? throw new ArgumentNullException(nameof(sdk));
            ConfigDirectory = Path.GetFullPath(
                string.IsNullOrWhiteSpace(configDirectory)
                    ? throw new ArgumentException("Config directory cannot be empty.", nameof(configDirectory))
                    : configDirectory);
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public string ConfigDirectory { get; private set; }
        public string SchemaDirectory { get { return Path.Combine(ConfigDirectory, "Schemas"); } }
        public string TemplateDirectory { get { return Path.Combine(ConfigDirectory, "Templates"); } }

        public string LoadTemplate(JsonConfigurationDocument document)
        {
            var path = GetTemplatePath(document);
            if (!File.Exists(path)) throw new FileNotFoundException("JSON template was not found.", path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public JsonConfigurationValidationResult ValidateFile(
            JsonConfigurationDocument document,
            string filePath,
            bool checkReferencedFiles = true)
        {
            var result = new JsonConfigurationValidationResult();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.Add(document, "$", "配置文件路径不能为空。", false);
                return result;
            }

            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                result.Add(document, "$", "配置文件不存在：" + fullPath, false);
                return result;
            }

            result.Merge(Validate(
                document,
                File.ReadAllText(fullPath, Encoding.UTF8),
                Path.GetDirectoryName(fullPath),
                checkReferencedFiles));
            return result;
        }

        public JsonConfigurationValidationResult Validate(
            JsonConfigurationDocument document,
            string json,
            string baseDirectory = null,
            bool checkReferencedFiles = true)
        {
            var result = new JsonConfigurationValidationResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Add(document, "$", "JSON 内容不能为空。", false);
                return result;
            }

            JsonSchema schema;
            try
            {
                schema = GetSchema(document);
                foreach (var error in schema.Validate(json))
                    result.Add(document, error.Path, error.Kind.ToString(), true);
            }
            catch (Exception ex)
            {
                result.Add(document, "$", "JSON 或 Schema 解析失败：" + ex.Message, true);
                return result;
            }

            if (!result.IsValid) return result;

            try
            {
                ValidateSemantics(document, json, baseDirectory ?? ConfigDirectory, checkReferencedFiles, result);
            }
            catch (Exception ex)
            {
                result.Add(document, "$", "业务校验失败：" + ex.Message, false);
            }
            return result;
        }

        public JsonConfigurationValidationResult ValidateForSave(
            JsonConfigurationDocument document,
            string json,
            string baseDirectory = null)
        {
            return Validate(document, json, baseDirectory, false);
        }

        public JsonConfigurationValidationResult ValidateNetworkConfiguration(
            NetworkServicesConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return Validate(
                JsonConfigurationDocument.NetworkServices,
                JsonConvert.SerializeObject(configuration, NetworkJsonSettings),
                ConfigDirectory,
                false);
        }

        public JsonConfigurationValidationResult ValidateCurrent(
            string devicesJson,
            string pointsJson,
            string pointFilePath)
        {
            var result = ValidateForSave(JsonConfigurationDocument.Devices, devicesJson, ConfigDirectory);
            result.Merge(ValidateForSave(JsonConfigurationDocument.Points, pointsJson, ConfigDirectory));

            if (!string.IsNullOrWhiteSpace(pointFilePath))
            {
                var pointPath = Path.GetFullPath(pointFilePath);
                if (!IsInsideConfigDirectory(pointPath))
                    result.Add(JsonConfigurationDocument.Points, "$.pointsFile", "点位文件必须位于配置目录内。", false);
            }
            return result;
        }

        public JsonConfigurationValidationResult ValidateAll(
            string devicesJson,
            string currentPointFilePath,
            string currentPointJson)
        {
            var result = Validate(
                JsonConfigurationDocument.Devices,
                devicesJson,
                ConfigDirectory,
                true);

            IndustrialSdkConfig config = null;
            try { config = _sdk.ParseConfiguration(devicesJson); }
            catch { }

            var pointFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config != null && config.Devices != null)
            {
                foreach (var device in config.Devices.Where(item => item != null && !string.IsNullOrWhiteSpace(item.PointsFile)))
                {
                    try
                    {
                        var pointPath = device.ResolvePointsFile(ConfigDirectory);
                        if (!pointFiles.Add(pointPath)) continue;
                        if (!string.IsNullOrWhiteSpace(currentPointFilePath) &&
                            string.Equals(pointPath, Path.GetFullPath(currentPointFilePath), StringComparison.OrdinalIgnoreCase))
                        {
                            result.Merge(Validate(JsonConfigurationDocument.Points, currentPointJson, ConfigDirectory, false));
                        }
                        else
                        {
                            result.Merge(ValidateFile(JsonConfigurationDocument.Points, pointPath, false));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Add(JsonConfigurationDocument.Points, "$.pointsFile", ex.Message, false);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(currentPointJson))
            {
                result.Merge(Validate(JsonConfigurationDocument.Points, currentPointJson, ConfigDirectory, false));
            }

            result.Merge(ValidateFile(
                JsonConfigurationDocument.ModbusProfiles,
                Path.Combine(ConfigDirectory, "modbus-profiles.json"),
                false));
            result.Merge(ValidateFile(
                JsonConfigurationDocument.NetworkServices,
                Path.Combine(ConfigDirectory, "network-services.json"),
                false));
            return result;
        }

        private void ValidateSemantics(
            JsonConfigurationDocument document,
            string json,
            string baseDirectory,
            bool checkReferencedFiles,
            JsonConfigurationValidationResult result)
        {
            switch (document)
            {
                case JsonConfigurationDocument.Devices:
                    var config = _sdk.ParseConfiguration(json);
                    if (checkReferencedFiles)
                    {
                        foreach (var error in config.Validate(baseDirectory, _sdk.Protocols, _logger).Errors)
                            result.Add(document, "$", error, false);
                    }
                    else
                    {
                        ValidateDeviceSemantics(config, result);
                    }
                    break;
                case JsonConfigurationDocument.Points:
                    TagTable.FromJson(json);
                    break;
                case JsonConfigurationDocument.ModbusProfiles:
                    ValidateModbusProfiles(json, result);
                    break;
                case JsonConfigurationDocument.NetworkServices:
                    var network = JsonConvert.DeserializeObject<NetworkServicesConfiguration>(json);
                    NetworkServicesConfigurationStore.Validate(network);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(document), document, null);
            }
        }

        private void ValidateDeviceSemantics(
            IndustrialSdkConfig config,
            JsonConfigurationValidationResult result)
        {
            if (config.Devices == null || config.Devices.Count == 0)
            {
                result.Add(JsonConfigurationDocument.Devices, "$.devices", "至少需要配置一台设备。", false);
                return;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < config.Devices.Count; index++)
            {
                var device = config.Devices[index];
                var path = "$.devices[" + index + "]";
                if (device == null)
                {
                    result.Add(JsonConfigurationDocument.Devices, path, "设备不能为空。", false);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(device.Name))
                    result.Add(JsonConfigurationDocument.Devices, path + ".name", "设备名不能为空。", false);
                else if (!names.Add(device.Name))
                    result.Add(JsonConfigurationDocument.Devices, path + ".name", "设备名重复：" + device.Name, false);
                if (device.Runtime == null)
                {
                    result.Add(JsonConfigurationDocument.Devices, path + ".runtime", "运行参数不能为空。", false);
                }
                else
                {
                    if (device.Runtime.PollingIntervalMilliseconds <= 0)
                        result.Add(JsonConfigurationDocument.Devices, path + ".runtime.pollingIntervalMilliseconds", "必须大于 0。", false);
                    if (device.Runtime.ReconnectDelayMilliseconds <= 0)
                        result.Add(JsonConfigurationDocument.Devices, path + ".runtime.reconnectDelayMilliseconds", "必须大于 0。", false);
                    if (device.Runtime.OperationTimeoutMilliseconds <= 0)
                        result.Add(JsonConfigurationDocument.Devices, path + ".runtime.operationTimeoutMilliseconds", "必须大于 0。", false);
                }

                try
                {
                    var provider = _sdk.Protocols.Get(device.Protocol);
                    if (device.Settings == null)
                        result.Add(JsonConfigurationDocument.Devices, path + ".settings", "协议 settings 不能为空。", false);
                    else
                    {
                        foreach (var error in provider.Validate(device.Settings))
                            result.Add(JsonConfigurationDocument.Devices, path + ".settings", error, false);
                    }
                }
                catch (Exception ex)
                {
                    result.Add(JsonConfigurationDocument.Devices, path + ".protocol", ex.Message, false);
                }
            }
        }

        private static void ValidateModbusProfiles(
            string json,
            JsonConfigurationValidationResult result)
        {
            ModbusProfileDefinitionCollection collection;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                collection = (ModbusProfileDefinitionCollection)new DataContractJsonSerializer(
                    typeof(ModbusProfileDefinitionCollection)).ReadObject(stream);
            }

            if (collection == null || collection.Profiles == null || collection.Profiles.Count == 0)
            {
                result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles", "至少需要一个 Modbus profile。", false);
                return;
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in collection.Profiles)
            {
                if (profile == null)
                {
                    result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles", "profile 不能为空。", false);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(profile.Key))
                    result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].key", "key 不能为空。", false);
                else if (!keys.Add(profile.Key))
                    result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].key", "profile key 重复：" + profile.Key, false);

                try
                {
                    var regex = new Regex(profile.AddressPattern ?? string.Empty, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    if (regex.GetGroupNumbers().Length < 3)
                        result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].addressPattern", "必须包含 prefix 和 index 两个捕获组。", false);
                    new JsonModbusProfile(profile);
                }
                catch (Exception ex)
                {
                    result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].addressPattern", ex.Message, false);
                }

                var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (profile.Mappings == null || profile.Mappings.Count == 0)
                {
                    result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].mappings", "至少需要一个映射规则。", false);
                    continue;
                }
                foreach (var mapping in profile.Mappings)
                {
                    if (mapping == null) continue;
                    if (!prefixes.Add(mapping.Prefix ?? string.Empty))
                        result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].mappings[*].prefix", "映射前缀重复：" + mapping.Prefix, false);
                    if (mapping.Max <= 0)
                        result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].mappings[*].max", "必须大于 0。", false);
                    if ((long)mapping.Base + Math.Max(0, mapping.Max - 1) > ushort.MaxValue)
                        result.Add(JsonConfigurationDocument.ModbusProfiles, "$.profiles[*].mappings[*]", "base + max 超出 Modbus 地址范围。", false);
                }
            }
        }

        private JsonSchema GetSchema(JsonConfigurationDocument document)
        {
            JsonSchema schema;
            if (_schemas.TryGetValue(document, out schema)) return schema;
            var path = GetSchemaPath(document);
            if (!File.Exists(path)) throw new FileNotFoundException("JSON schema was not found.", path);
            schema = JsonSchema.FromJsonAsync(File.ReadAllText(path, Encoding.UTF8)).GetAwaiter().GetResult();
            _schemas.Add(document, schema);
            return schema;
        }

        private string GetSchemaPath(JsonConfigurationDocument document)
        {
            return Path.Combine(SchemaDirectory, GetDocumentName(document) + ".schema.json");
        }

        private string GetTemplatePath(JsonConfigurationDocument document)
        {
            return Path.Combine(TemplateDirectory, GetDocumentName(document) + ".template.json");
        }

        private static string GetDocumentName(JsonConfigurationDocument document)
        {
            switch (document)
            {
                case JsonConfigurationDocument.Devices: return "devices";
                case JsonConfigurationDocument.Points: return "points";
                case JsonConfigurationDocument.ModbusProfiles: return "modbus-profiles";
                case JsonConfigurationDocument.NetworkServices: return "network-services";
                default: throw new ArgumentOutOfRangeException(nameof(document), document, null);
            }
        }

        private bool IsInsideConfigDirectory(string path)
        {
            var root = ConfigDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
