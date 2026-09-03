using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace InduLink.FileTransfer.Ftp
{
    /// <summary>
    /// Applies one unambiguous FTPS trust policy: a configured pin is an alternative trust anchor and must
    /// always match; without a pin, the operating-system certificate policy is authoritative.
    /// </summary>
    internal static class FtpCertificateValidator
    {
        internal static bool IsAccepted(
            X509Certificate certificate,
            SslPolicyErrors policyErrors,
            string trustedCertificateThumbprint)
        {
            if (!string.IsNullOrWhiteSpace(trustedCertificateThumbprint))
            {
                var expected = NormalizeThumbprint(trustedCertificateThumbprint);
                if (certificate == null || string.IsNullOrEmpty(expected)) return false;

                string actual;
                if (expected.Length == 64)
                {
                    using (var sha256 = SHA256.Create())
                        actual = ToHex(sha256.ComputeHash(certificate.GetRawCertData()));
                }
                else if (expected.Length == 40)
                {
                    actual = NormalizeThumbprint(certificate.GetCertHashString());
                }
                else
                {
                    return false;
                }

                // A configured pin deliberately acts as the trust basis for private/self-signed FTPS servers.
                // Consequently, a matching pin is sufficient even when the system chain reports policy errors.
                return !string.IsNullOrEmpty(actual) && ConstantTimeEquals(expected, actual);
            }

            return certificate != null && policyErrors == SslPolicyErrors.None;
        }

        internal static string NormalizeThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint)) return string.Empty;
            var result = new StringBuilder(thumbprint.Length);
            foreach (var value in thumbprint)
            {
                if (char.IsWhiteSpace(value) || value == ':' || value == '-') continue;
                if (!Uri.IsHexDigit(value)) return null;
                result.Append(char.ToUpperInvariant(value));
            }
            return result.ToString();
        }

        internal static string ToHex(byte[] value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return BitConverter.ToString(value).Replace("-", string.Empty);
        }

        private static bool ConstantTimeEquals(string left, string right)
        {
            var difference = left.Length ^ right.Length;
            var length = Math.Max(left.Length, right.Length);
            for (var index = 0; index < length; index++)
            {
                var a = index < left.Length ? left[index] : '\0';
                var b = index < right.Length ? right[index] : '\0';
                difference |= a ^ b;
            }
            return difference == 0;
        }
    }
}
