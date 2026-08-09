using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace IndustrialCommSdk.Runtime.Security
{
    /// <summary>保存和读取由配置文件通过名称引用的敏感值。</summary>
    public interface ISecretStore
    {
        void Set(string name, string value);
        string Get(string name);
        bool TryGet(string name, out string value);
        bool Remove(string name);
    }

    /// <summary>使用 Windows DPAPI CurrentUser 保护每个敏感值的本地文件存储。</summary>
    public sealed class DpapiSecretStore : ISecretStore, IDisposable
    {
        private static readonly byte[] FileHeader = { (byte)'I', (byte)'C', (byte)'S', 1 };
        private readonly string _directory;
        private readonly byte[] _optionalEntropy;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private int _disposed;

        public DpapiSecretStore(string directory, byte[] optionalEntropy = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Secret directory cannot be empty.", nameof(directory));
            _directory = Path.GetFullPath(directory);
            _optionalEntropy = optionalEntropy == null ? null : (byte[])optionalEntropy.Clone();
        }

        public void Set(string name, string value)
        {
            ThrowIfDisposed();
            ValidateName(name);
            if (value == null) throw new ArgumentNullException(nameof(value));

            _gate.Wait();
            try
            {
                Directory.CreateDirectory(_directory);
                var plain = Encoding.UTF8.GetBytes(value);
                byte[] protectedBytes = null;
                try
                {
                    protectedBytes = ProtectedData.Protect(plain, _optionalEntropy, DataProtectionScope.CurrentUser);
                    var payload = new byte[FileHeader.Length + protectedBytes.Length];
                    Buffer.BlockCopy(FileHeader, 0, payload, 0, FileHeader.Length);
                    Buffer.BlockCopy(protectedBytes, 0, payload, FileHeader.Length, protectedBytes.Length);
                    WriteAtomically(GetPath(name), payload);
                }
                finally
                {
                    Array.Clear(plain, 0, plain.Length);
                    if (protectedBytes != null) Array.Clear(protectedBytes, 0, protectedBytes.Length);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public string Get(string name)
        {
            string value;
            if (TryGet(name, out value)) return value;
            throw new KeyNotFoundException("Secret was not found: " + name);
        }

        public bool TryGet(string name, out string value)
        {
            ThrowIfDisposed();
            ValidateName(name);
            value = null;

            _gate.Wait();
            try
            {
                var path = GetPath(name);
                if (!File.Exists(path)) return false;
                var payload = File.ReadAllBytes(path);
                ValidatePayload(payload);
                var encrypted = new byte[payload.Length - FileHeader.Length];
                Buffer.BlockCopy(payload, FileHeader.Length, encrypted, 0, encrypted.Length);
                byte[] plain = null;
                try
                {
                    plain = ProtectedData.Unprotect(encrypted, _optionalEntropy, DataProtectionScope.CurrentUser);
                    value = Encoding.UTF8.GetString(plain);
                    return true;
                }
                catch (CryptographicException ex)
                {
                    throw new InvalidDataException("The secret cannot be decrypted for the current Windows user.", ex);
                }
                finally
                {
                    Array.Clear(payload, 0, payload.Length);
                    Array.Clear(encrypted, 0, encrypted.Length);
                    if (plain != null) Array.Clear(plain, 0, plain.Length);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public bool Remove(string name)
        {
            ThrowIfDisposed();
            ValidateName(name);
            _gate.Wait();
            try
            {
                var path = GetPath(name);
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _gate.Dispose();
            if (_optionalEntropy != null) Array.Clear(_optionalEntropy, 0, _optionalEntropy.Length);
        }

        private string GetPath(string name)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(name.Trim().ToUpperInvariant()));
                var fileName = string.Concat(bytes.Select(value => value.ToString("x2"))) + ".secret";
                return Path.Combine(_directory, fileName);
            }
        }

        private static void WriteAtomically(string path, byte[] payload)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var backup = path + ".bak";
            try
            {
                File.WriteAllBytes(temporary, payload);
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, backup, true);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void ValidatePayload(byte[] payload)
        {
            if (payload == null || payload.Length <= FileHeader.Length)
                throw new InvalidDataException("The secret file is empty or truncated.");
            for (var i = 0; i < FileHeader.Length; i++)
            {
                if (payload[i] != FileHeader[i]) throw new InvalidDataException("The secret file format is invalid.");
            }
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Secret name cannot be empty.", nameof(name));
            if (name.Length > 200) throw new ArgumentOutOfRangeException(nameof(name), "Secret name is too long.");
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(DpapiSecretStore));
        }
    }

    /// <summary>为日志输出提供统一的敏感字段判断与文本替换。</summary>
    public static class SecretRedactor
    {
        private static readonly string[] SensitiveNames =
        {
            "authorization", "api-key", "apikey", "password", "passwd", "secret", "token"
        };

        public static bool IsSensitiveName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return SensitiveNames.Any(value => name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string Redact(string text, IEnumerable<string> secrets)
        {
            if (text == null || secrets == null) return text;
            var result = text;
            foreach (var secret in secrets.Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal))
            {
                result = result.Replace(secret, "***");
            }
            return result;
        }
    }
}
