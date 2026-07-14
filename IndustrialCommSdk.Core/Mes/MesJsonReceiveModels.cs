using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Mes
{
    /// <summary>MES 主动推送的原始 HTTP JSON 请求。</summary>
    public sealed class MesJsonReceiveRequest
    {
        public string Endpoint { get; internal set; }
        public string Body { get; internal set; }
        public string ContentType { get; internal set; }
        public string RemoteEndPoint { get; internal set; }
        public IReadOnlyDictionary<string, string> Headers { get; internal set; }
    }

    /// <summary>返回给 MES 的 HTTP JSON 响应。</summary>
    public sealed class MesJsonReceiveResponse
    {
        public int StatusCode { get; set; } = 200;
        public string Json { get; set; } = "{\"success\":true}";
    }

    /// <summary>处理 MES 主动推送并异步生成 JSON 响应。</summary>
    public delegate Task<MesJsonReceiveResponse> MesJsonReceiveHandler(
        MesJsonReceiveRequest request,
        CancellationToken cancellationToken);
}
