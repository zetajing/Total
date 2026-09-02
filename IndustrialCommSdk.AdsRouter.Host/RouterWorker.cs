using IndustrialCommSdk.Protocols.Ads.Router;
using Microsoft.Extensions.Hosting;

namespace IndustrialCommSdk.AdsRouter.Host;

public sealed class RouterWorker : BackgroundService
{
    private readonly AdsTcpRouterHost _router;

    public RouterWorker(AdsTcpRouterHost router)
    {
        _router = router;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _router.StartAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _router.StopAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
