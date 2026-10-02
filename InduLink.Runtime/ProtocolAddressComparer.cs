using System;
using InduLink.Abstractions;

namespace InduLink.Runtime
{
    /// <summary>按协议选择地址比较规则；业务点位名称仍不区分大小写。</summary>
    public static class ProtocolAddressComparer
    {
        public static StringComparer ForProtocol(ProtocolKind kind)
        {
            return kind == ProtocolKind.ModbusTcp || kind == ProtocolKind.ModbusRtu ||
                   kind == ProtocolKind.SiemensS7 || kind == ProtocolKind.MitsubishiMc
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        public static StringComparer ForProtocol(string protocol)
        {
            return protocol == "modbus-tcp" || protocol == "modbus-rtu" ||
                   protocol == "siemens-s7" || protocol == "mitsubishi-mc"
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }
    }
}
