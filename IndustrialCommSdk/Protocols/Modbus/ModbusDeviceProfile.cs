using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
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
    /// 可通过 <see cref="LoadJsonProfiles"/> 加载 JSON 配置的外置品牌映射。
    /// </remarks>
    public static class ModbusDeviceProfiles
    {
        /// <summary>获取不含品牌地址映射的通用 Modbus 配置。</summary>
        public static GenericModbusProfile Generic { get; } = new GenericModbusProfile();

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
        /// 获取三菱 PLC 的 Modbus TCP 设备配置文件实例。
        /// </summary>
        /// <value>
        /// <see cref="MitsubishiModbusTcpProfile"/> 的单例实例，
        /// 提供三菱常见 D/R/W/M/X/Y/L 地址的 Modbus 映射规则。
        /// </value>
        public static MitsubishiModbusTcpProfile MitsubishiModbusTcp { get; } = new MitsubishiModbusTcpProfile();

        // 通过 JSON 注册的外置设备配置文件
        private static readonly Dictionary<string, IModbusDeviceProfile> _jsonProfiles =
            new Dictionary<string, IModbusDeviceProfile>(StringComparer.OrdinalIgnoreCase);

        // 标记是否已尝试从默认路径加载
        private static bool _defaultLoaded;

        /// <summary>
        /// 获取所有已注册的设备配置文件的只读列表。
        /// 包含内置配置文件（Generic、InovanceEasyPlc、MitsubishiModbusTcp）
        /// 以及通过 <see cref="LoadJsonProfiles"/> 加载的所有 JSON 配置文件。
        /// </summary>
        /// <value>
        /// 一个 <see cref="IReadOnlyList{T}"/>，包含所有可用的
        /// <see cref="IModbusDeviceProfile"/> 实现。
        /// </value>
        public static IReadOnlyList<IModbusDeviceProfile> All
        {
            get
            {
                TryLoadDefaultConfig();
                var list = new List<IModbusDeviceProfile> { Generic, InovanceEasyPlc, MitsubishiModbusTcp };
                list.AddRange(_jsonProfiles.Values);
                return list.AsReadOnly();
            }
        }

        /// <summary>
        /// 从 JSON 文件加载设备配置文件并注册到全局配置库。
        /// 已存在的同 key 配置会被覆盖（大小写不敏感）。
        /// </summary>
        /// <param name="filePath">modbus-profiles.json 的完整路径。</param>
        /// <exception cref="System.ArgumentException">filePath 为 null 或空。</exception>
        /// <exception cref="System.IO.FileNotFoundException">文件不存在。</exception>
        /// <exception cref="System.Runtime.Serialization.SerializationException">JSON 格式无效。</exception>
        public static void LoadJsonProfiles(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new System.ArgumentException("File path cannot be null or empty.", nameof(filePath));

            var collection = LoadDefinitionCollection(filePath);
            if (collection?.Profiles == null || collection.Profiles.Count == 0)
                return;

            foreach (var definition in collection.Profiles)
            {
                if (string.IsNullOrWhiteSpace(definition.Key))
                    continue;

                _jsonProfiles[definition.Key] = new JsonModbusProfile(definition);
            }
        }

        /// <summary>
        /// 按 key 查找设备配置文件。先在硬编码配置中查找，找不到再去 JSON 注册表中查找。
        /// 如果都找不到则返回 null。
        /// </summary>
        /// <param name="key">配置文件标识键（大小写不敏感）。</param>
        /// <returns>匹配的 <see cref="IModbusDeviceProfile"/>，或 null。</returns>
        public static IModbusDeviceProfile Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var normalized = NormalizeToken(key);

            // 检查硬编码配置
            if (normalized == "generic" || normalized == "modbus")
                return Generic;

            foreach (var profile in new[] { (IModbusDeviceProfile)InovanceEasyPlc, MitsubishiModbusTcp })
            {
                if (NormalizeToken(profile.Key) == normalized || NormalizeToken(profile.DisplayName) == normalized)
                    return profile;
            }

            // 检查 JSON 注册表
            TryLoadDefaultConfig();
            IModbusDeviceProfile jsonProfile;
            if (_jsonProfiles.TryGetValue(key, out jsonProfile))
                return jsonProfile;

            // 也允许用 NormalizeToken 匹配 JSON Profile 的 Key
            foreach (var kvp in _jsonProfiles)
            {
                if (NormalizeToken(kvp.Key) == normalized || NormalizeToken(kvp.Value.DisplayName) == normalized)
                    return kvp.Value;
            }

            return null;
        }

        /// <summary>
        /// 尝试从默认路径加载 modbus-profiles.json。
        /// 查找顺序：当前工作目录下的 Config/modbus-profiles.json。
        /// 文件不存在时静默跳过。
        /// </summary>
        private static void TryLoadDefaultConfig()
        {
            if (_defaultLoaded)
                return;

            _defaultLoaded = true;

            try
            {
                var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "Config", "modbus-profiles.json");
                if (File.Exists(defaultPath))
                {
                    LoadJsonProfiles(defaultPath);
                }
            }
            catch
            {
                // 默认配置文件可选，静默忽略加载失败
            }
        }

        private static ModbusProfileDefinitionCollection LoadDefinitionCollection(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                var serializer = new DataContractJsonSerializer(typeof(ModbusProfileDefinitionCollection));
                return (ModbusProfileDefinitionCollection)serializer.ReadObject(stream);
            }
        }

        private static string NormalizeToken(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();
        }
    }
}
