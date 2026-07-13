using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using IndustrialCommSdk.Mes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IndustrialCommDemo.Helpers
{
    internal static class MesDemoJson
    {
        private const string DefaultBaseUrl = "http://127.0.0.1:8080/api";

        public static MesHttpClientOptions ParseConfiguration(string json)
        {
            ValidateObject(json, "MES HTTP 配置");
            MesHttpDemoConfiguration config;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    config = (MesHttpDemoConfiguration)new DataContractJsonSerializer(
                        typeof(MesHttpDemoConfiguration)).ReadObject(stream);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("MES HTTP 配置 JSON 无法解析。", ex);
            }

            if (config == null) throw new InvalidOperationException("MES HTTP 配置不能为空。");
            if (string.IsNullOrWhiteSpace(config.BaseUrl))
                throw new InvalidOperationException("配置字段 baseUrl 不能为空。");
            if (string.IsNullOrWhiteSpace(config.Endpoint))
                throw new InvalidOperationException("配置字段 endpoint 不能为空。");
            if (!config.TimeoutMilliseconds.HasValue || !config.MaxRetries.HasValue ||
                !config.RetryDelayMilliseconds.HasValue || !config.MaxResponseContentBytes.HasValue)
                throw new InvalidOperationException("配置必须包含 timeoutMilliseconds、maxRetries、retryDelayMilliseconds 和 maxResponseContentBytes。");

            return new MesHttpClientOptions
            {
                BaseUrl = config.BaseUrl.Trim(),
                TimeoutMilliseconds = config.TimeoutMilliseconds.Value,
                MaxRetries = config.MaxRetries.Value,
                RetryDelayMilliseconds = config.RetryDelayMilliseconds.Value,
                MaxResponseContentBytes = config.MaxResponseContentBytes.Value,
            };
        }

        public static string ParseEndpoint(string json)
        {
            ParseConfiguration(json);
            var endpoint = (string)JObject.Parse(json)["endpoint"];
            return endpoint.Trim();
        }

        public static string FormatObject(string json)
        {
            ValidateObject(json, "JSON");
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            using (var output = new MemoryStream())
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                input, Encoding.UTF8, XmlDictionaryReaderQuotas.Max, null))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(
                output, Encoding.UTF8, false, true, "  "))
            {
                writer.WriteNode(reader, true);
                writer.Flush();
                return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
            }
        }

        public static string CreateDefaultConfiguration()
        {
            var config = new MesHttpDemoConfiguration
            {
                BaseUrl = DefaultBaseUrl,
                Endpoint = "/upload",
                TimeoutMilliseconds = 5000,
                MaxRetries = 2,
                RetryDelayMilliseconds = 500,
                MaxResponseContentBytes = 1024 * 1024,
            };
            return SerializePretty(config);
        }

        public static string CreateDefaultRequest()
        {
            return new JObject
            {
                ["sample"] = "value",
            }.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static void ValidateObject(string json, string label)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException(label + "不能为空。");
            if (json.TrimStart()[0] != '{') throw new InvalidOperationException(label + "必须以 JSON 对象作为根节点。");
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(
                    stream, Encoding.UTF8, XmlDictionaryReaderQuotas.Max, null))
                {
                    while (reader.Read()) { }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(label + "格式无效。", ex);
            }
        }

        private static string SerializePretty<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(
                    typeof(T),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true })
                    .WriteObject(stream, value);
                var json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
                return FormatObject(json);
            }
        }

        [DataContract]
        private sealed class MesHttpDemoConfiguration
        {
            [DataMember(Name = "baseUrl", Order = 1)] public string BaseUrl { get; set; }
            [DataMember(Name = "endpoint", Order = 2)] public string Endpoint { get; set; }
            [DataMember(Name = "timeoutMilliseconds", Order = 3)] public int? TimeoutMilliseconds { get; set; }
            [DataMember(Name = "maxRetries", Order = 4)] public int? MaxRetries { get; set; }
            [DataMember(Name = "retryDelayMilliseconds", Order = 5)] public int? RetryDelayMilliseconds { get; set; }
            [DataMember(Name = "maxResponseContentBytes", Order = 6)] public int? MaxResponseContentBytes { get; set; }
        }
    }
}
