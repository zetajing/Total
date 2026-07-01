using System;
using System.IO.Ports;
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
                cancellationToken.ThrowIfCancellationRequested();
                var tracingResource = new ModbusRtuTracingStreamResource(_serialPort, Logger);
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
        }
    }
}
