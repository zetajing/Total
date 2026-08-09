using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.FileTransfer.Ftp;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class FtpFileClientTests
    {
        private const string TestCertificateBase64 =
            "MIICyjCCAbKgAwIBAgIIIZbag1jtGvYwDQYJKoZIhvcNAQELBQAwJTEjMCEGA1UEAxMa" +
            "SW5kdXN0cmlhbENvbW1TZGsgRlRQIFRlc3QwHhcNMjYwODA4MDM1MzA1WhcNMjgwODA5" +
            "MDM1MzA1WjAlMSMwIQYDVQQDExpJbmR1c3RyaWFsQ29tbVNkayBGVFAgVGVzdDCCASIw" +
            "DQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAN8gQ3OnRlyPhUbujtNreHkxTJGCAiJ8" +
            "9lhvwtvRhXXa2v1/kpnfPphn2f5+YFw73nhyzXreDqRr6xA55USUwSNqDMv7Eo+Ujf6" +
            "FJ1LDQmq4+Sr7MwZXar89S9ogTG4CqFBVDvwt68B/SMb9IkoPX8W1mY1sHWNqfYdu6" +
            "HowO9DfYFhC9qm/d4KK6LvJiLHWJtzHm3ZOmqBj8HOscAP6cy76qVLNA7DmEbPXMme" +
            "LwIk32EpW6XgVPzKwN3IRBpkkxNov/Kq2kz6xKrdmeFZL6p6QiRQFC96r3NDZQvPi/s" +
            "1zuoPSwsx4N59KQ4ffQsqOOMA7d3FqKs0WaW36wWTsUOECAwEAATANBgkqhkiG9w0B" +
            "AQsFAAOCAQEAUWWpuMhse1Q+TyNNqOipgNbs7G/TwY/SO5zNf6Oih2e/+igGKElaVVO" +
            "81aYXztGXjusfKvmJcpEHLAkr3ujn29SkOGI4T64+EX9TvdHYWKpcw4VFtl97uFEpmQI" +
            "VKgfMfOL5hlJtyy/eV8u54Ai/TZ7rEDK/0IY+Pbhyh4tl6tU2Gsl3o1NnoUjHyEwZCT" +
            "H+k7Km655+8IUj8lgBtYX3VOfWJvBux1sX/vZa0GH8U0NSM8qbPgycSnaKXnrNxcZU" +
            "C9bV9jBqfHZOyFDaZF/jwgAV+FE4GKyAbLsoDJlnITvnqUQL6FIuMO4qxXLj2FvS+g" +
            "9TNsAChK1/RuEUjn3G6w==";

        [Test]
        public void DefaultsRequireExplicitFtpsAndPassiveDataConnections()
        {
            var options = CreateValidOptions();

            Assert.Multiple(() =>
            {
                Assert.That(options.SecurityMode, Is.EqualTo(FtpSecurityMode.ExplicitTls));
                Assert.That(options.DataConnectionMode, Is.EqualTo(FtpDataConnectionMode.Passive));
                Assert.That(options.AllowInsecureFtp, Is.False);
                Assert.That(options.Port, Is.EqualTo(21));
                Assert.That(options.ValidateCertificateRevocation, Is.True);
                Assert.That(options.TrustedCertificateThumbprint, Is.Null);
            });

            using (var client = new FtpFileClient(options))
            {
                Assert.That(client.State, Is.EqualTo(FtpConnectionState.Disconnected));
                Assert.That(client.IsConnected, Is.False);
            }
        }

        [Test]
        public void PlainFtpRequiresExplicitInsecureOptIn()
        {
            var options = CreateValidOptions();
            options.SecurityMode = FtpSecurityMode.Plain;

            var exception = Assert.Throws<InvalidOperationException>(() => new FtpFileClient(options));
            Assert.That(exception.Message, Does.Contain("AllowInsecureFtp"));

            options.AllowInsecureFtp = true;
            using (var client = new FtpFileClient(options))
                Assert.That(client.State, Is.EqualTo(FtpConnectionState.Disconnected));
        }

        [Test]
        public void ValidSystemChainCannotBypassWrongConfiguredPin()
        {
            using (var certificate = CreateTestCertificate())
            {
                var wrongPin = new string('0', 40);

                Assert.That(
                    FtpCertificateValidator.IsAccepted(certificate, SslPolicyErrors.None, wrongPin),
                    Is.False);
            }
        }

        [Test]
        public void CorrectSha1PinIsAcceptedEvenForPrivateCertificateChain()
        {
            using (var certificate = CreateTestCertificate())
            {
                Assert.That(
                    FtpCertificateValidator.IsAccepted(
                        certificate,
                        SslPolicyErrors.RemoteCertificateChainErrors,
                        certificate.GetCertHashString()),
                    Is.True);
            }
        }

        [Test]
        public void CorrectSha256PinIsAcceptedEvenWhenSystemNameCheckFails()
        {
            using (var certificate = CreateTestCertificate())
            using (var sha256 = SHA256.Create())
            {
                var pin = FtpCertificateValidator.ToHex(sha256.ComputeHash(certificate.GetRawCertData()));

                Assert.That(
                    FtpCertificateValidator.IsAccepted(
                        certificate,
                        SslPolicyErrors.RemoteCertificateNameMismatch,
                        pin),
                    Is.True);
            }
        }

        [Test]
        public void NoPinAcceptsCertificateWhenSystemPolicyHasNoErrors()
        {
            using (var certificate = CreateTestCertificate())
                Assert.That(FtpCertificateValidator.IsAccepted(certificate, SslPolicyErrors.None, null), Is.True);
        }

        [Test]
        public void NoPinRejectsCertificateWhenSystemPolicyReportsErrors()
        {
            using (var certificate = CreateTestCertificate())
                Assert.That(FtpCertificateValidator.IsAccepted(
                    certificate,
                    SslPolicyErrors.RemoteCertificateChainErrors,
                    null), Is.False);
        }

        [Test]
        public void NoPinStillRejectsMissingCertificate()
        {
            Assert.That(FtpCertificateValidator.IsAccepted(null, SslPolicyErrors.None, null), Is.False);
        }

        [TestCase("../secret.txt")]
        [TestCase("inbox/../secret.txt")]
        [TestCase("%2e%2e/secret.txt")]
        [TestCase("%2E%2E%2Fsecret.txt")]
        [TestCase("%252e%252e%252fsecret.txt")]
        [TestCase("%25252e%25252e%25252fsecret.txt")]
        public void TraversalPathsAreRejectedBeforeAnyNetworkWait(string unsafeRemotePath)
        {
            using (var client = new FtpFileClient(CreateValidOptions()))
            {
                var operation = client.ListDirectoryAsync(unsafeRemotePath, CancellationToken.None);

                Assert.That(operation.IsCompleted, Is.True, "Path validation must finish before connection or network I/O.");
                Assert.ThrowsAsync<ArgumentException>(async () => await operation);
                Assert.That(client.State, Is.EqualTo(FtpConnectionState.Disconnected));
                Assert.That(client.GetHealth().LastSuccessfulOperationUtc, Is.Null);
            }
        }

        [TestCase("../outside")]
        [TestCase("%2e%2e/outside")]
        [TestCase("%252e%252e%252foutside")]
        public void UnsafeRootPathIsRejectedDuringConstruction(string unsafeRoot)
        {
            var options = CreateValidOptions();
            options.RootPath = unsafeRoot;

            Assert.Throws<ArgumentException>(() => new FtpFileClient(options));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("ftp://example.test")]
        [TestCase("example.test/path")]
        [TestCase("example.test\\path")]
        public void InvalidHostIsRejected(string host)
        {
            var options = CreateValidOptions();
            options.Host = host;

            Assert.Throws<ArgumentException>(() => new FtpFileClient(options));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(65536)]
        public void InvalidPortIsRejected(int port)
        {
            var options = CreateValidOptions();
            options.Port = port;

            Assert.Throws<ArgumentOutOfRangeException>(() => new FtpFileClient(options));
        }

        [TestCase("connect")]
        [TestCase("operation")]
        [TestCase("data-connect")]
        [TestCase("data-operation")]
        public void NonPositiveTimeoutIsRejected(string timeoutName)
        {
            var options = CreateValidOptions();
            switch (timeoutName)
            {
                case "connect": options.ConnectTimeoutMilliseconds = 0; break;
                case "operation": options.OperationTimeoutMilliseconds = 0; break;
                case "data-connect": options.DataConnectTimeoutMilliseconds = 0; break;
                case "data-operation": options.DataOperationTimeoutMilliseconds = 0; break;
                default: throw new ArgumentOutOfRangeException(nameof(timeoutName));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => new FtpFileClient(options));
        }

        [TestCase("not-a-thumbprint")]
        [TestCase("AA:BB:CC")]
        [TestCase("000000000000000000000000000000000000000G")]
        public void InvalidCertificateThumbprintIsRejected(string thumbprint)
        {
            var options = CreateValidOptions();
            options.TrustedCertificateThumbprint = thumbprint;

            Assert.Throws<ArgumentException>(() => new FtpFileClient(options));
        }

        [TestCase("")]
        [TestCase("../uploading")]
        [TestCase("/uploading")]
        [TestCase("\\uploading")]
        public void InvalidAtomicUploadSuffixIsRejected(string suffix)
        {
            var options = CreateValidOptions();
            options.AtomicUploadTemporarySuffix = suffix;

            Assert.Throws<ArgumentException>(() => new FtpFileClient(options));
        }

        [Test]
        public void NegativeRetryCountAndUnknownEnumsAreRejected()
        {
            var retryOptions = CreateValidOptions();
            retryOptions.RetryAttempts = -1;
            Assert.Throws<ArgumentOutOfRangeException>(() => new FtpFileClient(retryOptions));

            var securityOptions = CreateValidOptions();
            securityOptions.SecurityMode = (FtpSecurityMode)999;
            Assert.Throws<ArgumentOutOfRangeException>(() => new FtpFileClient(securityOptions));

            var dataOptions = CreateValidOptions();
            dataOptions.DataConnectionMode = (FtpDataConnectionMode)999;
            Assert.Throws<ArgumentOutOfRangeException>(() => new FtpFileClient(dataOptions));
        }

        [Test]
        [Category("Integration")]
        public async Task RealFtpOrFtpsServerCanConnectProbeAndListWhenConfigured()
        {
            var host = Environment.GetEnvironmentVariable("INDUSTRIAL_FTP_TEST_HOST");
            if (string.IsNullOrWhiteSpace(host))
                Assert.Ignore("Set INDUSTRIAL_FTP_TEST_HOST to enable the real FTP/FTPS integration test.");

            var security = ReadEnum("INDUSTRIAL_FTP_TEST_SECURITY", FtpSecurityMode.ExplicitTls);
            var defaultPort = security == FtpSecurityMode.ImplicitTls ? 990 : 21;
            var options = new FtpClientOptions
            {
                Host = host,
                Port = ReadInt("INDUSTRIAL_FTP_TEST_PORT", defaultPort),
                Username = Environment.GetEnvironmentVariable("INDUSTRIAL_FTP_TEST_USERNAME") ?? "anonymous",
                Password = Environment.GetEnvironmentVariable("INDUSTRIAL_FTP_TEST_PASSWORD") ?? "anonymous@",
                RootPath = Environment.GetEnvironmentVariable("INDUSTRIAL_FTP_TEST_ROOT") ?? "/",
                SecurityMode = security,
                AllowInsecureFtp = ReadBool("INDUSTRIAL_FTP_TEST_ALLOW_INSECURE"),
                TrustedCertificateThumbprint = Environment.GetEnvironmentVariable("INDUSTRIAL_FTP_TEST_CERT_THUMBPRINT"),
            };

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (var client = new FtpFileClient(options))
            {
                await client.ConnectAsync(timeout.Token);
                try
                {
                    var health = await client.CheckHealthAsync(timeout.Token);
                    var capabilities = await client.ProbeCapabilitiesAsync(timeout.Token);
                    var listing = await client.ListDirectoryAsync("/", timeout.Token);

                    Assert.That(health.IsConnected, Is.True);
                    Assert.That(capabilities.IsAvailable, Is.True);
                    Assert.That(listing, Is.Not.Null);
                    if (security != FtpSecurityMode.Plain)
                        Assert.That(health.IsEncrypted, Is.True);
                }
                finally
                {
                    await client.DisconnectAsync(CancellationToken.None);
                }
            }
        }

        private static FtpClientOptions CreateValidOptions()
        {
            return new FtpClientOptions
            {
                Host = "network-must-not-be-contacted.invalid",
                RootPath = "/plant/inbox",
            };
        }

        private static X509Certificate2 CreateTestCertificate()
        {
            return new X509Certificate2(Convert.FromBase64String(TestCertificateBase64));
        }

        private static T ReadEnum<T>(string name, T fallback) where T : struct
        {
            var value = Environment.GetEnvironmentVariable(name);
            T parsed;
            return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out parsed) ? parsed : fallback;
        }

        private static int ReadInt(string name, int fallback)
        {
            int parsed;
            return int.TryParse(Environment.GetEnvironmentVariable(name), out parsed) ? parsed : fallback;
        }

        private static bool ReadBool(string name)
        {
            bool parsed;
            return bool.TryParse(Environment.GetEnvironmentVariable(name), out parsed) && parsed;
        }
    }
}
