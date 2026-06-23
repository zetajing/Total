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
    public sealed class ModbusTcpClientOptions
    {
        public string DeviceId { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 502;
        public byte SlaveId { get; set; } = 1;
        public int ConnectTimeoutMilliseconds { get; set; } = 3000;
        public IModbusDeviceProfile DeviceProfile { get; set; } = ModbusDeviceProfiles.InovanceEasyPlc;
    }

    public sealed class ModbusTcpClient : IndustrialClientBase
    {
        private readonly ModbusTcpClientOptions _options;
        private readonly IModbusDeviceProfile _deviceProfile;
        private readonly ModbusAddressParser _addressParser;
        private TcpClient _tcpClient;
        private ModbusMaster _master;

        public ModbusTcpClient(ModbusTcpClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null, ModbusAddressParser addressParser = null)
            : base(options.DeviceId, ProtocolKind.ModbusTcp, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _deviceProfile = options.DeviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc;
            _addressParser = addressParser ?? new ModbusAddressParser(_deviceProfile);
        }

        public override bool IsConnected
        {
            get { return _master != null && _tcpClient != null && _tcpClient.Connected; }
        }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            DisconnectInternal();
            try
            {
                _tcpClient = new TcpClient();
                using var timeoutCts = new CancellationTokenSource(_options.ConnectTimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var connectTask = _tcpClient.ConnectAsync(_options.Host, _options.Port);
                var waitTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
                var completed = await Task.WhenAny(connectTask, waitTask).ConfigureAwait(false);

                if (completed == waitTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IndustrialTimeoutException("Modbus TCP connect timeout.");
                }

                await connectTask.ConfigureAwait(false);
                _master = ModbusIpMaster.CreateIp(_tcpClient);
            }
            catch (Exception ex) when (!(ex is IndustrialConnectionException) && !(ex is IndustrialTimeoutException) && !(ex is OperationCanceledException))
            {
                throw new IndustrialConnectionException("Failed to connect Modbus TCP device.", ex);
            }
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            DisconnectInternal();
            return Task.CompletedTask;
        }

        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = (ModbusAddress)_addressParser.Parse(request.Address);
            var normalizedRequest = NormalizeReadRequest(request, parsed);
            switch (parsed.Area)
            {
                case ModbusArea.Coil:
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        await _master.ReadCoilsAsync(_options.SlaveId, parsed.ZeroBasedAddress, normalizedRequest.Length).ConfigureAwait(false));
                case ModbusArea.DiscreteInput:
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        await _master.ReadInputsAsync(_options.SlaveId, parsed.ZeroBasedAddress, normalizedRequest.Length).ConfigureAwait(false));
                case ModbusArea.InputRegister:
                    var ir = await _master.ReadInputRegistersAsync(_options.SlaveId, parsed.ZeroBasedAddress, normalizedRequest.Length).ConfigureAwait(false);
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        _deviceProfile.NormalizeRegistersForRead(normalizedRequest.DataType, ir));
                case ModbusArea.HoldingRegister:
                    var hr = await _master.ReadHoldingRegistersAsync(_options.SlaveId, parsed.ZeroBasedAddress, normalizedRequest.Length).ConfigureAwait(false);
                    return RegisterValueCodec.ToDataValue(normalizedRequest,
                        _deviceProfile.NormalizeRegistersForRead(normalizedRequest.DataType, hr));
                default:
                    throw new IndustrialProtocolException("Unsupported Modbus area.");
            }
        }

        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = (ModbusAddress)_addressParser.Parse(request.Address);
            var normalizedRequest = NormalizeWriteRequest(request, parsed);
            switch (parsed.Area)
            {
                case ModbusArea.Coil:
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

        protected override async Task<BatchReadResult> ReadManyCoreAsync(
            IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var values = new List<DataValue>(requests.Count);
            var overallStopwatch = Stopwatch.StartNew();
            var mergedBatchCount = 0;

            // Group by Area, then sort by address and merge contiguous ranges
            var parsedItems = requests
                .Select(r => new { Request = r, Address = (ModbusAddress)_addressParser.Parse(r.Address) })
                .ToList();

            var groups = parsedItems
                .GroupBy(x => x.Address.Area);

            foreach (var group in groups)
            {
                var sortedItems = group
                    .Select(x => new MergeItem { Request = x.Request, Address = x.Address, Norm = NormalizeReadRequest(x.Request, x.Address) })
                    .OrderBy(x => x.Address.ZeroBasedAddress)
                    .ToList();

                var mergedBatches = MergeContiguous(sortedItems);
                mergedBatchCount += mergedBatches.Count;

                foreach (var batch in mergedBatches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batchStopwatch = Stopwatch.StartNew();

                    var startAddr = batch[0].Address.ZeroBasedAddress;
                    var lastItem = batch[batch.Count - 1];
                    var totalLength = (ushort)(lastItem.Address.ZeroBasedAddress + lastItem.Norm.Length - startAddr);

                    switch (group.Key)
                    {
                        case ModbusArea.Coil:
                        {
                            var raw = await _master.ReadCoilsAsync(_options.SlaveId, startAddr, totalLength).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new bool[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values.Add(RegisterValueCodec.ToDataValue(item.Norm, slice));
                            }
                            break;
                        }
                        case ModbusArea.DiscreteInput:
                        {
                            var raw = await _master.ReadInputsAsync(_options.SlaveId, startAddr, totalLength).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new bool[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values.Add(RegisterValueCodec.ToDataValue(item.Norm, slice));
                            }
                            break;
                        }
                        case ModbusArea.InputRegister:
                        {
                            var raw = await _master.ReadInputRegistersAsync(_options.SlaveId, startAddr, totalLength).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new ushort[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values.Add(RegisterValueCodec.ToDataValue(item.Norm,
                                    _deviceProfile.NormalizeRegistersForRead(item.Norm.DataType, slice)));
                            }
                            break;
                        }
                        case ModbusArea.HoldingRegister:
                        {
                            var raw = await _master.ReadHoldingRegistersAsync(_options.SlaveId, startAddr, totalLength).ConfigureAwait(false);
                            foreach (var item in batch)
                            {
                                var offset = item.Address.ZeroBasedAddress - startAddr;
                                var slice = new ushort[item.Norm.Length];
                                Array.Copy(raw, offset, slice, 0, item.Norm.Length);
                                values.Add(RegisterValueCodec.ToDataValue(item.Norm,
                                    _deviceProfile.NormalizeRegistersForRead(item.Norm.DataType, slice)));
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

        protected override void DisposeCore()
        {
            DisconnectInternal();
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new IndustrialConnectionException("Modbus TCP client is not connected.");
            }
        }

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

        private sealed class MergeItem
        {
            public ReadRequest Request { get; set; }
            public ModbusAddress Address { get; set; }
            public ReadRequest Norm { get; set; }
        }

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
