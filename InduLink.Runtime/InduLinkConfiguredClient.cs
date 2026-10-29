using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;

namespace InduLink.Runtime
{
    public sealed class InduLinkConfiguredClient : IDisposable, IAsyncDisposable
    {
        public InduLinkConfiguredClient(string deviceName, IInduLinkClient client, TagTable tags)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("Device name cannot be empty.", nameof(deviceName));
            DeviceName = deviceName;
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        }

        public string DeviceName { get; }
        public IInduLinkClient Client { get; }
        public TagTable Tags { get; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            return Client.ConnectAsync(cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return Client.DisconnectAsync(cancellationToken);
        }

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

        public Task<InduLinkTagReadResult> ReadManyAsync(CancellationToken cancellationToken = default)
        {
            return Client.ReadManyAsync(Tags.Tags, cancellationToken);
        }

        /// <summary>按业务名称读取至少两个点位；名称不区分大小写，重复名称只读取一次。单个名称可用集合重载。</summary>
        public Task<InduLinkTagReadResult> ReadManyAsync(string firstTagName, string secondTagName, params string[] additionalTagNames)
        {
            if (additionalTagNames == null) throw new ArgumentNullException(nameof(additionalTagNames));
            var names = new List<string>(additionalTagNames.Length + 2) { firstTagName, secondTagName };
            names.AddRange(additionalTagNames);
            return ReadManyAsync(names, CancellationToken.None);
        }

        /// <summary>按业务名称读取指定点位，并允许取消本次读取；保留名称首次出现的顺序。</summary>
        public Task<InduLinkTagReadResult> ReadManyAsync(
            IReadOnlyCollection<string> tagNames,
            CancellationToken cancellationToken)
        {
            if (tagNames == null) throw new ArgumentNullException(nameof(tagNames));
            if (tagNames.Count == 0) throw new ArgumentException("At least one tag name is required.", nameof(tagNames));
            cancellationToken.ThrowIfCancellationRequested();

            // 发出请求前解析全部名称，避免名称拼错时已经读取了部分点位。
            var selected = new List<InduLinkTag>(tagNames.Count);
            var seen = new HashSet<InduLinkTag>();
            foreach (var name in tagNames)
            {
                var tag = Tags.Get(name);
                if (seen.Add(tag)) selected.Add(tag);
            }
            return Client.ReadManyAsync(selected, cancellationToken);
        }

        public Task WriteAsync(string tagName, object value, CancellationToken cancellationToken = default)
        {
            var tag = Tags.Get(tagName);
            return Client.WriteValueAsync(tag.Address, tag.DataType, value, tag.Length, cancellationToken);
        }

        public Task WriteManyAsync(IReadOnlyDictionary<string, object> values, CancellationToken cancellationToken = default)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var writes = new List<InduLinkWrite>(values.Count);
            foreach (var value in values)
            {
                writes.Add(new InduLinkWrite(Tags.Get(value.Key), value.Value));
            }

            return Client.WriteManyAsync(writes, cancellationToken);
        }

        public void Dispose()
        {
            Client.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            if (Client is IAsyncDisposable asyncClient)
            {
                return asyncClient.DisposeAsync();
            }

            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
