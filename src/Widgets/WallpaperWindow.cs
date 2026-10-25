using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using MotionDesk.Services;

namespace MotionDesk.Widgets
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public sealed class WallpaperBridge : IDisposable
    {
        private AudioPeakService? _audio;
        private readonly string? _screenDeviceName;

        public WallpaperBridge(string? screenDeviceName = null)
        {
            _screenDeviceName = screenDeviceName;
        }

        // Called by the page once its first frame is on screen (see index.html signalReady): the window is only REVEALED then, so the
        // user never sees a black rectangle while WebView2 is still starting up (the flicker at startup / when the overlay-only mode starts).
        public Action? ContentReady { get; set; }
        public void NotifyReady() => ContentReady?.Invoke();

        public float GetAudioPeak()
        {
            _audio ??= new AudioPeakService();
            return _audio.GetPeak();
        }

        public void Dispose() { _audio?.Dispose(); _spectrum?.Dispose(); }

        // ---- API for local web wallpapers (pushed to the page by the window, see WallpaperWindow.SetApiPump) ----
        private AudioSpectrumService? _spectrum;
        private bool _apiActive;
        public Action<bool>? ApiChanged { get; set; }
        public bool ApiActive { get => _apiActive; private set { if (_apiActive == value) return; _apiActive = value; ApiChanged?.Invoke(value); } }
        public bool WantsAudio { get; private set; }
        public bool WantsGpu { get; private set; }
        public bool WantsPointer { get; private set; }
        public Action? SubscriptionChanged { get; set; }
        public void SetSubscription(bool audio, bool gpu, bool pointer = false) { WantsAudio = audio; WantsGpu = gpu; bool p = WantsPointer != pointer; WantsPointer = pointer; if (p) SubscriptionChanged?.Invoke(); }

        public string GetPagePropertiesJson()
        {
            var s = WallpaperSettings.Load();
            string? index = MotionDesk.Widgets.MediaSource.LocalPagePath(s.PageSource);
            var m = WallpaperManifest.LoadFor(index);
            return index == null || m == null ? "{}" : WallpaperManifest.ValuesJson(s, index, m);
        }

        private DateTime _disksAt = DateTime.MinValue;
        private object? _disks;
        private object DisksSnapshot()
        {
            if (_disks != null && (DateTime.UtcNow - _disksAt).TotalSeconds < 15) return _disks;
            var list = new List<object>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    double total = d.TotalSize / 1048576.0, free = d.AvailableFreeSpace / 1048576.0;
                    list.Add(new { name = d.Name.TrimEnd('\\'), totalMb = Math.Round(total), freeMb = Math.Round(free), usedPercent = total > 0 ? Math.Round((total - free) / total * 100, 1) : 0 });
                }
            }
            catch (Exception) { }
            _disksAt = DateTime.UtcNow; return _disks = list;
        }

        public string GetSystemJson(bool gpu) => BuildSystemJson(gpu);
        public string GetAudioBandsJson() => BuildAudioJson();

        internal string BuildSystemJson() => BuildSystemJson(WantsGpu);
        internal string BuildSystemJson(bool wantGpu)
        {
            var m = MotionDesk.Services.AdvancedSystemMonitorService.Instance.GetSnapshot();
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            object? gpu = null;
            if (wantGpu)
            {
                var g = MotionDesk.Services.GpuMonitorService.Instance.GetSnapshot();
                gpu = new { name = g.Name, load = g.LoadPercent, temperature = g.TemperatureC, memoryUsedMb = g.MemoryUsedMb };
            }
            double total = m.TotalMemoryMb, avail = m.AvailableMemoryMb;
            return JsonSerializer.Serialize(new
            {
                cpu = Math.Round(m.CpuPercent, 1),
                ramTotalMb = Math.Round(total), ramAvailableMb = Math.Round(avail),
                ramUsedPercent = total > 0 ? Math.Round((total - avail) / total * 100, 1) : 0,
                netDownKbps = Math.Round(m.NetworkDownKbps, 1), netUpKbps = Math.Round(m.NetworkUpKbps, 1),
                processes = m.ProcessCount,
                batteryPercent = power.BatteryLifePercent is >= 0 and <= 1 ? (int)Math.Round(power.BatteryLifePercent * 100) : -1,
                charging = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online,
                gpu,
                disks = DisksSnapshot(),
            });
        }

        internal string BuildAudioJson()
        {
            try { _spectrum ??= new MotionDesk.Services.AudioSpectrumService(24); return JsonSerializer.Serialize(_spectrum.GetBands().Select(b => Math.Round(b, 3))); }
            catch (Exception) { return "[]"; }
        }

        public string GetPerformanceMode() => WallpaperSettings.Load().PerformanceMode;

        public bool GetIsLightTheme()
        {
            var settings = WallpaperSettings.Load();
            return settings.ThemeMode switch
            {
                "Light" => true,
                "Dark" => false,
                _ => ThemeService.IsLightTheme(), // Follow / Custom βασίζονται στα Windows εκτός αν έχει επιλεγεί παλέτα ρητά
            };
        }

        // Επιστρέφει JSON με mode, palette name/χρώματα και τις ρυθμίσεις animation (ταχύτητα/glow/πάχος γραμμής).
        public string GetWaveConfigJson()
        {
            var settings = WallpaperSettings.Load();
            bool light = GetIsLightTheme();
            var palette = WavePalettes.Resolve(settings.PaletteName, light);
            // "overlay-only": the animated wallpaper is off but the info overlay is on -> the window shows the normal Windows wallpaper
            string effectiveMode = WallpaperHostEngine.Instance.EffectiveMode(settings);
            string pageUri = effectiveMode == "Page" ? MediaSource.PageUri(settings.PageSource) : "";
            ApiActive = pageUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
            long everyMs = settings.RotateEveryMinutes * 60_000L;
            long firstMs = everyMs;
            if (everyMs > 0 && settings.LastRotateUtc is { } last) firstMs = Math.Max(15_000, everyMs - (long)(DateTime.UtcNow - last).TotalMilliseconds);

            return JsonSerializer.Serialize(new
            {
                mode = effectiveMode,
                desk = effectiveMode == "Desktop" ? DeskInfo() : null,
                infoFont = string.IsNullOrWhiteSpace(settings.InfoFont) ? "Segoe UI Light" : settings.InfoFont,
                waveStyle = settings.WaveStyle,
                pageUri,
                elements = settings.Elements.Where(e => e.Enabled).Select(e => new { id = e.Id, scale = Math.Clamp(e.Scale, 40, 250) }),
                elSide = settings.ElementsSide == "Right" ? "Right" : "Left",
                elVAlign = settings.ElementsVAlign is "Top" or "Bottom" ? settings.ElementsVAlign : "Center",
                elAccent = settings.ElementsAccent,
                elCard = settings.ElementsCard,
                pageLocal = ApiActive,
                rotateFirstMs = firstMs,
                imageUri = effectiveMode == "Image" ? MediaSource.ImageUri(settings.ImagePath) : "",
                imageFit = settings.ImageFit,
                background = palette.Background,
                wave = palette.WaveColor,
                peak = palette.PeakColor,
                glow = palette.GlowColor,
                highlight = palette.HighlightColor,
                speed = settings.WaveSpeed,
                glowIntensity = settings.GlowIntensity,
                lineThickness = settings.LineThickness,
                performanceMode = settings.PerformanceMode,
                audioEnabled = settings.AudioEnabled,
                autoMuted = WallpaperHostEngine.Instance.AutoMuted,
                audioVolume = settings.AudioVolume,
                audioBassGain = settings.AudioBassGain,
                audioMidGain = settings.AudioMidGain,
                audioTrebleGain = settings.AudioTrebleGain,
                weatherSim = settings.WeatherSimulation,
                timeSim = settings.TimeSimulation,
                weatherGlass = settings.WeatherGlass,
                weatherLat = settings.WeatherLat,
                weatherLon = settings.WeatherLon,
                rotateMinutes = settings.RotateEveryMinutes,
                windSim = settings.WindSimulation,
                weatherInfo = settings.WeatherShowInfo && (effectiveMode == "Weather" || settings.InfoOnAllModes),
                infoX = Math.Clamp(settings.WeatherInfoX, 0, 100),
                infoY = Math.Clamp(settings.WeatherInfoY, 0, 100),
                infoScale = Math.Clamp(settings.WeatherInfoScale, 40, 250),
                fahrenheit = settings.WeatherFahrenheit,
                lang = MotionDesk.Services.LocalizationManager.CurrentLanguage,
                weatherCity = string.IsNullOrWhiteSpace(settings.WeatherCity) ? MotionDesk.Services.LocalizationManager.T("Wallpaper.WeatherLocationDefaultName") : settings.WeatherCity,
                condLabels = new Dictionary<string, string>
                {
                    ["Clear"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.Clear"),
                    ["PartlyCloudy"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.PartlyCloudy"),
                    ["Cloudy"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.Cloudy"),
                    ["Drizzle"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.Drizzle"),
                    ["Rain"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.Rain"),
                    ["HeavyRain"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.HeavyRain"),
                    ["Thunderstorm"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.Thunderstorm"),
                    ["Snow"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.Snow"),
                    ["Fog"] = MotionDesk.Services.LocalizationManager.T("Wallpaper.Sim.Fog")
                }
            });
        }

        // Current Windows wallpaper (polled by the "Desktop" mode so a slideshow / a changed wallpaper is picked up)
        public string GetDesktopWallpaperJson() => JsonSerializer.Serialize(DeskInfo());

        // lower-case keys on purpose: index.html reads d.uri / d.style / d.color (the record's PascalCase names did not match)
        private static object DeskInfo()
        {
            var i = MotionDesk.Services.DesktopWallpaperReader.Read();
            return new { uri = i.Uri, style = i.Style, color = i.Color, stamp = i.Stamp };
        }

        // Live weather for the "Weather" wallpaper mode (cached 10 min in WeatherService; the JS side polls every 10 min).
        public string GetWeatherJson()
        {
            var settings = WallpaperSettings.Load();
            return MotionDesk.Services.WeatherService.GetWallpaperWeatherCached(settings.WeatherLat, settings.WeatherLon);
        }

        // Starts a background refresh when the cached weather is missing or older than 10 minutes (returns immediately)
        public void RequestWeatherRefresh()
        {
            var settings = WallpaperSettings.Load();
            MotionDesk.Services.WeatherService.RequestWallpaperWeatherRefresh(settings.WeatherLat, settings.WeatherLon);
        }

        // Αν αυτή η οθόνη έχει "καρφιτσωμένο" δικό της βίντεο (ScreenVideoOverride), το
        // χρησιμοποιεί αντί για το κοινό playlist — έτσι υποστηρίζεται διαφορετικό βίντεο ανά
        // οθόνη χωρίς να χρειάζεται ξεχωριστό playlist/shuffle state ανά οθόνη.
        private string? PinnedVideoForThisScreen(WallpaperSettings settings)
        {
            if (_screenDeviceName != null && settings.ScreenVideoOverride.TryGetValue(_screenDeviceName, out var pinned) && File.Exists(pinned))
                return pinned;
            return null;
        }

        public string GetCurrentVideoUri()
        {
            var settings = WallpaperSettings.Load();
            if (WallpaperHostEngine.Instance.EffectiveMode(settings) != "Video") return string.Empty;

            string? path = PinnedVideoForThisScreen(settings) ?? settings.CurrentPlaylistFile();
            _lastServedSerial = settings.AdvanceSerial;
            return string.IsNullOrEmpty(path) ? string.Empty : new Uri(path).AbsoluteUri;
        }

        // Το κοινό playlist έχει ΕΝΑΝ δείκτη, αλλά υπάρχει ένα WallpaperBridge ανά οθόνη. Κάθε πραγματική προώθηση αυξάνει το AdvanceSerial·
        // μια οθόνη προχωράει το playlist ΜΟΝΟ αν είναι ακόμα στο serial που σέρβιρε η ίδια (αλλιώς άλλη οθόνη έχει ήδη προχωρήσει και
        // απλώς παίρνει το τρέχον). Έτσι δουλεύουν σωστά και οι επαναλήψεις ("παίξε κάθε βίντεο N φορές") που ΔΕΝ αλλάζουν αρχείο.
        private int _lastServedSerial = -1;

        // Καλείται από το JS όταν ένα βίντεο τελειώνει (advance), αποτυγχάνει να παιχτεί (skip) ή λήγει ο χρονιστής εναλλαγής.
        public string AdvanceVideo()
        {
            var settings = WallpaperSettings.Load();

            // Μια "καρφιτσωμένη" οθόνη δεν προχωράει ποτέ στο κοινό playlist — ξαναπαίζει το ίδιο βίντεο.
            var pinned = PinnedVideoForThisScreen(settings);
            if (pinned != null) return new Uri(pinned).AbsoluteUri;

            if (_lastServedSerial < 0 || settings.AdvanceSerial == _lastServedSerial)
            {
                settings.AdvancePlaylist();
                settings.AdvanceSerial++;
                settings.LastRotateUtc = DateTime.UtcNow;
                settings.Save();
            }
            _lastServedSerial = settings.AdvanceSerial;

            string? path = settings.CurrentPlaylistFile();
            return string.IsNullOrEmpty(path) ? string.Empty : new Uri(path).AbsoluteUri;
        }
    }

    public sealed class WavePalette
    {
        public string Name { get; set; } = string.Empty;
        public string Background { get; set; } = "#05070d";
        public string WaveColor { get; set; } = "#0b3d91";
        public string PeakColor { get; set; } = "#00d2ff";
        public string GlowColor { get; set; } = "#00e5ff";
        public string HighlightColor { get; set; } = "#ffffff";
    }

    // Προκαθορισμένες παλέτες, εμπνευσμένες από το Windows 11 "Bloom" wallpaper — ξεχωριστές
    // για Light και Dark θέμα, ώστε το "Follow Windows" να επιλέγει αυτόματα τη σωστή ομάδα.
    public static class WavePalettes
    {
        public static readonly WavePalette[] Light =
        {
            new() { Name = "Ice Blue / Cyan",  Background = "#eaf6ff", WaveColor = "#bfe4ff", PeakColor = "#3aa0ff", GlowColor = "#00d2ff", HighlightColor = "#ffffff" },
            new() { Name = "Aqua / Emerald",   Background = "#eafaf3", WaveColor = "#bdeedd", PeakColor = "#12b886", GlowColor = "#37e6b0", HighlightColor = "#ffffff" },
            new() { Name = "Pearl / Violet",   Background = "#f3eefc", WaveColor = "#ddc8f7", PeakColor = "#8a5cf6", GlowColor = "#c084fc", HighlightColor = "#ffffff" },
        };

        public static readonly WavePalette[] Dark =
        {
            new() { Name = "Windows Blue / Cyan", Background = "#050b18", WaveColor = "#0b3d91", PeakColor = "#00d2ff", GlowColor = "#00e5ff", HighlightColor = "#ffffff" },
            new() { Name = "Deep Blue / Purple",  Background = "#07081a", WaveColor = "#1c2a6b", PeakColor = "#7c5cff", GlowColor = "#b18cff", HighlightColor = "#ffffff" },
            new() { Name = "Cyan / Magenta",      Background = "#05100f", WaveColor = "#0a4d4a", PeakColor = "#ff3ec9", GlowColor = "#00f5d4", HighlightColor = "#ffffff" },
            new() { Name = "Indigo / Orange",     Background = "#080714", WaveColor = "#241a5e", PeakColor = "#ff9f43", GlowColor = "#ffcf86", HighlightColor = "#ffffff" },
            // Δύο νέες, "τεχνολογικές" παλέτες — ταιριάζουν ιδιαίτερα με το νέο στυλ TechGrid,
            // αλλά διαθέσιμες και στα Ribbons/Aurora αφού η παλέτα είναι ανεξάρτητη από το στυλ.
            new() { Name = "Matrix Green",        Background = "#020a05", WaveColor = "#0a3d1e", PeakColor = "#39ff6a", GlowColor = "#7dffb0", HighlightColor = "#ccffdd" },
            new() { Name = "Cyber Neon",          Background = "#0a0018", WaveColor = "#2a0a4d", PeakColor = "#ff2ec4", GlowColor = "#00e5ff", HighlightColor = "#ffffff" },
        };

        public static IEnumerable<string> AllNames => Light.Concat(Dark).Select(p => p.Name).Distinct();

        public static WavePalette Resolve(string name, bool light)
        {
            var group = light ? Light : Dark;
            return group.FirstOrDefault(p => p.Name == name) ?? group[0];
        }
    }

    public sealed class WallpaperScheduleRule
    {
        public bool Enabled { get; set; }
        public int StartMinutes { get; set; }      // minutes after local midnight
        public int EndMinutes { get; set; }        // exclusive; a window with End <= Start wraps past midnight
        public string Mode { get; set; } = "Waves";

        public bool Contains(int minutes) =>
            StartMinutes == EndMinutes ? true
            : StartMinutes < EndMinutes ? minutes >= StartMinutes && minutes < EndMinutes
            : minutes >= StartMinutes || minutes < EndMinutes;
    }

    public sealed class WallpaperElement
    {
        public string Id { get; set; } = "";
        public bool Enabled { get; set; }
        public int X { get; set; } = 50;          // centre, % of the screen
        public int Y { get; set; } = 50;
        public int Scale { get; set; } = 100;     // %

        public static readonly (string Id, int X, int Y)[] Catalog =
        {
            ("analog", 14, 22), ("cpu", 14, 52), ("ram", 14, 72), ("gpu", 14, 90), ("network", 86, 22), ("disks", 86, 50), ("battery", 86, 72), ("audio", 50, 88),
        };
    }

    public sealed class WallpaperSettings
    {
        /// <summary>The catalog merged with what the user saved: every known element is present, in catalog order.</summary>
        public void EnsureElements()
        {
            var merged = new List<WallpaperElement>();
            foreach (var (id, x, y) in WallpaperElement.Catalog)
                merged.Add(Elements.FirstOrDefault(e => e.Id == id) ?? new WallpaperElement { Id = id, X = x, Y = y });
            Elements = merged;
        }

        public string Mode { get; set; } = "Waves"; // "Waves" | "Video" | "Particles" | "Weather" | "Desktop" (the normal Windows wallpaper, so the info overlay works over it)
        // "Page" mode: a website (https://…), a YouTube link or a local .html file / folder with index.html. "Image" mode: a GIF / PNG / JPG file.
        public string PageSource { get; set; } = string.Empty;
        public string AppWallpaperPath { get; set; } = string.Empty;   // "Application" mode: a program whose window is shown as the wallpaper
        public string AppWallpaperArgs { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string ImageFit { get; set; } = "Cover";   // Cover | Contain
        // Wallpaper elements: the widget pieces (clock, CPU, memory, ...) the user shows on top of ANY wallpaper mode, see wallpaper/elements.js.
        public List<WallpaperElement> Elements { get; set; } = new();
        public string ElementsAccent { get; set; } = "#00d2ff";
        public bool ElementsCard { get; set; } = true;          // frosted card behind every element
        public string ElementsSide { get; set; } = "Left";      // Left | Right: the elements are stacked in a column on that edge of the screen
        public string ElementsVAlign { get; set; } = "Center";  // Top | Center | Bottom
        public Dictionary<string, string> PageProperties { get; set; } = new();   // "<page>|<key>" -> value, see WallpaperManifest
        public DateTime? LastRotateUtc { get; set; }                               // when the playlist last advanced (the rotation timer survives restarts)
        public string WaveStyle { get; set; } = "Ribbons"; // "Ribbons" | "Aurora" — παραλλαγές ΜΕΣΑ στο Waves mode
        public string VideoPath { get; set; } = string.Empty; // legacy single-video field, kept for back-compat
        public List<string> VideoPaths { get; set; } = new();
        // Ζητήθηκε ρητά "να επιλέγει ο χρήστης 1 ή περισσότερα βίντεο για να αναπαράγονται" —
        // ένα βίντεο μπορεί να είναι ΦΟΡΤΩΜΕΝΟ στη βιβλιοθήκη (VideoPaths) χωρίς να συμμετέχει
        // στην ενεργή αναπαραγωγή/shuffle. Άδειο σύνολο = όλα ενεργά (προεπιλογή/παλιά συμπεριφορά).
        public HashSet<string> DisabledVideoPaths { get; set; } = new();
        public bool Shuffle { get; set; } = false;
        public int CurrentVideoIndex { get; set; } = 0;
        // Shuffle "σακούλα": τα βίντεο που έχουν ήδη παιχτεί στον τρέχοντα κύκλο. Αποθηκεύεται στο JSON
        // (το WallpaperSettings φορτώνεται από την αρχή σε κάθε κλήση, άρα μνήμη μόνο σε πεδίο δεν αρκεί).
        public List<string> ShuffleHistory { get; set; } = new();

        // "Weather" mode: the scene follows the real time of day + live weather of this location (default: Athens, like the weather widget).
        public string WeatherCity { get; set; } = string.Empty;
        public double WeatherLat { get; set; } = 37.9838;
        public double WeatherLon { get; set; } = 23.7275;
        // Ζητήθηκε ρητά: αν ο χρήστης δεν έχει ορίσει ποτέ πόλη, ο καιρός δείχνει σιωπηλά τον
        // προεπιλεγμένο (Αθήνα) χωρίς καμία ένδειξη ότι δεν είναι η πραγματική του τοποθεσία —
        // ρωτάμε μία φορά, όχι σε κάθε επίσκεψη της σελίδας.
        public bool WeatherLocationPrompted { get; set; } = false;
        // "Auto" = live. Otherwise a fixed scene (handy for previews/offline): Clear|PartlyCloudy|Cloudy|Drizzle|Rain|HeavyRain|Thunderstorm|Snow|Fog
        public string WeatherSimulation { get; set; } = "Auto";
        // "Auto" = live. Otherwise Dawn|Day|Dusk|Night
        public string TimeSimulation { get; set; } = "Auto";
        // "Auto" = live wind. Otherwise Calm|LightBreeze|FreshBreeze|Strong|Gale (Beaufort 1/3/5/7/9) — drives waves and cloud speed
        public string WindSimulation { get; set; } = "Auto";
        public bool WeatherGlass { get; set; } = true;   // raindrops on a window pane while it rains

        // Clock / date / temperature overlay of the Weather mode. Position is the CENTRE of the block, in % of the screen
        // (default: horizontally centred, in the upper third so it sits in the sky and not on the horizon/desktop icons).
        public bool WeatherShowInfo { get; set; } = true;
        public bool InfoOnAllModes { get; set; } = true;     // the overlay also appears over videos / Waves / Particles / the Windows wallpaper
        public string InfoFont { get; set; } = "Segoe UI Light";
        public int WeatherInfoX { get; set; } = 50;
        public int WeatherInfoY { get; set; } = 34;
        public int WeatherInfoScale { get; set; } = 100;  // % of the default size
        public bool WeatherFahrenheit { get; set; } = false;

        // Playlist behaviour
        public bool IncludeSubfolders { get; set; } = false;   // "Add folder" also scans sub-folders
        public int RepeatPerVideo { get; set; } = 1;            // each video plays N times before the next one (1..10)
        public int RotateEveryMinutes { get; set; } = 0;        // 0 = switch when the video ends; N = switch every N minutes
        public int RepeatsDone { get; set; } = 0;               // internal: how many times the current video already repeated
        public int AdvanceSerial { get; set; } = 0;             // internal: incremented on every real advance (lets several screens share one playlist)

        // Time-of-day schedule: switch the wallpaper MODE automatically (e.g. calm waves in the morning, weather at night)
        public bool ScheduleEnabled { get; set; } = false;
        public List<WallpaperScheduleRule> Schedule { get; set; } = new();

        // Ήχος wallpaper video — ζητήθηκε ρητά, πραγματικό DSP (Web Audio API μέσα στο
        // wallpaper/index.html) πάνω στον ΔΙΚΟ ΜΑΣ ήχο του video wallpaper (όχι system-wide, βλ.
        // σχόλιο στο ShowAudioEnhancement για το γιατί system-wide δεν είναι εφικτό εδώ χωρίς
        // driver-level component). Προεπιλογή ΣΙΓΗ (AudioEnabled=false) — το wallpaper video ήταν
        // πάντα σιωπηλό μέχρι τώρα, δεν αλλάζουμε τη συμπεριφορά υπαρχόντων χρηστών χωρίς ρητή
        // ενεργοποίηση.
        public bool AudioEnabled { get; set; } = false;
        public double AudioVolume { get; set; } = 0.8;   // 0 - 1
        public double AudioBassGain { get; set; } = 0;   // dB, -12..+12
        public double AudioMidGain { get; set; } = 0;    // dB, -12..+12
        public double AudioTrebleGain { get; set; } = 0; // dB, -12..+12

        public string PerformanceMode { get; set; } = "Balanced";
        public string ThemeMode { get; set; } = "Follow"; // Follow | Light | Dark
        public string PaletteName { get; set; } = "Windows Blue / Cyan";
        public double WaveSpeed { get; set; } = 1.0;      // 0.3 - 2.0
        public double GlowIntensity { get; set; } = 1.0;  // 0 - 2.0
        public double LineThickness { get; set; } = 1.0;  // 0.5 - 2.0

        // Screen.DeviceName -> "καρφιτσωμένο" βίντεο σε ΜΙΑ συγκεκριμένη οθόνη, ανεξάρτητο από
        // το κοινό playlist/shuffle των υπόλοιπων — ζητήθηκε ρητά "διαφορετικό βίντεο ανά οθόνη".
        // Οθόνες χωρίς entry εδώ συνεχίζουν να μοιράζονται το κανονικό playlist.
        public Dictionary<string, string> ScreenVideoOverride { get; set; } = new();

        public static readonly string[] SupportedVideoExtensions =
        {
            ".mp4", ".m4v", ".webm", ".mov", ".ogv", ".ogg",
            ".avi", ".mkv", ".wmv", ".mpeg", ".mpg", ".m2ts", ".ts"
        };

        private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "wallpaper.json");

        public static WallpaperSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var loaded = JsonSerializer.Deserialize<WallpaperSettings>(MotionDesk.Services.AtomicFile.ReadAllText(ConfigPath));
                    if (loaded != null)
                    {
                        loaded.MigrateLegacyVideoPath();
                        return loaded;
                    }
                }
            }
            // Confirmed crash: IOException (π.χ. το αρχείο κλειδωμένο στιγμιαία από άλλο write —
            // πραγματικό σενάριο, όχι μόνο σε δοκιμές: antivirus scan, OneDrive sync, ή απλά δύο
            // threads που διαβάζουν/γράφουν σχεδόν ταυτόχρονα, αφού το WallpaperSettings.Load()
            // καλείται συχνότατα σε όλη την εφαρμογή) ΔΕΝ πιανόταν εδώ, μόνο JsonException — μια
            // στιγμιαία κλειδωμένη ανάγνωση κατά την ΕΚΚΙΝΗΣΗ (μέσα στον constructor του
            // MainWindow, πριν καν ξεκινήσει το Application.Run) έριχνε unhandled exception που
            // τερμάτιζε ολόκληρη την εφαρμογή αμέσως. Fail-soft όπως και το AppSettings.Load().
            catch (JsonException) { }
            catch (IOException) { }

            return new WallpaperSettings { PerformanceMode = AppSettings.Load().WallpaperPerformanceMode };
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            MotionDesk.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(this));
        }

        private void MigrateLegacyVideoPath()
        {
            if (!string.IsNullOrEmpty(VideoPath) && !VideoPaths.Contains(VideoPath))
            {
                VideoPaths.Insert(0, VideoPath);
            }
        }

        private List<string> PlayableVideos() => VideoPaths.Where(p => File.Exists(p) && !DisabledVideoPaths.Contains(p)).ToList();

        public string? CurrentPlaylistFile()
        {
            var playable = PlayableVideos();
            if (playable.Count == 0) return null;

            if (CurrentVideoIndex < 0 || CurrentVideoIndex >= playable.Count) CurrentVideoIndex = 0;
            return playable[CurrentVideoIndex];
        }

        public void SetVideoEnabled(string path, bool enabled)
        {
            var current = CurrentPlaylistFile();
            if (enabled) DisabledVideoPaths.Remove(path);
            else DisabledVideoPaths.Add(path);
            KeepCurrentVideo(current);
        }

        // Ενεργοποίηση/απενεργοποίηση ΟΛΩΝ των βίντεο της βιβλιοθήκης με μία κίνηση.
        public void SetAllVideosEnabled(bool enabled)
        {
            var current = CurrentPlaylistFile();
            if (enabled) DisabledVideoPaths.Clear();
            else foreach (var p in VideoPaths) DisabledVideoPaths.Add(p);
            KeepCurrentVideo(current);
        }

        // Ο δείκτης CurrentVideoIndex αναφέρεται στη λίστα των ΕΝΕΡΓΩΝ βίντεο: όταν αυτή αλλάζει (checkbox/αφαίρεση)
        // οι θέσεις μετακινούνται και το τρέχον βίντεο άλλαζε "μόνο του". Ξαναδείχνουμε το ίδιο αρχείο αν παραμένει ενεργό.
        public void KeepCurrentVideo(string? current)
        {
            var playable = PlayableVideos();
            var idx = current == null ? -1 : playable.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
            CurrentVideoIndex = idx >= 0 ? idx : 0;
        }

        public void AdvancePlaylist()
        {
            var playable = PlayableVideos();
            if (playable.Count == 0) { CurrentVideoIndex = 0; ShuffleHistory.Clear(); RepeatsDone = 0; return; }

            // "Play each video N times": stay on the same file until the repeat counter is used up
            if (RepeatPerVideo > 1 && RepeatsDone + 1 < RepeatPerVideo) { RepeatsDone++; return; }
            RepeatsDone = 0;

            if (Shuffle && playable.Count > 1)
            {
                // Τυχαία σειρά ΧΩΡΙΣ επανάληψη μέσα στον κύκλο: παίζουν όλα τα ενεργά βίντεο μία φορά πριν ξαναπαίξει κάποιο
                // (πριν: καθαρά τυχαία επιλογή με επανάληψη, κάποια βίντεο μπορούσαν να παίζουν συνέχεια και άλλα σχεδόν ποτέ).
                var current = CurrentPlaylistFile();
                if (current != null && !ShuffleHistory.Contains(current, StringComparer.OrdinalIgnoreCase)) ShuffleHistory.Add(current);
                var candidates = playable.Where(p => !ShuffleHistory.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 0)
                {
                    ShuffleHistory.Clear();
                    if (current != null) ShuffleHistory.Add(current);
                    candidates = playable.Where(p => !string.Equals(p, current, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                var pick = candidates[Random.Shared.Next(candidates.Count)];
                CurrentVideoIndex = playable.IndexOf(pick);
            }
            else
            {
                ShuffleHistory.Clear();
                CurrentVideoIndex = (CurrentVideoIndex + 1) % playable.Count;
            }
        }

        public void AddVideoFiles(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (SupportedVideoExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()) && !VideoPaths.Contains(p))
                    VideoPaths.Add(p);
            }
        }

        public int RemoveMissingVideos()
        {
            var current = CurrentPlaylistFile();
            int removed = VideoPaths.RemoveAll(p => !File.Exists(p));
            DisabledVideoPaths.RemoveWhere(p => !File.Exists(p));
            ShuffleHistory.RemoveAll(p => !File.Exists(p));
            if (removed > 0) KeepCurrentVideo(current);
            return removed;
        }

        public int AddVideoFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return 0;

            var found = Directory.EnumerateFiles(folderPath, "*.*", IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => SupportedVideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            AddVideoFiles(found);
            return found.Count;
        }
    }

    // Ζωντανή επιφάνεια εργασίας πίσω από τα εικονίδια: είτε ένα βίντεο (ή playlist βίντεο)
    // είτε τα procedural, theme-aware "Opsis Waves" — ένα ΚΑΙ ΜΟΝΟ engine (WebView2/Canvas)
    // χειρίζεται και τις δύο περιπτώσεις, ώστε να μην υπάρχουν πολλαπλά ασύνδετα rendering paths.
    public sealed class WallpaperWindow : Form
    {
        private WebView2? _webView;
        private WallpaperBridge? _bridge;
        private bool _initializing;
        private System.Windows.Forms.Timer? _reattachTimer;
        private System.Windows.Forms.Timer? _settleTimer;
        private int _settleAttemptsLeft;

        public Screen TargetScreen { get; }
        // True μόλις το SetParent προς το WorkerW πετύχει έστω μία φορά — από εκεί και πέρα ένα
        // Show()/Hide() δεν το ξανα-βγάζει μπροστά από τα εικονίδια (το SetParent είναι μόνιμο),
        // οπότε ο caller (WallpaperHostEngine.Enable) μπορεί να το ξανα-δείξει απευθείας χωρίς να
        // περάσει ξανά από όλο το attach dance.
        public bool IsAttached { get; private set; }

        // The window is attached behind the icons while still HIDDEN; it is shown only when the page reports its first frame (or after a
        // 4 s safety timeout) — otherwise the black form background flashes over the user's wallpaper while WebView2 starts.
        private bool _contentReady;
        private bool _wantShow;
        public event Action? Revealed;      // raised when the window has been shown (the engine then retires the windows it replaces)
        private System.Windows.Forms.Timer? _revealTimeout;

        // Σηκώνει ΜΑΖΙ, στο ίδιο tick, το Form (Show) ΚΑΙ το ίδιο το WebView2 control
        // (Visible=true — είχε μείνει σκόπιμα false από το InitializeWebViewAsync). Ο στόχος είναι
        // να μην υπάρχει καθόλου χρονικό παράθυρο όπου ο parent είναι ορατός αλλά το WebView2
        // περιεχόμενο ακόμη όχι (αυτό ακριβώς παρατηρήθηκε ως μαύρο flash στην πρώτη εμφάνιση του
        // animated wallpaper) — σε συνδυασμό με το ότι BackColor/DefaultBackgroundColor είναι πλέον
        // ταυτόσημα με το πραγματικό CSS background της σελίδας (#05070d, όχι καθαρό μαύρο), ώστε
        // ακόμη κι αν ένα frame του parent προλάβει να ζωγραφιστεί πριν «κολλήσει» το WebView2 από
        // πάνω, να είναι οπτικά αδιάκριτο από το πραγματικό σκοτεινό σκηνικό αντί για έντονο μαύρο.
        private void ShowRevealed()
        {
            if (_webView != null) _webView.Visible = true;
            Show();
            KeepBehindIcons();
            SyncApplication();
            Revealed?.Invoke();
        }

        /// <summary>Όσο είναι false (wallpaper ΑΠΕΝΕΡΓΟΠΟΙΗΜΕΝΟ) το παράθυρο ΔΕΝ εμφανίζεται ποτέ, ακόμη κι αν ένα attach που ήταν σε εξέλιξη τελειώσει αργότερα.</summary>
        public bool AllowReveal { get; set; } = true;

        /// <summary>Άμεση απόκρυψη + ακύρωση εκκρεμούς εμφάνισης/επανάληψης attach (Disable).</summary>
        public void HideAndCancelReveal()
        {
            AllowReveal = false;
            _wantShow = false;
            _revealTimeout?.Stop();
            _reattachTimer?.Stop();
            if (!IsDisposed && Visible) Hide();
        }

        private void RevealWhenReady()
        {
            if (!AllowReveal) { _wantShow = false; return; }
            if (Visible) { KeepBehindIcons(); return; }     // never BringToFront: as a child of the desktop it would cover the icons
            if (_contentReady) { ShowRevealed(); return; }
            _wantShow = true;
            if (_revealTimeout == null)
            {
                _revealTimeout = new System.Windows.Forms.Timer { Interval = 4000 };
                _revealTimeout.Tick += (_, _) =>
                {
                    _revealTimeout?.Stop();
                    if (_wantShow && !IsDisposed && !Visible)
                    {
                        // Αν το WebView2 δεν ξεκίνησε, ΜΗΝ αποκαλύπτεις συμπαγές σκούρο παράθυρο πάνω από το φόντο
                        // των Windows (φαινόταν ως "το wallpaper δεν δουλεύει"): ξαναδοκίμασε και κράτα το κρυφό.
                        if (_webView?.CoreWebView2 == null)
                        {
                            MotionDesk.Services.AttachLog.Write($"[{TargetScreen.DeviceName}] WebView2 not ready after 4s - keeping window hidden, retrying init");
                            if (!_initializing) _ = InitializeWebViewAsync();
                            _revealTimeout?.Stop(); _revealTimeout?.Start();
                            return;
                        }
                        _wantShow = false; if (AllowReveal) ShowRevealed();
                    }
                };
            }
            _revealTimeout.Stop(); _revealTimeout.Start();
        }

        private void MarkContentReady()
        {
            _contentReady = true;
            if (!AllowReveal) { _wantShow = false; return; }
            if (_wantShow && !IsDisposed && !Visible) { _wantShow = false; _revealTimeout?.Stop(); ShowRevealed(); }
        }

        public WallpaperWindow(Screen targetScreen)
        {
            TargetScreen = targetScreen;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            Bounds = targetScreen.Bounds;
            // Ταυτόσημο με το CSS background του index.html (#05070d), όχι καθαρό μαύρο — βλ. σχόλιο
            // στο ShowRevealed.
            BackColor = Color.FromArgb(5, 7, 13);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // WS_EX_LAYERED εδώ (στο CreateParams, όχι αργότερα μέσω SetWindowLongPtr) —
                // επιβεβαιωμένο σε δύο ανεξάρτητα, ενεργά open-source projects που κάνουν
                // ακριβώς την ίδια δουλειά με τον ίδιο μηχανισμό μας (WebView2 + WorkerW):
                // rocksdanister/lively ("Note: Godot fails to apply WS_EX_LAYERED if attached
                // after SetParent") και bbabcock1990/tool-animated-wallpapers ("WS_EX_LAYERED
                // is required so DWM composites the window correctly when hosted under the
                // desktop icons on the raised desktop, otherwise it renders solid black").
                // Ισχύει σε ΚΑΘΕ διάταξη desktop (όχι μόνο raised) — ακίνδυνο no-op όταν δεν
                // χρειάζεται, μαζί με SetLayeredWindowAttributes(alpha=255) πριν το SetParent.
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                return cp;
            }
        }

        // ΚΡΙΣΙΜΟ: η προηγούμενη εκδοχή έκανε Show() πρώτα και ξεκινούσε το attach ΜΕΣΑ στο
        // OnShown — δηλαδή το παράθυρο γινόταν ορατό ως κανονικό, πλήρους-οθόνης, ΜΠΡΟΣΤΑ από
        // την επιφάνεια εργασίας για όσο διαρκούσε το (πλέον ασύγχρονο) attach, πριν καν προλάβει
        // να μπει πίσω από τα εικονίδια. Αυτό ακριβώς ήταν το bug "συμπεριφέρεται σαν προστασία
        // οθόνης / μαύρη οθόνη που αναβοσβήνει πριν φανεί η επιφάνεια εργασίας" — ένα regression
        // από το προηγούμενο πέρασμα (η ασύγχρονη εκδοχή έλυσε το πάγωμα του UI thread, αλλά
        // άνοιξε αυτό το ορατό "flash"). Διόρθωση: ΠΟΤΕ Show()/Visible=true πριν επιβεβαιωθεί ότι
        // το SetParent πέτυχε — το BeginAttach() παρακάτω ξεκινά μόνο το attach (η πρόσβαση στο
        // Handle αναγκάζει τη δημιουργία του native handle χωρίς να κάνει το Form ορατό), και το
        // TryAttachAsync είναι το ΜΟΝΟ σημείο που καλεί Show(), αφού πρώτα έχει ήδη γίνει
        // SendToBack πίσω από τα εικονίδια.
        private void KeepBehindIcons() => WallpaperInterop.KeepBehindIcons(Handle);
        public void EnsureBehindIcons() { if (!IsDisposed && IsHandleCreated) KeepBehindIcons(); }

        public void BeginAttach() => _ = TryAttachAsync();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!_initializing) _ = InitializeWebViewAsync();
        }

        // Αν το SetParent αποτύχει (π.χ. η Explorer μόλις επανεκκινήθηκε και δεν έχει ακόμη
        // φτιάξει το WorkerW), ΔΕΝ αφήνουμε το μαύρο, πλήρους-οθόνης wallpaper window ως κανονικό
        // top-level παράθυρο -> θα έκρυβε ΟΛΑ τα εικονίδια της επιφάνειας εργασίας (αυτό ήταν το
        // αναφερόμενο bug). Το κρατάμε ΑΟΡΑΤΟ (ποτέ δεν έγινε Show() ακόμη) μέχρι να πετύχει το
        // attach, με retry κάθε δευτερόλεπτο.
        //
        // Παλαιότερο bug fix (παραμένει): το WallpaperInterop.AttachToDesktop περιέχει ένα retry
        // loop με SendMessageTimeout(...,1000ms) × έως 8 προσπάθειες + Thread.Sleep — δηλαδή μέχρι
        // και ~8.5 δευτερόλεπτα blocking στην ΠΡΩΤΗ ενεργοποίηση wallpaper μιας συνεδρίας.
        // Τρέχει σε background thread (Task.Run) — τα Win32 calls δεν έχουν thread-affinity
        // περιορισμό (λειτουργούν πάνω σε HWNDs, όχι σε managed Controls).
        private async Task TryAttachAsync()
        {
            IntPtr handle = Handle;
            Rectangle bounds = TargetScreen.Bounds;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool attached = await Task.Run(() => WallpaperInterop.AttachToDesktop(handle, bounds));
            MotionDesk.Services.AttachLog.Write($"[{TargetScreen.DeviceName}] first attach {(attached ? "OK" : "FAILED (retrying every 1s)")} in {sw.ElapsedMilliseconds} ms");
            if (IsDisposed) return;

            if (attached)
            {
                IsAttached = true;
                KeepBehindIcons();
                _reattachTimer?.Stop();
                RevealWhenReady();
                BeginSettleRecheck();
                return;
            }

            _reattachTimer ??= new System.Windows.Forms.Timer { Interval = 1000 };
            _reattachTimer.Tick -= ReattachTick;
            _reattachTimer.Tick += ReattachTick;
            _reattachTimer.Start();
        }

        // Αυτο-επούλωση για το γνωστό race σε ψυχρή εκκίνηση (ζητήθηκε ρητά: "μερικές φορές δεν
        // φαίνονται τα εικονίδια, μόνο η taskbar, όχι πάντα"): το AttachToDesktop έχει μικρό
        // αρχικό retry παράθυρο (8 προσπάθειες × 60ms ≈ μισό δευτερόλεπτο). Αν η Explorer δεν έχει
        // προλάβει ΑΚΟΜΑ να φτιάξει το SHELLDLL_DefView (αργή εκκίνηση, πολλά προγράμματα
        // εκκίνησης), το attach "πετυχαίνει" μέσω του εφεδρικού μονοπατιού (απευθείας στο
        // Progman, HWND_BOTTOM) — αλλά αν το DefView εμφανιστεί ΑΡΓΟΤΕΡΑ, αυτό ΔΕΝ διορθώνεται
        // ποτέ μόνο του, αφού IsAttached ήδη=true και τίποτα δεν ξαναπροσπαθεί. Ξαναδοκιμάζει το
        // ΠΛΗΡΕΣ attach (idempotent — SetParent στον ίδιο/καλύτερο στόχο, ασφαλές να ξανατρέξει)
        // κάθε 1.5s για ~20 δευτερόλεπτα μετά την πρώτη επιτυχία, ώστε να "αναβαθμιστεί" αυτόματα
        // σε σωστό z-order μόλις η Explorer προλάβει να ολοκληρώσει την αρχικοποίησή της.
        private void BeginSettleRecheck()
        {
            _settleTimer?.Stop();
            _settleTimer?.Dispose();
            _settleAttemptsLeft = 14;
            _settleTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _settleTimer.Tick += async (_, _) =>
            {
                if (IsDisposed || _settleAttemptsLeft-- <= 0) { _settleTimer?.Stop(); return; }
                IntPtr handle = Handle;
                Rectangle bounds = TargetScreen.Bounds;
                bool ok = await Task.Run(() => WallpaperInterop.AttachToDesktop(handle, bounds));
                MotionDesk.Services.AttachLog.Write($"[{TargetScreen.DeviceName}] settle re-check ({14 - _settleAttemptsLeft}/14) {(ok ? "OK" : "FAILED")}");
                if (IsDisposed) return;
                if (ok) KeepBehindIcons();
            };
            _settleTimer.Start();
        }

        private async void ReattachTick(object? sender, EventArgs e)
        {
            if (IsDisposed) { _reattachTimer?.Stop(); return; }
            _reattachTimer?.Stop(); // αποφυγή επικαλυπτόμενων προσπαθειών όσο η τρέχουσα εκκρεμεί
            IntPtr handle = Handle;
            Rectangle bounds = TargetScreen.Bounds;
            bool attached = await Task.Run(() => WallpaperInterop.AttachToDesktop(handle, bounds));
            MotionDesk.Services.AttachLog.Write($"[{TargetScreen.DeviceName}] re-attach {(attached ? "OK" : "FAILED (will retry)")}");
            if (IsDisposed) return;

            if (attached)
            {
                IsAttached = true;
                KeepBehindIcons();
                RevealWhenReady();
                BeginSettleRecheck();
            }
            else
            {
                _reattachTimer?.Start();
            }
        }

        public Task RefreshAsync() { SyncApplication(); return RefreshWebViewAsync(); }

        // "Application" mode lives in the wallpaper window of the PRIMARY screen only.
        private void SyncApplication()
        {
            if (IsDisposed || !TargetScreen.Primary) return;
            var s = WallpaperSettings.Load();
            bool want = WallpaperHostEngine.Instance.EffectiveMode(s) == "Application" && WallpaperHostEngine.Instance.IsEnabled;
            if (want) ApplicationWallpaper.Sync(this, s.AppWallpaperPath, s.AppWallpaperArgs, _paused);
            else ApplicationWallpaper.Stop();
        }

        // Ακριβές "στιγμιότυπο" του ΠΡΑΓΜΑΤΙΚΑ αποδομένου wallpaper (waves/video + το overlay
        // ρολογιού κ.λπ.) για χρήση ως εικόνα οθόνης κλειδώματος — όχι ξαναζωγραφισμένο αντίγραφο,
        // αλλά literal screenshot του WebView2 μέσω της επίσημης CapturePreview API.
        public async Task<bool> CapturePngAsync(string filePath)
        {
            var webView = _webView;
            if (webView?.CoreWebView2 == null) return false;
            try
            {
                // Η πραγματική οθόνη κλειδώματος των Windows δείχνει ήδη τη δική της ώρα/ημερομηνία —
                // το ενσωματωμένο overlay ώρας/καιρού του wallpaper (#wxInfo) θα το διπλασίαζε στο
                // στιγμιότυπο. Κρύβεται ΜΟΝΟ για τη λήψη και ξαναεμφανίζεται αμέσως μετά μέσω της
                // ίδιας της λογικής της σελίδας (updateWeatherInfo), ώστε να μην αλλάξει τίποτα στο
                // ζωντανό wallpaper που βλέπει ο χρήστης.
                try { await webView.CoreWebView2.ExecuteScriptAsync("(function(){var e=document.getElementById('wxInfo'); if(e) e.style.display='none';})();"); } catch (Exception) { }

                await using var stream = new MemoryStream();
                await webView.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
                stream.Position = 0;
                await using var file = File.Create(filePath);
                await stream.CopyToAsync(file);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Wallpaper CapturePngAsync failed: {ex}");
                return false;
            }
            finally
            {
                try { await webView.CoreWebView2.ExecuteScriptAsync("if (typeof updateWeatherInfo === 'function') updateWeatherInfo();"); } catch (Exception) { }
            }
        }

        // Αυτόματη παύση όταν μια άλλη εφαρμογή είναι πλήρους οθόνης (ζητήθηκε ρητά στο
        // roadmap) — καθαρά εξοικονόμηση CPU/μπαταρίας, αφού το wallpaper έτσι κι αλλιώς δεν
        // είναι ορατό πίσω από ένα fullscreen παράθυρο.
        private bool _paused;
        private System.Windows.Forms.Timer? _apiTimer;
        private int _apiTick;
        private void SetApiPump(bool on)
        {
            if (IsDisposed) return;
            if (!on) { _apiTimer?.Stop(); return; }
            if (_apiTimer == null)
            {
                _apiTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _apiTimer.Tick += (_, _) =>
                {
                    var core = _webView?.CoreWebView2;
                    if (core == null || _bridge == null || _paused || !Visible) return;
                    _apiTick++;
                    try
                    {
                        if (_bridge.WantsAudio) _ = core.ExecuteScriptAsync("window.opsisPush&&window.opsisPush({opsis:true,type:'audio',bands:" + _bridge.BuildAudioJson() + "})");
                        if (_apiTick % 10 == 0) _ = core.ExecuteScriptAsync("window.opsisPush&&window.opsisPush({opsis:true,type:'system',data:" + _bridge.BuildSystemJson() + "})");
                    }
                    catch (Exception) { }
                };
            }
            _apiTimer.Start();
        }

        // Pointer API for local web wallpapers ("subscribe pointer"): the page lives behind the icons and gets no mouse input from Windows, so Opsis
        // reads the cursor (position on THIS screen, buttons, and whether it is over the empty desktop) and sends it as messages.
        private System.Windows.Forms.Timer? _pointerTimer;
        private int _lastPx = -1, _lastPy = -1, _lastBtn = -1;
        private void UpdatePointerTimer()
        {
            bool on = _bridge is { WantsPointer: true, ApiActive: true };
            if (!on) { _pointerTimer?.Stop(); return; }
            _pointerTimer ??= new System.Windows.Forms.Timer { Interval = 33 };
            _pointerTimer.Tick -= PointerTick; _pointerTimer.Tick += PointerTick;
            _pointerTimer.Start();
        }
        private void PointerTick(object? sender, EventArgs e)
        {
            var core = _webView?.CoreWebView2;
            if (core == null || _paused || !Visible || !GetCursorPos(out var pt)) return;
            var b = TargetScreen.Bounds;
            if (!b.Contains(pt.X, pt.Y)) return;
            int px = (pt.X - b.X) * 10000 / Math.Max(1, b.Width), py = (pt.Y - b.Y) * 10000 / Math.Max(1, b.Height);
            int btn = ((GetAsyncKeyState(0x01) & 0x8000) != 0 ? 1 : 0) | ((GetAsyncKeyState(0x02) & 0x8000) != 0 ? 2 : 0);
            if (px == _lastPx && py == _lastPy && btn == _lastBtn) return;
            _lastPx = px; _lastPy = py; _lastBtn = btn;
            bool overDesktop = IsDesktopSurface(WindowFromPoint(pt));
            _ = core.ExecuteScriptAsync("window.opsisPush&&window.opsisPush({opsis:true,type:'pointer',x:" + (px / 10000.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                + ",y:" + (py / 10000.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ",left:" + ((btn & 1) != 0 ? "true" : "false")
                + ",right:" + ((btn & 2) != 0 ? "true" : "false") + ",overDesktop:" + (overDesktop ? "true" : "false") + "})");
        }
        private static bool IsDesktopSurface(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, 64);
            return sb.ToString() is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman";
        }
        [StructLayout(LayoutKind.Sequential)] private struct PT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out PT p);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PT p);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

        public async Task RunScriptAsync(string js)
        {
            if (_webView?.CoreWebView2 == null) return;
            try { await _webView.CoreWebView2.ExecuteScriptAsync(js); } catch (Exception) { }
        }

        public async Task PushPropertiesAsync()
        {
            if (_webView?.CoreWebView2 == null) return;
            try { await _webView.CoreWebView2.ExecuteScriptAsync("window.opsisSendProps&&window.opsisSendProps()"); } catch (Exception) { }
        }

        public int BrowserProcessId { get { try { return (int)(_webView?.CoreWebView2?.BrowserProcessId ?? 0); } catch (Exception) { return 0; } } }

        public async Task SetPausedAsync(bool paused)
        {
            _paused = paused;
            if (TargetScreen.Primary) ApplicationWallpaper.SetPaused(paused);
            // θυμόμαστε την κατάσταση ώστε να εφαρμοστεί και μετά από (re)navigation
            if (_webView?.CoreWebView2 == null) return;
            var core = _webView.CoreWebView2;
            try
            {
                // Πρώτα ξυπνάμε ένα ενδεχομένως suspended WebView2 — αλλιώς το script δεν εκτελείται.
                if (!paused) { ProcessFreezer.Thaw(); try { core.Resume(); } catch { } }
                await core.ExecuteScriptAsync($"window.motionDeskSetPaused?.({(paused ? "true" : "false")});");
                // Σε κρυμμένο παράθυρο (wallpaper απενεργοποιημένο) το TrySuspend παγώνει πλήρως τη
                // διεργασία rendering (0% CPU/GPU, λιγότερη RAM). Απαιτεί IsVisible=false — αν το
                // παράθυρο είναι ακόμα ορατό (π.χ. fullscreen pause) απλώς επιστρέφει false, χωρίς βλάβη.
                if (paused && !Visible) await core.TrySuspendAsync();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        private async Task InitializeWebViewAsync()
        {
            if (_initializing || IsDisposed) return;
            _initializing = true;
            try
            {
                // Ίδιο χρώμα με το Form.BackColor (#05070d, βλ. constructor) αντί για καθαρό μαύρο —
                // βλ. σχόλιο στο ShowRevealed. Visible=false: το ίδιο το WebView2 control μένει
                // κρυφό μέχρι το ShowRevealed, ώστε να μην ξεκινήσει να compositάρει πριν προλάβει
                // να ζωγραφιστεί το πρώτο πραγματικό frame (prepareFirstFrame στο index.html).
                _webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor, Visible = false };
                Controls.Add(_webView);
                await _webView.EnsureCoreWebView2Async(await WebView2Support.CreateEnvironmentAsync());
                if (IsDisposed || _webView.CoreWebView2 == null) return;
                WebView2Support.Harden(_webView.CoreWebView2);
                _bridge = new WallpaperBridge(TargetScreen.DeviceName)
                {
                    ContentReady = () => { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(MarkContentReady)); }
                };
                _bridge.ApiChanged = on => { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => { SetApiPump(on); UpdatePointerTimer(); })); };
                _bridge.SubscriptionChanged = () => { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(UpdatePointerTimer)); };
                _webView.CoreWebView2.AddHostObjectToScript("wallpaper", _bridge);
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widgets", "wallpaper", "index.html");
                // Μετά από κάθε φόρτωση σελίδας ξαναεφαρμόζουμε την κατάσταση παύσης — αλλιώς ένα
                // νέο/ανανεωμένο παράθυρο (π.χ. rebuild λόγω αλλαγής οθονών ενώ τρέχει fullscreen
                // παιχνίδι) θα έπαιζε κανονικά πίσω από το παιχνίδι.
                _webView.CoreWebView2.NavigationCompleted += (_, _) => { if (_paused) _ = SetPausedAsync(true); };
                if (File.Exists(htmlPath)) _webView.CoreWebView2.Navigate(htmlPath);
                else MotionDesk.Services.AttachLog.Write($"[{TargetScreen.DeviceName}] wallpaper page MISSING: {htmlPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Wallpaper WebView2 initialization failed: {ex}");
                MotionDesk.Services.AttachLog.Write($"[{TargetScreen.DeviceName}] WebView2 init FAILED: {ex.GetType().Name}: {ex.Message} (runtime={WebView2Support.GetRuntimeVersion() ?? "MISSING"})");
                // Καθάρισε το μισο-φτιαγμένο control ώστε η επόμενη προσπάθεια να ξεκινά καθαρά.
                try { if (_webView != null && _webView.CoreWebView2 == null) { Controls.Remove(_webView); _webView.Dispose(); _webView = null; } } catch (Exception) { }
            }
            finally { _initializing = false; }
        }

        private async Task RefreshWebViewAsync()
        {
            if (_webView?.CoreWebView2 == null) return;
            try { await _webView.CoreWebView2.ExecuteScriptAsync("window.motionDeskRefresh?.();"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reattachTimer?.Stop();
                _reattachTimer?.Dispose();
                _reattachTimer = null;
                _settleTimer?.Stop();
                _settleTimer?.Dispose();
                _settleTimer = null;
                _revealTimeout?.Stop();
                _revealTimeout?.Dispose();
                _revealTimeout = null;
                _apiTimer?.Stop(); _apiTimer?.Dispose(); _apiTimer = null;
                _pointerTimer?.Stop(); _pointerTimer?.Dispose(); _pointerTimer = null;
                _bridge?.Dispose();
                _bridge = null;
                _webView?.Dispose();
                _webView = null;
            }
            base.Dispose(disposing);
        }

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
    }

    // Ένα WallpaperWindow ανά οθόνη, ώστε το video/waves να καλύπτει σωστά (cover) κάθε
    // monitor αντί να τεντώνεται σε ολόκληρο το virtual desktop σαν μία μεγάλη εικόνα.
    public sealed class WallpaperHostEngine
    {
        private static WallpaperHostEngine? _instance;
        public static WallpaperHostEngine Instance => _instance ??= new WallpaperHostEngine();

        private readonly List<WallpaperWindow> _windows = new();
        private List<WallpaperWindow> _retiringWindows = new();     // παλιά παράθυρα που περιμένουν να εμφανιστεί το αντικαταστάτης τους (rebuild)
        private bool _enabled;
        // "Overlay-only": the animated wallpaper is OFF but the clock/date/temperature overlay is ON -> the windows run in "Desktop" mode
        // (the normal Windows wallpaper + the overlay). IsEnabled stays false: the user still sees the wallpaper as switched off.
        private bool _overlayOnly;
        private bool Active => _enabled || _overlayOnly;
        public string EffectiveMode(WallpaperSettings settings) => !_enabled && _overlayOnly ? "Desktop" : settings.Mode;
        private System.Windows.Forms.Timer? _fullscreenCheckTimer;
        private System.Windows.Forms.Timer? _lockScreenSyncTimer;

        // Auto-mute: while another application plays sound the wallpaper's own audio (if the user enabled it) is faded out.
        private System.Windows.Forms.Timer? _autoMuteTimer;
        private MotionDesk.Services.OtherAudioDetector? _otherAudio;
        public bool AutoMuted { get; private set; }
        private void EnsureAutoMuteWatcher()
        {
            if (_autoMuteTimer != null) return;
            _autoMuteTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _autoMuteTimer.Tick += (_, _) =>
            {
                try
                {
                    bool want = false;
                    if (Active && MotionDesk.Services.AppSettings.Load().AutoMuteWhenOtherAudio && WallpaperSettings.Load().AudioEnabled)
                        want = (_otherAudio ??= new MotionDesk.Services.OtherAudioDetector()).IsOtherAppPlaying();
                    if (want == AutoMuted) return;
                    AutoMuted = want;
                    foreach (var w in _windows) _ = w.RunScriptAsync("window.opsisAutoMute&&window.opsisAutoMute(" + (want ? "true" : "false") + ")");
                }
                catch (Exception) { }
            };
            _autoMuteTimer.Start();
        }
        private readonly ExplorerRestartWatcher _explorerWatcher = new();
        private System.Threading.Timer? _displayDebounce;
        private readonly SynchronizationContext _ui;

        // IsEnabled = ο χρήστης/η εκκίνηση ΖΗΤΗΣΕ wallpaper (σταθερό από το Enable() ως το Disable()). Πριν απαιτούσε και ορατό
        // παράθυρο — το attach πίσω από τα εικονίδια είναι ασύγχρονο, οπότε στο άνοιγμα της εφαρμογής ο πίνακας έδειχνε "ανενεργό"
        // και διορθωνόταν αργότερα. Το IsRunning ισχύει όταν υπάρχει πράγματι ορατό wallpaper window.
        public bool IsEnabled => _enabled;
        /// <summary>Raised (on the calling thread) after Enable()/Disable(), so the Wallpaper page can keep its Enable/Disable buttons and status in step with the tray, profiles and the schedule.</summary>
        public event Action? StateChanged;
        public bool IsRunning => _enabled && _windows.Any(w => !w.IsDisposed && w.Visible);

        private WallpaperHostEngine()
        {
            // Το SystemEvents καλεί τους handlers από ΔΙΚΟ ΤΟΥ thread (όχι το UI thread) — το RebuildWindows()
            // /RefreshAllAsync() πειράζουν Forms/WebView2 και ΠΡΕΠΕΙ να τρέχουν στο UI thread. Καταγράφουμε
            // το UI SynchronizationContext εδώ (ο constructor τρέχει στο UI thread) και κάνουμε Post.
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // Παύση/συνέχιση σε fullscreen εφαρμογή — τώρα από το κοινό AppActivity (ίδιο state με τα widgets).
            AppActivity.FullscreenChanged += fullscreen =>
            {
                if (!Active) return;
                ApplyHeavyPause();
                // per monitor: a fullscreen app on one screen does not pause the wallpaper of the others (lock/RDP/battery saver pause all)
                foreach (var w in _windows) _ = w.SetPausedAsync(AppActivity.IsPausedFor(w.TargetScreen));
            };

            // Debounce: το DisplaySettingsChanged στέλνεται σε ριπές (πολλά events για μία αλλαγή
            // ανάλυσης/οθόνης/docking) — κάθε RebuildWindows() καταστρέφει και ξαναφτιάχνει ΟΛΑ τα
            // WebView2 (αργό, βαρύ σε RAM/GPU). Περιμένουμε να "ησυχάσει" η ριπή και ξαναχτίζουμε ΜΙΑ φορά.
            SystemEvents.DisplaySettingsChanged += (_, _) =>
            {
                _displayDebounce?.Dispose();
                // Windows often raises DisplaySettingsChanged right after logon/start without any real change; rebuilding then destroyed and
                // recreated every wallpaper window a second after startup (a visible flicker). Rebuild only if the monitor layout differs.
                _displayDebounce = new System.Threading.Timer(_ => _ui.Post(__ => { if (Active && LayoutKey() != _layoutKey) RebuildWindows(); }, null),
                    null, 1200, System.Threading.Timeout.Infinite);
            };
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Desktop) _ui.Post(__ => { _ = RefreshAllAsync(); }, null);
            };

            // Explorer.exe επανεκκινήθηκε -> το παλιό WorkerW (και το wallpaper window που ήταν
            // child του) καταστράφηκε. Ξαναφτιάξε τα παράθυρα από την αρχή (βλ. σχόλιο στο
            // ExplorerRestartWatcher). Μικρή καθυστέρηση πριν το rebuild ώστε η καινούρια Explorer
            // να έχει προλάβει να στήσει το δικό της Progman/WorkerW (one-shot timer· ο ίδιος
            // WndProc τρέχει ήδη στο UI thread, οπότε δεν χρειάζεται Invoke/marshalling).
            _explorerWatcher.ExplorerRestarted += () =>
            {
                if (!Active) return;
                var delay = new System.Windows.Forms.Timer { Interval = 1500 };
                delay.Tick += (_, _) => { delay.Stop(); delay.Dispose(); if (Active) RebuildWindows(); };
                delay.Start();
            };
        }

        // Ελέγχει κάθε 2 δευτερόλεπτα αν το τρέχον foreground παράθυρο καλύπτει ολόκληρη μια
        // οθόνη (borderless fullscreen — παιχνίδι, video player κ.λπ.) και παύει/συνεχίζει το
        // rendering αναλόγως. Ζητήθηκε ρητά στο roadmap.
        private void EnsureFullscreenWatcher()
        {
            if (_fullscreenCheckTimer != null) return;
            _fullscreenCheckTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _fullscreenCheckTimer.Tick += (_, _) =>
            {
                if (!Active) return;

                // Ασφαλιστική δικλείδα πέρα από το TaskbarCreated broadcast: αν το WorkerW στο
                // οποίο είμαστε reparented δεν υπάρχει πλέον (π.χ. μετά από sleep/resume ή reset
                // του GPU driver, όχι απαραίτητα πλήρη επανεκκίνηση της Explorer), τα windows μας
                // έχουν ήδη καταστραφεί σιωπηλά μαζί του. Rebuild πριν προλάβει ο χρήστης να το
                // παρατηρήσει ως "εξαφανίστηκε το wallpaper".
                if (_windows.Count > 0 && _windows.Any(w => !w.IsDisposed) && !WallpaperInterop.IsWorkerWAlive())
                {
                    RebuildWindows();
                    return;
                }

            };
            _fullscreenCheckTimer.Start();
        }

        // Heavy pause: when EVERY wallpaper window is paused (lock, RDP, battery saver, or a fullscreen app with nothing else to show) for 15 s,
        // the WebView2 process tree is suspended. The runtime is shared with the web widgets; they are paused by the same state, and Thaw() runs
        // before any call into the WebView (SetPausedAsync(false)) and at exit.
        private System.Windows.Forms.Timer? _heavyTimer;
        private void ApplyHeavyPause()
        {
            bool all = Active && _windows.Count > 0 && _windows.All(w => w.IsDisposed || AppActivity.IsPausedFor(w.TargetScreen));
            if (!all || !AppSettings.Load().WallpaperHeavyPause)
            {
                _heavyTimer?.Stop(); _heavyTimer?.Dispose(); _heavyTimer = null;
                ProcessFreezer.Thaw();
                return;
            }
            if (_heavyTimer != null || ProcessFreezer.IsFrozen) return;
            _heavyTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            _heavyTimer.Tick += (_, _) =>
            {
                _heavyTimer?.Stop(); _heavyTimer?.Dispose(); _heavyTimer = null;
                try
                {
                    int pid = _windows.Where(w => !w.IsDisposed).Select(w => w.BrowserProcessId).FirstOrDefault(x => x > 0);
                    bool stillAll = Active && _windows.All(w => w.IsDisposed || AppActivity.IsPausedFor(w.TargetScreen));
                    if (stillAll && pid > 0) ProcessFreezer.Freeze(pid);
                }
                catch (Exception ex) { MotionDesk.Services.AttachLog.Write("heavy pause failed: " + ex.Message); ProcessFreezer.Thaw(); }
            };
            _heavyTimer.Start();
        }

        public void EnsureStarted() => Enable();

        public void Enable()
        {
            // Αν υπάρχει ενεργό κινούμενο wallpaper, η εφαρμογή πρέπει να ξαναγυρίζει αυτόματα
            // στην επόμενη εκκίνηση των Windows — ίδια λογική με τα widgets/DeskContainers
            // (ζητήθηκε ρητά "όταν υπάρχει κινούμενο wallpaper όπως και με τα widgets"). Το
            // --background flag (StartupManager.SetStartup) φροντίζει ήδη ώστε αυτή η αυτόματη
            // εκκίνηση να μην αναδύει το κύριο παράθυρο — μόνο να επαναφέρει wallpaper/widgets/
            // DeskContainers στο παρασκήνιο.
            if (!StartupManager.IsStartupEnabled()) StartupManager.SetStartup(true);

            var app = AppSettings.Load();
            if (app.AutoPerformanceMode)
            {
                var recommended = PerformanceModeManager.GetRecommendedMode();
                var settings = WallpaperSettings.Load();
                if (recommended != settings.PerformanceMode) { settings.PerformanceMode = recommended; settings.Save(); }
            }

            bool wasOverlayOnly = _overlayOnly;
            _overlayOnly = false;
            _enabled = true;
            foreach (var w in _windows) w.AllowReveal = true;
            bool freshlyBuilt = _windows.Count == 0;
            if (freshlyBuilt)
            {
                // Το RebuildWindows() ήδη ξεκινάει BeginAttach() για κάθε νέο παράθυρο — αυτά θα
                // γίνουν Show() μόνα τους μόλις (και ΜΟΝΟ μόλις) πετύχει το attach πίσω από τα
                // εικονίδια. Αν εδώ καλούσαμε Show() απευθείας, θα ξαναεμφανιζόταν το ίδιο bug
                // ("μαύρη οθόνη σαν προστασία οθόνης, πριν φανεί η επιφάνεια εργασίας") — θα
                // δείχναμε το παράθυρο ΠΡΙΝ προλάβει να μπει πίσω από τα εικονίδια.
                RebuildWindows();
            }
            else
            {
                // Ήδη υπάρχοντα παράθυρα (π.χ. Disable -> Enable χωρίς αλλαγή mode): αν έχουν ήδη
                // επιτύχει attach μία φορά, το SetParent παραμένει — ασφαλές να τα ξαναδείξουμε
                // απευθείας. Όσα ακόμη περιμένουν attach (σπάνιο — π.χ. προηγούμενη αποτυχία σε
                // εξέλιξη) τα χειρίζεται ήδη μόνος του ο δικός τους reattach timer.
                foreach (var w in _windows)
                {
                    if (!w.IsAttached) continue;
                    if (!w.Visible) w.Show();
                    w.EnsureBehindIcons();      // BringToFront() here put the wallpaper window ABOVE the desktop icons (they "disappeared")
                    _ = w.SetPausedAsync(AppActivity.IsPausedFor(w.TargetScreen));
                }
            }
            EnsureFullscreenWatcher();
            EnsureAutoMuteWatcher();
            EnsureLockScreenSync();
            if (wasOverlayOnly && !freshlyBuilt) _ = RefreshAllAsync();   // switch the running windows from "Desktop" to the real mode
            StateChanged?.Invoke();
        }

        // Starts / stops the "overlay-only" state (see _overlayOnly). Called at startup, after Disable(), and whenever the overlay settings change.
        public void UpdateOverlayOnly()
        {
            var s = WallpaperSettings.Load();
            bool want = !_enabled && s.WeatherShowInfo && s.InfoOnAllModes;
            if (want == _overlayOnly) { if (want) _ = RefreshAllAsync(); return; }
            _overlayOnly = want;
            if (want)
            {
                foreach (var w in _windows) w.AllowReveal = true;
                if (_windows.Count == 0) RebuildWindows();
                else foreach (var w in _windows) { if (w.IsAttached) { if (!w.Visible) w.Show(); w.EnsureBehindIcons(); _ = w.SetPausedAsync(AppActivity.IsPausedFor(w.TargetScreen)); } }
                EnsureFullscreenWatcher();
                _ = RefreshAllAsync();
            }
            else if (!_enabled)
            {
                foreach (var w in _windows) { w.HideAndCancelReveal(); _ = w.SetPausedAsync(true); }
            }
        }

        public void Disable()
        {
            _enabled = false;
            ApplicationWallpaper.Stop();
            ProcessFreezer.Thaw();
            // ΔΙΟΡΘΩΣΗ "το disable θέλει 2-3 φορές": (1) τα παλιά παράθυρα ενός rebuild που ΑΚΟΜΑ περίμεναν τον αντικαταστάτη τους δεν
            // κρύβονταν ποτέ από εδώ, (2) ένα παράθυρο με attach σε εξέλιξη εμφανιζόταν ΜΟΝΟ ΤΟΥ όταν τελείωνε το attach, αφού είχε γίνει Disable.
            foreach (var old in _retiringWindows.ToList()) { try { if (!old.IsDisposed) old.Dispose(); } catch (Exception) { } }
            _retiringWindows.Clear();
            // Ρητή παύση ΠΡΙΝ το Hide — ένα κρυμμένο WebView2 συνεχίζει αλλιώς να αποκωδικοποιεί
            // video/τρέχει canvas σε χαμηλότερο ρυθμό, καταναλώνοντας CPU/GPU για κάτι αόρατο.
            foreach (var w in _windows) { w.HideAndCancelReveal(); _ = w.SetPausedAsync(true); }
            _fullscreenCheckTimer?.Stop();
            UpdateOverlayOnly();   // the overlay keeps running over the normal Windows wallpaper when it is switched on
            StateChanged?.Invoke();
        }

        private string _layoutKey = "";
        private static string LayoutKey() => string.Join("|", Screen.AllScreens.Select(sc => $"{sc.DeviceName}:{sc.Bounds}"));

        private void RebuildWindows()
        {
            _layoutKey = LayoutKey();
            // The old windows stay on screen until a new one is actually visible (or 10 s pass): disposing them first left the plain
            // desktop wallpaper showing for a moment while the new WebView2 started — a visible flicker on every rebuild.
            var retiring = _windows.ToList();
            _retiringWindows = retiring;
            _windows.Clear();
            void RetireOld(string? deviceName = null)      // null = all (safety timeout)
            {
                foreach (var w in retiring.ToList())
                {
                    if (deviceName != null && w.TargetScreen.DeviceName != deviceName) continue;   // per screen: keep the others until THEIR replacement is up
                    if (!w.IsDisposed) w.Dispose();
                    retiring.Remove(w);
                }
            }
            if (retiring.Count > 0)
            {
                var safety = new System.Windows.Forms.Timer { Interval = 10_000 };
                safety.Tick += (_, _) => { safety.Stop(); safety.Dispose(); RetireOld(null); };
                safety.Start();
            }

            foreach (var screen in Screen.AllScreens)
            {
                var window = new WallpaperWindow(screen);
                string deviceName = screen.DeviceName;
                window.Revealed += () => RetireOld(deviceName);
                _windows.Add(window);
                // BeginAttach (ΟΧΙ Show) — το παράθυρο γίνεται ορατό ΜΟΝΟ αφού πρώτα επιβεβαιωθεί
                // ότι μπήκε πίσω από τα εικονίδια, βλ. σχόλιο στο WallpaperWindow.BeginAttach.
                if (Active) window.BeginAttach();
            }
        }

        // Public: ζητήθηκε ρητά έξω από αυτή την κλάση (AddWallpaperVideosAsync/AddVideosAsync
        // στο MainWindow/TrayApplicationContext) — αφού προσθέτουν αρχεία απευθείας στο
        // WallpaperSettings (όχι μέσω SetVideo/AddVideoFolder), χρειάζονται να ζητήσουν ρητά
        // motionDeskRefresh() στο ήδη-τρέχον WebView2, αλλιώς ένα ήδη ανοιχτό wallpaper window
        // (π.χ. σε λειτουργία Waves) δεν μαθαίνει ΠΟΤΕ ότι το Mode/playlist άλλαξε σε Video —
        // ακριβώς το bug "το .wmv μετατράπηκε επιτυχώς αλλά δεν παίζει το βίντεο".
        public void RefreshAll() => _ = RefreshAllAsync();
        public void RefreshElements() { foreach (var w in _windows) _ = w.RunScriptAsync("window.opsisApplyElements&&window.opsisApplyElements()"); }
        public void PushProperties() { foreach (var w in _windows) _ = w.PushPropertiesAsync(); }
        public Task RefreshAllAsync() => Task.WhenAll(_windows.Select(w => w.RefreshAsync()));

        // MD roadmap item: ακριβές στιγμιότυπο του τρέχοντος wallpaper (waves/βίντεο + overlay) για
        // χρήση ως εικόνα οθόνης κλειδώματος — από την οθόνη με το WallpaperWindow που είναι όντως
        // ορατό αυτή τη στιγμή (Screen.PrimaryScreen όταν υπάρχει, αλλιώς το πρώτο διαθέσιμο).
        public async Task<bool> CaptureSnapshotAsync(string filePath)
        {
            var target = _windows.FirstOrDefault(w => !w.IsDisposed && w.Visible && w.TargetScreen.Primary)
                      ?? _windows.FirstOrDefault(w => !w.IsDisposed && w.Visible);
            if (target == null) return false;
            return await target.CapturePngAsync(filePath);
        }

        // Κρατά την οθόνη κλειδώματος "ταιριαστή" με το ζωντανό wallpaper ΧΩΡΙΣ κανένα νέο UAC
        // prompt: το registry key δείχνει ήδη σε αυτό το ΙΔΙΟ αρχείο (ορίστηκε μία φορά elevated) —
        // ξαναγράφουμε απλώς τα bytes της ίδιας εικόνας, που δεν χρειάζεται δικαιώματα διαχειριστή.
        private void EnsureLockScreenSync()
        {
            if (_lockScreenSyncTimer != null) return;
            _lockScreenSyncTimer = new System.Windows.Forms.Timer { Interval = 600_000 }; // 10 λεπτά
            _lockScreenSyncTimer.Tick += async (_, _) =>
            {
                var s = AppSettings.Load();
                if (!s.LockScreenFollowsWallpaper || string.IsNullOrEmpty(s.LockScreenSnapshotPath) || !Active) return;
                try { await CaptureSnapshotAsync(s.LockScreenSnapshotPath); } catch (Exception) { }
            };
            _lockScreenSyncTimer.Start();
        }

        public void SetMode(string mode)
        {
            var settings = WallpaperSettings.Load();
            settings.Mode = mode;
            settings.Save();
            Enable();
            _ = RefreshAllAsync();
        }

        public void SetVideo(string path)
        {
            var settings = WallpaperSettings.Load();
            settings.AddVideoFiles(new[] { path });
            settings.Mode = "Video";
            settings.CurrentVideoIndex = settings.VideoPaths.IndexOf(path);
            settings.Save();
            Enable();
            _ = RefreshAllAsync();
        }

        public int AddVideoFolder(string folder)
        {
            if (!Directory.Exists(folder)) return 0;

            // Τα .wmv ΔΕΝ παίζουν στο WebView2 (δεν έχει decoder WMV3/VC-1) — πριν, ένας φάκελος με
            // .wmv τα πρόσθετε ως έχουν και δεν έπαιζαν ποτέ. Τα μετατρέπουμε πρώτα σε .mp4 (FFmpeg) στο
            // παρασκήνιο, ακριβώς όπως η προσθήκη μεμονωμένων αρχείων.
            var allFiles = Directory.EnumerateFiles(folder, "*.*", WallpaperSettings.Load().IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => WallpaperSettings.SupportedVideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            var wmvFiles = allFiles.Where(f => string.Equals(Path.GetExtension(f), ".wmv", StringComparison.OrdinalIgnoreCase)).ToList();
            var playable = allFiles.Except(wmvFiles).ToList();

            var settings = WallpaperSettings.Load();
            settings.AddVideoFiles(playable);
            if (playable.Count > 0)
            {
                settings.Mode = "Video";
                settings.Save();
                Enable();
                _ = RefreshAllAsync();
            }

            if (wmvFiles.Count > 0) _ = ConvertAndAddWmvAsync(wmvFiles);
            return allFiles.Count;
        }

        private async Task ConvertAndAddWmvAsync(List<string> wmvFiles)
        {
            if (!WmvConversionService.IsFfmpegAvailable)
            {
WmvConversionService.PromptInstallFfmpeg(wmvFiles.Count);
                return;
            }

            var converted = new List<string>();
            foreach (var wmv in wmvFiles)
            {
                string? mp4 = await WmvConversionService.ConvertToMp4Async(wmv);
                if (mp4 != null) converted.Add(mp4);
            }
            if (converted.Count == 0) return;

            var settings = WallpaperSettings.Load();
            settings.AddVideoFiles(converted);
            settings.Mode = "Video";
            settings.Save();
            Enable();
            _ = RefreshAllAsync();
        }

        // ---- playlist options
        public void SetIncludeSubfolders(bool enabled)
        {
            var settings = WallpaperSettings.Load();
            settings.IncludeSubfolders = enabled;
            settings.Save();
        }

        public void SetPlaybackOptions(int repeatPerVideo, int rotateEveryMinutes)
        {
            var settings = WallpaperSettings.Load();
            settings.RepeatPerVideo = Math.Clamp(repeatPerVideo, 1, 10);
            settings.RotateEveryMinutes = Math.Clamp(rotateEveryMinutes, 0, 120);
            settings.RepeatsDone = 0;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public int RemoveMissingVideos()
        {
            var settings = WallpaperSettings.Load();
            int removed = settings.RemoveMissingVideos();
            if (removed > 0) { settings.Save(); _ = RefreshAllAsync(); }
            return removed;
        }

        // ---- time-of-day schedule (mode switching)
        private System.Windows.Forms.Timer? _scheduleTimer;
        private int _lastScheduleRule = -2;

        public void SetSchedule(bool enabled, List<WallpaperScheduleRule> rules)
        {
            var settings = WallpaperSettings.Load();
            settings.ScheduleEnabled = enabled;
            settings.Schedule = rules;
            settings.Save();
            _lastScheduleRule = -2;       // re-evaluate immediately
            EnsureScheduleTimer();
            ScheduleTick();
        }

        public void ApplyScheduleNow() { EnsureScheduleTimer(); ScheduleTick(); }

        public void EnsureScheduleTimer()
        {
            if (_scheduleTimer != null) return;
            _scheduleTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _scheduleTimer.Tick += (_, _) => ScheduleTick();
            _scheduleTimer.Start();
        }

        // Applies a rule only when the ACTIVE rule changes (so a manual mode change stays until the next boundary), and never turns
        // the wallpaper on by itself.
        private void ScheduleTick()
        {
            try
            {
                var settings = WallpaperSettings.Load();
                if (!settings.ScheduleEnabled) { _lastScheduleRule = -2; return; }
                var now = DateTime.Now; int minutes = now.Hour * 60 + now.Minute;
                int idx = settings.Schedule.FindIndex(r => r.Enabled && r.Contains(minutes));
                if (idx == _lastScheduleRule) return;
                _lastScheduleRule = idx;
                if (idx < 0) return;
                var rule = settings.Schedule[idx];
                if (string.Equals(settings.Mode, rule.Mode, StringComparison.Ordinal)) return;
                settings.Mode = rule.Mode;
                settings.Save();
                if (_enabled) _ = RefreshAllAsync();
            }
            catch (Exception) { /* a schedule tick must never throw into the UI loop */ }
        }

        // ---- "Weather" mode settings (each change refreshes the running wallpaper)
        public void SetWeatherLocation(string city, double lat, double lon)
        {
            var settings = WallpaperSettings.Load();
            settings.WeatherCity = city; settings.WeatherLat = lat; settings.WeatherLon = lon;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void MarkWeatherLocationPrompted()
        {
            var settings = WallpaperSettings.Load();
            if (settings.WeatherLocationPrompted) return;
            settings.WeatherLocationPrompted = true;
            settings.Save();
        }

        public void SetWeatherSimulation(string sim)
        {
            var settings = WallpaperSettings.Load();
            settings.WeatherSimulation = sim;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetWindSimulation(string sim)
        {
            var settings = WallpaperSettings.Load();
            settings.WindSimulation = sim;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetTimeSimulation(string sim)
        {
            var settings = WallpaperSettings.Load();
            settings.TimeSimulation = sim;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetWeatherInfo(bool show, bool allModes, int x, int y, int scale, bool fahrenheit)
        {
            var settings = WallpaperSettings.Load();
            settings.WeatherShowInfo = show;
            settings.InfoOnAllModes = allModes;
            settings.WeatherInfoX = Math.Clamp(x, 0, 100);
            settings.WeatherInfoY = Math.Clamp(y, 0, 100);
            settings.WeatherInfoScale = Math.Clamp(scale, 40, 250);
            settings.WeatherFahrenheit = fahrenheit;
            settings.Save();
            UpdateOverlayOnly();
            _ = RefreshAllAsync();
        }

        public void SetInfoFont(string family)
        {
            var settings = WallpaperSettings.Load();
            settings.InfoFont = family;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetWeatherGlass(bool enabled)
        {
            var settings = WallpaperSettings.Load();
            settings.WeatherGlass = enabled;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetShuffle(bool shuffle)
        {
            var settings = WallpaperSettings.Load();
            settings.Shuffle = shuffle;
            settings.ShuffleHistory.Clear();
            settings.Save();
        }

        public void SetVideoEnabled(string path, bool enabled)
        {
            var settings = WallpaperSettings.Load();
            settings.SetVideoEnabled(path, enabled);
            settings.Save();
            _ = RefreshAllAsync();
        }

        // Ρυθμίσεις ήχου wallpaper video (πραγματικό DSP, βλ. σχόλιο στο WallpaperSettings) —
        // ζητήθηκε ρητά. RefreshAllAsync ώστε ένα ήδη ανοιχτό wallpaper window να ενημερώσει
        // αμέσως τον Web Audio γράφο του χωρίς επανεκκίνηση.
        public void SetAudioSettings(bool enabled, double volume, double bass, double mid, double treble)
        {
            var settings = WallpaperSettings.Load();
            settings.AudioEnabled = enabled;
            settings.AudioVolume = Math.Clamp(volume, 0, 1);
            settings.AudioBassGain = Math.Clamp(bass, -12, 12);
            settings.AudioMidGain = Math.Clamp(mid, -12, 12);
            settings.AudioTrebleGain = Math.Clamp(treble, -12, 12);
            settings.Save();
            RefreshAllDebounced();
        }

        public void RemoveVideo(string path)
        {
            var settings = WallpaperSettings.Load();
            var current = settings.CurrentPlaylistFile();
            settings.VideoPaths.Remove(path);
            // Το legacy VideoPath ξαναπροσθέτει το αρχείο στο playlist σε κάθε Load() (MigrateLegacyVideoPath) —
            // χωρίς αυτό, ένα βίντεο που αφαιρούσες "επέστρεφε" μόνο του.
            if (string.Equals(settings.VideoPath, path, StringComparison.OrdinalIgnoreCase)) settings.VideoPath = string.Empty;
            settings.DisabledVideoPaths.Remove(path);
            settings.ShuffleHistory.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            settings.KeepCurrentVideo(current);
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetAllVideosEnabled(bool enabled)
        {
            var settings = WallpaperSettings.Load();
            settings.SetAllVideosEnabled(enabled);
            settings.ShuffleHistory.Clear();
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void ClearVideo()
        {
            var settings = WallpaperSettings.Load();
            settings.VideoPaths.Clear();
            settings.ShuffleHistory.Clear();
            settings.VideoPath = string.Empty;
            settings.Mode = "Waves";
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetWaveStyle(string style)
        {
            var settings = WallpaperSettings.Load();
            settings.WaveStyle = style;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public void SetPalette(string paletteName)
        {
            var settings = WallpaperSettings.Load();
            settings.PaletteName = paletteName;
            settings.Save();
            _ = RefreshAllAsync();
        }

        // Καρφιτσώνει/ξεκαρφιτσώνει ένα συγκεκριμένο βίντεο σε μία οθόνη — ζητήθηκε ρητά
        // "διαφορετικό βίντεο ανά οθόνη" αντί για το ίδιο κοινό playlist παντού.
        public void SetScreenVideoOverride(string screenDeviceName, string? videoPath)
        {
            var settings = WallpaperSettings.Load();
            if (string.IsNullOrEmpty(videoPath)) settings.ScreenVideoOverride.Remove(screenDeviceName);
            else settings.ScreenVideoOverride[screenDeviceName] = videoPath;
            settings.Save();
            _ = RefreshAllAsync();
        }

        public string? GetScreenVideoOverride(string screenDeviceName) =>
            WallpaperSettings.Load().ScreenVideoOverride.TryGetValue(screenDeviceName, out var p) ? p : null;

        public void SetThemeMode(string themeMode)
        {
            var settings = WallpaperSettings.Load();
            settings.ThemeMode = themeMode;
            settings.Save();
            _ = RefreshAllAsync();
        }

        // Τα sliders (ταχύτητα/λάμψη/ένταση ήχου/EQ) καλούν τον setter σε ΚΑΘΕ βήμα της κίνησης — πριν, κάθε
        // βήμα έκανε refresh όλων των WebView2 (και ξανάρχιζε το video). Αποθηκεύουμε αμέσως αλλά κάνουμε ΕΝΑ
        // refresh όταν σταματήσει η κίνηση για ~150ms.
        private System.Windows.Forms.Timer? _refreshDebounce;

        private void RefreshAllDebounced()
        {
            if (_refreshDebounce == null)
            {
                _refreshDebounce = new System.Windows.Forms.Timer { Interval = 150 };
                _refreshDebounce.Tick += (_, _) => { _refreshDebounce!.Stop(); _ = RefreshAllAsync(); };
            }
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }

        public void SetWaveTuning(double speed, double glow, double thickness)
        {
            var settings = WallpaperSettings.Load();
            settings.WaveSpeed = speed;
            settings.GlowIntensity = glow;
            settings.LineThickness = thickness;
            settings.Save();
            RefreshAllDebounced();
        }

        public void SetPerformanceMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) mode = "Balanced";
            var settings = WallpaperSettings.Load();
            settings.PerformanceMode = mode;
            settings.Save();
            var app = AppSettings.Load();
            app.WallpaperPerformanceMode = mode;
            app.Save();
            _ = RefreshAllAsync();
        }
    }
}
