using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk
{
    /// <summary>
    /// 从部署目录中的 devices.json 和 pointsFile 打开指定设备。
    /// 用于将配置加载、点位表加载和客户端创建收敛为一个入口。
    /// </summary>
    public static class IndustrialDeployment
    {
        /// <summary>打开指定设备，不建立网络连接。</summary>
        public static IndustrialConfiguredClient Open(
            string configFilePath,
            string deviceName,
            IIndustrialLogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(configFilePath)) throw new ArgumentException("Config file path cannot be null or empty.", nameof(configFilePath));

            var fullConfigPath = Path.GetFullPath(configFilePath);
            var config = IndustrialSdkConfig.Load(fullConfigPath);
            var device = config.FindDevice(deviceName);
            var tags = TagTable.Load(device.ResolvePointsFile(Path.GetDirectoryName(fullConfigPath)));
            var client = IndustrialClientFactory.FromConfig(device, logger);
            return new IndustrialConfiguredClient(device.Name, client, tags);
        }

        /// <summary>打开、连接、执行操作并自动断开和释放指定设备。</summary>
        public static async Task UseAsync(
            string configFilePath,
            string deviceName,
            Func<IndustrialConfiguredClient, Task> operation,
            IIndustrialLogger logger = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            using (var device = Open(configFilePath, deviceName, logger))
            {
                try
                {
                    await device.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    await operation(device).ConfigureAwait(false);
                }
                finally
                {
                    if (device.Client.IsConnected)
                    {
                        await device.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>打开、连接、执行带返回值的操作并自动断开和释放指定设备。</summary>
        public static async Task<TResult> UseAsync<TResult>(
            string configFilePath,
            string deviceName,
            Func<IndustrialConfiguredClient, Task<TResult>> operation,
            IIndustrialLogger logger = null,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            using (var device = Open(configFilePath, deviceName, logger))
            {
                try
                {
                    await device.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return await operation(device).ConfigureAwait(false);
                }
                finally
                {
                    if (device.Client.IsConnected)
                    {
                        await device.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 表示已按 JSON 配置打开的设备，支持按点位名称进行读取、写入和批量操作。
    /// </summary>
    public sealed class IndustrialConfiguredClient : IDisposable
    {
        /// <summary>使用指定客户端和点位表创建配置化设备实例，适用于自定义集成或测试。</summary>
        public IndustrialConfiguredClient(string deviceName, IIndustrialClient client, TagTable tags)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("Device name cannot be null or empty.", nameof(deviceName));

            DeviceName = deviceName;
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        }

        /// <summary>获取 devices.json 中的设备名称。</summary>
        public string DeviceName { get; private set; }

        /// <summary>获取协议客户端；高级场景可直接使用该对象。</summary>
        public IIndustrialClient Client { get; private set; }

        /// <summary>获取该设备关联的点位表。</summary>
        public TagTable Tags { get; private set; }

        /// <summary>连接设备。</summary>
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            return Client.ConnectAsync(cancellationToken);
        }

        /// <summary>断开设备连接。</summary>
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return Client.DisconnectAsync(cancellationToken);
        }

        /// <summary>按点位名称读取原始数据值，数据类型和长度来自点位表。</summary>
        public Task<DataValue> ReadAsync(string tagName, CancellationToken cancellationToken = default)
        {
            var tag = Tags.Get(tagName);
            return Client.ReadAsync(new ReadRequest(Client.DeviceId, tag.Address, tag.DataType, tag.Length), cancellationToken);
        }

        /// <summary>按点位名称读取并转换为目标 CLR 类型。</summary>
        public Task<T> ReadAsync<T>(string tagName, CancellationToken cancellationToken = default)
        {
            var tag = Tags.Get(tagName);
            return Client.ReadValueAsync<T>(tag.Address, tag.DataType, tag.Length, cancellationToken);
        }

        /// <summary>读取当前设备点位表中的全部点位。</summary>
        public Task<IndustrialTagReadResult> ReadManyAsync(CancellationToken cancellationToken = default)
        {
            return Client.ReadManyAsync(Tags.Tags, cancellationToken);
        }

        /// <summary>按点位名称写入一个值，数据类型和长度来自点位表。</summary>
        public Task WriteAsync(string tagName, object value, CancellationToken cancellationToken = default)
        {
            var tag = Tags.Get(tagName);
            return Client.WriteValueAsync(tag.Address, tag.DataType, value, tag.Length, cancellationToken);
        }

        /// <summary>按点位名称批量写入值，字典键必须是点位表中的名称。</summary>
        public Task WriteManyAsync(IReadOnlyDictionary<string, object> values, CancellationToken cancellationToken = default)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));

            var writes = new List<IndustrialWrite>();
            foreach (var value in values)
            {
                writes.Add(new IndustrialWrite(Tags.Get(value.Key), value.Value));
            }

            return Client.WriteManyAsync(writes, cancellationToken);
        }

        /// <summary>释放底层协议客户端。</summary>
        public void Dispose()
        {
            Client.Dispose();
        }
    }
}
