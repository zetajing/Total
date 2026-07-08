using System;
using System.Collections.Generic;
using System.Globalization;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;
using IndustrialCommDemo.Helpers;

namespace IndustrialCommDemo
{
    /// <summary>订阅结果表格的一行，保存已格式化的值、质量和时间。</summary>
    internal sealed class SubscriptionDisplayRow
    {
        public string Address { get; set; }
        public string DataType { get; set; }
        public string ValueText { get; set; }
        public string QualityText { get; set; }
        public string TimestampText { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>数据库历史记录表格的一行。</summary>
    internal sealed class DatabaseHistoryDisplayRow
    {
        public long Id { get; set; }
        public string Protocol { get; set; }
        public string DeviceId { get; set; }
        public string Address { get; set; }
        public string DataType { get; set; }
        public string ValueText { get; set; }
        public string Quality { get; set; }
        public string Timestamp { get; set; }
        public string ErrorMessage { get; set; }

        // 集中完成枚举、时间和空值格式化，避免 XAML 中重复转换。
        public static DatabaseHistoryDisplayRow FromRecord(IndustrialDataRecord record)
        {
            return new DatabaseHistoryDisplayRow
            {
                Id = record.Id,
                Protocol = record.Protocol.ToString(),
                DeviceId = record.DeviceId,
                Address = record.Address,
                DataType = record.DataType.ToString(),
                ValueText = record.ValueText ?? string.Empty,
                Quality = FormatHelper.FormatQualityLabel(record.Quality),
                Timestamp = record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                ErrorMessage = record.ErrorMessage ?? string.Empty,
            };
        }
    }

    /// <summary>保存 S7 地址规范化结果及由地址推断的数据类型。</summary>
    internal sealed class S7AddressInputInfo
    {
        public S7AddressInputInfo(string normalizedAddress, DataType? inferredDataType, ushort? inferredLength)
        {
            NormalizedAddress = normalizedAddress;
            InferredDataType = inferredDataType;
            InferredLength = inferredLength;
        }

        public string NormalizedAddress { get; private set; }
        public DataType? InferredDataType { get; private set; }
        public ushort? InferredLength { get; private set; }
    }

    /// <summary>保存批量 Modbus 地址输入的解析和校验结果。</summary>
    internal sealed class ModbusAddressInputAnalysis
    {
        public List<string> Addresses { get; private set; }
        public bool IsValid { get; private set; }
        public bool IsBitFamily { get; private set; }
        public string ErrorMessage { get; private set; }
        public int AddressCount { get { return Addresses == null ? 0 : Addresses.Count; } }

        public static ModbusAddressInputAnalysis Empty()
        {
            return new ModbusAddressInputAnalysis { Addresses = new List<string>(), IsValid = true };
        }

        public static ModbusAddressInputAnalysis Valid(List<string> addresses, bool isBitFamily)
        {
            return new ModbusAddressInputAnalysis
            {
                Addresses = addresses,
                IsValid = true,
                IsBitFamily = isBitFamily,
            };
        }

        public static ModbusAddressInputAnalysis Invalid(List<string> addresses, string errorMessage)
        {
            return new ModbusAddressInputAnalysis
            {
                Addresses = addresses ?? new List<string>(),
                IsValid = false,
                ErrorMessage = errorMessage,
            };
        }
    }
}
