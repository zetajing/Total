using System;
using System.Net.Sockets;
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
    /// Modbus TCP 协议客户端，继承自 <see cref="ModbusClientBase"/>，提供通过 TCP 协议进行 Modbus 通信的功能。
    /// </summary>
    public sealed class ModbusTcpClient : ModbusClientBase
    {
        private readonly ModbusTcpClientOptions _options;
        private TcpClient _tcpClient;
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
            : base(GetDeviceId(options), ProtocolKind.ModbusTcp, options.SlaveId, options.DeviceProfile, addressParser, pollingScheduler, logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        private static string GetDeviceId(ModbusTcpClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return options.DeviceId;
        }

        /// <summary>
        /// 获取当前活跃的 NModbus 主站实例。
        /// </summary>
        protected override ModbusMaster Master => _master;

        /// <summary>
        /// 获取一个值，指示当前是否已成功连接到 Modbus TCP 设备。
        /// </summary>
        public override bool IsConnected => _master != null && _tcpClient != null && _tcpClient.Connected;

        /// <summary>
        /// 建立与 Modbus TCP 设备的连接。
        /// </summary>
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

        /// <summary>
        /// 断开与 Modbus TCP 设备的连接。
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
    }
}
