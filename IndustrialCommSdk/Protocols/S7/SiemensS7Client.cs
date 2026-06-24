using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using S7.Net;
using PlcArea = S7.Net.DataType;
using PlcVarType = S7.Net.VarType;

namespace IndustrialCommSdk.Protocols.S7
{
    /// <summary>
    /// 表示西门子 S7 PLC 的数据区域类型。
    /// </summary>
    public enum S7Area
    {
        /// <summary>数据块（Data Block）区域。</summary>
        Db,
        /// <summary>存储器（Memory / Merker）区域。</summary>
        Memory,
        /// <summary>输入（Input）区域。</summary>
        Input,
        /// <summary>输出（Output）区域。</summary>
        Output,
    }

    /// <summary>
    /// 表示一个解析后的西门子 S7 地址，包含区域、数据块编号、字节偏移、位偏移及归一化地址字符串。
    /// </summary>
    public sealed class S7Address
    {
        /// <summary>
        /// 初始化 <see cref="S7Address"/> 的新实例。
        /// </summary>
        /// <param name="area">S7 数据区域。</param>
        /// <param name="dbNumber">数据块编号（非 DB 区域时为 0）。</param>
        /// <param name="byteOffset">字节偏移量。</param>
        /// <param name="bitOffset">位偏移量（未指定位时为 -1）。</param>
        /// <param name="normalized">归一化后的地址字符串。</param>
        public S7Address(S7Area area, int dbNumber, int byteOffset, int bitOffset, string normalized)
        {
            Area = area;
            DbNumber = dbNumber;
            ByteOffset = byteOffset;
            BitOffset = bitOffset;
            Normalized = normalized;
        }

        /// <summary>获取 S7 数据区域。</summary>
        public S7Area Area { get; private set; }
        /// <summary>获取数据块编号。对于非 DB 区域，此值为 0。</summary>
        public int DbNumber { get; private set; }
        /// <summary>获取字节偏移量。</summary>
        public int ByteOffset { get; private set; }
        /// <summary>获取位偏移量。若地址未指定位，返回 -1。</summary>
        public int BitOffset { get; private set; }
        /// <summary>获取归一化后的地址字符串，用于直接传递给底层库。</summary>
        public string Normalized { get; private set; }
    }

    /// <summary>
    /// 西门子 S7 地址解析器。支持 DB 块地址（如 DB1.DBX12.3）和区域地址（如 M10.2、I0.0、Q1.5）。
    /// </summary>
    public sealed class S7AddressParser : IAddressParser
    {
        /// <summary>
        /// 匹配 DB 块地址的正则表达式，如 "DB1.DBX12.3"、"DB100.DBD200"。
        /// 分组：db（块号）、type（数据类型 X/B/W/D/L）、offset（偏移）、bit（可选位）。
        /// </summary>
        private static readonly Regex DbRegex = new Regex(@"^DB(?<db>\d+)\.DB(?<type>[XBWDL])(?<offset>\d+)(\.(?<bit>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 匹配区域地址的正则表达式，如 "M10.2"、"I0.0"、"Q1.5"、"M100"。
        /// 分组：area（区域 M/I/Q）、type（可选数据类型）、offset（偏移）、bit（可选位）。
        /// </summary>
        private static readonly Regex AreaRegex = new Regex(@"^(?<area>[MIQ])(?<type>[XBWDL])?(?<offset>\d+)(\.(?<bit>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 将 S7 地址字符串解析为 <see cref="S7Address"/> 对象。
        /// </summary>
        /// <param name="address">S7 地址字符串，例如 "DB1.DBX12.3"、"M10.2"、"I0.0"。</param>
        /// <returns>解析后的 <see cref="S7Address"/> 对象。</returns>
        /// <exception cref="IndustrialAddressParseException">地址格式无效或区域不受支持时引发。</exception>
        public object Parse(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new IndustrialAddressParseException("Address is required.");
            }

            var input = NormalizeAddress(address);
            var dbMatch = DbRegex.Match(input);
            if (dbMatch.Success)
            {
                return new S7Address(S7Area.Db, int.Parse(dbMatch.Groups["db"].Value), int.Parse(dbMatch.Groups["offset"].Value), ParseBit(dbMatch.Groups["bit"].Value), input);
            }

            var areaMatch = AreaRegex.Match(input);
            if (areaMatch.Success)
            {
                var area = ParseArea(areaMatch.Groups["area"].Value);
                return new S7Address(area, 0, int.Parse(areaMatch.Groups["offset"].Value), ParseBit(areaMatch.Groups["bit"].Value), input);
            }

            throw new IndustrialAddressParseException(string.Format("Unsupported S7 address: {0}", address));
        }

        private static string NormalizeAddress(string address)
        {
            var input = address.Trim().ToUpperInvariant();
            if (input.StartsWith("P#", StringComparison.Ordinal))
            {
                input = input.Substring(2);
            }

            if (input.StartsWith("%", StringComparison.Ordinal))
            {
                input = input.Substring(1);
            }

            return input;
        }

        /// <summary>
        /// 将区域字符串转换为 <see cref="S7Area"/> 枚举值。
        /// </summary>
        /// <param name="token">区域字符串：M（存储器）、I（输入）、Q（输出）。</param>
        /// <returns>对应的 <see cref="S7Area"/> 枚举值。</returns>
        /// <exception cref="IndustrialAddressParseException">遇到不受支持的区域时引发。</exception>
        private static S7Area ParseArea(string token)
        {
            switch (token)
            {
                case "M": return S7Area.Memory;
                case "I": return S7Area.Input;
                case "Q": return S7Area.Output;
                default: throw new IndustrialAddressParseException("Unsupported S7 area.");
            }
        }

        /// <summary>
        /// 将位偏移字符串解析为整数。若字符串为空或空白，返回 -1。
        /// </summary>
        /// <param name="token">位偏移字符串，例如 "3"。</param>
        /// <returns>位偏移值；若未指定则返回 -1。</returns>
        private static int ParseBit(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return -1;
            }

            var bit = int.Parse(token);
            if (bit < 0 || bit > 7)
            {
                throw new IndustrialAddressParseException("S7 bit offset must be in the range 0-7.");
            }

            return bit;
        }
    }

    /// <summary>
    /// 西门子 S7 客户端的配置选项。
    /// </summary>
    public sealed class SiemensS7ClientOptions
    {
        /// <summary>获取或设置设备标识符。</summary>
        public string DeviceId { get; set; }
        /// <summary>获取或设置 S7 PLC 的主机地址（IP 或主机名）。</summary>
        public string Host { get; set; }
        /// <summary>获取或设置 PLC 的机架号（Rack）。</summary>
        public short Rack { get; set; }
        /// <summary>获取或设置 PLC 的槽位号（Slot）。默认值为 1。</summary>
        public short Slot { get; set; } = 1;
        /// <summary>获取或设置 S7 PLC 的 CPU 类型。默认值为 <see cref="S7.Net.CpuType.S71200"/>。</summary>
        public CpuType CpuType { get; set; } = CpuType.S71200;
    }

    /// <summary>
    /// 西门子 S7 协议客户端。通过 TCP 与 S7 PLC 通信，支持读写数据块、存储器、输入和输出区域。
    /// </summary>
    public sealed class SiemensS7Client : IndustrialClientBase
    {
        private readonly SiemensS7ClientOptions _options;
        private readonly S7AddressParser _parser;
        private Plc _plc;

        /// <summary>
        /// 初始化 <see cref="SiemensS7Client"/> 的新实例。
        /// </summary>
        /// <param name="options">S7 客户端配置选项。</param>
        /// <param name="logger">可选的日志记录器实例。</param>
        /// <param name="pollingScheduler">可选的轮询调度器实例。</param>
        /// <param name="parser">可选的 S7 地址解析器实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null 时引发。</exception>
        public SiemensS7Client(SiemensS7ClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null, S7AddressParser parser = null)
            : base(options.DeviceId, ProtocolKind.SiemensS7, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _parser = parser ?? new S7AddressParser();
        }

        /// <summary>
        /// 获取一个值，指示客户端是否已成功连接到 S7 PLC。
        /// </summary>
        public override bool IsConnected
        {
            get { return _plc != null && _plc.IsConnected; }
        }

        /// <summary>
        /// 建立与 S7 PLC 的连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            _plc = new Plc(_options.CpuType, _options.Host, _options.Rack, _options.Slot);
            await _plc.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 断开与 S7 PLC 的连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            if (_plc != null)
            {
                _plc.Close();
                _plc = null;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 从 S7 PLC 读取数据。
        /// </summary>
        /// <param name="request">读取请求，包含地址和数据类型信息。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>包含读取结果的数据值。</returns>
        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = (S7Address)_parser.Parse(request.Address);
            var value = await ReadValueAsync(parsed, request, cancellationToken).ConfigureAwait(false);
            return new DataValue(request.Address, request.DataType, value, null, QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        /// <summary>
        /// 向 S7 PLC 写入数据。
        /// </summary>
        /// <param name="request">写入请求，包含地址和待写入的值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var parsed = (S7Address)_parser.Parse(request.Address);
            await WriteValueAsync(parsed, request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 使用与 PLC DB 块布局一致的 C# 类读取一整段数据，并返回填充后的实例。
        /// 适合像 <c>DB9 model = await client.ReadDbClassAsync&lt;DB9&gt;(9);</c> 这样的用法。
        /// </summary>
        /// <typeparam name="T">要映射的 DB 模型类型，需具备无参构造函数。</typeparam>
        /// <param name="dbNumber">DB 块号，例如 9 表示 DB9。</param>
        /// <param name="startByteAddress">起始字节偏移，默认从 0 开始。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>填充后的模型实例。</returns>
        public Task<T> ReadDbClassAsync<T>(int dbNumber, int startByteAddress = 0, CancellationToken cancellationToken = default(CancellationToken))
            where T : class, new()
        {
            ValidateDbBlockArguments(dbNumber, startByteAddress);

            return ExecuteExclusiveAsync(
                async token =>
                {
                    EnsureConnected();
                    var value = await _plc.ReadClassAsync<T>(dbNumber, startByteAddress, token).ConfigureAwait(false);
                    if (value == null)
                    {
                        throw new IndustrialConnectionException(string.Format("S7 DB{0} read returned no data.", dbNumber));
                    }

                    return value;
                },
                cancellationToken);
        }

        /// <summary>
        /// 使用自定义工厂创建 DB 模型实例，然后从 PLC 读取并填充其属性。
        /// 当模型没有无参构造函数或需要预设默认值时适用。
        /// </summary>
        /// <typeparam name="T">要映射的 DB 模型类型。</typeparam>
        /// <param name="factory">模型工厂方法。</param>
        /// <param name="dbNumber">DB 块号，例如 9 表示 DB9。</param>
        /// <param name="startByteAddress">起始字节偏移，默认从 0 开始。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>填充后的模型实例。</returns>
        public Task<T> ReadDbClassAsync<T>(Func<T> factory, int dbNumber, int startByteAddress = 0, CancellationToken cancellationToken = default(CancellationToken))
            where T : class
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            ValidateDbBlockArguments(dbNumber, startByteAddress);

            return ExecuteExclusiveAsync(
                async token =>
                {
                    EnsureConnected();
                    var value = await _plc.ReadClassAsync(factory, dbNumber, startByteAddress, token).ConfigureAwait(false);
                    if (value == null)
                    {
                        throw new IndustrialConnectionException(string.Format("S7 DB{0} read returned no data.", dbNumber));
                    }

                    return value;
                },
                cancellationToken);
        }

        /// <summary>
        /// 将 PLC 中的 DB 块内容读取到调用方已经创建好的实例中。
        /// 这与现有 WinForms 项目里“先 new 类，再读 DB”的用法一致。
        /// </summary>
        /// <param name="instance">待填充的模型实例。</param>
        /// <param name="dbNumber">DB 块号，例如 9 表示 DB9。</param>
        /// <param name="startByteAddress">起始字节偏移，默认从 0 开始。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>底层库返回的读取字节数。</returns>
        public Task<int> ReadDbClassAsync(object instance, int dbNumber, int startByteAddress = 0, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            ValidateDbBlockArguments(dbNumber, startByteAddress);

            return ExecuteExclusiveAsync(
                async token =>
                {
                    EnsureConnected();
                    var result = await _plc.ReadClassAsync(instance, dbNumber, startByteAddress, token).ConfigureAwait(false);
                    return result.Item1;
                },
                cancellationToken);
        }

        /// <summary>
        /// 将一个与 PLC DB 布局一致的 C# 类整块写回 PLC。
        /// </summary>
        /// <param name="value">要写入的模型实例。</param>
        /// <param name="dbNumber">DB 块号，例如 9 表示 DB9。</param>
        /// <param name="startByteAddress">起始字节偏移，默认从 0 开始。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>表示异步操作的任务。</returns>
        public Task WriteDbClassAsync(object value, int dbNumber, int startByteAddress = 0, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ValidateDbBlockArguments(dbNumber, startByteAddress);

            return ExecuteExclusiveAsync(
                async token =>
                {
                    EnsureConnected();
                    await _plc.WriteClassAsync(value, dbNumber, startByteAddress, token).ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <summary>
        /// 释放客户端占用的资源，关闭与 S7 PLC 的连接。
        /// </summary>
        protected override void DisposeCore()
        {
            if (_plc != null)
            {
                _plc.Close();
                _plc = null;
            }
        }

        /// <summary>
        /// 确保客户端已连接到 S7 PLC。若未连接则抛出异常。
        /// </summary>
        /// <exception cref="IndustrialConnectionException">客户端未连接时引发。</exception>
        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new IndustrialConnectionException("S7 client is not connected.");
            }
        }

        private async Task<object> ReadValueAsync(S7Address address, ReadRequest request, CancellationToken cancellationToken)
        {
            switch (request.DataType)
            {
                case Abstractions.DataType.String:
                    {
                        var bytes = await _plc.ReadBytesAsync(ToPlcArea(address.Area), address.DbNumber, address.ByteOffset, request.Length, cancellationToken).ConfigureAwait(false);
                        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
                    }
                case Abstractions.DataType.ByteArray:
                    return await _plc.ReadBytesAsync(ToPlcArea(address.Area), address.DbNumber, address.ByteOffset, request.Length, cancellationToken).ConfigureAwait(false);
                case Abstractions.DataType.Char:
                    {
                        var byteValue = await ReadTypedValueAsync(address, PlcVarType.Byte, 1, cancellationToken).ConfigureAwait(false);
                        return Convert.ToChar(Convert.ToByte(byteValue));
                    }
                case Abstractions.DataType.Byte:
                    return await ReadTypedValueAsync(address, PlcVarType.Byte, request.Length, cancellationToken).ConfigureAwait(false);
                default:
                    return await ReadTypedValueAsync(address, ToVarType(request.DataType), request.Length, cancellationToken).ConfigureAwait(false);
            }
        }

        private Task<object> ReadTypedValueAsync(S7Address address, PlcVarType varType, int count, CancellationToken cancellationToken)
        {
            return _plc.ReadAsync(
                ToPlcArea(address.Area),
                address.DbNumber,
                address.ByteOffset,
                varType,
                count,
                address.BitOffset >= 0 ? (byte)address.BitOffset : (byte)0,
                cancellationToken);
        }

        private async Task WriteValueAsync(S7Address address, WriteRequest request, CancellationToken cancellationToken)
        {
            switch (request.DataType)
            {
                case Abstractions.DataType.Bool:
                    {
                        if (address.BitOffset < 0)
                        {
                            throw new IndustrialAddressParseException(
                                string.Format("S7 bool write requires a bit address such as DB1.DBX0.0 or M10.2. Actual address: {0}", address.Normalized));
                        }

                        // S7.Net 将通用 WriteAsync 的最后一个参数解释为可选 bitAdr，
                        // 其中 0 同时可能代表“第 0 位”或“未指定 bit”。
                        // 对 DBX0.0 / M0.0 这类合法位地址，走专用的 WriteBitAsync
                        // 才能避免把 bit 0 误判成无效地址。
                        await _plc.WriteBitAsync(
                            ToPlcArea(address.Area),
                            address.DbNumber,
                            address.ByteOffset,
                            address.BitOffset,
                            Convert.ToBoolean(request.Value),
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }
                case Abstractions.DataType.String:
                    {
                        var text = (request.Value ?? string.Empty).ToString();
                        var bytes = Encoding.ASCII.GetBytes(text);
                        await _plc.WriteBytesAsync(ToPlcArea(address.Area), address.DbNumber, address.ByteOffset, bytes, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                case Abstractions.DataType.ByteArray:
                    await _plc.WriteBytesAsync(
                        ToPlcArea(address.Area),
                        address.DbNumber,
                        address.ByteOffset,
                        (byte[])request.Value,
                        cancellationToken).ConfigureAwait(false);
                    return;
                case Abstractions.DataType.Char:
                    {
                        var character = request.Value is char
                            ? (char)request.Value
                            : ((request.Value ?? string.Empty).ToString().Length > 0 ? (request.Value ?? string.Empty).ToString()[0] : '\0');
                        await _plc.WriteAsync(
                            address.Normalized,
                            (byte)character,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }
                default:
                    // 对非 Bool 的标量写入，交给 S7.Net 的字符串地址重载去按 DBB/DBW/DBD 等地址类型解释。
                    // 如果这里继续走带 bitAdr 的重载，像 DB200.DBD6 这样的 DWord 地址也会因为传入 bitAdr=0
                    // 被底层误带进位地址校验，最终报 "bitwise locations 0-7" 之类的异常。
                    await _plc.WriteAsync(
                        address.Normalized,
                        request.Value,
                        cancellationToken).ConfigureAwait(false);
                    return;
            }
        }

        private static PlcArea ToPlcArea(S7Area area)
        {
            switch (area)
            {
                case S7Area.Db:
                    return PlcArea.DataBlock;
                case S7Area.Memory:
                    return PlcArea.Memory;
                case S7Area.Input:
                    return PlcArea.Input;
                case S7Area.Output:
                    return PlcArea.Output;
                default:
                    throw new IndustrialAddressParseException("Unsupported S7 area.");
            }
        }

        private static PlcVarType ToVarType(Abstractions.DataType dataType)
        {
            switch (dataType)
            {
                case Abstractions.DataType.Bool:
                    return PlcVarType.Bit;
                case Abstractions.DataType.Int16:
                    return PlcVarType.Int;
                case Abstractions.DataType.UInt16:
                    return PlcVarType.Word;
                case Abstractions.DataType.Int32:
                    return PlcVarType.DInt;
                case Abstractions.DataType.UInt32:
                    return PlcVarType.DWord;
                case Abstractions.DataType.Float:
                    return PlcVarType.Real;
                case Abstractions.DataType.Double:
                    return PlcVarType.LReal;
                case Abstractions.DataType.Byte:
                case Abstractions.DataType.Char:
                    return PlcVarType.Byte;
                default:
                    throw new InvalidOperationException(string.Format("S7 暂不支持将数据类型 {0} 映射到 VarType。", dataType));
            }
        }

        /// <summary>
        /// 校验 DB 块读写常用参数，避免把明显无效的地址传到下层库。
        /// </summary>
        private static void ValidateDbBlockArguments(int dbNumber, int startByteAddress)
        {
            if (dbNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dbNumber), "DB number must be greater than zero.");
            }

            if (startByteAddress < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startByteAddress), "Start byte address cannot be negative.");
            }
        }
    }
}
