using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MotionDesk.Services
{
    // Safe mode (v2.0 handoff, Φάση 1 §S, ΥΠΟΧΡΕΩΤΙΚΟ) — αν η εφαρμογή κρασάρει στην εκκίνηση ≥2
    // φορές στη σειρά, ή ο χρήστης κρατάει Shift κατά την εκκίνηση, η επόμενη εκκίνηση γίνεται
    // χωρίς wallpaper/plugins/αυτόματη επαναφορά "Last Session" — μόνο το βασικό UI. Ο μετρητής
    // μηδενίζεται μετά από 30s επιτυχούς λειτουργίας (βλ. MarkStartupHealthy, καλείται από ένα
    // one-shot Timer στο TrayApplicationContext).
    public static class SafeModeService
    {
        private static string CounterPath => Path.Combine(AppPaths.AppDataFolder, "startup_counter.txt");

        public static bool IsSafeMode { get; private set; }
        public static bool TriggeredByShift { get; private set; }
        public static int ConsecutiveFailedStartups { get; private set; }

        // Καλείται ΟΣΟ ΤΟ ΔΥΝΑΤΟΝ ΝΩΡΙΤΕΡΑ στο Main() — πριν αρχικοποιηθεί οτιδήποτε βαρύ (wallpaper,
        // plugins). Αυξάνει τον μετρητή ΠΡΙΝ ξέρουμε αν αυτή η εκτέλεση θα πετύχει· αν η εφαρμογή
        // κρασάρει πριν προλάβει να καλέσει MarkStartupHealthy(), ο μετρητής μένει αυξημένος και η
        // ΕΠΟΜΕΝΗ εκκίνηση το βλέπει.
        public static void EvaluateAtStartup()
        {
            TriggeredByShift = IsShiftHeld();
            ConsecutiveFailedStartups = ReadCounter();
            IsSafeMode = TriggeredByShift || ConsecutiveFailedStartups >= 2;
            WriteCounter(ConsecutiveFailedStartups + 1);
        }

        public static void MarkStartupHealthy() => WriteCounter(0);

        private static bool IsShiftHeld() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

        private static int ReadCounter()
        {
            try { return int.TryParse(File.ReadAllText(CounterPath).Trim(), out var n) ? n : 0; }
            catch (Exception) { return 0; }
        }

        private static void WriteCounter(int n)
        {
            try { Directory.CreateDirectory(AppPaths.AppDataFolder); File.WriteAllText(CounterPath, n.ToString()); }
            catch (Exception) { }
        }

        private const int VK_SHIFT = 0x10;
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
