using System.Drawing;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    // Ανεξάρτητο θέμα για widgets/DeskContainers από αυτό της ίδιας της εφαρμογής — ζητήθηκε
    // ρητά "αν η εφαρμογή είναι σε φωτεινό θέμα, τα widgets/containers να μπορούν είτε να
    // ακολουθούν τα windows είτε να έχουν διαφορετικά χρώματα και από το σύστημα και από την
    // εφαρμογή" (AppSettings.WidgetsThemeMode: "App" | "Windows" | "Dark" | "Light").
    //
    // Ίδιο ακριβώς σύνολο ιδιοτήτων με το UiTheme, ώστε το WidgetEngine.cs/DeskContainerWindow.cs
    // να μπορούν να ανακατευθύνουν ΟΛΕΣ τις υπάρχουσες αναφορές "UiTheme.X" σε αυτή την κλάση με
    // ένα using-alias (using UiTheme = MotionDesk.UI.WidgetTheme;) αντί να αλλάξει το καθένα από
    // τα δεκάδες σημεία ξεχωριστά.
    public static class WidgetTheme
    {
        private static UiPalette Resolve()
        {
            string mode = AppSettings.Load().WidgetsThemeMode;
            return mode switch
            {
                "Windows" => ThemeManager.IsWindowsLightTheme() ? UiPalette.Light() : UiPalette.Dark(AppSettings.Load().DarkIntensity),
                "Dark" => UiPalette.Dark(AppSettings.Load().DarkIntensity),
                "Light" => UiPalette.Light(),
                _ => ThemeManager.Current, // "App" — ακολουθεί το θέμα της ίδιας της εφαρμογής (προεπιλογή, προηγούμενη συμπεριφορά)
            };
        }

        public static Color Background => Resolve().Background;
        public static Color Sidebar => Resolve().Sidebar;
        public static Color Surface => Resolve().Surface;
        public static Color SurfaceHover => Resolve().SurfaceHover;
        public static Color Border => Resolve().Border;
        public static Color TextPrimary => Resolve().TextPrimary;
        public static Color TextSecondary => Resolve().TextSecondary;
        public static Color TextMuted => Resolve().TextMuted;
        public static Color AccentCyan => Resolve().AccentCyan;
        public static Color AccentBlue => Resolve().AccentBlue;
        public static Color AccentSoft => Color.FromArgb(40, AccentCyan.R, AccentCyan.G, AccentCyan.B);

        // ΔΙΟΡΘΩΣΗ "μαύρο/λευκό εσωτερικό πλαίσιο στα widgets, να είναι όλο διαφάνεια": τα widgets
        // έχουν ήδη πραγματικό glass-blur στο ίδιο το παράθυρο (βλ. WidgetWindow.ApplyGlassBlur) -
        // το πρόβλημα ήταν ότι το δικό τους περιεχόμενο (SystemMonitorRowsControl/DiskRingControl/
        // κ.λπ.) ζωγράφιζε από πάνω μια ΑΔΙΑΦΑΝΗ (alpha=255) ορθογώνια γέμιση με UiTheme.Surface σε
        // ΟΛΟΚΛΗΡΟ το ClientRectangle, που σκέπαζε εντελώς το blur-behind της DWM με έναν συμπαγή
        // μπλοκ χρώματος - σχεδόν αόρατο σε Dark θέμα (το Surface εκεί είναι ήδη σκούρο γκρι, μοιάζει
        // τυχαία με θολωμένο σκοτεινό φόντο) αλλά εμφανώς λάθος σε Light θέμα (λευκό/ανοιχτό γκρι
        // συμπαγές κουτί). Ίδιο μοτίβο με το ήδη σωστό DeskStrip, που ΔΕΝ ζωγραφίζει τέτοιο γέμισμα
        // και αφήνει το glass-blur του παραθύρου να φαίνεται παντού εκτός από τα ίδια τα εικονίδια.
        //
        // Μια απλή ημι-διαφανή απόχρωση (όπως πριν, alpha~150) παραμένει εμφανώς "συμπαγής" -
        // ζητήθηκε ρητά μια ρητή λειτουργία "Πλήρης διαφάνεια" όπου ΔΕΝ υπάρχει ΚΑΝΕΝΑ μαύρο/λευκό
        // πλαίσιο.
        //
        // ΔΙΟΡΘΩΣΗ πραγματικού bug στην πρώτη προσπάθεια: alpha=0 σε ένα GDI+ FillRectangle ΔΕΝ
        // παράγει πραγματική διαφάνεια εδώ - το WidgetWindow (βλ. constructor) δεν χρησιμοποιεί
        // DWM alpha-compositing για το περιεχόμενό του, χρησιμοποιεί το παλιό, απλό Form.TransparencyKey
        // = Color.Black: ο,τιδήποτε ζωγραφιστεί με ΑΚΡΙΒΩΣ αυτό το χρώμα (RGB 0,0,0, πλήρως αδιαφανές)
        // γίνεται πραγματικά διαφανές/click-through από τα ίδια τα Windows σε επίπεδο color-key, ΟΧΙ
        // με βάση το alpha channel. alpha=0 απλώς αφήνει ό,τι ήδη υπήρχε στο buffer πριν το fill -
        // δεν είναι απαραίτητα το μαγικό χρώμα-κλειδί, οπότε έμενε αδιαφανές. Η σωστή τιμή είναι το
        // ΙΔΙΟ, ακριβές Color.Black (όχι κάποια σκούρα απόχρωση κοντά σε αυτό).
        // Δοκιμάστηκε πρώτα ένα ημι-διαφανές alpha (~150) για ΟΛΑ τα θέματα, με την ελπίδα ότι θα
        // περάσει μέσα από το ήδη υπάρχον glass-blur του παραθύρου - αποδείχτηκε εμπειρικά ότι ΔΕΝ
        // έχει κανένα ορατό αποτέλεσμα (βλ. σχόλιο IsFullyTransparent) σε αυτή την αρχιτεκτονική
        // widget, άρα ήταν άσκοπο ρίσκο οπτικής ασυνέπειας χωρίς κανένα πραγματικό όφελος. Οι 4
        // κανονικές λειτουργίες θέματος μένουν ΑΚΡΙΒΩΣ όπως πριν (αδιαφανές Surface) - μόνο η ρητή
        // "Πλήρης διαφάνεια" αλλάζει συμπεριφορά.
        public static bool IsFullyTransparent => AppSettings.Load().WidgetsThemeMode == "Transparent";
        public static Color SurfaceGlass => IsFullyTransparent ? Color.Black : Surface;

        public static readonly Font FontHeading = UiTheme.FontHeading;
        public static readonly Font FontSubheading = UiTheme.FontSubheading;
        public static readonly Font FontBody = UiTheme.FontBody;

        public static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius) => UiTheme.RoundedPath(bounds, radius);
        public static void ApplyRoundedRegion(System.Windows.Forms.Control control, int radius) => UiTheme.ApplyRoundedRegion(control, radius);
    }
}
