using System;
using System.IO;

namespace MotionDesk.Services
{
    // Κεντρικό σημείο για το %AppData% folder της εφαρμογής — πριν αυτό το αρχείο το literal
    // "MotionDeskStudio" ήταν επαναλαμβανόμενο σε 16 διαφορετικά αρχεία. Δημιουργήθηκε κατά τη
    // μετονομασία σε "Opsis" ακριβώς επειδή ένα απλό εύρημα/αντικατάσταση του string δεν αρκεί: αν
    // ο φάκελος απλώς μετονομαστεί κώδικα, κάθε υπάρχων χρήστης θα έβλεπε την εφαρμογή να ξεκινά
    // εντελώς άδεια (ρυθμίσεις, containers, zones, widgets, automation rules κ.λπ. θα "χάνονταν"
    // σιωπηλά, παραμένοντας στον παλιό φάκελο). Το AppDataFolder κάνει αυτόματη, μία-φορά μετακίνηση
    // του παλιού φακέλου στον νέο αν χρειάζεται, πριν τον επιστρέψει.
    public static class AppPaths
    {
        private const string CurrentFolderName = "Opsis";
        private const string LegacyFolderName = "MotionDeskStudio";

        private static readonly Lazy<string> _appDataFolder = new(() =>
        {
            // Απομονωμένος φάκελος δεδομένων για αυτοματοποιημένες δοκιμές/προσομοιώσεις (δεν αγγίζει
            // τις πραγματικές ρυθμίσεις του χρήστη).
            string? isolated = Environment.GetEnvironmentVariable("OPSIS_APPDATA");
            if (!string.IsNullOrWhiteSpace(isolated)) { Directory.CreateDirectory(isolated); return isolated; }
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string current = Path.Combine(root, CurrentFolderName);
            string legacy = Path.Combine(root, LegacyFolderName);
            try
            {
                if (!Directory.Exists(current) && Directory.Exists(legacy))
                    Directory.Move(legacy, current);
            }
            catch (Exception) { } // Αν η μετακίνηση αποτύχει (π.χ. κλειδωμένο αρχείο), η εφαρμογή απλά
                                   // ξεκινά με νέο, άδειο φάκελο αντί να ρίξει exception στην εκκίνηση.
            return current;
        });

        public static string AppDataFolder => _appDataFolder.Value;
    }
}
