using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MotionDesk.Services
{
    // Διαβάζει τις εφαρμογές που είναι καρφιτσωμένες στην πραγματική Windows taskbar, για να
    // προ-γεμίσει το DeskStrip την πρώτη φορά, πριν ο χρήστης προσθέσει κάτι μόνος του (ζητήθηκε
    // ρητά: "όταν εμφανίζεται από προεπιλογή να έχει τις εφαρμογές που είναι καρφιτσωμένες στην
    // taskbar"). Καλύπτει μόνο desktop (Win32) εφαρμογές: τα .lnk αρχεία στο "Quick Launch\User
    // Pinned\TaskBar" είναι ο κλασικός, ακόμα λειτουργικός μηχανισμός γι' αυτές στα Windows 10/11.
    // Καρφιτσωμένες εφαρμογές από το Microsoft Store (UWP/packaged) ΔΕΝ εμφανίζονται εδώ — δεν
    // έχουν αντίστοιχο .lnk εκεί, θα χρειάζονταν ξεχωριστό AppsFolder/PackageManager API, εκτός
    // scope αυτού του περάσματος. Η ΣΕΙΡΑ δεν είναι εγγυημένα ταυτόσημη με την πραγματική σειρά
    // στην taskbar (αυτή είναι αποθηκευμένη σε ένα μη τεκμηριωμένο binary blob στο registry,
    // "Taskband") — εδώ χρησιμοποιείται η σειρά τροποποίησης των ίδιων των .lnk αρχείων, μια
    // λογική προσέγγιση, όχι εγγύηση 1:1.
    //
    // ΣΗΜΑΝΤΙΚΟ: επιστρέφονται τα ίδια τα .lnk αρχεία, ΟΧΙ το resolved target exe. Αρχική εκδοχή
    // έκανε resolve μέχρι το target (μέσω WScript.Shell) — δύο πραγματικά, ξεχωριστά bugs βρέθηκαν
    // live μόλις δοκιμάστηκε στο πραγματικό μηχάνημα του χρήστη: (1) το "File Explorer.lnk" έχει
    // ΚΕΝΟ TargetPath (ειδική shell διαδρομή, όχι απλό exe) — αφαιρούνταν εντελώς· (2) ένα custom
    // site-specific-browser shortcut ("Okairos.lnk") resolve-άρει στο ΙΔΙΟ firefox.exe με το
    // κανονικό "Firefox.lnk" — η deduplication-by-target το αφαιρούσε σαν "διπλότυπο", ενώ στην
    // πραγματικότητα είναι διαφορετική καρφιτσωμένη συντόμευση (διαφορετικά arguments/icon). Το να
    // κρατηθεί το ίδιο το .lnk λύνει και τα δύο ταυτόχρονα: το Icon.ExtractAssociatedIcon και το
    // Process.Start (UseShellExecute) δουλεύουν ήδη σωστά πάνω σε .lnk αρχεία — σέβονται το δικό
    // του ανατεθειμένο εικονίδιο/ορίσματα, ακριβώς όπως θα έκανε ένα διπλό-κλικ πάνω του.
    public static class TaskbarPinsReader
    {
        public static List<string> GetPinnedAppPaths()
        {
            var result = new List<string>();
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                if (!Directory.Exists(folder)) return result;

                result.AddRange(Directory.GetFiles(folder, "*.lnk").OrderBy(File.GetLastWriteTime));
            }
            catch (Exception) { /* best effort — ποτέ δεν πρέπει να μπλοκάρει την εκκίνηση του DeskStrip */ }
            return result;
        }
    }
}
