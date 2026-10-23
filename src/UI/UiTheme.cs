using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.UI
{
    // Κοινό οπτικό σύστημα (χρώματα, τυπογραφία, rounded-corner helpers) ώστε όλα τα
    // παράθυρα/σελίδες της εφαρμογής (κύριο παράθυρο, dialogs, κάρτες, κουμπιά) να
    // μοιράζονται την ίδια αισθητική αντί για ad-hoc χρώματα σε κάθε αρχείο.
    // Τα χρώματα διαβάζονται ζωντανά από το ThemeManager.Current — μια αλλαγή Light/Dark
    // Ισχύει αμέσως για κάθε owner-draw στοιχείο (NavButton/StatCard/κ.λπ.) στο επόμενο paint.
    public static class UiTheme
    {
        public static Color Background => ThemeManager.Current.Background;
        public static Color Sidebar => ThemeManager.Current.Sidebar;
        public static Color Surface => ThemeManager.Current.Surface;
        public static Color SurfaceHover => ThemeManager.Current.SurfaceHover;
        public static Color Border => ThemeManager.Current.Border;
        public static Color TextPrimary => ThemeManager.Current.TextPrimary;
        public static Color TextSecondary => ThemeManager.Current.TextSecondary;
        public static Color TextMuted => ThemeManager.Current.TextMuted;

        public static Color AccentCyan => ThemeManager.Current.AccentCyan;
        public static Color AccentBlue => ThemeManager.Current.AccentBlue;
        public static Color AccentSoft => Color.FromArgb(40, AccentCyan.R, AccentCyan.G, AccentCyan.B);

        // "Segoe UI Variable" (Display για τίτλους, Text για το υπόλοιπο) είναι η γραμματοσειρά
        // συστήματος των Windows 11 (πιο σύγχρονες αναλογίες από το παλιό "Segoe UI") — ζητήθηκε
        // ρητά SeelenUI-inspired βελτίωση γραμματοσειρών. Σε Windows 10, όπου δεν υπάρχει
        // εγκατεστημένη, το GDI+ κάνει σιωπηλά fallback (δεν πετάει exception) σε προεπιλεγμένη
        // γραμματοσειρά — ασφαλές και στις δύο εκδόσεις των Windows.
        public static readonly Font FontHeading = new("Segoe UI Variable Display", 20f, FontStyle.Bold);
        public static readonly Font FontSubheading = new("Segoe UI Variable Display", 12f, FontStyle.Bold);
        public static readonly Font FontBody = new("Segoe UI Variable Text", 10f);
        public static readonly Font FontIcon16 = new("Segoe Fluent Icons", 15f);

        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            var bounds = new Rectangle(0, 0, control.Width, control.Height);
            using var path = RoundedPath(bounds, radius);
            control.Region = new Region(path);
        }

        // Σωστά ελληνικά κεφαλαία: η ορθογραφική σύμβαση είναι να ΜΗΝ τονίζονται τα κεφαλαία
        // γράμματα (π.χ. "ΕΞΑΤΟΜΙΚΕΥΣΗ", όχι "ΕΞΑΤΟΜΙΚΕΥΣΉ"). Το απλό ToUpperInvariant() ΔΕΝ
        // αφαιρεί τους τόνους (μετατρέπει "διάταξη" -> "ΔΙΆΤΑΞΗ", με τόνο) — ζητήθηκε ρητά να
        // διορθωθεί σε όλα τα σημεία που εμφανίζουν κείμενο σε κεφαλαία (τίτλοι ενοτήτων, κάρτες).
        public static string UpperNoAccents(string text)
        {
            string upper = text.ToUpperInvariant();
            var sb = new System.Text.StringBuilder(upper.Length);
            foreach (char c in upper)
            {
                sb.Append(c switch
                {
                    'Ά' => 'Α',
                    'Έ' => 'Ε',
                    'Ή' => 'Η',
                    'Ί' => 'Ι',
                    'Ό' => 'Ο',
                    'Ύ' => 'Υ',
                    'Ώ' => 'Ω',
                    'Ϊ' => 'Ι',
                    'Ϋ' => 'Υ',
                    _ => c
                });
            }
            return sb.ToString();
        }
    }

    // ΔΙΟΡΘΩΣΗ ζητήματος "η διαφάνεια να αφορά το παράθυρο όχι το περιεχόμενο": το παλιό μοντέλο
    // (Form.Opacity) θολώνει ΟΛΟΚΛΗΡΟ το παράθυρο σαν bitmap — κείμενο/εικονίδια ξεθωριάζουν μαζί
    // με το φόντο. Το DWM "blur-behind" (SetWindowCompositionAttribute, WCA_ACCENT_POLICY) κάνει
    // ΑΚΡΙΒΩΣ το αντίθετο: θολώνει/διαφανοποιεί μόνο ό,τι είναι ΠΙΣΩ από το παράθυρο (την επιφάνεια
    // εργασίας), ενώ το ίδιο το περιεχόμενο που ζωγραφίζει η εφαρμογή (κείμενο, εικονίδια, γραμμές)
    // παραμένει πλήρως αδιαφανές — ο μηχανισμός υπήρχε ήδη (DeskContainerWindow.ApplyGlassBlur)
    // αλλά με σταθερό, μη ρυθμιζόμενο alpha· εδώ γίνεται κοινός και παραμετρικός ώστε να τον
    // χρησιμοποιούν και τα DeskContainers και τα widgets αντί για Form.Opacity.
    public static class GlassBlur
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        // ΔΙΟΡΘΩΣΗ "μη εμφάνιση λευκού πλαισίου στο widget" (ζητήθηκε ρητά "έλεγχος για όλα"):
        // τα Windows 11 ζωγραφίζουν ένα λεπτό accent-χρωματιστό περίγραμμα (συνήθως ανοιχτόχρωμο/
        // λευκό) γύρω από ΚΑΘΕ top-level παράθυρο, ακόμα και borderless (FormBorderStyle=None) —
        // ορατό γύρω από widgets/containers ανεξάρτητα από θέμα. DWMWA_BORDER_COLOR = COLORREF
        // "none" (0xFFFFFFFE) το απενεργοποιεί ρητά για συγκεκριμένο hwnd. Άκυρο/no-op σε Windows
        // εκδόσεις πριν το 22H2 (απλά επιστρέφει κωδικό σφάλματος, δεν πετάει exception).
        private const int DWMWA_BORDER_COLOR = 34;
        private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

        public static void DisableNativeBorderAccent(IntPtr handle)
        {
            try { uint none = DWMWA_COLOR_NONE; DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref none, sizeof(uint)); }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }

        // Handoff Φάση 8 (GUI polish): πραγματικό Mica backdrop (Win11 22H2+) με σιωπηλό fallback
        // σε Win10/παλαιότερο Win11 — το attribute απλά αγνοείται/επιστρέφει σφάλμα σε μηχανήματα
        // που δεν το υποστηρίζουν, ΔΕΝ πετάει exception, άρα δεν χρειάζεται δικό της εκδοχή ανά OS.
        // DWMSBT_MAINWINDOW (=2) είναι το "κανονικό" Mica (όχι Acrylic/Tabbed) — το σωστό για ένα
        // βασικό application window όπως το MainWindow. ΣΗΜΑΝΤΙΚΟ (βρέθηκε στο ίδιο το handoff doc,
        // επιβεβαιώθηκε με γρήγορο οπτικό έλεγχο): το WS_EX_COMPOSITED/AllowTransparency μαζί με
        // DWM backdrop προκαλούν συχνά τρεμόπαιγμα/μαύρο φόντο — το MainWindow δεν τα χρησιμοποιεί
        // ήδη, οπότε δεν χρειάζεται επιπλέον προσοχή εδώ.
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const uint DWMSBT_MAINWINDOW = 2;
        private const uint DWMSBT_NONE = 1;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void ApplyMicaBackdrop(IntPtr handle, bool enabled = true)
        {
            try { int v = (int)(enabled ? DWMSBT_MAINWINDOW : DWMSBT_NONE); DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int)); }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }

        // DWMWA_WINDOW_CORNER_PREFERENCE (Win11 22H2+) — στρογγυλεμένες γωνίες στο ίδιο το OS-level
        // frame αντί για χειροκίνητο region-clipping (UiTheme.ApplyRoundedRegion, ήδη σε χρήση αλλού
        // σε custom-painted controls) — εδώ θέλουμε το πραγματικό DWM frame, όχι μια ζωγραφισμένη
        // προσομοίωση, άρα διαφορετικό μηχανισμό.
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const uint DWMWCP_ROUND = 2;

        public static void ApplyRoundedCorners(IntPtr handle)
        {
            try { uint v = DWMWCP_ROUND; DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(uint)); }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }

        // Αμυντική διόρθωση για το αναφερόμενο "το drag&drop δεν λειτουργεί": αν η εφαρμογή τρέχει
        // ΚΑΤΑ ΛΑΘΟΣ elevated (π.χ. ξεκίνησε από ένα admin terminal/IDE κατά τη διάρκεια δοκιμών) ενώ
        // ο Explorer/η πηγή του drag τρέχει στο κανονικό (medium) επίπεδο, τα Windows (UIPI) μπλοκάρουν
        // σιωπηλά τα μηνύματα OLE drag-drop προς το "ψηλότερο" παράθυρο — χωρίς κανένα σφάλμα, απλά
        // δεν συμβαίνει τίποτα. Το ChangeWindowMessageFilterEx είναι η τεκμηριωμένη λύση της Microsoft
        // γι' αυτό ακριβώς το σενάριο. Αβλαβές/no-op όταν η εφαρμογή ΔΕΝ τρέχει elevated (η πλειονότητα
        // των περιπτώσεων) — απλά επιτρέπει ρητά μηνύματα που θα περνούσαν ούτως ή άλλως.
        private const uint WM_DROPFILES = 0x0233;
        private const uint WM_COPYDATA = 0x004A;
        private const uint WM_COPYGLOBALDATA = 0x0049;
        private const uint MSGFLT_ALLOW = 1;
        [DllImport("user32.dll")] private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);

        public static void AllowDragDropAcrossUipi(IntPtr handle)
        {
            try
            {
                ChangeWindowMessageFilterEx(handle, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(handle, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(handle, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
            }
            catch (EntryPointNotFoundException) { } // Windows 7-; δεν χρειάζεται εκεί (δεν υπήρχε UIPI messagefilter API ακόμα)
        }

        // Ζητήθηκε ρητά "ενσωμάτωσε... σκιασμένα popup" (MediaPlayerPro: τα context menus/flyouts
        // του έχουν DropShadowEffect) — η καθιερωμένη, native τεχνική των Windows για μια πραγματική
        // (όχι ζωγραφισμένη) σκιά γύρω από ένα borderless popup/dialog είναι το CS_DROPSHADOW class
        // style, που ζωγραφίζει ο ίδιος ο DWM compositor. Εφαρμόζεται ΜΕΤΑ τη δημιουργία του handle
        // (π.χ. dialog.HandleCreated += (_,_) => UiTheme.GlassBlur.ApplyWindowDropShadow(dialog.Handle))
        // ώστε να δουλεύει και σε ανώνυμα "new Form{...}" dialogs χωρίς να χρειάζεται custom
        // υποκλάση που να υπερκαλύπτει CreateParams.
        private const int GCL_STYLE = -26;
        private const int CS_DROPSHADOW = 0x00020000;
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")] private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetClassLong")] private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")] private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetClassLong")] private static extern uint SetClassLong32(IntPtr hWnd, int nIndex, uint dwNewLong);

        // ΔΙΟΡΘΩΣΗ "το scrollbar να μην είναι λευκό σε dark mode": το native scrollbar των
        // WinForms AutoScroll panels ακολουθεί πάντα το ΚΛΑΣΙΚΟ ανοιχτόχρωμο visual style των
        // Windows, ανεξάρτητα από το σκούρο θέμα της ίδιας της εφαρμογής. Το SetWindowTheme
        // (documented, public API, ΟΧΙ undocumented hack) είναι η ίδια τεχνική που χρησιμοποιεί ο
        // ίδιος ο Explorer των Windows για το dark-mode scrollbar του: αλλάζει ΜΟΝΟ ποια οπτική
        // κλάση χρησιμοποιεί το non-client rendering (scrollbar κ.λπ.) του συγκεκριμένου control,
        // "DarkMode_Explorer" αντί για το προεπιλεγμένο "Explorer". Πρέπει να εφαρμοστεί ΜΕΤΑ τη
        // δημιουργία του handle (HandleCreated), αλλιώς αποτυγχάνει σιωπηλά.
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

        public static void ApplyScrollbarTheme(Control control, bool dark)
        {
            void Apply()
            {
                try { SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
                catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            }
            if (control.IsHandleCreated) Apply();
            else control.HandleCreated += (_, _) => Apply();
        }

        public static void ApplyWindowDropShadow(IntPtr handle)
        {
            try
            {
                if (IntPtr.Size == 8)
                {
                    long style = GetClassLongPtr64(handle, GCL_STYLE).ToInt64();
                    SetClassLongPtr64(handle, GCL_STYLE, new IntPtr(style | CS_DROPSHADOW));
                }
                else
                {
                    uint style = GetClassLong32(handle, GCL_STYLE);
                    SetClassLong32(handle, GCL_STYLE, style | CS_DROPSHADOW);
                }
            }
            catch { }
        }

        // opacityFraction: 1.0 = η προεπιλεγμένη "γυάλινη" εμφάνιση (ίδιο baseline alpha με πριν,
        // 0x99), χαμηλότερες τιμές = πιο διάφανο φόντο. Το περιεχόμενο του παραθύρου ΔΕΝ επηρεάζεται
        // ποτέ από αυτή την τιμή.
        public static void Apply(IntPtr handle, Color accentColor, double opacityFraction)
        {
            var baseColor = Color.FromArgb(0x20, 0x20, 0x20);
            const double t = 0.16;
            int r = (int)(baseColor.R + (accentColor.R - baseColor.R) * t);
            int g = (int)(baseColor.G + (accentColor.G - baseColor.G) * t);
            int b = (int)(baseColor.B + (accentColor.B - baseColor.B) * t);
            byte alpha = (byte)Math.Clamp(0x99 * opacityFraction, 18, 255);
            int gradientColor = (alpha << 24) | (r << 16) | (g << 8) | b;

            var accent = new ACCENT_POLICY { AccentState = 3, GradientColor = gradientColor };
            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, SizeOfData = size, Data = accentPtr };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally { Marshal.FreeHGlobal(accentPtr); }
        }

        // ΔΙΟΡΘΩΣΗ πραγματικού bug ("ακόμα μαύρο μέσα, δεν είναι πραγματική διαφάνεια"): η λειτουργία
        // "Πλήρης διαφάνεια" των widgets βασίζεται στο Form.TransparencyKey (color-key, απαιτεί
        // ΑΚΡΙΒΩΣ Color.Black) - όσο το ίδιο παράθυρο έχει ΚΑΙ ενεργό blur-behind accent (Apply, πιο
        // πάνω), ο ίδιος ο DWM αναμειγνύει/τονίζει ΚΑΘΕ pixel του παραθύρου (ακόμα και τα "μαύρα" που
        // προορίζονται για το color-key) με το accent χρώμα - το αποτέλεσμα δεν είναι πια ΑΚΡΙΒΩΣ
        // (0,0,0), άρα δεν ταιριάζει ποτέ με το TransparencyKey και μένει ορατό ως ένα σκούρο/μαύρο
        // συμπαγές μπλοκ αντί να γίνει πραγματική τρύπα διαφάνειας. Λύση: απενεργοποίηση εντελώς της
        // accent policy (AccentState=0) όσο είναι ενεργή η Πλήρης διαφάνεια, ώστε το color-key να
        // δουλέψει χωρίς παρέμβαση του DWM blur.
        // DesktopFences-style (MIT) fence background: a flat tint colour at a given opacity over the desktop, no blur
        // (ACCENT_ENABLE_TRANSPARENTGRADIENT). alpha is 0..255.
        public static void ApplyTint(IntPtr handle, Color color, int alpha)
        {
            int gradientColor = (Math.Clamp(alpha, 0, 255) << 24) | (color.B << 16) | (color.G << 8) | color.R;   // ABGR
            var accent = new ACCENT_POLICY { AccentState = 2, AccentFlags = 2, GradientColor = gradientColor };
            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, SizeOfData = size, Data = accentPtr };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally { Marshal.FreeHGlobal(accentPtr); }
        }

        public static void Disable(IntPtr handle)
        {
            var accent = new ACCENT_POLICY { AccentState = 0, GradientColor = 0 };
            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, SizeOfData = size, Data = accentPtr };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally { Marshal.FreeHGlobal(accentPtr); }
        }

        // Ζητήθηκε ρητά "κλωνοποίηση" του TranslucentTB (μόνο η ασφαλής εκδοχή, ΧΩΡΙΣ DLL injection
        // σε άλλες διεργασίες — αποφασίστηκε ρητά να μείνουμε εκτός του injection/hooking engine).
        // Το SetWindowCompositionAttribute δουλεύει σε OPOIOΔΗΠΟΤΕ hwnd, ακόμα κι αν ανήκει σε άλλη
        // διεργασία: ο DWM compositor το χειρίζεται με βάση το ίδιο το hwnd, όχι κώδικα μέσα στη
        // διεργασία-στόχο — ΙΔΙΑ ακριβώς τεχνική με αυτήν που χρησιμοποιεί το TranslucentTB για να
        // κάνει blur την Taskbar του Explorer χωρίς καμία μορφή injection. Το κουμπί "Εφέ διαφάνειας
        // (Windows)" στις Ρυθμίσεις γράφει ήδη το EnableTransparency registry key + κάνει broadcast
        // (WindowsSystemSettings) — αυτό εδώ είναι το συμπλήρωμα: ζητά απευθείας από τον DWM να κάνει
        // blur-behind την ίδια την Taskbar, που είναι πιο αξιόπιστα ορατό από το να περιμένουμε τον
        // Explorer να διαβάσει μόνος του το registry/broadcast.
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private static System.Collections.Generic.List<IntPtr> FindTaskbarWindows()
        {
            var result = new System.Collections.Generic.List<IntPtr>();
            try
            {
                EnumWindows((hWnd, _) =>
                {
                    var sb = new System.Text.StringBuilder(64);
                    GetClassName(hWnd, sb, sb.Capacity);
                    string cls = sb.ToString();
                    if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") result.Add(hWnd);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return result;
        }

        public static void SetTaskbarTranslucency(bool enabled)
        {
            foreach (var hWnd in FindTaskbarWindows())
            {
                // AccentState 3 = ACCENT_ENABLE_BLURBEHIND (ζωγραφίζει ο ίδιος ο DWM πίσω από το
                // παράθυρο, χωρίς tint — GradientColor=0), AccentState 0 = ACCENT_DISABLED
                // (επαναφορά στην προεπιλεγμένη, αδιαφανή εμφάνιση της Taskbar).
                var accent = new ACCENT_POLICY { AccentState = enabled ? 3 : 0, GradientColor = 0 };
                int size = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);
                    var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, SizeOfData = size, Data = accentPtr };
                    SetWindowCompositionAttribute(hWnd, ref data);
                }
                catch { }
                finally { Marshal.FreeHGlobal(accentPtr); }
            }
        }
    }
}
