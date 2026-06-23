using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 汇川 EasyPLC（Inovance EasyPLC）的 Modbus 设备配置文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类实现了 <see cref="IModbusDeviceProfile"/> 接口，为汇川 EasyPLC 系列 PLC
    /// 提供专用的地址解析和寄存器数据规范化功能。
    /// </para>
    /// <para>
    /// 汇川 EasyPLC 支持以下地址类型：
    /// <list type="table">
    ///   <item><term>D（数据寄存器）</term><description>保持寄存器，地址范围 0-7999</description></item>
    ///   <item><term>R（扩展寄存器）</term><description>保持寄存器，地址范围 3000-35767</description></item>
    ///   <item><term>M（中间继电器）</term><description>线圈，地址范围 0-7999</description></item>
    ///   <item><term>B（辅助继电器）</term><description>线圈，地址范围 3000-35767</description></item>
    ///   <item><term>S（状态继电器）</term><description>线圈，地址范围 57344-61439</description></item>
    ///   <item><term>X（输入点）</term><description>离散量输入，地址范围 63488-64511，使用八进制索引</description></item>
    ///   <item><term>Y（输出点）</term><description>线圈，地址范围 64512-65023，使用八进制索引</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 对于 32 位及以上数据类型（Int32、UInt32、Float、Double），设备采用低字在前
    /// （Low-Word First）的字节序，在读写时需要进行寄存器反转操作。
    /// </para>
    /// </remarks>
    public sealed class InovanceEasyPlcModbusProfile : IModbusDeviceProfile
    {
        /// <summary>
        /// 地址映射表，将地址类型前缀字符映射到对应的地址解析规则。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 字典键为地址类型前缀字符（如 'D'、'R'、'M' 等），值为包含以下信息的元组：
        /// <list type="bullet">
        ///   <item>Item1（ushort）：Modbus 基地址偏移量。</item>
        ///   <item>Item2（ModbusArea）：地址所属的 Modbus 区域。</item>
        ///   <item>Item3（int）：该地址类型的最大索引数量（用于范围检查）。</item>
        ///   <item>Item4（bool）：是否使用八进制解析索引（true 表示 X、Y 地址使用八进制）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 支持的地址类型：
        /// <list type="table">
        ///   <item><term>D</term><description>数据寄存器，基地址 0x0000，保持寄存器，8000 个，十进制索引</description></item>
        ///   <item><term>R</term><description>扩展寄存器，基地址 0x3000，保持寄存器，32768 个，十进制索引</description></item>
        ///   <item><term>M</term><description>中间继电器，基地址 0x0000，线圈，8000 个，十进制索引</description></item>
        ///   <item><term>B</term><description>辅助继电器，基地址 0x3000，线圈，32768 个，十进制索引</description></item>
        ///   <item><term>S</term><description>状态继电器，基地址 0xE000，线圈，4096 个，十进制索引</description></item>
        ///   <item><term>X</term><description>输入点，基地址 0xF800，离散量输入，1024 个，八进制索引</description></item>
        ///   <item><term>Y</term><description>输出点，基地址 0xFC00，线圈，1024 个，八进制索引</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        private static readonly Dictionary<char, Tuple<ushort, ModbusArea, int, bool>> AddressMap = new Dictionary<char, Tuple<ushort, ModbusArea, int, bool>>
        {
            { 'D', Tuple.Create((ushort)0x0000, ModbusArea.HoldingRegister, 8000, false) },
            { 'R', Tuple.Create((ushort)0x3000, ModbusArea.HoldingRegister, 32768, false) },
            { 'M', Tuple.Create((ushort)0x0000, ModbusArea.Coil, 8000, false) },
            { 'B', Tuple.Create((ushort)0x3000, ModbusArea.Coil, 32768, false) },
            { 'S', Tuple.Create((ushort)0xE000, ModbusArea.Coil, 4096, false) },
            { 'X', Tuple.Create((ushort)0xF800, ModbusArea.DiscreteInput, 1024, true) },
            { 'Y', Tuple.Create((ushort)0xFC00, ModbusArea.Coil, 1024, true) },
        };

        /// <summary>
        /// 获取设备配置文件的唯一标识键。
        /// </summary>
        /// <value>
        /// 返回字符串 "inovance-easyplc"，用于在配置中唯一标识汇川 EasyPLC 设备类型。
        /// </value>
        public string Key
        {
            get { return "inovance-easyplc"; }
        }

        /// <summary>
        /// 获取设备配置文件的显示名称。
        /// </summary>
        /// <value>
        /// 返回字符串 "Inovance EasyPLC"，用于在用户界面中显示设备类型名称。
        /// </value>
        public string DisplayName
        {
            get { return "Inovance EasyPLC"; }
        }

        /// <summary>
        /// 获取设备的默认示例地址。
        /// </summary>
        /// <value>
        /// 返回字符串 "D100"，在未指定地址时作为默认值使用。
        /// </value>
        public string DefaultAddress
        {
            get { return "D100"; }
        }

        /// <summary>
        /// 获取设备支持的示例地址列表。
        /// </summary>
        /// <value>
        /// 返回字符串 "D100, R200, M0, S10, B5, X17, Y20"，
        /// 涵盖了所有支持的地址类型，用于在用户界面中为用户提供地址格式参考。
        /// </value>
        public string ExampleAddresses
        {
            get { return "D100, R200, M0, S10, B5, X17, Y20"; }
        }

        /// <summary>
        /// 将汇川 EasyPLC 的地址字符串解析为 <see cref="ModbusAddress"/> 对象。
        /// </summary>
        /// <param name="address">
        /// 要解析的地址字符串，格式为 "A123"，其中 A 为地址类型前缀（D/R/M/B/S/X/Y），
        /// 123 为索引号。X 和 Y 地址的索引使用八进制表示，其余地址类型使用十进制表示。
        /// 例如："D100"、"X17"、"Y20"。
        /// </param>
        /// <returns>
        /// 解析后的 <see cref="ModbusAddress"/> 对象，包含 <see cref="ModbusArea"/> 区域
        /// 和计算得到的基于零的 Modbus 绝对地址。
        /// </returns>
        /// <exception cref="IndustrialAddressParseException">
        /// <paramref name="address"/> 为 <c>null</c> 或空字符串时抛出。
        /// </exception>
        /// <exception cref="IndustrialAddressParseException">
        /// 地址格式无法匹配正则表达式（如缺少索引号或包含非法字符）时抛出。
        /// </exception>
        /// <exception cref="IndustrialAddressParseException">
        /// 地址类型前缀不在支持的范围（D/R/M/B/S/X/Y）内时抛出。
        /// </exception>
        /// <exception cref="IndustrialAddressParseException">
        /// 索引号无法解析（如八进制解析失败）或超出该地址类型的最大范围时抛出。
        /// </exception>
        /// <remarks>
        /// 解析过程：
        /// <list type="number">
        ///   <item>去除前后空白并将地址转换为大写。</item>
        ///   <item>使用正则表达式 <c>^([A-Z])(\d+)$</c> 验证地址格式。</item>
        ///   <item>根据地址类型前缀在 <see cref="AddressMap"/> 中查找映射规则。</item>
        ///   <item>根据规则中的八进制标志决定使用八进制还是十进制解析索引号。</item>
        ///   <item>验证索引号是否在有效范围内（0 到最大索引数量之间）。</item>
        ///   <item>计算最终 Modbus 地址 = 基地址偏移量 + 索引号。</item>
        /// </list>
        /// </remarks>
        public ModbusAddress ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new IndustrialAddressParseException("Address is required.");
            }

            var match = Regex.Match(address.Trim().ToUpperInvariant(), @"^([A-Z])(\d+)$");
            if (!match.Success)
            {
                throw new IndustrialAddressParseException(string.Format("Unsupported Inovance PLC variable: {0}", address));
            }

            var type = match.Groups[1].Value[0];
            Tuple<ushort, ModbusArea, int, bool> rule;
            if (!AddressMap.TryGetValue(type, out rule))
            {
                throw new IndustrialAddressParseException(string.Format("Unsupported Inovance PLC variable type: {0}", type));
            }

            int index;
            try
            {
                index = rule.Item4 ? Convert.ToInt32(match.Groups[2].Value, 8) : int.Parse(match.Groups[2].Value);
            }
            catch (Exception ex)
            {
                throw new IndustrialAddressParseException("Invalid Inovance PLC variable index.", ex);
            }

            if (index < 0 || index >= rule.Item3)
            {
                throw new IndustrialAddressParseException("Inovance PLC variable index out of range.");
            }

            return new ModbusAddress(rule.Item2, (ushort)(rule.Item1 + index));
        }

        /// <summary>
        /// 将从汇川 EasyPLC 读取的寄存器数据规范化为当前平台所需的字节序。
        /// </summary>
        /// <param name="dataType">读取操作的目标数据类型。</param>
        /// <param name="registers">
        /// 从设备读取的原始寄存器数组（16 位无符号整数数组）。
        /// </param>
        /// <returns>
        /// 规范化后的寄存器数组。对于需要低字在前（Low-Word First）转换的数据类型
        /// （Int32、UInt32、Float、Double），返回反转后的寄存器数组；
        /// 其他数据类型直接返回原始数组。
        /// </returns>
        /// <remarks>
        /// 汇川 EasyPLC 在多字数据类型（如 32 位整数、浮点数）的传输中，
        /// 采用低字在前（Low-Word First，即小端序）的方式。
        /// 例如，32 位值 0x12345678 在寄存器中的表示为 [0x5678, 0x1234]。
        /// 此方法通过反转寄存器数组将其转换为高字在前（Big-Endian）的表示。
        /// </remarks>
        public ushort[] NormalizeRegistersForRead(DataType dataType, ushort[] registers)
        {
            return RequiresLowWordFirst(dataType) ? ReverseRegisters(registers) : registers;
        }

        /// <summary>
        /// 将写入汇川 EasyPLC 的寄存器数据从当前平台字节序规范化为设备所需的字节序。
        /// </summary>
        /// <param name="dataType">写入操作的目标数据类型。</param>
        /// <param name="registers">
        /// 当前平台上的寄存器数组（16 位无符号整数数组），准备写入设备。
        /// </param>
        /// <returns>
        /// 规范化后的寄存器数组。对于需要低字在前（Low-Word First）转换的数据类型
        /// （Int32、UInt32、Float、Double），返回反转后的寄存器数组；
        /// 其他数据类型直接返回原始数组。
        /// </returns>
        /// <remarks>
        /// <see cref="NormalizeRegistersForWrite"/> 是 <see cref="NormalizeRegistersForRead"/>
        /// 的逆操作。对于采用低字在前字节序的设备，写入前需要将当前平台的高字在前表示
        /// 反转回设备的低字在前表示，因此逻辑与读取时相同（均执行反转操作）。
        /// </remarks>
        public ushort[] NormalizeRegistersForWrite(DataType dataType, ushort[] registers)
        {
            return RequiresLowWordFirst(dataType) ? ReverseRegisters(registers) : registers;
        }

        /// <summary>
        /// 确定指定的数据类型是否需要低字在前（Low-Word First）的字节序转换。
        /// </summary>
        /// <param name="dataType">要检查的数据类型。</param>
        /// <returns>
        /// 如果 <paramref name="dataType"/> 为 <see cref="DataType.Int32"/>、
        /// <see cref="DataType.UInt32"/>、<see cref="DataType.Float"/> 或
        /// <see cref="DataType.Double"/>，则为 <c>true</c>；否则为 <c>false</c>。
        /// </returns>
        /// <remarks>
        /// 汇川 EasyPLC 对占用多个寄存器的数据类型（32 位及以上）采用低字在前的方式传输。
        /// 单寄存器数据类型（如 Int16、UInt16）和位数据类型不需要进行字节序转换。
        /// </remarks>
        private static bool RequiresLowWordFirst(DataType dataType)
        {
            switch (dataType)
            {
                case DataType.Int32:
                case DataType.UInt32:
                case DataType.Float:
                case DataType.Double:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 反转寄存器数组的顺序，用于低字在前（Low-Word First）和
        /// 高字在前（High-Word First）之间的字节序转换。
        /// </summary>
        /// <param name="registers">
        /// 要反转的寄存器数组。如果为 <c>null</c> 或长度小于等于 1，则直接返回原数组。
        /// </param>
        /// <returns>
        /// 反转顺序后的新寄存器数组副本；如果输入为 <c>null</c> 或长度小于等于 1，
        /// 则直接返回原始引用（不创建副本）。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 此方法通过 <see cref="Buffer.BlockCopy"/> 高效复制数组内容，
        /// 然后使用 <see cref="Array.Reverse(Array)"/> 反转顺序。
        /// 返回的是数组的副本，不会修改原始数组。
        /// </para>
        /// <para>
        /// 例如，输入 [0x5678, 0x1234] 将反转为 [0x1234, 0x5678]。
        /// 这使得低字在前（0x5678, 0x1234 表示 0x12345678）转换为高字在前
        /// （0x1234, 0x5678 表示 0x12345678），反之亦然。
        /// </para>
        /// </remarks>
        private static ushort[] ReverseRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length <= 1)
            {
                return registers;
            }

            var buffer = new ushort[registers.Length];
            Buffer.BlockCopy(registers, 0, buffer, 0, registers.Length * sizeof(ushort));
            Array.Reverse(buffer);
            return buffer;
        }
    }
}
