using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace IndustrialCommSdk.FileTransfer.Ftp
{
    /// <summary>Local FTP client lifecycle state.</summary>
    public enum FtpConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Faulted = 3,
        Disposed = 4,
    }

    /// <summary>Remote directory item kind.</summary>
    public enum FtpDirectoryItemType
    {
        Unknown = 0,
        File = 1,
        Directory = 2,
        SymbolicLink = 3,
    }

    /// <summary>Transfer operation kind.</summary>
    public enum FtpTransferDirection
    {
        Upload = 1,
        Download = 2,
    }

    /// <summary>Options for a single upload.</summary>
    public sealed class FtpUploadOptions
    {
        /// <summary>Resume an existing deterministic temporary upload when possible.</summary>
        public bool Resume { get; set; }

        /// <summary>Replace an existing final destination. Enabled by default.</summary>
        public bool Overwrite { get; set; } = true;

        /// <summary>Create missing remote parent directories. Enabled by default.</summary>
        public bool CreateRemoteDirectory { get; set; } = true;

        /// <summary>Upload to a temporary name and move it into place after verification. Enabled by default.</summary>
        public bool Atomic { get; set; } = true;

        /// <summary>Verify by checksum when supported, otherwise by file size when supported. Enabled by default.</summary>
        public bool Verify { get; set; } = true;
    }

    /// <summary>Options for a single download.</summary>
    public sealed class FtpDownloadOptions
    {
        /// <summary>Resume an existing partial local file when possible.</summary>
        public bool Resume { get; set; }

        /// <summary>Replace an existing local file when resume is disabled. Enabled by default.</summary>
        public bool Overwrite { get; set; } = true;

        /// <summary>Create a missing local parent directory. Enabled by default.</summary>
        public bool CreateLocalDirectory { get; set; } = true;

        /// <summary>Verify by checksum when supported, otherwise by file size when supported. Enabled by default.</summary>
        public bool Verify { get; set; } = true;
    }

    /// <summary>Snapshot of FTP server features observed after authentication.</summary>
    public sealed class FtpServerCapabilities
    {
        internal FtpServerCapabilities(
            bool available,
            bool encrypted,
            bool supportsResume,
            bool supportsChecksum,
            bool supportsFileSize,
            bool supportsMachineListing,
            bool supportsUtf8,
            bool supportsNoop,
            string serverType,
            string systemType,
            IEnumerable<string> features,
            IEnumerable<string> hashAlgorithms,
            DateTimeOffset probedAtUtc)
        {
            IsAvailable = available;
            IsEncrypted = encrypted;
            SupportsResume = supportsResume;
            SupportsChecksum = supportsChecksum;
            SupportsFileSize = supportsFileSize;
            SupportsMachineListing = supportsMachineListing;
            SupportsUtf8 = supportsUtf8;
            SupportsNoop = supportsNoop;
            ServerType = serverType;
            SystemType = systemType;
            Features = new ReadOnlyCollection<string>(new List<string>(features ?? Array.Empty<string>()));
            HashAlgorithms = new ReadOnlyCollection<string>(new List<string>(hashAlgorithms ?? Array.Empty<string>()));
            ProbedAtUtc = probedAtUtc;
        }

        public bool IsAvailable { get; private set; }
        public bool IsEncrypted { get; private set; }
        public bool SupportsResume { get; private set; }
        public bool SupportsChecksum { get; private set; }
        public bool SupportsFileSize { get; private set; }
        public bool SupportsMachineListing { get; private set; }
        public bool SupportsUtf8 { get; private set; }
        public bool SupportsNoop { get; private set; }
        public string ServerType { get; private set; }
        public string SystemType { get; private set; }
        public IReadOnlyList<string> Features { get; private set; }
        public IReadOnlyList<string> HashAlgorithms { get; private set; }
        public DateTimeOffset ProbedAtUtc { get; private set; }

        internal static FtpServerCapabilities Unavailable()
        {
            return new FtpServerCapabilities(
                false, false, false, false, false, false, false, false,
                null, null, Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.MinValue);
        }
    }

    /// <summary>Connection/health snapshot.</summary>
    public sealed class FtpClientHealth
    {
        internal FtpClientHealth(
            FtpConnectionState state,
            bool connected,
            bool encrypted,
            DateTimeOffset checkedAtUtc,
            DateTimeOffset? lastSuccessfulOperationUtc,
            DateTimeOffset? lastFailureUtc,
            string lastError)
        {
            State = state;
            IsConnected = connected;
            IsEncrypted = encrypted;
            CheckedAtUtc = checkedAtUtc;
            LastSuccessfulOperationUtc = lastSuccessfulOperationUtc;
            LastFailureUtc = lastFailureUtc;
            LastError = lastError;
        }

        public FtpConnectionState State { get; private set; }
        public bool IsConnected { get; private set; }
        public bool IsEncrypted { get; private set; }
        public DateTimeOffset CheckedAtUtc { get; private set; }
        public DateTimeOffset? LastSuccessfulOperationUtc { get; private set; }
        public DateTimeOffset? LastFailureUtc { get; private set; }
        public string LastError { get; private set; }
    }

    /// <summary>One entry returned by a remote directory listing.</summary>
    public sealed class FtpDirectoryItem
    {
        internal FtpDirectoryItem(
            string name,
            string remotePath,
            FtpDirectoryItemType type,
            long size,
            DateTimeOffset? modifiedUtc,
            DateTimeOffset? createdUtc,
            string linkTarget)
        {
            Name = name;
            RemotePath = remotePath;
            Type = type;
            Size = size;
            ModifiedUtc = modifiedUtc;
            CreatedUtc = createdUtc;
            LinkTarget = linkTarget;
        }

        public string Name { get; private set; }

        /// <summary>Canonical path relative to the configured FTP root, beginning with /.</summary>
        public string RemotePath { get; private set; }

        public FtpDirectoryItemType Type { get; private set; }
        public long Size { get; private set; }
        public DateTimeOffset? ModifiedUtc { get; private set; }
        public DateTimeOffset? CreatedUtc { get; private set; }
        public string LinkTarget { get; private set; }
    }

    /// <summary>Progress reported during one file transfer.</summary>
    public sealed class FtpTransferProgress
    {
        internal FtpTransferProgress(
            FtpTransferDirection direction,
            string localPath,
            string remotePath,
            double percentage,
            long transferredBytes,
            long totalBytes,
            double bytesPerSecond,
            TimeSpan estimatedRemaining)
        {
            Direction = direction;
            LocalPath = localPath;
            RemotePath = remotePath;
            Percentage = percentage;
            TransferredBytes = transferredBytes;
            TotalBytes = totalBytes;
            BytesPerSecond = bytesPerSecond;
            EstimatedRemaining = estimatedRemaining;
        }

        public FtpTransferDirection Direction { get; private set; }
        public string LocalPath { get; private set; }
        public string RemotePath { get; private set; }
        public double Percentage { get; private set; }
        public long TransferredBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public double BytesPerSecond { get; private set; }
        public TimeSpan EstimatedRemaining { get; private set; }
    }

    /// <summary>Completed transfer result, including the verification mechanism actually used.</summary>
    public sealed class FtpTransferResult
    {
        internal FtpTransferResult(
            FtpTransferDirection direction,
            string localPath,
            string remotePath,
            long bytes,
            bool resumed,
            bool verified,
            string verificationMethod)
        {
            Direction = direction;
            LocalPath = localPath;
            RemotePath = remotePath;
            Bytes = bytes;
            WasResumed = resumed;
            WasVerified = verified;
            VerificationMethod = verificationMethod;
        }

        public FtpTransferDirection Direction { get; private set; }
        public string LocalPath { get; private set; }
        public string RemotePath { get; private set; }
        public long Bytes { get; private set; }
        public bool WasResumed { get; private set; }
        public bool WasVerified { get; private set; }
        public string VerificationMethod { get; private set; }
    }
}
