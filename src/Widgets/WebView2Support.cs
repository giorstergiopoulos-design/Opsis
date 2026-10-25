using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MotionDesk.Widgets
{
    /// <summary>Creates one shared WebView2 environment for all rich-content surfaces.</summary>
    public static class WebView2Support
    {
        // ΟΧΙ Lazy<Task>: ένα αποτυχημένο Task (π.χ. λείπει το WebView2 Runtime) θα έμενε cached για όλη τη
        // συνεδρία και το wallpaper δεν θα ανέκαμπτε ποτέ, ούτε αφού ο χρήστης εγκαταστήσει το runtime.
        private static Task<CoreWebView2Environment>? _environment;
        private static readonly object _envLock = new();

        public static Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            lock (_envLock)
            {
                if (_environment == null || _environment.IsFaulted || _environment.IsCanceled)
                    _environment = CreateEnvironmentAsyncCore();
                return _environment;
            }
        }

        /// <summary>Έκδοση WebView2 Runtime ή null αν λείπει (για διαγνωστικά και μηνύματα σφάλματος).</summary>
        public static string? GetRuntimeVersion()
        {
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception) { return null; }
        }

        // Σκληραγώγηση των WebView2 που φιλοξενούν ΜΟΝΟ τοπικές σελίδες της εφαρμογής (wallpaper/widgets) και
        // εκθέτουν host objects (WallpaperBridge/WidgetBridge): κανένα link/σφάλμα δεν πρέπει να μπορεί να
        // φορτώσει εξωτερικό περιεχόμενο στο ίδιο WebView (θα είχε πρόσβαση στα host objects), ούτε να ανοίξει
        // νέο παράθυρο ή DevTools/μενού περιβάλλοντος πάνω στην επιφάνεια εργασίας.
        private static readonly Queue<DateTime> _failures = new();

        public static void Harden(CoreWebView2 core)
        {
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            // Crash του GPU/renderer process: χωρίς handler το wallpaper έμενε παγωμένο/λευκό μέχρι επανεκκίνηση.
            core.ProcessFailed += (_, e) =>
            {
                MotionDesk.Services.AttachLog.Write($"WebView2 ProcessFailed: {e.ProcessFailedKind} reason={e.Reason} exit={e.ExitCode}");
                // A frozen (heavy pause) or just-thawed renderer reports itself as unresponsive: that is not a crash.
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive && ProcessFreezer.IsFrozenOrRecent) return;
                // Retry budget: a renderer that keeps dying (bad driver, corrupt page) must not make Opsis reload in a loop.
                var now = DateTime.UtcNow;
                lock (_failures) { _failures.Enqueue(now); while (_failures.Count > 0 && (now - _failures.Peek()).TotalSeconds > 120) _failures.Dequeue(); if (_failures.Count > 4) { MotionDesk.Services.AttachLog.Write("WebView2 failure budget exceeded - not reloading"); return; } }
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                    or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
                    or CoreWebView2ProcessFailedKind.GpuProcessExited)
                {
                    try { core.Reload(); } catch (Exception) { }
                }
            };
        }

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsyncCore()
        {
            // (LocalApplicationData, όχι ApplicationData/AppPaths.AppDataFolder: cache δεδομένα του
            // WebView2 runtime, όχι ρυθμίσεις χρήστη — σωστά τοπικό/μη-roaming, ξαναδημιουργείται
            // αβλαβώς αν χαθεί, οπότε δεν χρειάζεται μετάβαση από το παλιό όνομα.)
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Opsis", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            // Λιγότερη μνήμη/CPU σε αδύναμα συστήματα: χωρίς background networking/sync/translate, χωρίς renderer
            // backgrounding (το wallpaper είναι πίσω από όλα, τα Windows θα το "κοιμίζανε").
            var options = new CoreWebView2EnvironmentOptions(
                "--disable-features=msSmartScreenProtection,Translate,msEdgeSync --disable-background-networking " +
                "--disable-renderer-backgrounding --disable-backgrounding-occluded-windows --autoplay-policy=no-user-gesture-required");
            return await CoreWebView2Environment.CreateAsync(null, userDataFolder, options).ConfigureAwait(true);
        }
    }
}
