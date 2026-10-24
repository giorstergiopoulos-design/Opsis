using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    // DeskContainers engine: the genuine DesktopFences code (MIT, see THIRD_PARTY_NOTICES.md) built as the separate WPF
    // project fences\OpsisFences.csproj and shipped in the "fences" subfolder. Opsis itself stays WinForms and only
    // starts/stops that process; its data lives in %AppData%\Opsis\fences (OPSIS_APPDATA is inherited by the child).
    internal static class FencesEngine
    {
        private const string ProcessName = "OpsisFences";
        private static bool _exitHooked;

        public static string ExePath => Path.Combine(AppContext.BaseDirectory, "fences", ProcessName + ".exe");
        public static bool IsAvailable => File.Exists(ExePath);
        // With an isolated data folder (automated tests) only the engine this Opsis started counts, never the user's real one.
        private static readonly bool Isolated = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPSIS_APPDATA"));
        private static Process? _own;
        public static bool IsRunning => Isolated ? _own is { HasExited: false } : Process.GetProcessesByName(ProcessName).Length > 0;

        /// <summary>True when the Fences engine replaces the legacy DeskContainers (setting on and the exe is present).</summary>
        public static bool Enabled => AppSettings.Load().UseFencesEngine && IsAvailable;

        // The fences ask Opsis to open its settings page (menu "Options...") by writing bridge\to_opsis.txt.
        public static event Action? SettingsRequested;
        private static FileSystemWatcher? _toOpsis;
        private static System.Threading.SynchronizationContext? _ui;

        private static void WatchRequests()
        {
            if (_toOpsis != null) return;
            try
            {
                _ui = System.Threading.SynchronizationContext.Current;
                Directory.CreateDirectory(FencesBridge.BridgeDir);
                _toOpsis = new FileSystemWatcher(FencesBridge.BridgeDir, "to_opsis.txt") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
                void Raise() { if (_ui != null) _ui.Post(_ => SettingsRequested?.Invoke(), null); else SettingsRequested?.Invoke(); }
                _toOpsis.Created += (_, _) => Raise();
                _toOpsis.Changed += (_, _) => Raise();
            }
            catch (Exception) { }
        }

        // "1;bg;surface;hover;border;text;text2;muted;accent" (hex RRGGBB); the first field is 1 for a dark Opsis theme
        private static string ThemeSpec()
        {
            var pal = MotionDesk.UI.ThemeManager.Current;
            string Hx(System.Drawing.Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
            return string.Join(";", pal.IsDark ? "1" : "0", Hx(pal.Background), Hx(pal.Surface), Hx(pal.SurfaceHover), Hx(pal.Border), Hx(pal.TextPrimary), Hx(pal.TextSecondary), Hx(pal.TextMuted), Hx(pal.AccentCyan));
        }

        private static bool _themeHooked;

        public static void Start()
        {
            if (!IsAvailable || IsRunning) return;
            FencesMigration.MigrateLegacyContainers();     // first start only: brings the old DeskContainers over
            WatchRequests();
            if (!_themeHooked)
            {
                _themeHooked = true;      // a theme change in Opsis reaches the running engine without restarting it
                MotionDesk.UI.ThemeManager.Changed += () =>
                {
                    try { if (IsRunning) { Directory.CreateDirectory(FencesBridge.BridgeDir); File.WriteAllText(Path.Combine(FencesBridge.BridgeDir, "theme.txt"), ThemeSpec()); } } catch (IOException) { }
                };
            }
            if (!_exitHooked) { _exitHooked = true; Application.ApplicationExit += (_, _) => Stop(); }
            try
            {
                var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExePath)! };
                psi.Environment["OPSIS_PARENT_PID"] = Environment.ProcessId.ToString();
                psi.Environment["OPSIS_LANG"] = LocalizationManager.CurrentLanguage;      // the engine shows its texts in the Opsis language
                // Opsis palette for the Fences settings windows (see fences\OpsisTheme.cs).
                psi.Environment["OPSIS_THEME"] = ThemeSpec();
                // An elevated Opsis must not hand its elevation to the engine: UIPI would block drag & drop from Explorer into its windows.
                if (DeElevatedLauncher.IsElevated())
                {
                    var extra = new System.Collections.Hashtable();
                    foreach (var kv in psi.Environment) if (kv.Key.StartsWith("OPSIS_", StringComparison.OrdinalIgnoreCase)) extra[kv.Key] = kv.Value;
                    _own = DeElevatedLauncher.Start(ExePath, psi.WorkingDirectory, extra);
                }
                _own ??= Process.Start(psi);
            }
            catch (Exception ex) { RotatingLogger.Write($"Fences engine failed to start: {ex.Message}"); }
        }

        public static void Stop()
        {
            foreach (var p in Isolated ? (_own == null ? Array.Empty<Process>() : new[] { _own }) : Process.GetProcessesByName(ProcessName))
            {
                // ask politely first: the engine gives the hidden desktop icons back before it exits
                try { FencesBridge.Command("exit"); if (!p.WaitForExit(2500)) { p.Kill(); p.WaitForExit(2000); } } catch (Exception) { try { p.Kill(); } catch (Exception) { } }
                finally { p.Dispose(); }
            }
        }
    }
}
