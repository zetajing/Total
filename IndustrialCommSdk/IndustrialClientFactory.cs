using System;
using System.IO.Ports;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using S7.Net;

namespace IndustrialCommSdk
{
    /// <summary>
    /// Provides small factory helpers for the SDK's built-in protocol clients.
    /// </summary>
    public static class IndustrialClientFactory
    {
        public static ModbusTcpClient ModbusTcp(
            string host,
            int port = 502,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int connectTimeoutMilliseconds = 3000)
        {
            ValidateHost(host);
            ValidatePort(port, nameof(port));
            ValidatePositive(connectTimeoutMilliseconds, nameof(connectTimeoutMilliseconds));

            return new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "modbus-tcp", host, port, slaveId),
                Host = host,
                Port = port,
                SlaveId = slaveId,
                DeviceProfile = deviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc,
                ConnectTimeoutMilliseconds = connectTimeoutMilliseconds,
            }, logger);
        }

        public static ModbusRtuClient ModbusRtu(
            string portName,
            int baudRate = 9600,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int dataBits = 8,
            Parity parity = Parity.Even,
            StopBits stopBits = StopBits.One,
            int readTimeout = 3000,
            int writeTimeout = 3000,
            int retries = 2,
            int waitToRetryMilliseconds = 100)
        {
            ValidateText(portName, nameof(portName));
            ValidatePositive(baudRate, nameof(baudRate));
            ValidatePositive(dataBits, nameof(dataBits));
            ValidatePositive(readTimeout, nameof(readTimeout));
            ValidatePositive(writeTimeout, nameof(writeTimeout));
            ValidateNonNegative(retries, nameof(retries));
            ValidateNonNegative(waitToRetryMilliseconds, nameof(waitToRetryMilliseconds));

            return new ModbusRtuClient(new ModbusRtuClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "modbus-rtu", portName, baudRate, slaveId),
                PortName = portName,
                BaudRate = baudRate,
                SlaveId = slaveId,
                DeviceProfile = deviceProfile ?? ModbusDeviceProfiles.Generic,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                ReadTimeout = readTimeout,
                WriteTimeout = writeTimeout,
                Retries = retries,
                WaitToRetryMilliseconds = waitToRetryMilliseconds,
            }, logger);
        }

        public static SiemensS7Client SiemensS7(
            string host,
            CpuType cpuType = CpuType.S71200,
            short rack = 0,
            short slot = 1,
            string deviceId = null,
            IIndustrialLogger logger = null)
        {
            ValidateHost(host);

            return new SiemensS7Client(new SiemensS7ClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "siemens-s7", host, rack, slot),
                Host = host,
                CpuType = cpuType,
                Rack = rack,
                Slot = slot,
            }, logger);
        }

        public static MitsubishiMcClient MitsubishiMc(
            string host,
            int port = 5000,
            string deviceId = null,
            IIndustrialLogger logger = null,
            int sendTimeoutMilliseconds = 3000,
            int receiveTimeoutMilliseconds = 5000)
        {
            ValidateHost(host);
            ValidatePort(port, nameof(port));
            ValidatePositive(sendTimeoutMilliseconds, nameof(sendTimeoutMilliseconds));
            ValidatePositive(receiveTimeoutMilliseconds, nameof(receiveTimeoutMilliseconds));

            return new MitsubishiMcClient(new MitsubishiMcClientOptions
            {
                DeviceId = CoalesceDeviceId(deviceId, "mitsubishi-mc", host, port),
                Host = host,
                Port = port,
                SendTimeoutMilliseconds = sendTimeoutMilliseconds,
                ReceiveTimeoutMilliseconds = receiveTimeoutMilliseconds,
            }, logger);
        }

        public static IIndustrialClient CreateModbus(
            string host,
            int port = 502,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int connectTimeoutMilliseconds = 3000)
        {
            return ModbusTcp(host, port, slaveId, deviceId, logger, deviceProfile, connectTimeoutMilliseconds);
        }

        public static IIndustrialClient CreateModbusTcp(
            string host,
            int port = 502,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int connectTimeoutMilliseconds = 3000)
        {
            return ModbusTcp(host, port, slaveId, deviceId, logger, deviceProfile, connectTimeoutMilliseconds);
        }

        public static IIndustrialClient CreateModbusTcp(
            ModbusTcpClientOptions options,
            IIndustrialLogger logger = null)
        {
            return CreateModbus(options, logger);
        }

        public static IIndustrialClient CreateModbus(
            ModbusTcpClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new ModbusTcpClient(options, logger);
        }

        public static IIndustrialClient CreateModbusRtu(
            string portName,
            int baudRate = 9600,
            byte slaveId = 1,
            string deviceId = null,
            IIndustrialLogger logger = null,
            IModbusDeviceProfile deviceProfile = null,
            int dataBits = 8,
            Parity parity = Parity.Even,
            StopBits stopBits = StopBits.One,
            int readTimeout = 3000,
            int writeTimeout = 3000,
            int retries = 2,
            int waitToRetryMilliseconds = 100)
        {
            return ModbusRtu(portName, baudRate, slaveId, deviceId, logger, deviceProfile, dataBits, parity, stopBits, readTimeout, writeTimeout, retries, waitToRetryMilliseconds);
        }

        public static IIndustrialClient CreateModbusRtu(
            ModbusRtuClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new ModbusRtuClient(options, logger);
        }

        public static SiemensS7Client CreateSiemensS7(
            string host,
            CpuType cpuType = CpuType.S71200,
            short rack = 0,
            short slot = 1,
            string deviceId = null,
            IIndustrialLogger logger = null)
        {
            return SiemensS7(host, cpuType, rack, slot, deviceId, logger);
        }

        public static IIndustrialClient CreateSiemensS7(
            SiemensS7ClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new SiemensS7Client(options, logger);
        }

        public static MitsubishiMcClient CreateMitsubishiMc(
            string host,
            int port = 5000,
            string deviceId = null,
            IIndustrialLogger logger = null,
            int sendTimeoutMilliseconds = 3000,
            int receiveTimeoutMilliseconds = 5000)
        {
            return MitsubishiMc(host, port, deviceId, logger, sendTimeoutMilliseconds, receiveTimeoutMilliseconds);
        }

        public static IIndustrialClient CreateMitsubishiMc(
            MitsubishiMcClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new MitsubishiMcClient(options, logger);
        }

        private static string CoalesceDeviceId(string deviceId, string prefix, params object[] parts)
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return deviceId;
            }

            return prefix + "-" + string.Join("-", parts);
        }

        private static void ValidateHost(string host)
        {
            ValidateText(host, nameof(host));
        }

        private static void ValidateText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(parameterName + " cannot be null or empty.", parameterName);
            }
        }

        private static void ValidatePort(int port, string parameterName)
        {
            if (port <= 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Port must be in the range 1-65535.");
            }
        }

        private static void ValidatePositive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Value must be greater than zero.");
            }
        }

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
            }
        }
    }
}
