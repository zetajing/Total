using System;
using System.Collections.Generic;
using System.Globalization;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;

namespace IndustrialCommDemo
{
    internal sealed class SubscriptionDisplayRow
    {
        public string Address { get; set; }
        public string DataType { get; set; }
        public string ValueText { get; set; }
        public string QualityText { get; set; }
        public string TimestampText { get; set; }
        public string ErrorMessage { get; set; }
    }

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
                Quality = FormatQuality(record.Quality),
                Timestamp = record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                ErrorMessage = record.ErrorMessage ?? string.Empty,
            };
        }

        private static string FormatQuality(QualityStatus quality)
        {
            switch (quality)
            {
                case QualityStatus.Good: return "正常 (Good)";
                case QualityStatus.Bad: return "失败 (Bad)";
                case QualityStatus.Stale: return "过期 (Stale)";
                default: return "未知 (Unknown)";
            }
        }
    }

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
