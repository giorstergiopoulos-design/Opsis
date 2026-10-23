using System.Text.Json;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

public sealed class WorkspaceProfile
{
    public string Name { get; set; } = "Default";
    public AppSettings AppSettings { get; set; } = new();
    public WallpaperSettingsSnapshot Wallpaper { get; set; } = new();
    public List<WidgetSnapshot> Widgets { get; set; } = new();
    public List<DeskContainerSnapshot> DeskContainers { get; set; } = new();
}

public sealed class WallpaperSettingsSnapshot
{
    public string VideoPath { get; set; } = string.Empty;
    public string PerformanceMode { get; set; } = "Balanced";
    public bool Enabled { get; set; }
}

public static class WorkspaceProfileService
{
    private static string Root => Path.Combine(AppPaths.AppDataFolder, "profiles");

    public static IReadOnlyList<string> ListProfiles()
    {
        Directory.CreateDirectory(Root);
        return Directory.GetFiles(Root, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(x => x)
            .ToArray()!;
    }

    // True αφού αποθηκεύτηκε το "Last Session" στην έξοδο (tray Exit) ΠΡΙΝ κλείσουν τα παράθυρα. Το
    // Application.Exit() κλείνει τα Forms με τυχαία σειρά — αν τα widgets έκλειναν πρώτα, το αποθηκευμένο
    // "Last Session" του MainWindow.FormClosed ήταν ΑΔΕΙΟ και τα widgets/containers χάνονταν στην επόμενη εκκίνηση.
    public static bool ExitSaveDone { get; set; }

    public static void Save(string name)
    {
        name = Sanitize(name);
        Directory.CreateDirectory(Root);
        var wallpaper = WallpaperSettings.Load();
        var profile = new WorkspaceProfile
        {
            Name = name,
            AppSettings = AppSettings.Load(),
            Wallpaper = new WallpaperSettingsSnapshot
            {
                VideoPath = wallpaper.VideoPath,
                PerformanceMode = wallpaper.PerformanceMode,
                Enabled = WallpaperHostEngine.Instance.IsEnabled
            },
            Widgets = WidgetHostEngine.Instance.GetSnapshots().ToList(),
            DeskContainers = DeskContainerHostEngine.Instance.GetSnapshots().ToList()
        };

        MotionDesk.Services.AtomicFile.WriteAllText(Path.Combine(Root, name + ".json"), JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool Load(string name)
    {
        var path = Path.Combine(Root, Sanitize(name) + ".json");
        if (!File.Exists(path)) return false;
        WorkspaceProfile? profile;
        try { profile = JsonSerializer.Deserialize<WorkspaceProfile>(AtomicFile.ReadAllText(path)); }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        if (profile == null) return false;

        // Εφαρμόζουμε ΜΟΝΟ τις ρυθμίσεις που ανήκουν στον "χώρο εργασίας" (snap). Πριν γινόταν
        // profile.AppSettings.Save() — ΟΛΟ το snapshot των ρυθμίσεων τη στιγμή που αποθηκεύτηκε το προφίλ —
        // οπότε η φόρτωση π.χ. του "Gaming" άλλαζε σιωπηλά γλώσσα, θέμα, διαφάνεια, φάκελο μετατροπής κ.ά. στις
        // παλιές τιμές και έσβηνε ό,τι είχε αλλάξει ο χρήστης έκτοτε.
        var currentApp = AppSettings.Load();
        currentApp.GridSnap = profile.AppSettings.GridSnap;
        currentApp.SnapThreshold = profile.AppSettings.SnapThreshold;
        currentApp.GridSize = profile.AppSettings.GridSize;
        currentApp.Save();
        var wallpaper = WallpaperSettings.Load();
        wallpaper.VideoPath = profile.Wallpaper.VideoPath ?? string.Empty;
        wallpaper.PerformanceMode = profile.Wallpaper.PerformanceMode ?? "Balanced";
        wallpaper.Save();

        WidgetSnapEngine.EnableGridSnap = profile.AppSettings.GridSnap;
        WidgetSnapEngine.SnapThreshold = Math.Clamp(profile.AppSettings.SnapThreshold, 5, 50);
        WidgetSnapEngine.GridSize = Math.Clamp(profile.AppSettings.GridSize, 5, 100);

        WidgetHostEngine.Instance.RestoreSnapshots(profile.Widgets ?? new List<WidgetSnapshot>());
        // ΝΕΟ: τα DeskContainers τώρα θυμούνται/επαναφέρονται σε κάθε session ακριβώς όπως τα
        // widgets — ζητήθηκε ρητά ("widgets kai deskcontainers ... σε κάθε session του υπολογιστή
        // να θυμούνται τα windows"). Πριν, μόνο τα widgets ήταν μέρος του "Last Session" profile.
        if (!FencesEngine.Enabled)         DeskContainerHostEngine.Instance.RestoreSnapshots(profile.DeskContainers ?? new List<DeskContainerSnapshot>());
        // Τα DeskZones layouts ΔΕΝ είναι πλέον μέρος του workspace profile — ζουν ανεξάρτητα στο
        // δικό τους αρχείο (ZoneLayoutStore), ακριβώς όπως τα πραγματικά FancyZones layouts δεν
        // αλλάζουν όταν εναλλάσσεις "προφίλ" εργασίας.

        WallpaperHostEngine.Instance.SetPerformanceMode(wallpaper.PerformanceMode);
        if (profile.Wallpaper.Enabled) WallpaperHostEngine.Instance.Enable();
        else WallpaperHostEngine.Instance.Disable();
        return true;
    }

    public static void Delete(string name)
    {
        var path = Path.Combine(Root, Sanitize(name) + ".json");
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((value ?? "Default").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Default" : clean;
    }
}
