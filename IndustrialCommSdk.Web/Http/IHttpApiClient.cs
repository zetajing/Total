using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Web.Http
{
    public interface IHttpApiClient : IDisposable
    {
        Task<HttpApiResponse> SendAsync(HttpApiRequest request, CancellationToken cancellationToken);
    }
}
