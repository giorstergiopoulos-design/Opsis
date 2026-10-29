using System;
using System.IO;
using System.IO.Compression;

namespace OpsisContainers
{
    // Imports of DeskContainers exports/backups come from files the user may have received from anyone: extract with full path validation and size limits.
    internal static class SafeZip
    {
        public static void Extract(string archivePath, string destDir, int maxEntries = 5000, long maxTotalBytes = 1L * 1024 * 1024 * 1024)
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
                if (name.EndsWith("/") || name.EndsWith("\\")) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using var input = entry.Open();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += n;
                    if (total > maxTotalBytes) throw new InvalidDataException("archive too large when extracted");
                    output.Write(buffer, 0, n);
                }
            }
        }
    }
}
