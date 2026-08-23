using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Runtime
{
    /// <summary>
    /// 表示可按名称或地址查询的点位表，并提供 JSON、CSV 加载入口。
    /// </summary>
    public sealed class TagTable
    {
        private readonly Dictionary<string, IndustrialTag> _nameIndexes;
        private readonly Dictionary<string, IndustrialTag> _addressIndexes;

        /// <summary>使用给定点位集合创建点位表。</summary>
        public TagTable(IReadOnlyList<IndustrialTag> tags)
        {
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _nameIndexes = new Dictionary<string, IndustrialTag>(StringComparer.OrdinalIgnoreCase);
            _addressIndexes = new Dictionary<string, IndustrialTag>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in tags)
            {
                if (tag == null) throw new ArgumentException("Tag table cannot contain null tags.", nameof(tags));

                if (!string.IsNullOrWhiteSpace(tag.Name))
                {
                    if (_nameIndexes.ContainsKey(tag.Name))
                        throw new ArgumentException(
                            string.Format("Tag name '{0}' is duplicated. Named tags must be unique (case-insensitive).", tag.Name),
                            nameof(tags));
                    _nameIndexes.Add(tag.Name, tag);
                }

                if (!_addressIndexes.ContainsKey(tag.Address))
                {
                    _addressIndexes.Add(tag.Address, tag);
                }
            }
        }

        /// <summary>获取点位表中按原始顺序保存的全部点位。</summary>
        public IReadOnlyList<IndustrialTag> Tags { get; private set; }

        /// <summary>根据文件扩展名自动加载 JSON 或 CSV 点位表。</summary>
        public static TagTable Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Tag table file path cannot be null or empty.", nameof(filePath));

            var extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                return LoadJson(filePath);
            }

            return LoadCsv(filePath);
        }

        /// <summary>从 JSON 文件加载点位表。</summary>
        public static TagTable LoadJson(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Tag table file path cannot be null or empty.", nameof(filePath));

            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var dto = (TagTableDto)new DataContractJsonSerializer(typeof(TagTableDto)).ReadObject(stream);
                    return FromDto(dto);
                }
            }
            catch (SerializationException ex)
            {
                throw new FormatException(BuildJsonFormatMessage(filePath), ex);
            }
        }

        /// <summary>从 JSON 文本解析点位表。</summary>
        public static TagTable FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Tag table JSON cannot be null or empty.", nameof(json));

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var dto = (TagTableDto)new DataContractJsonSerializer(typeof(TagTableDto)).ReadObject(stream);
                    return FromDto(dto);
                }
            }
            catch (SerializationException ex)
            {
                throw new FormatException(BuildJsonFormatMessage("points JSON"), ex);
            }
        }

        /// <summary>将点位表序列化为便于人工维护的 JSON 文本。</summary>
        public string ToJson()
        {
            var dto = new TagTableDto
            {
                Tags = Tags.Select(tag => new TagDto
                {
                    Name = tag.Name,
                    Address = tag.Address,
                    Type = SerializeTagType(tag),
                    Length = tag.DataType == DataType.S7String
                        ? (ushort?)null
                        : tag.Length == 1 ? (ushort?)null : tag.Length,
                    Writable = tag.Writable ? (bool?)true : null,
                }).ToList(),
            };

            using (var stream = new MemoryStream())
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true))
            {
                new DataContractJsonSerializer(typeof(TagTableDto)).WriteObject(writer, dto);
                writer.Flush();
                return Encoding.UTF8.GetString(stream.ToArray()).Replace("\\/", "/");
            }
        }

        /// <summary>将点位表保存为 UTF-8 JSON 文件。</summary>
        public void SaveJson(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Tag table file path cannot be null or empty.", nameof(filePath));
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, ToJson(), new UTF8Encoding(false));
        }

        /// <summary>从 UTF-8 CSV 文件加载点位表。</summary>
        public static TagTable LoadCsv(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Tag table file path cannot be null or empty.", nameof(filePath));

            return ParseCsv(File.ReadAllText(filePath, Encoding.UTF8));
        }

        /// <summary>从 CSV 文本解析点位表，支持带引号和逗号的字段。</summary>
        public static TagTable ParseCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) throw new ArgumentException("Tag table CSV cannot be null or empty.", nameof(csv));

            var rows = ParseCsvRows(csv);
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("Tag table CSV is empty.");
            }

            var headers = BuildHeaderIndexes(rows[0]);
            var tags = new List<IndustrialTag>();
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Count == 0 || IsBlankRow(row))
                {
                    continue;
                }

                tags.Add(CreateTag(
                    GetCell(row, headers, "Address", true),
                    GetCell(row, headers, "Type", true),
                    GetCell(row, headers, "Length", false),
                    GetCell(row, headers, "Name", false),
                    GetCell(row, headers, "Writable", false)));
            }

            return new TagTable(tags);
        }

        /// <summary>按点位名称查找，名称匹配不区分大小写。</summary>
        public IndustrialTag Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tag name cannot be null or empty.", nameof(name));

            IndustrialTag tag;
            if (_nameIndexes.TryGetValue(name, out tag))
            {
                return tag;
            }

            throw new KeyNotFoundException(string.Format("Tag '{0}' was not found.", name));
        }

        /// <summary>按设备地址查找，地址匹配不区分大小写。</summary>
        public IndustrialTag GetByAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Tag address cannot be null or empty.", nameof(address));

            IndustrialTag tag;
            if (_addressIndexes.TryGetValue(address, out tag))
            {
                return tag;
            }

            throw new KeyNotFoundException(string.Format("Tag address '{0}' was not found.", address));
        }

        private static TagTable FromDto(TagTableDto dto)
        {
            if (dto == null || dto.Tags == null)
            {
                throw new InvalidOperationException("Tag table JSON must contain a tags array.");
            }

            var tags = new List<IndustrialTag>(dto.Tags.Count);
            foreach (var tag in dto.Tags)
            {
                if (tag == null)
                {
                    continue;
                }

                tags.Add(CreateTag(tag.Address, tag.Type, tag.Length, tag.Name, tag.Writable.GetValueOrDefault()));
            }

            return new TagTable(tags);
        }

        private static IndustrialTag CreateTag(string address, string type, string length, string name, string writable)
        {
            ushort? parsedLength = null;
            if (string.IsNullOrWhiteSpace(length))
            {
                parsedLength = null;
            }
            else
            {
                ushort value;
                if (!ushort.TryParse(length, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value == 0)
                    throw new FormatException(string.Format("Invalid tag length: {0}", length));
                parsedLength = value;
            }

            return CreateTag(address, type, parsedLength, name, ParseWritable(writable));
        }

        private static IndustrialTag CreateTag(string address, string type, ushort? length, string name, bool writable)
        {
            ushort? inlineLength;
            var dataType = ParseDataType(type, out inlineLength);
            if (inlineLength.HasValue && length.HasValue && inlineLength.Value != length.Value)
                throw new FormatException(string.Format(
                    "Tag type length {0} does not match explicit length {1}.",
                    inlineLength.Value,
                    length.Value));

            return new IndustrialTag(
                address,
                dataType,
                inlineLength ?? length.GetValueOrDefault(1),
                string.IsNullOrWhiteSpace(name) ? null : name,
                writable);
        }

        private static bool ParseWritable(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var normalized = value.Trim();
            if (string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool parsed;
            if (bool.TryParse(normalized, out parsed)) return parsed;
            throw new FormatException("Invalid tag writable value: " + value);
        }

        private static string SerializeTagType(IndustrialTag tag)
        {
            if (tag.DataType == DataType.S7String)
                return string.Format(CultureInfo.InvariantCulture, "STRING[{0}]", tag.Length);
            return tag.DataType.ToString();
        }

        private static DataType ParseDataType(string type, out ushort? inlineLength)
        {
            if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("Tag type cannot be null or empty.", nameof(type));

            inlineLength = null;
            var normalized = type.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            if (normalized.StartsWith("string[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
            {
                var lengthText = normalized.Substring(7, normalized.Length - 8);
                ushort length;
                if (!ushort.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) ||
                    length == 0 || length > 254)
                {
                    throw new ArgumentException("S7 STRING[n] length must be between 1 and 254.", nameof(type));
                }

                inlineLength = length;
                return DataType.S7String;
            }

            switch (normalized)
            {
                case "bool": return DataType.Bool;
                case "int16":
                case "short": return DataType.Int16;
                case "uint16":
                case "ushort":
                case "word": return DataType.UInt16;
                case "int32":
                case "int":
                case "dint": return DataType.Int32;
                case "uint32":
                case "uint":
                case "dword": return DataType.UInt32;
                case "float":
                case "real": return DataType.Float;
                case "double":
                case "lreal": return DataType.Double;
                case "byte": return DataType.Byte;
                case "char": return DataType.Char;
                case "string": return DataType.String;
                case "s7string": return DataType.S7String;
                case "bytes":
                case "bytearray": return DataType.ByteArray;
                default:
                    DataType parsed;
                    if (Enum.TryParse(type, true, out parsed))
                    {
                        return parsed;
                    }

                    throw new ArgumentException(string.Format("Unsupported tag type: {0}", type), nameof(type));
            }
        }

        private static Dictionary<string, int> BuildHeaderIndexes(IReadOnlyList<string> headers)
        {
            var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i] == null ? string.Empty : headers[i].Trim();
                if (!string.IsNullOrWhiteSpace(header) && !indexes.ContainsKey(header))
                {
                    indexes.Add(header, i);
                }
            }

            return indexes;
        }

        private static string GetCell(IReadOnlyList<string> row, Dictionary<string, int> headers, string name, bool required)
        {
            int index;
            if (!headers.TryGetValue(name, out index) || index >= row.Count)
            {
                if (required)
                {
                    throw new InvalidOperationException(string.Format("Tag table CSV is missing required column '{0}'.", name));
                }

                return null;
            }

            return row[index];
        }

        private static bool IsBlankRow(IReadOnlyList<string> row)
        {
            foreach (var cell in row)
            {
                if (!string.IsNullOrWhiteSpace(cell))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<List<string>> ParseCsvRows(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(cell.ToString().Trim());
                    cell.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    {
                        i++;
                    }

                    row.Add(cell.ToString().Trim());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else
                {
                    cell.Append(c);
                }
            }

            row.Add(cell.ToString().Trim());
            if (!IsBlankRow(row))
            {
                rows.Add(row);
            }

            return rows;
        }

        private static string BuildJsonFormatMessage(string source)
        {
            return string.Format(
                "{0} 格式错误。请检查 tags 数组中每个点位对象之间是否有英文逗号，并且最后一个点位对象后面不要带尾逗号。",
                source);
        }

        [DataContract]
        private sealed class TagTableDto
        {
            [DataMember(Name = "tags")]
            public List<TagDto> Tags { get; set; }
        }

        [DataContract]
        private sealed class TagDto
        {
            [DataMember(Name = "name", EmitDefaultValue = false)]
            public string Name { get; set; }

            [DataMember(Name = "address")]
            public string Address { get; set; }

            [DataMember(Name = "type")]
            public string Type { get; set; }

            [DataMember(Name = "length", EmitDefaultValue = false)]
            public ushort? Length { get; set; }

            [DataMember(Name = "writable", EmitDefaultValue = false)]
            public bool? Writable { get; set; }
        }
    }
}
