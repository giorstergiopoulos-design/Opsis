using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using MotionDesk.Widgets;

namespace MotionDesk.Services
{
    // One-time import of the legacy Opsis DeskContainers (AppData\deskcontainers\*_config.json) into the Fences engine's
    // fences.json, run before the engine's first start. The legacy files are only read, never changed or deleted.
    internal static class FencesMigration
    {
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

        public static string FencesJsonPath => Path.Combine(AppPaths.AppDataFolder, "fences", "Profiles", "Default", "fences.json");

        /// <summary>Imports legacy containers when the engine has no fences.json yet. Returns how many fences were written.</summary>
        public static int MigrateLegacyContainers()
        {
            try
            {
                if (File.Exists(FencesJsonPath)) return 0;
                string dir = Path.Combine(AppPaths.AppDataFolder, "deskcontainers");
                if (!Directory.Exists(dir)) return 0;
                var states = new List<DeskContainerState>();
                foreach (var f in Directory.GetFiles(dir, "*_config.json"))
                {
                    try { var s = JsonSerializer.Deserialize<DeskContainerState>(AtomicFile.ReadAllText(f)); if (s != null) states.Add(s); }
                    catch (Exception ex) when (ex is JsonException or IOException) { }
                }
                if (states.Count == 0) return 0;
                double scale = Math.Max(1.0, GetDpiForSystem() / 96.0);
                Directory.CreateDirectory(Path.GetDirectoryName(FencesJsonPath)!);
                File.WriteAllText(FencesJsonPath, Convert(states, scale));
                return states.Count;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }

        /// <summary>Builds the fences.json text (DesktopFences schema) for the given legacy container states. Pixel sizes are divided by <paramref name="dpiScale"/>.</summary>
        internal static string Convert(IReadOnlyList<DeskContainerState> states, double dpiScale)
        {
            var array = new JsonArray();
            foreach (var s in states)
            {
                bool portal = !string.IsNullOrWhiteSpace(s.PortalFolder);
                bool note = s.IsNote && !portal;
                double h = Math.Max(60, s.Height / dpiScale);
                var fence = new JsonObject
                {
                    ["Id"] = Guid.NewGuid().ToString(),
                    ["Title"] = string.IsNullOrWhiteSpace(s.Title) ? "Fence" : s.Title,
                    ["X"] = Math.Round(s.X / dpiScale, 1),
                    ["Y"] = Math.Round(s.Y / dpiScale, 1),
                    ["Width"] = Math.Round(Math.Max(120, s.Width / dpiScale), 1),
                    ["Height"] = Math.Round(h, 1),
                    ["IsLocked"] = s.IsLocked ? "true" : "false",
                    ["IsHidden"] = "false",
                    ["IsRolled"] = s.IsRolledUp ? "true" : "false",
                    ["UnrolledHeight"] = Math.Round(h, 1),
                    ["CustomColor"] = null,
                    ["FenceBorderThickness"] = 2
                };

                if (portal)
                {
                    fence["ItemsType"] = "Portal";
                    fence["Items"] = s.PortalFolder;
                    fence["TabsEnabled"] = "false"; fence["CurrentTab"] = 0; fence["Tabs"] = new JsonArray();
                }
                else if (note)
                {
                    fence["ItemsType"] = "Note";
                    fence["Items"] = new JsonArray();
                    fence["NoteContent"] = s.NoteText ?? "";
                    fence["TextColor"] = "White";
                    fence["TabsEnabled"] = "false"; fence["CurrentTab"] = 0; fence["Tabs"] = new JsonArray();
                }
                else
                {
                    fence["ItemsType"] = "Data";
                    if (s.Tabs is { Count: > 1 })
                    {
                        fence["Items"] = new JsonArray();
                        fence["TabsEnabled"] = "true";
                        fence["CurrentTab"] = Math.Clamp(s.ActiveTab, 0, s.Tabs.Count - 1);
                        var tabs = new JsonArray();
                        for (int i = 0; i < s.Tabs.Count; i++)
                            tabs.Add(new JsonObject { ["TabName"] = string.IsNullOrWhiteSpace(s.Tabs[i].Name) ? (i == 0 ? (s.Title ?? "Main") : $"Tab {i + 1}") : s.Tabs[i].Name, ["Items"] = ItemsOf(s.Tabs[i].Items) });
                        fence["Tabs"] = tabs;
                    }
                    else
                    {
                        fence["Items"] = ItemsOf(s.Items ?? (s.Paths ?? Array.Empty<string>()).Select(p => new ContainerItem { Path = p }).ToList());
                        fence["TabsEnabled"] = "false"; fence["CurrentTab"] = 0; fence["Tabs"] = new JsonArray();
                    }
                }
                array.Add(fence);
            }
            return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static JsonArray ItemsOf(IEnumerable<ContainerItem>? items)
        {
            var arr = new JsonArray();
            foreach (var i in items ?? Enumerable.Empty<ContainerItem>())
            {
                if (string.IsNullOrWhiteSpace(i.Path) || i.Path.StartsWith("::spacer::", StringComparison.Ordinal)) continue;   // spacers have no Fences equivalent
                string ext = Path.GetExtension(i.Path).ToLowerInvariant();
                arr.Add(new JsonObject
                {
                    ["Filename"] = i.Path,
                    ["IsFolder"] = Directory.Exists(i.Path),
                    ["IsLink"] = ext is ".lnk" or ".url",
                    ["IsNetwork"] = i.Path.StartsWith(@"\\", StringComparison.Ordinal),
                    ["DisplayName"] = Path.GetFileNameWithoutExtension(i.Path) is { Length: > 0 } n ? n : Path.GetFileName(i.Path),
                    ["AlwaysRunAsAdmin"] = false
                });
            }
            return arr;
        }
    }
}
