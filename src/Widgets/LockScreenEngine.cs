using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // Φόντο οθόνης κλειδώματος — επίσημος μηχανισμός Personalization CSP
    // (HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP), ο ίδιος που
    // χρησιμοποιούν εργαλεία MDM/Intune για να ορίσουν lock screen image. Απαιτεί εγγραφή σε
    // HKLM, άρα δικαιώματα διαχειριστή — αντί να απαιτεί ολόκληρη η εφαρμογή elevated εκκίνηση,
    // ανοίγει έναν ελάχιστο elevated "helper" (η ίδια η exe, με κρυφό όρισμα γραμμής εντολών) ΜΟΝΟ
    // για αυτή τη μία εγγραφή registry — βλ. Program.cs. Σε Windows Home edition αυτό το κλειδί
    // μπορεί να αγνοηθεί από το shell (η CSP/policy μηχανή είναι πιο περιορισμένη εκεί)· η οθόνη
    // το αναφέρει καθαρά αντί να προσποιείται εγγύηση.
    internal static class LockScreenEngine
    {
        private const string PersonalizationCspKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";

        public static string GetWindowsEdition()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return key?.GetValue("EditionID") as string ?? "Unknown";
            }
            catch (Exception) { return "Unknown"; }
        }

        public static bool IsHomeEdition() =>
            GetWindowsEdition().Contains("Core", StringComparison.OrdinalIgnoreCase);

        public static string? GetCurrentLockScreenImagePath()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PersonalizationCspKey);
                return key?.GetValue("LockScreenImagePath") as string;
            }
            catch (Exception) { return null; }
        }

        // Καλείται ΜΟΝΟ από τον ελάχιστο elevated helper process (Program.cs --set-lockscreen) —
        // ποτέ απευθείας από το κύριο, μη-elevated UI thread.
        public static void WriteLockScreenRegistry(string imagePath)
        {
            using var key = Registry.LocalMachine.CreateSubKey(PersonalizationCspKey);
            key?.SetValue("LockScreenImagePath", imagePath, RegistryValueKind.String);
            key?.SetValue("LockScreenImageUrl", imagePath, RegistryValueKind.String);
            key?.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
        }

        // ΔΙΟΡΘΩΣΗ "έβαλα άλλη οθόνη κλειδώματος και δεν μπορώ να επαναφέρω την προεπιλεγμένη": όσο
        // αυτές οι 3 τιμές CSP υπάρχουν στο registry, τα Windows δείχνουν "Some settings are managed
        // by your organization" στις Ρυθμίσεις > Εξατομίκευση > Οθόνη κλειδώματος και αγνοούν ό,τι
        // επιλέξει εκεί ο χρήστης - δεν υπήρχε ΚΑΜΙΑ λειτουργία διαγραφής τους. Διαγράφει μόνο αυτές
        // τις 3 τιμές (όχι ολόκληρο το κλειδί, σε περίπτωση που κάτι άλλο - π.χ. πολιτική MDM - έχει
        // γράψει κι άλλα εκεί) ώστε τα Windows να ξαναδιαλέγουν μόνα τους.
        public static void ClearLockScreenRegistry()
        {
            using var key = Registry.LocalMachine.OpenSubKey(PersonalizationCspKey, writable: true);
            if (key == null) return;
            foreach (var name in new[] { "LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus" })
            {
                try { key.DeleteValue(name, throwOnMissingValue: false); } catch (Exception) { }
            }
        }

        // "Auto-detect failure": όχι με Scheduled Task (μοιάζει με persistence mechanism που
        // χρησιμοποιεί και malware — ρητή απόφαση να ΜΗΝ το κάνουμε έτσι). Αντ' αυτού το ίδιο το
        // Opsis, στην κανονική του εκκίνηση με τα Windows, ελέγχει αν η τρέχουσα τιμή registry
        // ταιριάζει ακόμα με το αναμενόμενο αρχείο· αν όχι (π.χ. το επανέφερε ένα Windows update ή
        // το άλλαξε ο χρήστης από το Settings app), εμφανίζει ένα pop-up με κουμπί στον χρήστη για να
        // το ενεργοποιήσει ξανά ή όχι — βλ. TrayApplicationContext.CheckLockScreenFollowsWallpaper.
        public static bool MatchesExpected(string expectedPath) =>
            string.Equals(GetCurrentLockScreenImagePath(), expectedPath, StringComparison.OrdinalIgnoreCase);

        // Καλείται από το κύριο UI — ανοίγει elevated instance της ίδιας exe μόνο για αυτή την
        // ενέργεια (εμφανίζει το πραγματικό UAC prompt των Windows), περιμένει να τελειώσει, μετά
        // διαβάζει πίσω την τιμή για να επιβεβαιώσει αν πέτυχε.
        public static bool TrySetLockScreenImageElevated(string imagePath, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string exePath = Environment.ProcessPath!;
                // ArgumentList, όχι interpolated string — ειδικά εδώ που η επανεκκίνηση γίνεται elevated
                // (Verb=runas): μια διαδρομή με " θα μπορούσε αλλιώς να σπάσει/αλλάξει τα ορίσματα της
                // ανυψωμένης διεργασίας.
                var psi = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                psi.ArgumentList.Add("--set-lockscreen");
                psi.ArgumentList.Add(imagePath);
                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                errorMessage = "cancelled"; // ο χρήστης αρνήθηκε το UAC prompt
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }

            return string.Equals(GetCurrentLockScreenImagePath(), imagePath, StringComparison.OrdinalIgnoreCase);
        }

        // Ίδιο μοτίβο με το TrySetLockScreenImageElevated, για την αντίστροφη ενέργεια.
        public static bool TryResetLockScreenElevated(out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string exePath = Environment.ProcessPath!;
                var psi = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" };
                psi.ArgumentList.Add("--reset-lockscreen");
                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                errorMessage = "cancelled";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }

            return GetCurrentLockScreenImagePath() == null;
        }
    }
}
