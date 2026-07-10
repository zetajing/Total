using System;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>
    /// Default immutable implementation of <see cref="IIndustrialAddress" />.
    /// Protocols may use their own richer address objects, but this type is useful for tests, tooling,
    /// configuration validation, and capability documentation.
    /// </summary>
    public sealed class IndustrialAddress : IIndustrialAddress
    {
        public IndustrialAddress(string original, string normalized, string area, int offset, int? bit = null)
        {
            if (string.IsNullOrWhiteSpace(original)) throw new ArgumentException("Original address cannot be null or empty.", nameof(original));
            if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("Normalized address cannot be null or empty.", nameof(normalized));
            if (string.IsNullOrWhiteSpace(area)) throw new ArgumentException("Area cannot be null or empty.", nameof(area));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (bit.HasValue && bit.Value < 0) throw new ArgumentOutOfRangeException(nameof(bit));

            Original = original;
            Normalized = normalized;
            Area = area;
            Offset = offset;
            Bit = bit;
        }

        public string Original { get; private set; }
        public string Normalized { get; private set; }
        public string Area { get; private set; }
        public int Offset { get; private set; }
        public int? Bit { get; private set; }
        public bool IsBitAddress { get { return Bit.HasValue; } }

        public override string ToString()
        {
            return Normalized;
        }
    }
}
