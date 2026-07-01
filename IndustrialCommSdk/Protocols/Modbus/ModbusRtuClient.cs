using System;
using System.IO.Ports;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Polling;
using Modbus.Device;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// Modbus RTU 串口协议客户端，继承自 <see cref="ModbusClientBase"/>，提供通过 RS-232/RS-485 串口进行 Modbus RTU 通信的功能。
    /// </summary>
    public sealed class ModbusRtuClient : ModbusClientBase
    {
        private readonly ModbusRtuClientOptions _options;
        private SerialPort _serialPort;
        private ModbusMaster _master;

        /// <summary>实际写入或从串口组装完成的标准 Modbus RTU 帧。</summary>
        public event EventHandler<ModbusRtuFrameEventArgs> FrameTraced;

        /// <summary>
        /// 像 Modbus 调试助手一样发送完整 RTU 请求并返回完整响应。请求必须包含 CRC。
        /// 支持标准功能码 01、02、03、04、05、06、0F、10 及异常响应。
        /// </summary>
        public Task<byte[]> TransceiveRawAsync(byte[] requestFrame, CancellationToken cancellationToken)
        {
            if (requestFrame == null) throw new ArgumentNullException(nameof(requestFrame));
            var request = (byte[])requestFrame.Clone();
            if (request.Length < 4) throw new ArgumentException("Modbus RTU 请求长度不能少于 4 字节。", nameof(requestFrame));
            if (!ModbusRtuFrameCodec.HasValidCrc(request)) throw new ArgumentException("Modbus RTU 请求 CRC 错误。", nameof(requestFrame));

            return ExecuteExclusiveAsync(token => Task.Run(() => TransceiveRaw(request, token), token), cancellationToken);
        }

        private byte[] TransceiveRaw(byte[] request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            Logger.Info(string.Format(
                "Modbus RTU RAW begin | Port={0} | Serial={1}-{2}-{3}-{4} | Timeout={5}ms | Slave={6} | Function=0x{7:X2} | Bytes={8}",
                _options.PortName, _options.BaudRate, _options.DataBits, _options.Parity, _options.StopBits,
                _options.ReadTimeout, request[0], request[1], request.Length));
            _serialPort.DiscardInBuffer();
            Logger.Trace("Modbus RTU RAW input buffer cleared.");
            _serialPort.Write(request, 0, request.Length);
            OnFrameTraced(new ModbusRtuFrameEventArgs(ModbusRtuFrameDirection.Transmit, request, true));
            Logger.Info("Modbus RTU TX (raw) | " + BitConverter.ToString(request).Replace('-', ' '));

            // 站号 0 是广播：从站按规范不返回响应。
            if (request[0] == 0)
            {
                Logger.Info(string.Format("Modbus RTU RAW broadcast completed | Elapsed={0}ms | ResponseExpected=false", stopwatch.ElapsedMilliseconds));
                return new byte[0];
            }

            var received = new List<byte>();
            try
            {
                var header = ReadExact(2, cancellationToken, received);
                Logger.Trace(string.Format("Modbus RTU RAW response header | Slave={0} | Function=0x{1:X2} | FirstBytesElapsed={2}ms", header[0], header[1], stopwatch.ElapsedMilliseconds));
                if ((header[1] & 0x80) != 0)
                    ReadExact(3, cancellationToken, received); // 异常码 + CRC
                else if (header[1] >= 1 && header[1] <= 4)
                {
                    var byteCount = ReadExact(1, cancellationToken, received)[0];
                    ReadExact(byteCount + 2, cancellationToken, received);
                }
                else if (header[1] == 5 || header[1] == 6 || header[1] == 15 || header[1] == 16)
                    ReadExact(6, cancellationToken, received);
                else
                    throw new InvalidDataException("原始调试暂不支持响应功能码 0x" + header[1].ToString("X2") + "。");
            }
            catch (TimeoutException ex)
            {
                if (received.Count > 0)
                {
                    var partial = received.ToArray();
                    OnFrameTraced(new ModbusRtuFrameEventArgs(ModbusRtuFrameDirection.Receive, partial, false));
                    Logger.Warn("Modbus RTU RX (partial timeout) | " + BitConverter.ToString(partial).Replace('-', ' '));
                }
                Logger.Warn(string.Format(
                    "Modbus RTU RAW timeout | Port={0} | Slave={1} | Function=0x{2:X2} | Timeout={3}ms | ReceivedBytes={4} | Elapsed={5}ms",
                    _options.PortName, request[0], request[1], _serialPort.ReadTimeout, received.Count, stopwatch.ElapsedMilliseconds));
                throw new IndustrialTimeoutException(string.Format(
                    "等待 Modbus RTU 响应超时（{0} ms，已接收 {1} 字节）。请检查从站地址、波特率、校验位、停止位、A/B 接线及 485 收发方向。",
                    _serialPort.ReadTimeout,
                    received.Count), ex);
            }

            var response = received.ToArray();
            var crcValid = ModbusRtuFrameCodec.HasValidCrc(response);
            OnFrameTraced(new ModbusRtuFrameEventArgs(ModbusRtuFrameDirection.Receive, response, crcValid));
            Logger.Info("Modbus RTU RX (raw) | " + BitConverter.ToString(response).Replace('-', ' '));
            if (!crcValid)
            {
                Logger.Warn(string.Format("Modbus RTU RAW CRC failed | Bytes={0} | Elapsed={1}ms", response.Length, stopwatch.ElapsedMilliseconds));
                throw new InvalidDataException("Modbus RTU 响应 CRC 校验失败。");
            }
            if ((response[1] & 0x80) != 0)
                Logger.Warn(string.Format("Modbus RTU RAW exception response | Function=0x{0:X2} | ExceptionCode=0x{1:X2}", response[1], response[2]));
            Logger.Info(string.Format("Modbus RTU RAW completed | Bytes={0} | CRC=OK | Elapsed={1}ms", response.Length, stopwatch.ElapsedMilliseconds));
            return response;
        }

        private byte[] ReadExact(int count, CancellationToken cancellationToken, List<byte> received)
        {
            var result = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = _serialPort.Read(result, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException("串口在完整 RTU 响应到达前已结束。");
                for (var index = 0; index < read; index++) received.Add(result[offset + index]);
                offset += read;
            }
            return result;
        }

        /// <summary>
        /// 初始化 <see cref="ModbusRtuClient"/> 类的新实例。
        /// </summary>
        /// <param name="options">Modbus RTU 客户端配置选项，包含设备 ID、串口名、波特率等设置。</param>
        /// <param name="logger">可选的工业日志记录器实例。如果为 null，则使用 <see cref="NullIndustrialLogger"/>。</param>
        /// <param name="pollingScheduler">可选的轮询调度器实例。如果为 null，则创建默认的 <see cref="PollingScheduler"/>。</param>
        /// <param name="addressParser">可选的 Modbus 地址解析器。如果为 null，则使用配置文件的默认解析器。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时引发。</exception>
        public ModbusRtuClient(ModbusRtuClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null, ModbusAddressParser addressParser = null)
            : base(GetDeviceId(options), ProtocolKind.ModbusRtu, options.SlaveId, options.DeviceProfile, addressParser, pollingScheduler, logger)
        {
            ValidateOptions(options);
            _options = options;
        }

        private static string GetDeviceId(ModbusRtuClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return options.DeviceId;
        }

        private static void ValidateOptions(ModbusRtuClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceId))
                throw new ArgumentException("Modbus RTU device ID is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.PortName))
                throw new ArgumentException("Modbus RTU serial port name is required.", nameof(options));
            if (options.BaudRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU baud rate must be greater than zero.");
            if (options.DataBits != 8)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU requires 8 data bits.");
            if (options.StopBits == StopBits.None)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU stop bits cannot be None.");
            if (options.SlaveId == 0 || options.SlaveId > 247)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU slave ID must be between 1 and 247.");
            if (options.ReadTimeout <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU read timeout must be greater than zero.");
            if (options.WriteTimeout <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU write timeout must be greater than zero.");
            if (options.Retries < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU retries cannot be negative.");
            if (options.WaitToRetryMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Modbus RTU retry wait must be greater than zero.");
        }

        /// <summary>
        /// 获取当前活跃的 NModbus 主站实例。
        /// </summary>
        protected override ModbusMaster Master => _master;

        /// <summary>
        /// 获取一个值，指示当前串口是否处于打开状态。
        /// </summary>
        public override bool IsConnected => _master != null && _serialPort != null && _serialPort.IsOpen;

        /// <summary>
        /// 通过串口建立与 Modbus RTU 设备的连接。
        /// </summary>
        protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectInternal();
            try
            {
                _serialPort = new SerialPort(
                    _options.PortName,
                    _options.BaudRate,
                    _options.Parity,
                    _options.DataBits,
                    _options.StopBits);

                // SerialPort 默认使用无限读取超时。设备掉线时这会永久占用公共操作锁，
                // 因此必须在打开串口前设置有限超时，让失败能够返回给调用方。
                _serialPort.ReadTimeout = _options.ReadTimeout;
                _serialPort.WriteTimeout = _options.WriteTimeout;

                _serialPort.Open();
                Logger.Info(string.Format(
                    "Modbus RTU serial opened | Port={0} | Baud={1} | DataBits={2} | Parity={3} | StopBits={4} | ReadTimeout={5}ms | WriteTimeout={6}ms | Slave={7} | Retries={8}",
                    _options.PortName, _options.BaudRate, _options.DataBits, _options.Parity, _options.StopBits,
                    _options.ReadTimeout, _options.WriteTimeout, _options.SlaveId, _options.Retries));
                cancellationToken.ThrowIfCancellationRequested();
                var tracingResource = new ModbusRtuTracingStreamResource(_serialPort, Logger, OnFrameTraced);
                _master = ModbusSerialMaster.CreateRtu(tracingResource);
                _master.Transport.Retries = _options.Retries;
                _master.Transport.WaitToRetryMilliseconds = _options.WaitToRetryMilliseconds;
            }
            catch (Exception ex) when (!(ex is IndustrialConnectionException))
            {
                DisconnectInternal();
                throw new IndustrialConnectionException(
                    string.Format("Failed to open serial port {0}.", _options.PortName), ex);
            }

            return Task.CompletedTask;
        }

        private void OnFrameTraced(ModbusRtuFrameEventArgs args)
        {
            FrameTraced?.Invoke(this, args);
        }

        /// <summary>
        /// 断开与 Modbus RTU 设备的连接（关闭串口）。
        /// </summary>
        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            DisconnectInternal();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 释放客户端占用的资源。
        /// </summary>
        protected override void DisposeCore()
        {
            DisconnectInternal();
        }

        /// <summary>
        /// 断开串口连接并释放 Modbus 主站和串口资源。
        /// </summary>
        private void DisconnectInternal()
        {
            var wasOpen = _serialPort != null && _serialPort.IsOpen;
            if (_master != null)
            {
                _master.Dispose();
                _master = null;
            }
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
                _serialPort.Dispose();
                _serialPort = null;
            }
            if (wasOpen) Logger.Info("Modbus RTU serial closed | Port=" + _options.PortName);
        }
    }
}
