using System.Collections.Generic;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>
    /// Optional capability provider for protocol clients that need to override the SDK default capability matrix.
    /// Keep this interface separate from IIndustrialClient so existing clients and tests do not break.
    /// </summary>
    public interface IProtocolCapabilityProvider
    {
        /// <summary>Gets the effective capabilities of the current protocol client.</summary>
        ProtocolCapabilities Capabilities { get; }
    }

    /// <summary>
    /// Protocol-neutral address shape used by tooling, validation, batching, and documentation.
    /// Protocol implementations may expose richer internal address types while still mapping to this contract.
    /// </summary>
    public interface IIndustrialAddress
    {
        /// <summary>Original address text supplied by the user or point table.</summary>
        string Original { get; }

        /// <summary>Canonical normalized address text after protocol-specific parsing.</summary>
        string Normalized { get; }

        /// <summary>Protocol area or memory region, such as HR, DB, M, D, X, Y.</summary>
        string Area { get; }

        /// <summary>Word/byte/device offset inside the protocol area.</summary>
        int Offset { get; }

        /// <summary>Optional bit offset for bit-level addresses.</summary>
        int? Bit { get; }

        /// <summary>Whether this address targets a bit value.</summary>
        bool IsBitAddress { get; }
    }

    /// <summary>
    /// Strongly typed address parser. Existing IAddressParser remains available for backwards compatibility.
    /// New protocols should prefer this generic parser internally to avoid repeated object casts.
    /// </summary>
    /// <typeparam name="TAddress">Protocol-specific address type.</typeparam>
    public interface IAddressParser<TAddress> where TAddress : IIndustrialAddress
    {
        /// <summary>Parses and normalizes a protocol address.</summary>
        TAddress Parse(string address);
    }

    /// <summary>
    /// Optional planner for protocol implementations that can split, merge, or reorder batch operations safely.
    /// </summary>
    public interface IBatchOperationPlanner
    {
        /// <summary>Plans a read batch according to protocol capabilities and caller options.</summary>
        BatchSplitPlan PlanRead(IReadOnlyCollection<ReadRequest> requests, BatchReadOptions options, ProtocolCapabilities capabilities);

        /// <summary>Plans a write batch according to protocol capabilities and caller options.</summary>
        BatchSplitPlan PlanWrite(IReadOnlyCollection<WriteRequest> requests, BatchWriteOptions options, ProtocolCapabilities capabilities);
    }
}
