using System;
using System.Collections.Generic;
using System.Linq;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Runtime.Configuration
{
    public interface IProtocolSettings
    {
    }

    public interface IIndustrialProtocolProvider
    {
        string Protocol { get; }
        Type SettingsType { get; }
        IProtocolSettings CreateDefaultSettings();
        IReadOnlyList<string> Validate(IProtocolSettings settings);
        IIndustrialClient CreateClient(IndustrialDeviceConfig device, IIndustrialLogger logger);
    }

    public abstract class IndustrialProtocolProvider<TSettings> : IIndustrialProtocolProvider
        where TSettings : class, IProtocolSettings, new()
    {
        public abstract string Protocol { get; }
        public Type SettingsType { get { return typeof(TSettings); } }
        public virtual IProtocolSettings CreateDefaultSettings() { return new TSettings(); }

        public IReadOnlyList<string> Validate(IProtocolSettings settings)
        {
            var typed = settings as TSettings;
            if (typed == null) return new[] { "Settings type must be " + typeof(TSettings).Name + "." };
            return Validate(typed);
        }

        public IIndustrialClient CreateClient(IndustrialDeviceConfig device, IIndustrialLogger logger)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (device.Runtime == null || device.Runtime.OperationTimeoutMilliseconds <= 0)
                throw new ArgumentException("Device runtime and a positive operation timeout are required.", nameof(device));
            var typed = device.Settings as TSettings;
            if (typed == null) throw new ArgumentException("Settings type does not match protocol '" + Protocol + "'.", nameof(device));
            var errors = Validate(typed);
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(device));
            return CreateClient(device, typed, logger ?? NullIndustrialLogger.Instance);
        }

        protected abstract IReadOnlyList<string> Validate(TSettings settings);
        protected abstract IIndustrialClient CreateClient(IndustrialDeviceConfig device, TSettings settings, IIndustrialLogger logger);

        protected static IReadOnlyList<string> Errors(params string[] errors)
        {
            return errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToList().AsReadOnly();
        }
    }

    public sealed class IndustrialProtocolRegistry
    {
        private readonly Dictionary<string, IIndustrialProtocolProvider> _providers =
            new Dictionary<string, IIndustrialProtocolProvider>(StringComparer.Ordinal);

        public IReadOnlyList<IIndustrialProtocolProvider> Providers
        {
            get { return _providers.Values.OrderBy(provider => provider.Protocol, StringComparer.Ordinal).ToList().AsReadOnly(); }
        }

        public IndustrialProtocolRegistry Register(IIndustrialProtocolProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            ValidateProtocolKey(provider.Protocol);
            if (provider.SettingsType == null || !typeof(IProtocolSettings).IsAssignableFrom(provider.SettingsType))
                throw new ArgumentException("Provider SettingsType must implement IProtocolSettings.", nameof(provider));
            if (_providers.ContainsKey(provider.Protocol))
                throw new InvalidOperationException("Protocol is already registered: " + provider.Protocol);
            _providers.Add(provider.Protocol, provider);
            return this;
        }

        public bool TryGet(string protocol, out IIndustrialProtocolProvider provider)
        {
            provider = null;
            return !string.IsNullOrWhiteSpace(protocol) && _providers.TryGetValue(protocol, out provider);
        }

        public IIndustrialProtocolProvider Get(string protocol)
        {
            IIndustrialProtocolProvider provider;
            if (!TryGet(protocol, out provider))
                throw new KeyNotFoundException("Unsupported protocol: " + protocol);
            return provider;
        }

        private static void ValidateProtocolKey(string protocol)
        {
            if (string.IsNullOrWhiteSpace(protocol)) throw new ArgumentException("Protocol key cannot be empty.");
            if (!string.Equals(protocol, protocol.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                throw new ArgumentException("Protocol key must be canonical lowercase text: " + protocol);
            foreach (var value in protocol)
                if (!(value >= 'a' && value <= 'z') && !(value >= '0' && value <= '9') && value != '-')
                    throw new ArgumentException("Protocol key contains an unsupported character: " + protocol);
        }
    }
}
