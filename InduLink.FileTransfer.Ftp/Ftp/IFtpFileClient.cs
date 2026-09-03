using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;

namespace InduLink.FileTransfer.Ftp
{
    /// <summary>
    /// Standard FTP/FTPS file-transfer client. This contract deliberately does not model SFTP (SSH)
    /// and does not implement the address-based industrial device client abstraction.
    /// </summary>
    public interface IFtpFileClient : IFileTransferClient
    {
        /// <summary>Current local lifecycle state.</summary>
        FtpConnectionState State { get; }

        /// <summary>Whether the underlying FTP control connection is currently available.</summary>
        new bool IsConnected { get; }

        /// <summary>Capabilities discovered during the latest successful connection/probe.</summary>
        FtpServerCapabilities Capabilities { get; }

        /// <summary>Connect and authenticate, then capture the server capability set.</summary>
        new Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>Gracefully close the FTP control connection.</summary>
        new Task DisconnectAsync(CancellationToken cancellationToken);

        /// <summary>Issue a lightweight command to verify the current authenticated session.</summary>
        Task<FtpClientHealth> CheckHealthAsync(CancellationToken cancellationToken);

        /// <summary>Return a snapshot of local connection and error state without network I/O.</summary>
        FtpClientHealth GetHealth();

        /// <summary>Refresh the capability model populated by the FTP FEAT exchange at connect time.</summary>
        Task<FtpServerCapabilities> ProbeCapabilitiesAsync(CancellationToken cancellationToken);

        /// <summary>List one remote directory below the configured root.</summary>
        Task<IReadOnlyList<FtpDirectoryItem>> ListDirectoryAsync(
            string remotePath,
            CancellationToken cancellationToken);

        /// <summary>Create a directory below the configured root, optionally including missing parents.</summary>
        Task CreateDirectoryAsync(
            string remotePath,
            bool createParents,
            CancellationToken cancellationToken);

        /// <summary>
        /// Upload a local file. Atomic upload, best available verification and final rename are enabled by default.
        /// </summary>
        Task<FtpTransferResult> UploadFileAsync(
            string localFilePath,
            string remotePath,
            FtpUploadOptions options,
            IProgress<FtpTransferProgress> progress,
            CancellationToken cancellationToken);

        /// <summary>Download a remote file with optional resume and best available verification.</summary>
        Task<FtpTransferResult> DownloadFileAsync(
            string remotePath,
            string localFilePath,
            FtpDownloadOptions options,
            IProgress<FtpTransferProgress> progress,
            CancellationToken cancellationToken);

        /// <summary>Delete a remote file below the configured root.</summary>
        Task DeleteFileAsync(string remotePath, CancellationToken cancellationToken);

        /// <summary>Rename or move a remote file below the configured root.</summary>
        Task RenameAsync(
            string sourceRemotePath,
            string destinationRemotePath,
            bool overwrite,
            CancellationToken cancellationToken);
    }
}
