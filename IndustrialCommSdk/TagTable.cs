using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk
{
    public sealed class TagTable
    {
        private readonly Dictionary<string, IndustrialTag> _nameIndexes;
        private readonly Dictionary<string, IndustrialTag> _addressIndexes;

        public TagTable(IReadOnlyList<IndustrialTag> tags)
        {
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _nameIndexes = new Dictionary<string, IndustrialTag>(StringComparer.OrdinalIgnoreCase);
            _addressIndexes = new Dictionary<string, IndustrialTag>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in tags)
            {
                if (tag == null) throw new ArgumentException("Tag table cannot contain null tags.", nameof(tags));

                if (!string.IsNullOrWhiteSpace(tag.Name) && !_nameIndexes.ContainsKey(tag.Name))
                {
                    _nameIndexes.Add(tag.Name, tag);
                }

                if (!_addressIndexes.ContainsKey(tag.Address))
                {
                    _addressIndexes.Add(tag.Address, tag);
                }
            }
        }

        public IReadOnlyList<IndustrialTag> Tags { get; private set; }

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

        public static TagTable LoadJson(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Tag table file path cannot be null or empty.", nameof(filePath));

            using (var stream = File.OpenRead(filePath))
            {
                var dto = (TagTableDto)new DataContractJsonSerializer(typeof(TagTableDto)).ReadObject(stream);
                return FromDto(dto);
            }
        }

        public static TagTable FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Tag table JSON cannot be null or empty.", nameof(json));

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var dto = (TagTableDto)new DataContractJsonSerializer(typeof(TagTableDto)).ReadObject(stream);
                return FromDto(dto);
            }
        }

        public static TagTable LoadCsv(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Tag table file path cannot be null or empty.", nameof(filePath));

            return ParseCsv(File.ReadAllText(filePath, Encoding.UTF8));
        }

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
                    GetCell(row, headers, "Name", false)));
            }

            return new TagTable(tags);
        }

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

                tags.Add(CreateTag(tag.Address, tag.Type, tag.Length, tag.Name));
            }

            return new TagTable(tags);
        }

        private static IndustrialTag CreateTag(string address, string type, string length, string name)
        {
            ushort parsedLength;
            if (string.IsNullOrWhiteSpace(length))
            {
                parsedLength = 1;
            }
            else if (!ushort.TryParse(length, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedLength) || parsedLength == 0)
            {
                throw new FormatException(string.Format("Invalid tag length: {0}", length));
            }

            return new IndustrialTag(address, ParseDataType(type), parsedLength, string.IsNullOrWhiteSpace(name) ? null : name);
        }

        private static IndustrialTag CreateTag(string address, string type, ushort? length, string name)
        {
            return new IndustrialTag(address, ParseDataType(type), length.GetValueOrDefault(1), string.IsNullOrWhiteSpace(name) ? null : name);
        }

        private static DataType ParseDataType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("Tag type cannot be null or empty.", nameof(type));

            var normalized = type.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
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

        [DataContract]
        private sealed class TagTableDto
        {
            [DataMember(Name = "tags")]
            public List<TagDto> Tags { get; set; }
        }

        [DataContract]
        private sealed class TagDto
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "address")]
            public string Address { get; set; }

            [DataMember(Name = "type")]
            public string Type { get; set; }

            [DataMember(Name = "length")]
            public ushort? Length { get; set; }
        }
    }
}
