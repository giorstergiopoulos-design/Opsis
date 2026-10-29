using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Desktop_Fences
{
    // Opsis integration: every user-visible string of the engine goes through Loc.T.
    //  * the product word "fence" is shown as "DeskContainer" (Opsis naming);
    //  * Greek translations for the strings that exist in the UI (language comes from Opsis via OPSIS_LANG, else the system UI culture).
    internal static class Loc
    {
        public static readonly bool Greek =
            string.Equals((Environment.GetEnvironmentVariable("OPSIS_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName), "el", StringComparison.OrdinalIgnoreCase);

        // Exact-match translation only (no "fence" renaming): used for texts that were not wrapped at their source.
        public static string Translate(string s) => Greek && !string.IsNullOrEmpty(s) && Map.TryGetValue(s, out var g) ? g : s;

        // Texts of the new Opsis-side menus come from the Opsis locale files (all 14 languages): Opsis passes the path of the current language
        // file in OPSIS_LOCALE_FILE and the English one in OPSIS_LOCALE_FALLBACK. A missing key falls back to English, then to the literal.
        private static Dictionary<string, string> _ext, _extEn;
        private static Dictionary<string, string> LoadLocale(string envName)
        {
            var d = new Dictionary<string, string>();
            try
            {
                string path = Environment.GetEnvironmentVariable(envName);
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return d;
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Name.StartsWith("DCM.", StringComparison.Ordinal) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String) d[prop.Name] = prop.Value.GetString();
            }
            catch (Exception) { }
            return d;
        }
        public static string K(string key, string english)
        {
            if (string.IsNullOrEmpty(key)) return english;
            _ext ??= LoadLocale("OPSIS_LOCALE_FILE");
            if (_ext.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            _extEn ??= LoadLocale("OPSIS_LOCALE_FALLBACK");
            if (_extEn.TryGetValue(key, out var e) && !string.IsNullOrEmpty(e)) return e;
            return english;
        }

        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (Greek && Map.TryGetValue(s, out var g)) return g;
            return Rename(s);
        }

        private static readonly Regex FenceWord = new Regex(@"\b(Fences|Fence|fences|fence)\b", RegexOptions.Compiled);

        private static string Rename(string s)
        {
            s = s.Replace("Desktop Fences Plus", "Opsis DeskContainers").Replace("Desktop Fences +", "Opsis DeskContainers").Replace("Opsis DeskContainers", "Opsis DeskContainers");
            return FenceWord.Replace(s, m => m.Value switch { "Fences" => "DeskContainers", "Fence" => "DeskContainer", "fences" => "desk containers", _ => "desk container" });
        }

        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            // menus
            ["Process Name (Pick or Type):"] = "Όνομα διεργασίας (επιλογή ή πληκτρολόγηση):", ["Target Profile:"] = "Προφίλ προορισμού:", ["Activation Delay:"] = "Καθυστέρηση ενεργοποίησης:",
            ["Up"] = "Πάνω", ["Down"] = "Κάτω", ["Move Up"] = "Μετακίνηση πάνω", ["Move Down"] = "Μετακίνηση κάτω", ["Rename"] = "Μετονομασία", ["Delete"] = "Διαγραφή", ["Delete Profile"] = "Διαγραφή προφίλ",
            ["Rename profile:"] = "Μετονομασία προφίλ:", ["New Profile Name..."] = "Όνομα νέου προφίλ…", ["Invalid name or profile already exists."] = "Μη έγκυρο όνομα ή το προφίλ υπάρχει ήδη.",
            ["Rename failed. Name may be in use or invalid."] = "Η μετονομασία απέτυχε (το όνομα χρησιμοποιείται ή δεν είναι έγκυρο).", ["Cancel/Close"] = "Άκυρο/Κλείσιμο",
            ["Backup"] = "Αντίγραφο ασφαλείας", ["Backup completed successfully."] = "Το αντίγραφο ασφαλείας ολοκληρώθηκε.", ["Restore"] = "Επαναφορά",
            ["Custom path:"] = "Προσαρμοσμένη διαδρομή:", ["BROWSE"] = "ΕΠΙΛΟΓΗ", ["Portal"] = "Portal", ["New Rule"] = "Νέος κανόνας", ["Saved"] = "Αποθηκεύτηκε",
            ["Your rules have been saved and applied successfully."] = "Οι κανόνες αποθηκεύτηκαν και εφαρμόστηκαν.", ["Last successful run: Never"] = "Τελευταία επιτυχής εκτέλεση: Ποτέ",
            ["Custom Extensions..."] = "Προσαρμοσμένες επεκτάσεις…", ["Overwrite (Send old file to Recycle Bin)"] = "Αντικατάσταση (το παλιό αρχείο πάει στον Κάδο)", ["Skip (Leave on Desktop)"] = "Παράλειψη (μένει στην επιφάνεια)",
            ["Images"] = "Εικόνες", ["Documents"] = "Έγγραφα", ["Archives"] = "Αρχεία συμπίεσης", ["Executables"] = "Εκτελέσιμα",
            ["Settings"] = "Ρυθμίσεις", ["All settings in Opsis..."] = "Όλες οι ρυθμίσεις στο Opsis…", ["Single click to launch"] = "Άνοιγμα με ένα κλικ", ["Snap near other containers"] = "Μαγνητική στοίχιση κοντά σε άλλα containers",
            ["Sounds"] = "Ήχοι", ["Auto roll-up (expand on hover)"] = "Αυτόματο μάζεμα (άνοιγμα με hover)", ["Auto-hide when idle"] = "Αυτόματη απόκρυψη μετά από αδράνεια",
            ["Chameleon (colour from the wallpaper)"] = "Chameleon (χρώμα από το wallpaper)", ["Dropped desktop icons leave the desktop"] = "Τα εικονίδια που σύρονται μέσα φεύγουν από την επιφάνεια", ["Hide scrollbars"] = "Απόκρυψη scrollbars",
            ["About..."] = "Σχετικά…", ["Options..."] = "Ρυθμίσεις…", ["New Fence"] = "Νέο DeskContainer", ["New Portal Fence"] = "Νέο Portal DeskContainer",
            ["New Note Fence"] = "Νέο DeskContainer σημείωσης", ["Enable Tabs On This Fence"] = "Ενεργοποίηση καρτελών σε αυτό το DeskContainer",
            ["Delete this Fence"] = "Διαγραφή αυτού του DeskContainer", ["Export this Fence"] = "Εξαγωγή αυτού του DeskContainer", ["Import a Fence..."] = "Εισαγωγή DeskContainer…",
            ["Restore Last Deleted Fence"] = "Επαναφορά τελευταίου διαγραμμένου DeskContainer", ["Exit"] = "Έξοδος", ["Hide Fence"] = "Απόκρυψη DeskContainer",
            ["Sort by"] = "Ταξινόμηση κατά", ["Name"] = "Όνομα", ["Type"] = "Τύπος", ["Date modified"] = "Ημερομηνία τροποποίησης", ["Size"] = "Μέγεθος",
            ["Exclude from auto-hide"] = "Εξαίρεση από την αυτόματη απόκρυψη", ["Peek Behind"] = "Κρυφή ματιά (πίσω από το DeskContainer)", ["Customize..."] = "Προσαρμογή…",
            ["Add New Tab"] = "Προσθήκη νέας καρτέλας", ["Rename Tab"] = "Μετονομασία καρτέλας", ["Delete Tab"] = "Διαγραφή καρτέλας", ["Move Left"] = "Μετακίνηση αριστερά", ["Move Right"] = "Μετακίνηση δεξιά",
            ["Add spacer"] = "Προσθήκη κενού", ["Blank"] = "Κενό", ["Dot"] = "Τελεία", ["Open target folder..."] = "Άνοιγμα φακέλου προορισμού…", ["Copy path"] = "Αντιγραφή διαδρομής",
            ["Full path"] = "Πλήρης διαδρομή", ["Folder path"] = "Διαδρομή φακέλου", ["Run as administrator"] = "Εκτέλεση ως διαχειριστής", ["Run as different user..."] = "Εκτέλεση ως άλλος χρήστης…",
            ["Always run as administrator"] = "Πάντα εκτέλεση ως διαχειριστής", ["Always run as different user"] = "Πάντα εκτέλεση ως άλλος χρήστης", ["Remove"] = "Αφαίρεση", ["Edit..."] = "Επεξεργασία…",
            ["Move..."] = "Μετακίνηση…", ["Copy Item"] = "Αντιγραφή στοιχείου", ["Cut Item"] = "Αποκοπή στοιχείου", ["Paste Item"] = "Επικόλληση στοιχείου", ["Delete item"] = "Διαγραφή στοιχείου",
            ["Rename item"] = "Μετονομασία στοιχείου", ["Send to Desktop"] = "Αποστολή στην επιφάνεια εργασίας", ["Export all icons to desktop"] = "Εξαγωγή όλων των εικονιδίων στην επιφάνεια εργασίας",
            ["Clear Dead Shortcuts"] = "Καθαρισμός νεκρών συντομεύσεων", ["Name Fence After Target Path"] = "Όνομα DeskContainer από τη διαδρομή προορισμού", ["Open fence folder"] = "Άνοιγμα φακέλου του DeskContainer",
            ["Generate new Portal Fence for this folder"] = "Δημιουργία Portal DeskContainer για αυτόν τον φάκελο", ["Go Up"] = "Πάνω", ["Text Format..."] = "Μορφοποίηση κειμένου…",
            ["Copy All Text"] = "Αντιγραφή όλου του κειμένου", ["Clear All Text"] = "Εκκαθάριση κειμένου", ["Edit Shortcut"] = "Επεξεργασία συντόμευσης", ["Browse for Folder..."] = "Επιλογή φακέλου…",
            // dialogs
            ["Are you sure you want to delete this fence?"] = "Είσαι σίγουρος ότι θέλεις να διαγράψεις αυτό το DeskContainer;", ["Delete Fence"] = "Διαγραφή DeskContainer", ["Confirm delete"] = "Επιβεβαίωση διαγραφής",
            ["Yes"] = "Ναι", ["No"] = "Όχι", ["OK"] = "OK", ["Cancel"] = "Άκυρο", ["Close"] = "Κλείσιμο", ["Save"] = "Αποθήκευση", ["Apply"] = "Εφαρμογή", ["Default"] = "Προεπιλογή", ["Create"] = "Δημιουργία",
            ["Export Successful"] = "Η εξαγωγή ολοκληρώθηκε", ["Import Complete"] = "Η εισαγωγή ολοκληρώθηκε", ["Success"] = "Επιτυχία", ["Error"] = "Σφάλμα", ["Information"] = "Πληροφορία",
            ["Reloading all fences, please wait..."] = "Επαναφόρτωση όλων των DeskContainers, περίμενε…", ["Please wait..."] = "Περίμενε…", ["Update Available"] = "Διαθέσιμη ενημέρωση",
            ["Select Fence Export File"] = "Επιλογή αρχείου εξαγωγής DeskContainer", ["No other Data Fences found to import."] = "Δεν βρέθηκαν άλλα DeskContainers δεδομένων για εισαγωγή.",
            ["Select a destination fence or tab:"] = "Επίλεξε DeskContainer ή καρτέλα προορισμού:", ["Select a source fence or tab to import:"] = "Επίλεξε DeskContainer ή καρτέλα πηγής για εισαγωγή:",
            ["Import Tab"] = "Εισαγωγή καρτέλας", ["Move Item To..."] = "Μετακίνηση στοιχείου σε…", ["Move To:"] = "Μετακίνηση σε:", ["Select Icon"] = "Επιλογή εικονιδίου", ["Select an Icon"] = "Επίλεξε εικονίδιο",
            ["Select an Icon from the available and click OK"] = "Επίλεξε ένα εικονίδιο και πάτα OK", ["Select Target File"] = "Επιλογή αρχείου προορισμού", ["Search active fences:"] = "Αναζήτηση στα ενεργά DeskContainers:",
            ["Type to search..."] = "Πληκτρολόγησε για αναζήτηση…", ["Desktop Fences Search"] = "Αναζήτηση DeskContainers", ["Clear filter and close"] = "Εκκαθάριση φίλτρου και κλείσιμο",
            ["Filter files (e.g. '*.jpg' or '>*.tmp' to exclude)"] = "Φίλτρο αρχείων (π.χ. '*.jpg' ή '>*.tmp' για εξαίρεση)", ["Draw a box to create a Fence (Esc to cancel)"] = "Σχεδίασε ένα ορθογώνιο για νέο DeskContainer (Esc για ακύρωση)",
            ["Set current view as the new Home for this fence"] = "Ορισμός της τρέχουσας προβολής ως αρχικής για αυτό το DeskContainer", ["Click to edit note content"] = "Κλικ για επεξεργασία της σημείωσης",
            ["Click to finish editing"] = "Κλικ για τέλος επεξεργασίας", ["Add new tab (Ctrl+Click to Import)"] = "Νέα καρτέλα (Ctrl+κλικ για εισαγωγή)", ["Blank Spacer (Hold CTRL to move)"] = "Κενό (κράτα Ctrl για μετακίνηση)",
            ["New Fence - Drop your shortcuts here"] = "Νέο DeskContainer — σύρε εδώ τις συντομεύσεις σου",
            // customize
            ["Customize Fence"] = "Προσαρμογή DeskContainer", ["Fence"] = "DeskContainer", ["Custom Color:"] = "Προσαρμοσμένο χρώμα:", ["Custom Launch Effect:"] = "Εφέ εκκίνησης:", ["Fence Border Color:"] = "Χρώμα περιγράμματος:",
            ["Fence Border Thickness:"] = "Πάχος περιγράμματος:", ["Title"] = "Τίτλος", ["Title Text Color:"] = "Χρώμα κειμένου τίτλου:", ["Title Text Size:"] = "Μέγεθος κειμένου τίτλου:", ["Title Font:"] = "Γραμματοσειρά τίτλου:",
            ["Title Bar Color:"] = "Χρώμα γραμμής τίτλου:", ["Bold Title Text"] = "Έντονο κείμενο τίτλου", ["Icons"] = "Εικονίδια", ["Icon Size:"] = "Μέγεθος εικονιδίων:", ["Icon Spacing:"] = "Απόσταση εικονιδίων:",
            ["Text Color:"] = "Χρώμα κειμένου:", ["Disable Text Shadow"] = "Απενεργοποίηση σκιάς κειμένου", ["Grayscale Icons"] = "Εικονίδια σε αποχρώσεις του γκρι", ["Text Format"] = "Μορφή κειμένου",
            ["Text Appearance"] = "Εμφάνιση κειμένου", ["Text Behavior"] = "Συμπεριφορά κειμένου", ["Text Operations"] = "Ενέργειες κειμένου", ["Text Color"] = "Χρώμα κειμένου", ["Font Family"] = "Γραμματοσειρά", ["Font Size"] = "Μέγεθος γραμματοσειράς",
            ["Title Bar"] = "Γραμμή τίτλου", ["Color"] = "Χρώμα", ["Effect"] = "Εφέ", ["Menu Icon"] = "Εικονίδιο μενού",
            // profiles / automation / smart desktop
            ["Profile Manager"] = "Διαχείριση προφίλ", ["Active"] = "Ενεργό", ["Enable Profile Automation"] = "Ενεργοποίηση αυτοματισμού προφίλ", ["Automation Rules"] = "Κανόνες αυτοματισμού", ["Manage Automation Rules"] = "Διαχείριση κανόνων αυτοματισμού",
            ["Persisted Mode (Stay on profile after close)"] = "Μόνιμη λειτουργία (παραμονή στο προφίλ μετά το κλείσιμο)", ["CLICK ON THE TARGET WINDOW"] = "ΚΑΝΕ ΚΛΙΚ ΣΤΟ ΠΑΡΑΘΥΡΟ ΠΡΟΟΡΙΣΜΟΥ", ["🎯 Pick Window"] = "🎯 Επιλ.",
            ["Smart Desktop Rules"] = "Κανόνες έξυπνης επιφάνειας εργασίας", ["Existing Rules"] = "Υπάρχοντες κανόνες", ["Rule Definition"] = "Ορισμός κανόνα", ["Rule Name:"] = "Όνομα κανόνα:", ["Save Rules"] = "Αποθήκευση κανόνων",
            ["+ Add Rule"] = "+ Νέος", ["− Remove"] = "− Αφαίρεση", ["Delete Selected"] = "Διαγραφή", ["Enable this rule"] = "Ενεργοποίηση κανόνα", ["Double-click to edit rule"] = "Διπλό κλικ για επεξεργασία κανόνα",
            ["If File Extension Is:"] = "Αν η επέκταση αρχείου είναι:", ["And Filename Contains (Optional, leave blank for any):"] = "Και το όνομα περιέχει (προαιρετικά, άφησέ το κενό για οποιοδήποτε):",
            ["If File Already Exists:"] = "Αν το αρχείο υπάρχει ήδη:", ["Custom Extensions (e.g., *.zip; *.rar):"] = "Προσαρμοσμένες επεκτάσεις (π.χ. *.zip; *.rar):", ["Icon:"] = "Εικονίδιο:",
            ["Original project"] = "Αρχικό project", ["Opsis DeskContainers"] = "Opsis DeskContainers", ["About Desktop Fences +"] = "Σχετικά με τα Opsis DeskContainers", ["Desktop Fences Plus"] = "Opsis DeskContainers",
            ["Opsis DeskContainers Notification"] = "Ειδοποίηση Opsis DeskContainers", ["Welcome to Opsis DeskContainers"] = "Καλώς ήρθες στα Opsis DeskContainers"
        };
    }
}
