using System;
using System.Collections.Generic;
using System.Linq;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>Identifies the type of operation represented by a batch split plan.</summary>
    public enum BatchOperationKind
    {
        Read = 1,
        Write = 2,
    }

    /// <summary>
    /// Protocol-neutral description of how a logical batch should be split into physical protocol requests.
    /// The plan does not execute IO; it is a reusable contract for Modbus, S7, MC, and future protocols.
    /// </summary>
    public sealed class BatchSplitPlan
    {
        public BatchSplitPlan(ProtocolKind protocolKind, BatchOperationKind operationKind, IReadOnlyList<BatchRequestGroup> groups, int originalRequestCount)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            if (originalRequestCount < 0) throw new ArgumentOutOfRangeException(nameof(originalRequestCount));

            ProtocolKind = protocolKind;
            OperationKind = operationKind;
            Groups = groups;
            OriginalRequestCount = originalRequestCount;
            PlannedRequestCount = groups.Count;
            SavedRequestCount = Math.Max(0, originalRequestCount - groups.Count);
        }

        public ProtocolKind ProtocolKind { get; private set; }
        public BatchOperationKind OperationKind { get; private set; }
        public IReadOnlyList<BatchRequestGroup> Groups { get; private set; }
        public int OriginalRequestCount { get; private set; }
        public int PlannedRequestCount { get; private set; }
        public int SavedRequestCount { get; private set; }

        public static BatchSplitPlan SingleReadGroup(ProtocolKind protocolKind, IReadOnlyCollection<ReadRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            return new BatchSplitPlan(
                protocolKind,
                BatchOperationKind.Read,
                new[] { BatchRequestGroup.ForRead(0, null, null, null, null, requests.ToList()) },
                requests.Count);
        }

        public static BatchSplitPlan SingleWriteGroup(ProtocolKind protocolKind, IReadOnlyCollection<WriteRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            return new BatchSplitPlan(
                protocolKind,
                BatchOperationKind.Write,
                new[] { BatchRequestGroup.ForWrite(0, null, null, null, null, requests.ToList()) },
                requests.Count);
        }
    }

    /// <summary>One physical request group in a batch split plan.</summary>
    public sealed class BatchRequestGroup
    {
        private BatchRequestGroup(
            int sequence,
            string area,
            int? startOffset,
            int? endOffset,
            DataType? dataType,
            IReadOnlyList<ReadRequest> readRequests,
            IReadOnlyList<WriteRequest> writeRequests)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (startOffset.HasValue && startOffset.Value < 0) throw new ArgumentOutOfRangeException(nameof(startOffset));
            if (endOffset.HasValue && endOffset.Value < 0) throw new ArgumentOutOfRangeException(nameof(endOffset));
            if (startOffset.HasValue && endOffset.HasValue && endOffset.Value < startOffset.Value) throw new ArgumentOutOfRangeException(nameof(endOffset));

            Sequence = sequence;
            Area = area;
            StartOffset = startOffset;
            EndOffset = endOffset;
            DataType = dataType;
            ReadRequests = readRequests ?? new ReadRequest[0];
            WriteRequests = writeRequests ?? new WriteRequest[0];
        }

        public int Sequence { get; private set; }
        public string Area { get; private set; }
        public int? StartOffset { get; private set; }
        public int? EndOffset { get; private set; }
        public DataType? DataType { get; private set; }
        public IReadOnlyList<ReadRequest> ReadRequests { get; private set; }
        public IReadOnlyList<WriteRequest> WriteRequests { get; private set; }

        public static BatchRequestGroup ForRead(int sequence, string area, int? startOffset, int? endOffset, DataType? dataType, IReadOnlyList<ReadRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            return new BatchRequestGroup(sequence, area, startOffset, endOffset, dataType, requests, null);
        }

        public static BatchRequestGroup ForWrite(int sequence, string area, int? startOffset, int? endOffset, DataType? dataType, IReadOnlyList<WriteRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            return new BatchRequestGroup(sequence, area, startOffset, endOffset, dataType, null, requests);
        }
    }
}
