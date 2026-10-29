using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpsisContainers
{
    // Opsis 2.2.0: the engine's "fence" vocabulary became "container" everywhere (code, UI, files, persisted keys, registry). This class carries
    // data written by earlier builds across: it renames the data files and the JSON keys that contained the word "Fence", once, before the
    // data is read; it is also applied to every JSON text read from a backup or an import (old exports keep working).
    internal static class LegacyNames
    {
        private static readonly Regex Key = new Regex("\"([A-Za-z_]*Fence[A-Za-z_]*)\"(\\s*:)", RegexOptions.Compiled);

        /// <summary>Renames every JSON key containing "Fence"/"Fences" to the "Container"/"Containers" spelling (values are never touched).</summary>
        public static string MigrateKeys(string json)
        {
            if (string.IsNullOrEmpty(json) || json.IndexOf("Fence", StringComparison.Ordinal) < 0) return json;
            return Key.Replace(json, m => "\"" + m.Groups[1].Value.Replace("Fences", "Containers").Replace("Fence", "Container") + "\"" + m.Groups[2].Value);
        }

        /// <summary>Migrates the data folder: fences.json -> containers.json, and the keys in every profile json file. Safe to run on every start.</summary>
        public static void MigrateFolder(string dataRoot)
        {
            try
            {
                if (!Directory.Exists(dataRoot)) return;
                foreach (var file in Directory.GetFiles(dataRoot, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        string name = Path.GetFileName(file);
                        string target = file;
                        if (name.Equals("fences.json", StringComparison.OrdinalIgnoreCase))
                        {
                            target = Path.Combine(Path.GetDirectoryName(file)!, "containers.json");
                            if (File.Exists(target)) continue;               // already migrated: never overwrite the new file
                            File.Move(file, target);
                        }
                        string text = File.ReadAllText(target), migrated = MigrateKeys(text);
                        if (name.Equals("containers.json", StringComparison.OrdinalIgnoreCase)) migrated = IconSizeToDesktopOnce(target, migrated);
                        if (migrated != text) File.WriteAllText(target, migrated);
                    }
                    catch (Exception) { /* one unreadable file must not block start-up */ }
                }
            }
            catch (Exception) { }
        }

        /// <summary>2.2.1: containers used a fixed "Medium" (40 px) icon, smaller than the desktop icons. Once per file (marker next to it), "Medium" becomes "Desktop"
        /// (= the real desktop icon size); any later choice of the user is kept.</summary>
        private static string IconSizeToDesktopOnce(string file, string json)
        {
            try
            {
                string marker = file + ".iconsize-desktop";
                if (File.Exists(marker)) return json;
                File.WriteAllText(marker, "2.2.1");
                return json.Replace("\"IconSize\": \"Medium\"", "\"IconSize\": \"Desktop\"").Replace("\"IconSize\":\"Medium\"", "\"IconSize\":\"Desktop\"");
            }
            catch (Exception) { return json; }
        }

        /// <summary>Removes the desktop-menu verb registered under the old name by earlier Opsis builds (only if it points at an Opsis exe).</summary>
        public static void CleanRegistry()
        {
            try
            {
                const string verb = @"Software\Classes\DesktopBackground\Shell\DesktopFences";
                using var cmd = Registry.CurrentUser.OpenSubKey(verb + @"\command");
                string value = cmd?.GetValue("") as string ?? "";
                if (value.IndexOf("Opsis", StringComparison.OrdinalIgnoreCase) >= 0) Registry.CurrentUser.DeleteSubKeyTree(verb, false);
            }
            catch (Exception) { }
        }
    }
}
