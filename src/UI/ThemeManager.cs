using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    public sealed class UiPalette
    {
        public Color Background = Color.FromArgb(18, 18, 20);
        public Color Sidebar = Color.FromArgb(23, 23, 26);
        public Color Surface = Color.FromArgb(32, 32, 36);
        public Color SurfaceHover = Color.FromArgb(42, 42, 47);
        public Color Border = Color.FromArgb(52, 52, 58);
        public Color TextPrimary = Color.White;
        public Color TextSecondary = Color.Silver;
        public Color TextMuted = Color.FromArgb(140, 140, 148);
        public Color AccentCyan = Color.FromArgb(0, 210, 255);
        public Color AccentBlue = Color.FromArgb(58, 123, 213);
        public bool IsDark = true;

        // intensity: 0 = πιο ανοιχτό σκούρο (soft dark, σαν GitHub dark), 1 = σχεδόν μαύρο
        // (AMOLED-style), 0.5 = η αρχική, αμετάβλητη βάση (τα ίδια χρώματα με πριν αυτή τη
        // ρύθμιση) — ζητήθηκε ρητά ο χρήστης να μπορεί να αλλάζει πόσο σκούρο είναι το dark theme.
        // Μόνο τα φόντα/επιφάνειες κλιμακώνονται· κείμενο και accent χρώματα μένουν σταθερά για
        // εγγυημένη αντίθεση σε όλο το εύρος.
        //
        // Σκόπιμα ΔΕΝ γίνεται απλή πολλαπλασιαστική εξασθένιση προς το (0,0,0): σε intensity=1 κάθε
        // κανάλι θα συνέκλινε στο μηδέν ανεξαρτήτως αρχικής τιμής, δηλαδή ΟΛΑ τα επίπεδα
        // (Background/Sidebar/Surface/SurfaceHover/Border) θα γίνονταν ταυτόσημο καθαρό μαύρο —
        // χάνοντας κάθε οπτικό διαχωρισμό μεταξύ panel/hover/border ακριβώς στο πιο ακραίο σημείο
        // του slider. Αντ' αυτού, κάθε ιδιότητα παίρνει δικό της ρητό "floor" χρώμα (στο 1.0) και
        // "ceiling" χρώμα (στο 0.0), και γίνεται γραμμική παρεμβολή ceiling→base→floor — η σχετική
        // σειρά φωτεινότητας διατηρείται σε όλο το εύρος.
        public static UiPalette Dark(double intensity = 0.5)
        {
            var p = new UiPalette();
            p.Background = Shade(p.Background, Color.FromArgb(52, 52, 54), Color.FromArgb(2, 2, 3), intensity);
            p.Sidebar = Shade(p.Sidebar, Color.FromArgb(57, 57, 60), Color.FromArgb(5, 5, 6), intensity);
            p.Surface = Shade(p.Surface, Color.FromArgb(66, 66, 70), Color.FromArgb(9, 9, 10), intensity);
            p.SurfaceHover = Shade(p.SurfaceHover, Color.FromArgb(76, 76, 81), Color.FromArgb(14, 14, 16), intensity);
            p.Border = Shade(p.Border, Color.FromArgb(86, 86, 92), Color.FromArgb(20, 20, 23), intensity);
            return p;
        }

        private static Color Shade(Color baseColor, Color lightCeiling, Color darkFloor, double intensity)
        {
            double t = Math.Clamp(intensity, 0, 1);
            return t <= 0.5
                ? LerpColor(lightCeiling, baseColor, t / 0.5)
                : LerpColor(baseColor, darkFloor, (t - 0.5) / 0.5);
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                a.R + (int)((b.R - a.R) * t),
                a.G + (int)((b.G - a.G) * t),
                a.B + (int)((b.B - a.B) * t));
        }

        // v2.0.0: νέα ονομασμένα θέματα (ζητήθηκε ρητά "νέα θέματα στην εφαρμογή"). Κάθε ένα είναι
        // πλήρης παλέτα (όχι tint πάνω στο Dark) με δικό του accent· επιστρέφει null για άγνωστο όνομα.
        public static readonly string[] NamedThemes = { "Midnight", "Forest", "Dawn", "Contrast" };

        public static UiPalette? Named(string name) => name switch
        {
            "Midnight" => new UiPalette
            {
                Background = Color.FromArgb(11, 16, 32), Sidebar = Color.FromArgb(15, 22, 42), Surface = Color.FromArgb(22, 31, 58),
                SurfaceHover = Color.FromArgb(32, 44, 78), Border = Color.FromArgb(45, 58, 96),
                TextPrimary = Color.FromArgb(232, 238, 255), TextSecondary = Color.FromArgb(180, 192, 224), TextMuted = Color.FromArgb(120, 134, 170),
                AccentCyan = Color.FromArgb(110, 168, 255), AccentBlue = Color.FromArgb(120, 90, 235), IsDark = true
            },
            "Forest" => new UiPalette
            {
                Background = Color.FromArgb(13, 20, 16), Sidebar = Color.FromArgb(17, 27, 21), Surface = Color.FromArgb(24, 38, 30),
                SurfaceHover = Color.FromArgb(34, 53, 42), Border = Color.FromArgb(48, 72, 58),
                TextPrimary = Color.FromArgb(232, 245, 236), TextSecondary = Color.FromArgb(178, 202, 188), TextMuted = Color.FromArgb(122, 148, 132),
                AccentCyan = Color.FromArgb(72, 214, 140), AccentBlue = Color.FromArgb(40, 150, 110), IsDark = true
            },
            "Dawn" => new UiPalette
            {
                Background = Color.FromArgb(251, 246, 240), Sidebar = Color.FromArgb(255, 251, 246), Surface = Color.FromArgb(245, 236, 226),
                SurfaceHover = Color.FromArgb(236, 223, 209), Border = Color.FromArgb(224, 208, 190),
                TextPrimary = Color.FromArgb(44, 32, 24), TextSecondary = Color.FromArgb(104, 84, 68), TextMuted = Color.FromArgb(146, 124, 106),
                AccentCyan = Color.FromArgb(214, 96, 40), AccentBlue = Color.FromArgb(176, 60, 90), IsDark = false
            },
            "Contrast" => new UiPalette
            {
                Background = Color.Black, Sidebar = Color.Black, Surface = Color.FromArgb(14, 14, 14),
                SurfaceHover = Color.FromArgb(38, 38, 38), Border = Color.White,
                TextPrimary = Color.White, TextSecondary = Color.FromArgb(235, 235, 235), TextMuted = Color.FromArgb(200, 200, 200),
                AccentCyan = Color.FromArgb(255, 221, 0), AccentBlue = Color.FromArgb(0, 232, 255), IsDark = true
            },
            _ => null
        };

        public static UiPalette Light() => new()
        {
            Background = Color.FromArgb(246, 247, 249),
            Sidebar = Color.FromArgb(255, 255, 255),
            Surface = Color.FromArgb(238, 240, 243),
            SurfaceHover = Color.FromArgb(226, 229, 234),
            Border = Color.FromArgb(214, 217, 222),
            TextPrimary = Color.FromArgb(20, 22, 26),
            TextSecondary = Color.FromArgb(80, 84, 92),
            TextMuted = Color.FromArgb(120, 124, 132),
            AccentCyan = Color.FromArgb(0, 145, 190),
            AccentBlue = Color.FromArgb(30, 90, 180),
            IsDark = false
        };
    }

    // Κεντρική διαχείριση θέματος (Light/Dark/Follow Windows) για ΟΛΗ την εφαρμογή.
    // Το UiTheme διαβάζει τα χρώματα από εδώ, ώστε μία αλλαγή να επηρεάζει άμεσα κάθε σελίδα.
    public static class ThemeManager
    {
        public static UiPalette Current { get; private set; } = ResolveInitial();
        public static event Action? Changed;

        // Ξεχωριστό, ΦΤΗΝΟ event για ζωντανή προεπισκόπηση ενώ σέρνεις το slider σκουρότητας —
        // ΟΧΙ το ίδιο με το Changed. Το Changed προκαλεί πλήρη ανακατασκευή σελίδας/sidebar (ώστε
        // τα non-owner-draw controls, π.χ. CheckBox.ForeColor, να πάρουν το νέο χρώμα — αυτά ΔΕΝ
        // το διαβάζουν ζωντανά, μόνο τη στιγμή της κατασκευής τους). Αυτό ήταν ΤΟ bug: το
        // TrackBar.Scroll πυροδοτούσε Changed σε ΚΑΘΕ tick ενός συνεχόμενου drag (δεκάδες φορές/
        // δευτερόλεπτο), δηλαδή ανακατασκεύαζε ΟΛΗ τη σελίδα -ΣΥΜΠΕΡΙΛΑΜΒΑΝΟΜΕΝΟΥ του ίδιου του
        // TrackBar που έσερνε ο χρήστης- πολλές φορές το δευτερόλεπτο· αυτό εξηγούσε τόσο το
        // "τρεμόπαιγμα" (constant rebuild) όσο και το "πηγαίνει κάθε 5%" (διακοπτόμενο mouse
        // capture από την καταστροφή/αναδημιουργία του control μεσοδρομής). Το Repainted κάνει
        // μόνο Invalidate — ενημερώνει άμεσα όλα τα owner-draw controls (NavButton/StatCard/κ.λπ.,
        // που ήδη διαβάζουν το UiTheme.* ζωντανά σε κάθε OnPaint) χωρίς να πειράξει το δέντρο
        // ελέγχων· η πλήρης, ακριβής ανακατασκευή γίνεται ΜΙΑ φορά όταν αφήνει ο χρήστης το slider.
        public static event Action? Repainted;

        public static void PreviewDarkIntensity(double intensity)
        {
            if (!Current.IsDark) return;
            Current = UiPalette.Dark(Math.Clamp(intensity, 0, 1));
            Repainted?.Invoke();
        }

        private static UiPalette ResolveInitial()
        {
            var mode = AppSettings.Load().ThemeMode;
            return Resolve(mode);
        }

        private static UiPalette Resolve(string mode)
        {
            var settings = AppSettings.Load();
            var p = UiPalette.Named(mode) ?? mode switch
            {
                "Light" => UiPalette.Light(),
                "Dark" => UiPalette.Dark(settings.DarkIntensity),
                _ => IsWindowsLightTheme() ? UiPalette.Light() : UiPalette.Dark(settings.DarkIntensity),
            };
            if (settings.FollowWindowsAccent && GetWindowsAccentColor() is Color accent)
            {
                p.AccentCyan = accent;
                // Δεν υπάρχει ένα "δεύτερο" accent χρώμα στα Windows — παράγουμε το AccentBlue
                // σκουραίνοντάς το ελαφρώς, ώστε τα ήδη υπάρχοντα διαγώνια gradients (λογότυπο,
                // tiles κ.λπ.) να συνεχίσουν να δουλεύουν με δύο αποχρώσεις αντί για ένα flat χρώμα.
                p.AccentBlue = Color.FromArgb(255, (int)(accent.R * 0.65), (int)(accent.G * 0.65), (int)(accent.B * 0.65));
            }
            return p;
        }

        // Εμπνευσμένο από το Seelen UI (SeelenUI-inspired pass, ζητήθηκε ρητά): εκεί κάθε widget
        // παίρνει έτοιμα --system-accent-color CSS variables από το πραγματικό accent color των
        // Windows. Εδώ δεν υπάρχει CSS, οπότε διαβάζουμε το ΙΔΙΟ χρώμα απευθείας με το επίσημο,
        // τεκμηριωμένο DwmGetColorizationColor (ίδια τιμή που χρησιμοποιεί η ίδια η γραμμή τίτλου
        // των Windows όταν είναι ενεργό το "Εμφάνιση χρώματος στη γραμμή τίτλου").
        [DllImport("dwmapi.dll")] private static extern int DwmGetColorizationColor(out uint colorizationColor, out bool opaqueBlend);

        public static Color? GetWindowsAccentColor()
        {
            try
            {
                if (DwmGetColorizationColor(out uint argb, out _) != 0) return null;
                return Color.FromArgb(255, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            }
            catch (Exception) { return null; }
        }

        public static bool IsWindowsLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int i && i != 0;
            }
            catch (System.Security.SecurityException) { return false; }
        }

        public static void SetMode(string mode)
        {
            var settings = AppSettings.Load();
            settings.ThemeMode = mode;
            settings.Save();
            Current = Resolve(mode);
            Changed?.Invoke();
        }

        public static void SetFollowWindowsAccent(bool follow)
        {
            var settings = AppSettings.Load();
            settings.FollowWindowsAccent = follow;
            settings.Save();
            Current = Resolve(settings.ThemeMode);
            Changed?.Invoke();
        }

        // Ζωντανή ενημέρωση καθώς ο χρήστης σέρνει το slider "πόσο σκούρο" στις Ρυθμίσεις — δεν
        // κάνει τίποτα αν το τρέχον θέμα δεν είναι καν σκούρο αυτή τη στιγμή (Light mode), ώστε να
        // μην προκαλεί άσκοπο repaint· θα εφαρμοστεί όταν ο χρήστης γυρίσει σε Dark/Follow-dark.
        public static void SetDarkIntensity(double intensity)
        {
            var settings = AppSettings.Load();
            settings.DarkIntensity = Math.Clamp(intensity, 0, 1);
            settings.Save();
            if (Current.IsDark)
            {
                Current = UiPalette.Dark(settings.DarkIntensity);
                Changed?.Invoke();
            }
        }

        // Δημόσιο "χειροκίνητο" trigger του Changed — χρειάζεται όταν αλλάζει κάτι που ΔΕΝ αγγίζει
        // το ίδιο το ThemeManager.Current (π.χ. AppSettings.WidgetsThemeMode, το ανεξάρτητο θέμα
        // widgets/DeskContainers) αλλά πρέπει να ξαναζωγραφιστούν όλα τα ήδη ανοιχτά widgets.
        public static void NotifyChanged() => Changed?.Invoke();

        // UI SynchronizationContext — ορίζεται από το MainWindow (UI thread). Το SystemEvents.
        // UserPreferenceChanged καλείται από ΔΙΚΟ ΤΟΥ thread· χωρίς marshalling, τα widgets
        // ενημέρωναν χρώματα/Region από foreign thread (cross-thread exception/crash όταν άλλαζε το
        // θέμα των Windows με ThemeMode=Follow).
        public static System.Threading.SynchronizationContext? UiContext { get; set; }

        public static void RefreshFromWindows()
        {
            var ui = UiContext;
            if (ui != null && System.Threading.SynchronizationContext.Current != ui)
            {
                ui.Post(_ => RefreshFromWindows(), null);
                return;
            }
            if (AppSettings.Load().ThemeMode == "Follow")
            {
                var resolved = Resolve("Follow");
                if (resolved.IsDark != Current.IsDark)
                {
                    Current = resolved;
                    Changed?.Invoke();
                }
            }
        }
    }
}
