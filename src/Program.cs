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

            // Single-instance: δύο ταυτόχρονες εκτελέσεις θα πάλευαν για τα ίδια global hotkeys,
            // θα έφτιαχναν διπλά wallpaper windows/tray icons και θα έγραφαν τα ίδια config αρχεία.
            using var singleInstance = new System.Threading.Mutex(true, @"Local\Opsis.SingleInstance", out bool isFirst);
            if (!isFirst) return;

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);

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

            // Ένα exception μέσα σε timer tick επαναλαμβάνεται κάθε tick — χωρίς όριο, ο χρήστης έβλεπε
            // ατέλειωτη αλληλουχία modal διαλόγων. Ένας διάλογος ανά 30s αρκεί (όλα καταγράφονται στο crash.log).
            if ((DateTime.Now - _lastErrorDialog).TotalSeconds < 30) return;
            _lastErrorDialog = DateTime.Now;

            MessageBox.Show(string.Format(MotionDesk.Services.LocalizationManager.T("Dialog.UnhandledErrorFormat"), ex.Message), "Opsis",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
