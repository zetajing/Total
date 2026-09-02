using IndustrialCommSdk.AdsRouter.Host;
using IndustrialCommSdk.Protocols.Ads.Router;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton<AdsTcpRouterHost>();
        services.AddHostedService<RouterWorker>();
    })
    .Build();

await host.RunAsync();
