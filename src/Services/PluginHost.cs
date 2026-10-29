using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Win32;
using MotionDesk.UI;
using OpsisNewsCrawler.Plugin.Abstractions;

namespace MotionDesk.Services
{
    // Μηχανισμός plugin του Opsis - Desktop Customization.
    //
    // Ένα plugin είναι ένας φάκελος μέσα σε έναν από τους φακέλους "plugins" με ένα *Plugin.dll που περιέχει μια κλάση
    // που υλοποιεί IOpsisNewsCrawlerPlugin (το contract βρίσκεται στο libs\OpsisNewsCrawler.Plugin.Abstractions.dll).
    // Φάκελοι αναζήτησης (με αυτή τη σειρά):
    //   1. <φάκελος Opsis>\plugins\*\
    //   2. %AppData%\Opsis\plugins\*\
    //   3. κάθε διαδρομή στο HKCU\Software\Opsis\PluginPaths (την γράφει ο installer του plugin)
    // Κάθε plugin φορτώνεται σε δικό του AssemblyLoadContext ώστε οι εξαρτήσεις του να μη μπλέκονται με του Opsis·
    // μόνο το contract (και το framework) μοιράζονται. Ένα plugin που αποτυγχάνει ΠΟΤΕ δεν ρίχνει το Opsis —
    // το σφάλμα γράφεται στο plugins.log και το plugin παραλείπεται.
    public sealed class PluginHost
    {
        public static PluginHost Instance { get; } = new();

        private sealed class Loaded
        {
            public IOpsisNewsCrawlerPlugin Plugin = null!;
            public string Folder = "";
            public bool Started;
        }

        private readonly List<Loaded> _plugins = new();
        private bool _started;

        public IReadOnlyList<IOpsisNewsCrawlerPlugin> Plugins => _plugins.Select(p => p.Plugin).ToList();

        // Ζητήθηκε ρητά: ξεχωριστό κουμπί ενεργοποίησης/απενεργοποίησης ΓΙΑ ΚΑΘΕ plugin (π.χ. το
        // News Crawler), όχι μόνο ο γενικός διακόπτης "Φόρτωση plugins" (AppSettings.PluginsEnabled)
        // που τα αφορά όλα μαζί. Ένα απενεργοποιημένο plugin παραμένει φορτωμένο (ώστε να φαίνεται
        // στη σελίδα About με το δικό του κουμπί) αλλά ΔΕΝ τρέχει (Start/Stop) και δεν συνεισφέρει
        // στοιχεία πλοήγησης/tray όσο είναι απενεργοποιημένο.
        public bool IsPluginEnabled(string id) => !AppSettings.Load().DisabledPluginIds.Contains(id);

        public void SetPluginEnabled(string id, bool enabled)
        {
            var settings = AppSettings.Load();
            if (enabled) settings.DisabledPluginIds.Remove(id);
            else if (!settings.DisabledPluginIds.Contains(id)) settings.DisabledPluginIds.Add(id);
            settings.Save();

            var p = _plugins.FirstOrDefault(x => x.Plugin.Id == id);
            if (p != null)
            {
                try
                {
                    if (enabled && !p.Started) { PushLanguage(p.Plugin); PushTheme(p.Plugin); PushHardwareProfile(p.Plugin); p.Plugin.Start(); p.Started = true; }
                    else if (!enabled && p.Started) { p.Plugin.Stop(); p.Started = false; }
                }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' failed to {(enabled ? "start" : "stop")} after toggle: {ex.Message}"); }
            }
            Changed?.Invoke();
        }

        /// <summary>Σταματά και αφαιρεί ένα plugin από τη λίστα αμέσως (η σελίδα About/tray το χάνουν
        /// στη στιγμή), και προγραμματίζει τη διαγραφή του φακέλου του. Μια πρώτη άμεση απόπειρα
        /// γίνεται εδώ (συνήθως αποτυγχάνει όσο το .dll είναι ακόμη φορτωμένο σε αυτή τη διεργασία —
        /// Windows file lock), αλλιώς ο φάκελος μένει σε ουρά και σαρώνεται καθαρά στην επόμενη
        /// εκκίνηση, πριν προλάβει να φορτωθεί οτιδήποτε (βλ. StartAll). Δεν χρειάζεται collectible
        /// AssemblyLoadContext για αυτό — πιο απλό και αξιόπιστο από το να βασιστούμε σε unload ενώ ο
        /// host κρατάει ήδη μόνιμες αναφορές (π.χ. η συνδρομή στο NavigationRequested παρακάτω).</summary>
        public bool UninstallPlugin(string id)
        {
            var p = _plugins.FirstOrDefault(x => x.Plugin.Id == id);
            if (p == null) return false;

            try { if (p.Started) p.Plugin.Stop(); }
            catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' failed to stop before uninstall: {ex.Message}"); }

            _plugins.Remove(p);
            var settings = AppSettings.Load();
            settings.DisabledPluginIds.Remove(id);

            bool deletedNow = false;
            try { Directory.Delete(p.Folder, recursive: true); deletedNow = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!settings.PendingPluginRemovals.Contains(p.Folder, StringComparer.OrdinalIgnoreCase))
                    settings.PendingPluginRemovals.Add(p.Folder);
            }
            settings.Save();
            Changed?.Invoke();
            return deletedNow;
        }

        /// <summary>Ένα plugin ζήτησε να εμφανιστεί μία από τις σελίδες του (π.χ. δεξί κλικ στη μπάρα ειδήσεων).</summary>
        public event Action<string>? NavigationRequested;

        /// <summary>Η λίστα στοιχείων του tray menu ή του sidebar μπορεί να έχει αλλάξει.</summary>
        public event Action? Changed;

        /// <summary>Φορτώνει και ξεκινά όλα τα plugin. Πρέπει να κληθεί στο UI thread.</summary>
        public void StartAll()
        {
            if (_started) return;
            _started = true;
            SweepPendingRemovals();
            if (!AppSettings.Load().PluginsEnabled) return;

            foreach (var folder in EnumerateCandidateFolders())
            {
                try { LoadFolder(folder); }
                catch (Exception ex) { Log($"Failed to load '{folder}': {ex}"); }
            }

            var disabled = AppSettings.Load().DisabledPluginIds;
            foreach (var p in _plugins.ToList())
            {
                try
                {
                    p.Plugin.NavigationRequested += key => NavigationRequested?.Invoke(key);
                    PushLanguage(p.Plugin);
                    PushTheme(p.Plugin);
                    PushHardwareProfile(p.Plugin);
                    if (!disabled.Contains(p.Plugin.Id)) { p.Plugin.Start(); p.Started = true; }
                }
                catch (Exception ex)
                {
                    Log($"Plugin '{p.Plugin.Name}' failed to start: {ex}");
                    _plugins.Remove(p);
                }
            }

            ThemeManager.Changed += () => { foreach (var p in _plugins.ToList()) PushTheme(p.Plugin); };
            // Η γλώσσα του Opsis ισχύει και για τα plugin: αλλάζει ΚΑΙ στα δύο ταυτόχρονα.
            LocalizationManager.Changed += () => { foreach (var p in _plugins.ToList()) PushLanguage(p.Plugin); };
            HardwareProfileService.ProfileChanged += _ => { foreach (var p in _plugins.ToList()) PushHardwareProfile(p.Plugin); };
            Changed?.Invoke();
        }

        public void StopAll()
        {
            foreach (var p in _plugins.ToList())
            {
                try { p.Plugin.Stop(); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' failed to stop: {ex.Message}"); }
            }
        }

        /// <summary>Σελίδες των plugin για το sidebar, ταξινομημένες κατά Order. Ένα απενεργοποιημένο plugin δεν συνεισφέρει.</summary>
        public IReadOnlyList<PluginNavigationItem> GetNavigationItems()
        {
            var items = new List<PluginNavigationItem>();
            foreach (var p in _plugins.Where(p => p.Started))
            {
                try { items.AddRange(p.Plugin.GetNavigationItems()); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' navigation failed: {ex.Message}"); }
            }
            return items.OrderBy(i => i.Order).ToList();
        }

        /// <summary>Tray items per plugin (plugin display name -> its items) so the tray menu can group them by plugin.</summary>
        public IReadOnlyList<(string PluginName, IReadOnlyList<PluginMenuItem> Items)> GetTrayMenuGroups()
        {
            var groups = new List<(string, IReadOnlyList<PluginMenuItem>)>();
            foreach (var p in _plugins.Where(p => p.Started))
            {
                try
                {
                    var items = p.Plugin.GetTrayMenuItems().ToList();
                    if (items.Count > 0) groups.Add((p.Plugin.Name, items));
                }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' tray items failed: {ex.Message}"); }
            }
            return groups;
        }

        public IReadOnlyList<PluginMenuItem> GetTrayMenuItems()
        {
            var items = new List<PluginMenuItem>();
            foreach (var p in _plugins.Where(p => p.Started))
            {
                try { items.AddRange(p.Plugin.GetTrayMenuItems()); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' tray items failed: {ex.Message}"); }
            }
            return items;
        }

        /// <summary>Πληροφορίες "Σχετικά" όλων των plugin (ιδιότητες, βοήθεια, οδηγίες, ιστορικό, άδεια) για τη σελίδα About,
        /// μαζί με το Id τους (ώστε η σελίδα About να μπορεί να βάλει το σωστό κουμπί ενεργό/ανενεργό δίπλα στο καθένα).
        /// Τα δείχνει ΟΛΑ, ακόμη και τα απενεργοποιημένα — αλλιώς δεν θα υπήρχε πώς να τα ξανα-ενεργοποιήσεις.</summary>
        public IReadOnlyList<(string Id, PluginAboutInfo Info)> GetAboutInfos()
        {
            var infos = new List<(string, PluginAboutInfo)>();
            foreach (var p in _plugins)
            {
                try { infos.Add((p.Plugin.Id, p.Plugin.GetAboutInfo())); }
                catch (Exception ex) { Log($"Plugin '{p.Plugin.Name}' about info failed: {ex.Message}"); }
            }
            return infos;
        }

        public static string TitleOf(PluginNavigationItem item) =>
            LocalizationManager.CurrentLanguage == "el" ? item.TitleEl : item.TitleEn;

        private void PushLanguage(IOpsisNewsCrawlerPlugin plugin)
        {
            try { plugin.SetLanguage(LocalizationManager.CurrentLanguage); }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetLanguage failed: {ex.Message}"); }
        }

        // Handoff Φάση 6 (ελάχιστο): προαιρετική ικανότητα — καλείται ΜΟΝΟ αν το plugin υλοποιεί το
        // νέο, προαιρετικό IPluginProfileAware (δεν αγγίζει καθόλου το υποχρεωτικό
        // IOpsisNewsCrawlerPlugin contract, άρα κανένα υπάρχον plugin δεν σπάει).
        private void PushHardwareProfile(IOpsisNewsCrawlerPlugin plugin)
        {
            if (plugin is not OpsisNewsCrawler.Plugin.Abstractions.IPluginProfileAware aware) return;
            try { aware.SetHardwareProfile(HardwareProfileService.CurrentTier.ToString()); }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetHardwareProfile failed: {ex.Message}"); }
        }

        private void PushTheme(IOpsisNewsCrawlerPlugin plugin)
        {
            try
            {
                var t = ThemeManager.Current;
                var opacity = Math.Clamp(AppSettings.Load().WindowOpacity, 0.3, 1.0);
                plugin.SetTheme(t.IsDark, t.Background.ToArgb(), t.Surface.ToArgb(), t.Border.ToArgb(),
                    t.TextPrimary.ToArgb(), t.TextMuted.ToArgb(), t.AccentCyan.ToArgb(), opacity);
            }
            catch (Exception ex) { Log($"Plugin '{plugin.Name}' SetTheme failed: {ex.Message}"); }
        }

        // Διαγράφει τους φακέλους plugin που σημειώθηκαν για αφαίρεση σε προηγούμενη συνεδρία, ΠΡΙΝ
        // φορτωθεί οτιδήποτε άλλο σε αυτή — τώρα κανένα .dll τους δεν είναι ακόμη φορτωμένο, οπότε η
        // διαγραφή πετυχαίνει αξιόπιστα.
        private static void SweepPendingRemovals()
        {
            var settings = AppSettings.Load();
            if (settings.PendingPluginRemovals.Count == 0) return;
            var remaining = new List<string>();
            foreach (var folder in settings.PendingPluginRemovals)
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                catch (Exception ex) { Log($"Could not remove pending plugin folder '{folder}': {ex.Message}"); remaining.Add(folder); }
            }
            settings.PendingPluginRemovals = remaining;
            settings.Save();
        }

        // ---- discovery & loading ---------------------------------------------------------------

        private static IEnumerable<string> EnumerateCandidateFolders()
        {
            var roots = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "plugins"),
                Path.Combine(AppPaths.AppDataFolder, "plugins")
            };
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Opsis\PluginPaths");
                if (key != null)
                    foreach (var name in key.GetValueNames())
                        if (key.GetValue(name) is string path && path.Length > 0) roots.Add(path);
            }
            catch (Exception) { }

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.EnumerateDirectories(root)) yield return dir;
            }
        }

        private void LoadFolder(string folder)
        {
            foreach (var dll in Directory.EnumerateFiles(folder, "*Plugin.dll"))
            {
                var context = new PluginLoadContext(dll);
                var assembly = context.LoadFromAssemblyPath(dll);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || !typeof(IOpsisNewsCrawlerPlugin).IsAssignableFrom(type)) continue;
                    if (Activator.CreateInstance(type) is not IOpsisNewsCrawlerPlugin plugin) continue;

                    // Το ίδιο plugin μπορεί να βρεθεί σε δύο φακέλους (π.χ. η παλιά έκδοση που ήρθε
                    // bundled με τον installer του Opsis στο <app>\plugins, ΚΑΙ μια νεότερη που έγραψε
                    // ο δικός του αυτόματος updater στο %AppData%\Opsis\plugins). Ζητήθηκε ρητά να μην
                    // μένει "κολλημένη" η παλιά — κρατάμε πάντα τη ΝΕΟΤΕΡΗ έκδοση, όχι αυτή που απλά
                    // βρέθηκε πρώτη (ο παλιός κώδικας έκανε ακριβώς αυτό το λάθος: "first wins").
                    var existing = _plugins.FirstOrDefault(p => p.Plugin.Id == plugin.Id);
                    if (existing != null)
                    {
                        if (!IsNewerVersion(plugin.Version, existing.Plugin.Version)) continue;
                        _plugins.Remove(existing);
                    }
                    _plugins.Add(new Loaded { Plugin = plugin, Folder = folder });
                }
            }
        }

        private static bool IsNewerVersion(string candidate, string current)
        {
            if (System.Version.TryParse(candidate, out var c) && System.Version.TryParse(current, out var u)) return c > u;
            return string.CompareOrdinal(candidate, current) > 0; // fallback: ασυνήθιστη μορφή έκδοσης, τουλάχιστον σταθερή σύγκριση
        }

        private sealed class PluginLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;
            private readonly string _resolverMainAssemblyPath;

            public PluginLoadContext(string pluginPath) : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
            {
                _resolverMainAssemblyPath = Path.GetFullPath(pluginPath);
                _resolver = new AssemblyDependencyResolver(_resolverMainAssemblyPath);
            }

            protected override Assembly? Load(AssemblyName name)
            {
                // Το contract πρέπει να είναι το ίδιο Assembly instance με του host.
                // Το return null αφήνει την επίλυση στο default context και μπορεί να αποτύχει
                // όταν το contract δεν βρίσκεται στο probing path της εφαρμογής.
                var contractAssembly = typeof(IOpsisNewsCrawlerPlugin).Assembly;
                if (string.Equals(name.Name, contractAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                    return contractAssembly;

                // Πρώτα χρησιμοποιούμε το .deps.json μέσω του AssemblyDependencyResolver.
                var path = _resolver.ResolveAssemblyToPath(name);
                if (path != null && File.Exists(path))
                    return LoadFromAssemblyPath(path);

                // Fallback: αρκετά plugin distributions περιλαμβάνουν τις εξαρτήσεις δίπλα
                // στο *Plugin.dll, αλλά το .deps.json μπορεί να μην τις καταγράφει σωστά.
                var pluginDirectory = Path.GetDirectoryName(_resolverMainAssemblyPath);
                if (!string.IsNullOrEmpty(pluginDirectory))
                {
                    var candidate = Path.Combine(pluginDirectory, name.Name + ".dll");
                    if (File.Exists(candidate))
                    {
                        // Έλεγχος ταυτότητας ώστε να μη φορτωθεί DLL άλλης έκδοσης με το ίδιο όνομα.
                        try
                        {
                            var candidateName = AssemblyName.GetAssemblyName(candidate);
                            if (AssemblyName.ReferenceMatchesDefinition(name, candidateName))
                                return LoadFromAssemblyPath(candidate);
                        }
                        catch (BadImageFormatException)
                        {
                            // Δεν είναι managed assembly· αφήνουμε το runtime να συνεχίσει την επίλυση.
                        }
                    }
                }

                return null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
            }
        }

        private static void Log(string message)
        {
            try
            {
                var path = Path.Combine(AppPaths.AppDataFolder, "plugins.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTime.Now:O}] {message}\n");
            }
            catch (Exception) { }
        }
    }
}
