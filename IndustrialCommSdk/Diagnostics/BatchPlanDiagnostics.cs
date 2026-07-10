using System;
using System.Globalization;
using System.Linq;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Diagnostics
{
    /// <summary>
    /// Formats protocol-neutral batch plan diagnostics so protocols, polling, and Demo logs
    /// use the same wording and fields.
    /// </summary>
    public static class BatchPlanDiagnostics
    {
        public static string FormatSummary(string source, string deviceId, BatchSplitPlan plan, long? elapsedMilliseconds = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var elapsed = elapsedMilliseconds.HasValue
                ? string.Format(CultureInfo.InvariantCulture, " | Elapsed={0}ms", elapsedMilliseconds.Value)
                : string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "BATCH_PLAN summary | Source={0} | Device={1} | Protocol={2} | Operation={3} | OriginalRequests={4} | PlannedRequests={5} | SavedRequests={6}{7}",
                source ?? string.Empty,
                deviceId ?? string.Empty,
                plan.ProtocolKind,
                plan.OperationKind,
                plan.OriginalRequestCount,
                plan.PlannedRequestCount,
                plan.SavedRequestCount,
                elapsed);
        }

        public static string FormatGroup(string source, string deviceId, BatchSplitPlan plan, BatchRequestGroup group)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (group == null) throw new ArgumentNullException(nameof(group));

            var requestCount = plan.OperationKind == BatchOperationKind.Read
                ? group.ReadRequests.Count
                : group.WriteRequests.Count;
            var addresses = plan.OperationKind == BatchOperationKind.Read
                ? group.ReadRequests.Select(item => item.Address).ToArray()
                : group.WriteRequests.Select(item => item.Address).ToArray();

            return string.Format(
                CultureInfo.InvariantCulture,
                "BATCH_PLAN group | Source={0} | Device={1} | Protocol={2} | Operation={3} | Sequence={4} | Area={5} | Start={6} | End={7} | DataType={8} | Requests={9} | Addresses=[{10}]",
                source ?? string.Empty,
                deviceId ?? string.Empty,
                plan.ProtocolKind,
                plan.OperationKind,
                group.Sequence,
                group.Area ?? string.Empty,
                group.StartOffset.HasValue ? group.StartOffset.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                group.EndOffset.HasValue ? group.EndOffset.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                group.DataType.HasValue ? group.DataType.Value.ToString() : string.Empty,
                requestCount,
                string.Join(", ", addresses));
        }
    }
}
