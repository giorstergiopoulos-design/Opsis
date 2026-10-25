using System.IO.Compression;

namespace MotionDesk.Services;

/// <summary>
/// Wallpaper packages: a .zip with an index.html (and optionally opsis-wallpaper.json, scripts, images, videos) that can be shared and imported.
/// Import extracts into %AppData%\Opsis\wallpapers\&lt;name&gt; with checks against path escapes and absurd sizes.
/// </summary>
public static class WallpaperPackages
{
    public const long MaxUncompressedBytes = 400L * 1024 * 1024;
    public const int MaxEntries = 4000;

    public static string Root => Path.Combine(AppPaths.AppDataFolder, "wallpapers");

    /// <summary>Extracts the package and returns the full path of its index.html.</summary>
    public static string Import(string zipPath, string? destinationRoot = null)
    {
        destinationRoot ??= Root;
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("The package has too many files.");
        long total = 0;
        foreach (var e in zip.Entries) total += e.Length;
        if (total > MaxUncompressedBytes) throw new InvalidDataException("The package is too large.");

        // index.html at the root, or inside a single top-level folder (the way most zip tools pack a folder)
        var index = zip.Entries.Where(e => e.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                               .OrderBy(e => e.FullName.Count(c => c == '/')).FirstOrDefault()
                    ?? throw new InvalidDataException("The package has no index.html.");
        string prefix = index.FullName.Contains('/') ? index.FullName[..(index.FullName.LastIndexOf('/') + 1)] : "";

        string name = SafeName(Path.GetFileNameWithoutExtension(zipPath));
        string target = UniqueFolder(destinationRoot, name);
        Directory.CreateDirectory(target);
        string targetFull = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        try
        {
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string rel = e.FullName[prefix.Length..];
                if (rel.Length == 0) continue;
                string dest = Path.GetFullPath(Path.Combine(target, rel));
                if (!dest.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The package contains an unsafe path.");
                if (e.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest, overwrite: true);
            }
        }
        catch { try { Directory.Delete(target, true); } catch (Exception) { } throw; }
        return Path.Combine(target, "index.html");
    }

    /// <summary>Packs the folder of a local wallpaper page into a .zip.</summary>
    public static void Export(string indexHtml, string zipPath)
    {
        string dir = Path.GetDirectoryName(indexHtml) ?? throw new ArgumentException("No folder", nameof(indexHtml));
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(dir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    private static string SafeName(string n)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        n = n.Trim().Trim('.');
        return n.Length == 0 ? "wallpaper" : n.Length > 60 ? n[..60] : n;
    }

    private static string UniqueFolder(string root, string name)
    {
        string p = Path.Combine(root, name);
        for (int i = 2; Directory.Exists(p); i++) p = Path.Combine(root, $"{name} ({i})");
        return p;
    }
}
