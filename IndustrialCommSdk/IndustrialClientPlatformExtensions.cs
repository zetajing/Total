using System;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk
{
    /// <summary>
    /// Platform-level helpers for clients. These methods keep IIndustrialClient stable while allowing
    /// new SDK infrastructure to discover capabilities and make protocol-neutral decisions.
    /// </summary>
    public static class IndustrialClientPlatformExtensions
    {
        /// <summary>
        /// Gets protocol capabilities for any industrial client. Custom clients may implement
        /// IProtocolCapabilityProvider to override the built-in defaults.
        /// </summary>
        public static ProtocolCapabilities GetCapabilities(this IIndustrialClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            var provider = client as IProtocolCapabilityProvider;
            if (provider != null && provider.Capabilities != null) return provider.Capabilities;
            return ProtocolCapabilities.ForProtocol(client.Kind);
        }

        /// <summary>Returns whether the client has protocol-level batch read optimization beyond sequential fallback.</summary>
        public static bool HasOptimizedBatchRead(this IIndustrialClient client)
        {
            return client.GetCapabilities().SupportsOptimizedBatchRead;
        }

        /// <summary>Returns whether the client has protocol-level batch write optimization beyond sequential fallback.</summary>
        public static bool HasOptimizedBatchWrite(this IIndustrialClient client)
        {
            return client.GetCapabilities().SupportsOptimizedBatchWrite;
        }

        /// <summary>Returns whether the requested polling interval is at or above the protocol recommendation.</summary>
        public static bool IsRecommendedPollingInterval(this IIndustrialClient client, TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            return interval >= client.GetCapabilities().RecommendedMinPollingInterval;
        }
    }
}
