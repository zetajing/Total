using System.Collections.Generic;
using System.Runtime.Serialization;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 表示 modbus-profiles.json 的根对象，包含一组设备配置文件定义。
    /// </summary>
    [DataContract]
    public sealed class ModbusProfileDefinitionCollection
    {
        /// <summary>获取或设置设备配置文件列表。</summary>
        [DataMember(Name = "profiles")]
        public List<ModbusProfileDefinition> Profiles { get; set; } = new List<ModbusProfileDefinition>();
    }

    /// <summary>
    /// 描述一个 Modbus 设备配置文件的 JSON 数据模型。
    /// 包含地址解析规则、映射表和字节序配置，是 <see cref="JsonModbusProfile"/> 的数据源。
    /// </summary>
    [DataContract]
    public sealed class ModbusProfileDefinition
    {
        /// <summary>唯一标识键，例如 "inovance-easyplc"。</summary>
        [DataMember(Name = "key")]
        public string Key { get; set; }

        /// <summary>用户界面显示名称，例如 "Inovance EasyPLC"。</summary>
        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; }

        /// <summary>默认地址示例，例如 "D100"。</summary>
        [DataMember(Name = "defaultAddress")]
        public string DefaultAddress { get; set; }

        /// <summary>逗号分隔的示例地址列表。</summary>
        [DataMember(Name = "exampleAddresses")]
        public string ExampleAddresses { get; set; }

        /// <summary>
        /// 用于解析地址的正则表达式。必须包含两个捕获组：组 1 为前缀（prefix），组 2 为索引（index）。
        /// 例如：^([A-Z])(\d+)$ 或 ^([A-Z]{1,2})([0-9A-F]+)$
        /// </summary>
        [DataMember(Name = "addressPattern")]
        public string AddressPattern { get; set; }

        /// <summary>多寄存器数据类型（Int32/UInt32/Float/Double）是否需要低字在前（Low-Word First）交换。</summary>
        [DataMember(Name = "lowWordFirst")]
        public bool LowWordFirst { get; set; }

        /// <summary>前缀到 Modbus 地址的映射规则表。</summary>
        [DataMember(Name = "mappings")]
        public List<ModbusProfileMapping> Mappings { get; set; } = new List<ModbusProfileMapping>();
    }

    /// <summary>
    /// 描述单个前缀（如 "D"、"M"、"X"）到 Modbus 区域的映射规则。
    /// </summary>
    [DataContract]
    public sealed class ModbusProfileMapping
    {
        /// <summary>地址前缀，例如 "D"、"M"、"SM"、"X"。</summary>
        [DataMember(Name = "prefix")]
        public string Prefix { get; set; }

        /// <summary>Modbus 区域名称：Coil、DiscreteInput、InputRegister、HoldingRegister。</summary>
        [DataMember(Name = "area")]
        public string Area { get; set; }

        /// <summary>Modbus 基址偏移量。</summary>
        [DataMember(Name = "base")]
        public ushort Base { get; set; }

        /// <summary>最大索引数量（0 到 max - 1 有效）。</summary>
        [DataMember(Name = "max")]
        public int Max { get; set; }

        /// <summary>索引进制：decimal（十进制）、hex（十六进制）、octal（八进制）。</summary>
        [DataMember(Name = "radix")]
        public string Radix { get; set; } = "decimal";
    }
}
