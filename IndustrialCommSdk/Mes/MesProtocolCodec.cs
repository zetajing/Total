using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IndustrialCommSdk.Mes
{
    internal static class MesProtocolCodec
    {
        private static readonly DataContractJsonSerializerSettings JsonSettings =
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };

        public static string CreateOnline(MesClientOptions options, DateTimeOffset now)
        {
            return string.Format("START {0},{1},{2},{3},{4}STOP", options.DeviceNo, options.DeviceName,
                options.DeviceIp, options.DeviceMac, now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        public static string Serialize<T>(T message)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T), JsonSettings).WriteObject(stream, message);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T), JsonSettings).ReadObject(stream);
        }

        public static string ReadType(string json)
        {
            var header = Deserialize<MesTypeHeader>(json);
            return header == null ? null : header.Type;
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class MesTypeHeader
        {
            [System.Runtime.Serialization.DataMember(Name = "type")]
            public string Type { get; set; }
        }
    }

    /// <summary>从连续字符流提取完整 JSON 对象，正确处理字符串、转义字符、拆包和粘包。</summary>
    internal sealed class MesJsonFrameParser
    {
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly int _maximumCharacters;

        public MesJsonFrameParser(int maximumCharacters) { _maximumCharacters = maximumCharacters; }

        public System.Collections.Generic.IReadOnlyList<string> Append(string text)
        {
            var frames = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(text)) _buffer.Append(text);
            while (true)
            {
                var start = IndexOf('{');
                if (start < 0)
                {
                    if (_buffer.Length > _maximumCharacters) _buffer.Clear();
                    break;
                }
                if (start > 0) _buffer.Remove(0, start);
                var end = FindObjectEnd();
                if (end < 0)
                {
                    if (_buffer.Length > _maximumCharacters)
                        throw new InvalidOperationException("MES message exceeds the configured maximum size.");
                    break;
                }
                frames.Add(_buffer.ToString(0, end + 1));
                _buffer.Remove(0, end + 1);
            }
            return frames;
        }

        private int IndexOf(char value)
        {
            for (var i = 0; i < _buffer.Length; i++) if (_buffer[i] == value) return i;
            return -1;
        }

        private int FindObjectEnd()
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = 0; i < _buffer.Length; i++)
            {
                var c = _buffer[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
            return -1;
        }
    }
}
