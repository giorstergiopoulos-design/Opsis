using System;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services
{
    /// <summary>
    /// Which look the pages of the plugins and the standalone family apps use by default: Opsis' own, the Opsis Appearance one or the Opsis News Crawler one.
    /// The choice is kept in the settings and published as %AppData%\Opsis\plugin-ui.json ({"style":"Opsis|Appearance|Crawler","version":n}), which the plugins and
    /// the standalone apps read (and watch) to follow it.
    /// </summary>
    public static class PluginUiStyle
    {
        public static readonly string[] Styles = { "Opsis", "Appearance", "Crawler" };

        public static string Normalize(string? s) => Array.Find(Styles, x => x.Equals(s, StringComparison.OrdinalIgnoreCase)) ?? "Opsis";

        public static string Current => Normalize(AppSettings.Load().PluginUiStyle);

        public static string FilePath => Path.Combine(AppPaths.AppDataFolder, "plugin-ui.json");

        public static void Set(string style)
        {
            var a = AppSettings.Load(); a.PluginUiStyle = Normalize(style); a.Save();
            Publish();
        }

        public static void Publish()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.AppDataFolder);
                AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(new { style = Current, version = DateTime.UtcNow.Ticks }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { RotatingLogger.Write("plugin-ui.json not written: " + ex.Message); }
        }
    }
}
