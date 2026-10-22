using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MotionDesk.Services
{
    // ΠΡΑΓΜΑΤΙΚΗ, system-wide ενίσχυση ήχου — ζητήθηκε ρητά μετά από έρευνα στο πραγματικό FXSound
    // repo (github.com/fxsound2/fxsound-app): επιβεβαιώθηκε (WebFetch στο ίδιο το repo) ότι ΑΚΟΜΑ
    // ΚΙ ΕΚΕΙ η πραγματική επεξεργασία system-wide ήχου γίνεται μέσω ΞΕΧΩΡΙΣΤΟΥ, ΚΛΕΙΣΤΟΥ virtual
    // audio driver ("FxSound Audio Enhancer virtual audio driver") — ΔΕΝ περιλαμβάνεται καν στο
    // ίδιο το open-source repo τους. Ένα τέτοιο driver χρειάζεται kernel-mode driver signing
    // (ακόμα πιο απαιτητικό από απλό code-signing certificate) — εκτός εμβέλειας εδώ.
    //
    // Η ΠΡΑΓΜΑΤΙΚΗ, εφικτή λύση (βρέθηκε μέσω έρευνας παρόμοιων GitHub projects, π.χ.
    // github.com/psidex/EACS): το Equalizer APO (equalizerapo.sourceforge.io) είναι ένα ΗΔΗ
    // δωρεάν, ανοιχτού κώδικα, ήδη-υπογεγραμμένο, ευρέως-εμπιστευμένο (εκατομμύρια downloads)
    // πραγματικό Windows Audio Processing Object — τρέχει ΜΕΣΑ στο audiodg.exe του ίδιου του
    // Windows, system-wide, ΧΩΡΙΣ κανένα virtual driver. Ελέγχεται απλά γράφοντας το δικό του
    // config.txt (ίδιο ακριβώς μοτίβο ενσωμάτωσης με FFmpeg/7-Zip σε αυτό το project: ανίχνευση
    // εξωτερικού, ήδη-λυμένου εργαλείου αντί να ξαναγραφτεί ο τροχός). Δεν αγγίζουμε το υπόλοιπο
    // config.txt του χρήστη — προσθέτουμε ΜΟΝΟ μία γραμμή "Include: MotionDesk.txt" (αν λείπει)
    // και γράφουμε το δικό μας preset σε ΞΕΧΩΡΙΣΤΟ αρχείο.
    public static class EqualizerApoService
    {
        /// <summary>Μόνο για δοκιμές: ανακατεύθυνση σε προσωρινό φάκελο αντί για το πραγματικό Equalizer APO.</summary>
        internal static string? ConfigDirOverride;
        private static string ConfigDir => ConfigDirOverride ?? @"C:\Program Files\EqualizerAPO\config";
        private static string ConfigFile => Path.Combine(ConfigDir, "config.txt");
        private static string MotionDeskIncludeFile => Path.Combine(ConfigDir, "Opsis.txt");
        private const string IncludeLine = "Include: Opsis.txt";
        // Από τη μετονομασία "MotionDesk" -> "Opsis": ο παλιός include-γραμμή/αρχείο καθαρίζονται
        // στο επόμενο ApplyPreset ώστε να μη μείνει ένα "ορφανό" παλιό preset ενεργό στο σύστημα
        // του χρήστη για πάντα (το config.txt του EqualizerAPO είναι εξωτερικό, κοινόχρηστο αρχείο —
        // δεν το διαβάζει κανείς άλλος μέσα στην εφαρμογή για να το μεταναστεύσει αλλού).
        private static string LegacyIncludeFile => Path.Combine(ConfigDir, "MotionDesk.txt");
        private const string LegacyIncludeLine = "Include: MotionDesk.txt";

        public static bool IsInstalled => Directory.Exists(ConfigDir) && File.Exists(ConfigFile);

        public static void OpenDownloadPage()
        {
            try { Process.Start(new ProcessStartInfo("https://equalizerapo.sourceforge.io/") { UseShellExecute = true }); }
            catch { }
        }

        // Γράφει το preset ως πραγματικά parametric EQ filters (Equalizer APO syntax) στο δικό
        // μας Opsis.txt, και εγγυάται ότι το config.txt το συμπεριλαμβάνει (Include) — δεν
        // πειράζει τίποτα άλλο που έχει ήδη ο χρήστης στο config.txt του.
        public static (bool Success, string Message) ApplyPreset(string presetName)
        {
            if (!IsInstalled) return (false, LocalizationManager.T("Apo.NotFound"));

            string filters = presetName switch
            {
                "Bass Boost" => "Preamp: -3 dB\nFilter 1: ON LSC Fc 120 Hz Gain 7.0 dB Q 0.70\n",
                "Treble Boost" => "Preamp: -3 dB\nFilter 1: ON HSC Fc 6500 Hz Gain 6.0 dB Q 0.70\n",
                "Vocal Boost" => "Preamp: -2 dB\nFilter 1: ON PK Fc 2500 Hz Gain 5.0 dB Q 1.0\n",
                "Loudness" => "Preamp: -4 dB\nFilter 1: ON LSC Fc 100 Hz Gain 6.0 dB Q 0.70\nFilter 2: ON HSC Fc 9000 Hz Gain 5.0 dB Q 0.70\n",
                _ => "Preamp: 0 dB\n", // "Flat"
            };

            return WriteInclude(filters);
        }

        /// <summary>Εφαρμόζει ένα προφίλ 10 ζωνών (ή εισαγμένο από το Waveframe / αρχείο Equalizer APO) σε ΟΛΟ το σύστημα.</summary>
        public static (bool Success, string Message) ApplyProfile(AudioEqProfile profile)
        {
            if (!IsInstalled) return (false, LocalizationManager.T("Apo.NotFound"));
            return WriteInclude(profile.ToEqualizerApo());
        }

        // Κοινή εγγραφή: δικό μας Opsis.txt + (μία φορά) η γραμμή Include στο config.txt. Δεν πειράζει τίποτα άλλο του χρήστη.
        private static (bool Success, string Message) WriteInclude(string filters)
        {
            try
            {
                File.WriteAllText(MotionDeskIncludeFile,
                    "// Δημιουργήθηκε από το Opsis (Audio Enhancement) — μη το επεξεργάζεστε χειροκίνητα, αντικαθίσταται σε κάθε αλλαγή preset.\n" + filters);

                string existing = File.ReadAllText(ConfigFile);
                var rawLines = existing.Split('\n').ToList();
                // Αφαιρεί ΜΟΝΟ τη γραμμή include του παλιού ονόματος (ακριβές trimmed match), αφήνει
                // οτιδήποτε άλλο του χρήστη στο config.txt ανέγγιχτο — ίδια πειθαρχία με το αρχικό
                // "δεν πειράζουμε τίποτα άλλο" πριν τη μετονομασία.
                int removed = rawLines.RemoveAll(l => l.Trim() == LegacyIncludeLine);
                try { File.Delete(LegacyIncludeFile); } catch (Exception) { }

                bool hasNewInclude = rawLines.Any(l => l.Trim() == IncludeLine);
                if (removed > 0 || !hasNewInclude)
                {
                    existing = string.Join("\n", rawLines);
                    if (!hasNewInclude)
                    {
                        string sep = existing.Length > 0 && !existing.EndsWith("\n") ? "\n" : "";
                        existing += sep + IncludeLine + "\n";
                    }
                    File.WriteAllText(ConfigFile, existing);
                }
                return (true, "OK");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, LocalizationManager.T("Apo.NoWriteAccess"));
            }
            catch (IOException ex) { return (false, ex.Message); }
        }
    }
}
