using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 定义 Modbus 设备配置文件的接口，提供地址解析和寄存器规范化功能。
    /// </summary>
    /// <remarks>
    /// <see cref="IModbusDeviceProfile"/> 是 Modbus 协议中设备相关信息的抽象层。
    /// 不同的 Modbus 设备（如汇川 EasyPLC、西门子 S7 等）可能使用不同的地址格式和字节序，
    /// 通过实现此接口可以为每种设备提供特定的地址解析和寄存器数据规范化逻辑。
    /// </remarks>
    public interface IModbusDeviceProfile
    {
        /// <summary>
        /// 获取设备配置文件的唯一标识键。
        /// </summary>
        /// <value>
        /// 一个字符串，用于唯一标识此设备配置文件，例如 "inovance-easyplc"。
        /// 该键通常用于配置文件中选择设备类型。
        /// </value>
        string Key { get; }

        /// <summary>
        /// 获取设备配置文件的显示名称。
        /// </summary>
        /// <value>
        /// 一个人类可读的字符串，用于在用户界面中显示设备类型名称，例如 "Inovance EasyPLC"。
        /// </value>
        string DisplayName { get; }

        /// <summary>
        /// 获取设备默认的示例地址。
        /// </summary>
        /// <value>
        /// 一个地址字符串，例如 "D100"，在用户未指定地址时作为默认值使用。
        /// </value>
        string DefaultAddress { get; }

        /// <summary>
        /// 获取设备支持的示例地址列表（以逗号分隔的字符串形式）。
        /// </summary>
        /// <value>
        /// 一个包含多个示例地址的字符串，例如 "D100, R200, M0, S10, B5, X17, Y20"。
        /// 用于在用户界面中为用户提供地址格式参考。
        /// </value>
        string ExampleAddresses { get; }

        /// <summary>
        /// 将设备特定的地址字符串解析为 <see cref="ModbusAddress"/> 对象。
        /// </summary>
        /// <param name="address">
        /// 设备特定的地址字符串，例如 "D100"、"M0"、"X17" 等。
        /// 格式由设备配置文件的具体实现定义。
        /// </param>
        /// <returns>
        /// 解析后的 <see cref="ModbusAddress"/> 对象，包含 Modbus 区域和基于零的偏移地址。
        /// </returns>
        /// <exception cref="IndustrialAddressParseException">
        /// 当地址格式无法识别、地址类型不受支持或地址索引超出范围时抛出。
        /// </exception>
        ModbusAddress ParseAddress(string address);

        /// <summary>
        /// 将从设备读取的原始寄存器数据规范化为当前平台所需的字节序。
        /// </summary>
        /// <param name="dataType">读取操作的目标数据类型。</param>
        /// <param name="registers">
        /// 从设备读取的原始寄存器数组（16 位无符号整数数组）。
        /// </param>
        /// <returns>
        /// 规范化后的寄存器数组，适用于当前平台的字节序。
        /// 对于不需要字节序转换的数据类型，直接返回原始数组。
        /// </returns>
        /// <remarks>
        /// 某些设备（如汇川 EasyPLC）在传输多字数据类型（如 Int32、Float）时，
        /// 采用低字在前（Low-Word First）的字节序。此方法将设备端的字节序
        /// 转换为当前平台预期的字节序。
        /// </remarks>
        ushort[] NormalizeRegistersForRead(DataType dataType, ushort[] registers);

        /// <summary>
        /// 将写入设备的寄存器数据从当前平台字节序规范化为设备所需的字节序。
        /// </summary>
        /// <param name="dataType">写入操作的目标数据类型。</param>
        /// <param name="registers">
        /// 当前平台上的寄存器数组（16 位无符号整数数组），准备写入设备。
        /// </param>
        /// <returns>
        /// 规范化后的寄存器数组，适用于目标设备的字节序。
        /// 对于不需要字节序转换的数据类型，直接返回原始数组。
        /// </returns>
        /// <remarks>
        /// <see cref="NormalizeRegistersForWrite"/> 是
        /// <see cref="NormalizeRegistersForRead"/> 的逆操作。
        /// 如果读取时需要反转字节序，则写入时也需要进行相同的反转操作，
        /// 以确保数据以正确的字节序写入设备。
        /// </remarks>
        ushort[] NormalizeRegistersForWrite(DataType dataType, ushort[] registers);
    }

    /// <summary>
    /// 提供预定义的 Modbus 设备配置文件集合。
    /// </summary>
    /// <remarks>
    /// <see cref="ModbusDeviceProfiles"/> 是一个静态类，充当设备配置文件的注册中心。
    /// 所有内置支持的设备配置文件都作为静态属性在此公开，
    /// 可通过 <see cref="All"/> 属性获取完整的配置文件列表。
    /// </remarks>
    public static class ModbusDeviceProfiles
    {
        /// <summary>
        /// 获取汇川 EasyPLC（Inovance EasyPLC）的设备配置文件实例。
        /// </summary>
        /// <value>
        /// <see cref="InovanceEasyPlcModbusProfile"/> 的单例实例，
        /// 提供汇川 EasyPLC 的地址映射和寄存器规范化规则。
        /// </value>
        /// <remarks>
        /// 汇川 EasyPLC 支持 D（数据寄存器）、R（扩展寄存器）、M（中间继电器）、
        /// S（状态继电器）、B（辅助继电器）、X（输入点）、Y（输出点）等地址类型。
        /// 其中 X 和 Y 地址使用八进制索引。
        /// </remarks>
        public static InovanceEasyPlcModbusProfile InovanceEasyPlc { get; } = new InovanceEasyPlcModbusProfile();

        /// <summary>
        /// 获取所有已注册的设备配置文件的只读列表。
        /// </summary>
        /// <value>
        /// 一个 <see cref="IReadOnlyList{T}"/>，包含所有内置支持的
        /// <see cref="IModbusDeviceProfile"/> 实现。
        /// </value>
        /// <remarks>
        /// 此列表可用于在用户界面中提供设备类型选择选项，
        /// 或在需要遍历所有支持的设备时使用。
        /// 当前包含的设备：<see cref="InovanceEasyPlc"/>。
        /// </remarks>
        public static IReadOnlyList<IModbusDeviceProfile> All { get; } = new IModbusDeviceProfile[]
        {
            InovanceEasyPlc,
        };
    }
}
