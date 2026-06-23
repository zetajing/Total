using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Transport
{
    public sealed class TcpTransportSession : IDisposable
    {
        private readonly TcpClient _client;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly NetworkStream _stream;

        public TcpTransportSession(TcpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _stream = client.GetStream();
            SessionId = Guid.NewGuid();
        }

        public Guid SessionId { get; private set; }
        public bool IsConnected { get { return _client.Connected; } }

        public event EventHandler<byte[]> DataReceived;
        public event EventHandler Closed;

        public void Start()
        {
            Task.Run(ReceiveLoopAsync);
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            await _stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[4096];
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var read = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    var data = new byte[read];
                    Buffer.BlockCopy(buffer, 0, data, 0, read);
                    DataReceived?.Invoke(this, data);
                }
            }
            catch
            {
            }
            finally
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _stream.Dispose();
            }
            catch
            {
            }

            try
            {
                _client.Close();
            }
            catch
            {
            }
            _cts.Dispose();
        }
    }
}
