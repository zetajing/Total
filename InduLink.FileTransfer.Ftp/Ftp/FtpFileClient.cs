using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using InduLink.Diagnostics;

namespace InduLink.FileTransfer.Ftp
{
    /// <summary>
    /// Thread-safe, single-session FTP/FTPS client backed by FluentFTP. Commands are serialized because an FTP
    /// control connection cannot safely interleave command/reply sequences from concurrent callers.
    /// </summary>
    public sealed class FtpFileClient : IFtpFileClient
    {
        private readonly FtpClientOptions _options;
        private readonly FtpRemotePath _remotePath;
        private readonly AsyncFtpClient _client;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly object _healthSync = new object();
        private FtpServerCapabilities _capabilities = FtpServerCapabilities.Unavailable();
        private DateTimeOffset? _lastSuccessUtc;
        private DateTimeOffset? _lastFailureUtc;
        private string _lastError;
        private int _state = (int)FtpConnectionState.Disconnected;
        private int _disposed;

        public FtpFileClient(FtpClientOptions options, IIndustrialLogger logger = null)
        {
            _options = ValidateAndCopyOptions(options);
            _remotePath = new FtpRemotePath(_options.RootPath);
            _logger = logger ?? NullIndustrialLogger.Instance;

            var config = new FtpConfig
            {
                EncryptionMode = ToFluentEncryptionMode(_options.SecurityMode),
                DataConnectionEncryption = _options.SecurityMode != FtpSecurityMode.Plain,
                DataConnectionType = _options.DataConnectionMode == FtpDataConnectionMode.Passive
                    ? FtpDataConnectionType.AutoPassive
                    : FtpDataConnectionType.AutoActive,
                ConnectTimeout = _options.ConnectTimeoutMilliseconds,
                ReadTimeout = _options.OperationTimeoutMilliseconds,
                WriteTimeout = _options.OperationTimeoutMilliseconds,
                DataConnectionConnectTimeout = _options.DataConnectTimeoutMilliseconds,
                DataConnectionReadTimeout = _options.DataOperationTimeoutMilliseconds,
                DataConnectionWriteTimeout = _options.DataOperationTimeoutMilliseconds,
                RetryAttempts = _options.RetryAttempts,
                ValidateAnyCertificate = false,
                ValidateCertificateRevocation = _options.ValidateCertificateRevocation,
                SslProtocols = _options.SslProtocols,
                SocketKeepAlive = _options.SocketKeepAlive,
                TimeConversion = FtpDate.UTC,
                SanitizeTraversal = true,
                SanitizeControlChars = true,
                VerifyMethod = FtpVerifyMethod.Size | FtpVerifyMethod.Checksum,
            };

            _client = new AsyncFtpClient(
                _options.Host,
                new NetworkCredential(_options.Username, _options.Password),
                _options.Port,
                config,
                null);
            _client.ValidateCertificate += (sender, args) => ValidateCertificate(args);
        }

        public FtpConnectionState State
        {
            get { return (FtpConnectionState)Volatile.Read(ref _state); }
        }

        public bool IsConnected
        {
            get { return Volatile.Read(ref _disposed) == 0 && _client.IsConnected; }
        }

        public FtpServerCapabilities Capabilities
        {
            get { return Volatile.Read(ref _capabilities); }
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                SetStateUnlessDisposed(FtpConnectionState.Connecting);
                Volatile.Write(ref _capabilities, FtpServerCapabilities.Unavailable());
                if (_client.IsConnected)
                    await _client.Disconnect(cancellationToken).ConfigureAwait(false);

                await _client.Connect(cancellationToken).ConfigureAwait(false);
                ThrowIfDisposed();
                if (!_client.IsConnected)
                    throw new IOException("FTP authentication completed without an active control connection.");

                Volatile.Write(ref _capabilities, BuildCapabilities());
                SetStateUnlessDisposed(FtpConnectionState.Connected);
                RecordSuccess();
                _logger.Info(string.Format(
                    "FTP connected | Endpoint={0}:{1} | TLS={2} | Root={3}",
                    _options.Host,
                    _options.Port,
                    _client.IsEncrypted,
                    _remotePath.Root));
            }
            catch (OperationCanceledException)
            {
                SetStateUnlessDisposed(_client.IsConnected
                    ? FtpConnectionState.Connected
                    : FtpConnectionState.Disconnected);
                throw;
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _capabilities, FtpServerCapabilities.Unavailable());
                SetStateUnlessDisposed(FtpConnectionState.Faulted);
                RecordFailure(ex);
                _logger.Error("FTP connection failed.", ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_client.IsConnected)
                    await _client.Disconnect(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _capabilities, FtpServerCapabilities.Unavailable());
                SetStateUnlessDisposed(FtpConnectionState.Disconnected);
                RecordSuccess();
                _logger.Info(string.Format("FTP disconnected | Endpoint={0}:{1}", _options.Host, _options.Port));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                SetStateUnlessDisposed(_client.IsConnected
                    ? FtpConnectionState.Connected
                    : FtpConnectionState.Faulted);
                _logger.Error("FTP disconnect failed.", ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<FtpClientHealth> CheckHealthAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                if (_client.Capabilities.Contains(FtpCapability.NOOP))
                {
                    var reply = await _client.Execute("NOOP", cancellationToken).ConfigureAwait(false);
                    if (!reply.Success)
                        throw new IOException("FTP NOOP health check was rejected: " + (reply.ErrorMessage ?? "no reply"));
                }
                else
                {
                    await _client.GetWorkingDirectory(cancellationToken).ConfigureAwait(false);
                }

                SetStateUnlessDisposed(FtpConnectionState.Connected);
                RecordSuccess();
                return GetHealth();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                SetStateUnlessDisposed(_client.IsConnected
                    ? FtpConnectionState.Connected
                    : FtpConnectionState.Faulted);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public FtpClientHealth GetHealth()
        {
            lock (_healthSync)
            {
                return new FtpClientHealth(
                    State,
                    IsConnected,
                    Volatile.Read(ref _disposed) == 0 && _client.IsEncrypted,
                    DateTimeOffset.UtcNow,
                    _lastSuccessUtc,
                    _lastFailureUtc,
                    _lastError);
            }
        }

        public async Task<FtpServerCapabilities> ProbeCapabilitiesAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                var capabilities = BuildCapabilities();
                Volatile.Write(ref _capabilities, capabilities);
                RecordSuccess();
                return capabilities;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<IReadOnlyList<FtpDirectoryItem>> ListDirectoryAsync(
            string remotePath,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var resolved = _remotePath.Resolve(remotePath, true);
            var relativeParent = _remotePath.ToRelative(remotePath);
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                var listing = await _client.GetListing(resolved, FtpListOption.Auto, cancellationToken).ConfigureAwait(false);
                var items = new List<FtpDirectoryItem>(listing.Length);
                foreach (var item in listing)
                {
                    if (item == null || item.Name == "." || item.Name == "..") continue;
                    items.Add(MapDirectoryItem(item, relativeParent));
                }
                RecordSuccess();
                return items.AsReadOnly();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task CreateDirectoryAsync(
            string remotePath,
            bool createParents,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var resolved = _remotePath.Resolve(remotePath, false);
            await ExecuteConnectedAsync(async () =>
            {
                var created = await _client.CreateDirectory(resolved, createParents, cancellationToken).ConfigureAwait(false);
                if (!created && !await _client.DirectoryExists(resolved, cancellationToken).ConfigureAwait(false))
                    throw new IOException("FTP server did not create the requested directory.");
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<FtpTransferResult> UploadFileAsync(
            string localFilePath,
            string remotePath,
            FtpUploadOptions options,
            IProgress<FtpTransferProgress> progress,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(localFilePath))
                throw new ArgumentException("Local upload path is required.", nameof(localFilePath));
            var localFullPath = Path.GetFullPath(localFilePath);
            if (!File.Exists(localFullPath))
                throw new FileNotFoundException("Local upload file was not found.", localFullPath);

            options = options ?? new FtpUploadOptions();
            var resolvedTarget = _remotePath.Resolve(remotePath, false);
            var relativeTarget = _remotePath.ToRelative(remotePath);
            var fileLength = new FileInfo(localFullPath).Length;

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                if (!options.Overwrite && await _client.FileExists(resolvedTarget, cancellationToken).ConfigureAwait(false))
                    throw new IOException("The destination FTP file already exists and overwrite is disabled.");

                var uploadPath = options.Atomic
                    ? resolvedTarget + _options.AtomicUploadTemporarySuffix
                    : resolvedTarget;
                var existsMode = options.Resume ? FtpRemoteExists.Resume : FtpRemoteExists.Overwrite;
                var adapter = new FluentProgressAdapter(
                    FtpTransferDirection.Upload,
                    localFullPath,
                    relativeTarget,
                    fileLength,
                    progress);
                var status = await _client.UploadFile(
                    localFullPath,
                    uploadPath,
                    existsMode,
                    options.CreateRemoteDirectory,
                    FtpVerify.None,
                    adapter,
                    cancellationToken).ConfigureAwait(false);
                if (status == FtpStatus.Failed)
                    throw new IOException("FTP upload failed before verification.");

                var verification = options.Verify
                    ? await VerifyLocalAndRemoteAsync(localFullPath, uploadPath, cancellationToken).ConfigureAwait(false)
                    : VerificationResult.NotRequested();

                if (options.Atomic)
                {
                    var moved = await _client.MoveFile(
                        uploadPath,
                        resolvedTarget,
                        options.Overwrite ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip,
                        cancellationToken).ConfigureAwait(false);
                    if (!moved)
                        throw new IOException("FTP temporary upload could not be moved to its final destination.");
                }

                RecordSuccess();
                return new FtpTransferResult(
                    FtpTransferDirection.Upload,
                    localFullPath,
                    relativeTarget,
                    fileLength,
                    options.Resume,
                    verification.Verified,
                    verification.Method);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<FtpTransferResult> DownloadFileAsync(
            string remotePath,
            string localFilePath,
            FtpDownloadOptions options,
            IProgress<FtpTransferProgress> progress,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(localFilePath))
                throw new ArgumentException("Local download path is required.", nameof(localFilePath));

            options = options ?? new FtpDownloadOptions();
            var resolvedSource = _remotePath.Resolve(remotePath, false);
            var relativeSource = _remotePath.ToRelative(remotePath);
            var localFullPath = Path.GetFullPath(localFilePath);
            if (!options.Resume && !options.Overwrite && File.Exists(localFullPath))
                throw new IOException("The local destination file already exists and overwrite is disabled.");
            var parent = Path.GetDirectoryName(localFullPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                if (!options.CreateLocalDirectory)
                    throw new DirectoryNotFoundException("The local destination directory does not exist: " + parent);
                Directory.CreateDirectory(parent);
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                var remoteLength = await TryGetRemoteSizeAsync(resolvedSource, cancellationToken).ConfigureAwait(false);
                var adapter = new FluentProgressAdapter(
                    FtpTransferDirection.Download,
                    localFullPath,
                    relativeSource,
                    remoteLength,
                    progress);
                var status = await _client.DownloadFile(
                    localFullPath,
                    resolvedSource,
                    options.Resume ? FtpLocalExists.Resume : FtpLocalExists.Overwrite,
                    FtpVerify.None,
                    adapter,
                    cancellationToken).ConfigureAwait(false);
                if (status == FtpStatus.Failed)
                    throw new IOException("FTP download failed before verification.");

                var verification = options.Verify
                    ? await VerifyLocalAndRemoteAsync(localFullPath, resolvedSource, cancellationToken).ConfigureAwait(false)
                    : VerificationResult.NotRequested();
                var finalLength = new FileInfo(localFullPath).Length;
                RecordSuccess();
                return new FtpTransferResult(
                    FtpTransferDirection.Download,
                    localFullPath,
                    relativeSource,
                    finalLength,
                    options.Resume,
                    verification.Verified,
                    verification.Method);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public Task DeleteFileAsync(string remotePath, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var resolved = _remotePath.Resolve(remotePath, false);
            return ExecuteConnectedAsync(
                () => _client.DeleteFile(resolved, cancellationToken),
                cancellationToken);
        }

        public async Task RenameAsync(
            string sourceRemotePath,
            string destinationRemotePath,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var source = _remotePath.Resolve(sourceRemotePath, false);
            var destination = _remotePath.Resolve(destinationRemotePath, false);
            if (string.Equals(source, destination, StringComparison.Ordinal)) return;

            await ExecuteConnectedAsync(async () =>
            {
                var moved = await _client.MoveFile(
                    source,
                    destination,
                    overwrite ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip,
                    cancellationToken).ConfigureAwait(false);
                if (!moved) throw new IOException("FTP server did not rename the requested file.");
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteConnectedAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                EnsureConnected();
                await operation().ConfigureAwait(false);
                RecordSuccess();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<VerificationResult> VerifyLocalAndRemoteAsync(
            string localPath,
            string resolvedRemotePath,
            CancellationToken cancellationToken)
        {
            if (_client.HashAlgorithms != FtpHashAlgorithm.NONE)
            {
                var result = await _client.CompareFile(
                    localPath,
                    resolvedRemotePath,
                    FtpCompareOption.Checksum,
                    cancellationToken).ConfigureAwait(false);
                if (result == FtpCompareResult.Equal) return VerificationResult.Success("Checksum");
                if (result == FtpCompareResult.NotEqual)
                    throw new InvalidDataException("FTP file checksum verification failed.");
                if (result == FtpCompareResult.FileNotExisting)
                    throw new FileNotFoundException("Remote FTP file disappeared before verification.", resolvedRemotePath);
            }

            if (_client.Capabilities.Contains(FtpCapability.SIZE))
            {
                var remoteSize = await _client.GetFileSize(resolvedRemotePath, -1L, cancellationToken).ConfigureAwait(false);
                var localSize = new FileInfo(localPath).Length;
                if (remoteSize < 0) return VerificationResult.Unsupported();
                if (remoteSize != localSize)
                    throw new InvalidDataException(string.Format(
                        "FTP file size verification failed. Local={0}, Remote={1}.",
                        localSize,
                        remoteSize));
                return VerificationResult.Success("Size");
            }

            return VerificationResult.Unsupported();
        }

        private async Task<long> TryGetRemoteSizeAsync(string resolvedRemotePath, CancellationToken cancellationToken)
        {
            if (!_client.Capabilities.Contains(FtpCapability.SIZE)) return -1L;
            return await _client.GetFileSize(resolvedRemotePath, -1L, cancellationToken).ConfigureAwait(false);
        }

        private FtpServerCapabilities BuildCapabilities()
        {
            var featureNames = _client.Capabilities
                .Select(value => value.ToString())
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var hashNames = Enum.GetValues(typeof(FtpHashAlgorithm))
                .Cast<FtpHashAlgorithm>()
                .Where(value => value != FtpHashAlgorithm.NONE && (_client.HashAlgorithms & value) == value)
                .Select(value => value.ToString())
                .ToArray();
            return new FtpServerCapabilities(
                true,
                _client.IsEncrypted,
                _client.Capabilities.Contains(FtpCapability.REST),
                _client.HashAlgorithms != FtpHashAlgorithm.NONE,
                _client.Capabilities.Contains(FtpCapability.SIZE),
                _client.Capabilities.Contains(FtpCapability.MLST),
                _client.Capabilities.Contains(FtpCapability.UTF8),
                _client.Capabilities.Contains(FtpCapability.NOOP),
                _client.ServerType.ToString(),
                _client.SystemType,
                featureNames,
                hashNames,
                DateTimeOffset.UtcNow);
        }

        private FtpDirectoryItem MapDirectoryItem(FtpListItem item, string relativeParent)
        {
            FtpDirectoryItemType type;
            switch (item.Type)
            {
                case FtpObjectType.File: type = FtpDirectoryItemType.File; break;
                case FtpObjectType.Directory: type = FtpDirectoryItemType.Directory; break;
                case FtpObjectType.Link: type = FtpDirectoryItemType.SymbolicLink; break;
                default: type = FtpDirectoryItemType.Unknown; break;
            }
            return new FtpDirectoryItem(
                item.Name,
                _remotePath.ToRelative(relativeParent, item.Name),
                type,
                item.Size,
                ToNullableUtc(item.Modified),
                ToNullableUtc(item.Created),
                item.LinkTarget);
        }

        private static DateTimeOffset? ToNullableUtc(DateTime value)
        {
            if (value == DateTime.MinValue) return null;
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        private void EnsureConnected()
        {
            if (!_client.IsConnected)
            {
                SetStateUnlessDisposed(FtpConnectionState.Faulted);
                throw new InvalidOperationException("FTP client is not connected.");
            }
        }

        private void RecordSuccess()
        {
            lock (_healthSync)
            {
                _lastSuccessUtc = DateTimeOffset.UtcNow;
                _lastError = null;
            }
        }

        private void RecordFailure(Exception exception)
        {
            lock (_healthSync)
            {
                _lastFailureUtc = DateTimeOffset.UtcNow;
                _lastError = exception.Message;
            }
            if (!_client.IsConnected && Volatile.Read(ref _disposed) == 0)
            {
                Volatile.Write(ref _capabilities, FtpServerCapabilities.Unavailable());
                SetStateUnlessDisposed(FtpConnectionState.Faulted);
            }
        }

        private void ValidateCertificate(FtpSslValidationEventArgs args)
        {
            args.Accept = FtpCertificateValidator.IsAccepted(
                args.Certificate,
                args.PolicyErrors,
                _options.TrustedCertificateThumbprint);
            if (!args.Accept)
                _logger.Warn("FTPS certificate validation failed: " + args.PolicyErrors);
        }

        private static FtpEncryptionMode ToFluentEncryptionMode(FtpSecurityMode value)
        {
            switch (value)
            {
                case FtpSecurityMode.ExplicitTls: return FtpEncryptionMode.Explicit;
                case FtpSecurityMode.ImplicitTls: return FtpEncryptionMode.Implicit;
                case FtpSecurityMode.Plain: return FtpEncryptionMode.None;
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static FtpClientOptions ValidateAndCopyOptions(FtpClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Host))
                throw new ArgumentException("FTP host is required.", nameof(options));
            if (options.Host.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                options.Host.IndexOf('/') >= 0 || options.Host.IndexOf('\\') >= 0)
                throw new ArgumentException("FTP Host must be a host name or IP address, not a URI.", nameof(options));
            if (options.Port < 1 || options.Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(options.Port));
            if (!Enum.IsDefined(typeof(FtpSecurityMode), options.SecurityMode))
                throw new ArgumentOutOfRangeException(nameof(options.SecurityMode));
            if (!Enum.IsDefined(typeof(FtpDataConnectionMode), options.DataConnectionMode))
                throw new ArgumentOutOfRangeException(nameof(options.DataConnectionMode));
            if (options.SecurityMode == FtpSecurityMode.Plain && !options.AllowInsecureFtp)
                throw new InvalidOperationException("Plain FTP is disabled. Set AllowInsecureFtp only for an explicitly accepted trusted-network requirement.");
            if (options.ConnectTimeoutMilliseconds <= 0 || options.OperationTimeoutMilliseconds <= 0 ||
                options.DataConnectTimeoutMilliseconds <= 0 || options.DataOperationTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "All FTP timeouts must be positive.");
            if (options.RetryAttempts < 0)
                throw new ArgumentOutOfRangeException(nameof(options.RetryAttempts));
            if ((options.SslProtocols & (SslProtocols.Ssl2 | SslProtocols.Ssl3)) != 0)
                throw new ArgumentException("SSL 2.0 and SSL 3.0 are not permitted.", nameof(options));
            var normalizedThumbprint = FtpCertificateValidator.NormalizeThumbprint(options.TrustedCertificateThumbprint);
            if (!string.IsNullOrWhiteSpace(options.TrustedCertificateThumbprint) &&
                (normalizedThumbprint == null || (normalizedThumbprint.Length != 40 && normalizedThumbprint.Length != 64)))
                throw new ArgumentException("TrustedCertificateThumbprint must be a SHA-1 or SHA-256 hexadecimal certificate thumbprint.", nameof(options));
            ValidateTemporarySuffix(options.AtomicUploadTemporarySuffix);

            var copy = new FtpClientOptions
            {
                Host = options.Host.Trim(),
                Port = options.Port,
                Username = string.IsNullOrEmpty(options.Username) ? "anonymous" : options.Username,
                Password = options.Password ?? string.Empty,
                RootPath = string.IsNullOrWhiteSpace(options.RootPath) ? "/" : options.RootPath,
                SecurityMode = options.SecurityMode,
                AllowInsecureFtp = options.AllowInsecureFtp,
                DataConnectionMode = options.DataConnectionMode,
                ConnectTimeoutMilliseconds = options.ConnectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = options.OperationTimeoutMilliseconds,
                DataConnectTimeoutMilliseconds = options.DataConnectTimeoutMilliseconds,
                DataOperationTimeoutMilliseconds = options.DataOperationTimeoutMilliseconds,
                RetryAttempts = options.RetryAttempts,
                ValidateCertificateRevocation = options.ValidateCertificateRevocation,
                TrustedCertificateThumbprint = normalizedThumbprint,
                SslProtocols = options.SslProtocols,
                SocketKeepAlive = options.SocketKeepAlive,
                AtomicUploadTemporarySuffix = options.AtomicUploadTemporarySuffix,
            };
            // Constructing this object performs canonical root validation before any network connection is attempted.
            new FtpRemotePath(copy.RootPath);
            return copy;
        }

        private static void ValidateTemporarySuffix(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
                throw new ArgumentException("Atomic upload temporary suffix is required.", nameof(suffix));
            if (suffix.IndexOf('/') >= 0 || suffix.IndexOf('\\') >= 0 || suffix == "." || suffix == "..")
                throw new ArgumentException("Atomic upload temporary suffix must be one safe file-name suffix.", nameof(suffix));
            foreach (var value in suffix)
                if (char.IsControl(value))
                    throw new ArgumentException("Atomic upload temporary suffix cannot contain control characters.", nameof(suffix));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(FtpFileClient));
        }

        private void SetStateUnlessDisposed(FtpConnectionState state)
        {
            if (Volatile.Read(ref _disposed) == 0)
                Volatile.Write(ref _state, (int)state);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Volatile.Write(ref _state, (int)FtpConnectionState.Disposed);
            Volatile.Write(ref _capabilities, FtpServerCapabilities.Unavailable());
            _client.Dispose();
        }

        private sealed class VerificationResult
        {
            private VerificationResult(bool verified, string method)
            {
                Verified = verified;
                Method = method;
            }

            public bool Verified { get; private set; }
            public string Method { get; private set; }

            public static VerificationResult Success(string method) { return new VerificationResult(true, method); }
            public static VerificationResult Unsupported() { return new VerificationResult(false, "NotSupported"); }
            public static VerificationResult NotRequested() { return new VerificationResult(false, "NotRequested"); }
        }

        private sealed class FluentProgressAdapter : IProgress<FtpProgress>
        {
            private readonly FtpTransferDirection _direction;
            private readonly string _localPath;
            private readonly string _remotePath;
            private readonly long _totalBytes;
            private readonly IProgress<FtpTransferProgress> _target;

            public FluentProgressAdapter(
                FtpTransferDirection direction,
                string localPath,
                string remotePath,
                long totalBytes,
                IProgress<FtpTransferProgress> target)
            {
                _direction = direction;
                _localPath = localPath;
                _remotePath = remotePath;
                _totalBytes = totalBytes;
                _target = target;
            }

            public void Report(FtpProgress value)
            {
                if (_target == null || value == null) return;
                _target.Report(new FtpTransferProgress(
                    _direction,
                    _localPath,
                    _remotePath,
                    value.Progress,
                    value.TransferredBytes,
                    _totalBytes,
                    value.TransferSpeed,
                    value.ETA));
            }
        }
    }
}
