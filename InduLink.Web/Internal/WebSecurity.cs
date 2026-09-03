using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace InduLink.Web.Internal
{
    internal static class WebSecurity
    {
        internal const string ApiKeyHeaderName = "X-Industrial-Api-Key";

        internal static bool FixedTimeEquals(string expected, string actual)
        {
            if (expected == null || actual == null) return false;
            var left = Encoding.UTF8.GetBytes(expected);
            var right = Encoding.UTF8.GetBytes(actual);
            var difference = left.Length ^ right.Length;
            var length = Math.Max(left.Length, right.Length);
            for (var i = 0; i < length; i++)
            {
                var a = i < left.Length ? left[i] : (byte)0;
                var b = i < right.Length ? right[i] : (byte)0;
                difference |= a ^ b;
            }
            return difference == 0;
        }

        internal static bool IsLoopbackHost(Uri uri)
        {
            if (uri == null) return false;
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            IPAddress address;
            return IPAddress.TryParse(uri.Host, out address) && IPAddress.IsLoopback(address);
        }

        internal static bool IsOriginAllowed(string origin, IReadOnlyCollection<string> allowedOrigins)
        {
            // Native/API clients normally omit Origin. Browsers send it, so only a present
            // Origin participates in the allowlist check; API-key authentication still applies.
            if (string.IsNullOrWhiteSpace(origin)) return true;
            if (allowedOrigins == null || allowedOrigins.Count == 0) return false;
            return allowedOrigins.Any(value =>
                string.Equals(value == null ? null : value.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsRequestOriginAllowed(
            string origin,
            bool requireApiKey,
            IReadOnlyCollection<string> allowedOrigins)
        {
            // A native client may omit Origin when API-key authentication is enabled. If API-key
            // authentication is disabled, however, the configured Origin allowlist is the only
            // remaining request-level protection and an absent Origin must not bypass it.
            if (string.IsNullOrWhiteSpace(origin)) return requireApiKey;
            return IsOriginAllowed(origin, allowedOrigins);
        }

        internal static void ValidateListenerSecurity(
            string listenPrefix,
            bool requireApiKey,
            string apiKey,
            IReadOnlyCollection<string> allowedOrigins,
            string optionName)
        {
            Uri prefix;
            if (string.IsNullOrWhiteSpace(listenPrefix) ||
                !Uri.TryCreate(listenPrefix, UriKind.Absolute, out prefix) ||
                (prefix.Scheme != Uri.UriSchemeHttp && prefix.Scheme != Uri.UriSchemeHttps) ||
                !listenPrefix.EndsWith("/", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(prefix.Query) || !string.IsNullOrEmpty(prefix.Fragment))
                throw new ArgumentException("ListenPrefix must be an absolute HTTP/HTTPS prefix ending with '/'.", optionName);

            if (requireApiKey && string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("An API key is required when RequireApiKey is enabled.", optionName);

            if (IsLoopbackHost(prefix))
            {
                if (!requireApiKey && (allowedOrigins == null || allowedOrigins.Count == 0))
                    throw new ArgumentException("Loopback listeners must require an API key or configure an Origin allowlist.", optionName);
            }
            else
            {
                if (prefix.Scheme != Uri.UriSchemeHttps)
                    throw new ArgumentException("Non-loopback listeners must use HTTPS/WSS.", optionName);
                if (!requireApiKey || string.IsNullOrWhiteSpace(apiKey))
                    throw new ArgumentException("Non-loopback listeners must require an API key.", optionName);
                if (allowedOrigins == null || allowedOrigins.Count == 0)
                    throw new ArgumentException("Non-loopback listeners must configure an Origin allowlist.", optionName);
            }
        }
    }
}
