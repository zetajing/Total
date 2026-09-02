using Microsoft.Extensions.Configuration;
using System.Net;

namespace IndustrialCommSdk.Protocols.Ads.Router;

public interface IAdsRouterHost : IDisposable
{
    bool IsRunning { get; }

    bool IsActive { get; }

    string Status { get; }

    event EventHandler? StatusChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class AdsRemoteRouteOptions
{
    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string NetId { get; set; } = string.Empty;

    public string Type { get; set; } = "TCP_IP";
}

public sealed class AdsRouterOptions
{
    public string Name { get; set; } = string.Empty;

    public string NetId { get; set; } = string.Empty;

    public int TcpPort { get; set; } = 48898;

    public string? ChannelPortType { get; set; }

    public string? LoopbackIP { get; set; }

    public int? LoopbackPort { get; set; }

    public List<AdsRemoteRouteOptions> RemoteConnections { get; } = [];

    public static AdsRouterOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("AmsRouter");
        var options = new AdsRouterOptions
        {
            Name = section["Name"] ?? string.Empty,
            NetId = section["NetId"] ?? string.Empty,
            ChannelPortType = section["ChannelPortType"],
            LoopbackIP = section["LoopbackIP"],
            LoopbackPort = ParseNullableInt(section["LoopbackPort"]),
            TcpPort = ParseInt(section["TcpPort"], 48898)
        };

        foreach (var routeSection in section.GetSection("RemoteConnections").GetChildren())
        {
            options.RemoteConnections.Add(new AdsRemoteRouteOptions
            {
                Name = routeSection["Name"] ?? string.Empty,
                Address = routeSection["Address"] ?? string.Empty,
                NetId = routeSection["NetId"] ?? string.Empty,
                Type = routeSection["Type"] ?? "TCP_IP"
            });
        }

        return options;
    }

    private static int ParseInt(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"AmsRouter numeric value '{value}' is invalid.");
    }

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"AmsRouter numeric value '{value}' is invalid.");
    }
}

public static class AdsRouterConfigurationValidator
{
    public static AdsRouterOptions Validate(IConfiguration configuration)
    {
        var options = AdsRouterOptions.FromConfiguration(configuration);
        Validate(options);
        return options;
    }

    public static void Validate(AdsRouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new InvalidOperationException("AmsRouter:Name is required.");
        }

        if (!TryParseAmsNetId(options.NetId))
        {
            throw new InvalidOperationException(
                $"AmsRouter:NetId '{options.NetId}' is not a valid AMS Net ID.");
        }

        if (options.TcpPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("AmsRouter:TcpPort must be between 1 and 65535.");
        }

        if (options.ChannelPortType is not null && !IsSupportedChannelPortType(options.ChannelPortType))
        {
            throw new InvalidOperationException(
                "AmsRouter:ChannelPortType must be All, Loopback, UnixSocket, PInvoke, or None.");
        }

        if (options.LoopbackIP is not null &&
            !IPAddress.TryParse(options.LoopbackIP, out _))
        {
            throw new InvalidOperationException(
                $"AmsRouter:LoopbackIP '{options.LoopbackIP}' is not a valid IP address.");
        }

        if (options.LoopbackPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("AmsRouter:LoopbackPort must be between 1 and 65535 when specified.");
        }

        for (var index = 0; index < options.RemoteConnections.Count; index++)
        {
            var route = options.RemoteConnections[index];
            if (string.IsNullOrWhiteSpace(route.Name))
            {
                throw new InvalidOperationException($"AmsRouter:RemoteConnections:{index}:Name is required.");
            }

            if (string.IsNullOrWhiteSpace(route.Address))
            {
                throw new InvalidOperationException($"AmsRouter:RemoteConnections:{index}:Address is required.");
            }

            if (!TryParseAmsNetId(route.NetId))
            {
                throw new InvalidOperationException(
                    $"AmsRouter:RemoteConnections:{index}:NetId '{route.NetId}' is not a valid AMS Net ID.");
            }

            if (!string.Equals(route.Type, "TCP_IP", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"AmsRouter:RemoteConnections:{index}:Type must be TCP_IP for this host.");
            }
        }
    }

    private static bool TryParseAmsNetId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = new TwinCAT.Ads.AmsNetId(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSupportedChannelPortType(string value)
    {
        return string.Equals(value, "All", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Loopback", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "UnixSocket", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "PInvoke", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase);
    }
}
