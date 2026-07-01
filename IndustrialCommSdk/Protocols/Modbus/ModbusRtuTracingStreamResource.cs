using System;
using System.Collections.Generic;
using System.IO.Ports;
using IndustrialCommSdk.Diagnostics;
using Modbus.IO;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 包装 NModbus 串口资源，在不改变收发内容的前提下记录完整 RTU 请求帧和响应帧。
    /// </summary>
    internal sealed class ModbusRtuTracingStreamResource : IStreamResource
    {
        private readonly SerialPort _serialPort;
        private readonly IIndustrialLogger _logger;
        private readonly object _sync = new object();
        private readonly List<byte> _responseBuffer = new List<byte>();

        public ModbusRtuTracingStreamResource(SerialPort serialPort, IIndustrialLogger logger)
        {
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public int InfiniteTimeout => SerialPort.InfiniteTimeout;

        public int ReadTimeout
        {
            get { return _serialPort.ReadTimeout; }
            set { _serialPort.ReadTimeout = value; }
        }

        public int WriteTimeout
        {
            get { return _serialPort.WriteTimeout; }
            set { _serialPort.WriteTimeout = value; }
        }

        public void DiscardInBuffer()
        {
            lock (_sync)
            {
                _responseBuffer.Clear();
            }
            _serialPort.DiscardInBuffer();
        }

        public void Dispose()
        {
            _serialPort.Dispose();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                var bytesRead = _serialPort.Read(buffer, offset, count);
                if (bytesRead > 0)
                {
                    TraceResponseBytes(buffer, offset, bytesRead);
                }
                return bytesRead;
            }
            catch
            {
                TracePartialResponse();
                throw;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                _responseBuffer.Clear();
            }

            // 先交给真实串口，只有写入成功后才将该帧记录为已发送。
            _serialPort.Write(buffer, offset, count);
            _logger.Info("Modbus RTU TX | " + ToHex(buffer, offset, count));
        }

        private void TraceResponseBytes(byte[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                for (var index = 0; index < count; index++)
                {
                    _responseBuffer.Add(buffer[offset + index]);
                }

                var expectedLength = GetExpectedResponseLength(_responseBuffer);
                if (expectedLength > 0 && _responseBuffer.Count >= expectedLength)
                {
                    _logger.Info("Modbus RTU RX | " + ToHex(_responseBuffer.ToArray(), 0, expectedLength));
                    _responseBuffer.RemoveRange(0, expectedLength);
                }
            }
        }

        private void TracePartialResponse()
        {
            lock (_sync)
            {
                if (_responseBuffer.Count == 0)
                {
                    return;
                }

                _logger.Info("Modbus RTU RX (partial) | " + ToHex(_responseBuffer.ToArray(), 0, _responseBuffer.Count));
                _responseBuffer.Clear();
            }
        }

        private static int GetExpectedResponseLength(IReadOnlyList<byte> frame)
        {
            if (frame.Count < 2)
            {
                return 0;
            }

            var functionCode = frame[1];
            if ((functionCode & 0x80) != 0)
            {
                return 5;
            }

            switch (functionCode)
            {
                case 1:
                case 2:
                case 3:
                case 4:
                    return frame.Count >= 3 ? 5 + frame[2] : 0;
                case 5:
                case 6:
                case 15:
                case 16:
                    return 8;
                default:
                    // 未知或自定义功能码无法可靠推导长度；等待底层读取失败时输出部分帧。
                    return 0;
            }
        }

        private static string ToHex(byte[] bytes, int offset, int count)
        {
            return BitConverter.ToString(bytes, offset, count).Replace('-', ' ');
        }
    }
}
