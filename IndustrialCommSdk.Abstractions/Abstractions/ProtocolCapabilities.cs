using System;
using System.Collections.Generic;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>Independent protocol capabilities exposed by the SDK.</summary>
    [Flags]
    public enum ProtocolCapability
    {
        None = 0,
        RegisterRead = 1,
        RegisterWrite = 2,
        EventSubscription = 4,
        KeyValue = 8,
        FileTransfer = 16,
    }

    /// <summary>
    /// Describes what a protocol client can do. This is intentionally protocol-neutral so Demo, DeviceHost,
    /// polling, validation, and documentation can make decisions without knowing concrete PLC implementations.
    /// </summary>
    public sealed class ProtocolCapabilities
    {
        public ProtocolCapabilities(
            ProtocolKind kind,
            string displayName,
            bool supportsRead = true,
            bool supportsWrite = true,
            bool supportsBatchRead = true,
            bool supportsBatchWrite = true,
            bool supportsOptimizedBatchRead = false,
            bool supportsOptimizedBatchWrite = false,
            bool supportsSubscriptions = true,
            bool supportsBitAddress = false,
            bool supportsString = false,
            bool supportsByteArray = false,
            bool supportsRawTransport = false,
            bool supportsConnectionDiagnostics = true,
            bool supportsNativeAsync = true,
            int maxReadItems = 1,
            int maxWriteItems = 1,
            int maxAddressSpan = 1,
            int maxPduBytes = 0,
            TimeSpan? recommendedMinPollingInterval = null,
            TimeSpan? defaultOperationTimeout = null,
            IReadOnlyDictionary<string, string> extensions = null,
            bool supportsNativeSubscriptions = false,
            ProtocolCapability? capabilityFlags = null)
        {
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name cannot be null or empty.", nameof(displayName));
            if (maxReadItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxReadItems));
            if (maxWriteItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxWriteItems));
            if (maxAddressSpan <= 0) throw new ArgumentOutOfRangeException(nameof(maxAddressSpan));
            if (maxPduBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxPduBytes));
            if (recommendedMinPollingInterval.HasValue && recommendedMinPollingInterval.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(recommendedMinPollingInterval));
            if (defaultOperationTimeout.HasValue && defaultOperationTimeout.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(defaultOperationTimeout));

            Kind = kind;
            DisplayName = displayName;
            SupportsRead = supportsRead;
            SupportsWrite = supportsWrite;
            SupportsBatchRead = supportsBatchRead;
            SupportsBatchWrite = supportsBatchWrite;
            SupportsOptimizedBatchRead = supportsOptimizedBatchRead;
            SupportsOptimizedBatchWrite = supportsOptimizedBatchWrite;
            SupportsSubscriptions = supportsSubscriptions;
            SupportsBitAddress = supportsBitAddress;
            SupportsString = supportsString;
            SupportsByteArray = supportsByteArray;
            SupportsRawTransport = supportsRawTransport;
            SupportsConnectionDiagnostics = supportsConnectionDiagnostics;
            SupportsNativeAsync = supportsNativeAsync;
            SupportsNativeSubscriptions = supportsNativeSubscriptions;
            MaxReadItems = maxReadItems;
            MaxWriteItems = maxWriteItems;
            MaxAddressSpan = maxAddressSpan;
            MaxPduBytes = maxPduBytes;
            RecommendedMinPollingInterval = recommendedMinPollingInterval ?? TimeSpan.FromMilliseconds(250);
            DefaultOperationTimeout = defaultOperationTimeout ?? TimeSpan.FromSeconds(3);
            Extensions = extensions ?? new Dictionary<string, string>();
            CapabilityFlags = capabilityFlags ?? DeriveCapabilityFlags(supportsRead, supportsWrite, supportsSubscriptions);
        }

        public ProtocolKind Kind { get; private set; }
        public string DisplayName { get; private set; }
        public bool SupportsRead { get; private set; }
        public bool SupportsWrite { get; private set; }
        public bool SupportsBatchRead { get; private set; }
        public bool SupportsBatchWrite { get; private set; }
        public bool SupportsOptimizedBatchRead { get; private set; }
        public bool SupportsOptimizedBatchWrite { get; private set; }
        public bool SupportsSubscriptions { get; private set; }
        public bool SupportsBitAddress { get; private set; }
        public bool SupportsString { get; private set; }
        public bool SupportsByteArray { get; private set; }
        public bool SupportsRawTransport { get; private set; }
        public bool SupportsConnectionDiagnostics { get; private set; }
        public bool SupportsNativeAsync { get; private set; }
        public bool SupportsNativeSubscriptions { get; private set; }
        public int MaxReadItems { get; private set; }
        public int MaxWriteItems { get; private set; }
        public int MaxAddressSpan { get; private set; }
        public int MaxPduBytes { get; private set; }
        public TimeSpan RecommendedMinPollingInterval { get; private set; }
        public TimeSpan DefaultOperationTimeout { get; private set; }
        public IReadOnlyDictionary<string, string> Extensions { get; private set; }
        public ProtocolCapability CapabilityFlags { get; private set; }
        public bool SupportsRegisterRead { get { return (CapabilityFlags & ProtocolCapability.RegisterRead) != 0; } }
        public bool SupportsRegisterWrite { get { return (CapabilityFlags & ProtocolCapability.RegisterWrite) != 0; } }
        public bool SupportsEventSubscription { get { return (CapabilityFlags & ProtocolCapability.EventSubscription) != 0; } }
        public bool SupportsKeyValue { get { return (CapabilityFlags & ProtocolCapability.KeyValue) != 0; } }
        public bool SupportsFileTransfer { get { return (CapabilityFlags & ProtocolCapability.FileTransfer) != 0; } }

        private static ProtocolCapability DeriveCapabilityFlags(bool supportsRead, bool supportsWrite, bool supportsSubscriptions)
        {
            var flags = ProtocolCapability.None;
            if (supportsRead) flags |= ProtocolCapability.RegisterRead;
            if (supportsWrite) flags |= ProtocolCapability.RegisterWrite;
            if (supportsSubscriptions) flags |= ProtocolCapability.EventSubscription;
            return flags;
        }

        /// <summary>Returns conservative defaults for the built-in protocols.</summary>
        public static ProtocolCapabilities ForProtocol(ProtocolKind kind)
        {
            switch (kind)
            {
                case ProtocolKind.ModbusTcp:
                    return new ProtocolCapabilities(
                        kind,
                        "Modbus TCP",
                        supportsOptimizedBatchRead: true,
                        supportsBitAddress: true,
                        supportsString: true,
                        supportsByteArray: true,
                        maxReadItems: 120,
                        maxWriteItems: 120,
                        maxAddressSpan: 125,
                        maxPduBytes: 253,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(100),
                        capabilityFlags: ProtocolCapability.RegisterRead | ProtocolCapability.RegisterWrite | ProtocolCapability.EventSubscription);

                case ProtocolKind.ModbusRtu:
                    return new ProtocolCapabilities(
                        kind,
                        "Modbus RTU",
                        supportsOptimizedBatchRead: true,
                        supportsBitAddress: true,
                        supportsString: true,
                        supportsByteArray: true,
                        maxReadItems: 120,
                        maxWriteItems: 120,
                        maxAddressSpan: 125,
                        maxPduBytes: 253,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(200),
                        capabilityFlags: ProtocolCapability.RegisterRead | ProtocolCapability.RegisterWrite | ProtocolCapability.EventSubscription);

                case ProtocolKind.SiemensS7:
                    return new ProtocolCapabilities(
                        kind,
                        "Siemens S7",
                        supportsOptimizedBatchRead: true,
                        supportsBitAddress: true,
                        supportsString: true,
                        supportsByteArray: true,
                        maxReadItems: 200,
                        maxWriteItems: 200,
                        maxAddressSpan: 960,
                        maxPduBytes: 960,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(100),
                        capabilityFlags: ProtocolCapability.RegisterRead | ProtocolCapability.RegisterWrite | ProtocolCapability.EventSubscription);

                case ProtocolKind.MitsubishiMc:
                    return new ProtocolCapabilities(
                        kind,
                        "Mitsubishi MC 3E",
                        supportsBitAddress: true,
                        supportsByteArray: true,
                        maxReadItems: 256,
                        maxWriteItems: 256,
                        maxAddressSpan: 960,
                        maxPduBytes: 2048,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(100),
                        capabilityFlags: ProtocolCapability.RegisterRead | ProtocolCapability.RegisterWrite | ProtocolCapability.EventSubscription);

                case ProtocolKind.TcpSocket:
                    return new ProtocolCapabilities(
                        kind,
                        "TCP Socket",
                        supportsBatchRead: false,
                        supportsBatchWrite: false,
                        supportsSubscriptions: false,
                        supportsByteArray: true,
                        supportsRawTransport: true,
                        maxReadItems: 1,
                        maxWriteItems: 1,
                        maxAddressSpan: 1,
                        maxPduBytes: 0,
                        capabilityFlags: ProtocolCapability.None);

                case ProtocolKind.OpcUa:
                    return new ProtocolCapabilities(
                        kind,
                        "OPC UA",
                        supportsOptimizedBatchRead: true,
                        supportsOptimizedBatchWrite: true,
                        supportsString: true,
                        supportsByteArray: true,
                        maxReadItems: 1000,
                        maxWriteItems: 1000,
                        maxAddressSpan: int.MaxValue,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(100),
                        supportsNativeSubscriptions: true,
                        capabilityFlags: ProtocolCapability.RegisterRead | ProtocolCapability.RegisterWrite | ProtocolCapability.EventSubscription);

                case ProtocolKind.Mqtt:
                    return new ProtocolCapabilities(kind, "MQTT",
                        supportsRead: false, supportsWrite: false, supportsSubscriptions: false,
                        supportsString: true, supportsByteArray: true, maxReadItems: 1000,
                        maxWriteItems: 1000, maxAddressSpan: int.MaxValue,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(100),
                        capabilityFlags: ProtocolCapability.KeyValue);

                case ProtocolKind.Redis:
                    return new ProtocolCapabilities(kind, "Redis", supportsRead: false, supportsWrite: false,
                        supportsSubscriptions: false, supportsOptimizedBatchRead: true,
                        supportsOptimizedBatchWrite: true, supportsString: true, supportsByteArray: true,
                        maxReadItems: 1000, maxWriteItems: 1000, maxAddressSpan: int.MaxValue,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(50),
                        capabilityFlags: ProtocolCapability.KeyValue);

                case ProtocolKind.TwinCatAds:
                    return new ProtocolCapabilities(
                        kind,
                        "TwinCAT ADS",
                        supportsString: true,
                        supportsByteArray: true,
                        supportsOptimizedBatchRead: true,
                        supportsOptimizedBatchWrite: true,
                        maxReadItems: 1000,
                        maxWriteItems: 1000,
                        maxAddressSpan: int.MaxValue,
                        recommendedMinPollingInterval: TimeSpan.FromMilliseconds(100),
                        supportsNativeSubscriptions: true,
                        capabilityFlags: ProtocolCapability.RegisterRead | ProtocolCapability.RegisterWrite | ProtocolCapability.EventSubscription);

                default:
                    return new ProtocolCapabilities(kind, kind.ToString());
            }
        }
    }
}
