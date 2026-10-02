using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Protocols.Common;
using InduLink.Protocols.Modbus;
using NModbus;

namespace InduLink.Simulation
{
    /// <summary>Real Modbus TCP slave with a generic big endian register map.</summary>
    public sealed class ModbusTcpSimulator : ProtocolSimulator
    {
        private readonly object _memorySync = new object();
        private readonly IPAddress _address;
        private readonly int _port;
        private readonly byte _slaveId;
        private readonly Dictionary<string, ModbusAddress> _addresses = new Dictionary<string, ModbusAddress>(StringComparer.OrdinalIgnoreCase);
        private readonly MemoryStore _store;
        private TcpListener _listener;
        private IModbusSlaveNetwork _network;
        private CancellationTokenSource _listenCancellation;
        private Task _listenTask = Task.CompletedTask;

        public ModbusTcpSimulator(IEnumerable<SimulationPoint> points, string address = "127.0.0.1", int port = 1502,
            byte slaveId = 1, TimeSpan? interval = null) : base(points, StringComparer.OrdinalIgnoreCase, interval)
        {
            _address = IPAddress.Parse(address);
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (slaveId == 0 || slaveId > 247) throw new ArgumentOutOfRangeException(nameof(slaveId));
            _port = port;
            _slaveId = slaveId;
            _store = new MemoryStore(_memorySync);
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var point in Points)
            {
                var parsed = ModbusDeviceProfiles.Generic.ParseAddress(point.Address);
                if (parsed.IsBitArea != (point.DataType == DataType.Bool))
                    throw new ArgumentException("C/DI require Bool; HR/IR require numeric types: " + point.Address);
                if (point.DataType == DataType.String) throw new ArgumentException("Modbus simulator does not support String points.");
                var width = parsed.IsBitArea ? 1 : RegisterValueCodec.GetRequiredRegisterLength(point.DataType);
                if ((int)parsed.ZeroBasedAddress + width > 65536) throw new ArgumentException("Point exceeds register map: " + point.Address);
                for (var offset = 0; offset < width; offset++)
                    if (!occupied.Add(parsed.Area + ":" + (parsed.ZeroBasedAddress + offset)))
                        throw new ArgumentException("Overlapping simulator points: " + point.Address);
                _addresses.Add(point.Address, parsed);
                SetPointValue(point, point.ParseValue(point.InitialValue));
            }
        }

        public int Port => _listener == null ? _port : ((IPEndPoint)_listener.LocalEndpoint).Port;
        public override string Endpoint => "modbus.tcp://" + (_address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + _address + "]" : _address.ToString()) + ":" + Port + " (Unit " + _slaveId + ")";

        protected override Task StartTransportAsync(CancellationToken cancellationToken)
        {
            _listener = new TcpListener(_address, _port);
            _listener.Start();
            var factory = new ModbusFactory();
            _network = factory.CreateSlaveNetwork(_listener);
            _network.AddSlave(factory.CreateSlave(_slaveId, _store));
            _listenCancellation = new CancellationTokenSource();
            _listenTask = _network.ListenAsync(_listenCancellation.Token);
            return Task.CompletedTask;
        }

        protected override async Task StopTransportAsync()
        {
            _listenCancellation?.Cancel();
            _network?.Dispose();
            _listener?.Stop();
            try { await _listenTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_listenCancellation?.IsCancellationRequested == true) { }
            catch (ObjectDisposedException) when (_listenCancellation?.IsCancellationRequested == true) { }
            catch (SocketException) when (_listenCancellation?.IsCancellationRequested == true) { }
            finally
            {
                _network = null;
                _listener = null;
                _listenCancellation?.Dispose();
                _listenCancellation = null;
                _listenTask = Task.CompletedTask;
            }
        }

        protected override object ReadPointValue(SimulationPoint point)
        {
            lock (_memorySync)
            {
                var address = _addresses[point.Address];
                var request = new ReadRequest("simulator", point.Address, point.DataType);
                if (address.IsBitArea)
                    return RegisterValueCodec.ToDataValue(request, Bits(address).ReadPoints(address.ZeroBasedAddress, 1)).Value;
                return RegisterValueCodec.ToDataValue(request, Registers(address).ReadPoints(address.ZeroBasedAddress,
                    RegisterValueCodec.GetRequiredRegisterLength(point.DataType))).Value;
            }
        }

        protected override void SetPointValue(SimulationPoint point, object value)
        {
            lock (_memorySync)
            {
                var address = _addresses[point.Address];
                if (address.IsBitArea) Bits(address).WritePoints(address.ZeroBasedAddress, new[] { (bool)value });
                else Registers(address).WritePoints(address.ZeroBasedAddress,
                    RegisterValueCodec.EncodeRegisters(new WriteRequest("simulator", point.Address, point.DataType, value)));
            }
        }

        protected override void AdvancePoint(SimulationPoint point)
        {
            lock (_memorySync) SetPointValue(point, point.NextValue(ReadPointValue(point)));
        }

        private IPointSource<bool> Bits(ModbusAddress address) => address.Area == ModbusArea.Coil ? _store.CoilDiscretes : _store.CoilInputs;
        private IPointSource<ushort> Registers(ModbusAddress address) => address.Area == ModbusArea.HoldingRegister ? _store.HoldingRegisters : _store.InputRegisters;

        private sealed class MemoryStore : ISlaveDataStore
        {
            public MemoryStore(object sync)
            {
                CoilDiscretes = new MemoryPoints<bool>(sync);
                CoilInputs = new MemoryPoints<bool>(sync);
                HoldingRegisters = new MemoryPoints<ushort>(sync);
                InputRegisters = new MemoryPoints<ushort>(sync);
            }
            public IPointSource<bool> CoilDiscretes { get; }
            public IPointSource<bool> CoilInputs { get; }
            public IPointSource<ushort> HoldingRegisters { get; }
            public IPointSource<ushort> InputRegisters { get; }
        }

        private sealed class MemoryPoints<T> : IPointSource<T>
        {
            private readonly object _sync;
            private readonly T[] _values = new T[65536];
            public MemoryPoints(object sync) { _sync = sync; }
            public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
            {
                if ((int)startAddress + numberOfPoints > _values.Length) throw new ArgumentOutOfRangeException(nameof(numberOfPoints));
                lock (_sync)
                {
                    var result = new T[numberOfPoints];
                    Array.Copy(_values, startAddress, result, 0, numberOfPoints);
                    return result;
                }
            }
            public void WritePoints(ushort startAddress, T[] points)
            {
                if (points == null) throw new ArgumentNullException(nameof(points));
                if ((int)startAddress + points.Length > _values.Length) throw new ArgumentOutOfRangeException(nameof(points));
                lock (_sync) Array.Copy(points, 0, _values, startAddress, points.Length);
            }
        }
    }
}
