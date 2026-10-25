using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services
{
    public sealed class AppSettings
    {
        public bool GridSnap { get; set; }
        public int SnapThreshold { get; set; } = 15;
        public int GridSize { get; set; } = 20;
        public bool AutoPerformanceMode { get; set; } = true;
        // Pause conditions (AppActivity): the wallpaper and the widgets stop rendering while the session is locked, in a remote (RDP) session or in Battery Saver.
        public bool PauseOnLock { get; set; } = true;
        public bool PauseOnRemoteSession { get; set; } = true;
        public bool PauseOnBatterySaver { get; set; } = true;
        // After a pause lasts 15 s the WebView2 process tree is suspended (0% CPU/GPU); resumed instantly when the pause ends.
        public bool WallpaperHeavyPause { get; set; } = true;
        public string WallpaperPerformanceMode { get; set; } = "Balanced";
        // Χειροκίνητο override της ανιχνευμένης ικανότητας υλικού (HardwareProfileService) — "" /
        // null = Auto (ανίχνευση RAM/πυρήνων/GPU). Τιμές: "Low" | "Balanced" | "High" (HardwareTier).
        // Ξεχωριστή έννοια από το WallpaperPerformanceMode παραπάνω: αυτό απαντά "πόσο δυνατό είναι
        // αυτό το μηχάνημα", όχι "τι θέλει ο χρήστης να δει τώρα" — βλ. PerformanceModeManager.
        public string? HardwareProfileOverride { get; set; }
        public bool ShowSystemMonitor { get; set; }
        public bool RestoreLastSession { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        // DeskZones: applications (process names, without .exe) whose windows are never snapped (e.g. media players, games)
        public List<string> ZoneExcludedApps { get; set; } = new();
        // Notify (tray balloon) when CPU or memory stays above 90% for a minute
        public bool UsageAlertsEnabled { get; set; } = false;
        // Check GitHub for a newer release once a day / show the changelog once after an update
        public bool UpdateCheckEnabled { get; set; } = true;
        public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
        // Update mechanism βάσει GearWin (ήδη portαρισμένο/δοκιμασμένο στο OpsisNewsCrawler): ένα
        // tray balloon ανά ΈΚΔΟΣΗ, όχι ανά check — θυμόμαστε το tag που ειδοποιήθηκε τελευταία.
        public string LastNotifiedUpdateTag { get; set; } = "";
        public string LastRunVersion { get; set; } = "";
        public bool StartWallpaperWithWindows { get; set; }
        public bool EnableAnimations { get; set; } = true;
        public string ThemeMode { get; set; } = "Follow"; // Follow | Light | Dark
        // Seelen UI-inspired: αντί για σταθερή παλέτα accent χρωμάτων, ακολούθησε το πραγματικό
        // accent color που έχει επιλέξει ο χρήστης στα Windows (Ρυθμίσεις > Εξατομίκευση > Χρώματα).
        public bool FollowWindowsAccent { get; set; } = false;
        public string Language { get; set; } = "Follow";  // Follow | el | en
        public double WindowOpacity { get; set; } = 1.0;  // 0.6 - 1.0, εφαρμόζεται στο κύριο παράθυρο
        public double DarkIntensity { get; set; } = 0.5;  // 0 (πιο ανοιχτό σκούρο) - 1 (σχεδόν μαύρο), 0.5 = η αρχική βάση
        public bool UiSoundsEnabled { get; set; }
        public bool SidebarCollapsed { get; set; }

        // Μηχανισμός plugin (φάκελος "plugins"): false = κανένα plugin δεν φορτώνεται (π.χ. για διάγνωση προβλήματος).
        public bool PluginsEnabled { get; set; } = true;
        // Προσωπικά presets του ισοσταθμιστή 10 ζωνών (Audio Enhancement) + το τελευταίο ενεργό.
        public List<AudioEqProfile> CustomEqProfiles { get; set; } = new();
        public string? ActiveEqProfile { get; set; }
        // Ζητήθηκε ρητά: ξεχωριστό ενεργό/ανενεργό ΑΝΑ plugin (π.χ. το News Crawler), πέρα από τον
        // γενικό διακόπτη PluginsEnabled παραπάνω. Αποθηκεύει το Id όσων ο χρήστης έχει απενεργοποιήσει.
        public HashSet<string> DisabledPluginIds { get; set; } = new();

        // Αφαίρεση plugin: ο φάκελός του δεν μπορεί να διαγραφεί όσο το Opsis τρέχει (το .dll του
        // είναι ήδη φορτωμένο, Windows file lock), οπότε ο φάκελος μπαίνει εδώ και σαρώνεται/
        // διαγράφεται στην ΕΠΟΜΕΝΗ εκκίνηση, πριν φορτωθεί κανένα plugin (βλ. PluginHost.StartAll).
        public List<string> PendingPluginRemovals { get; set; } = new();

        // Φάκελος όπου αποθηκεύονται τα μετατρεπόμενα .wmv->.mp4 (κενό = προεπιλογή στο AppData).
        public string? WmvConversionOutputDir { get; set; }

        // MD roadmap item: η οθόνη κλειδώματος/υποδοχής να μπορεί να γίνει ακριβές στιγμιότυπο του
        // τρέχοντος ζωντανού wallpaper (waves/βίντεο + overlay), όχι μια τυχαία επιλεγμένη εικόνα.
        // Το LockScreenSnapshotPath είναι το αρχείο που γράφει το στιγμιότυπο· όταν το
        // LockScreenFollowsWallpaper είναι true, το Opsis το ξανατραβάει αυτόματα σε κάθε αλλαγή
        // wallpaper ώστε η οθόνη κλειδώματος να μένει ταιριαστή (βλ. LockScreenEngine, WallpaperHostEngine).
        public bool LockScreenFollowsWallpaper { get; set; } = false;
        public string? LockScreenSnapshotPath { get; set; }

        // Ανεξάρτητο θέμα για widgets/DeskContainers από αυτό της ίδιας της εφαρμογής — ζητήθηκε
        // ρητά "αν η εφαρμογή είναι σε φωτεινό θέμα, τα widgets/containers να μπορούν είτε να
        // ακολουθούν τα windows είτε να έχουν διαφορετικά χρώματα και από το σύστημα και από την
        // εφαρμογή". "App" = ακολουθεί το θέμα της εφαρμογής (προηγούμενη, μοναδική συμπεριφορά),
        // "Windows" = ακολουθεί ανεξάρτητα το θέμα των Windows, "Dark"/"Light" = πάντα σταθερό.
        public string WidgetsThemeMode { get; set; } = "App";
        // "Transparent" widgets: pick the text colour from the desktop behind them, and optionally add a coloured tint (same colours as the DeskContainers).
        public bool WidgetsAutoContrast { get; set; } = true;
        public string WidgetsTintColor { get; set; } = "Black";
        public int WidgetsTintPercent { get; set; } = 0;          // 0 = fully transparent (colour-key); > 0 = tinted translucent surface

        // Ενιαίο προεπιλεγμένο μέγεθος για όλα τα νέα widgets — ζητήθηκε ρητά "όλα τα widgets να
        // έχουν το ίδιο μέγεθος και να μπορεί ο χρήστης να το αλλάζει".
        public int WidgetDefaultWidth { get; set; } = 300;
        public int WidgetDefaultHeight { get; set; } = 220;

        // "Audio Enhancement" preset (βλ. AudioSpectrumService.Presets) — κοινό ανάμεσα στο
        // πλωτό widget και στη σελίδα "Audio Enhancement" της εφαρμογής, ώστε να δείχνουν το ίδιο.
        public string AudioEnhancementPreset { get; set; } = "Flat";

        // Fences-style διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας για απόκρυψη/επαναφορά
        // εικονιδίων (DesktopIconVisibilityEngine) — υπήρχε ήδη ο μηχανισμός αλλά ήταν πάντα
        // ενεργός χωρίς καμία ρύθμιση ορατή στο χρήστη· προεπιλογή true ώστε η υπάρχουσα
        // συμπεριφορά να ΜΗΝ αλλάζει για όσους το χρησιμοποιούσαν ήδη χωρίς να το ξέρουν.
        public bool QuickHideDoubleClickEnabled { get; set; } = true;
        // v2.2.0 (ιδέες από το DesktopFences, MIT): γενικές ρυθμίσεις DeskContainers.
        // true = DeskContainers are provided by the bundled DesktopFences engine (fencesOpsisFences.exe); false = the legacy built-in containers.
        public bool UseFencesEngine { get; set; } = true;
        public bool ContainersAutoHide { get; set; } = false;          // κρύβονται μετά από αδράνεια χρήστη
        public int ContainersAutoHideSeconds { get; set; } = 20;
        public bool ContainersChameleon { get; set; } = false;         // χρώμα από το κυρίαρχο χρώμα του wallpaper
        public bool ContainersDailyBackup { get; set; } = true;        // καθημερινό backup των ρυθμίσεων/περιεχομένων
        // Profile (named container layout) that applies itself when a given display layout appears; key = DeskContainerProfiles.DisplaySignature().
        public Dictionary<string, string> ContainerProfileByDisplay { get; set; } = new();
        public bool ContainersSpotSearch { get; set; } = false;       // Ctrl+` : γρήγορη αναζήτηση/εκκίνηση (ισχύει μετά από επανεκκίνηση)
        // Ποια εικονίδια ΜΕΝΟΥΝ ορατά όταν το διπλό κλικ κρύβει την επιφάνεια εργασίας. Προεπιλογή: Ο Υπολογιστής μου, ο φάκελος του χρήστη,
        // ο Πίνακας Ελέγχου και ο Κάδος Ανακύκλωσης· μπορείς να τα αλλάξεις/να προσθέσεις ό,τι θέλεις (QuickHideExtraKept = ονόματα εικονιδίων).
        public bool QuickHideKeepThisPc { get; set; } = true;
        public bool QuickHideKeepUserFolder { get; set; } = true;
        public bool QuickHideKeepControlPanel { get; set; } = true;
        public bool QuickHideKeepRecycleBin { get; set; } = true;
        public List<string> QuickHideExtraKept { get; set; } = new();

        private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "settings.json");
        public static AppSettings Load()
        {
            try { if (File.Exists(ConfigPath)) return JsonSerializer.Deserialize<AppSettings>(MotionDesk.Services.AtomicFile.ReadAllText(ConfigPath)) ?? new AppSettings(); }
            catch (JsonException) { }
            catch (IOException) { }
            return new AppSettings();
        }
        public void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!); MotionDesk.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
            catch (IOException) { }
        }
    }

    public static class PerformanceModeManager
    {
        public static string GetRecommendedMode()
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline)
                return "Battery";
            if (power.BatteryLifePercent >= 0 && power.BatteryLifePercent < 0.30f)
                return "Low Power";

            // Συνδυάζεται με το HardwareProfileService (ικανότητα μηχανήματος) αντί να μείνει ως
            // δεύτερο, ασύνδετο σύστημα — ένα Low-End μηχάνημα μένει ελαφρύτερο ακόμα και στο
            // ρεύμα, ένα High-End μπορεί να έχει το πλουσιότερο εφέ από προεπιλογή. Σε Balanced
            // ικανότητα η συμπεριφορά μένει ακριβώς η ίδια με πριν ("Balanced").
            return HardwareProfileService.CurrentTier switch
            {
                HardwareTier.Low => "Low Power",
                HardwareTier.High => "High",
                _ => "Balanced"
            };
        }
    }
}
