using System;
using System.Collections.Generic;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>A byte-oriented key/value write entry.</summary>
    public sealed class KeyValueWrite
    {
        public KeyValueWrite(string key, byte[] value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be null or empty.", nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));
            Key = key;
            Value = (byte[])value.Clone();
        }

        public string Key { get; private set; }
        public byte[] Value { get; private set; }
    }

    /// <summary>A byte-oriented key/value read result.</summary>
    public sealed class KeyValueValue
    {
        public KeyValueValue(string key, byte[] value, bool exists, DateTimeOffset timestamp)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be null or empty.", nameof(key));
            Key = key;
            Value = value == null ? null : (byte[])value.Clone();
            Exists = exists;
            Timestamp = timestamp;
        }

        public string Key { get; private set; }
        public byte[] Value { get; private set; }
        public bool Exists { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
    }
}
