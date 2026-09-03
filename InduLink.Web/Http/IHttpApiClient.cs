using System;
using System.Threading;
using System.Threading.Tasks;

namespace InduLink.Web.Http
{
    public interface IHttpApiClient : IDisposable
    {
        Task<HttpApiResponse> SendAsync(HttpApiRequest request, CancellationToken cancellationToken);
    }
}
