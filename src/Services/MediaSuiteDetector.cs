using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace MotionDesk.Services
{
    /// <summary>Is Opsis Media Suite (the former Waveframe) installed? "Audio management" is offered as an extra feature only then. Looks at the usual folders and the uninstall registry.</summary>
    public static class MediaSuiteDetector
    {
        public static readonly string[] DisplayNames = { "Opsis Media Suite", "Waveframe" };

        public static IEnumerable<string> CandidatePaths()
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
            string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? "";
            string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "";
            foreach (var root in new[] { Path.Combine(local, "Programs"), pf, pf86 })
            {
                if (root.Length == 0) continue;
                foreach (var name in new[] { "Opsis Media Suite", "waveframe", "Waveframe" })
                    yield return Path.Combine(root, name);
            }
        }

        /// <summary>Pure decision (testable): any candidate folder exists with an .exe inside, or any registry display name matches.</summary>
        public static bool Decide(IEnumerable<string> existingFolders, IEnumerable<string> uninstallDisplayNames) =>
            existingFolders.Any(f => { try { return Directory.Exists(f) && Directory.EnumerateFiles(f, "*.exe", SearchOption.TopDirectoryOnly).Any(); } catch (Exception) { return false; } })
            || uninstallDisplayNames.Any(n => DisplayNames.Any(d => n != null && n.StartsWith(d, StringComparison.OrdinalIgnoreCase)));

        private static bool? _cached;

        public static bool IsInstalled(bool refresh = false)
        {
            if (_cached.HasValue && !refresh) return _cached.Value;
            var names = new List<string>();
            foreach (var (hive, view, sub) in new[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            })
            {
                try
                {
                    using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(sub);
                    if (k == null) continue;
                    foreach (var n in k.GetSubKeyNames()) { using var s = k.OpenSubKey(n); if (s?.GetValue("DisplayName") is string dn) names.Add(dn); }
                }
                catch (Exception) { }
            }
            return (_cached = Decide(CandidatePaths(), names)).Value;
        }
    }
}
