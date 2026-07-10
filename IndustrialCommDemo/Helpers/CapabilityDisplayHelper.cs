using System;
using System.Collections.Generic;
using System.Globalization;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommDemo.Helpers
{
    /// <summary>
    /// Formats protocol capability metadata for display in Demo protocol tabs.
    /// </summary>
    internal static class CapabilityDisplayHelper
    {
        public static string Format(ProtocolCapabilities capabilities)
        {
            if (capabilities == null)
            {
                return "协议能力：未知。";
            }

            var tags = new List<string>();
            if (capabilities.SupportsRead) tags.Add("读");
            if (capabilities.SupportsWrite) tags.Add("写");
            if (capabilities.SupportsBatchRead) tags.Add("批量读");
            if (capabilities.SupportsBatchWrite) tags.Add("批量写");
            if (capabilities.SupportsOptimizedBatchRead) tags.Add("优化批量读");
            if (capabilities.SupportsSubscriptions) tags.Add("轮询订阅");
            if (capabilities.SupportsBitAddress) tags.Add("位地址");
            if (capabilities.SupportsString) tags.Add("字符串");
            if (capabilities.SupportsByteArray) tags.Add("ByteArray");
            if (capabilities.SupportsRawTransport) tags.Add("原始传输");
            if (capabilities.SupportsConnectionDiagnostics) tags.Add("连接诊断");
            if (capabilities.SupportsNativeAsync) tags.Add("原生异步");

            var pduText = capabilities.MaxPduBytes > 0
                ? capabilities.MaxPduBytes.ToString(CultureInfo.InvariantCulture) + " bytes"
                : "未限制/不适用";

            return string.Format(
                CultureInfo.InvariantCulture,
                "协议能力：{0} | {1}\n限制：最大读 {2} 项，最大写 {3} 项，最大地址跨度 {4}，PDU {5}，建议轮询 ≥ {6:0} ms，默认超时 {7:0.###} s。",
                capabilities.DisplayName,
                tags.Count == 0 ? "无已声明能力" : string.Join("、", tags.ToArray()),
                capabilities.MaxReadItems,
                capabilities.MaxWriteItems,
                capabilities.MaxAddressSpan,
                pduText,
                capabilities.RecommendedMinPollingInterval.TotalMilliseconds,
                capabilities.DefaultOperationTimeout.TotalSeconds);
        }
    }
}
