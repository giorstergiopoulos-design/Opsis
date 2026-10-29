using System;
using System.Windows.Forms;
using MotionDesk.Widgets;

namespace MotionDesk
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Ελάχιστος elevated "helper" branch — καμία θυρίδα UI, μόνο μία εγγραφή HKLM και
            // έξοδος. Ενεργοποιείται ΜΟΝΟ όταν το κύριο (μη-elevated) UI ξεκινήσει ξανά την ίδια
            // exe με Verb="runas" (βλ. LockScreenEngine.TrySetLockScreenImageElevated) — έτσι δεν
            // χρειάζεται ολόκληρη η εφαρμογή να τρέχει elevated για μία μόνο ενέργεια.
            if (args.Length == 2 && args[0] == "--set-lockscreen")
            {
                try { LockScreenEngine.WriteLockScreenRegistry(args[1]); } catch (Exception) { }
                return;
            }

            // Ίδιο ελάχιστο elevated helper branch, για την αντίστροφη ενέργεια: διαγραφή των τιμών
            // ώστε τα Windows να ξαναδείχνουν την προεπιλεγμένη/δική τους επιλεγμένη οθόνη
            // κλειδώματος αντί αυτή που όρισε το Opsis (βλ. LockScreenEngine.TryResetLockScreenElevated).
            if (args.Length == 1 && args[0] == "--reset-lockscreen")
            {
                try { LockScreenEngine.ClearLockScreenRegistry(); } catch (Exception) { }
                return;
            }

            // Headless self-test (v2.0 handoff, Φάση 1/9) — ΔΕΝ περνάει από το single-instance Mutex
            // ούτε αρχικοποιεί UI/tray, ώστε να μπορεί να τρέξει από ένα build script ακόμα κι αν
            // μια κανονική εκτέλεση του Opsis είναι ήδη ανοιχτή.
            if (args.Length == 1 && args[0] == "--run-selftest")
            {
                Environment.Exit(Services.SelfTestRunner.Run());
                return;
            }

            // Διαγνωστικά επιφάνειας εργασίας/wallpaper/καιρού (βλ. DesktopDiagnostics).
            if (args.Length >= 1 && args[0] == "--diag")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.Run(args.Length > 1 && args[1] == "--show-desktop"));
                return;
            }

            if (args.Length == 2 && args[0] == "--test-transparency")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestTransparency(args[1]));
                return;
            }
            if (args.Length == 3 && args[0] == "--test-widgets")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestWidgets(args[1], args[2]));
                return;
            }
            if (args.Length == 3 && args[0] == "--test-deskstrip")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestDeskStrip(args[1], args[2]));
                return;
            }
            if (args.Length == 2 && args[0] == "--test-wallpaper-icons")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestWallpaperIcons(args[1]));
                return;
            }
            if (args.Length == 3 && args[0] == "--test-audio-detect")        // --test-audio-detect <seconds> <logfile>: polls the other-app audio detector
            {
                using var det = new Services.OtherAudioDetector();
                var lines = new System.Collections.Generic.List<string>();
                var end = DateTime.UtcNow.AddSeconds(int.Parse(args[1]));
                while (DateTime.UtcNow < end) { lines.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " otherAppPlaying=" + det.IsOtherAppPlaying()); System.Threading.Thread.Sleep(300); }
                System.IO.File.WriteAllLines(args[2], lines);
                Environment.Exit(0);
                return;
            }
            if (args.Length >= 1 && args[0] == "--test-iconhide")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestIconHide(args.Length > 1 ? args[1] : ""));
                return;
            }
            if (args.Length == 2 && args[0] == "--render-sysmon")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.RenderSysmon(args[1]));
                return;
            }

            // Single-instance: δύο ταυτόχρονες εκτελέσεις θα πάλευαν για τα ίδια global hotkeys,
            // θα έφτιαχναν διπλά wallpaper windows/tray icons και θα έγραφαν τα ίδια config αρχεία.
            // An isolated data folder (OPSIS_APPDATA, used by automated tests) gets its own mutex, so a test run never collides with the user's real instance.
            string isolatedData = Environment.GetEnvironmentVariable("OPSIS_APPDATA") ?? "";
            string mutexName = isolatedData.Length == 0 ? @"Local\Opsis.SingleInstance" : @"Local\Opsis.SingleInstance." + (uint)isolatedData.ToLowerInvariant().GetHashCode();
            using var singleInstance = new System.Threading.Mutex(true, mutexName, out bool isFirst);
            if (!isFirst) return;

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);
            Services.RotatingLogger.HookUnobservedTaskExceptions();

            // Safe mode (v2.0 handoff, Φάση 1 §S) — πρέπει να αξιολογηθεί ΟΣΟ ΤΟ ΔΥΝΑΤΟΝ
            // νωρίτερα, πριν αρχικοποιηθεί οτιδήποτε βαρύ (wallpaper, plugins). Το
            // TrayApplicationContext διαβάζει το Services.SafeModeService.IsSafeMode παρακάτω.
            Services.SafeModeService.EvaluateAtStartup();
            Services.RotatingLogger.Write($"Startup: SafeMode={Services.SafeModeService.IsSafeMode} (shift={Services.SafeModeService.TriggeredByShift}, failedStartups={Services.SafeModeService.ConsecutiveFailedStartups})");
            Services.RotatingLogger.Write($"Hardware: tier={Services.HardwareProfileService.CurrentTier}, cores={Environment.ProcessorCount}");

            // Το .NET 5+ δεν περιλαμβάνει πλέον τις legacy κωδικοσελίδες (π.χ. 737 για ελληνικά
            // OEM/console) by default — χωρίς αυτή την εγγραφή, Encoding.GetEncoding(737) πετάει
            // NotSupportedException. Χρειάζεται για σωστή ανάγνωση της εξόδου κονσόλας εργαλείων
            // σαν το powercfg (SystemControlService.GetPowerPlans) σε μη-αγγλικά Windows.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Sync η Taskbar translucency (GlassBlur.SetTaskbarTranslucency) με την τρέχουσα τιμή του
            // Windows "Εφέ διαφάνειας" στην εκκίνηση — χωρίς αυτό, το blur της Taskbar θα εφαρμοζόταν
            // μόνο αφού ο χρήστης σβήσει/ξανατσεκάρει το checkbox στις Ρυθμίσεις μία φορά.
            try { MotionDesk.UI.GlassBlur.SetTaskbarTranslucency(Services.WindowsSystemSettings.GetTransparencyEffectsEnabled()); } catch (Exception) { }

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }

        private static DateTime _lastErrorDialog = DateTime.MinValue;

        private static void ReportError(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                var logPath = System.IO.Path.Combine(Services.AppPaths.AppDataFolder, "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\n\n");
            }
            catch (Exception) { }
            Services.RotatingLogger.WriteException("UnhandledException", ex);

            // Ένα exception μέσα σε timer tick επαναλαμβάνεται κάθε tick — χωρίς όριο, ο χρήστης έβλεπε
            // ατέλειωτη αλληλουχία modal διαλόγων. Ένας διάλογος ανά 30s αρκεί (όλα καταγράφονται στο crash.log).
            if ((DateTime.Now - _lastErrorDialog).TotalSeconds < 30) return;
            _lastErrorDialog = DateTime.Now;

            MessageBox.Show(string.Format(MotionDesk.Services.LocalizationManager.T("Dialog.UnhandledErrorFormat"), ex.Message), "Opsis",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
