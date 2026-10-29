using System;
using System.IO;
using System.IO.Compression;

namespace MotionDesk.Services
{
    /// <summary>
    /// Extracts a zip archive from an UNTRUSTED source (downloaded playlist, imported backup) into <paramref name="destDir"/>: every entry is resolved
    /// and must stay inside the destination (no "..", no absolute or drive paths), entry count and the real uncompressed size are limited (zip bombs),
    /// nothing outside destDir is ever written. Throws InvalidDataException on the first violation.
    /// </summary>
    internal static class SafeZip
    {
        public static void Extract(string archivePath, string destDir, int maxEntries = 20000, long maxTotalBytes = 16L * 1024 * 1024 * 1024, long maxEntryBytes = 8L * 1024 * 1024 * 1024)
        {
            string root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);
            using var zip = ZipFile.OpenRead(archivePath);
            if (zip.Entries.Count > maxEntries) throw new InvalidDataException("too many entries");
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name.Length == 0 || name.IndexOf('\0') >= 0 || Path.IsPathRooted(name) || name.Contains(':')) throw new InvalidDataException("unsafe entry name: " + name);
                string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("entry escapes the destination: " + name);
                if (name.EndsWith('/') || name.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                if (entry.Length > maxEntryBytes) throw new InvalidDataException("entry too large: " + name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long entryBytes = 0; int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    entryBytes += n; total += n;                                    // the real size counts, not the (forgeable) header
                    if (entryBytes > maxEntryBytes || total > maxTotalBytes) throw new InvalidDataException("archive too large when extracted");
                    output.Write(buffer, 0, n);
                }
            }
        }
    }
}
