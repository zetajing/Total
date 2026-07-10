using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using IndustrialCommSdk.Protocols.Common;
using Modbus.Device;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// Modbus 协议客户端的公共基类，封装所有传输无关的 Modbus 读写逻辑。
    /// TCP 和 RTU 子类只需实现连接/断开和 <see cref="Master"/> 属性即可复用完整的协议能力。
    /// </summary>
    public abstract class ModbusClientBase : IndustrialClientBase, IBatchOperationPlanner
    {
        /// <summary>设备配置文件，用于处理寄存器规范化和字节序。</summary>
        protected readonly IModbusDeviceProfile DeviceProfile;

        /// <summary>Modbus 地址解析器。</summary>
        protected readonly ModbusAddressParser AddressParser;

        /// <summary>Modbus 从站 ID（站号）。</summary>
        protected readonly byte SlaveId;

        protected ModbusClientBase(
            string deviceId,
            ProtocolKind kind,
            byte slaveId,
            IModbusDeviceProfile deviceProfile,
            ModbusAddressParser addressParser,
            IPollingScheduler pollingScheduler,
            IIndustrialLogger logger)
            : base(deviceId, kind, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            SlaveId = slaveId;
            DeviceProfile = deviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc;
            AddressParser = addressParser ?? new ModbusAddressParser(DeviceProfile);
        }

        /// <summary>获取当前活跃的 NModbus 主站实例。子类在连接后赋值，断开后置 null。</summary>
        protected abstract ModbusMaster Master { get; }

        /// <summary>从 Modbus 设备读取数据的核心异步方法。</summary>
        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = AddressParser.ParseTyped(request.Address);
            var normalizedRequest = NormalizeReadRequest(request, parsed);
            switch (parsed.Area)
            {
                case ModbusArea.Coil:
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        await ReadBitsInChunksAsync(ModbusArea.Coil, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false));
                case ModbusArea.DiscreteInput:
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        await ReadBitsInChunksAsync(ModbusArea.DiscreteInput, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false));
                case ModbusArea.InputRegister:
                    var ir = await ReadRegistersInChunksAsync(ModbusArea.InputRegister, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false);
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        DeviceProfile.NormalizeRegistersForRead(normalizedRequest.DataType, ir));
                case ModbusArea.HoldingRegister:
                    var hr = await ReadRegistersInChunksAsync(ModbusArea.HoldingRegister, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false);
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        DeviceProfile.NormalizeRegistersForRead(normalizedRequest.DataType, hr));
                default:
                    throw new IndustrialProtocolException("Unsupported Modbus area.");
            }
        }

        /// <summary>向 Modbus 设备写入数据的核心异步方法。</summary>
        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = AddressParser.ParseTyped(request.Address);
            var normalizedRequest = NormalizeWriteRequest(request, parsed);
            var master = Master;
            switch (parsed.Area)
            {
                case ModbusArea.Coil:
                    var bits = RegisterValueCodec.EncodeBits(normalizedRequest);
                    if (bits.Length == 1)
                        await master.WriteSingleCoilAsync(SlaveId, parsed.ZeroBasedAddress, bits[0]).ConfigureAwait(false);
                    else
                        await master.WriteMultipleCoilsAsync(SlaveId, parsed.ZeroBasedAddress, bits).ConfigureAwait(false);
                    break;
                case ModbusArea.DiscreteInput:
                    throw new IndustrialProtocolException("Discrete inputs are read-only.");
                case ModbusArea.InputRegister:
                    throw new IndustrialProtocolException("Input registers are read-only.");
                case ModbusArea.HoldingRegister:
                    var registers = DeviceProfile.NormalizeRegistersForWrite(
                        normalizedRequest.DataType,
                        RegisterValueCodec.EncodeRegisters(normalizedRequest));
                    if (registers.Length == 1)
                        await master.WriteSingleRegisterAsync(SlaveId, parsed.ZeroBasedAddress, registers[0]).ConfigureAwait(false);
                    else
                        await master.WriteMultipleRegistersAsync(SlaveId, parsed.ZeroBasedAddress, registers).ConfigureAwait(false);
                    break;
                default:
                    throw new IndustrialProtocolException("Unsupported Modbus area.");
            }
        }

        /// <summary>
        /// Plans a Modbus read batch using the same area grouping and contiguous-range merge rules used by execution.
        /// </summary>
        public BatchSplitPlan PlanRead(IReadOnlyCollection<ReadRequest> requests, BatchReadOptions options, ProtocolCapabilities capabilities)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (requests.Count == 0)
                return new BatchSplitPlan(Kind, BatchOperationKind.Read, new BatchRequestGroup[0], 0);

            var parsedItems = BuildReadItems(requests);
            var groups = BuildReadPlanGroups(parsedItems);
            return new BatchSplitPlan(Kind, BatchOperationKind.Read, groups, requests.Count);
        }

        /// <summary>
        /// Plans Modbus writes conservatively: each logical write remains one physical write group.
        /// Future work can safely optimize adjacent holding-register writes through this contract.
        /// </summary>
        public BatchSplitPlan PlanWrite(IReadOnlyCollection<WriteRequest> requests, BatchWriteOptions options, ProtocolCapabilities capabilities)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (requests.Count == 0)
                return new BatchSplitPlan(Kind, BatchOperationKind.Write, new BatchRequestGroup[0], 0);

            var groups = new List<BatchRequestGroup>();
            var sequence = 0;
            foreach (var request in requests)
            {
                var address = AddressParser.ParseTyped(request.Address);
                var normalized = NormalizeWriteRequest(request, address);
                var start = (int)address.ZeroBasedAddress;
                var end = start + normalized.Length - 1;
                groups.Add(BatchRequestGroup.ForWrite(
                    sequence++,
                    address.Area.ToString(),
                    start,
                    end,
                    normalized.DataType,
                    new[] { request }));
            }

            return new BatchSplitPlan(Kind, BatchOperationKind.Write, groups, requests.Count);
        }

        /// <summary>
        /// 批量读取 Modbus 设备数据的核心异步方法，支持自动合并连续的地址范围以减少通信次数。
        /// </summary>
        protected override async Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var values = new DataValue[requests.Count];
            var overallStopwatch = Stopwatch.StartNew();

            var parsedItems = BuildReadItems(requests);
            var plan = new BatchSplitPlan(Kind, BatchOperationKind.Read, BuildReadPlanGroups(parsedItems), requests.Count);

            Logger.Info(string.Format(
                "Modbus batch read plan | OriginalRequests={0} | PlannedRequests={1} | SavedRequests={2}",
                plan.OriginalRequestCount,
                plan.PlannedRequestCount,
                plan.SavedRequestCount));

            var groups = parsedItems.GroupBy(x => x.Address.Area);

            foreach (var group in groups)
            {
                var sortedItems = group
                    .OrderBy(x => x.Address.ZeroBasedAddress)
                    .ToList();

                var mergedBatches = MergeContiguous(sortedItems);

                foreach (var batch in mergedBatches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batchStopwatch = Stopwatch.StartNew();

                    var startAddr = batch[0].Address.ZeroBasedAddress;
                    var endAddressExclusive = batch.Max(item => (int)item.Address.ZeroBasedAddress + item.Norm.Length);
                    var totalLength = endAddressExclusive - startAddr;

                    switch (group.Key)
                    {
                        case ModbusArea.Coil:
                        {
                            var raw = await ReadBitsInChunksAsync(ModbusArea.Coil, startAddr, totalLength, cancellationToken).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new bool[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values[item.OriginalIndex] = RegisterValueCodec.ToDataValue(item.Norm, slice);
                            }
                            break;
                        }
                        case ModbusArea.DiscreteInput:
                        {
                            var raw = await ReadBitsInChunksAsync(ModbusArea.DiscreteInput, startAddr, totalLength, cancellationToken).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new bool[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values[item.OriginalIndex] = RegisterValueCodec.ToDataValue(item.Norm, slice);
                            }
                            break;
                        }
                        case ModbusArea.InputRegister:
                        {
                            var raw = await ReadRegistersInChunksAsync(ModbusArea.InputRegister, startAddr, totalLength, cancellationToken).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new ushort[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values[item.OriginalIndex] = RegisterValueCodec.ToDataValue(item.Norm,
                                    DeviceProfile.NormalizeRegistersForRead(item.Norm.DataType, slice));
                            }
                            break;
                        }
                        case ModbusArea.HoldingRegister:
                        {
                            var raw = await ReadRegistersInChunksAsync(ModbusArea.HoldingRegister, startAddr, totalLength, cancellationToken).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new ushort[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values[item.OriginalIndex] = RegisterValueCodec.ToDataValue(item.Norm,
                                    DeviceProfile.NormalizeRegistersForRead(item.Norm.DataType, slice));
                            }
                            break;
                        }
                        default:
                            throw new IndustrialProtocolException("Unsupported Modbus area.");
                    }

                    batchStopwatch.Stop();
                    Logger.Info(string.Format(
                        "Modbus batch read merged {0} requests into 1 call | Area={1} | Start={2} | Length={3} | Addresses=[{4}] | Elapsed={5}ms",
                        batch.Count,
                        group.Key,
                        startAddr,
                        totalLength,
                        string.Join(", ", batch.Select(item => item.Request.Address).ToArray()),
                        batchStopwatch.ElapsedMilliseconds));
                }
            }

            overallStopwatch.Stop();
            Logger.Info(string.Format(
                "Modbus batch read summary | OriginalRequests={0} | PlannedRequests={1} | SavedRequests={2} | Elapsed={3}ms",
                plan.OriginalRequestCount,
                plan.PlannedRequestCount,
                plan.SavedRequestCount,
                overallStopwatch.ElapsedMilliseconds));

            return new BatchReadResult(values);
        }

        /// <summary>分块读取位（线圈/离散输入）数据，单次最多 2000 个位。</summary>
        private async Task<bool[]> ReadBitsInChunksAsync(
            ModbusArea area,
            ushort startAddress,
            int totalLength,
            CancellationToken cancellationToken)
        {
            ValidateAddressRange(startAddress, totalLength);
            const int maxBitsPerRequest = 2000;
            var result = new bool[totalLength];
            var offset = 0;
            var master = Master;
            while (offset < totalLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkLength = Math.Min(maxBitsPerRequest, totalLength - offset);
                var chunkStart = (ushort)(startAddress + offset);
                var chunk = area == ModbusArea.Coil
                    ? await master.ReadCoilsAsync(SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false)
                    : await master.ReadInputsAsync(SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false);
                Array.Copy(chunk, 0, result, offset, chunkLength);
                offset += chunkLength;
            }
            return result;
        }

        /// <summary>分块读取寄存器（输入/保持）数据，单次最多 125 个寄存器。</summary>
        private async Task<ushort[]> ReadRegistersInChunksAsync(
            ModbusArea area,
            ushort startAddress,
            int totalLength,
            CancellationToken cancellationToken)
        {
            ValidateAddressRange(startAddress, totalLength);
            const int maxRegistersPerRequest = 125;
            var result = new ushort[totalLength];
            var offset = 0;
            var master = Master;
            while (offset < totalLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkLength = Math.Min(maxRegistersPerRequest, totalLength - offset);
                var chunkStart = (ushort)(startAddress + offset);
                var chunk = area == ModbusArea.InputRegister
                    ? await master.ReadInputRegistersAsync(SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false)
                    : await master.ReadHoldingRegistersAsync(SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false);
                Array.Copy(chunk, 0, result, offset, chunkLength);
                offset += chunkLength;
            }
            return result;
        }

        /// <summary>校验读取范围是否超出 16 位地址空间。</summary>
        private static void ValidateAddressRange(ushort startAddress, int totalLength)
        {
            if (totalLength <= 0 || (long)startAddress + totalLength > ushort.MaxValue + 1L)
            {
                throw new IndustrialProtocolException("Modbus read range exceeds the 16-bit address space.");
            }
        }

        /// <summary>确保客户端已连接，否则抛出异常。</summary>
        protected void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new IndustrialConnectionException("Modbus client is not connected.");
            }
        }

        private List<MergeItem> BuildReadItems(IReadOnlyCollection<ReadRequest> requests)
        {
            return requests
                .Select((r, index) =>
                {
                    var address = AddressParser.ParseTyped(r.Address);
                    return new MergeItem
                    {
                        Request = r,
                        Address = address,
                        Norm = NormalizeReadRequest(r, address),
                        OriginalIndex = index,
                    };
                })
                .ToList();
        }

        private List<BatchRequestGroup> BuildReadPlanGroups(IReadOnlyCollection<MergeItem> parsedItems)
        {
            var planGroups = new List<BatchRequestGroup>();
            var sequence = 0;
            foreach (var areaGroup in parsedItems.GroupBy(x => x.Address.Area))
            {
                var sortedItems = areaGroup.OrderBy(x => x.Address.ZeroBasedAddress).ToList();
                var mergedBatches = MergeContiguous(sortedItems);
                foreach (var batch in mergedBatches)
                {
                    var startAddr = (int)batch[0].Address.ZeroBasedAddress;
                    var endAddressExclusive = batch.Max(item => (int)item.Address.ZeroBasedAddress + item.Norm.Length);
                    planGroups.Add(BatchRequestGroup.ForRead(
                        sequence++,
                        areaGroup.Key.ToString(),
                        startAddr,
                        endAddressExclusive - 1,
                        GetCommonDataType(batch),
                        batch.Select(item => item.Request).ToList()));
                }
            }
            return planGroups;
        }

        private static DataType? GetCommonDataType(IReadOnlyCollection<MergeItem> batch)
        {
            DataType? value = null;
            foreach (var item in batch)
            {
                if (!value.HasValue)
                {
                    value = item.Norm.DataType;
                    continue;
                }

                if (value.Value != item.Norm.DataType)
                    return null;
            }
            return value;
        }

        /// <summary>规范化读取请求，确保请求的长度符合 Modbus 协议和设备配置文件的要求。</summary>
        private static ReadRequest NormalizeReadRequest(ReadRequest request, ModbusAddress address)
        {
            if (address.IsBitArea)
            {
                if (request.DataType == DataType.Bool)
                {
                    return request;
                }

                return new ReadRequest(request.DeviceId, request.Address, DataType.Bool, request.Length, request.Timeout);
            }

            var normalizedLength = Math.Max(request.Length, RegisterValueCodec.GetRequiredRegisterLength(request.DataType));
            if (normalizedLength == request.Length)
            {
                return request;
            }

            return new ReadRequest(request.DeviceId, request.Address, request.DataType, normalizedLength, request.Timeout);
        }

        /// <summary>规范化写入请求，确保请求的长度和数据类型符合 Modbus 协议和设备配置文件的要求。</summary>
        private static WriteRequest NormalizeWriteRequest(WriteRequest request, ModbusAddress address)
        {
            if (address.IsBitArea)
            {
                if (request.DataType == DataType.Bool)
                {
                    return request;
                }

                return new WriteRequest(request.DeviceId, request.Address, DataType.Bool, request.Value, request.Length, request.Timeout);
            }

            var normalizedLength = Math.Max(request.Length, RegisterValueCodec.GetRequiredRegisterLength(request.DataType, request.Value));
            if (normalizedLength == request.Length)
            {
                return request;
            }

            return new WriteRequest(request.DeviceId, request.Address, request.DataType, request.Value, normalizedLength, request.Timeout);
        }

        /// <summary>用于批量读取合并操作的内部数据项。</summary>
        private sealed class MergeItem
        {
            public ReadRequest Request { get; set; }
            public ModbusAddress Address { get; set; }
            public ReadRequest Norm { get; set; }
            public int OriginalIndex { get; set; }
        }

        /// <summary>将已按地址排序的合并项列表中的连续地址范围合并为多个批次。</summary>
        private static List<List<MergeItem>> MergeContiguous(List<MergeItem> sortedItems)
        {
            var batches = new List<List<MergeItem>>();
            if (sortedItems.Count == 0)
                return batches;

            var current = new List<MergeItem> { sortedItems[0] };
            for (int i = 1; i < sortedItems.Count; i++)
            {
                var item = sortedItems[i];
                var prevEnd = current.Last().Address.ZeroBasedAddress + current.Last().Norm.Length;
                var currStart = item.Address.ZeroBasedAddress;

                if (currStart <= prevEnd + 16)
                {
                    current.Add(item);
                }
                else
                {
                    batches.Add(current);
                    current = new List<MergeItem> { item };
                }
            }
            batches.Add(current);
            return batches;
        }
    }
}