using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Services
{
    /// <summary>
    /// Trust model for plugins. A plugin folder under the install directory of Opsis is trusted only when that directory is itself protected
    /// (Program Files: writable by administrators only). Plugin folders anywhere else - %AppData%\Opsis\plugins, registry paths, a per-user install -
    /// are writable by the user (or by any program running as the user), so a dropped DLL would be code execution inside Opsis: such a folder is loaded
    /// only after the user approved its exact content (SHA-256 over every .dll / .deps.json in the folder). The approval is stored in plugin-trust.json;
    /// any change of the content invalidates it.
    /// </summary>
    internal static class PluginTrust
    {
        /// <summary>True when <paramref name="dir"/> is inside Program Files (64/32-bit): only administrators can write there.</summary>
        public static bool IsUnderProtectedInstall(string dir, IEnumerable<string>? protectedRoots = null)
        {
            string full = Normalize(dir);
            var roots = protectedRoots ?? new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles") ?? "",
                Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "",
                Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
            };
            foreach (var r in roots)
            {
                if (string.IsNullOrWhiteSpace(r)) continue;
                string root = Normalize(r);
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string Normalize(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>SHA-256 (hex) over the relative names and contents of all .dll and .deps.json files below the folder, in a stable order.</summary>
        public static string HashFolder(string folder)
        {
            using var total = SHA256.Create();
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                using var sha = SHA256.Create();
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
                string h = Convert.ToHexString(sha.ComputeHash(fs));
                sb.Append(Path.GetRelativePath(folder, f).ToLowerInvariant()).Append('|').Append(h).Append('\n');
            }
            return Convert.ToHexString(total.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public sealed class Entry
        {
            public string Name { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public DateTime ApprovedUtc { get; set; }
        }

        public sealed class Store
        {
            private readonly string _path;
            private readonly List<Entry> _entries;

            public Store(string path)
            {
                _path = path;
                try { _entries = File.Exists(path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new() : new(); }
                catch (Exception ex) when (ex is JsonException or IOException) { _entries = new(); }
            }

            public bool IsApproved(string name, string sha256) =>
                _entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

            public void Approve(string name, string sha256)
            {
                _entries.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));      // one approved content per plugin name
                _entries.Add(new Entry { Name = name, Sha256 = sha256.ToUpperInvariant(), ApprovedUtc = DateTime.UtcNow });
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    public sealed record PluginTrustRequest(string Name, string Folder, string Sha256);
}
