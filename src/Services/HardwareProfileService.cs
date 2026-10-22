using System;
using System.Management;
using System.Runtime.InteropServices;

namespace MotionDesk.Services
{
    public enum HardwareTier { Low, Balanced, High }

    // Runtime ανίχνευση ΙΚΑΝΟΤΗΤΑΣ του μηχανήματος (RAM + πυρήνες CPU + τύπος GPU) — ζητήθηκε ρητά
    // στο v2.0 handoff (Φάση 1, §Α). Ξεχωριστή έννοια από το ήδη υπάρχον PerformanceModeManager
    // (AppSettings.cs): εκείνο απαντά "ποια είναι η τρέχουσα κατάσταση ρεύματος ΤΩΡΑ" (μπαταρία/
    // AC/χαμηλή μπαταρία), αυτό απαντά "πόσο δυνατό είναι ΑΥΤΟ το μηχάνημα σε γενικές γραμμές" — οι
    // δύο άξονες συνδυάζονται στο PerformanceModeManager.GetRecommendedMode() αντί να υπάρχουν ως
    // δύο παράλληλα, ασύνδετα συστήματα (ρητή απαίτηση του CLAUDE_TASK.md).
    //
    // Σκόπιμα ΔΕΝ χρησιμοποιεί το LibreHardwareMonitor/GpuMonitorService (ήδη υπάρχει για το System
    // Monitor widget) — το πρώτο του Computer.Open() είναι γνωστά αργό (~1s+, γι' αυτό ήδη ζεσταίνεται
    // ασύγχρονα αλλού) και η ανίχνευση προφίλ πρέπει να είναι γρήγορη, ιδίως σε αδύναμα μηχανήματα.
    // RAM μέσω GetPhysicallyInstalledSystemMemory (kernel32, ακαριαίο), πυρήνες μέσω
    // Environment.ProcessorCount (ήδη έτοιμο), GPU μέσω μία ελαφριά WMI ερώτηση μόνο για το όνομα
    // (Win32_VideoController· μόνο μεταδεδομένα, όχι sensor negotiation — ελαφρύ, όχι σαν τα βαριά
    // ερωτήματα που προειδοποιεί να αποφευχθούν το ίδιο το handoff doc, §Ι/E4).
    public static class HardwareProfileService
    {
        public static event Action<HardwareTier>? ProfileChanged;

        private static readonly object _sync = new();
        private static HardwareTier? _cached;

        public static HardwareTier CurrentTier
        {
            get { lock (_sync) { return _cached ??= Compute(); } }
        }

        // null = Auto (ανίχνευση). Αποθηκεύεται στο AppSettings ώστε να επιζεί μεταξύ επανεκκινήσεων.
        public static void SetOverride(HardwareTier? tier)
        {
            var app = AppSettings.Load();
            app.HardwareProfileOverride = tier?.ToString();
            app.Save();
            Refresh();
        }

        public static HardwareTier? GetOverride()
        {
            var raw = AppSettings.Load().HardwareProfileOverride;
            return !string.IsNullOrEmpty(raw) && Enum.TryParse<HardwareTier>(raw, out var t) ? t : null;
        }

        // Ξαναϋπολογίζει (π.χ. μετά από αλλαγή override στις ρυθμίσεις) και ειδοποιεί μόνο αν άλλαξε
        // πραγματικά το αποτέλεσμα.
        public static void Refresh()
        {
            var next = Compute();
            bool changed;
            lock (_sync) { changed = _cached != next; _cached = next; }
            if (changed) ProfileChanged?.Invoke(next);
        }

        private static HardwareTier Compute()
        {
            var forced = GetOverride();
            if (forced.HasValue) return forced.Value;

            return Classify(GetTotalRamMb(), Environment.ProcessorCount, HasDedicatedGpu());
        }

        // Καθαρή συνάρτηση (χωρίς I/O) ώστε να δοκιμάζεται με προσομοιωμένα προφίλ υλικού, π.χ. ένα
        // laptop 300-400€ (8GB RAM, 2-4 λογικοί πυρήνες, ενσωματωμένη GPU) -> Low.
        internal static HardwareTier Classify(long ramMb, int cores, bool dedicatedGpu)
        {
            int score = 0;
            if (ramMb >= 8000) score++;
            if (ramMb >= 16000) score++;
            if (cores >= 6) score++;
            if (cores >= 12) score++;
            if (dedicatedGpu) score++;

            if (score <= 1) return HardwareTier.Low;
            if (score >= 4) return HardwareTier.High;
            return HardwareTier.Balanced;
        }

        // Hooks προσομοίωσης για δοκιμές σε "αδύναμο" μηχάνημα (scripts/LowEndSim.ps1): ΔΕΝ υπάρχουν
        // στο κανονικό UI και δεν επηρεάζουν τίποτα αν οι μεταβλητές περιβάλλοντος δεν οριστούν.
        private static long GetTotalRamMb()
        {
            if (long.TryParse(Environment.GetEnvironmentVariable("OPSIS_SIM_RAM_MB"), out long simulated) && simulated > 0) return simulated;
            try
            {
                if (GetPhysicallyInstalledSystemMemory(out ulong kb)) return (long)(kb / 1024);
            }
            catch (Exception) { }
            return 8000; // αποτυχία ανίχνευσης -> υπόθεση μεσαίου μηχανήματος, όχι Low (αποφυγή
                         // απενεργοποίησης εφέ σε ένα κανονικό μηχάνημα λόγω αποτυχημένης ανίχνευσης)
        }

        // Ευρετική, όχι απόλυτη: ένα όνομα GPU που ΔΕΝ μοιάζει με γνωστή ενσωματωμένη κάρτα
        // θεωρείται "αφοσιωμένη" (NVIDIA/AMD τυπικά). Γνωστός περιορισμός: νεότερες ενσωματωμένες
        // (Intel Arc κ.λπ.) μπορεί να ταξινομηθούν λάθος ως "όχι αφοσιωμένη" — αποδεκτό για μια
        // πρώτη, απλή εκδοχή· η βαθμολογία ούτως ή άλλως δεν εξαρτάται μόνο από αυτό το ένα σημείο.
        private static bool HasDedicatedGpu()
        {
            string? simGpu = Environment.GetEnvironmentVariable("OPSIS_SIM_DGPU");
            if (simGpu == "0") return false;
            if (simGpu == "1") return true;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject mo in searcher.Get())
                {
                    string name = mo["Name"] as string ?? "";
                    if (name.Length == 0) continue;
                    bool knownIntegrated = name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase);
                    if (!knownIntegrated) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
    }
}
