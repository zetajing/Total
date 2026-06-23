using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Transport
{
    public sealed class TcpTransportClient : ITransportClient
    {
        private readonly TcpTransportOptions _options;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private TcpClient _client;
        private NetworkStream _stream;

        public TcpTransportClient(TcpTransportOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool IsConnected
        {
            get
            {
                var client = _client;
                if (client == null || !client.Connected)
                {
                    return false;
                }

                try
                {
                    return !(client.Client.Poll(1, SelectMode.SelectRead) && client.Client.Available == 0);
                }
                catch
                {
                    return false;
                }
            }
        }

        public EndPoint RemoteEndPoint
        {
            get { return _client == null ? null : _client.Client.RemoteEndPoint; }
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    return;
                }

                DisposeClient();
                _client = new TcpClient();
                _client.SendTimeout = _options.SendTimeoutMilliseconds;
                _client.ReceiveTimeout = _options.ReceiveTimeoutMilliseconds;

                var connectTask = _client.ConnectAsync(_options.Host, _options.Port);
                var timeoutTask = Task.Delay(_options.ConnectTimeoutMilliseconds, cancellationToken);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completedTask != connectTask)
                {
                    DisposeClient();
                    throw new IndustrialTimeoutException("TCP connect timeout.");
                }

                await connectTask.ConfigureAwait(false);
                _stream = _client.GetStream();
            }
            catch (SocketException ex)
            {
                throw new IndustrialConnectionException("TCP connect failed.", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DisposeClient();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]> ReceiveExactAsync(int length, CancellationToken cancellationToken)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await _stream.ReadAsync(buffer, offset, length - offset, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    DisposeClient();
                    throw new IndustrialConnectionException("Remote peer closed the connection.");
                }
                offset += read;
            }
            return buffer;
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (IsConnected)
            {
                return;
            }

            if (!_options.AutoReconnect)
            {
                throw new IndustrialConnectionException("TCP client is not connected.");
            }

            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        private void DisposeClient()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_client != null)
            {
                _client.Close();
                _client = null;
            }
        }

        public void Dispose()
        {
            DisposeClient();
            _gate.Dispose();
        }
    }
}
