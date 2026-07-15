using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace IndustrialCommSdk.Transport
{
    public sealed partial class TcpTransportClient
    {
        private readonly object _connectionSync = new object();
        private ConnectionState _connection;
        private TcpClient _pendingClient;
        private long _connectionGeneration;

        private TcpClient CreateClient()
        {
            return new TcpClient
            {
                NoDelay = true,
                SendTimeout = _options.SendTimeoutMilliseconds,
                ReceiveTimeout = _options.ReceiveTimeoutMilliseconds
            };
        }

        private ConnectionState GetConnectedState()
        {
            var connection = Volatile.Read(ref _connection);
            return connection != null && connection.IsConnected ? connection : null;
        }

        private void RegisterPendingClient(TcpClient client)
        {
            lock (_connectionSync)
            {
                ThrowIfDisposed();
                _pendingClient = client;
            }
        }

        private void ClearPendingClient(TcpClient client)
        {
            lock (_connectionSync)
            {
                if (ReferenceEquals(_pendingClient, client))
                {
                    _pendingClient = null;
                }
            }
        }

        private void PublishConnection(ConnectionState connection)
        {
            ConnectionState previous;
            lock (_connectionSync)
            {
                ThrowIfDisposed();
                previous = _connection;
                connection.Generation = Interlocked.Increment(ref _connectionGeneration);
                Volatile.Write(ref _connection, connection);
                if (ReferenceEquals(_pendingClient, connection.Client))
                {
                    _pendingClient = null;
                }
            }

            if (previous != null && !ReferenceEquals(previous, connection))
            {
                previous.Close();
            }
        }

        private void InvalidateCurrentConnection()
        {
            ConnectionState connection;
            lock (_connectionSync)
            {
                connection = _connection;
                Volatile.Write(ref _connection, null);
                Interlocked.Increment(ref _connectionGeneration);
            }

            connection?.Close();
        }

        private void InvalidateConnection(ConnectionState expected)
        {
            lock (_connectionSync)
            {
                if (ReferenceEquals(_connection, expected))
                {
                    Volatile.Write(ref _connection, null);
                    Interlocked.Increment(ref _connectionGeneration);
                }
            }

            // A failed old snapshot is closed, but it can never remove a newer connection.
            expected?.Close();
        }

        internal struct TransportReadResult
        {
            internal TransportReadResult(byte[] payload, long generation)
            {
                Payload = payload;
                Generation = generation;
            }

            internal byte[] Payload { get; private set; }
            internal long Generation { get; private set; }
        }

        private sealed class ConnectionState
        {
            private int _closed;

            internal ConnectionState(TcpClient client, NetworkStream stream)
            {
                Client = client;
                Stream = stream;
            }

            internal TcpClient Client { get; private set; }
            internal NetworkStream Stream { get; private set; }
            internal long Generation { get; set; }

            internal bool IsConnected
            {
                get
                {
                    if (Volatile.Read(ref _closed) != 0 || !Client.Connected)
                    {
                        return false;
                    }

                    try
                    {
                        return !(Client.Client.Poll(1, SelectMode.SelectRead) && Client.Client.Available == 0);
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            internal EndPoint RemoteEndPoint
            {
                get
                {
                    if (Volatile.Read(ref _closed) != 0)
                    {
                        return null;
                    }

                    try { return Client.Client.RemoteEndPoint; }
                    catch { return null; }
                }
            }

            internal void Close()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0)
                {
                    return;
                }

                try { Stream.Dispose(); }
                catch { }
                try { Client.Close(); }
                catch { }
            }
        }
    }
}
