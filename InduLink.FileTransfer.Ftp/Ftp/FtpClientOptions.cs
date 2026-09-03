using System.Security.Authentication;

namespace InduLink.FileTransfer.Ftp
{
    /// <summary>FTP control/data channel security mode.</summary>
    public enum FtpSecurityMode
    {
        /// <summary>Explicit FTP over TLS (AUTH TLS), normally on port 21.</summary>
        ExplicitTls = 1,

        /// <summary>Implicit FTP over TLS, normally on port 990.</summary>
        ImplicitTls = 2,

        /// <summary>Unencrypted FTP. This also requires <see cref="FtpClientOptions.AllowInsecureFtp"/>.</summary>
        Plain = 3,
    }

    /// <summary>FTP data connection mode.</summary>
    public enum FtpDataConnectionMode
    {
        /// <summary>Use passive connections and automatically select EPSV or PASV.</summary>
        Passive = 1,

        /// <summary>Use active connections and automatically select EPRT or PORT.</summary>
        Active = 2,
    }

    /// <summary>Configuration for <see cref="FtpFileClient"/>.</summary>
    public sealed class FtpClientOptions
    {
        /// <summary>FTP server host name or IP address. Do not include an ftp:// URI scheme.</summary>
        public string Host { get; set; }

        /// <summary>FTP control port. The default is 21.</summary>
        public int Port { get; set; } = 21;

        /// <summary>Login name. The default is the standard anonymous FTP identity.</summary>
        public string Username { get; set; } = "anonymous";

        /// <summary>Login password. Applications should obtain this from a secure secret store.</summary>
        public string Password { get; set; } = "anonymous@";

        /// <summary>
        /// Remote root exposed to callers. All operation paths are interpreted relative to this root.
        /// The default is the FTP account root (/).
        /// </summary>
        public string RootPath { get; set; } = "/";

        /// <summary>Control channel security. Explicit FTPS is the secure default.</summary>
        public FtpSecurityMode SecurityMode { get; set; } = FtpSecurityMode.ExplicitTls;

        /// <summary>
        /// Explicit opt-in required before <see cref="FtpSecurityMode.Plain"/> can be used.
        /// This does not weaken certificate validation for FTPS.
        /// </summary>
        public bool AllowInsecureFtp { get; set; }

        /// <summary>FTP data connection mode. Passive mode is the firewall-friendly default.</summary>
        public FtpDataConnectionMode DataConnectionMode { get; set; } = FtpDataConnectionMode.Passive;

        /// <summary>Connect timeout in milliseconds.</summary>
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;

        /// <summary>Control channel read/write timeout in milliseconds.</summary>
        public int OperationTimeoutMilliseconds { get; set; } = 10000;

        /// <summary>Data connection establishment timeout in milliseconds.</summary>
        public int DataConnectTimeoutMilliseconds { get; set; } = 10000;

        /// <summary>Data transfer read/write timeout in milliseconds.</summary>
        public int DataOperationTimeoutMilliseconds { get; set; } = 30000;

        /// <summary>Number of library-level retries for retryable transfer failures.</summary>
        public int RetryAttempts { get; set; } = 1;

        /// <summary>Whether FTPS certificate revocation should be checked. Enabled by default.</summary>
        public bool ValidateCertificateRevocation { get; set; } = true;

        /// <summary>
        /// Optional SHA-1 (40 hex digits) or SHA-256 (64 hex digits) certificate thumbprint pin. When configured,
        /// every server certificate must match this pin, including certificates whose system chain is otherwise
        /// valid. A matching pin acts as the trust basis and therefore also permits a private/self-signed server
        /// certificate. When omitted, normal operating-system certificate validation is required.
        /// </summary>
        public string TrustedCertificateThumbprint { get; set; }

        /// <summary>
        /// TLS protocol selection. None delegates protocol selection to the operating system and is the default.
        /// </summary>
        public SslProtocols SslProtocols { get; set; } = SslProtocols.None;

        /// <summary>Keep the underlying control socket alive while the client is connected.</summary>
        public bool SocketKeepAlive { get; set; } = true;

        /// <summary>
        /// Suffix used for the deterministic temporary remote file created by atomic uploads.
        /// Keeping it deterministic permits a later upload to resume the temporary file.
        /// </summary>
        public string AtomicUploadTemporarySuffix { get; set; } = ".uploading";
    }
}
