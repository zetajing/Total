using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using TwinCAT.Ads.TcpRouter;

namespace IndustrialCommSdk.Protocols.Ads.Router;

public sealed class AdsTcpRouterHost : IAdsRouterHost
{
    private readonly ILogger<AdsTcpRouterHost> _logger;
    private readonly AmsTcpIpRouter _router;
    private readonly AdsRouterOptions _options;
    private readonly object _syncRoot = new();
    private bool _disposed;

    public AdsTcpRouterHost(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _options = AdsRouterConfigurationValidator.Validate(configuration);
        _logger = loggerFactory.CreateLogger<AdsTcpRouterHost>();
        _router = new AmsTcpIpRouter(configuration, loggerFactory);
        _router.RouterStatusChanged += OnRouterStatusChanged;
    }

    public bool IsRunning => _router.IsRunning;

    public bool IsActive => _router.IsActive;

    public string Status => _router.RouterStatus.ToString();

    public event EventHandler? StatusChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_router.IsActive)
            {
                return;
            }
        }

        _logger.LogInformation(
            "Starting standalone ADS TCP Router. LocalNetId={LocalNetId}, TcpPort={TcpPort}",
            _options.NetId,
            _options.TcpPort);

        try
        {
            await _router.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsPortConflict(ex))
        {
            throw new InvalidOperationException(
                $"Cannot start the standalone ADS Router because TCP port {_options.TcpPort} or loopback port {_options.LoopbackPort ?? _options.TcpPort} is already in use. Stop the system TwinCAT Router or choose another port.",
                ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (_router.IsActive)
        {
            _logger.LogInformation("Stopping standalone ADS TCP Router.");
            _router.Stop();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _router.RouterStatusChanged -= OnRouterStatusChanged;
        if (_router.IsActive)
        {
            _router.Stop();
        }
    }

    private void OnRouterStatusChanged(object? sender, RouterStatusChangedEventArgs e)
    {
        _logger.LogDebug("ADS TCP Router status changed to {RouterStatus}.", _router.RouterStatus);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool IsPortConflict(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SocketException socketException &&
                (socketException.SocketErrorCode == SocketError.AddressAlreadyInUse ||
                 socketException.SocketErrorCode == SocketError.AccessDenied))
            {
                return true;
            }

            var message = current.Message;
            if (message.IndexOf("address already in use", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("only one usage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("通常每个套接字地址", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
