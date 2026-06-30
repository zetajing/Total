using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
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
    /// Modbus TCP 客户端的配置选项。
    /// </summary>
    public sealed class ModbusTcpClientOptions
    {
        /// <summary>
        /// 获取或设置设备标识符，用于在系统中唯一标识该设备。
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// 获取或设置 Modbus TCP 服务器的主机名或 IP 地址。
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 获取或设置 Modbus TCP 服务器的端口号。默认值为 502。
        /// </summary>
        public int Port { get; set; } = 502;

        /// <summary>
        /// 获取或设置 Modbus 从站 ID（站号）。默认值为 1。
        /// </summary>
        public byte SlaveId { get; set; } = 1;

        /// <summary>
        /// 获取或设置连接超时时间（毫秒）。默认值为 3000 毫秒。
        /// </summary>
        public int ConnectTimeoutMilliseconds { get; set; } = 3000;

        /// <summary>
        /// 获取或设置 Modbus 设备配置文件，用于定义设备的寄存器映射和字节序等特性。默认使用汇川 EasyPLC 配置。
        /// </summary>
        public IModbusDeviceProfile DeviceProfile { get; set; } = ModbusDeviceProfiles.InovanceEasyPlc;
    }

    /// <summary>
    /// Modbus TCP 协议客户端，继承自 <see cref="IndustrialClientBase"/>，提供通过 TCP 协议进行 Modbus 通信的功能。
    /// </summary>
    public sealed class ModbusTcpClient : IndustrialClientBase
    {
        // 客户端配置选项
        private readonly ModbusTcpClientOptions _options;

        // 设备配置文件，用于处理寄存器规范化和字节序
        private readonly IModbusDeviceProfile _deviceProfile;

        // Modbus 地址解析器
        private readonly ModbusAddressParser _addressParser;

        // 底层 TCP 客户端实例
        private TcpClient _tcpClient;

        // NModbus 主站实例，用于执行实际的 Modbus 读写操作
        private ModbusMaster _master;

        /// <summary>
        /// 初始化 <see cref="ModbusTcpClient"/> 类的新实例。
        /// </summary>
        /// <param name="options">Modbus TCP 客户端配置选项，包含设备 ID、主机、端口等设置。</param>
        /// <param name="logger">可选的工业日志记录器实例。如果为 null，则使用 <see cref="NullIndustrialLogger"/>。</param>
        /// <param name="pollingScheduler">可选的轮询调度器实例。如果为 null，则创建默认的 <see cref="PollingScheduler"/>。</param>
        /// <param name="addressParser">可选的 Modbus 地址解析器。如果为 null，则使用配置文件的默认解析器。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时引发。</exception>
        public ModbusTcpClient(ModbusTcpClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null, ModbusAddressParser addressParser = null)
            : base(GetDeviceId(options), ProtocolKind.ModbusTcp, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _deviceProfile = options.DeviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc;
            _addressParser = addressParser ?? new ModbusAddressParser(_deviceProfile);
        }

        private static string GetDeviceId(ModbusTcpClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return options.DeviceId;
        }

        /// <summary>
        /// 获取一个值，指示当前是否已成功连接到 Modbus TCP 设备。
        /// </summary>
        /// <remarks>
        /// 当 <see cref="_master"/> 和 <see cref="_tcpClient"/> 均不为 null，
        /// 且底层 TCP 连接处于已连接状态时，返回 true。
        /// </remarks>
        public override bool IsConnected
        {
            get { return _master != null && _tcpClient != null && _tcpClient.Connected; }
        }

        /// <summary>
        /// 建立与 Modbus TCP 设备的连接的核心异步方法。
        /// </summary>
        /// <param name="cancellationToken">用于取消连接操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <exception cref="OperationCanceledException">操作被取消时引发。</exception>
        /// <exception cref="IndustrialTimeoutException">连接超时时引发。</exception>
        /// <exception cref="IndustrialConnectionException">连接失败时引发。</exception>
        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            // 先断开可能存在的旧连接
            DisconnectInternal();
            try
            {
                _tcpClient = new TcpClient();
                using var timeoutCts = new CancellationTokenSource(_options.ConnectTimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var connectTask = _tcpClient.ConnectAsync(_options.Host, _options.Port);
                var waitTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
                var completed = await Task.WhenAny(connectTask, waitTask).ConfigureAwait(false);

                // 如果 waitTask 先完成，说明发生了超时或取消
                if (completed == waitTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IndustrialTimeoutException("Modbus TCP connect timeout.");
                }

                await connectTask.ConfigureAwait(false);
                // 基于已连接的 TcpClient 创建 Modbus 主站实例
                _master = ModbusIpMaster.CreateIp(_tcpClient);
            }
            catch (Exception ex) when (!(ex is IndustrialConnectionException) && !(ex is IndustrialTimeoutException) && !(ex is OperationCanceledException))
            {
                throw new IndustrialConnectionException("Failed to connect Modbus TCP device.", ex);
            }
        }

        /// <summary>
        /// 断开与 Modbus TCP 设备的连接的核心异步方法。
        /// </summary>
        /// <param name="cancellationToken">用于取消断开操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            DisconnectInternal();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 从 Modbus TCP 设备读取数据的核心异步方法。
        /// </summary>
        /// <param name="request">读取请求，包含设备 ID、地址、数据类型和长度等信息。</param>
        /// <param name="cancellationToken">用于取消读取操作的取消令牌。</param>
        /// <returns>包含读取结果的 <see cref="DataValue"/>。</returns>
        /// <exception cref="IndustrialConnectionException">客户端未连接时引发。</exception>
        /// <exception cref="IndustrialProtocolException">不支持的 Modbus 区域类型时引发。</exception>
        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = (ModbusAddress)_addressParser.Parse(request.Address);
            var normalizedRequest = NormalizeReadRequest(request, parsed);
            switch (parsed.Area)
            {
                case ModbusArea.Coil:
                    // 读取线圈状态
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        await ReadBitsInChunksAsync(ModbusArea.Coil, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false));
                case ModbusArea.DiscreteInput:
                    // 读取离散输入
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        await ReadBitsInChunksAsync(ModbusArea.DiscreteInput, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false));
                case ModbusArea.InputRegister:
                    // 读取输入寄存器，并按设备配置文件规范化
                    var ir = await ReadRegistersInChunksAsync(ModbusArea.InputRegister, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false);
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        _deviceProfile.NormalizeRegistersForRead(normalizedRequest.DataType, ir));
                case ModbusArea.HoldingRegister:
                    // 读取保持寄存器，并按设备配置文件规范化
                    var hr = await ReadRegistersInChunksAsync(ModbusArea.HoldingRegister, parsed.ZeroBasedAddress, normalizedRequest.Length, cancellationToken).ConfigureAwait(false);
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        _deviceProfile.NormalizeRegistersForRead(normalizedRequest.DataType, hr));
                default:
                    throw new IndustrialProtocolException("Unsupported Modbus area.");
            }
        }

        /// <summary>
        /// 向 Modbus TCP 设备写入数据的核心异步方法。
        /// </summary>
        /// <param name="request">写入请求，包含设备 ID、地址、数据类型、值和长度等信息。</param>
        /// <param name="cancellationToken">用于取消写入操作的取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <exception cref="IndustrialConnectionException">客户端未连接时引发。</exception>
        /// <exception cref="IndustrialProtocolException">
        /// 当尝试写入只读区域（离散输入、输入寄存器）或不支持的 Modbus 区域时引发。
        /// </exception>
        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = (ModbusAddress)_addressParser.Parse(request.Address);
            var normalizedRequest = NormalizeWriteRequest(request, parsed);
            switch (parsed.Area)
            {
                case ModbusArea.Coil:
                    // 写入线圈：单个线圈使用 WriteSingleCoil，多个线圈使用 WriteMultipleCoils
                    var bits = RegisterValueCodec.EncodeBits(normalizedRequest);
                    if (bits.Length == 1)
                        await _master.WriteSingleCoilAsync(_options.SlaveId, parsed.ZeroBasedAddress, bits[0]).ConfigureAwait(false);
                    else
                        await _master.WriteMultipleCoilsAsync(_options.SlaveId, parsed.ZeroBasedAddress, bits).ConfigureAwait(false);
                    break;
                case ModbusArea.DiscreteInput:
                    throw new IndustrialProtocolException("Discrete inputs are read-only.");
                case ModbusArea.InputRegister:
                    throw new IndustrialProtocolException("Input registers are read-only.");
                case ModbusArea.HoldingRegister:
                    // 写入保持寄存器：单个寄存器使用 WriteSingleRegister，多个寄存器使用 WriteMultipleRegisters
                    var registers = _deviceProfile.NormalizeRegistersForWrite(
                        normalizedRequest.DataType,
                        RegisterValueCodec.EncodeRegisters(normalizedRequest));
                    if (registers.Length == 1)
                        await _master.WriteSingleRegisterAsync(_options.SlaveId, parsed.ZeroBasedAddress, registers[0]).ConfigureAwait(false);
                    else
                        await _master.WriteMultipleRegistersAsync(_options.SlaveId, parsed.ZeroBasedAddress, registers).ConfigureAwait(false);
                    break;
                default:
                    throw new IndustrialProtocolException("Unsupported Modbus area.");
            }
        }

        /// <summary>
        /// 批量读取 Modbus TCP 设备数据的核心异步方法，支持自动合并连续的地址范围以减少网络通信次数。
        /// </summary>
        /// <param name="requests">读取请求集合，每个请求包含设备 ID、地址、数据类型等信息。</param>
        /// <param name="cancellationToken">用于取消读取操作的取消令牌。</param>
        /// <returns>包含所有读取结果的 <see cref="BatchReadResult"/>。</returns>
        /// <exception cref="IndustrialConnectionException">客户端未连接时引发。</exception>
        /// <exception cref="IndustrialProtocolException">不支持的 Modbus 区域类型时引发。</exception>
        /// <remarks>
        /// 该方法按 Modbus 区域（Area）对请求进行分组，在每个区域内按地址排序，
        /// 并使用 <see cref="MergeContiguous"/> 将地址连续的请求合并为一次 Modbus 调用，
        /// 从而显著提升批量读取性能。
        /// </remarks>
        protected override async Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var values = new DataValue[requests.Count];
            var overallStopwatch = Stopwatch.StartNew();
            var mergedBatchCount = 0;

            // 按 Modbus 区域分组，然后按地址排序并合并连续的范围
            var parsedItems = requests
                .Select((r, index) => new { Request = r, Address = (ModbusAddress)_addressParser.Parse(r.Address), Index = index })
                .ToList();

            var groups = parsedItems
                .GroupBy(x => x.Address.Area);

            foreach (var group in groups)
            {
                var sortedItems = group
                    .Select(x => new MergeItem { Request = x.Request, Address = x.Address, Norm = NormalizeReadRequest(x.Request, x.Address), OriginalIndex = x.Index })
                    .OrderBy(x => x.Address.ZeroBasedAddress)
                    .ToList();

                var mergedBatches = MergeContiguous(sortedItems);
                mergedBatchCount += mergedBatches.Count;

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
                            // 批量读取线圈数据，然后按各个请求的偏移量切片
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
                            // 批量读取离散输入数据，然后按各个请求的偏移量切片
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
                            // 批量读取输入寄存器数据，按设备配置文件规范化后按偏移量切片
                            var raw = await ReadRegistersInChunksAsync(ModbusArea.InputRegister, startAddr, totalLength, cancellationToken).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new ushort[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values[item.OriginalIndex] = RegisterValueCodec.ToDataValue(item.Norm,
                                    _deviceProfile.NormalizeRegistersForRead(item.Norm.DataType, slice));
                            }
                            break;
                        }
                        case ModbusArea.HoldingRegister:
                        {
                            // 批量读取保持寄存器数据，按设备配置文件规范化后按偏移量切片
                            var raw = await ReadRegistersInChunksAsync(ModbusArea.HoldingRegister, startAddr, totalLength, cancellationToken).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new ushort[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values[item.OriginalIndex] = RegisterValueCodec.ToDataValue(item.Norm,
                                    _deviceProfile.NormalizeRegistersForRead(item.Norm.DataType, slice));
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
                "Modbus batch read summary | OriginalRequests={0} | MergedCalls={1} | SavedCalls={2} | Elapsed={3}ms",
                requests.Count,
                mergedBatchCount,
                Math.Max(0, requests.Count - mergedBatchCount),
                overallStopwatch.ElapsedMilliseconds));

            return new BatchReadResult(values);
        }

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
            while (offset < totalLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkLength = Math.Min(maxBitsPerRequest, totalLength - offset);
                var chunkStart = (ushort)(startAddress + offset);
                var chunk = area == ModbusArea.Coil
                    ? await _master.ReadCoilsAsync(_options.SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false)
                    : await _master.ReadInputsAsync(_options.SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false);
                Array.Copy(chunk, 0, result, offset, chunkLength);
                offset += chunkLength;
            }
            return result;
        }

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
            while (offset < totalLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkLength = Math.Min(maxRegistersPerRequest, totalLength - offset);
                var chunkStart = (ushort)(startAddress + offset);
                var chunk = area == ModbusArea.InputRegister
                    ? await _master.ReadInputRegistersAsync(_options.SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false)
                    : await _master.ReadHoldingRegistersAsync(_options.SlaveId, chunkStart, (ushort)chunkLength).ConfigureAwait(false);
                Array.Copy(chunk, 0, result, offset, chunkLength);
                offset += chunkLength;
            }
            return result;
        }

        private static void ValidateAddressRange(ushort startAddress, int totalLength)
        {
            if (totalLength <= 0 || (long)startAddress + totalLength > ushort.MaxValue + 1L)
            {
                throw new IndustrialProtocolException("Modbus read range exceeds the 16-bit address space.");
            }
        }

        /// <summary>
        /// 释放客户端所占用的托管资源的异步方法。
        /// </summary>
        protected override void DisposeCore()
        {
            DisconnectInternal();
        }

        /// <summary>
        /// 确保客户端已连接到 Modbus TCP 设备，否则抛出异常。
        /// </summary>
        /// <exception cref="IndustrialConnectionException">当客户端未处于已连接状态时引发。</exception>
        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new IndustrialConnectionException("Modbus TCP client is not connected.");
            }
        }

        /// <summary>
        /// 规范化读取请求，确保请求的长度符合 Modbus 协议和设备配置文件的要求。
        /// </summary>
        /// <param name="request">原始读取请求。</param>
        /// <param name="address">解析后的 Modbus 地址。</param>
        /// <returns>规范化后的读取请求。</returns>
        /// <remarks>
        /// 对于位区域（线圈/离散输入），如果数据类型不是 <see cref="DataType.Bool"/>，则强制转为 Bool。
        /// 对于寄存器区域，确保请求长度不小于数据类型所需的最小寄存器数量。
        /// </remarks>
        private static ReadRequest NormalizeReadRequest(ReadRequest request, ModbusAddress address)
        {
            // 位区域：强制使用 Bool 类型
            if (address.IsBitArea)
            {
                if (request.DataType == DataType.Bool)
                {
                    return request;
                }

                return new ReadRequest(request.DeviceId, request.Address, DataType.Bool, request.Length, request.Timeout);
            }

            // 寄存器区域：确保长度满足数据类型要求
            var normalizedLength = Math.Max(request.Length, RegisterValueCodec.GetRequiredRegisterLength(request.DataType));
            if (normalizedLength == request.Length)
            {
                return request;
            }

            return new ReadRequest(request.DeviceId, request.Address, request.DataType, normalizedLength, request.Timeout);
        }

        /// <summary>
        /// 规范化写入请求，确保请求的长度和数据类型符合 Modbus 协议和设备配置文件的要求。
        /// </summary>
        /// <param name="request">原始写入请求。</param>
        /// <param name="address">解析后的 Modbus 地址。</param>
        /// <returns>规范化后的写入请求。</returns>
        /// <remarks>
        /// 对于位区域（线圈），如果数据类型不是 <see cref="DataType.Bool"/>，则强制转为 Bool。
        /// 对于寄存器区域，确保请求长度不小于数据类型和值所需的最小寄存器数量。
        /// </remarks>
        private static WriteRequest NormalizeWriteRequest(WriteRequest request, ModbusAddress address)
        {
            // 位区域：强制使用 Bool 类型
            if (address.IsBitArea)
            {
                if (request.DataType == DataType.Bool)
                {
                    return request;
                }

                return new WriteRequest(request.DeviceId, request.Address, DataType.Bool, request.Value, request.Length, request.Timeout);
            }

            // 寄存器区域：确保长度满足数据类型和值的需求
            var normalizedLength = Math.Max(request.Length, RegisterValueCodec.GetRequiredRegisterLength(request.DataType, request.Value));
            if (normalizedLength == request.Length)
            {
                return request;
            }

            return new WriteRequest(request.DeviceId, request.Address, request.DataType, request.Value, normalizedLength, request.Timeout);
        }

        /// <summary>
        /// 断开底层 TCP 连接并释放 Modbus 主站和 TCP 客户端的资源。
        /// </summary>
        private void DisconnectInternal()
        {
            if (_master != null)
            {
                _master.Dispose();
                _master = null;
            }
            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }
        }

        /// <summary>
        /// 用于批量读取合并操作的内部数据项，包含原始请求、解析地址和规范化后的请求。
        /// </summary>
        private sealed class MergeItem
        {
            /// <summary>
            /// 获取或设置原始的读取请求。
            /// </summary>
            public ReadRequest Request { get; set; }

            /// <summary>
            /// 获取或设置解析后的 Modbus 地址。
            /// </summary>
            public ModbusAddress Address { get; set; }

            /// <summary>
            /// 获取或设置规范化后的读取请求。
            /// </summary>
            public ReadRequest Norm { get; set; }
            public int OriginalIndex { get; set; }
        }

        /// <summary>
        /// 将已按地址排序的合并项列表中的连续地址范围合并为多个批次。
        /// </summary>
        /// <param name="sortedItems">按地址升序排序的 <see cref="MergeItem"/> 列表。</param>
        /// <returns>合并后的批次列表，每个批次包含一组地址连续的请求。</returns>
        /// <remarks>
        /// 合并规则：如果当前项的开始地址与上一项的结束地址之间的间隙不超过 16 个地址，则归入同一批次；
        /// 否则开始一个新的批次。这有助于减少 Modbus 通信次数。
        /// </remarks>
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

                // 如果当前项的开始地址与上一项的结束地址之间的间隙 ≤ 16，则合并
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
