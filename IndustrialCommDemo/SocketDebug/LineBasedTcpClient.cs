using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommDemo.SocketDebug
{
    internal sealed class LineBasedTcpClient : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly List<byte> _pendingBytes = new List<byte>();
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private Task _receiveLoopTask;

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

        public string RemoteEndPoint
        {
            get { return _client?.Client?.RemoteEndPoint == null ? string.Empty : _client.Client.RemoteEndPoint.ToString(); }
        }

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<SocketTextMessageEventArgs> MessageReceived;

        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);

            var client = new TcpClient();
            client.NoDelay = true;

            try
            {
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(3000, cancellationToken);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completedTask != connectTask)
                {
                    client.Close();
                    throw new TimeoutException("TCP client connect timeout.");
                }

                await connectTask.ConfigureAwait(false);
                _client = client;
                _stream = client.GetStream();
                _cts = new CancellationTokenSource();
                _pendingBytes.Clear();
                _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                client.Close();
                throw;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            var source = _cts;
            _cts = null;

            if (source != null)
            {
                source.Cancel();
            }

            CloseClient();

            if (_receiveLoopTask != null)
            {
                try
                {
                    await _receiveLoopTask.ConfigureAwait(false);
                }
                catch
                {
                }
                finally
                {
                    _receiveLoopTask = null;
                }
            }

            if (source != null)
            {
                source.Dispose();
            }
        }

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("TCP client is not connected.");
            }

            var payload = Encoding.UTF8.GetBytes((message ?? string.Empty) + "\r\n");

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
            _sendLock.Dispose();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }

                    if (read <= 0)
                    {
                        break;
                    }

                    AppendAndDispatch(buffer, read);
                }
            }
            finally
            {
                CloseClient();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private void AppendAndDispatch(byte[] buffer, int count)
        {
            for (var index = 0; index < count; index++)
            {
                _pendingBytes.Add(buffer[index]);
            }

            while (true)
            {
                var lineEndIndex = FindLineEnding(_pendingBytes);
                if (lineEndIndex < 0)
                {
                    return;
                }

                var lineBytes = _pendingBytes.GetRange(0, lineEndIndex).ToArray();
                _pendingBytes.RemoveRange(0, lineEndIndex + 2);
                MessageReceived?.Invoke(this, new SocketTextMessageEventArgs(Guid.Empty, RemoteEndPoint, Encoding.UTF8.GetString(lineBytes)));
            }
        }

        private void CloseClient()
        {
            if (_stream != null)
            {
                try
                {
                    _stream.Dispose();
                }
                catch
                {
                }

                _stream = null;
            }

            if (_client != null)
            {
                try
                {
                    _client.Close();
                }
                catch
                {
                }

                _client = null;
            }
        }

        private static int FindLineEnding(List<byte> buffer)
        {
            for (var index = 0; index < buffer.Count - 1; index++)
            {
                if (buffer[index] == '\r' && buffer[index + 1] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
