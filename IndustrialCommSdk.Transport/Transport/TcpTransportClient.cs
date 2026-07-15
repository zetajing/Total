using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Transport
{
    /// <summary>
    /// Thread-safe TCP transport client. Every I/O operation uses an immutable connection snapshot,
    /// so disconnect, reconnect and disposal cannot replace the stream underneath an operation.
    /// </summary>
    public sealed partial class TcpTransportClient : IStreamingTransportClient
    {
        private readonly TcpTransportOptions _options;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _receiveGate = new SemaphoreSlim(1, 1);
        private int _disposed;

        public TcpTransportClient(TcpTransportOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.Host)) throw new ArgumentException("TCP host is required.", nameof(options));
            if (_options.Port < 1 || _options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options), "TCP port must be between 1 and 65535.");
            if (_options.ConnectTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Connect timeout must be greater than zero.");
            if (_options.SendTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Send timeout must be greater than zero.");
            if (_options.ReceiveTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Receive timeout must be greater than zero.");
        }

        public bool IsConnected
        {
            get
            {
                var connection = Volatile.Read(ref _connection);
                return connection != null && connection.IsConnected;
            }
        }

        public EndPoint RemoteEndPoint
        {
            get
            {
                var connection = Volatile.Read(ref _connection);
                return connection == null ? null : connection.RemoteEndPoint;
            }
        }

        /// <summary>
        /// Changes whenever the active connection is published or invalidated. Framing layers use
        /// this value to ensure buffered bytes never cross a TCP connection boundary.
        /// </summary>
        internal long ConnectionGeneration { get { return Volatile.Read(ref _connectionGeneration); } }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (GetConnectedState() != null)
                {
                    return;
                }

                InvalidateCurrentConnection();
                var client = CreateClient();
                var published = false;
                try
                {
                    RegisterPendingClient(client);
                    var connectTask = client.ConnectAsync(_options.Host, _options.Port);
                    var timeoutTask = Task.Delay(_options.ConnectTimeoutMilliseconds, cancellationToken);
                    var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                    if (completedTask != connectTask)
                    {
                        CloseClient(client);
                        await IgnoreFailureAsync(connectTask).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new IndustrialTimeoutException("TCP connect timeout.");
                    }

                    await connectTask.ConfigureAwait(false);
                    var connection = new ConnectionState(client, client.GetStream());
                    PublishConnection(connection);
                    published = true;
                }
                finally
                {
                    ClearPendingClient(client);
                    if (!published)
                    {
                        CloseClient(client);
                    }
                }
            }
            catch (SocketException ex)
            {
                throw new IndustrialConnectionException("TCP connect failed.", ex);
            }
            catch (ObjectDisposedException ex)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(TcpTransportClient));
                }

                throw new IndustrialConnectionException("TCP connect was interrupted.", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                InvalidateCurrentConnection();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            ThrowIfDisposed();
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await AwaitIoAsync(
                        connection.Stream.WriteAsync(payload, 0, payload.Length, cancellationToken),
                        connection,
                        _options.SendTimeoutMilliseconds,
                        "TCP send timeout.",
                        cancellationToken).ConfigureAwait(false);
                }
                catch (IndustrialTimeoutException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    ThrowIfDisposed();
                    throw;
                }
                catch (Exception ex) when (IsConnectionFailure(ex))
                {
                    InvalidateConnection(connection);
                    ThrowDisposedOrConnectionFailure("TCP send failed because the connection was closed.", ex);
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async Task<byte[]> ReceiveExactAsync(int length, CancellationToken cancellationToken)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            ThrowIfDisposed();
            await _receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[length];
                var offset = 0;
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    while (offset < length)
                    {
                        var remainingTimeout = _options.ReceiveTimeoutMilliseconds - (int)stopwatch.ElapsedMilliseconds;
                        if (remainingTimeout <= 0)
                        {
                            InvalidateConnection(connection);
                            throw new IndustrialTimeoutException("TCP receive timeout.");
                        }

                        var read = await AwaitIoAsync(
                            connection.Stream.ReadAsync(buffer, offset, length - offset, cancellationToken),
                            connection,
                            remainingTimeout,
                            "TCP receive timeout.",
                            cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            InvalidateConnection(connection);
                            throw new IndustrialConnectionException("Remote peer closed the connection.");
                        }

                        offset += read;
                    }

                    return buffer;
                }
                catch (IndustrialCommunicationException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    ThrowIfDisposed();
                    throw;
                }
                catch (Exception ex) when (IsConnectionFailure(ex))
                {
                    InvalidateConnection(connection);
                    ThrowDisposedOrConnectionFailure("TCP receive failed because the connection was closed.", ex);
                    return null;
                }
            }
            finally
            {
                _receiveGate.Release();
            }
        }

        public async Task<byte[]> ReceiveAsync(int maxLength, CancellationToken cancellationToken)
        {
            var result = await ReceiveWithGenerationAsync(maxLength, cancellationToken).ConfigureAwait(false);
            return result.Payload;
        }

        internal async Task<TransportReadResult> ReceiveWithGenerationAsync(int maxLength, CancellationToken cancellationToken)
        {
            if (maxLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxLength));
            ThrowIfDisposed();
            await _receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[maxLength];
                try
                {
                    var read = await AwaitIoAsync(
                        connection.Stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken),
                        connection,
                        _options.ReceiveTimeoutMilliseconds,
                        "TCP receive timeout.",
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        InvalidateConnection(connection);
                        throw new IndustrialConnectionException("Remote peer closed the connection.");
                    }

                    if (read != buffer.Length)
                    {
                        var result = new byte[read];
                        Buffer.BlockCopy(buffer, 0, result, 0, read);
                        buffer = result;
                    }

                    return new TransportReadResult(buffer, connection.Generation);
                }
                catch (IndustrialCommunicationException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    ThrowIfDisposed();
                    throw;
                }
                catch (Exception ex) when (IsConnectionFailure(ex))
                {
                    InvalidateConnection(connection);
                    ThrowDisposedOrConnectionFailure("TCP receive failed because the connection was closed.", ex);
                    return default(TransportReadResult);
                }
            }
            finally
            {
                _receiveGate.Release();
            }
        }

        private async Task AwaitIoAsync(
            Task operation,
            ConnectionState connection,
            int timeoutMilliseconds,
            string timeoutMessage,
            CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(timeoutMilliseconds, cancellationToken);
            var completed = await Task.WhenAny(operation, timeoutTask).ConfigureAwait(false);
            if (completed == operation)
            {
                await operation.ConfigureAwait(false);
                return;
            }

            InvalidateConnection(connection);
            await IgnoreFailureAsync(operation).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new IndustrialTimeoutException(timeoutMessage);
        }

        private async Task<T> AwaitIoAsync<T>(
            Task<T> operation,
            ConnectionState connection,
            int timeoutMilliseconds,
            string timeoutMessage,
            CancellationToken cancellationToken)
        {
            var timeoutTask = Task.Delay(timeoutMilliseconds, cancellationToken);
            var completed = await Task.WhenAny(operation, timeoutTask).ConfigureAwait(false);
            if (completed == operation)
            {
                return await operation.ConfigureAwait(false);
            }

            InvalidateConnection(connection);
            await IgnoreFailureAsync(operation).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new IndustrialTimeoutException(timeoutMessage);
        }

        private async Task<ConnectionState> EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var connection = GetConnectedState();
            if (connection != null)
            {
                return connection;
            }

            if (!_options.AutoReconnect)
            {
                throw new IndustrialConnectionException("TCP client is not connected.");
            }

            await ConnectAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfDisposed();
            connection = GetConnectedState();
            if (connection == null)
            {
                throw new IndustrialConnectionException("TCP client is not connected.");
            }

            return connection;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            ConnectionState connection;
            TcpClient pendingClient;
            lock (_connectionSync)
            {
                connection = _connection;
                pendingClient = _pendingClient;
                Volatile.Write(ref _connection, null);
                _pendingClient = null;
                Interlocked.Increment(ref _connectionGeneration);
            }

            connection?.Close();
            CloseClient(pendingClient);

            // Do not dispose the semaphores: callers queued immediately before disposal must still
            // enter their finally blocks safely and then observe ObjectDisposedException.
            WaitForGate(_gate);
            WaitForGate(_sendGate);
            WaitForGate(_receiveGate);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(TcpTransportClient));
            }
        }

        private void ThrowDisposedOrConnectionFailure(string message, Exception innerException)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(TcpTransportClient));
            }

            throw new IndustrialConnectionException(message, innerException);
        }

        private static bool IsConnectionFailure(Exception exception)
        {
            return exception is IOException || exception is SocketException || exception is ObjectDisposedException;
        }

        private static async Task IgnoreFailureAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Closing the connection is expected to fault an outstanding connect or I/O task.
            }
        }

        private static void CloseClient(TcpClient client)
        {
            if (client == null)
            {
                return;
            }

            try { client.Close(); }
            catch { }
        }

        private static void WaitForGate(SemaphoreSlim gate)
        {
            gate.Wait();
            gate.Release();
        }

    }
}
