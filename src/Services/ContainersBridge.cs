using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MotionDesk.Services
{
    // Opsis side of the settings bridge for the DeskContainers engine: the settings UI on the DeskZones page edits the engine's
    // options.json directly (the engine watches the file and applies changes live) and sends one-shot commands through small files
    // in <AppData>\containers\bridge (see containers\OpsisBridge.cs).
    internal static class ContainersBridge
    {
        private static string Root { get { MigrateOldFolder(); return Path.Combine(AppPaths.AppDataFolder, "containers"); } }
        private static void MigrateOldFolder()
        {
            try { string o = Path.Combine(AppPaths.AppDataFolder, "fences"), n = Path.Combine(AppPaths.AppDataFolder, "containers"); if (!Directory.Exists(n) && Directory.Exists(o)) Directory.Move(o, n); } catch (Exception) { }
        }
        public static string BridgeDir => Path.Combine(Root, "bridge");

        private static string ActiveProfile()
        {
            try
            {
                var j = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "ProfileOptions.json")));
                string? p = j?["ActiveProfile"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(p)) return p;
            }
            catch (Exception) { }
            return "Default";
        }

        public static string OptionsPath => Path.Combine(Root, "Profiles", ActiveProfile(), "options.json");

        private static JsonObject Load()
        {
            try { if (File.Exists(OptionsPath) && JsonNode.Parse(File.ReadAllText(OptionsPath)) is JsonObject o) return o; }
            catch (Exception) { }
            return new JsonObject();
        }

        public static bool GetBool(string key, bool def) { try { return Load()[key]?.GetValue<bool>() ?? def; } catch (Exception) { return def; } }
        public static int GetInt(string key, int def) { try { return Load()[key]?.GetValue<int>() ?? def; } catch (Exception) { return def; } }
        public static string GetString(string key, string def) { try { return Load()[key]?.GetValue<string>() ?? def; } catch (Exception) { return def; } }

        public static List<string> GetStrings(string key, IEnumerable<string> def)
        {
            try { if (Load()[key] is JsonArray a) return a.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList(); } catch (Exception) { }
            return def.ToList();
        }

        private static void Save(Action<JsonObject> edit)
        {
            try
            {
                var o = Load();
                edit(o);
                Directory.CreateDirectory(Path.GetDirectoryName(OptionsPath)!);
                AtomicFile.WriteAllText(OptionsPath, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { RotatingLogger.Write("Fences options write failed: " + ex.Message); }
        }

        public static void Set(string key, bool v) => Save(o => o[key] = v);
        public static void Set(string key, int v) => Save(o => o[key] = v);
        public static void Set(string key, string v) => Save(o => o[key] = v);
        public static void SetStrings(string key, IEnumerable<string> v) => Save(o => o[key] = new JsonArray(v.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()));

        public sealed record EngineState(string Profile, string[] Profiles, bool Automation, bool AutoOrganize, string[] Hidden);

        /// <summary>State published by the engine (bridgestate.json): profiles, switches, hidden containers. Empty state when the engine has not written it yet.</summary>
        public static EngineState ReadState()
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(Path.Combine(BridgeDir, "state.json"))) is JsonObject o)
                {
                    string[] Arr(string k) => (o[k] as JsonArray)?.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToArray() ?? Array.Empty<string>();
                    return new EngineState(o["profile"]?.ToString() ?? "Default", Arr("profiles"), o["automation"]?.GetValue<bool>() ?? false, o["autoOrganize"]?.GetValue<bool>() ?? false, Arr("hidden"));
                }
            }
            catch (Exception) { }
            return new EngineState("Default", Array.Empty<string>(), false, false, Array.Empty<string>());
        }

        public static void Command(string name)
        {
            try
            {
                Directory.CreateDirectory(BridgeDir);
                File.WriteAllText(Path.Combine(BridgeDir, $"cmd_{DateTime.Now.Ticks}.txt"), name);
            }
            catch (IOException) { }
        }
    }
}
