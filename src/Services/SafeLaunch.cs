using System;
using System.IO;
using System.Linq;

namespace MotionDesk.Services
{
    /// <summary>
    /// Checks a path BEFORE Opsis hands it to the shell (pinned dock items, application wallpaper, command-palette apps): it must be an existing
    /// LOCAL file or folder (drive-letter path, no UNC share, no URL, no device path) and must not be a script / auto-run type that Windows would
    /// execute through a script host. Whatever passes still runs with the user's own rights, exactly like double-clicking it in Explorer.
    /// </summary>
    internal static class SafeLaunch
    {
        private static readonly string[] Blocked =
        {
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".scr", ".pif", ".com", ".jar", ".reg",
            ".cpl", ".application", ".gadget", ".msh", ".msh1", ".msh2", ".inf", ".sct", ".chm",
        };

        /// <summary>Returns null when the path may be launched, otherwise a short English reason (for logs).</summary>
        public static string? Reject(string? path, bool requireExe = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return "empty path";
            path = path.Trim().Trim('"');
            if (path.Contains("://", StringComparison.Ordinal) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return "network or URL path";
            if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal)) return "device path";
            if (path.IndexOf('\0') >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return "invalid characters";
            string full;
            try { full = Path.GetFullPath(path); } catch (Exception) { return "invalid path"; }
            if (!Path.IsPathFullyQualified(path)) return "relative path";
            if (full.Length < 3 || full[1] != ':') return "not a drive-letter path";
            try { if (new DriveInfo(full[..1]).DriveType == DriveType.Network) return "network drive"; } catch (Exception) { return "unknown drive"; }
            bool isDir = Directory.Exists(full);
            if (!isDir && !File.Exists(full)) return "does not exist";
            if (isDir) return requireExe ? "folder is not an executable" : null;
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (Blocked.Contains(ext)) return "script or auto-run file type";
            if (requireExe && ext != ".exe") return "not an .exe";
            return null;
        }

        public static bool IsAllowed(string? path, bool requireExe = false) => Reject(path, requireExe) == null;
    }
}
