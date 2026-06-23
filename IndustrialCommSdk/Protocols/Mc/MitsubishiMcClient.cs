using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using IndustrialCommSdk.Protocols.Common;
using IndustrialCommSdk.Transport;

namespace IndustrialCommSdk.Protocols.Mc
{
    /// <summary>
    /// 三菱 PLC MC 协议支持的设备类型。
    /// </summary>
    public enum McDeviceType
    {
        /// <summary>数据寄存器（Data Register）。</summary>
        D,
        /// <summary>字链接寄存器（Word Link Register）。</summary>
        W,
        /// <summary>文件寄存器（File Register）。</summary>
        R,
        /// <summary>特殊数据寄存器（Special Data Register）。</summary>
        SD,
        /// <summary>索引寄存器（Index Register）。</summary>
        Z,
        /// <summary>扩展文件寄存器（Extended File Register）。</summary>
        ZR,
        /// <summary>辅助继电器（Auxiliary Relay / Internal Relay）。</summary>
        M,
        /// <summary>输入继电器（Input Relay）。</summary>
        X,
        /// <summary>输出继电器（Output Relay）。</summary>
        Y,
        /// <summary>锁存继电器（Latch Relay）。</summary>
        L,
        /// <summary>定时器当前值（Timer Current Value）。</summary>
        TN,
        /// <summary>累计定时器（Summation / Retentive Timer）。</summary>
        SS,
        /// <summary>计数器当前值（Counter Current Value）。</summary>
        CN,
    }

    /// <summary>
    /// 表示一个解析后的三菱 MC 协议地址，包含设备类型、索引编号及是否为位设备。
    /// </summary>
    public sealed class McAddress
    {
        /// <summary>
        /// 初始化 <see cref="McAddress"/> 的新实例。
        /// </summary>
        /// <param name="deviceType">MC 设备类型。</param>
        /// <param name="index">设备地址索引（可能为十进制或十六进制）。</param>
        /// <param name="isBitDevice">指示该地址是否为位设备。</param>
        public McAddress(McDeviceType deviceType, int index, bool isBitDevice)
        {
            DeviceType = deviceType;
            Index = index;
            IsBitDevice = isBitDevice;
        }

        /// <summary>获取设备类型。</summary>
        public McDeviceType DeviceType { get; private set; }
        /// <summary>获取设备地址索引。</summary>
        public int Index { get; private set; }
        /// <summary>获取一个值，指示该设备是否为位设备（如 M、X、Y、L）。</summary>
        public bool IsBitDevice { get; private set; }
    }

    /// <summary>
    /// 三菱 MC 协议地址解析器。支持常见的设备类型前缀和十六进制/十进制地址格式。
    /// </summary>
    public sealed class McAddressParser : IAddressParser
    {
        /// <summary>
        /// 匹配 MC 地址的正则表达式，例如 "D100"、"X3F"、"M0"、"ZR1000"。
        /// 分组 1：设备类型前缀（如 D、M、X、Y、ZR、SD 等）；分组 2：地址数字部分（十六进制或十进制）。
        /// </summary>
        private static readonly Regex Pattern = new Regex(@"^(ZR|SD|TN|SS|CN|[A-Z])([0-9A-F]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 将 MC 地址字符串解析为 <see cref="McAddress"/> 对象。
        /// </summary>
        /// <param name="address">MC 地址字符串，例如 "D100"、"X3F"、"M0"、"ZR100"。</param>
        /// <returns>解析后的 <see cref="McAddress"/> 对象。</returns>
        /// <exception cref="IndustrialAddressParseException">地址格式无效或设备类型不受支持时引发。</exception>
        public object Parse(string address)
        {
            var match = Pattern.Match((address ?? string.Empty).Trim().ToUpperInvariant());
            if (!match.Success)
            {
                throw new Exceptions.IndustrialAddressParseException(string.Format("Unsupported MC address: {0}", address));
            }

            McDeviceType type;
            if (!Enum.TryParse(match.Groups[1].Value, true, out type))
            {
                throw new Exceptions.IndustrialAddressParseException("Unsupported MC device type.");
            }

            var isHex = type == McDeviceType.X || type == McDeviceType.Y || type == McDeviceType.W;
            var index = Convert.ToInt32(match.Groups[2].Value, isHex ? 16 : 10);
            var isBit = type == McDeviceType.M || type == McDeviceType.X || type == McDeviceType.Y || type == McDeviceType.L;
            return new McAddress(type, index, isBit);
        }
    }

    /// <summary>
    /// 三菱 MC 协议客户端的配置选项。
    /// </summary>
    public sealed class MitsubishiMcClientOptions
    {
        /// <summary>获取或设置设备标识符。</summary>
        public string DeviceId { get; set; }
        /// <summary>获取或设置三菱 PLC 的主机地址（IP 或主机名）。</summary>
        public string Host { get; set; }
        /// <summary>获取或设置 TCP 端口号。默认值为 5000。</summary>
        public int Port { get; set; } = 5000;
        /// <summary>获取或设置发送超时时间（毫秒）。默认值为 3000。</summary>
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        /// <summary>获取或设置接收超时时间（毫秒）。默认值为 5000。</summary>
        public int ReceiveTimeoutMilliseconds { get; set; } = 5000;
    }

    /// <summary>
    /// 三菱 MC 协议客户端。通过 TCP 与三菱 PLC 通信，支持读写字设备和位设备。
    /// 内部使用 <see cref="McFrame3E"/> 构建和解析 3E 帧协议。
    /// </summary>
    public sealed class MitsubishiMcClient : IndustrialClientBase
    {
        private readonly TcpTransportClient _transport;
        private readonly McAddressParser _parser;

        /// <summary>
        /// 初始化 <see cref="MitsubishiMcClient"/> 的新实例。
        /// </summary>
        /// <param name="options">客户端配置选项。</param>
        /// <param name="logger">可选的日志记录器实例。</param>
        /// <param name="pollingScheduler">可选的轮询调度器实例。</param>
        /// <param name="parser">可选的 MC 地址解析器实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 null 时引发。</exception>
        public MitsubishiMcClient(MitsubishiMcClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null, McAddressParser parser = null)
            : base(options.DeviceId, ProtocolKind.MitsubishiMc, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _transport = new TcpTransportClient(new TcpTransportOptions
            {
                Host = options.Host,
                Port = options.Port,
                SendTimeoutMilliseconds = options.SendTimeoutMilliseconds,
                ReceiveTimeoutMilliseconds = options.ReceiveTimeoutMilliseconds,
            });
            _parser = parser ?? new McAddressParser();
        }

        /// <summary>
        /// 获取一个值，指示客户端是否已成功连接到三菱 PLC。
        /// </summary>
        public override bool IsConnected
        {
            get { return _transport.IsConnected; }
        }

        /// <summary>
        /// 建立与三菱 PLC 的 TCP 连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.ConnectAsync(cancellationToken);
        }

        /// <summary>
        /// 断开与三菱 PLC 的 TCP 连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.DisconnectAsync(cancellationToken);
        }

        /// <summary>
        /// 从三菱 PLC 读取数据。根据地址类型（位设备或字设备）自动选择读取方式。
        /// </summary>
        /// <param name="request">读取请求，包含地址和长度信息。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>包含读取结果的数据值。</returns>
        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            var address = (McAddress)_parser.Parse(request.Address);
            var frame = address.IsBitDevice
                ? McFrame3E.BuildReadBitsRequest(address, request.Length)
                : McFrame3E.BuildReadWordsRequest(address, request.Length);

            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            var header = await _transport.ReceiveExactAsync(11, cancellationToken).ConfigureAwait(false);
            var responseLength = McFrame3E.ReadU16LE(header, 7);
            var body = await _transport.ReceiveExactAsync(responseLength, cancellationToken).ConfigureAwait(false);
            var response = McFrame3E.Combine(header, body);
            var payload = McFrame3E.ParseResponse(response);

            if (address.IsBitDevice)
            {
                return RegisterValueCodec.ToDataValue(request, McFrame3E.UnpackBits(payload, request.Length));
            }

            return RegisterValueCodec.ToDataValue(request, McFrame3E.ToRegisters(payload));
        }

        /// <summary>
        /// 向三菱 PLC 写入数据。根据地址类型（位设备或字设备）自动选择写入方式。
        /// </summary>
        /// <param name="request">写入请求，包含地址和待写入的值。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            var address = (McAddress)_parser.Parse(request.Address);
            var frame = address.IsBitDevice
                ? McFrame3E.BuildWriteBitsRequest(address, RegisterValueCodec.EncodeBits(request))
                : McFrame3E.BuildWriteWordsRequest(address, RegisterValueCodec.EncodeRegisters(request));

            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            var header = await _transport.ReceiveExactAsync(11, cancellationToken).ConfigureAwait(false);
            var responseLength = McFrame3E.ReadU16LE(header, 7);
            var body = await _transport.ReceiveExactAsync(responseLength, cancellationToken).ConfigureAwait(false);
            McFrame3E.ParseResponse(McFrame3E.Combine(header, body));
        }

        /// <summary>
        /// 释放客户端占用的资源（关闭 TCP 传输层连接）。
        /// </summary>
        protected override void DisposeCore()
        {
            _transport.Dispose();
        }
    }
}
