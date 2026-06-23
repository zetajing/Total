using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using S7.Net;

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

            var input = address.Trim().ToUpperInvariant();
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
            return int.Parse(token);
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
            var value = await _plc.ReadAsync(parsed.Normalized, cancellationToken).ConfigureAwait(false);
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
            _parser.Parse(request.Address);
            await _plc.WriteAsync(request.Address, request.Value, cancellationToken).ConfigureAwait(false);
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
    }
}
