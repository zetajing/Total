using System;
using System.Collections.Generic;

namespace IndustrialCommSdk.FileTransfer.Ftp
{
    /// <summary>Canonicalizes all caller paths beneath one immutable FTP account root.</summary>
    internal sealed class FtpRemotePath
    {
        private readonly string _root;

        public FtpRemotePath(string rootPath)
        {
            _root = NormalizeAbsoluteRoot(rootPath);
        }

        public string Root { get { return _root; } }

        public string Resolve(string relativePath, bool allowRoot)
        {
            var segments = ParseSegments(relativePath, true);
            if (!allowRoot && segments.Count == 0)
                throw new ArgumentException("A remote file or directory path is required.", nameof(relativePath));

            var suffix = string.Join("/", segments);
            if (_root == "/") return suffix.Length == 0 ? "/" : "/" + suffix;
            return suffix.Length == 0 ? _root : _root + "/" + suffix;
        }

        public string ToRelative(string parentRelativePath, string itemName)
        {
            var parent = ParseSegments(parentRelativePath, true);
            var name = ParseSegments(itemName, false);
            parent.AddRange(name);
            return parent.Count == 0 ? "/" : "/" + string.Join("/", parent);
        }

        public string ToRelative(string relativePath)
        {
            var segments = ParseSegments(relativePath, true);
            return segments.Count == 0 ? "/" : "/" + string.Join("/", segments);
        }

        private static string NormalizeAbsoluteRoot(string path)
        {
            var segments = ParseSegments(path, true);
            return segments.Count == 0 ? "/" : "/" + string.Join("/", segments);
        }

        private static List<string> ParseSegments(string path, bool allowEmpty)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                if (allowEmpty) return new List<string>();
                throw new ArgumentException("Remote path segment is required.", nameof(path));
            }

            path = path.Trim();
            if (path.IndexOf('\\') >= 0)
                throw new ArgumentException("Remote FTP paths must use '/' and cannot contain backslashes.", nameof(path));

            foreach (var value in path)
                if (char.IsControl(value))
                    throw new ArgumentException("Remote FTP paths cannot contain control characters.", nameof(path));

            var result = new List<string>();
            foreach (var segment in path.Split('/'))
            {
                if (segment.Length == 0) continue;
                RejectTraversal(segment, path);
                result.Add(segment);
            }

            if (!allowEmpty && result.Count != 1)
                throw new ArgumentException("A directory item name must contain exactly one path segment.", nameof(path));
            return result;
        }

        private static void RejectTraversal(string segment, string originalPath)
        {
            var candidate = segment;
            for (var index = 0; index < 3; index++)
            {
                if (candidate == "." || candidate == "..")
                    throw new ArgumentException("Remote FTP paths cannot contain '.' or '..' traversal segments.", nameof(originalPath));

                string decoded;
                try { decoded = Uri.UnescapeDataString(candidate); }
                catch (UriFormatException ex)
                {
                    throw new ArgumentException("Remote FTP path contains invalid escaping.", nameof(originalPath), ex);
                }
                if (decoded.IndexOf('/') >= 0 || decoded.IndexOf('\\') >= 0)
                    throw new ArgumentException("Remote FTP paths cannot contain encoded path separators.", nameof(originalPath));
                if (string.Equals(decoded, candidate, StringComparison.Ordinal)) break;
                candidate = decoded;
            }
            if (candidate == "." || candidate == "..")
                throw new ArgumentException("Remote FTP paths cannot contain encoded traversal segments.", nameof(originalPath));
        }
    }
}
