using System;
using System.Collections.Generic;
using System.IO.Ports;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime.Configuration;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Protocols.Modbus
{
    public sealed class ModbusTcpSettings : IProtocolSettings
    {
        public string Host { get; set; }
        public int Port { get; set; } = 502;
        public byte SlaveId { get; set; } = 1;
        public string DeviceProfile { get; set; } = ModbusDeviceProfiles.InovanceEasyPlc.Key;
        public int ConnectTimeoutMilliseconds { get; set; } = 3000;
    }

    public sealed class ModbusRtuSettings : IProtocolSettings
    {
        public string PortName { get; set; }
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public Parity Parity { get; set; } = Parity.Even;
        public StopBits StopBits { get; set; } = StopBits.One;
        public int ReadTimeoutMilliseconds { get; set; } = 3000;
        public int WriteTimeoutMilliseconds { get; set; } = 3000;
        public int Retries { get; set; } = 2;
        public int RetryDelayMilliseconds { get; set; } = 100;
        public byte SlaveId { get; set; } = 1;
        public string DeviceProfile { get; set; } = ModbusDeviceProfiles.Generic.Key;
    }

    public sealed class ModbusTcpProtocolProvider : IndustrialProtocolProvider<ModbusTcpSettings>
    {
        public override string Protocol { get { return "modbus-tcp"; } }

        protected override IReadOnlyList<string> Validate(ModbusTcpSettings settings)
        {
            return Errors(
                string.IsNullOrWhiteSpace(settings.Host) ? "host is required." : null,
                settings.Port < 1 || settings.Port > 65535 ? "port must be between 1 and 65535." : null,
                settings.SlaveId == 0 ? "slaveId must be between 1 and 255." : null,
                settings.ConnectTimeoutMilliseconds <= 0 ? "connectTimeoutMilliseconds must be positive." : null,
                ResolveProfileError(settings.DeviceProfile));
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, ModbusTcpSettings settings, IIndustrialLogger logger)
        {
            return new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                Host = settings.Host,
                Port = settings.Port,
                SlaveId = settings.SlaveId,
                DeviceProfile = ModbusDeviceProfiles.GetRequired(settings.DeviceProfile),
                ConnectTimeoutMilliseconds = settings.ConnectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }

        private static string ResolveProfileError(string key)
        {
            try { ModbusDeviceProfiles.GetRequired(key); return null; }
            catch (Exception ex) { return ex.Message; }
        }
    }

    public sealed class ModbusRtuProtocolProvider : IndustrialProtocolProvider<ModbusRtuSettings>
    {
        public override string Protocol { get { return "modbus-rtu"; } }

        protected override IReadOnlyList<string> Validate(ModbusRtuSettings settings)
        {
            string profileError;
            try { ModbusDeviceProfiles.GetRequired(settings.DeviceProfile); profileError = null; }
            catch (Exception ex) { profileError = ex.Message; }
            return Errors(
                string.IsNullOrWhiteSpace(settings.PortName) ? "portName is required." : null,
                settings.BaudRate <= 0 ? "baudRate must be positive." : null,
                settings.DataBits < 5 || settings.DataBits > 8 ? "dataBits must be between 5 and 8." : null,
                settings.ReadTimeoutMilliseconds <= 0 ? "readTimeoutMilliseconds must be positive." : null,
                settings.WriteTimeoutMilliseconds <= 0 ? "writeTimeoutMilliseconds must be positive." : null,
                settings.Retries < 0 ? "retries cannot be negative." : null,
                settings.RetryDelayMilliseconds < 0 ? "retryDelayMilliseconds cannot be negative." : null,
                settings.SlaveId == 0 ? "slaveId must be between 1 and 255." : null,
                profileError);
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, ModbusRtuSettings settings, IIndustrialLogger logger)
        {
            return new ModbusRtuClient(new ModbusRtuClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                PortName = settings.PortName,
                BaudRate = settings.BaudRate,
                DataBits = settings.DataBits,
                Parity = settings.Parity,
                StopBits = settings.StopBits,
                ReadTimeout = settings.ReadTimeoutMilliseconds,
                WriteTimeout = settings.WriteTimeoutMilliseconds,
                Retries = settings.Retries,
                WaitToRetryMilliseconds = settings.RetryDelayMilliseconds,
                SlaveId = settings.SlaveId,
                DeviceProfile = ModbusDeviceProfiles.GetRequired(settings.DeviceProfile),
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }
    }
}
