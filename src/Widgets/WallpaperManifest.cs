using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotionDesk.Widgets
{
    /// <summary>One user-adjustable property of a web wallpaper (declared in <c>opsis-wallpaper.json</c> next to its index.html).</summary>
    public sealed class WallpaperProperty
    {
        public string Key { get; set; } = "";
        public string Type { get; set; } = "text";          // slider | checkbox | color | combo | text
        public string Label { get; set; } = "";
        public string LabelEl { get; set; } = "";
        public double Min { get; set; } = 0;
        public double Max { get; set; } = 100;
        public double Step { get; set; } = 1;
        public JsonElement Default { get; set; }
        public List<string> Options { get; set; } = new();

        public string DefaultText => Default.ValueKind switch
        {
            JsonValueKind.String => Default.GetString() ?? "",
            JsonValueKind.Number => Default.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => Type == "checkbox" ? "false" : Type == "slider" ? Min.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
        };
    }

    /// <summary>
    /// Description of a web wallpaper: name, author and the properties the user can change in Opsis (sliders, check boxes, colours, lists, text).
    /// The page receives the values (and system/audio data) through window messages, see docs/WALLPAPER_API.md.
    /// </summary>
    public sealed class WallpaperManifest
    {
        public const string FileName = "opsis-wallpaper.json";
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public List<WallpaperProperty> Properties { get; set; } = new();

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>The manifest of the wallpaper page at <paramref name="indexHtml"/>, or null when there is none / it is not valid.</summary>
        public static WallpaperManifest? LoadFor(string? indexHtml)
        {
            try
            {
                if (string.IsNullOrEmpty(indexHtml)) return null;
                string path = Path.Combine(Path.GetDirectoryName(indexHtml) ?? "", FileName);
                if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return null;
                var m = JsonSerializer.Deserialize<WallpaperManifest>(File.ReadAllText(path), Options);
                if (m == null) return null;
                m.Properties = m.Properties.Where(p => p.Key.Length > 0 && p.Key.Length <= 64).Take(40).ToList();
                return m;
            }
            catch (Exception) { return null; }
        }

        private static string StoreKey(string source, string key) => source + "|" + key;

        public static string GetValue(WallpaperSettings s, string source, WallpaperProperty p) =>
            s.PageProperties.TryGetValue(StoreKey(source, p.Key), out var v) ? v : p.DefaultText;

        public static void SetValue(WallpaperSettings s, string source, string key, string value) => s.PageProperties[StoreKey(source, key)] = value;

        /// <summary>{ "key": value, ... } with numbers and booleans typed, as the page expects.</summary>
        public static string ValuesJson(WallpaperSettings s, string source, WallpaperManifest m)
        {
            var d = new Dictionary<string, object>();
            foreach (var p in m.Properties)
            {
                string v = GetValue(s, source, p);
                d[p.Key] = p.Type switch
                {
                    "slider" => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : p.Min,
                    "checkbox" => v.Equals("true", StringComparison.OrdinalIgnoreCase),
                    _ => v,
                };
            }
            return JsonSerializer.Serialize(d);
        }
    }
}
