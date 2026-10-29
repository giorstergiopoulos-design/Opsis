using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Desktop_Fences
{
    // Opsis: when an icon of the Windows desktop is dropped into a container it leaves the desktop (the container shows it instead), like in
    // the classic icon-fence tools. Nothing is moved or deleted: the desktop original only gets the Hidden attribute, and gets it back as soon
    // as the item is removed from every container (or the container is deleted). The map shortcut -> original lives in the profile folder.
    internal static class DesktopOriginals
    {
        private static string MapPath => Path.Combine(ProfileManager.CurrentProfileDir, "hidden_originals.json");

        private static Dictionary<string, string> Load()
        {
            try { if (File.Exists(MapPath)) return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(MapPath)) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
            catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void Save(Dictionary<string, string> map)
        {
            try { File.WriteAllText(MapPath, JsonConvert.SerializeObject(map, Formatting.Indented)); } catch { }
        }

        private static bool IsDirectlyOnDesktop(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                return string.Equals(dir, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dir, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void OnDropped(string shortcutFilename, string originalPath)
        {
            if (!SettingsManager.HideDesktopOriginalsOnDrop || !IsDirectlyOnDesktop(originalPath)) return;
            try
            {
                var attrs = File.GetAttributes(originalPath);
                if ((attrs & FileAttributes.Hidden) != 0) return;                  // already hidden by the user: leave it alone
                File.SetAttributes(originalPath, attrs | FileAttributes.Hidden);
                var map = Load();
                map[Path.GetFullPath(shortcutFilename)] = originalPath;
                Save(map);
                NotifyShell();
            }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling, "Could not hide desktop original: " + ex.Message); }
        }

        /// <summary>Called on every save: originals whose shortcut is no longer in any container are shown on the desktop again.</summary>
        public static void Reconcile(IEnumerable<JObject> fences)
        {
            try
            {
                var map = Load();
                if (map.Count == 0) return;
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in fences)
                {
                    foreach (var arr in new[] { f["Items"] as JArray }.Concat((f["Tabs"] as JArray ?? new JArray()).Select(t => t["Items"] as JArray)))
                        if (arr != null) foreach (var i in arr) { var fn = i["Filename"]?.ToString(); if (!string.IsNullOrEmpty(fn)) { try { present.Add(Path.GetFullPath(fn)); } catch { } } }
                }
                bool changed = false;
                foreach (var kv in map.ToList())
                {
                    if (present.Contains(kv.Key)) continue;
                    try
                    {
                        if (File.Exists(kv.Value) || Directory.Exists(kv.Value))
                            File.SetAttributes(kv.Value, File.GetAttributes(kv.Value) & ~FileAttributes.Hidden);
                    }
                    catch { }
                    map.Remove(kv.Key); changed = true;
                }
                if (changed) { Save(map); NotifyShell(); }
            }
            catch { }
        }

        /// <summary>Hides again the desktop originals of the items that are in containers (engine start).</summary>
        public static void HideAll()
        {
            try
            {
                foreach (var kv in Load())
                    if ((File.Exists(kv.Value) || Directory.Exists(kv.Value)) && File.Exists(kv.Key))
                    {
                        var a = File.GetAttributes(kv.Value);
                        if ((a & FileAttributes.Hidden) == 0) File.SetAttributes(kv.Value, a | FileAttributes.Hidden);
                    }
                NotifyShell();
            }
            catch { }
        }

        /// <summary>Shows every hidden original (engine exit / uninstall): the desktop looks as it did before the containers.</summary>
        public static void ShowAll()
        {
            try
            {
                foreach (var kv in Load())
                    if (File.Exists(kv.Value) || Directory.Exists(kv.Value))
                        File.SetAttributes(kv.Value, File.GetAttributes(kv.Value) & ~FileAttributes.Hidden);
                NotifyShell();
            }
            catch { }
        }

        /// <summary>--unhide-desktop: runs without starting the engine (used by the uninstaller).</summary>
        public static void UnhideAllStandalone()
        {
            try
            {
                string profile = "Default";
                try { profile = JObject.Parse(File.ReadAllText(Path.Combine(DataRoot.Dir, "ProfileOptions.json")))["ActiveProfile"]?.ToString() ?? "Default"; } catch { }
                string map = Path.Combine(DataRoot.Dir, "Profiles", profile, "hidden_originals.json");
                if (!File.Exists(map)) return;
                var d = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(map)) ?? new Dictionary<string, string>();
                foreach (var v in d.Values)
                    if (File.Exists(v) || Directory.Exists(v)) File.SetAttributes(v, File.GetAttributes(v) & ~FileAttributes.Hidden);
                NotifyShell();
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
        private static void NotifyShell() { try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { } }   // SHCNE_ASSOCCHANGED: desktop refreshes
    }
}
