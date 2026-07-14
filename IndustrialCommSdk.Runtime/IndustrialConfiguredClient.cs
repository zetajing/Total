using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Runtime
{
    public sealed class IndustrialConfiguredClient : IDisposable
    {
        public IndustrialConfiguredClient(string deviceName, IIndustrialClient client, TagTable tags)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("Device name cannot be empty.", nameof(deviceName));
            DeviceName = deviceName;
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        }

        public string DeviceName { get; private set; }
        public IIndustrialClient Client { get; private set; }
        public TagTable Tags { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default) { return Client.ConnectAsync(cancellationToken); }
        public Task DisconnectAsync(CancellationToken cancellationToken = default) { return Client.DisconnectAsync(cancellationToken); }

        public Task<DataValue> ReadAsync(string tagName, CancellationToken cancellationToken = default)
        {
            var tag = Tags.Get(tagName);
            return Client.ReadAsync(new ReadRequest(Client.DeviceId, tag.Address, tag.DataType, tag.Length), cancellationToken);
        }

        public Task<T> ReadAsync<T>(string tagName, CancellationToken cancellationToken = default)
        {
            var tag = Tags.Get(tagName);
            return Client.ReadValueAsync<T>(tag.Address, tag.DataType, tag.Length, cancellationToken);
        }

        public Task<IndustrialTagReadResult> ReadManyAsync(CancellationToken cancellationToken = default)
        {
            return Client.ReadManyAsync(Tags.Tags, cancellationToken);
        }

        public Task WriteAsync(string tagName, object value, CancellationToken cancellationToken = default)
        {
            var tag = Tags.Get(tagName);
            return Client.WriteValueAsync(tag.Address, tag.DataType, value, tag.Length, cancellationToken);
        }

        public Task WriteManyAsync(IReadOnlyDictionary<string, object> values, CancellationToken cancellationToken = default)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var writes = new List<IndustrialWrite>();
            foreach (var value in values) writes.Add(new IndustrialWrite(Tags.Get(value.Key), value.Value));
            return Client.WriteManyAsync(writes, cancellationToken);
        }

        public void Dispose() { Client.Dispose(); }
    }
}
