using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using MotionDesk.Services;
using MotionDesk.Widgets;
using System.Diagnostics;

namespace MotionDesk.UI
{
    /// <summary>
    /// The real application shell. Feature windows/widgets remain independent,
    /// while this window provides a single navigation point for the desktop manager.
    /// </summary>
    public sealed partial class MainWindow : Form
    {
        private Panel _sidebar = null!;
        private TableLayoutPanel _sidebarLayout = null!;
        private Panel _brandPanel = null!;
        private FlowLayoutPanel _nav = null!;
        private PulsingLogoPanel _brandDot = null!;
        private Label _brandLabel = null!;
        private Label _brandSubLabel = null!;
        private Label _brandVersionLabel = null!;
        private SidebarToggleButton _collapseToggle = null!;
        private System.Windows.Forms.Timer? _sidebarAnimTimer;
        private bool _sidebarCollapsed;
        private const int SidebarExpandedWidth = 232;
        private const int SidebarCollapsedWidth = 80;
        private HeaderWavePanel _header = null!;
        private double _headerWaveT;
        private System.Windows.Forms.Timer? _headerWaveTimer;
        private Panel _headerRule = null!;
        private Panel _content = null!;
        private Label _pageTitle = null!;
        private StatusStrip _status = null!;
        private readonly ToolStripStatusLabel _statusLabel = new("Opsis ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly System.Windows.Forms.Timer _statusTimer;
        private readonly System.Windows.Forms.Timer _brandPulseTimer;
        private double _brandPulseT;
        private NavButton? _activeButton;
        private readonly System.Collections.Generic.Dictionary<string, NavButton> _navButtons = new();
        private readonly System.Collections.Generic.Dictionary<string, Action> _pageBuilders = new();
        private string _currentPageKey = "Dashboard";
        private readonly HotkeyManager _hotkeys;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int value, int size);

        // ΔΙΟΡΘΩΣΗ "λευκό πλαίσιο παραθύρου εμφανίζεται στιγμιαία πάνω-αριστερά κατά την αλλαγή
        // καρτέλας": το MainWindow δεν είχε ΠΟΤΕ εφαρμόσει DWMWA_BORDER_COLOR="none" (μόνο
        // widgets/DeskContainers το είχαν) — το προεπιλεγμένο λεπτό accent-περίγραμμα των Windows
        // 11 γύρω από κάθε top-level παράθυρο είναι κανονικά αόρατο πίσω από το δικό μας σκούρο
        // θέμα, αλλά σε κάποιο ενδιάμεσο frame κατά το Controls.Clear()+Add() του SetPage (βλ.
        // DoubleBuffered πιο κάτω για ένα σχετικό αλλά διαφορετικό bug) γίνεται στιγμιαία ορατό.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            GlassBlur.DisableNativeBorderAccent(Handle);
        }

        public MainWindow()
        {
            // Διορθώνει "λευκά τετραγωνάκια" κατά την αλλαγή σκουρότητας θέματος: η πλήρης
            // ανακατασκευή σελίδας/sidebar σε κάθε ThemeManager.Changed (OnThemeOrLanguageChanged)
            // κάνει Controls.Clear()+Add() σε πολλά containers — χωρίς DoubleBuffered στο ίδιο το
            // Form, τα Windows ζωγραφίζουν προσωρινά το προεπιλεγμένο φόντο (λευκό) στις περιοχές
            // που μόλις αδειάσανε πριν προλάβουν να ζωγραφιστούν τα νέα controls από πάνω.
            //
            // ΔΙΟΡΘΩΣΗ πραγματικού regression bug (επιβεβαιώθηκε ζωντανά): το αρχικό fix πρόσθεσε
            // ΚΑΙ ControlStyles.UserPaint — αυτό λέει στα WinForms "θα ζωγραφίζω μόνος μου ΟΛΟ το
            // φόντο", απενεργοποιώντας το κανονικό WM_ERASEBKGND· αφού το MainWindow ΔΕΝ έχει δικό
            // του πλήρες OnPaint που γεμίζει όλη την περιοχή πελάτη, παλιά pixels από προηγούμενο
            // paint pass έμεναν ορατά κάτω από τα νέα — φαινόταν σαν "διπλό", θολό/φαντασματικό
            // κείμενο (π.χ. "Πίνακας Ελέγχου" σε γιγάντια γράμματα πάνω από το κανονικό). Το απλό
            // DoubleBuffered = true (η καθιερωμένη τεχνική για Form, χωρίς UserPaint) διπλασιάζει
            // την απόδοση χωρίς να πειράξει το background-erase pipeline.
            DoubleBuffered = true;
            Flicker.Attach(this);   // κάθε container/παιδί (τωρινό και μελλοντικό) με double buffering
            var appSettings = AppSettings.Load();
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            Text = "Opsis";
            Icon = LoadApplicationIcon();
            StartPosition = FormStartPosition.CenterScreen;
            // Μεγαλύτερο προεπιλεγμένο μέγεθος — ζητήθηκε ρητά ώστε να χωράει άνετα η νέα σελίδα
            // "Audio Enhancement" (equalizer bands + πολλαπλές ρυθμίσεις + visualizer μαζί).
            MinimumSize = new Size(1040, 680);
            Size = new Size(1280, 800);
            Opacity = Math.Clamp(appSettings.WindowOpacity, 0.6, 1.0);

            _pageBuilders["Dashboard"] = () => SafeBuildPage("Dashboard", ShowDashboard);
            _pageBuilders["Widgets"] = () => SafeBuildPage("Widgets", ShowWidgets);
            _pageBuilders["DeskZones"] = () => SafeBuildPage("DeskZones", ShowDeskZones);
            _pageBuilders["Wallpaper"] = () => SafeBuildPage("Wallpaper", ShowWallpaper);
            _pageBuilders["Settings"] = () => SafeBuildPage("Settings", ShowSettings);
            _pageBuilders["Profiles"] = () => SafeBuildPage("Profiles", ShowProfiles);
            _pageBuilders["Performance"] = () => SafeBuildPage("Performance", ShowPerformance);
            _pageBuilders["Automation"] = () => SafeBuildPage("Automation", ShowAutomation);
            _pageBuilders["Personalization"] = () => SafeBuildPage("Personalization", ShowPersonalization);
            _pageBuilders["About"] = () => SafeBuildPage("About", ShowAbout);
            _pageBuilders["AudioEnhancement"] = () => SafeBuildPage("AudioEnhancement", ShowAudioEnhancement);

            BuildChrome();
            ApplyChromeColors();

            if (appSettings.SidebarCollapsed)
            {
                _sidebarCollapsed = true;
                _sidebar.Width = SidebarCollapsedWidth;
                ApplySidebarCollapsedVisuals(true);
            }

            // "Αναπνέουσα" λάμψη στο λογότυπο του sidebar (όχι πλέον στο tray icon).
            _brandPulseTimer = new System.Windows.Forms.Timer { Interval = HardwareProfileService.ScaleDecorativeInterval(60) };
            _brandPulseTimer.Tick += (_, _) => { _brandPulseT += 0.08; _brandDot.Invalidate(); };
            _brandPulseTimer.Start();

            _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _statusTimer.Tick += (_, _) => UpdateStatus();
            _statusTimer.Start();
            _hotkeys = new HotkeyManager();
            _hotkeys.RegisterCtrlAlt('G', ToggleGamingMode);
            _hotkeys.RegisterCtrlAlt('M', ShowAndActivate);
            // ΝΕΟ, στα πρότυπα του FancyWM (ζητήθηκε ρητά): μετακινεί το τρέχον ενεργό παράθυρο
            // στη γειτονική DeskZone προς αυτή την κατεύθυνση, χωρίς ποντίκι. Ctrl+Alt+Shift (όχι
            // απλό Ctrl+Alt) — το απλό Ctrl+Alt+βελάκι βρέθηκε ήδη δεσμευμένο σε αυτό το μηχάνημα
            // από τον οδηγό γραφικών (περιστροφή οθόνης), επιβεβαιωμένο με αποτυχημένο RegisterHotKey.
            _hotkeys.RegisterCtrlAltShift(Keys.Left, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Left));
            _hotkeys.RegisterCtrlAltShift(Keys.Right, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Right));
            _hotkeys.RegisterCtrlAltShift(Keys.Up, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Up));
            _hotkeys.RegisterCtrlAltShift(Keys.Down, () => ZoneSnapEngine.MoveForegroundWindowToZone(ZoneSnapEngine.ZoneDirection.Down));
            // ΝΕΟ ("πλήρες σετ πληκτρολογίου FancyWM", task tracker t27): Ctrl+Shift+βελάκι (χωρίς
            // Alt) μετακινεί μόνο την ΕΣΤΙΑΣΗ στη γειτονική ζώνη, χωρίς να μετακινήσει κανένα παράθυρο.
            _hotkeys.RegisterCtrlShift(Keys.Left, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Left));
            _hotkeys.RegisterCtrlShift(Keys.Right, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Right));
            _hotkeys.RegisterCtrlShift(Keys.Up, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Up));
            _hotkeys.RegisterCtrlShift(Keys.Down, () => ZoneSnapEngine.FocusAdjacentZone(ZoneSnapEngine.ZoneDirection.Down));

            ThemeManager.UiContext = System.Threading.SynchronizationContext.Current;
            AnimationsSettingChanged += UpdateAnimationTimers;
            WorkspaceProfileService.ExitSaveDone = false;
            ThemeManager.Changed += OnThemeOrLanguageChanged;
            ThemeManager.Repainted += OnThemeRepainted;
            LocalizationManager.Changed += OnThemeOrLanguageChanged;
            SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;

            // Χαμηλού-κόστους hook, τρέχει σε όλη τη διάρκεια ζωής της εφαρμογής — δεν κάνει
            // τίποτα εκτός αν ο χρήστης κρατάει Shift ενώ σέρνει ένα παράθυρο (βλ. ZoneSnapEngine).
            // After an update, show the Version History once (the first time the window is shown with a new version)
            Shown += (_, _) => ShowChangelogAfterUpdate();
            ZoneSnapEngine.AllowOwnWindow = h => IsHandleCreated && h == Handle;
            ZoneSnapEngine.Start();

            FormClosed += (_, _) =>
            {
                if (!WorkspaceProfileService.ExitSaveDone) { try { WorkspaceProfileService.Save("Last Session"); } catch { } }
                AnimationsSettingChanged -= UpdateAnimationTimers;
                ThemeManager.Changed -= OnThemeOrLanguageChanged;
                ThemeManager.Repainted -= OnThemeRepainted;
                LocalizationManager.Changed -= OnThemeOrLanguageChanged;
                SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
                ZoneSnapEngine.Stop();
                _statusTimer.Dispose();
                _brandPulseTimer.Dispose();
                _sidebarAnimTimer?.Dispose();
                _headerWaveTimer?.Dispose();
                _hotkeys.Dispose();
            };

            ShowDashboard();
            var saved = AppSettings.Load();
            // Safe mode (v2.0 handoff, Φάση 1 §S): η "Last Session" περιέχει ακριβώς τα widgets/
            // DeskContainers/wallpaper που ίσως προκάλεσαν τα διαδοχικά crashes — δεν επαναφέρεται.
            if (!SafeModeService.IsSafeMode && saved.RestoreLastSession && WorkspaceProfileService.ListProfiles().Contains("Last Session", StringComparer.OrdinalIgnoreCase))
            {
                try { WorkspaceProfileService.Load("Last Session"); } catch { }
            }
            else if (saved.ShowSystemMonitor && !WidgetHostEngine.Instance.GetActiveWidgets().Any(w => w.WidgetId == "sysmon"))
            {
                WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, 300, 200);
            }
            if (_navButtons.TryGetValue("Dashboard", out var dashboardButton))
                SetActiveButton(dashboardButton);
            UpdateAnimationTimers(); // παράθυρο ακόμη αόρατο (π.χ. --background) → οι animations ξεκινούν μόνο όταν εμφανιστεί
        }

        private void OnWindowsPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General) ThemeManager.RefreshFromWindows();
        }

        // ΠΑΝΤΑ μέσω BeginInvoke (όχι μόνο όταν InvokeRequired) — ζητήθηκε bug fix: όταν η
        // εφαρμογή άνοιγε σε Dark και ο χρήστης διάλεγε "Light" από το FlatComboBox της σελίδας
        // Ρυθμίσεων, η εφαρμογή "δεν αποκρινόταν". Αιτία: το SelectedIndexChanged του combo
        // (μέσα στο click handler του δικού του popup ContextMenuStrip) καλούσε συγχρονισμένα
        // ThemeManager.SetMode -> Changed -> εδώ -> πλήρη ανακατασκευή της τρέχουσας σελίδας
        // (dispose ΟΛΩΝ των controls, ΣΥΜΠΕΡΙΛΑΜΒΑΝΟΜΕΝΟΥ του ίδιου του combo) ΕΝΩ το popup ήταν
        // ακόμη στη μέση του δικού του closing/click-handling — κλασικό re-entrancy hazard, ίδιας
        // οικογένειας με το ήδη τεκμηριωμένο "ΟΧΙ using στο ContextMenuStrip" bug pattern αυτού
        // του project. Το BeginInvoke αναβάλλει την ανακατασκευή μέχρι να ξετυλιχτεί πλήρως το
        // τρέχον event πρώτα.
        private void OnThemeOrLanguageChanged()
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                // SuspendLayout/ResumeLayout γύρω από ΟΛΗ την ανακατασκευή (χρώματα + sidebar +
                // τρέχουσα σελίδα) — χωρίς αυτό, κάθε ενδιάμεσο Controls.Clear()/Add() προκαλούσε
                // δικό του layout+repaint pass, ορατό ως στιγμιαία "λευκά τετραγωνάκια" στο σημείο
                // που μόλις άδειασε πριν γεμίσει ξανά.
                Flicker.BatchRedraw(this, () =>
                {
                    SuspendLayout();
                    try
                    {
                        ApplyChromeColors();
                        RebuildSidebarNav();
                        if (_pageBuilders.TryGetValue(_currentPageKey, out var builder)) builder();
                    }
                    finally { ResumeLayout(true); }
                });
            }));
        }

        // Φτηνιά ζωντανή προεπισκόπηση ενώ σέρνεις το slider σκουρότητας — καμία ανακατασκευή
        // control tree (αυτό παραμένει αποκλειστικά στο MouseUp μέσω Changed/OnThemeOrLanguageChanged).
        //
        // Bug fix: πριν, εδώ γινόταν ΜΟΝΟ Invalidate(true) — αυτό ενημερώνει ζωντανά μόνο τα
        // owner-draw controls (NavButton/StatCard/PulsingLogoPanel κ.λπ., που διαβάζουν το
        // UiTheme.* ζωντανά μέσα στο δικό τους OnPaint), ενώ τα απλά Panel (_content/_header/
        // _status κ.λπ.) ζωγραφίζουν με το ΗΔΗ αποθηκευμένο BackColor property τους — που ΔΕΝ
        // αλλάζει μόνο του από ένα Invalidate. Αυτό ακριβώς εξηγούσε το αναφερόμενο σύμπτωμα "η
        // σκουρότητα εφαρμόζεται μόνο στο πλευρικό μενού και στην κορυφή (τα owner-draw κομμάτια),
        // όχι σε όλο το παράθυρο" όσο ο χρήστης έσερνε το slider. Το ApplyChromeColors() είναι
        // φτηνό (μόνο αναθέσεις χρωμάτων σε ήδη υπάρχοντα controls, καμία Controls.Clear()/Add()),
        // οπότε ασφαλές να τρέχει σε κάθε tick χωρίς να επαναφέρει το αρχικό flicker bug.
        private void OnThemeRepainted()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(OnThemeRepainted)); return; }
            ApplyChromeColors();
            Invalidate(true);
        }

        // 1.7.8 - διπλό buffering για container controls (Panel/TableLayoutPanel/FlowLayoutPanel) που δεν το
        // εκθέτουν δημόσια: χωρίς αυτό το animation πλάτους του sidebar άφηνε "κυματισμούς" από παλιά καρέ.
        private static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(c, true);
            }
            catch { }
        }

        private void BuildChrome()
        {
            _sidebar = new Panel { Dock = DockStyle.Left, Width = SidebarExpandedWidth, Padding = new Padding(16, 20, 16, 12) };
            EnableDoubleBuffer(_sidebar);

            // TableLayoutPanel αντί για Dock=Top (brand) + Dock=Fill (nav) απευθείας μέσα στο
            // padded _sidebar: σε αυτόν τον συνδυασμό το Fill rectangle του _nav υπολογιζόταν
            // λανθασμένα σαν να μην υπήρχε καθόλου δεσμευμένος χώρος από το _brandPanel (bug
            // που ΔΕΝ διορθωνόταν ούτε με ρητό PerformLayout/Invalidate μετά το Shown), με
            // αποτέλεσμα το _nav να ξεκινάει επικαλυπτόμενο με το brand και να κρύβει εντελώς
            // το πρώτο κουμπί πλοήγησης (Dashboard). Οι σταθερές δύο σειρές ενός
            // TableLayoutPanel δεν έχουν αυτή την ασάφεια.
            _sidebarLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            EnableDoubleBuffer(_sidebarLayout);
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _sidebar.Controls.Add(_sidebarLayout);

            _brandPanel = new Panel { Dock = DockStyle.Fill };
            // Το ίδιο σχέδιο με το Opsis.ico (τετράγωνο, στρογγυλεμένο, gradient, δύο
            // κυματιστές γραμμές) αντί για απλό κύκλο. Οι γραμμές μετατοπίζουν φάση με τον
            // χρόνο (πραγματική κίνηση κύματος) — το halo έχει ΣΤΑΘΕΡΟ μέγεθος (μόνο η
            // διαφάνειά του παλλεται) ώστε να μη δίνει εντύπωση "μεγέθυνσης/σμίκρυνσης".
            // Μεγαλύτερο λογότυπο + τίτλος (ζητήθηκε ρητά, ξανά μεγαλύτερο για να ισορροπεί με
            // τις 3 γραμμές κειμένου παρακάτω) — όλες οι συντεταγμένες σχεδίασης παραμένουν στο
            // αρχικό 36x36 grid και κλιμακώνονται μέσω ScaleTransform, ώστε το μέγεθος να
            // ρυθμίζεται μόνο αλλάζοντας το Size παρακάτω.
            const int logoSize = 56;
            _brandDot = new PulsingLogoPanel { Size = new Size(logoSize, logoSize), Location = new Point(0, 2) };
            _brandDot.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Δυναμική κλιμάκωση βάσει του ΤΡΕΧΟΝΤΟΣ Width του control (όχι της σταθεράς
                // logoSize) — έτσι το λογότυπο μπορεί να ξαναμεγεθυνθεί όταν συμπτύσσεται το
                // sidebar, χωρίς να "κόβεται" σε ένα στενότερο compact μενού.
                float scale = ((Control)s!).Width / 36f;
                g.ScaleTransform(scale, scale);
                double glowAlpha = Math.Sin(_brandPulseT) * 0.5 + 0.5;
                double wave = _brandPulseT * 1.6;

                using (var halo = new SolidBrush(Color.FromArgb((int)(25 + glowAlpha * 55), UiTheme.AccentCyan)))
                    g.FillEllipse(halo, 18 - 20, 18 - 20, 40, 40);

                var rect = new Rectangle(2, 2, 32, 32);
                using (var bgBrush = new LinearGradientBrush(rect, UiTheme.AccentCyan, UiTheme.AccentBlue, 45f))
                using (var path = UiTheme.RoundedPath(rect, 8))
                    g.FillPath(bgBrush, path);

                // Glossy highlight (ζητήθηκε ρητά): απαλή λευκή λάμψη στο πάνω μισό, σαν
                // γυαλιστερό κουμπί — clipped στο ίδιο rounded-rect ώστε να μη ξεχειλίζει.
                using (var glossPath = UiTheme.RoundedPath(rect, 8))
                {
                    var oldClip = g.Clip;
                    g.SetClip(glossPath, System.Drawing.Drawing2D.CombineMode.Intersect);
                    var glossRect = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height / 2 + 4);
                    using (var glossBrush = new LinearGradientBrush(glossRect, Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillRectangle(glossBrush, glossRect);
                    g.Clip = oldClip;
                }

                // "Trace" λογότυπο (επιλέχθηκε από τον χρήστη ανάμεσα σε 5 προτάσεις): το ίδιο κύμα
                // παραμένει σταθερό σαν ίχνος/track, και ένας φωτεινός κόμβος το ξαναδιατρέχει σε
                // βρόχο — ίδια ιδέα με το νέο assets/Opsis.ico και τα tray frames (scripts/generate_*.ps1),
                // εδώ ζωντανή/συνεχής αντί για 6 διακριτά καρέ, πάνω στο ήδη υπάρχον animation timer.
                PointF P0 = new(8, 20), P1 = new(13, 13), P2 = new(21, 27), P3 = new(26, 20);
                using (var pen1 = new Pen(Color.FromArgb(150, 255, 255, 255), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawBezier(pen1, P0, P1, P2, P3);

                double t = (_brandPulseT * 0.22) % 1.0;
                double mt = 1 - t;
                float nx = (float)(mt * mt * mt * P0.X + 3 * mt * mt * t * P1.X + 3 * mt * t * t * P2.X + t * t * t * P3.X);
                float ny = (float)(mt * mt * mt * P0.Y + 3 * mt * mt * t * P1.Y + 3 * mt * t * t * P2.Y + t * t * t * P3.Y);
                using (var nodeHalo = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                    g.FillEllipse(nodeHalo, nx - 5, ny - 5, 10, 10);
                using (var nodeDot = new SolidBrush(Color.White))
                    g.FillEllipse(nodeDot, nx - 2.3f, ny - 2.3f, 4.6f, 4.6f);
            };
            _brandPanel.Controls.Add(_brandDot);
            const int brandTextX = logoSize + 10;
            int brandLabelWidth = SidebarExpandedWidth - brandTextX - _sidebar.Padding.Horizontal;
            _brandLabel = new Label { AutoSize = true, Location = new Point(brandTextX, 2), Text = "Opsis", Font = new Font("Segoe UI", 16f, FontStyle.Bold), AutoEllipsis = true };
            _brandPanel.Controls.Add(_brandLabel);
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            // Ζητήθηκε ρητά "εφόσον στον τίτλο η έκδοση δεν φαίνεται, βάλε την σε τρίτη γραμμή από
            // κάτω" — στο προηγούμενο πέρασμα η έκδοση ήταν στην ΙΔΙΑ γραμμή με το tagline
            // ("Desktop Customization  ·  vX.X.X"), και το AutoEllipsis έκοβε το κείμενο πριν καν
            // φτάσει στην έκδοση (το σύνολο δεν χωρούσε στα ~142px). Τώρα 3 ξεχωριστές γραμμές:
            // τίτλος / tagline / έκδοση — η καθεμία χωράει πλέον άνετα μόνη της.
            _brandSubLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(brandTextX, 30),
                Size = new Size(brandLabelWidth, 16),
                Text = "Desktop Customization",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            _brandPanel.Controls.Add(_brandSubLabel);
            _brandVersionLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(brandTextX, 46),
                Size = new Size(brandLabelWidth, 16),
                Text = $"v{version?.ToString(3) ?? "1.0.0"}",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            _brandPanel.Controls.Add(_brandVersionLabel);
            _sidebarLayout.Controls.Add(_brandPanel, 0, 0);

            // ΟΧΙ AutoScroll: μια κάθετη scrollbar (όταν τα 9 nav items δεν χωρούν καθ' ύψος)
            // "έτρωγε" πλάτος από το ήδη στενό, συμπτυγμένο μενού (64px), προκαλώντας ΚΑΙ
            // οριζόντια scrollbar σε καταρράκτη — αφού τα τετράγωνα πλακίδια είχαν υπολογιστεί
            // να γεμίζουν ΑΚΡΙΒΩΣ το διαθέσιμο πλάτος, χωρίς περιθώριο. Ζητήθηκε ρητά καμία
            // scrollbar· καλύτερα να κόβεται καθαρά ένα τελευταίο στοιχείο παρά να εμφανίζεται.
            _nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Padding = new Padding(0, 14, 0, 0) };
            EnableDoubleBuffer(_nav);
            _sidebarLayout.Controls.Add(_nav, 0, 1);
            RebuildSidebarNav();

            _collapseToggle = new SidebarToggleButton();
            _collapseToggle.Click += (_, _) => ToggleSidebarCollapsed();
            _sidebarLayout.Controls.Add(_collapseToggle, 0, 2);

            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 24, 32, 24) };
            EnableDoubleBuffer(_content);

            // Απαλό, κινούμενο κύμα-φόντο πίσω από τον τίτλο κάθε σελίδας (ζητήθηκε ρητά) — ίδια
            // "γλώσσα" με το wallpaper/λογότυπο, αλλά πολύ χαμηλής έντασης ώστε να μην αποσπά.
            _header = new HeaderWavePanel(() => _headerWaveT) { Dock = DockStyle.Top, Height = 64 };
            _pageTitle = new Label { Dock = DockStyle.Fill, Text = "Dashboard", Font = UiTheme.FontHeading, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
            _header.Controls.Add(_pageTitle);
            _headerRule = new Panel { Dock = DockStyle.Bottom, Height = 1 };
            _header.Controls.Add(_headerRule);
            _headerWaveTimer = new System.Windows.Forms.Timer { Interval = HardwareProfileService.ScaleDecorativeInterval(45) };
            _headerWaveTimer.Tick += (_, _) => { _headerWaveT += 0.03; _header.Invalidate(); };
            _headerWaveTimer.Start();

            _status = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _status.Items.Add(_statusLabel);

            Controls.Add(_content);
            Controls.Add(_header);
            Controls.Add(_sidebar);
            Controls.Add(_status);
        }

        // Οι διακοσμητικές animations (παλμός λογότυπου, κύμα header) τρέχαν συνεχώς σε 20Hz/16Hz
        // ακόμη κι όταν το παράθυρο ήταν κρυμμένο στο tray ή ελαχιστοποιημένο — άσκοπη κατανάλωση
        // CPU/GPU για μια εφαρμογή που ζει κυρίως στο παρασκήνιο. Τρέχουν μόνο όταν φαίνονται.
        // Καλείται και από τη ρύθμιση Animations ώστε η αλλαγή να ισχύει αμέσως.
        internal static event Action? AnimationsSettingChanged;

        private void UpdateAnimationTimers()
        {
            bool visible = Visible && WindowState != FormWindowState.Minimized && AppSettings.Load().EnableAnimations;
            if (_brandPulseTimer != null) _brandPulseTimer.Enabled = visible;
            if (_headerWaveTimer != null) _headerWaveTimer.Enabled = visible;
        }

        // ΔΙΟΡΘΩΣΗ πραγματικού bug: το κλείσιμο (X) του κύριου παραθύρου το ΚΑΤΕΣΤΡΕΦΕ — και το
        // FormClosed handler σταματούσε ZoneSnapEngine + όλα τα global hotkeys (Ctrl+Alt+G/M/Shift+βέλη)
        // ενώ η εφαρμογή συνέχιζε να ζει στο tray (DeskZones/συντομεύσεις "έσβηναν" σιωπηλά), και το
        // επόμενο άνοιγμα ξανάτρεχε όλον τον constructor (επαναφορά "Last Session" ξανά). Η ρύθμιση
        // MinimizeToTray (προεπιλογή true) υπήρχε αλλά ΔΕΝ χρησιμοποιούνταν πουθενά. Τώρα το X κρύβει
        // στο tray· η πραγματική έξοδος γίνεται μόνο από το "Έξοδος" του tray menu (CloseReason != UserClosing).
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && AppSettings.Load().MinimizeToTray)
            {
                e.Cancel = true;
                // Φεύγουμε από βαριές σελίδες (live equalizer: WASAPI capture + timer 25Hz) ώστε να μην
                // τρέχουν αόρατες στο παρασκήνιο.
                if (_currentPageKey is "AudioEnhancement" or "Performance") NavigateTo("Dashboard");
                Hide();
                return;
            }
            // Πραγματικό κλείσιμο (έξοδος/shutdown): αποθήκευση ΤΩΡΑ, όσο τα widgets ζουν ακόμα.
            if (!WorkspaceProfileService.ExitSaveDone)
            {
                try { WorkspaceProfileService.Save("Last Session"); WorkspaceProfileService.ExitSaveDone = true; } catch { }
            }
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimationTimers(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateAnimationTimers(); }

        // Controls.Clear() ΜΟΝΟ αφαιρεί — δεν κάνει Dispose. Οι σελίδες Widgets/DeskZones/DeskStrip ξαναχτίζουν
        // τις λίστες τους κάθε 2s, άρα πριν διέρρεαν panels/labels/buttons/Regions συνεχώς όσο ήταν ανοιχτή η σελίδα.
        private static void DisposeChildren(Control parent)
        {
            foreach (Control child in parent.Controls.Cast<Control>().ToList()) child.Dispose();
            parent.Controls.Clear();
        }

        private void RebuildSidebarNav()
        {
            DisposeChildren(_nav);
            _navButtons.Clear();
            _activeButton = null;
            AddNavButton(_nav, LocalizationManager.T("Nav.Dashboard"), "Dashboard", ShowDashboard, 1);
            AddNavButton(_nav, LocalizationManager.T("Nav.Widgets"), "Widgets", ShowWidgets, 2);
            AddNavButton(_nav, LocalizationManager.T("Nav.DeskZones"), "DeskZones", ShowDeskZones, 3);
            AddNavButton(_nav, LocalizationManager.T("Nav.Wallpaper"), "Wallpaper", ShowWallpaper, 4);
            AddNavButton(_nav, LocalizationManager.T("Nav.Performance"), "Performance", ShowPerformance, 5);
            AddNavButton(_nav, LocalizationManager.T("Nav.Settings"), "Settings", ShowSettings, 6);
            AddNavButton(_nav, LocalizationManager.T("Nav.Profiles"), "Profiles", ShowProfiles, 7);
            AddNavButton(_nav, LocalizationManager.T("Nav.Automation"), "Automation", ShowAutomation, 8);
            AddNavButton(_nav, LocalizationManager.T("Nav.Personalization"), "Personalization", ShowPersonalization, 0);
            // Ζητήθηκε ρητά: αντί για απλό "Audio Visualizer", νέα καρτέλα "Διαχείριση Ήχου" με
            // presets ενίσχυσης + ζωντανό visualizer, εμπνευσμένη από τη λογική του FXSound.
            AddNavButton(_nav, LocalizationManager.T("Nav.AudioEnhancement"), "AudioEnhancement", ShowAudioEnhancement, (string?)null);
            // Σελίδες των plugin (π.χ. News Crawler) — μπαίνουν πριν το "Σχετικά" που μένει τελευταίο.
            foreach (var pluginItem in Services.PluginHost.Instance.GetNavigationItems())
            {
                var captured = pluginItem;
                AddNavButton(_nav, Services.PluginHost.TitleOf(captured), "plugin:" + captured.Key, () => ShowPluginPage(captured), (string?)null);
            }
            // "Σχετικά" ζητήθηκε ρητά να είναι το ΤΕΛΕΥΤΑΙΟ στοιχείο του nav, κάτω από τη
            // "Διαχείριση Ήχου" (πριν ήταν ανάποδα).
            AddNavButton(_nav, LocalizationManager.T("Nav.About"), "About", ShowAbout, 9);

            if (_sidebarCollapsed) foreach (var btn in _navButtons.Values) btn.Collapsed = true;
            if (_navButtons.TryGetValue(_currentPageKey, out var active)) SetActiveButton(active);
            EnsureSidebarFits();
        }

        // Plugins add pages to the side menu: the window grows (up to the screen) so the whole menu stays visible; if the screen is too small the menu scrolls.
        private void EnsureSidebarFits()
        {
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || WindowState != FormWindowState.Normal) return;
                int need = _nav.Padding.Vertical + _nav.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
                int missing = need - _nav.ClientSize.Height;
                if (missing > 0)
                {
                    var wa = Screen.FromControl(this).WorkingArea;
                    int newHeight = Math.Min(Height + missing + 8, wa.Height);
                    if (newHeight > Height)
                    {
                        Height = newHeight;
                        if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
                    }
                }
                _nav.AutoScroll = need > _nav.ClientSize.Height + 1;
            }));
        }

        private void ApplyChromeColors()
        {
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.TextPrimary;
            _sidebar.BackColor = UiTheme.Sidebar;
            _sidebarLayout.BackColor = UiTheme.Sidebar;
            _brandPanel.BackColor = UiTheme.Sidebar;
            _brandDot.BackColor = UiTheme.Sidebar;
            _content.BackColor = UiTheme.Background;
            _header.BackColor = UiTheme.Background;
            _headerRule.BackColor = UiTheme.Border;
            _pageTitle.ForeColor = UiTheme.TextPrimary;
            _status.BackColor = UiTheme.Sidebar;
            _statusLabel.ForeColor = UiTheme.TextSecondary;
            _brandLabel.ForeColor = UiTheme.TextPrimary;
            _brandSubLabel.ForeColor = UiTheme.TextMuted;
            _brandVersionLabel.ForeColor = UiTheme.TextMuted;

            int useDark = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0;
            try { if (IsHandleCreated) DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)); }
            catch (DllNotFoundException) { }

            // Handoff Φάση 8: Mica backdrop + στρογγυλεμένες γωνίες στο κύριο παράθυρο — σιωπηλό
            // no-op σε Win10/παλαιότερο Win11 (βλ. σχόλιο στο UiTheme.ApplyMicaBackdrop). Fallback σε
            // Low-End ικανότητα: καθόλου Mica (μένει το συμπαγές UiTheme.Background από πάνω, ήδη
            // εφαρμοσμένο) — ζητήθηκε ρητά "fallback σε συμπαγές χρώμα σε Low-End".
            if (IsHandleCreated)
            {
                GlassBlur.ApplyMicaBackdrop(Handle, enabled: Services.HardwareProfileService.CurrentTier != Services.HardwareTier.Low);
                GlassBlur.ApplyRoundedCorners(Handle);
            }

            Invalidate(true);
        }

        private NavButton AddNavButton(Control parent, string text, string key, Action action, int shortcutIndex) =>
            AddNavButton(parent, text, key, action, $"Ctrl+{shortcutIndex}");

        private NavButton AddNavButton(Control parent, string text, string key, Action action, string? shortcutLabel)
        {
            var button = new NavButton(key, text, shortcutLabel) { Width = 184 };
            button.Click += (_, _) => { _currentPageKey = key; SafeBuildPage(key, action); };
            button.Click += (_, _) => SetActiveButton(button);
            button.Click += (_, _) => Services.UiSounds.PlayClick();
            parent.Controls.Add(button);
            _navButtons[key] = button;
            return button;
        }

        /// <summary>Επιτρέπει σε εξωτερικό caller (π.χ. το tray context menu) να ανοίξει το
        /// κύριο παράθυρο κατευθείαν σε συγκεκριμένη σελίδα αντί για το Dashboard.</summary>
        public void NavigateTo(string pageKey)
        {
            if (_navButtons.TryGetValue(pageKey, out var button))
                button.PerformClick();
        }

        private void SetActiveButton(NavButton button)
        {
            if (_activeButton != null)
            {
                _activeButton.IsActive = false;
                _activeButton.Invalidate();
            }

            _activeButton = button;
            _activeButton.IsActive = true;
            _activeButton.Invalidate();
        }

        // Απλό double-buffered Panel για το owner-draw λογότυπο — χωρίς αυτό, οι συχνές
        // Invalidate() κλήσεις του animation timer προκαλούσαν εμφανές flicker.
        private sealed class PulsingLogoPanel : Panel
        {
            public PulsingLogoPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
        }

        // Header background: 3 πολύ χαμηλής έντασης κυματιστές γραμμές που ταξιδεύουν αργά,
        // πίσω από τον τίτλο κάθε σελίδας — ζητήθηκε ρητά "κινούμενο φόντο με κύματα".
        private sealed class HeaderWavePanel : Panel
        {
            private readonly Func<double> _getT;
            public HeaderWavePanel(Func<double> getT)
            {
                _getT = getT;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(UiTheme.Background); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels

                double t = _getT();
                var colors = new[] { UiTheme.AccentCyan, UiTheme.AccentBlue, UiTheme.AccentCyan };
                for (int i = 0; i < 3; i++)
                {
                    double phase = t * (0.5 + i * 0.15) + i * 1.7;
                    double amp = 6 + i * 3;
                    double baseline = Height * (0.35 + i * 0.28);
                    var pts = new System.Collections.Generic.List<PointF>();
                    for (int x = 0; x <= Width; x += 16)
                        pts.Add(new PointF(x, (float)(baseline + Math.Sin(phase + x * 0.012) * amp)));
                    if (pts.Count < 2) continue;
                    using var pen = new Pen(Color.FromArgb(38 - i * 6, colors[i]), 1.6f) { LineJoin = LineJoin.Round };
                    g.DrawLines(pen, pts.ToArray());
                }
            }
        }

        // Owner-draw nav item: εικονίδιο (Segoe Fluent Icons) + ετικέτα, με accent bar και
        // hover/active state. Βασισμένο σε Panel (όχι Button) — ένα ButtonBase-specific quirk
        // (η αυτόματη "default button" ειδοποίηση του Form προς το πρώτο Button ενός container)
        // εμπόδιζε ολοκληρωτικά το πρώτο owner-draw Button του sidebar να ζωγραφιστεί ποτέ.
        private sealed class NavButton : Panel
        {
            private readonly string _iconKey;
            private bool _hover;
            private bool _collapsed;
            public bool IsActive { get; set; }
            public new event EventHandler? Click;

            // Σε "συμπτυγμένη" λειτουργία δείχνει ΜΟΝΟ το (μεγαλύτερο, κεντραρισμένο) εικονίδιο —
            // ζητήθηκε ρητά: το πλευρικό μενού να μπορεί να συμπτύσσεται με μεγαλύτερα εικονίδια.
            public bool Collapsed
            {
                get => _collapsed;
                set
                {
                    if (_collapsed == value) return;
                    _collapsed = value;
                    // 56 (όχι ίσο με το πλήρες πλάτος του container, 64) — σκόπιμο περιθώριο
                    // ασφαλείας ώστε τίποτα να μην ακουμπάει/κόβεται στο δεξί άκρο, ό,τι κι αν
                    // προκαλούσε το προηγούμενο ζήτημα ("κόβονται τα εικονίδια").
                    if (!SuppressWidth) ApplyWidth();
                    Invalidate();
                }
            }

            // 1.7.8 - κατά το animation του sidebar το πλάτος του κάθε κουμπιού ΑΚΟΛΟΥΘΕΙ το πλάτος του sidebar
            // (ορίζεται από το MainWindow ανά καρέ) αντί να πηδά αμέσως 56 <-> 184 - αυτό προκαλούσε τα "κύματα".
            public bool SuppressWidth { get; set; }
            public void ApplyWidth() => Width = _collapsed ? 56 : 184;

            private readonly ToolTip _tip = new() { InitialDelay = 300, AutoPopDelay = 4000 };
            private readonly string? _shortcutHint;

            // Tooltip με τη συντόμευση πληκτρολογίου (π.χ. "Widgets (Ctrl+2)") — πάντα ορατό,
            // όχι μόνο στη συμπτυγμένη λειτουργία, ώστε οι συντομεύσεις να είναι ανακαλύψιμες
            // μέσα στο ίδιο το UI (ζητήθηκε ρητά στο roadmap).
            public NavButton(string iconKey, string text, string? shortcutHint = null)
            {
                _iconKey = iconKey;
                Text = text;
                _shortcutHint = shortcutHint;
                Height = 42;
                Margin = new Padding(0, 0, 0, 4);
                TabStop = false;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
                if (_shortcutHint != null) _tip.SetToolTip(this, $"{Text} ({_shortcutHint})");
            }

            public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var iconColor = IsActive ? UiTheme.AccentCyan : UiTheme.TextSecondary;

                // Συμπτυγμένη λειτουργία: ξεχωριστό, τετράγωνο πλακίδιο-κουμπί (όχι το ίδιο
                // επίμηκες pill του πλήρους sidebar) με μεγαλύτερο εικονίδιο — ζητήθηκε ρητά
                // "τετράγωνα κουμπιά" + μεγαλύτερα εικονίδια στη σύμπτυξη.
                if (_collapsed)
                {
                    // Το πλακίδιο ήταν 42 px μέσα σε κουμπί ύψους 42 px: το κάτω περίγραμμα κοβόταν και το πάνω έβγαινε παχύτερο.
                    // Τώρα 40 px με 1 px περιθώριο, και το περίγραμμα πέφτει ακριβώς πάνω σε pixel (offset 0,5), ίδιο πάχος σε όλες τις πλευρές.
                    const int tile = 40;
                    var tileRect = new Rectangle((Width - tile) / 2, (Height - tile) / 2, tile - 1, tile - 1);
                    var bg = IsActive ? UiTheme.SurfaceHover : (_hover ? UiTheme.Surface : UiTheme.Sidebar);
                    g.TranslateTransform(0.5f, 0.5f);
                    using (var bgBrush = new SolidBrush(bg))
                    using (var path = UiTheme.RoundedPath(tileRect, 6))
                        g.FillPath(bgBrush, path);
                    using (var borderPen = new Pen(IsActive ? UiTheme.AccentCyan : UiTheme.Border, IsActive ? 1.6f : 1f))
                    using (var path = UiTheme.RoundedPath(tileRect, 6))
                        g.DrawPath(borderPen, path);
                    g.ResetTransform();

                    const int iconSize = 26;
                    IconRenderer.Draw(g, _iconKey, new RectangleF(tileRect.X + (tile - iconSize) / 2f, tileRect.Y + (tile - iconSize) / 2f, iconSize, iconSize), iconColor);
                    return;
                }

                var expandedBg = IsActive ? UiTheme.SurfaceHover : (_hover ? UiTheme.Surface : UiTheme.Sidebar);
                using (var bgBrush = new SolidBrush(expandedBg))
                using (var path = UiTheme.RoundedPath(new Rectangle(4, 2, Width - 8, Height - 4), 8))
                    g.FillPath(bgBrush, path);

                if (IsActive)
                {
                    using var accentBrush = new SolidBrush(UiTheme.AccentCyan);
                    g.FillRectangle(accentBrush, 0, 8, 3, Height - 16);
                }

                IconRenderer.Draw(g, _iconKey, new RectangleF(14, (Height - 20) / 2f, 20, 20), iconColor);

                var textColor = IsActive ? UiTheme.TextPrimary : UiTheme.TextSecondary;
                using var textFont = new Font("Segoe UI", 9.5f, IsActive ? FontStyle.Bold : FontStyle.Regular);
                using (var textBrush = new SolidBrush(textColor))
                    g.DrawString(Text, textFont, textBrush, 42, (Height - textFont.Height) / 2f);
            }
        }

        // Μικρό κουμπί σύμπτυξης/επέκτασης στο κάτω μέρος του sidebar — το βέλος αλλάζει
        // κατεύθυνση ανάλογα με την τρέχουσα κατάσταση.
        private sealed class SidebarToggleButton : Panel
        {
            private bool _hover;
            private bool _collapsed;
            public new event EventHandler? Click;

            public bool Collapsed { get => _collapsed; set { _collapsed = value; Invalidate(); } }

            public SidebarToggleButton()
            {
                Dock = DockStyle.Fill;
                Cursor = Cursors.Hand;
                // ResizeRedraw: το κουμπί ξαναζωγραφίζεται σε ΚΑΘΕ αλλαγή πλάτους (animation) ώστε το βέλος να μένει
                // κεντραρισμένο και να μη μένουν υπολείμματα ("κυματισμοί") από παλιά καρέ.
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_hover)
                {
                    using var bg = new SolidBrush(UiTheme.Surface);
                    using var path = UiTheme.RoundedPath(new Rectangle(4, 2, Width - 8, Height - 4), 8);
                    g.FillPath(bg, path);
                }
                using var pen = new Pen(UiTheme.TextSecondary, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float cx = Width / 2f, cy = Height / 2f;
                float dir = _collapsed ? 1 : -1;
                g.DrawLines(pen, new[] { new PointF(cx + 3 * dir, cy - 5), new PointF(cx - 3 * dir, cy), new PointF(cx + 3 * dir, cy + 5) });
            }
        }

        // Ζωντανή animation (ease-out) για το πλάτος του sidebar κατά τη σύμπτυξη/επέκταση —
        // ζητήθηκε ρητά "εφέ κύλισης" αντί για στιγμιαία αλλαγή μεγέθους.
        private void ToggleSidebarCollapsed()
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            var appSettings = AppSettings.Load();
            appSettings.SidebarCollapsed = _sidebarCollapsed;
            appSettings.Save();

            // Η σειρά έχει σημασία (πριν όχι — αυτό έδινε ένα "άκυρο" απότομο snap): όταν
            // ΣΥΜΠΤΥΣΣΟΥΜΕ, πρώτα ζωγραφίζουμε πλήρες μενού που απλά στενεύει/κόβεται καθώς
            // τρέχει το animation, και ΜΟΝΟ όταν φτάσει στο τελικό στενό πλάτος αλλάζουμε σε
            // τετράγωνα πλακίδια/μεγάλα εικονίδια. Όταν ΕΠΕΚΤΕΙΝΟΥΜΕ, κάνουμε το αντίστροφο:
            // πρώτα ετοιμάζουμε την πλήρη διάταξη, μετά φαρδαίνει το sidebar να την αποκαλύψει.
            if (_sidebarCollapsed)
                AnimateSidebarWidth(SidebarCollapsedWidth, () => ApplySidebarCollapsedVisuals(true));
            else
            {
                ApplySidebarCollapsedVisuals(false);
                _collapseToggle.Collapsed = true; // το βέλος αλλάζει κατεύθυνση ΜΟΝΟ όταν τελειώσει η επέκταση
                AnimateSidebarWidth(SidebarExpandedWidth, () => _collapseToggle.Collapsed = false);
            }
        }

        // Με πλάτος sidebar 64px στη συμπτυγμένη λειτουργία και το ΙΔΙΟ Padding(16,...,16,...)
        // του πλήρους sidebar, το χρησιμοποιήσιμο πλάτος έπεφτε στα 32px — μικρότερο από το
        // λογότυπο (48px) και τα τετράγωνα πλακίδια των κουμπιών (48px), δηλαδή "δεν χωρούσαν".
        // Εδώ μειώνουμε το πλευρικό padding ΚΑΙ ξαναμεγεθύνουμε/κεντράρουμε το λογότυπο ώστε να
        // χωράει σωστά στο πραγματικά διαθέσιμο πλάτος.
        private void ApplySidebarCollapsedVisuals(bool collapsed)
        {
            _sidebar.Padding = collapsed ? new Padding(8, 20, 8, 12) : new Padding(16, 20, 16, 12);

            // Ζητήθηκε ρητά "όταν το πλευρικό μενού γίνεται μόνο εικονίδια να μην μικραίνει το
            // εικονίδιο της εφαρμογής & να είναι aligned με εκείνα του πλευρικού μενού" — πριν
            // μίκραινε (48->40) και κεντραριζόταν ανεξάρτητα μέσα στο usableWidth, όχι στο ίδιο
            // σημείο με τα τετράγωνα πλακίδια (42px) των NavButton, που κεντράρονται μέσα στο δικό
            // τους πλάτος 56px (δηλ. κέντρο στο x=28 μέσα στο padded περιεχόμενο). Το λογότυπο
            // μένει πλέον στο ΙΔΙΟ μέγεθος (48) και στο ίδιο κέντρο (x=28) με τα nav εικονίδια.
            const int logoSizeFixed = 48;
            const int navTileCenterX = 28;
            if (collapsed)
            {
                _brandDot.Size = new Size(logoSizeFixed, logoSizeFixed);
                _brandDot.Location = new Point(navTileCenterX - logoSizeFixed / 2, 6);
            }
            else
            {
                _brandDot.Size = new Size(logoSizeFixed, logoSizeFixed);
                _brandDot.Location = new Point(0, 2);
            }

            _collapseToggle.Collapsed = collapsed;
            _brandLabel.Visible = !collapsed;
            _brandSubLabel.Visible = !collapsed;
            _brandVersionLabel.Visible = !collapsed;
            foreach (var btn in _navButtons.Values) { btn.Collapsed = collapsed; btn.Refresh(); }
            _nav.Refresh();
        }

        private void AnimateSidebarWidth(int targetWidth, Action? onComplete = null)
        {
            // Η ρύθμιση "Animations" (Ρυθμίσεις → Εκκίνηση) δεν είχε καμία επίδραση — τώρα την τηρούμε.
            if (!AppSettings.Load().EnableAnimations)
            {
                _sidebar.Width = targetWidth;
                foreach (var btn in _navButtons.Values) { btn.SuppressWidth = false; btn.ApplyWidth(); }
                onComplete?.Invoke();
                return;
            }
            _sidebarAnimTimer?.Stop();
            _sidebarAnimTimer?.Dispose();
            int startWidth = _sidebar.Width;
            // 1.7.8 - animation βασισμένη στον ΠΡΑΓΜΑΤΙΚΟ χρόνο (220 ms, ease-out cubic) αντί για 10 σταθερά βήματα:
            // ο timer των WinForms έχει ανάλυση ~15 ms, οπότε τα 10 βήματα ήταν λίγα/ανομοιόμορφα (σκαλοπάτια).
            // Το πλάτος κάθε κουμπιού μενού ακολουθεί το πλάτος του sidebar ανά καρέ, ώστε περιεχόμενο και κουμπί
            // να μένουν κεντραρισμένα χωρίς να "πηδούν".
            const double durationMs = 220;
            foreach (var btn in _navButtons.Values) btn.SuppressWidth = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _sidebarAnimTimer = new System.Windows.Forms.Timer { Interval = 8 };
            _sidebarAnimTimer.Tick += (_, _) =>
            {
                double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / durationMs);
                double eased = 1 - Math.Pow(1 - t, 3);
                int w = (int)Math.Round(startWidth + (targetWidth - startWidth) * eased);
                if (w != _sidebar.Width)
                {
                    _sidebar.Width = w;
                    int usable = Math.Max(56, w - _sidebar.Padding.Horizontal);
                    foreach (var btn in _navButtons.Values) btn.Width = Math.Clamp(usable, 56, 184);
                }
                if (t >= 1.0)
                {
                    _sidebarAnimTimer?.Stop();
                    _sidebar.Width = targetWidth;
                    foreach (var btn in _navButtons.Values) { btn.SuppressWidth = false; btn.ApplyWidth(); }
                    onComplete?.Invoke();
                }
            };
            _sidebarAnimTimer.Start();
        }

        // Σελίδα που παρέχεται από plugin. Αν το plugin αποτύχει να φτιάξει τη σελίδα, δείχνουμε μήνυμα αντί να ρίξουμε το Opsis.
        private void ShowPluginPage(OpsisNewsCrawler.Plugin.Abstractions.PluginNavigationItem item)
        {
            Control page;
            try { page = item.CreatePage(); }
            catch (Exception ex)
            {
                page = new Label { Text = "Plugin error: " + ex.Message, ForeColor = UiTheme.TextSecondary, AutoSize = true, Padding = new Padding(24) };
            }
            SetPage(Services.PluginHost.TitleOf(item), page); // ένα κλειδί που λείπει από τα locale files εμφανίζεται ως το ίδιο το κείμενο
        }

        // Ίδια λογική με το ShowPluginPage παρακάτω, εφαρμοσμένη ΚΑΙ στις ενσωματωμένες σελίδες:
        // αν ένας page builder ρίξει exception στα μισά του χτισίματος, δείχνουμε μήνυμα σφάλματος
        // αντί για μισοχτισμένη/κενή σελίδα, και το καταγράφουμε ώστε να μην εξαφανίζεται σιωπηλά
        // (ζητήθηκε ρητά μετά από αναφορά χρήστη για κενή σελίδα Wallpaper χωρίς καταγεγραμμένο σφάλμα).
        private void SafeBuildPage(string key, Action builder)
        {
            try { builder(); }
            catch (Exception ex)
            {
                LogPageError(key, ex);
                _currentPageKey = key;
                var errorPage = new Label { Text = "Page error: " + ex.Message, ForeColor = UiTheme.TextSecondary, AutoSize = true, Padding = new Padding(24) };
                SetPage(key, errorPage);
            }
        }

        private static void LogPageError(string key, Exception ex)
        {
            try
            {
                var path = Path.Combine(AppPaths.AppDataFolder, "ui-errors.log");
                File.AppendAllText(path, $"[{DateTime.Now:O}] Page '{key}' failed to build: {ex}\n");
            }
            catch (Exception) { /* logging must never itself throw */ }
        }

        private void SetPage(string titleKey, Control control)
        {
            _pageTitle.Text = LocalizationManager.T(titleKey);
            Flicker.BatchRedraw(_content, () => SetPageCore(control));
        }

        private void SetPageCore(Control control)
        {
            _content.SuspendLayout();
            // ΔΙΟΡΘΩΣΗ πραγματικού bug: Controls.Clear() αφαιρεί τα child controls από τη συλλογή
            // αλλά ΔΕΝ τα κάνει Dispose — έτσι το παλιό page panel (π.χ. του Audio Enhancement, με
            // το δικό του System.Windows.Forms.Timer) έμενε "ορφανό" αλλά ζωντανό, ο Timer του
            // συνέχιζε να τρέχει επ' άπειρον, και το panel.Disposed cleanup ΠΟΤΕ δεν εκτελούνταν
            // ντετερμινιστικά στο UI thread — μόνο (ενδεχομένως) αργότερα από τον GC finalizer σε
            // ΔΙΑΦΟΡΕΤΙΚΟ thread, προκαλώντας διαλείπον NullReferenceException σε πεδία που
            // μοιράζονταν μεταξύ διαδοχικών επισκέψεων στην ίδια σελίδα. Ρητό Dispose εδώ κάνει το
            // cleanup άμεσο και στο σωστό thread.
            foreach (Control old in _content.Controls) old.Dispose();
            _content.Controls.Clear();
            control.Dock = DockStyle.Fill;
            _content.Controls.Add(control);
            _content.ResumeLayout();
        }

        private void ShowAndActivate()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            Show();
            BringToFront();
            Activate();
        }

        // Συντομεύσεις πληκτρολογίου για κάθε βασική λειτουργία της εφαρμογής (ζητήθηκε ρητά).
        // ProcessCmdKey αντί για απλό KeyDown ώστε να δουλεύουν ΟΠΟΥΔΗΠΟΤΕ μέσα στο παράθυρο,
        // ακόμη κι όταν ένα child control (π.χ. TextBox) έχει focus.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData is >= (Keys.Control | Keys.D1) and <= (Keys.Control | Keys.D9))
            {
                int index = (int)(keyData & Keys.KeyCode) - (int)Keys.D1;
                var order = new[] { "Dashboard", "Widgets", "DeskZones", "Wallpaper", "Performance", "Settings", "Profiles", "Automation", "About" };
                if (index >= 0 && index < order.Length) { NavigateTo(order[index]); return true; }
            }

            switch (keyData)
            {
                case Keys.Control | Keys.D0:
                    NavigateTo("Personalization");
                    return true;
                case Keys.Control | Keys.K:
                    ShowCommandPalette();
                    return true;
                case Keys.Control | Keys.B:
                    ToggleSidebarCollapsed();
                    return true;
                case Keys.Control | Keys.E:
                    if (WallpaperHostEngine.Instance.IsEnabled) WallpaperHostEngine.Instance.Disable();
                    else WallpaperHostEngine.Instance.Enable();
                    if (_currentPageKey == "Wallpaper" || _currentPageKey == "Dashboard") { if (_pageBuilders.TryGetValue(_currentPageKey, out var b)) b(); }
                    return true;
                case Keys.Control | Keys.Shift | Keys.N:
                    using (var editor = new ZoneLayoutEditorForm())
                        if (editor.ShowDialog(this) == DialogResult.OK) _statusLabel.Text = LocalizationManager.T("Status.ZonesUpdated");
                    if (_currentPageKey == "DeskZones" && _pageBuilders.TryGetValue("DeskZones", out var dzBuilder)) dzBuilder();
                    return true;
                case Keys.Control | Keys.Oemcomma:
                    NavigateTo("Settings");
                    return true;
                case Keys.F1:
                    NavigateTo("About");
                    return true;
                case Keys.F5:
                    if (_pageBuilders.TryGetValue(_currentPageKey, out var builder)) builder();
                    return true;
                case Keys.Control | Keys.Alt | Keys.F:
                    DeskFlipEngine.Show();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowDashboard()
        {
            _currentPageKey = "Dashboard";
            var panel = CreatePagePanel();
            AddHeading(panel, "Opsis — Desktop Customization");
            AddText(panel, LocalizationManager.T("Dashboard.Intro"));

            var metrics = SystemMonitorService.Instance.GetSnapshot();
            // "System" card = ζωντανή σύνοψη + συντόμευση στο πλήρες System Monitor (εκεί μένει η λεπτομέρεια:
            // CPU/RAM/δίκτυο/processes). Η γραμμή κατάστασης κάτω-κάτω δείχνει μόνο ένα ambient CPU/RAM glance.
            var systemCard = AddCard(panel, LocalizationManager.T("Dashboard.SystemCardTitle"),
                string.Format(LocalizationManager.T("Dashboard.SystemCardFormat"), metrics.CpuPercent.ToString("0.0"), metrics.AvailableMemoryMb.ToString("0"), metrics.TotalMemoryMb.ToString("0"), RamUsedPercent(metrics.AvailableMemoryMb, metrics.TotalMemoryMb)),
                onClick: () => NavigateTo("Performance"));
            AddCard(panel, LocalizationManager.T("Dashboard.DesktopCardTitle"), string.Format(LocalizationManager.T("Dashboard.DesktopCardBodyFormat"),
                Screen.AllScreens.Length, WidgetHostEngine.Instance.GetActiveWidgets().Count, Screen.AllScreens.Sum(s => ZoneLayoutStore.GetLayout(s.DeviceName).Zones.Count)));
            AddCard(panel, LocalizationManager.T("Dashboard.WallpaperCardTitle"), $"{LocalizationManager.T(WallpaperHostEngine.Instance.IsEnabled ? "Common.On" : "Common.Off")}   •   {WallpaperSettings.Load().Mode}   •   {WallpaperSettings.Load().PerformanceMode}");

            var dashboardRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            dashboardRefreshTimer.Tick += (_, _) =>
            {
                var m = SystemMonitorService.Instance.GetSnapshot();
                systemCard.Text = string.Format(LocalizationManager.T("Dashboard.SystemCardFormat"), m.CpuPercent.ToString("0.0"), m.AvailableMemoryMb.ToString("0"), m.TotalMemoryMb.ToString("0"), RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb));
            };
            dashboardRefreshTimer.Start();
            panel.Disposed += (_, _) => dashboardRefreshTimer.Dispose();

            AddSection(panel, LocalizationManager.T("Dashboard.SectionQuickLaunch"));
            AddIconButtonGrid(panel,
                ("Widgets", LocalizationManager.T("Dashboard.QuickOpenWidgets"), (_, _) => NavigateTo("Widgets")),
                ("DeskZones", LocalizationManager.T("Dashboard.QuickEditZones"), (_, _) => { using var editor = new ZoneLayoutEditorForm(); editor.ShowDialog(this); }),
                ("Wallpaper", LocalizationManager.T("Dashboard.QuickEnableWallpaper"), (_, _) => { WallpaperHostEngine.Instance.Enable(); _statusLabel.Text = LocalizationManager.T("Dashboard.WallpaperEnabledStatus"); }),
                ("Command", LocalizationManager.T("Dashboard.QuickCommandPalette"), (_, _) => ShowCommandPalette()));

            AddSection(panel, LocalizationManager.T("Dashboard.SectionWorkspace"));
            // Τα "Work"/"Gaming"/"Focus" παραμένουν ως έχουν (ονόματα προφίλ/IconKey, βλ. σχόλιο
            // στο TrayApplicationContext) — μόνο η λέξη "Profile/Προφίλ" γύρω τους μεταφράζεται.
            string profileFmt = LocalizationManager.T("Dashboard.ProfileButtonFormat");
            AddIconButtonGrid(panel,
                ("Work", string.Format(profileFmt, "Work"), (_, _) => LoadProfileFromQuickButton("Work")),
                ("Gaming", string.Format(profileFmt, "Gaming"), (_, _) => LoadProfileFromQuickButton("Gaming")),
                ("Focus", string.Format(profileFmt, "Focus"), (_, _) => LoadProfileFromQuickButton("Focus")));
            SetPage("Page.Dashboard.Title", panel);
        }

        private void ShowWidgets()
        {
            _currentPageKey = "Widgets";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Widgets.Intro"));
            // Ζητήθηκε ρητά "όλα τα widgets να έχουν το ίδιο μέγεθος" — ενιαίο προεπιλεγμένο
            // μέγεθος από τις Ρυθμίσεις αντί για διαφορετικό ανά τύπο widget (ο χρήστης μπορεί να
            // το αλλάξει μετά ανά widget, μέσω της νέας λαβής αλλαγής μεγέθους στη γωνία).
            var wds = AppSettings.Load();
            int ww = wds.WidgetDefaultWidth, wh = wds.WidgetDefaultHeight;
            AddSection(panel, LocalizationManager.T("Widgets.SectionDesktopWidgets"));
            AddIconButtonGrid(panel,
                ("SystemMonitor", LocalizationManager.T("Widgets.SystemMonitor"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, ww, wh)),
                ("Clock", LocalizationManager.T("Widgets.Clock"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("clock", 450, 100, ww, wh)),
                ("Network", LocalizationManager.T("Widgets.Network"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("network", 100, 350, ww, wh)),
                ("AudioVisualizer", LocalizationManager.T("Widgets.AudioVisualizer"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("audio", 450, 350, ww, wh)),
                ("Weather", LocalizationManager.T("Widgets.Weather"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("weather", 800, 100, ww, wh)),
                ("Disk", LocalizationManager.T("Widgets.Disk"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("disk", 800, 350, ww, wh)),
                ("Settings", LocalizationManager.T("Widgets.QuickSettings"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("quicksettings", 1100, 100, ww, wh)),
                // SeelenUI-inspired richer widget set (ζητήθηκε ρητά) - τα υπόλοιπα 4: quick-settings
                // (ήδη πάνω, "Settings"), notifications, media/now-playing, calendar, Bluetooth.
                ("Media", LocalizationManager.T("Widgets.Media"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("media", 1100, 350, ww, wh)),
                ("Notifications", LocalizationManager.T("Widgets.Notifications"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("notifications", 100, 600, ww, wh)),
                ("Calendar", LocalizationManager.T("Widgets.Calendar"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("calendar", 450, 600, ww, wh)),
                ("Bluetooth", LocalizationManager.T("Widgets.Bluetooth"), (_, _) => WidgetHostEngine.Instance.SpawnWidget("bluetooth", 800, 600, ww, wh)));

            // Μετακινήθηκε εδώ από τις γενικές Ρυθμίσεις (ζητήθηκε ρητά "διάσπαρτες ρυθμίσεις που
            // μπορούν να μπουν στις αντίστοιχες καρτέλες") - το θέμα widgets/containers και το
            // προεπιλεγμένο μέγεθος νέων widgets αφορούν ΑΠΟΚΛΕΙΣΤΙΚΑ αυτή τη σελίδα.
            AddSection(panel, LocalizationManager.T("Settings.SectionWidgetsContainers"));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.ShowSystemMonitorWidget"),
                AppSettings.Load().ShowSystemMonitor || WidgetHostEngine.Instance.GetActiveWidgets().Any(w => w.WidgetId == "sysmon"),
                v =>
                {
                    if (v) WidgetHostEngine.Instance.SpawnWidget("sysmon", 100, 100, ww, wh);
                    else WidgetHostEngine.Instance.GetActiveWidgets().FirstOrDefault(w => w.WidgetId == "sysmon")?.Close();
                    var a = AppSettings.Load(); a.ShowSystemMonitor = v; a.Save();
                }));
            panel.Controls.Add(SettingsView.BuildWidgetsThemeRow());
            panel.Controls.Add(BuildWidgetsTransparencyRows());
            var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
            sizeRow.Controls.Add(SettingsView.NumericRow(LocalizationManager.T("Settings.WidgetDefaultWidthLabel"), AppSettings.Load().WidgetDefaultWidth, 160, 800,
                v => { var a = AppSettings.Load(); a.WidgetDefaultWidth = v; a.Save(); }));
            sizeRow.Controls.Add(SettingsView.NumericRow(LocalizationManager.T("Settings.HeightLabel"), AppSettings.Load().WidgetDefaultHeight, 100, 800,
                v => { var a = AppSettings.Load(); a.WidgetDefaultHeight = v; a.Save(); }, marginLeft: 24));
            panel.Controls.Add(sizeRow);

            AddSection(panel, LocalizationManager.T("Widgets.SectionWorkspaceActions"));
            AddIconButtonGrid(panel,
                ("Save", LocalizationManager.T("Widgets.SaveLayout"), (_, _) => { WorkspaceProfileService.Save("Last Session"); _statusLabel.Text = LocalizationManager.T("Widgets.LayoutSavedStatus"); }),
                ("Restore", LocalizationManager.T("Widgets.RestoreLayout"), (_, _) => { WorkspaceProfileService.Load("Last Session"); _statusLabel.Text = LocalizationManager.T("Widgets.WorkspaceRestoredStatus"); }),
                ("Close", LocalizationManager.T("Widgets.CloseAllWidgets"), (_, _) => { WidgetHostEngine.Instance.CloseAll(); }));

            AddSection(panel, LocalizationManager.T("Widgets.SectionActiveWidgets"));
            var activeList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(activeList);

            // ΔΙΟΡΘΩΣΗ "εξάλειψη flicker στα ενεργά widgets": το παλιό κώδικας έκανε πλήρες
            // DisposeChildren+rebuild ΚΑΘΕ 2s ΑΣΧΕΤΑ αν κάτι είχε πραγματικά αλλάξει — καταστροφή/
            // αναδημιουργία πραγματικών child HWNDs κάθε tick είναι ο πιο συνηθισμένος λόγος
            // ορατού flicker στο WinForms. Τώρα συγκρίνεται μια φθηνή "υπογραφή" (id+lock state)
            // και η ανοικοδόμηση γίνεται ΜΟΝΟ όταν κάτι πραγματικά άλλαξε.
            string? lastSignature = null;
            void RefreshActiveList()
            {
                var active = WidgetHostEngine.Instance.GetActiveWidgets();
                string signature = string.Join("|", active.Select(w => $"{w.WidgetId}:{w.IsLocked}"));
                if (signature == lastSignature) return;
                lastSignature = signature;

                // Ίδια διόρθωση scroll-reset με το RefreshPinnedApps (DeskStrip)/topLabel (Performance):
                // το Controls.Clear()+rebuild μέσα σε AutoScroll container επαναφέρει σιωπηλά στην κορυφή.
                var savedScroll = panel.AutoScrollPosition;
                activeList.SuspendLayout();
                DisposeChildren(activeList);
                if (active.Count == 0)
                {
                    activeList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Common.NoActiveWidgets"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var w in active)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    row.Controls.Add(new Label { Text = w.WidgetId, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var lockBtn = new HoverButton { Text = LocalizationManager.T(w.IsLocked ? "Common.Unlock" : "Common.Lock"), Width = 80, Height = 28, Location = new Point(310, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    lockBtn.FlatAppearance.BorderSize = 0;
                    lockBtn.Click += (_, _) => { w.IsLocked = !w.IsLocked; RefreshActiveList(); };
                    row.Controls.Add(lockBtn);
                    var closeBtn = new HoverButton { Text = LocalizationManager.T("Common.Close"), Width = 80, Height = 28, Location = new Point(400, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    closeBtn.FlatAppearance.BorderSize = 0;
                    closeBtn.Click += (_, _) => { w.Close(); RefreshActiveList(); };
                    row.Controls.Add(closeBtn);
                    activeList.Controls.Add(row);
                }
                activeList.ResumeLayout();
                panel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            }
            RefreshActiveList();
            var widgetsRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            widgetsRefreshTimer.Tick += (_, _) => RefreshActiveList();
            widgetsRefreshTimer.Start();
            panel.Disposed += (_, _) => widgetsRefreshTimer.Dispose();

            SetPage("Page.Widgets.Title", panel);
        }

        private void ShowDeskZones()
        {
            _currentPageKey = "DeskZones";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("DeskZones.Intro"));
            AddSection(panel, LocalizationManager.T("DeskZones.SectionLayouts"));
            AddText(panel, LocalizationManager.T("DeskZones.ShiftDragHint"));
            AddText(panel, LocalizationManager.T("DeskZones.KeyboardAndNeighborHint"));

            foreach (var screen in Screen.AllScreens)
            {
                var row = new Panel { Width = 720, Height = 130, Margin = new Padding(0, 0, 0, 10) };
                var preview = new ZonePreviewPanel { Location = new Point(0, 0), Size = new Size(220, 124) };
                var layout = ZoneLayoutStore.GetLayout(screen.DeviceName);
                preview.SetLayout(layout);
                row.Controls.Add(preview);

                string screenLabel = Screen.AllScreens.Length > 1
                    ? string.Format(LocalizationManager.T(screen.Primary ? "Wallpaper.ScreenLabelPrimary" : "Wallpaper.ScreenLabel"), Array.IndexOf(Screen.AllScreens, screen) + 1)
                    : LocalizationManager.T("DeskZones.EditorSingleScreen");
                row.Controls.Add(new Label { Text = $"{screenLabel}  ({screen.Bounds.Width}×{screen.Bounds.Height})", ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Location = new Point(236, 8), AutoSize = true });
                row.Controls.Add(new Label { Text = LocalizationManager.T("DeskZones.Template." + layout.Template.Replace(" ", "")), ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Location = new Point(236, 34), AutoSize = true, Name = "templateLabel" });

                var editBtn = new PillButton { Text = LocalizationManager.T("DeskZones.EditorApply2"), Width = 160, Height = 32, Location = new Point(236, 64), BackColor = UiTheme.Surface, ForeColor = UiTheme.AccentCyan };
                var capturedScreen = screen;
                editBtn.Click += (_, _) =>
                {
                    using var editor = new ZoneLayoutEditorForm(capturedScreen);
                    if (editor.ShowDialog(this) == DialogResult.OK)
                    {
                        var updated = ZoneLayoutStore.GetLayout(capturedScreen.DeviceName);
                        preview.SetLayout(updated);
                        foreach (Control c in row.Controls) if (c.Name == "templateLabel") c.Text = LocalizationManager.T("DeskZones.Template." + updated.Template.Replace(" ", ""));
                    }
                };
                row.Controls.Add(editBtn);
                panel.Controls.Add(row);
            }

            // Ξεχωριστή, δεύτερη λειτουργία στην ίδια σελίδα: DeskContainers — η "πραγματική"
            // έννοια των Fences (ομαδοποίηση αρχείων/φακέλων/συντομεύσεων, όχι παραθύρων).
            AddSection(panel, LocalizationManager.T("DeskZones.SectionContainers"));
            AddText(panel, LocalizationManager.T("DeskZones.ContainersIntro"));

            // Ζητήθηκε ρητά: ο μηχανισμός "διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας
            // κρύβει/επαναφέρει τα εικονίδια" (DesktopIconVisibilityEngine) υπήρχε ήδη πλήρως
            // λειτουργικός αλλά ήταν πάντα ενεργός χωρίς καμία ρύθμιση ορατή στο χρήστη. Τώρα
            // μπορεί να απενεργοποιηθεί από εδώ (ενεργοποιείται/απενεργοποιείται ζωντανά, χωρίς
            // restart της εφαρμογής).
            // Επιλογή εικονιδίων που ΜΕΝΟΥΝ όταν το διπλό κλικ κρύβει την επιφάνεια εργασίας.
            string LdKeep(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            var keepIconsBtn = new HoverButton { Text = LdKeep("Εικονίδια που μένουν ορατά…", "Icons that stay visible…"), Width = 260, Height = 32, Margin = new Padding(0, 0, 0, 10), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
            keepIconsBtn.FlatAppearance.BorderSize = 0;
            keepIconsBtn.Click += (_, _) => ShowKeepIconsDialog();
            var quickHideCheck = new FlatCheckBox { Text = LocalizationManager.T("DeskZones.QuickHideDoubleClick"), Checked = AppSettings.Load().QuickHideDoubleClickEnabled, AutoSize = true, BackColor = UiTheme.Background, Margin = new Padding(0, 0, 0, 10) };
            quickHideCheck.CheckedChanged += (_, _) =>
            {
                var a = AppSettings.Load(); a.QuickHideDoubleClickEnabled = quickHideCheck.Checked; a.Save();
                if (quickHideCheck.Checked) MotionDesk.Widgets.DesktopIconVisibilityEngine.Instance.Start();
                else MotionDesk.Widgets.DesktopIconVisibilityEngine.Instance.Stop();
            };
            panel.Controls.Add(quickHideCheck);
            panel.Controls.Add(keepIconsBtn);

            // DeskContainers engine: only its on/off control lives here; every other DeskContainers setting is in the three-bars menu of a container.
            if (ContainersEngine.IsAvailable) BuildContainersSettings(panel);

            // Applications that are never snapped (one process name per line, e.g. vlc, obs64)
            AddSection(panel, LocalizationManager.T("DeskZones.SectionExcludedApps"));
            AddText(panel, LocalizationManager.T("DeskZones.ExcludedAppsHint"));
            var excludedBox = new TextBox
            {
                Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = UiTheme.FontBody,
                BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 10),
                Text = string.Join(Environment.NewLine, AppSettings.Load().ZoneExcludedApps)
            };
            excludedBox.Leave += (_, _) =>
            {
                var a = AppSettings.Load();
                a.ZoneExcludedApps = excludedBox.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Path.GetFileNameWithoutExtension(x.Trim())).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                a.Save();
            };
            panel.Controls.Add(excludedBox);

            SetPage("Page.DeskZones.Title", panel);
        }

        private void ShowWallpaper()
        {
            _currentPageKey = "Wallpaper";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Wallpaper.Intro"));

            var settings = WallpaperSettings.Load();
            var statusCard = AddCard(panel, LocalizationManager.T("Wallpaper.StatusLabel"), DescribeWallpaperState(settings));
            var playlistCard = AddCard(panel, LocalizationManager.T("Wallpaper.PlaylistLabel"), DescribePlaylist(settings));

            void RefreshStatus()
            {
                var s = WallpaperSettings.Load();
                statusCard.Text = DescribeWallpaperState(s);
                playlistCard.Text = DescribePlaylist(s);
            }

            Dictionary<string, HoverButton>? enableButtons = null;
            void SyncEnableButtons() { RefreshStatus(); if (enableButtons != null) MarkActive(enableButtons, WallpaperHostEngine.Instance.IsEnabled ? "Enable" : "Disable"); }
            enableButtons = AddToggleButtonGrid(panel,
                ("Enable", (_, _) => { WallpaperHostEngine.Instance.Enable(); SyncEnableButtons(); }),
                ("Disable", (_, _) => { WallpaperHostEngine.Instance.Disable(); SyncEnableButtons(); }));
            SyncEnableButtons();
            // the tray menu, profiles, the schedule and the Dashboard also switch the wallpaper: keep the buttons and the status right
            Action? onState = null;
            onState = () =>
            {
                if (statusCard.IsDisposed || IsDisposed) { WallpaperHostEngine.Instance.StateChanged -= onState!; return; }
                if (InvokeRequired) BeginInvoke(new Action(SyncEnableButtons)); else SyncEnableButtons();
            };
            WallpaperHostEngine.Instance.StateChanged += onState;

            AddSection(panel, LocalizationManager.T("Wallpaper.SectionMode"));
            var modeCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 10) };
            modeCombo.SetItems(new[] { "Waves", "Video", "Image", "Page", "Application", "Particles", "Weather", "Desktop" }, settings.Mode);
            // Ξαναχτίζει ΟΛΟΚΛΗΡΗ τη σελίδα κάθε φορά που αλλάζει το Mode — ζητήθηκε ρητά bug fix:
            // πριν, οι ενότητες "Video Library"/"Theme & Colors"/"Wave Tuning" ήταν ΠΑΝΤΑ ορατές
            // ανεξάρτητα από το επιλεγμένο mode, οπότε η επιλογή βίντεο έμενε στην οθόνη ακόμη κι
            // όταν ο χρήστης διάλεγε "Particles" — φαινόταν σαν να "ανακατευθύνει" σε βίντεο ενώ
            // απλά ποτέ δεν κρυβόταν.
            modeCombo.SelectedIndexChanged += (_, _) => { WallpaperHostEngine.Instance.SetMode(modeCombo.SelectedItem!); ShowWallpaper(); };
            panel.Controls.Add(modeCombo);

            if (settings.Mode == "Video")
            {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionVideoLibrary"));
            // Ξαναχτίζει ΟΛΟΚΛΗΡΗ τη σελίδα μετά από προσθήκη — ζητήθηκε ρητά "τόσο με την
            // προσθήκη video όσο και με την προσθήκη φακέλου η εφαρμογή να ενημερώνει τη λίστα
            // με τα βίντεο". Πριν, η λίστα (dropdown/κάρτα) δεν ανανεωνόταν καθόλου μετά την
            // προσθήκη — μόνο το στατιστικό κείμενο του playlistCard.
            AddIconButtonGrid(panel,
                ("Add", LocalizationManager.T("Wallpaper.AddVideoButton"), (_, _) => ChooseWallpaperVideoFiles(ShowWallpaper)),
                ("Add", LocalizationManager.T("Wallpaper.AddFolderButton"), (_, _) => { ChooseWallpaperFolder(); ShowWallpaper(); }),
                ("Delete", LocalizationManager.T("Wallpaper.ClearPlaylist"), (_, _) => { WallpaperHostEngine.Instance.ClearVideo(); ShowWallpaper(); }));

            var subfoldersCheck = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.IncludeSubfolders"), AutoSize = true, Checked = settings.IncludeSubfolders, BackColor = UiTheme.Background, Margin = new Padding(0, 4, 0, 6) };
            subfoldersCheck.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetIncludeSubfolders(subfoldersCheck.Checked);
            panel.Controls.Add(subfoldersCheck);

            // Επιλογή ποια από τα φορτωμένα βίντεο (αρχεία ή περιεχόμενο φακέλου) θα αναπαράγονται: όλα / κανένα με ένα κλικ,
            // και μετά τικάρισμα μεμονωμένων γραμμών παρακάτω (κλικ και πάνω στο όνομα).
            var selectionButtons = new List<(string IconKey, string Text, EventHandler Action)>
            {
                ("Restore", LocalizationManager.T("Wallpaper.SelectAll"), (_, _) => { WallpaperHostEngine.Instance.SetAllVideosEnabled(true); ShowWallpaper(); }),
                ("Close", LocalizationManager.T("Wallpaper.SelectNone"), (_, _) => { WallpaperHostEngine.Instance.SetAllVideosEnabled(false); ShowWallpaper(); }),
            };
            int missingCount = settings.VideoPaths.Count(vp => !File.Exists(vp));
            if (missingCount > 0)
                selectionButtons.Add(("Delete", string.Format(LocalizationManager.T("Wallpaper.RemoveMissing"), missingCount), (_, _) => { WallpaperHostEngine.Instance.RemoveMissingVideos(); ShowWallpaper(); }));
            AddIconButtonGrid(panel, selectionButtons.ToArray());

            // Πραγματική λίστα με ΟΛΑ τα φορτωμένα βίντεο (όχι μόνο ένα στατιστικό "N αρχεία") —
            // κάθε γραμμή έχει το όνομα αρχείου και ένα ✕ για αφαίρεση, ζητήθηκε ρητά "να μπορεί
            // να προσθαφαιρεί αρχεία βίντεο" απευθείας από τη βιβλιοθήκη.
            var videoListPanel = new Panel { Width = 720, Height = Math.Min(300, Math.Max(50, settings.VideoPaths.Count * 46 + 10)), Margin = new Padding(0, 0, 0, 10), AutoScroll = true, BackColor = UiTheme.Surface };
            GlassBlur.ApplyScrollbarTheme(videoListPanel, UiTheme.Surface.GetBrightness() < 0.5f);
            int rowY = 4;
            foreach (var videoPath in settings.VideoPaths.ToArray())
            {
                bool exists = File.Exists(videoPath);
                var row = new Panel { Location = new Point(4, rowY), Size = new Size(700, 42), BackColor = UiTheme.Surface };
                // Ζητήθηκε ρητά "να επιλέγει ο χρήστης 1 ή περισσότερα βίντεο για να
                // αναπαράγονται" — ένα φορτωμένο βίντεο μπορεί να μείνει στη βιβλιοθήκη χωρίς να
                // συμμετέχει στην ενεργή αναπαραγωγή/shuffle (checkbox, όχι αφαίρεση).
                var enabledCheck = new FlatCheckBox { Checked = !settings.DisabledVideoPaths.Contains(videoPath), Location = new Point(4, 11), Size = new Size(20, 20), BackColor = UiTheme.Surface };
                // Ζητήθηκε ρητά "ενσωμάτωσε την παρουσίαση ενός media player" (MediaPlayerPro) —
                // λεπτή έγχρωμη λωρίδα αριστερά (ίδια τεχνική με το accent border selection strip
                // στη λίστα playlist του MediaPlayerPro), εδώ δηλώνει "ενεργό στο playlist" αντί
                // για "επιλεγμένο" αφού δεν υπάρχει έννοια επιλογής γραμμής εδώ.
                row.Paint += (_, pe) =>
                {
                    if (enabledCheck.Checked)
                        using (var stripBrush = new SolidBrush(UiTheme.AccentCyan)) pe.Graphics.FillRectangle(stripBrush, 0, 0, 3, row.Height);
                };
                enabledCheck.CheckedChanged += (_, _) => { WallpaperHostEngine.Instance.SetVideoEnabled(videoPath, enabledCheck.Checked); row.Invalidate(); };
                var nameLabel = new Label
                {
                    Text = Path.GetFileName(videoPath) + (exists ? "" : LocalizationManager.T("Wallpaper.MissingSuffix")),
                    AutoSize = false,
                    Size = new Size(500, 28),
                    Location = new Point(102, 7),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = exists ? UiTheme.TextPrimary : UiTheme.TextMuted,
                    Font = UiTheme.FontBody,
                    AutoEllipsis = true
                };
                // Στρογγυλό, borderless κουμπί αφαίρεσης με κυκλικό hover-highlight αντί για απλή
                // αλλαγή χρώματος κειμένου — ίδιο πνεύμα με τα icon-only κουμπιά του MediaPlayerPro.
                var removeBtn = new Label { Text = "", AutoSize = false, Size = new Size(24, 24), Location = new Point(670, 9), Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9), BackColor = Color.Transparent };
                bool removeHot = false;
                removeBtn.Paint += (_, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (removeHot) using (var hb = new SolidBrush(Color.FromArgb(40, 231, 76, 60))) pe.Graphics.FillEllipse(hb, 0, 0, removeBtn.Width - 1, removeBtn.Height - 1);
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using var textBrush = new SolidBrush(removeHot ? Color.FromArgb(231, 76, 60) : UiTheme.TextSecondary);
                    pe.Graphics.DrawString("✕", removeBtn.Font, textBrush, new RectangleF(0, 0, removeBtn.Width, removeBtn.Height), sf);
                };
                removeBtn.MouseEnter += (_, _) => { removeHot = true; removeBtn.Invalidate(); };
                removeBtn.MouseLeave += (_, _) => { removeHot = false; removeBtn.Invalidate(); };
                removeBtn.Click += (_, _) => { WallpaperHostEngine.Instance.RemoveVideo(videoPath); ShowWallpaper(); };
                nameLabel.Click += (_, _) => enabledCheck.Checked = !enabledCheck.Checked;
                // thumbnail (Windows Shell) + duration (FFmpeg, if present) — loaded in the background, the row never waits for them
                var thumb = new PictureBox { Location = new Point(28, 3), Size = new Size(68, 36), SizeMode = PictureBoxSizeMode.Zoom, BackColor = UiTheme.Background, Cursor = Cursors.Hand };
                thumb.Click += (_, _) => enabledCheck.Checked = !enabledCheck.Checked;
                var durationLabel = new Label { Text = "", AutoSize = false, Size = new Size(60, 28), Location = new Point(604, 7), TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody };
                if (exists) _ = LoadVideoRowMetaAsync(videoPath, thumb, durationLabel);
                row.Controls.Add(enabledCheck);
                row.Controls.Add(thumb);
                row.Controls.Add(durationLabel);
                row.Controls.Add(nameLabel);
                row.Controls.Add(removeBtn);
                videoListPanel.Controls.Add(row);
                rowY += 46;
            }
            if (settings.VideoPaths.Count == 0)
                videoListPanel.Controls.Add(new Label { Text = LocalizationManager.T("Wallpaper.NoVideosYet"), AutoSize = true, Location = new Point(6, 6), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
            panel.Controls.Add(videoListPanel);

            var shuffle = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.Shuffle"), AutoSize = true, Checked = settings.Shuffle, BackColor = UiTheme.Background, Margin = new Padding(0, 4, 0, 10) };
            shuffle.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetShuffle(shuffle.Checked);
            panel.Controls.Add(shuffle);

            // Επαναλήψεις ανά βίντεο + εναλλαγή ανά N λεπτά (αποθηκεύονται με καθυστέρηση 500ms μετά το τελευταίο σύρσιμο)
            FlatSlider? repeatTrack = null, rotateTrack = null;
            void CommitPlayback() { if (repeatTrack != null && rotateTrack != null) WallpaperHostEngine.Instance.SetPlaybackOptions(repeatTrack.Value, rotateTrack.Value); }
            panel.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.RepeatPerVideo"), settings.RepeatPerVideo, 1, 10, v => v == 1 ? LocalizationManager.T("Wallpaper.RepeatOnce") : $"{v}×", CommitPlayback, t => repeatTrack = t));
            panel.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.RotateEvery"), settings.RotateEveryMinutes, 0, 60, v => v == 0 ? LocalizationManager.T("Wallpaper.RotateWhenEnds") : string.Format(LocalizationManager.T("Wallpaper.RotateMinutesFormat"), v), CommitPlayback, t => rotateTrack = t));

            AddText(panel, LocalizationManager.T("Wallpaper.SupportedFormats"));

            // Πραγματικό DSP (bass/mid/treble/volume) πάνω στον ΔΙΚΟ ΜΑΣ ήχο του wallpaper video,
            // μέσω Web Audio API στο wallpaper/index.html (ensureAudioGraph/applyAudioConfig) — όχι
            // system-wide, μόνο η αναπαραγωγή του Opsis. Προεπιλογή ΚΛΕΙΣΤΟ (AudioEnabled
            // false): το wallpaper ήταν πάντα σιωπηλό πριν, δεν αλλάζει συμπεριφορά χωρίς ρητή
            // ενεργοποίηση από τον χρήστη.
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionAudio"));
            var audioEnabledCheck = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.AudioEnabled"), AutoSize = true, Checked = settings.AudioEnabled, BackColor = UiTheme.Background, Margin = new Padding(0, 4, 0, 8) };
            panel.Controls.Add(audioEnabledCheck);
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Wallpaper.AutoMuteOtherAudio"), AppSettings.Load().AutoMuteWhenOtherAudio,
                v => { var a = AppSettings.Load(); a.AutoMuteWhenOtherAudio = v; a.Save(); }));

            var volumeTrack = new FlatSlider { Minimum = 0, Maximum = 100, Value = (int)Math.Clamp(settings.AudioVolume * 100, 0, 100), Width = 220 };
            var bassTrack = new FlatSlider { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioBassGain, -12, 12), Width = 220 };
            var midTrack = new FlatSlider { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioMidGain, -12, 12), Width = 220 };
            var trebleTrack = new FlatSlider { Minimum = -12, Maximum = 12, Value = (int)Math.Clamp(settings.AudioTrebleGain, -12, 12), Width = 220 };

            void ApplyAudioSettings() => WallpaperHostEngine.Instance.SetAudioSettings(
                audioEnabledCheck.Checked, volumeTrack.Value / 100.0, bassTrack.Value, midTrack.Value, trebleTrack.Value);

            Panel AudioRow(string label, FlatSlider track, string unit)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.TextSecondary, Width = 110, Padding = new Padding(0, 6, 8, 0) });
                var valueLabel = new Label { AutoSize = true, ForeColor = UiTheme.TextMuted, Padding = new Padding(8, 6, 0, 0) };
                void UpdateLabel() => valueLabel.Text = unit == "%" ? $"{track.Value}%" : $"{(track.Value > 0 ? "+" : "")}{track.Value} dB";
                UpdateLabel();
                track.ValueChanged += (_, _) => { UpdateLabel(); ApplyAudioSettings(); };
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            audioEnabledCheck.CheckedChanged += (_, _) => ApplyAudioSettings();
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioVolume"), volumeTrack, "%"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioBass"), bassTrack, "dB"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioMid"), midTrack, "dB"));
            panel.Controls.Add(AudioRow(LocalizationManager.T("Wallpaper.AudioTreble"), trebleTrack, "dB"));
            AddText(panel, LocalizationManager.T("Wallpaper.AudioNote"));
            }

            // Ζητήθηκε ρητά "διαφορετικό βίντεο ανά οθόνη" — εμφανίζεται μόνο όταν υπάρχουν
            // πράγματι πολλαπλές οθόνες ΚΑΙ το mode είναι Video (άσχετο για Waves/Particles).
            // Κάθε οθόνη μπορεί να "καρφιτσωθεί" σε ένα συγκεκριμένο βίντεο της βιβλιοθήκης, ή να
            // μείνει στο κοινό μοιρασμένο playlist (προεπιλογή).
            if (Screen.AllScreens.Length > 1 && settings.Mode == "Video")
            {
                AddSection(panel, LocalizationManager.T("Wallpaper.SectionPerScreenVideo"));
                string sharedOption = LocalizationManager.T("Wallpaper.SharedPlaylistOption");
                for (int screenIndex = 0; screenIndex < Screen.AllScreens.Length; screenIndex++)
                {
                    var targetScreen = Screen.AllScreens[screenIndex];
                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
                    string screenLabel = string.Format(LocalizationManager.T(targetScreen.Primary ? "Wallpaper.ScreenLabelPrimary" : "Wallpaper.ScreenLabel"), screenIndex + 1);
                    row.Controls.Add(new Label { Text = screenLabel + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0), Width = 140 });

                    var screenCombo = new FlatComboBox { Width = 260 };
                    var videoNames = settings.VideoPaths.Where(File.Exists).ToList();
                    var options = new List<string> { sharedOption };
                    options.AddRange(videoNames.Select(Path.GetFileName)!);
                    string? currentOverride = WallpaperHostEngine.Instance.GetScreenVideoOverride(targetScreen.DeviceName);
                    string selected = currentOverride != null && File.Exists(currentOverride) ? Path.GetFileName(currentOverride)! : sharedOption;
                    screenCombo.SetItems(options, selected);
                    screenCombo.SelectedIndexChanged += (_, _) =>
                    {
                        var pick = screenCombo.SelectedItem;
                        if (pick == null || pick == sharedOption)
                            WallpaperHostEngine.Instance.SetScreenVideoOverride(targetScreen.DeviceName, null);
                        else
                        {
                            var match = videoNames.FirstOrDefault(v => Path.GetFileName(v) == pick);
                            if (match != null) WallpaperHostEngine.Instance.SetScreenVideoOverride(targetScreen.DeviceName, match);
                        }
                    };
                    row.Controls.Add(screenCombo);
                    panel.Controls.Add(row);
                }
                AddText(panel, LocalizationManager.T("Wallpaper.PerScreenNote"));
            }

            // Theme/Palette και Wave Tuning ισχύουν για Waves ΚΑΙ Particles (μοιράζονται το ίδιο
            // cfg.speed/glowIntensity/palette στο JS engine) — άσχετα μόνο σε Video mode.
            if (settings.Mode is "Page" or "Image") BuildMediaWallpaperSection(panel, settings);
            if (settings.Mode == "Application") BuildApplicationSection(panel, settings);
            BuildElementsSection(panel, settings);
            if (settings.Mode == "Weather") BuildWeatherWallpaperSection(panel, settings);
            if (settings.Mode == "Desktop") AddText(panel, LocalizationManager.T("Wallpaper.DesktopIntro"));

            if (settings.Mode is "Waves" or "Particles")
            {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionThemeColors"));
            var themeCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 8) };
            themeCombo.SetItems(new[] { "Follow", "Light", "Dark" }, settings.ThemeMode);
            panel.Controls.Add(themeCombo);

            var styleCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 8) };
            styleCombo.SetItems(new[] { "Ribbons", "Aurora", "TechGrid" }, settings.WaveStyle);
            styleCombo.SelectedIndexChanged += (_, _) => WallpaperHostEngine.Instance.SetWaveStyle(styleCombo.SelectedItem!);
            panel.Controls.Add(styleCombo);

            var paletteCombo = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 10) };
            void RepopulatePalettes()
            {
                bool light = themeCombo.SelectedItem switch
                {
                    "Light" => true,
                    "Dark" => false,
                    _ => ThemeService.IsLightTheme(),
                };
                var names = (light ? WavePalettes.Light : WavePalettes.Dark).Select(p => p.Name).ToArray();
                var current = WallpaperSettings.Load().PaletteName;
                paletteCombo.SetItems(names, names.Contains(current) ? current : names.FirstOrDefault());
            }
            RepopulatePalettes();
            themeCombo.SelectedIndexChanged += (_, _) => { WallpaperHostEngine.Instance.SetThemeMode(themeCombo.SelectedItem!); RepopulatePalettes(); };
            paletteCombo.SelectedIndexChanged += (_, _) => { if (paletteCombo.SelectedItem is { } p) WallpaperHostEngine.Instance.SetPalette(p); };
            panel.Controls.Add(paletteCombo);

            AddSection(panel, LocalizationManager.T("Wallpaper.SectionWaveTuning"));
            panel.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.Speed"), (int)(settings.WaveSpeed * 10), 3, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(v / 10.0, WallpaperSettings.Load().GlowIntensity, WallpaperSettings.Load().LineThickness)));
            panel.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.GlowIntensity"), (int)(settings.GlowIntensity * 10), 0, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(WallpaperSettings.Load().WaveSpeed, v / 10.0, WallpaperSettings.Load().LineThickness)));
            panel.Controls.Add(BuildWaveSlider(LocalizationManager.T("Wallpaper.LineThickness"), (int)(settings.LineThickness * 10), 5, 20,
                v => WallpaperHostEngine.Instance.SetWaveTuning(WallpaperSettings.Load().WaveSpeed, WallpaperSettings.Load().GlowIntensity, v / 10.0)));
            }

            BuildInfoOverlaySection(panel, settings);
            BuildWallpaperScheduleSection(panel, settings);

            AddSection(panel, LocalizationManager.T("Wallpaper.SectionPerformance"));
            Dictionary<string, HoverButton>? perfButtons = null;
            perfButtons = AddToggleButtonGrid(panel,
                ("High", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("High"); RefreshStatus(); MarkActive(perfButtons!, "High"); }),
                ("Balanced", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Balanced"); RefreshStatus(); MarkActive(perfButtons!, "Balanced"); }),
                ("Low Power", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Low Power"); RefreshStatus(); MarkActive(perfButtons!, "Low Power"); }),
                ("Battery", (_, _) => { WallpaperHostEngine.Instance.SetPerformanceMode("Battery"); RefreshStatus(); MarkActive(perfButtons!, "Battery"); }));
            MarkActive(perfButtons, settings.PerformanceMode);

            // Μετακινήθηκε εδώ από τις γενικές Ρυθμίσεις (ζητήθηκε ρητά "διάσπαρτες ρυθμίσεις που
            // μπορούν να μπουν στις αντίστοιχες καρτέλες") - αφορούν ΑΠΟΚΛΕΙΣΤΙΚΑ το wallpaper.
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.AutoPerformanceMode"), AppSettings.Load().AutoPerformanceMode,
                v => { var a = AppSettings.Load(); a.AutoPerformanceMode = v; a.Save(); }));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.PauseOnLock"), AppSettings.Load().PauseOnLock,
                v => { var a = AppSettings.Load(); a.PauseOnLock = v; a.Save(); }));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.PauseOnRemote"), AppSettings.Load().PauseOnRemoteSession,
                v => { var a = AppSettings.Load(); a.PauseOnRemoteSession = v; a.Save(); }));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.PauseOnBatterySaver"), AppSettings.Load().PauseOnBatterySaver,
                v => { var a = AppSettings.Load(); a.PauseOnBatterySaver = v; a.Save(); }));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.HeavyPause"), AppSettings.Load().WallpaperHeavyPause,
                v => { var a = AppSettings.Load(); a.WallpaperHeavyPause = v; a.Save(); }));
            BuildLoadPauseRows(panel);
            panel.Controls.Add(SettingsView.BuildHardwareProfileRow());
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.StartWallpaperWithApp"), AppSettings.Load().StartWallpaperWithWindows,
                v => { var a = AppSettings.Load(); a.StartWallpaperWithWindows = v; a.Save(); }));
            panel.Controls.Add(SettingsView.BuildWmvOutputRow());

            SetPage("Page.Wallpaper.Title", panel);
        }

        // ---- "Weather" wallpaper mode: location, live status, simulated weather/time, window-glass effect
        private static readonly string[] WeatherSimIds = { "Auto", "Clear", "PartlyCloudy", "Cloudy", "Drizzle", "Rain", "HeavyRain", "Thunderstorm", "Snow", "Fog" };
        private static readonly string[] TimeSimIds = { "Auto", "Dawn", "Day", "Dusk", "Night" };
        private static readonly string[] WindSimIds = { "Auto", "Calm", "LightBreeze", "FreshBreeze", "Strong", "Gale" };

        // Weather-mode only: simulated weather / wind / time of day + window-glass effect
        private void BuildWeatherWallpaperSection(Panel panel, WallpaperSettings settings)
        {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionWeather"));
            AddText(panel, LocalizationManager.T("Wallpaper.WeatherIntro"));

            // simulated weather / time of day (Automatic = live)
            FlowLayoutPanel ComboRow(string labelKey, string[] ids, string idPrefix, string currentId, Action<string> onChange)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), AutoSize = false, Width = 150, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
                var labels = ids.Select(id => LocalizationManager.T(idPrefix + id)).ToArray();
                var combo = new FlatComboBox { Width = 240 };
                combo.SetItems(labels, labels[Math.Max(0, Array.IndexOf(ids, currentId))]);
                combo.SelectedIndexChanged += (_, _) => { int i = combo.SelectedIndex; if (i >= 0 && i < ids.Length) onChange(ids[i]); };
                row.Controls.Add(combo);
                return row;
            }
            panel.Controls.Add(ComboRow("Wallpaper.WeatherSimLabel", WeatherSimIds, "Wallpaper.Sim.", settings.WeatherSimulation, id => WallpaperHostEngine.Instance.SetWeatherSimulation(id)));
            panel.Controls.Add(ComboRow("Wallpaper.WindSimLabel", WindSimIds, "Wallpaper.Wind.", settings.WindSimulation, id => WallpaperHostEngine.Instance.SetWindSimulation(id)));
            panel.Controls.Add(ComboRow("Wallpaper.TimeSimLabel", TimeSimIds, "Wallpaper.Time.", settings.TimeSimulation, id => WallpaperHostEngine.Instance.SetTimeSimulation(id)));

            var glass = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.WeatherGlass"), AutoSize = true, Checked = settings.WeatherGlass, BackColor = UiTheme.Background, Margin = new Padding(0, 6, 0, 10) };
            glass.CheckedChanged += (_, _) => WallpaperHostEngine.Instance.SetWeatherGlass(glass.Checked);
            panel.Controls.Add(glass);
        }

        // ALL wallpaper modes (videos, Waves, Particles, Weather): live weather, location and the clock / date / temperature overlay
        private void BuildInfoOverlaySection(Panel panel, WallpaperSettings settings)
        {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionInfo"));
            AddText(panel, LocalizationManager.T("Wallpaper.InfoIntro"));

            var liveLabel = new Label { Text = LocalizationManager.T("Wallpaper.WeatherLoading"), AutoSize = true, MaximumSize = new Size(740, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.AccentCyan, Margin = new Padding(0, 0, 0, 12) };
            panel.Controls.Add(liveLabel);
            _ = RefreshLiveWeatherLabelAsync(liveLabel, settings.WeatherLat, settings.WeatherLon);

            // location
            string cityShown = string.IsNullOrWhiteSpace(settings.WeatherCity) ? LocalizationManager.T("Wallpaper.WeatherLocationDefault") : settings.WeatherCity;
            var locLabel = new Label { Text = string.Format(LocalizationManager.T("Wallpaper.WeatherLocationCurrent"), cityShown), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 4) };
            panel.Controls.Add(locLabel);
            var locRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var cityBox = new TextBox { Width = 240, Text = settings.WeatherCity, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, Margin = new Padding(0, 5, 10, 0) };
            var setBtn = new HoverButton { Text = LocalizationManager.T("Wallpaper.WeatherLocationSet"), AutoSize = true, Padding = new Padding(16, 6, 16, 6), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = UiTheme.AccentBlue, BaseColor = UiTheme.AccentBlue, HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f), TabStop = false, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 0) };
            setBtn.FlatAppearance.BorderSize = 0;

            // Ζητήθηκε ρητά μετά από αναφορά χρήστη ότι η αναζήτηση "δεν βγάζει επιλογές πόλεων" όταν
            // υπάρχουν πολλές πόλεις με το ίδιο όνομα (π.χ. "Αθήνα, Ελλάδα" vs "Αθήνα, Ουκρανία") — πριν
            // επιλεγόταν σιωπηλά η πρώτη. Τώρα, όταν το GeocodeMultipleAsync βρει περισσότερα από ένα
            // αποτελέσματα, εμφανίζονται ως κλικαρίσμενες κάρτες εδώ αντί να μαντεύουμε.
            var choicesPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 10), BackColor = Color.Transparent, Visible = false };

            // Controls.Clear() αφαιρεί τα child controls από τη συλλογή αλλά ΔΕΝ τα κάνει Dispose —
            // ίδιο bug class με το SetPage() παραπάνω (βλ. σχόλιο εκεί). Χωρίς αυτό, κάθε νέα αναζήτηση
            // τοποθεσίας θα άφηνε πίσω της ορφανά HoverButton handles.
            void ClearChoices()
            {
                foreach (Control c in choicesPanel.Controls) c.Dispose();
                choicesPanel.Controls.Clear();
                choicesPanel.Visible = false;
            }

            void ApplyMatch(WeatherService.GeocodeMatch m)
            {
                WallpaperHostEngine.Instance.SetWeatherLocation(m.Name, m.Lat, m.Lon);
                locLabel.Text = string.Format(LocalizationManager.T("Wallpaper.WeatherLocationCurrent"), m.Name);
                cityBox.Text = m.Name;
                ClearChoices();
                _ = RefreshLiveWeatherLabelAsync(liveLabel, m.Lat, m.Lon);
            }

            async void ApplyCity()
            {
                var name = cityBox.Text.Trim();
                if (name.Length == 0) return;
                setBtn.Enabled = false;
                ClearChoices();
                try
                {
                    var found = await WeatherService.GeocodeMultipleAsync(name, 5);
                    if (found.Count == 1)
                    {
                        ApplyMatch(found[0]);
                    }
                    else if (found.Count > 1)
                    {
                        locLabel.Text = LocalizationManager.T("Wallpaper.WeatherLocationChoose");
                        foreach (var m in found)
                        {
                            var choiceBtn = new HoverButton
                            {
                                Text = string.IsNullOrEmpty(m.Country) ? m.Name : $"{m.Name}, {m.Country}",
                                AutoSize = true, Padding = new Padding(10, 5, 10, 5), FlatStyle = FlatStyle.Flat,
                                ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, BaseColor = UiTheme.Surface,
                                HoverBackColor = ControlPaint.Light(UiTheme.Surface, 0.15f), TabStop = false, Cursor = Cursors.Hand,
                                Margin = new Padding(0, 0, 0, 4), TextAlign = ContentAlignment.MiddleLeft,
                            };
                            choiceBtn.FlatAppearance.BorderSize = 1;
                            choiceBtn.FlatAppearance.BorderColor = UiTheme.Border;
                            choiceBtn.Click += (_, _) => ApplyMatch(m);
                            choicesPanel.Controls.Add(choiceBtn);
                        }
                        choicesPanel.Visible = true;
                    }
                    else locLabel.Text = LocalizationManager.T("Wallpaper.WeatherLocationNotFound");
                }
                catch (Exception) { locLabel.Text = LocalizationManager.T("Wallpaper.WeatherLocationNotFound"); }
                finally { if (!setBtn.IsDisposed) setBtn.Enabled = true; }
            }
            setBtn.Click += (_, _) => ApplyCity();
            cityBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyCity(); } };
            locRow.Controls.Add(cityBox);
            locRow.Controls.Add(setBtn);
            panel.Controls.Add(locRow);
            panel.Controls.Add(choicesPanel);

            // Ζητήθηκε ρητά: ρώτα μία φορά αν δεν έχει οριστεί ποτέ πόλη, αντί να δείχνουμε σιωπηλά
            // τον καιρό της Αθήνας χωρίς ο χρήστης να το καταλάβει (έτσι βρέθηκε μια αναφορά "δείχνει
            // συννεφιά ενώ βρέχει" που ήταν απλά λάθος τοποθεσία, όχι bug). BeginInvoke ώστε η σελίδα
            // να έχει προλάβει να ζωγραφιστεί πριν εμφανιστεί το MessageBox.
            if (string.IsNullOrWhiteSpace(settings.WeatherCity) && !settings.WeatherLocationPrompted)
            {
                BeginInvoke(new Action(() =>
                {
                    WallpaperHostEngine.Instance.MarkWeatherLocationPrompted();
                    var result = MessageBox.Show(this,
                        LocalizationManager.T("Wallpaper.WeatherLocationPromptBody"),
                        LocalizationManager.T("Wallpaper.WeatherLocationPromptTitle"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes && !cityBox.IsDisposed) cityBox.Focus();
                }));
            }

            // ---- clock / date / temperature overlay + its position (centre of the block, % of the screen)
            FlatSlider? infoX = null, infoY = null, infoScale = null;
            CheckBox? infoShow = null, infoF = null, infoAll = null;
            void CommitInfo()
            {
                if (infoX == null || infoY == null || infoScale == null || infoShow == null || infoF == null || infoAll == null) return;
                WallpaperHostEngine.Instance.SetWeatherInfo(infoShow.Checked, infoAll.Checked, infoX.Value, infoY.Value, infoScale.Value, infoF.Checked);
            }
            infoShow = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.WeatherInfo"), AutoSize = true, Checked = settings.WeatherShowInfo, BackColor = UiTheme.Background, Margin = new Padding(0, 8, 0, 4) };
            infoF = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.Fahrenheit"), AutoSize = true, Checked = settings.WeatherFahrenheit, BackColor = UiTheme.Background, Margin = new Padding(0, 0, 0, 6) };
            infoAll = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.InfoAllModes"), AutoSize = true, Checked = settings.InfoOnAllModes, BackColor = UiTheme.Background, Margin = new Padding(0, 0, 0, 4) };
            infoShow.CheckedChanged += (_, _) => CommitInfo();
            infoAll.CheckedChanged += (_, _) => CommitInfo();
            infoF.CheckedChanged += (_, _) => CommitInfo();
            panel.Controls.Add(infoShow);
            panel.Controls.Add(infoAll);
            panel.Controls.Add(infoF);
            // ---- font of the overlay: common Windows fonts that are installed, plus "More…" for any other installed font
            var fontRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            fontRow.Controls.Add(new Label { Text = LocalizationManager.T("Wallpaper.InfoFont"), AutoSize = false, Width = 150, Height = 28, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
            var installed = new HashSet<string>(new System.Drawing.Text.InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            var fontNames = new List<string> { "Segoe UI Light", "Segoe UI", "Segoe UI Semibold", "Segoe UI Black", "Bahnschrift Light", "Bahnschrift", "Calibri Light", "Calibri", "Arial", "Arial Narrow", "Verdana", "Tahoma", "Trebuchet MS", "Georgia", "Cambria", "Consolas", "Courier New", "Impact", "Comic Sans MS", "Times New Roman" }
                .Where(installed.Contains).ToList();
            string currentFont = string.IsNullOrWhiteSpace(settings.InfoFont) ? "Segoe UI Light" : settings.InfoFont;
            if (!fontNames.Contains(currentFont, StringComparer.OrdinalIgnoreCase)) fontNames.Insert(0, currentFont);
            var fontCombo = new FlatComboBox { Width = 240 };
            fontCombo.SetItems(fontNames, currentFont);
            fontCombo.SelectedIndexChanged += (_, _) => { if (fontCombo.SelectedItem is { } f) WallpaperHostEngine.Instance.SetInfoFont(f); };
            var moreFonts = NewHoverButton(LocalizationManager.T("Wallpaper.InfoFontMore"), () =>
            {
                using var dlg = new FontDialog { FontMustExist = true, ShowEffects = false, AllowScriptChange = false, Font = new Font(fontCombo.SelectedItem ?? "Segoe UI", 12f) };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                var family = dlg.Font.FontFamily.Name;
                if (!fontNames.Contains(family, StringComparer.OrdinalIgnoreCase)) fontNames.Insert(0, family);
                fontCombo.SetItems(fontNames, family);
                WallpaperHostEngine.Instance.SetInfoFont(family);
            });
            moreFonts.Margin = new Padding(10, 0, 0, 0);
            fontRow.Controls.Add(fontCombo);
            fontRow.Controls.Add(moreFonts);
            panel.Controls.Add(fontRow);
            panel.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.InfoX"), settings.WeatherInfoX, 0, 100, v => $"{v}%", CommitInfo, t => infoX = t));
            panel.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.InfoY"), settings.WeatherInfoY, 0, 100, v => $"{v}%", CommitInfo, t => infoY = t));
            panel.Controls.Add(BuildOptionSlider(LocalizationManager.T("Wallpaper.InfoScale"), settings.WeatherInfoScale, 40, 250, v => $"{v}%", CommitInfo, t => infoScale = t));
            panel.Controls.Add(NewHoverButton(LocalizationManager.T("Wallpaper.InfoReset"), () =>
            {
                // back to the default position: centred horizontally, upper third of the screen (the size slider is left alone)
                if (infoX != null) infoX.Value = 50;
                if (infoY != null) infoY.Value = 34;
            }));
        }

        private async System.Threading.Tasks.Task RefreshLiveWeatherLabelAsync(Label target, double lat, double lon)
        {
            string text;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(await WeatherService.GetWallpaperWeatherJsonAsync(lat, lon));
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                {
                    int code = root.GetProperty("code").GetInt32();
                    string cond = LocalizationManager.T("Wallpaper.Sim." + CodeToSimId(code));
                    // Ζητήθηκε ρητά "η θερμοκρασία να είναι με 1 δεκαδικό" — "0.#" έδειχνε "18°C" όταν
                    // η τιμή έτυχε να είναι ακέραια (παραλείπει το .0), ασυνεπές με την επικάλυψη του
                    // wallpaper (που δείχνει πάντα 1 δεκαδικό). "0.0" δείχνει πάντα ακριβώς 1 δεκαδικό.
                    text = string.Format(LocalizationManager.T("Wallpaper.WeatherLive"), root.GetProperty("temp").GetDouble().ToString("0.0"), root.GetProperty("wind").GetDouble().ToString("0"), cond, WeatherService.ToBeaufort(root.GetProperty("wind").GetDouble()));
                }
                else text = LocalizationManager.T("Wallpaper.WeatherLiveUnavailable");
            }
            catch (Exception) { text = LocalizationManager.T("Wallpaper.WeatherLiveUnavailable"); }
            if (!target.IsDisposed) target.Text = text;
        }

        private static string CodeToSimId(int code) => code switch
        {
            0 => "Clear", 1 or 2 => "PartlyCloudy", 3 => "Cloudy", 45 or 48 => "Fog", 51 or 53 or 55 or 56 or 57 => "Drizzle",
            61 or 66 or 67 or 80 => "Rain", 63 or 81 => "Rain", 65 or 82 => "HeavyRain", 71 or 73 or 75 or 77 or 85 or 86 => "Snow", 95 or 96 or 99 => "Thunderstorm", _ => "Clear"
        };

        private static async System.Threading.Tasks.Task LoadVideoRowMetaAsync(string path, PictureBox thumb, Label duration)
        {
            try
            {
                var bmp = await VideoMetaService.GetThumbnailAsync(path);
                if (bmp != null && !thumb.IsDisposed) thumb.Image = bmp;      // the bitmap is shared/cached: never dispose it here
                var len = await VideoMetaService.GetDurationAsync(path);
                if (len != null && !duration.IsDisposed) duration.Text = len;
            }
            catch (Exception) { /* thumbnails/durations are decoration only */ }
        }

        // Slider with a live value readout that commits 500 ms after the last movement
        private static Panel BuildOptionSlider(string label, int value, int min, int max, Func<int, string> format, Action commit, Action<FlatSlider> register)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 230, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
            var track = new FlatSlider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220 };
            var readout = new Label { Text = format(track.Value), AutoSize = false, Width = 130, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.AccentCyan, Font = UiTheme.FontBody };
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) => { timer.Stop(); commit(); };
            track.ValueChanged += (_, _) => { readout.Text = format(track.Value); timer.Stop(); timer.Start(); };
            track.Disposed += (_, _) => timer.Dispose();
            register(track);
            row.Controls.Add(track);
            row.Controls.Add(readout);
            return row;
        }

        // Time-of-day schedule: up to 3 windows, each switching the wallpaper MODE
        private static readonly string[] ScheduleModes = { "Waves", "Particles", "Weather", "Video", "Image", "Page", "Application", "Desktop" };

        private void BuildWallpaperScheduleSection(Panel panel, WallpaperSettings settings)
        {
            AddSection(panel, LocalizationManager.T("Wallpaper.SectionSchedule"));
            AddText(panel, LocalizationManager.T("Wallpaper.ScheduleNote"));

            var rules = settings.Schedule.Count > 0
                ? settings.Schedule.Select(r => new WallpaperScheduleRule { Enabled = r.Enabled, StartMinutes = r.StartMinutes, EndMinutes = r.EndMinutes, Mode = r.Mode }).ToList()
                : new List<WallpaperScheduleRule>
                {
                    new() { Enabled = false, StartMinutes = 7 * 60, EndMinutes = 19 * 60, Mode = "Waves" },
                    new() { Enabled = false, StartMinutes = 19 * 60, EndMinutes = 23 * 60, Mode = "Weather" },
                    new() { Enabled = false, StartMinutes = 23 * 60, EndMinutes = 7 * 60, Mode = "Particles" },
                };
            while (rules.Count < 3) rules.Add(new WallpaperScheduleRule { Enabled = false, StartMinutes = 0, EndMinutes = 0, Mode = "Waves" });

            var master = new FlatCheckBox { Text = LocalizationManager.T("Wallpaper.ScheduleEnable"), AutoSize = true, Checked = settings.ScheduleEnabled, BackColor = UiTheme.Background, Margin = new Padding(0, 2, 0, 8) };
            panel.Controls.Add(master);

            var times = Enumerable.Range(0, 48).Select(i => $"{i / 2:00}:{i % 2 * 30:00}").ToArray();
            string Fmt(int m) => $"{m / 60 % 24:00}:{m % 60 / 30 * 30:00}";
            void Commit() => WallpaperHostEngine.Instance.SetSchedule(master.Checked, rules.Select(r => new WallpaperScheduleRule { Enabled = r.Enabled, StartMinutes = r.StartMinutes, EndMinutes = r.EndMinutes, Mode = r.Mode }).ToList());

            foreach (var rule in rules)
            {
                var rowPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
                var on = new FlatCheckBox { Checked = rule.Enabled, AutoSize = false, Width = 26, Height = 28, BackColor = UiTheme.Background, Margin = new Padding(0, 2, 6, 0) };
                var start = new FlatComboBox { Width = 90 }; start.SetItems(times, Fmt(rule.StartMinutes));
                var end = new FlatComboBox { Width = 90, Margin = new Padding(8, 3, 3, 3) }; end.SetItems(times, Fmt(rule.EndMinutes));
                var modeLabels = ScheduleModes.Select(m => m).ToArray();
                var mode = new FlatComboBox { Width = 160, Margin = new Padding(16, 3, 3, 3) }; mode.SetItems(modeLabels, ScheduleModes.Contains(rule.Mode) ? rule.Mode : "Waves");
                var arrow = new Label { Text = "→", AutoSize = false, Width = 24, Height = 28, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Margin = new Padding(6, 3, 0, 0) };
                int ParseMinutes(string? t) => t != null && t.Length == 5 && int.TryParse(t[..2], out var h) && int.TryParse(t[3..], out var mi) ? h * 60 + mi : 0;
                on.CheckedChanged += (_, _) => { rule.Enabled = on.Checked; Commit(); };
                start.SelectedIndexChanged += (_, _) => { rule.StartMinutes = ParseMinutes(start.SelectedItem); Commit(); };
                end.SelectedIndexChanged += (_, _) => { rule.EndMinutes = ParseMinutes(end.SelectedItem); Commit(); };
                mode.SelectedIndexChanged += (_, _) => { rule.Mode = mode.SelectedItem ?? "Waves"; Commit(); };
                rowPanel.Controls.Add(on); rowPanel.Controls.Add(start); rowPanel.Controls.Add(arrow); rowPanel.Controls.Add(end); rowPanel.Controls.Add(mode);
                panel.Controls.Add(rowPanel);
            }
            master.CheckedChanged += (_, _) => Commit();
        }

        private static string DescribeWallpaperState(WallpaperSettings s) =>
            string.Format(LocalizationManager.T("Wallpaper.StateFormat"), WallpaperHostEngine.Instance.IsEnabled ? LocalizationManager.T("Wallpaper.EnabledShort") : LocalizationManager.T("Wallpaper.DisabledShort"), s.Mode, s.PerformanceMode);

        private static string DescribePlaylist(WallpaperSettings s)
        {
            var playable = s.VideoPaths.Where(File.Exists).ToList();
            if (playable.Count == 0) return LocalizationManager.T("Wallpaper.PlaylistEmpty");
            return string.Format(LocalizationManager.T("Wallpaper.PlaylistFormat"), playable.Count, s.Shuffle ? LocalizationManager.T("Wallpaper.ShuffleSuffix") : "", Path.GetFileName(s.CurrentPlaylistFile() ?? ""));
        }

        private static Panel BuildWaveSlider(string label, int value, int min, int max, Action<int> onChange)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            row.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = UiTheme.TextSecondary, Width = 110, Padding = new Padding(0, 6, 8, 0) });
            var track = new FlatSlider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220 };
            track.ValueChanged += (_, _) => onChange(track.Value);
            row.Controls.Add(track);
            return row;
        }

        private static void ChooseWallpaperVideoFiles(Action? onCompleted = null)
        {
            // Ζητήθηκε ρητά "δυνατότητα φόρτωσης video μεμονωμένων ή playlists με αρχεία
            // zip/7zip" — το ίδιο dialog δέχεται πλέον και αρχεία συμπίεσης· η εξαγωγή τους
            // γίνεται στο AddWallpaperVideosAsync.
            using var dialog = new OpenFileDialog
            {
                Filter = LocalizationManager.T("Filter.VideoPlaylist") + "|" + LocalizationManager.T("Filter.AllFiles"),
                Title = LocalizationManager.T("Wallpaper.AddVideoTitle"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                _ = AddWallpaperVideosAsync(dialog.FileNames, onCompleted);
        }

        // Το Wallpaper Studio αποδίδει τα βίντεο μέσα σε WebView2 (Chromium <video>), το οποίο ΔΕΝ
        // περιλαμβάνει decoder για τον κλασικό WMV3/VC-1 codec (μόνο H.264/VP8/VP9/AV1/Theora).
        // Αντί για απλή προειδοποίηση, τα .wmv μετατρέπονται πλέον ΑΥΤΟΜΑΤΑ σε .mp4 μέσω FFmpeg
        // (WmvConversionService) πριν προστεθούν στη λίστα — βλ. αναλυτικό σχόλιο εκεί για το
        // γιατί επιλέχθηκε FFmpeg αντί για COM/ActiveX εναλλακτικές.
        private static async Task AddWallpaperVideosAsync(string[] fileNames, Action? onCompleted)
        {
            // Αρχεία .zip/.7z εξάγονται πρώτα — τα βίντεο που βρίσκονται μέσα τους μπαίνουν στην
            // ίδια ροή (ίδιος έλεγχος .wmv->mp4 παρακάτω) σαν να τα είχε επιλέξει ένα-ένα ο
            // χρήστης. Ένα .7z χωρίς εγκατεστημένο 7-Zip στο σύστημα παραλείπεται σιωπηλά εδώ,
            // με προειδοποίηση+σύνδεσμο λήψης μετά την επεξεργασία των υπολοίπων.
            var archiveFiles = fileNames.Where(ArchivePlaylistService.IsArchive).ToArray();
            var plainFiles = fileNames.Except(archiveFiles).ToList();
            bool anySevenZipMissing = false;

            foreach (var archive in archiveFiles)
            {
                if (string.Equals(Path.GetExtension(archive), ".7z", StringComparison.OrdinalIgnoreCase) && !ArchivePlaylistService.IsSevenZipAvailable)
                {
                    anySevenZipMissing = true;
                    continue;
                }
                plainFiles.AddRange(await Task.Run(() => ArchivePlaylistService.ExtractVideos(archive))); // εκτός UI thread — η εξαγωγή μεγάλου αρχείου πάγωνε το παράθυρο
            }

            if (anySevenZipMissing)
            {
var choice = MessageBox.Show(
                    LocalizationManager.T("Dialog.SevenZipMissing"),
                    LocalizationManager.T("Dialog.SevenZipTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes) ArchivePlaylistService.OpenSevenZipDownloadPage();
            }

            fileNames = plainFiles.ToArray();

            var wmvFiles = fileNames.Where(f => string.Equals(Path.GetExtension(f), ".wmv", StringComparison.OrdinalIgnoreCase)).ToArray();
            var finalPaths = fileNames.Except(wmvFiles).ToList();

            if (wmvFiles.Length > 0)
            {
                if (!WmvConversionService.IsFfmpegAvailable)
                {
WmvConversionService.PromptInstallFfmpeg(wmvFiles.Length);
                }
                else
                {
                    foreach (var wmv in wmvFiles)
                    {
                        string? mp4 = await WmvConversionService.ConvertToMp4Async(wmv);
                        if (mp4 != null) finalPaths.Add(mp4);
                    }
                    if (finalPaths.Count == 0)
                        MessageBox.Show(LocalizationManager.T("Dialog.ConvertFailed"), LocalizationManager.T("Dialog.ConvertFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (finalPaths.Count > 0)
            {
                var settings = WallpaperSettings.Load();
                settings.AddVideoFiles(finalPaths.ToArray());
                settings.Mode = "Video";
                settings.Save();
                WallpaperHostEngine.Instance.Enable();
                // ΚΡΙΣΙΜΟ: το Enable() από μόνο του ΔΕΝ ζητάει motionDeskRefresh() από ένα ήδη
                // ανοιχτό/ορατό wallpaper window (μόνο BringToFront/Show) — χωρίς αυτό, ένα ήδη
                // τρέχον WebView2 σε λειτουργία Waves δεν μαθαίνει ποτέ ότι το Mode έγινε Video,
                // το .mp4 μετατρέπεται επιτυχώς αλλά δεν παίζει ποτέ.
                _ = WallpaperHostEngine.Instance.RefreshAllAsync();
            }
            onCompleted?.Invoke();
        }

        private static void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = LocalizationManager.T("Wallpaper.ChooseFolder") };
            if (dialog.ShowDialog() == DialogResult.OK)
                WallpaperHostEngine.Instance.AddVideoFolder(dialog.SelectedPath);
        }


        // "Εξατομίκευση" — τα ασφαλή (registry/native API μόνο, καμία τροποποίηση αρχείων
        // συστήματος) κομμάτια του Customization Vision: IconAtlas, DeskCursors, DeskStrip,
        // DeskSounds. Όλα τα ονόματα ελέγχθηκαν ρητά για συγκρούσεις επωνυμίας πριν επιλεγούν.
        private void ShowPersonalization()
        {
            _currentPageKey = "Personalization";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Personalization.Intro"));

            // ---------- IconAtlas ----------
            var iconCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionIconAtlas"));
            AddText(iconCard, LocalizationManager.T("Personalization.IconAtlasIntro"));

            Control BuildIconSlotRow(string labelKey, Func<string?> get, Action<string?> set)
            {
                var row = new Panel { Width = 720, Height = 44, Margin = new Padding(0, 0, 0, 8) };
                var preview = new PictureBox { Location = new Point(0, 4), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Color.Transparent };

                void RefreshPreview()
                {
                    preview.Image?.Dispose();
                    preview.Image = null;
                    var p = get();
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        try { using var ic = Icon.ExtractAssociatedIcon(p); if (ic != null) preview.Image = ic.ToBitmap(); }
                        catch (Exception) { }
                    }
                }
                RefreshPreview();
                row.Controls.Add(preview);

                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = true, Location = new Point(42, 13) });

                var chooseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.ChooseIcon"), Width = 160, Height = 30, Location = new Point(320, 7), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                chooseBtn.FlatAppearance.BorderSize = 0;
                chooseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Icons"), Title = LocalizationManager.T("Personalization.ChooseIcon") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        set(dlg.FileName);
                        RefreshPreview();
                        _statusLabel.Text = LocalizationManager.T("Personalization.IconAppliedStatus");
                    }
                };
                row.Controls.Add(chooseBtn);

                var resetBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Reset"), Width = 90, Height = 30, Location = new Point(490, 7), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextSecondary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                resetBtn.FlatAppearance.BorderSize = 0;
                resetBtn.Click += (_, _) => { set(null); RefreshPreview(); _statusLabel.Text = LocalizationManager.T("Personalization.IconResetStatus"); };
                row.Controls.Add(resetBtn);

                return row;
            }

            iconCard.Controls.Add(BuildIconSlotRow("Personalization.ThisPcIcon", IconAtlasEngine.GetThisPcIcon, IconAtlasEngine.SetThisPcIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinEmpty", IconAtlasEngine.GetRecycleBinEmptyIcon, IconAtlasEngine.SetRecycleBinEmptyIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.RecycleBinFull", IconAtlasEngine.GetRecycleBinFullIcon, IconAtlasEngine.SetRecycleBinFullIcon));
            // Επέκταση κάλυψης (ζητήθηκε ρητά v1.5.0) — τα υπόλοιπα τυπικά εικονίδια επιφάνειας
            // εργασίας που υποστηρίζουν επίσημα custom icon μέσω registry.
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.NetworkIcon", IconAtlasEngine.GetNetworkIcon, IconAtlasEngine.SetNetworkIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.ControlPanelIcon", IconAtlasEngine.GetControlPanelIcon, IconAtlasEngine.SetControlPanelIcon));
            iconCard.Controls.Add(BuildIconSlotRow("Personalization.UsersFilesIcon", IconAtlasEngine.GetUsersFilesIcon, IconAtlasEngine.SetUsersFilesIcon));

            // "Εισαγωγή icon pack (depot)…" — σαρώνει έναν φάκελο για έτοιμα ζευγάρια εικονιδίων
            // Κάδου Ανακύκλωσης (σύμβαση "-empty"/"-full", π.χ. sdushantha/recycle-bin-themes στο
            // GitHub) και τα εφαρμόζει αυτόματα, αντί να χρειάζεται να επιλέξει ο χρήστης τα δύο
            // αρχεία ένα-ένα από τις παραπάνω γραμμές.
            AddButtonGrid(iconCard, (LocalizationManager.T("Personalization.IconPackImport"), (_, _) =>
            {
                using var folderDlg = new FolderBrowserDialog { Description = LocalizationManager.T("Personalization.IconPackFolderPrompt") };
                if (folderDlg.ShowDialog() != DialogResult.OK) return;
                var depot = IconAtlasEngine.ScanDepotFolder(folderDlg.SelectedPath);
                if (depot.EmptyIconPath != null || depot.FullIconPath != null)
                {
                    IconAtlasEngine.ApplyDepotToRecycleBin(depot);
                    _statusLabel.Text = string.Format(LocalizationManager.T("Personalization.IconPackAppliedFormat"),
                        LocalizationManager.T(depot.EmptyIconPath != null ? "Personalization.IconPackEmptyOk" : "Personalization.IconPackEmptyMissing"),
                        LocalizationManager.T(depot.FullIconPath != null ? "Personalization.IconPackFullOk" : "Personalization.IconPackFullMissing"));
                }
                else if (depot.SingleIconPath != null)
                {
                    IconAtlasEngine.SetThisPcIcon(depot.SingleIconPath);
                    _statusLabel.Text = LocalizationManager.T("Personalization.IconPackSingleApplied");
                }
                else
                {
                    MessageBox.Show(this, LocalizationManager.T("Personalization.IconPackNotFound"), LocalizationManager.T("Personalization.IconPackTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                ShowPersonalization();
            }),
            // 1.7.8 - "Εισαγωγή icon pack" και "Ορισμός εικονιδίου φακέλου" στην ΙΔΙΑ γραμμή (ένα AddButtonGrid).
            (LocalizationManager.T("Personalization.FolderIcon"), (_, _) =>
            {
                using var folderDlg = new FolderBrowserDialog { Description = LocalizationManager.T("Personalization.FolderIcon") };
                if (folderDlg.ShowDialog() != DialogResult.OK) return;
                using var iconDlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Icons"), Title = LocalizationManager.T("Personalization.ChooseIcon") };
                if (iconDlg.ShowDialog(this) == DialogResult.OK)
                {
                    IconAtlasEngine.SetFolderIcon(folderDlg.SelectedPath, iconDlg.FileName);
                    _statusLabel.Text = LocalizationManager.T("Personalization.FolderIconAppliedStatus");
                }
            }));

            // ---------- DeskCursors ----------
            var cursorCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionDeskCursors"));
            AddText(cursorCard, LocalizationManager.T("Personalization.DeskCursorsIntro"));

            foreach (var (registryName, labelKey) in DeskCursorsEngine.Roles)
            {
                var row = new Panel { Width = 720, Height = 36, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 6), Size = new Size(185, 24) });
                var pathLabel = new Label { Text = Path.GetFileName(DeskCursorsEngine.GetCursor(registryName) ?? "") is { Length: > 0 } fn ? fn : LocalizationManager.T("Personalization.NotSet"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(190, 6), Size = new Size(230, 24) };
                row.Controls.Add(pathLabel);

                var browseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Browse"), Width = 110, Height = 28, Location = new Point(430, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                browseBtn.FlatAppearance.BorderSize = 0;
                string capturedRole = registryName;
                browseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Cursors"), Title = LocalizationManager.T("Personalization.Browse") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        DeskCursorsEngine.SetCursor(capturedRole, dlg.FileName);
                        pathLabel.Text = Path.GetFileName(dlg.FileName);
                    }
                };
                row.Controls.Add(browseBtn);
                cursorCard.Controls.Add(row);
            }

            AddButtonGrid(cursorCard,
                (LocalizationManager.T("Personalization.ApplyCursors"), (_, _) => { DeskCursorsEngine.ApplyNow(); _statusLabel.Text = LocalizationManager.T("Personalization.CursorsAppliedStatus"); }),
                (LocalizationManager.T("Personalization.ResetCursors"), (_, _) => { DeskCursorsEngine.ResetAllToWindowsDefault(); if (_pageBuilders.TryGetValue("Personalization", out var rebuild)) rebuild(); }));

            // ---------- DeskStrip ----------
            var deskStripCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionDeskStrip"));
            AddText(deskStripCard, LocalizationManager.T("Personalization.DeskStripIntro"));

            var deskStripToggle = new FlatCheckBox { Text = LocalizationManager.T("Personalization.EnableDeskStrip"), AutoSize = true, Checked = DeskStripHostEngine.Instance.IsEnabled, BackColor = UiTheme.Surface, Margin = new Padding(0, 4, 0, 10) };
            deskStripCard.Controls.Add(deskStripToggle);

            // Ρυθμίσεις dock (θέση, auto-hide, μέγεθος, μεγέθυνση, τρέχουσες εφαρμογές, προεπισκοπήσεις) — εφαρμόζονται αμέσως.
            string Ld(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            var dockCfg = DeskStripHostEngine.Instance.GetSettings();
            var posValues = new[] { DockPosition.Bottom, DockPosition.Top, DockPosition.Left, DockPosition.Right };
            var posLabels = new[] { Ld("Κάτω", "Bottom"), Ld("Πάνω", "Top"), Ld("Αριστερά", "Left"), Ld("Δεξιά", "Right") };
            deskStripCard.Controls.Add(new Label { AutoSize = true, Text = Ld("Θέση στην οθόνη", "Position on screen"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Margin = new Padding(0, 2, 0, 2) });
            var posCombo = new FlatComboBox { Width = 220, Margin = new Padding(0, 0, 0, 8) };
            posCombo.SetItems(posLabels, posLabels[Math.Max(0, Array.IndexOf(posValues, dockCfg.Position))]);
            posCombo.SelectedIndexChanged += (_, _) => DeskStripHostEngine.Instance.UpdateSettings(c => c.Position = posValues[Math.Max(0, posCombo.SelectedIndex)]);
            deskStripCard.Controls.Add(posCombo);

            FlatCheckBox DockCheck(string text, bool value, Action<DeskStripSettings, bool> apply)
            {
                var cb = new FlatCheckBox { Text = text, AutoSize = true, Checked = value, BackColor = UiTheme.Surface, Margin = new Padding(0, 2, 0, 4) };
                cb.CheckedChanged += (_, _) => DeskStripHostEngine.Instance.UpdateSettings(c => apply(c, cb.Checked));
                deskStripCard.Controls.Add(cb);
                return cb;
            }
            DockCheck(Ld("Αυτόματη απόκρυψη (εμφάνιση από την άκρη της οθόνης)", "Auto-hide (reveal from the screen edge)"), dockCfg.AutoHide, (c, v) => c.AutoHide = v);
            DockCheck(Ld("Μεγέθυνση εικονιδίων στο hover", "Magnify icons on hover"), dockCfg.Magnify, (c, v) => c.Magnify = v);
            DockCheck(Ld("Εμφάνιση τρεχουσών εφαρμογών", "Show running apps"), dockCfg.ShowRunning, (c, v) => c.ShowRunning = v);
            DockCheck(Ld("Προεπισκόπηση παραθύρου στο hover", "Window preview on hover"), dockCfg.ShowPreviews, (c, v) => c.ShowPreviews = v);
            var sizeLabel = new Label { AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Margin = new Padding(0, 6, 0, 2) };
            void ShowSize(int v) => sizeLabel.Text = Ld($"Μέγεθος εικονιδίων: {v}px", $"Icon size: {v}px");
            ShowSize(dockCfg.IconSize);
            deskStripCard.Controls.Add(sizeLabel);
            var sizeSlider = new FlatSlider { Minimum = 28, Maximum = 72, Value = Math.Clamp(dockCfg.IconSize, 28, 72), Width = 300, Height = 28, Margin = new Padding(0, 0, 0, 10) };
            sizeSlider.ValueChanged += (_, _) => { ShowSize(sizeSlider.Value); DeskStripHostEngine.Instance.UpdateSettings(c => c.IconSize = sizeSlider.Value); };
            deskStripCard.Controls.Add(sizeSlider);

            var pinnedList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            deskStripCard.Controls.Add(pinnedList);

            // Ίδια διόρθωση flicker με RefreshActiveList/RefreshActiveContainers: skip το rebuild
            // όταν η λίστα δεν άλλαξε — πέρα από το flicker, γλιτώνει και το άσκοπο save/restore
            // scroll σε κάθε tick.
            string? lastPinnedSignature = null;
            void RefreshPinnedApps()
            {
                var pinned = DeskStripHostEngine.Instance.GetPinnedApps();
                string signature = string.Join("|", pinned);
                if (signature == lastPinnedSignature) return;
                lastPinnedSignature = signature;

                // Bug fix: αυτή η συνάρτηση καλείται κάθε 2 δευτερόλεπτα από timer (βλ.
                // deskStripRefreshTimer παρακάτω) ενώ ο χρήστης μπορεί να έχει ήδη κυλήσει
                // ΠΟΛΥ πιο κάτω στη σελίδα (π.χ. στο "Φόντο οθόνης κλειδώματος"). Το
                // Controls.Clear()+rebuild ενός FlowLayoutPanel μέσα σε ένα AutoScroll container
                // επαναφέρει σιωπηλά τη θέση κύλισης — αναφέρθηκε ως "autoscrolling πίσω στα
                // DeskSounds". Λύση: αποθήκευση/επαναφορά της θέσης κύλισης της ΣΕΛΙΔΑΣ γύρω από
                // την ανακατασκευή (το AutoScrollPosition GET επιστρέφει αρνητικές τιμές, το SET
                // περιμένει θετικές — WinForms ιδιαιτερότητα).
                var savedScroll = panel.AutoScrollPosition;
                pinnedList.SuspendLayout();
                DisposeChildren(pinnedList);
                if (pinned.Count == 0)
                {
                    pinnedList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Common.NoPinnedApps"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var appPath in pinned)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    row.Controls.Add(new Label { Text = Path.GetFileNameWithoutExtension(appPath), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var removeBtn = new HoverButton { Text = "✕", Width = 36, Height = 28, Location = new Point(444, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    removeBtn.FlatAppearance.BorderSize = 0;
                    string capturedPath = appPath;
                    removeBtn.Click += (_, _) => { DeskStripHostEngine.Instance.UnpinApp(capturedPath); RefreshPinnedApps(); };
                    row.Controls.Add(removeBtn);
                    pinnedList.Controls.Add(row);
                }
                pinnedList.ResumeLayout();
                panel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            }

            deskStripToggle.CheckedChanged += (_, _) =>
            {
                if (deskStripToggle.Checked) DeskStripHostEngine.Instance.Enable(); else DeskStripHostEngine.Instance.Disable();
                RefreshPinnedApps();
            };

            AddButtonGrid(deskStripCard, (LocalizationManager.T("Personalization.PinApp"), (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Apps"), Title = LocalizationManager.T("Personalization.PinApp") };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    DeskStripHostEngine.Instance.PinApp(dlg.FileName);
                    deskStripToggle.Checked = true;
                    RefreshPinnedApps();
                }
            }));

            RefreshPinnedApps();
            var deskStripRefreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            deskStripRefreshTimer.Tick += (_, _) => RefreshPinnedApps();
            deskStripRefreshTimer.Start();
            panel.Disposed += (_, _) => deskStripRefreshTimer.Dispose();

            // ---------- DeskSounds ----------
            var soundsCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionDeskSounds"));
            AddText(soundsCard, LocalizationManager.T("Personalization.DeskSoundsIntro"));

            foreach (var (eventLabel, labelKey) in DeskSoundsEngine.Events)
            {
                // Bug fix: τα κουμπιά ήταν πολύ στενά/πολύ αριστερά για το πραγματικό μήκος των
                // ελληνικών ετικετών (π.χ. "Προεπισκόπηση ▶") — φαρδύτερα κουμπιά + μετατόπιση
                // δεξιά ώστε να χωράει όλο το κείμενο χωρίς να κόβεται.
                var row = new Panel { Width = 760, Height = 36, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T(labelKey), ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 6), Size = new Size(150, 24) });
                var pathLabel = new Label { Text = Path.GetFileName(DeskSoundsEngine.GetSound(eventLabel) ?? "") is { Length: > 0 } sfn ? sfn : LocalizationManager.T("Personalization.NotSet"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(156, 6), Size = new Size(170, 24) };
                row.Controls.Add(pathLabel);

                string capturedEvent = eventLabel;

                var browseBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Browse"), Width = 116, Height = 28, Location = new Point(336, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                browseBtn.FlatAppearance.BorderSize = 0;
                browseBtn.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Wav"), Title = LocalizationManager.T("Personalization.Browse") };
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        DeskSoundsEngine.SetSound(capturedEvent, dlg.FileName);
                        pathLabel.Text = Path.GetFileName(dlg.FileName);
                    }
                };
                row.Controls.Add(browseBtn);

                var previewBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Preview"), Width = 150, Height = 28, Location = new Point(460, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                previewBtn.FlatAppearance.BorderSize = 0;
                previewBtn.Click += (_, _) => { var s = DeskSoundsEngine.GetSound(capturedEvent); if (!string.IsNullOrEmpty(s) && File.Exists(s)) DeskSoundsEngine.Preview(s); };
                row.Controls.Add(previewBtn);

                var clearBtn = new HoverButton { Text = LocalizationManager.T("Personalization.Clear"), Width = 110, Height = 28, Location = new Point(618, 4), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextSecondary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                clearBtn.FlatAppearance.BorderSize = 0;
                clearBtn.Click += (_, _) => { DeskSoundsEngine.SetSound(capturedEvent, null); pathLabel.Text = LocalizationManager.T("Personalization.NotSet"); };
                row.Controls.Add(clearBtn);

                soundsCard.Controls.Add(row);
            }

            // ---------- .theme Packages ----------
            var themeCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionThemePackages"));
            AddText(themeCard, LocalizationManager.T("Personalization.ThemePackagesIntro"));
            // Για ΠΛΗΡΗ θέματα (light/dark, accent, εφέ παραθύρων, wallpaper, εισαγωγή/εξαγωγή) υπάρχει το Theme Manager του plugin Opsis Appearance.
            string LdTheme(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            AddText(themeCard, LdTheme("Τα παρακάτω .theme των Windows αλλάζουν wallpaper, δείκτες και ήχους. Για πλήρη θέματα (σκούρο/φωτεινό, accent, εφέ παραθύρων, wallpaper, εισαγωγή/εξαγωγή) χρησιμοποίησε το Theme Manager.",
                "The Windows .theme files below change wallpaper, cursors and sounds. For full themes (dark/light, accent, window effects, wallpaper, import/export) use the Theme Manager."));
            AddButtonGrid(themeCard, (LdTheme("Άνοιγμα Theme Manager (Opsis Appearance)", "Open Theme Manager (Opsis Appearance)"), (_, _) =>
            {
                if (Services.PluginHost.Instance.GetNavigationItems().Any(i => i.Key == "OpsisAppearance")) NavigateTo("plugin:OpsisAppearance");
                else
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/giorstergiopoulos-design/OpsisAppearance/releases/latest") { UseShellExecute = true }); } catch (Exception) { }
                    _statusLabel.Text = LdTheme("Το plugin Opsis Appearance δεν είναι εγκατεστημένο — άνοιξε η σελίδα λήψης.", "The Opsis Appearance plugin is not installed — opened its download page.");
                }
            }));

            var themeList = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            themeCard.Controls.Add(themeList);

            void RefreshThemeList()
            {
                // Ίδια διόρθωση scroll-reset με το RefreshActiveList (Widgets page) — καλείται ξανά
                // μετά το "Αποθήκευση τρέχοντος θέματος", ενδεχομένως ενώ ο χρήστης είναι κυλισμένος
                // κάτω σε αυτό το (μεγάλο) Personalization page.
                var savedScroll = panel.AutoScrollPosition;
                themeList.SuspendLayout();
                DisposeChildren(themeList);
                var themes = ThemePackageEngine.ListAvailableThemes();
                if (themes.Count == 0)
                {
                    themeList.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("Personalization.NoThemesFound"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody });
                }
                foreach (var theme in themes)
                {
                    var row = new Panel { Width = 500, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                    UiTheme.ApplyRoundedRegion(row, 6);
                    string label = theme.Name + (theme.BuiltIn ? LocalizationManager.T("Personalization.BuiltInSuffix") : "");
                    row.Controls.Add(new Label { Text = label, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, Location = new Point(14, 11), AutoSize = true });
                    var applyBtn = new HoverButton { Text = LocalizationManager.T("Personalization.ApplyThemeBtn"), Width = 90, Height = 28, Location = new Point(394, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    applyBtn.FlatAppearance.BorderSize = 0;
                    string capturedPath = theme.Path;
                    applyBtn.Click += (_, _) => { ThemePackageEngine.ApplyTheme(capturedPath); _statusLabel.Text = LocalizationManager.T("Personalization.ThemeAppliedStatus"); };
                    row.Controls.Add(applyBtn);
                    themeList.Controls.Add(row);
                }
                themeList.ResumeLayout();
                panel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
            }
            RefreshThemeList();

            AddButtonGrid(themeCard, (LocalizationManager.T("Personalization.SaveCurrentTheme"), (_, _) =>
            {
                using var dialog = new Form { Text = LocalizationManager.T("Personalization.ThemeNamePrompt"), StartPosition = FormStartPosition.CenterParent, Size = new Size(420, 150), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
                var box = new TextBox { Text = "Opsis Custom", Dock = DockStyle.Top, Margin = new Padding(12) };
                var ok = new Button { Text = LocalizationManager.T("Common.OK"), DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
                dialog.Controls.Add(box);
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;
                dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text))
                {
                    ThemePackageEngine.SaveCurrentAsTheme(box.Text.Trim());
                    RefreshThemeList();
                    _statusLabel.Text = LocalizationManager.T("Personalization.ThemeSavedStatus");
                }
            }),
            // Έτοιμο, "τεχνολογικό" πακέτο θέματος — ζητήθηκε ρητά ένα νέο .theme με tech-themed
            // wallpaper. Χρησιμοποιεί ΤΟ ΙΔΙΟ οπτικό μοτίβο (circuit-grid) με το νέο ζωντανό
            // στυλ "TechGrid" του Wallpaper Studio, ως στατική εικόνα (απαίτηση του .theme format).
            (LocalizationManager.T("Personalization.CreateTechGridTheme"), (_, _) =>
            {
                string png = ThemePackageEngine.GenerateTechWallpaperPng();
                ThemePackageEngine.SaveCurrentAsTheme("Opsis Tech Grid", png);
                RefreshThemeList();
                _statusLabel.Text = LocalizationManager.T("Personalization.TechGridThemeCreatedStatus");
            }));

            // ---------- Lock Screen Background ----------
            var lockScreenCard = AddCardSection(panel, LocalizationManager.T("Personalization.SectionLockScreen"));
            AddText(lockScreenCard, LocalizationManager.T("Personalization.LockScreenIntro"));
            AddText(lockScreenCard, string.Format(LocalizationManager.T("Personalization.LockScreenEditionNote"), LockScreenEngine.GetWindowsEdition()));

            AddButtonGrid(lockScreenCard,
            (LocalizationManager.T("Personalization.SetLockScreenImage"), async (_, _) =>
            {
                string fileName;
                using (var dlg = new OpenFileDialog { Filter = LocalizationManager.T("Filter.Images"), Title = LocalizationManager.T("Personalization.SetLockScreenImage") })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    fileName = dlg.FileName;
                }
                // Εκτός UI thread: το helper περιμένει έως 15s (UAC prompt) — στο UI thread πάγωνε όλο το
                // παράθυρο ΚΑΙ (λόγω του global mouse hook του quick-hide) το ποντίκι όλου του συστήματος.
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(fileName, out error));
                if (IsDisposed) return;
                var settings = AppSettings.Load();
                settings.LockScreenFollowsWallpaper = false; // χειροκίνητη εικόνα -> σταματά να ακολουθεί το wallpaper
                settings.Save();
                if (ok) _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenSuccess");
                else if (error == "cancelled") _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCancelled");
                else _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenFailed");
            }),
            // MD roadmap item: αντί για τυχαία επιλεγμένη εικόνα, ακριβές στιγμιότυπο του τρέχοντος
            // ζωντανού wallpaper (waves/βίντεο + overlay) ως οθόνη κλειδώματος — με "self-heal" μέσω
            // ενός Scheduled Task (βλ. LockScreenEngine.EnsureSelfHealTask) που το ξαναφαρμόζει αυτόματα
            // σε κάθε logon αν τα Windows/ένα update επαναφέρουν το registry key.
            (LocalizationManager.T("Personalization.MatchLockScreenToWallpaper"), async (_, _) =>
            {
                if (!WallpaperHostEngine.Instance.IsRunning)
                {
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenNeedsWallpaper");
                    return;
                }
                string snapshotPath = Path.Combine(AppPaths.AppDataFolder, "lockscreen_wallpaper.png");
                bool captured = await WallpaperHostEngine.Instance.CaptureSnapshotAsync(snapshotPath);
                if (IsDisposed) return;
                if (!captured)
                {
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCaptureFailed");
                    return;
                }
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(snapshotPath, out error));
                if (IsDisposed) return;
                if (ok)
                {
                    var settings = AppSettings.Load();
                    settings.LockScreenFollowsWallpaper = true;
                    settings.LockScreenSnapshotPath = snapshotPath;
                    settings.Save();
                    _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenSuccess");
                }
                else if (error == "cancelled") _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCancelled");
                else _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenFailed");
            }),
            // ΔΙΟΡΘΩΣΗ "έβαλα άλλη οθόνη κλειδώματος και δεν μπορώ να επαναφέρω την προεπιλεγμένη":
            // δεν υπήρχε καμία λειτουργία επαναφοράς - βλ. LockScreenEngine.ClearLockScreenRegistry.
            (LocalizationManager.T("Personalization.ResetLockScreenImage"), async (_, _) =>
            {
                string error = string.Empty;
                bool ok = await Task.Run(() => LockScreenEngine.TryResetLockScreenElevated(out error));
                if (IsDisposed) return;
                var settings = AppSettings.Load();
                settings.LockScreenFollowsWallpaper = false;
                settings.Save();
                if (ok) _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenResetSuccess");
                else if (error == "cancelled") _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenCancelled");
                else _statusLabel.Text = LocalizationManager.T("Personalization.LockScreenFailed");
            }));

            SetPage("Page.Personalization.Title", panel);
        }

        private void ShowSettings()
        {
            _currentPageKey = "Settings";
            var settings = new SettingsView();
            SetPage("Page.Settings.Title", settings);
        }

        public void OpenCommandPalette() => ShowCommandPalette();

        private void ShowCommandPalette()
        {
            using var dialog = new Form
            {
                Text = LocalizationManager.T("Command.PaletteTitle"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(560, 430),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);

            var box = new TextBox
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 14),
                PlaceholderText = LocalizationManager.T("Command.PalettePlaceholder"),
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 11),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 30
            };
            list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using var bg = new SolidBrush(selected ? UiTheme.SurfaceHover : UiTheme.Background);
                e.Graphics.FillRectangle(bg, e.Bounds);
                using var textBrush = new SolidBrush(selected ? UiTheme.AccentCyan : UiTheme.TextPrimary);
                e.Graphics.DrawString(list.Items[e.Index].ToString(), e.Font!, textBrush, e.Bounds.X + 10, e.Bounds.Y + 5);
            };

            // ΔΙΟΡΘΩΣΗ REQ-008: οι εντολές ΗΤΑΝ ταυτόχρονα το εμφανιζόμενο κείμενο ΚΑΙ το κλειδί
            // αντιστοίχισης στο switch — πάντα αγγλικά, ανεξάρτητα από την επιλεγμένη γλώσσα.
            // Τώρα κάθε εντολή έχει ξεχωριστό σταθερό "id" (για το switch) από το μεταφρασμένο
            // label που βλέπει ο χρήστης (για εμφάνιση/αναζήτηση), μέσω ενός dictionary label->id.
            string profileFmt = LocalizationManager.T("Dashboard.ProfileButtonFormat");
            var commandDefs = new (string Id, string Label)[]
            {
                ("Dashboard", LocalizationManager.T("Nav.Dashboard")),
                ("WidgetGallery", LocalizationManager.T("Command.WidgetGallery")),
                ("DeskZones", LocalizationManager.T("Nav.DeskZones")),
                ("WallpaperStudio", LocalizationManager.T("Nav.Wallpaper")),
                ("Profiles", LocalizationManager.T("Nav.Profiles")),
                ("Performance", LocalizationManager.T("Nav.Performance")),
                ("Automation", LocalizationManager.T("Nav.Automation")),
                ("Personalization", LocalizationManager.T("Nav.Personalization")),
                ("Settings", LocalizationManager.T("Nav.Settings")),
                ("About", LocalizationManager.T("Nav.About")),
                ("AudioEnhancement", LocalizationManager.T("Nav.AudioEnhancement")),
                ("EnableWallpaper", LocalizationManager.T("Dashboard.QuickEnableWallpaper")),
                ("DisableWallpaper", LocalizationManager.T("Command.DisableWallpaper")),
                ("WorkProfile", string.Format(profileFmt, "Work")),
                ("GamingProfile", string.Format(profileFmt, "Gaming")),
                ("FocusProfile", string.Format(profileFmt, "Focus")),
            };
            // Οι σελίδες των plugin (π.χ. News Crawler) εμφανίζονται κι εδώ.
            var commandList = commandDefs.ToList();
            foreach (var pluginPage in Services.PluginHost.Instance.GetNavigationItems())
                commandList.Add(("plugin:" + pluginPage.Key, Services.PluginHost.TitleOf(pluginPage)));
            // v2.0.0 (t35, "Start Menu replacement" σε ασφαλή μορφή): το Ctrl+K γίνεται και app
            // launcher — ΧΩΡΙΣ να πειράζει το πραγματικό Start. Πρώτα οι καρφιτσωμένες της taskbar,
            // μετά οι συντομεύσεις του Start Menu· Enter τις εκκινεί (όπως ένα διπλό-κλικ σε .lnk).
            var appByLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                };
                var lnks = TaskbarPinsReader.GetPinnedAppPaths().Concat(roots.Where(Directory.Exists).SelectMany(r => Directory.EnumerateFiles(r, "*.lnk", SearchOption.AllDirectories)));
                foreach (var lnk in lnks)
                {
                    string name = Path.GetFileNameWithoutExtension(lnk);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || commandList.Any(c => c.Label.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    appByLabel.TryAdd(name, lnk);
                }
            }
            catch (Exception) { }
            foreach (var appLabel in appByLabel.Keys) commandList.Add(("app:" + appLabel, appLabel));
            var idByLabel = commandList.GroupBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            var allLabels = commandList.Select(c => c.Label).ToArray();
            list.Items.AddRange(allLabels);
            if (list.Items.Count > 0) list.SelectedIndex = 0;

            void Execute()
            {
                if (list.SelectedItem is not string label || !idByLabel.TryGetValue(label, out var id)) return;
                switch (id) {
                    case "Dashboard": NavigateTo("Dashboard"); break; case "WidgetGallery": NavigateTo("Widgets"); break; case "DeskZones": NavigateTo("DeskZones"); break;
                    case "WallpaperStudio": NavigateTo("Wallpaper"); break; case "Profiles": NavigateTo("Profiles"); break;
                    case "Performance": NavigateTo("Performance"); break; case "Automation": NavigateTo("Automation"); break;
                    case "Personalization": NavigateTo("Personalization"); break; case "Settings": NavigateTo("Settings"); break;
                    case "About": NavigateTo("About"); break;
                    case "AudioEnhancement": NavigateTo("AudioEnhancement"); break;
                    case string pluginId when pluginId.StartsWith("plugin:", StringComparison.Ordinal): NavigateTo(pluginId); break;
                    case string appId when appId.StartsWith("app:", StringComparison.Ordinal):
                        if (appByLabel.TryGetValue(appId[4..], out var appPath) && MotionDesk.Services.SafeLaunch.IsAllowed(appPath))
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appPath) { UseShellExecute = true }); } catch (Exception) { }
                        break;
                    case "EnableWallpaper": WallpaperHostEngine.Instance.Enable(); break;
                    case "DisableWallpaper": WallpaperHostEngine.Instance.Disable(); break; case "WorkProfile": LoadProfileFromQuickButton("Work"); break;
                    case "GamingProfile": LoadProfileFromQuickButton("Gaming"); break; case "FocusProfile": LoadProfileFromQuickButton("Focus"); break;
                }
                dialog.Close();
            }
            box.TextChanged += (_, _) =>
            {
                list.Items.Clear();
                list.Items.AddRange(allLabels.Where(c => c.Contains(box.Text, StringComparison.OrdinalIgnoreCase)).ToArray());
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            };
            list.DoubleClick += (_, _) => Execute();
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Execute(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.Items.Count - 1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) dialog.Close();
            };
            dialog.Controls.Add(list); dialog.Controls.Add(box); box.Focus(); dialog.ShowDialog(this);
        }

        private void ShowProfiles()
        {
            _currentPageKey = "Profiles";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Profiles.Intro"));
            var list = new ListBox { Width = 440, Height = 180, Font = new Font("Segoe UI", 10) };
            void Refresh() { list.Items.Clear(); foreach (var p in WorkspaceProfileService.ListProfiles()) list.Items.Add(p); }
            Refresh(); panel.Controls.Add(list);
            var name = new TextBox { Width = 240, Text = "Work" }; panel.Controls.Add(name);
            AddIconButtonGrid(panel,
                ("Save", LocalizationManager.T("Profiles.SaveCurrentAsProfile"), (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { WorkspaceProfileService.Save(name.Text.Trim()); Refresh(); } }),
                ("Restore", LocalizationManager.T("Profiles.LoadSelected"), (_, _) => { if (list.SelectedItem is string p && WorkspaceProfileService.Load(p)) { NavigateTo("Dashboard"); } }),
                ("Add", LocalizationManager.T("Profiles.CreateUpdateDefaults"), (_, _) => { foreach (var p in new[] { "Work", "Gaming", "Focus" }) { if (!WorkspaceProfileService.ListProfiles().Contains(p, StringComparer.OrdinalIgnoreCase)) WorkspaceProfileService.Save(p); } Refresh(); }),
                ("Delete", LocalizationManager.T("Profiles.DeleteSelected"), (_, _) => { if (list.SelectedItem is string p && !p.Equals("Last Session", StringComparison.OrdinalIgnoreCase)) { WorkspaceProfileService.Delete(p); Refresh(); } }));
            SetPage("Page.Profiles.Title", panel);
        }

        // Ενοποιημένη καρτέλα: πρώην ξεχωριστά "System Monitor" + "Performance Center" (το
        // δεύτερο ήταν σχεδόν πανομοιότυπο με το πρώτο) -> τώρα ΜΙΑ καρτέλα, με το ίδιο live
        // hardware περιεχόμενο, κάτω από τον τίτλο/εικονίδιο "Απόδοση".
        private void ShowPerformance()
        {
            _currentPageKey = "Performance";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Performance.Intro"));
            var cpu = AddMeter(panel, LocalizationManager.T("Performance.CpuLabel"), 0);
            var ram = AddMeter(panel, LocalizationManager.T("Performance.MemoryLabel"), 0);
            // GPU (φόρτος) — ζητήθηκε ρητά "βελτιώσεις για ram cpu gpu usage": το Performance page
            // έδειχνε μόνο CPU/RAM, ενώ το System Monitor widget έχει ήδη GPU load/temp μέσω
            // GpuMonitorService. Ίδιο μοτίβο "ζέσταμα σε background thread" με το widget (βλ.
            // WidgetEngine.InitializeNativeSystemMonitor) ώστε να ΜΗΝ ξαναεισάγουμε το ίδιο
            // UI-thread hitch που διορθώθηκε εκεί.
            var gpu = AddMeter(panel, LocalizationManager.T("Performance.GpuLabel"), 0);
            gpu.SetValue(0, LocalizationManager.T("Performance.GpuLoading"));
            bool gpuReady = false;
            _ = System.Threading.Tasks.Task.Run(() => { GpuMonitorService.Instance.GetSnapshot(); gpuReady = true; });
            // ΔΙΟΡΘΩΣΗ "όλα τα πλαίσια εδώ να είναι ίδιου μήκους": το SparklineCard είχε Width=500
            // ενώ τα CPU/Μνήμη/GPU (AddMeter) και η κάρτα Ενέργειας (AddCard) είναι όλα 760 -
            // ίδιο πλάτος με τα υπόλοιπα, συνεπές με όλη τη σελίδα.
            var net = new SparklineCard(LocalizationManager.T("Performance.NetworkLabel"), 2, 1024) { Width = 760, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(net);
            var procs = new SparklineCard(LocalizationManager.T("Performance.ProcessesLabel"), 1, 400) { Width = 760, Height = 110, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(procs);
            var powerCard = AddCard(panel, LocalizationManager.T("Performance.PowerLabel"), "");

            // 1.7.8: οι κάρτες ιστορικού "CPU & RAM"/"GPU" στο τέλος της σελίδας αφαιρέθηκαν (ζητήθηκε ρητά) - το ποσοστό RAM
            // φαίνεται πλέον μέσα στη γραμμή "Μνήμη" επάνω, τα CPU/GPU στις δικές τους γραμμές.

            // top 5 memory users (refreshed every ~6 s on a background thread)
            AddSection(panel, LocalizationManager.T("Performance.TopMemory"));
            var topLabel = new Label { Text = "…", AutoSize = true, MaximumSize = new Size(740, 0), Font = new Font("Consolas", 10f), ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 10) };
            panel.Controls.Add(topLabel);
            int topTick = 0; bool topBusy = false;

            // notification when CPU/RAM stay above 90% for a minute (works while the app lives in the tray)
            var alertsCheck = new FlatCheckBox { Text = LocalizationManager.T("Performance.AlertsEnable"), AutoSize = true, Checked = AppSettings.Load().UsageAlertsEnabled, BackColor = UiTheme.Background, Margin = new Padding(0, 4, 0, 10) };
            alertsCheck.CheckedChanged += (_, _) => { var a = AppSettings.Load(); a.UsageAlertsEnabled = alertsCheck.Checked; a.Save(); };
            panel.Controls.Add(alertsCheck);

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (_, _) => {
                var m = AdvancedSystemMonitorService.Instance.GetSnapshot();
                if (topTick++ % 3 == 0 && !topBusy)
                {
                    topBusy = true;
                    _ = System.Threading.Tasks.Task.Run(() => TopProcessesService.TopByMemory(5)).ContinueWith(t =>
                    {
                        topBusy = false;
                        if (t.IsCompletedSuccessfully && !topLabel.IsDisposed)
                            topLabel.BeginInvoke(new Action(() =>
                            {
                                // ΔΙΟΡΘΩΣΗ "όταν ενημερώνονται τα στοιχεία το παράθυρο scrollάρει
                                // κάτω": το topLabel είναι AutoSize μέσα σε ένα AutoScroll
                                // FlowLayoutPanel — αλλαγή κειμένου (νέα top-5 διεργασίες,
                                // διαφορετικό μήκος) αλλάζει το μετρημένο μέγεθός του και προκαλεί
                                // layout pass που το WinForms AutoScroll επαναφέρει σιωπηλά στην
                                // κορυφή. Ίδια τεχνική διάσωσης/επαναφοράς θέσης με το
                                // RefreshPinnedApps (DeskStrip page), εδώ γύρω μόνο από την αλλαγή
                                // που πραγματικά προκαλεί το resize.
                                var savedScroll = panel.AutoScrollPosition;
                                topLabel.Text = string.Join("\n", t.Result.Select((x, i) => $"{i + 1}. {x.Name,-28} {(x.Mb >= 1024 ? (x.Mb / 1024).ToString("0.0") + " GB" : x.Mb.ToString("0") + " MB"),9}"));
                                panel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
                            }));
                    });
                }
                cpu.SetValue(m.CpuPercent, $"{m.CpuPercent:0.0}%");
                double ramPercent = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                ram.SetValue(ramPercent, string.Format(LocalizationManager.T("Performance.MemoryValueFormat"), $"{m.AvailableMemoryMb:0}", $"{m.TotalMemoryMb:0}", RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb)));
                if (gpuReady)
                {
                    var g = GpuMonitorService.Instance.GetSnapshot();
                    gpu.SetValue(g.Available ? g.LoadPercent ?? 0 : 0,
                        g.Available ? $"{g.LoadPercent:0.0}%" + (g.TemperatureC.HasValue ? $"   {g.TemperatureC:0}°C" : "") : LocalizationManager.T("Performance.GpuUnavailable"));
                }
                net.Push(m.NetworkDownKbps, m.NetworkUpKbps, string.Format(LocalizationManager.T("Performance.NetworkValueFormat"), $"{m.NetworkDownKbps:0.0}", $"{m.NetworkUpKbps:0.0}"));
                procs.Push(m.ProcessCount, null, string.Format(LocalizationManager.T("Performance.ProcessesValueFormat"), m.ProcessCount));
                powerCard.Text = string.Format(LocalizationManager.T("Performance.RecommendedFormat"), m.BatteryMode);
            };
            timer.Start();
            panel.Disposed += (_, _) => timer.Dispose();
            SetPage("Page.Performance.Title", panel);
        }

        private void ShowAutomation()
        {
            _currentPageKey = "Automation";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("Automation.Intro"));
            foreach (var rule in AutomationService.Instance.Rules)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                var check = new FlatCheckBox { Text = $"{rule.Name}  ·  {rule.TriggerProcess}  →  {rule.Profile}", Checked = rule.Enabled, AutoSize = true, BackColor = UiTheme.Background, Padding = new Padding(0, 4, 0, 0) };
                check.CheckedChanged += (_, _) => { rule.Enabled = check.Checked; AutomationService.Instance.Save(); };
                row.Controls.Add(check);

                var editBtn = new HoverButton { Text = LocalizationManager.T("Common.Edit"), Width = 70, Height = 26, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand, Margin = new Padding(10, 0, 0, 0) };
                editBtn.FlatAppearance.BorderSize = 0;
                editBtn.Click += (_, _) => { if (PromptAutomationRule(rule)) { AutomationService.Instance.Save(); ShowAutomation(); } };
                row.Controls.Add(editBtn);

                var deleteBtn = new HoverButton { Text = LocalizationManager.T("Common.Delete"), Width = 70, Height = 26, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(231, 76, 60), BackColor = UiTheme.Surface, Cursor = Cursors.Hand, Margin = new Padding(6, 0, 0, 0) };
                deleteBtn.FlatAppearance.BorderSize = 0;
                deleteBtn.Click += (_, _) => { AutomationService.Instance.Rules.Remove(rule); AutomationService.Instance.Save(); ShowAutomation(); };
                row.Controls.Add(deleteBtn);

                panel.Controls.Add(row);
            }

            var addRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            addRow.Controls.Add(NewHoverButton(LocalizationManager.T("Automation.AddRule"), () =>
            {
                var newRule = new AutomationRule { Name = LocalizationManager.T("Automation.NewRuleDefaultName"), TriggerProcess = "", Profile = "" };
                if (PromptAutomationRule(newRule)) { AutomationService.Instance.Rules.Add(newRule); AutomationService.Instance.Save(); ShowAutomation(); }
            }));
            addRow.Controls.Add(NewHoverButton(LocalizationManager.T("Automation.AddGamingRule"), () =>
            {
                AutomationService.Instance.Rules.Add(new AutomationRule { Name = LocalizationManager.T("Automation.GamingRuleName"), TriggerProcess = "steam.exe", Profile = "Gaming" });
                AutomationService.Instance.Save();
                ShowAutomation();
            }));
            panel.Controls.Add(addRow);

            SetPage("Page.Automation.Title", panel);
        }

        private static HoverButton NewHoverButton(string text, Action onClick)
        {
            var btn = new HoverButton { Text = text, Width = 140, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (_, _) => onClick();
            return btn;
        }

        // Ζητήθηκε ρητά "εκτός από Add Gaming role να μπορώ να τον παραμετροποιώ & διαγράφω" —
        // ίδιο πρότυπο διαλόγου με το RenameContainer/PromptWeatherLocation, χρησιμοποιείται και
        // για Add (νέος, άδειος κανόνας) και για Edit (υπάρχων κανόνας) στο ίδιο dialog.
        private bool PromptAutomationRule(AutomationRule rule)
        {
            using var dialog = new Form
            {
                Text = LocalizationManager.T("Automation.RuleDialogTitle"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(420, 230),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                BackColor = UiTheme.Surface
            };
            var nameLabel = new Label { Text = LocalizationManager.T("Common.NameLabel"), Location = new Point(14, 14), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var nameBox = new TextBox { Location = new Point(14, 34), Width = 380, Text = rule.Name };
            var procLabel = new Label { Text = LocalizationManager.T("Automation.TriggerProcessLabel"), Location = new Point(14, 66), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var procBox = new TextBox { Location = new Point(14, 86), Width = 380, Text = rule.TriggerProcess };
            var profileLabel = new Label { Text = LocalizationManager.T("Automation.ProfileToLoadLabel"), Location = new Point(14, 118), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var profileBox = new TextBox { Location = new Point(14, 138), Width = 380, Text = rule.Profile };
            var okBtn = new Button { Text = LocalizationManager.T("Common.OK"), Location = new Point(228, 170), DialogResult = DialogResult.OK };
            var cancelBtn = new Button { Text = LocalizationManager.T("Common.Cancel"), Location = new Point(316, 170), DialogResult = DialogResult.Cancel };
            dialog.Controls.Add(nameLabel); dialog.Controls.Add(nameBox);
            dialog.Controls.Add(procLabel); dialog.Controls.Add(procBox);
            dialog.Controls.Add(profileLabel); dialog.Controls.Add(profileBox);
            dialog.Controls.Add(okBtn); dialog.Controls.Add(cancelBtn);
            dialog.AcceptButton = okBtn; dialog.CancelButton = cancelBtn;
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(nameBox.Text)) return false;
            rule.Name = nameBox.Text.Trim();
            rule.TriggerProcess = procBox.Text.Trim();
            rule.Profile = profileBox.Text.Trim();
            return true;
        }

        private bool _gamingModeActive;
        private void ToggleGamingMode() => LoadProfileFromQuickButton(_gamingModeActive ? "Work" : "Gaming", true);
        private void LoadProfileFromQuickButton(string profile, bool toggle = false)
        {
            if (WorkspaceProfileService.Load(profile)) { if (toggle) _gamingModeActive = !_gamingModeActive; _statusLabel.Text = string.Format(LocalizationManager.T("Automation.ProfileLoadedStatus"), profile); }
            else _statusLabel.Text = string.Format(LocalizationManager.T("Automation.ProfileNotFoundStatus"), profile);
        }

        // Ιστορικό εκδόσεων — απλή, in-code λίστα· ανοίγει σε ξεχωριστό (δευτερεύον) παράθυρο.
        // Οι σημειώσεις κάθε έκδοσης ζουν στα locale αρχεία (κλειδί "VersionNotes.<έκδοση>") ώστε να
        // ακολουθούν την επιλεγμένη γλώσσα — πριν ήταν hardcoded και μισές ελληνικά / μισές αγγλικά.
        // ΥΠΕΝΘΥΜΙΣΗ για κάθε μελλοντικό version bump: αυτός ο πίνακας ΔΕΝ ενημερώνεται αυτόματα από
        // το VersionNotes.* κλειδί — ξεχάστηκε 3 φορές στη σειρά (1.9.1/1.9.2/1.9.3), ο χρήστης το
        // έπιασε ζωντανά ("το ιστορικό μένει στην 1.9.0"). Πρόσθεσε ΚΑΙ εδώ τη νέα έκδοση.
        private static readonly (string Version, string Date)[] VersionHistory =
        {
            ("2.2.0", "2026-10"), ("2.1.3", "2026-10"), ("2.1.2", "2026-10"), ("2.1.1", "2026-10"), ("2.1.0", "2026-10"), ("2.0.0", "2026-10"), ("1.9.12", "2026-10"), ("1.9.11", "2026-10"), ("1.9.10", "2026-10"), ("1.9.9", "2026-10"), ("1.9.8", "2026-10"), ("1.9.7", "2026-10"), ("1.9.6", "2026-10"), ("1.9.5", "2026-10"), ("1.9.4", "2026-10"), ("1.9.3", "2026-10"), ("1.9.2", "2026-10"), ("1.9.1", "2026-10"), ("1.9.0", "2026-10"), ("1.8.9", "2026-10"), ("1.8.8", "2026-10"), ("1.8.7", "2026-10"), ("1.8.6", "2026-10"), ("1.8.5", "2026-10"), ("1.7.8", "2026-10"), ("1.7.6", "2026-10"), ("1.7.5", "2026-10"), ("1.6.8", "2026-10"), ("1.6.2", "2026-10"), ("1.6.1", "2026-10"), ("1.6.0", "2026-09"), ("1.5.0", "2026-09"), ("1.4.1", "2026-09"), ("1.4.0", "2026-09"), ("1.3.0", "2026-09"), ("1.2.9", "2026-09"), ("1.2.8", "2026-09"), ("1.2.7", "2026-09"), ("1.2.6", "2026-09"), ("1.2.5", "2026-09"), ("1.2.4", "2026-09"), ("1.2.3", "2026-09"), ("1.2.2", "2026-09"), ("1.2.1", "2026-09"), ("1.2.0", "2026-09"), ("1.1.2", "2026-09"), ("1.1.1", "2026-09"), ("1.1.0", "2026-09"), ("1.0.0", "2026-09")
        };


        // "Audio Enhancement" — ζητήθηκε ρητά, εμπνευσμένο από τη ΛΟΓΙΚΗ του FXSound (github.com/
        // fxsound2/fxsound-app), όχι αντιγραφή του κώδικά του. Έρευνα στο ίδιο το repo (WebFetch)
        // επιβεβαίωσε ότι ΑΚΟΜΑ ΚΙ ΕΚΕΙ η πραγματική system-wide επεξεργασία γίνεται μέσω
        // ξεχωριστού, ΚΛΕΙΣΤΟΥ virtual audio driver — δεν είναι καν μέρος του δικού τους open-
        // source κώδικα, χρειάζεται kernel-mode driver signing (πέρα από απλό code-signing cert).
        // Η ΠΡΑΓΜΑΤΙΚΗ, εφικτή λύση βρέθηκε ερευνώντας παρόμοια GitHub projects (π.χ.
        // github.com/psidex/EACS): το Equalizer APO (equalizerapo.sourceforge.io) είναι ένα ήδη
        // δωρεάν, ανοιχτού κώδικα, ήδη-υπογεγραμμένο, system-wide Windows Audio Processing Object
        // — ελέγχεται προγραμματιστικά γράφοντας το δικό του config.txt (EqualizerApoService, ίδιο
        // μοτίβο ενσωμάτωσης εξωτερικού εργαλείου με FFmpeg/7-Zip σε αυτό το project). Όταν είναι
        // εγκατεστημένο, τα presets ΕΔΩ αλλάζουν πραγματικά τον ήχο ΟΛΩΝ των εφαρμογών· χωρίς αυτό,
        // εξακολουθούν έστω να ενισχύουν πραγματικά τον live visualizer (WASAPI loopback).
        private AudioSpectrumService? _enhancementSpectrum;
        private void ShowAudioEnhancement()
        {
            _currentPageKey = "AudioEnhancement";
            var panel = CreatePagePanel();
            AddText(panel, LocalizationManager.T("AudioEnhancement.Intro"));

            // ΔΙΟΡΘΩΣΗ πραγματικού bug: Dock=Fill (κείμενο) + Dock=Right (κουμπί) στον ίδιο γονέα
            // δεν μείωνε σωστά το πλάτος του Fill label — το κουμπί απλά ζωγραφιζόταν ΠΑΝΩ από το
            // κείμενο (BringToFront), όχι δίπλα του, αφού το layout δεν αφαιρούσε ποτέ πραγματικά
            // τον χώρο του κουμπιού. Λύση: κάθετη στοίβα (FlowLayoutPanel, TopDown) — κείμενο πάνω,
            // κουμπί από κάτω, ΠΟΤΕ στο ίδιο ύψος, άρα αδύνατο να επικαλυφθούν.
            bool apoInstalled = EqualizerApoService.IsInstalled;
            var noteCard = new Panel { Width = 720, BackColor = UiTheme.Surface, Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0, 0, 0, 10) };
            UiTheme.ApplyRoundedRegion(noteCard, 8);
            var noteStack = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent };
            var statusLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Text = apoInstalled
                    ? "✓ " + LocalizationManager.T("AudioEnhancement.ApoFound")
                    : LocalizationManager.T("AudioEnhancement.ApoMissing"),
                ForeColor = apoInstalled ? Color.FromArgb(60, 210, 140) : UiTheme.TextSecondary,
                Font = UiTheme.FontBody
            };
            noteStack.Controls.Add(statusLabel);
            if (!apoInstalled)
            {
                var installBtn = new HoverButton { Text = LocalizationManager.T("AudioEnhancement.ApoInstallBtn"), Width = 190, Height = 32, Margin = new Padding(0, 8, 0, 0), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.AccentBlue, BaseColor = UiTheme.AccentBlue, HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f), Cursor = Cursors.Hand };
                installBtn.FlatAppearance.BorderSize = 0;
                installBtn.Click += (_, _) => EqualizerApoService.OpenDownloadPage();
                noteStack.Controls.Add(installBtn);
            }
            noteCard.Controls.Add(noteStack);
            noteCard.Height = noteStack.PreferredSize.Height + noteCard.Padding.Vertical;
            panel.Controls.Add(noteCard);

            AddSection(panel, LocalizationManager.T("AudioEnhancement.SectionPresets"));
            var settings = AppSettings.Load();
            Dictionary<string, HoverButton>? presetButtons = null;
            var presetItems = AudioSpectrumService.Presets.Select(p =>
                (p.Name, (EventHandler)((_, _) =>
                {
                    var a = AppSettings.Load(); a.AudioEnhancementPreset = p.Name; a.Save();
                    _enhancementSpectrum?.ApplyPreset(p.Name);
                    MarkActive(presetButtons!, p.Name);
                    if (apoInstalled)
                    {
                        var (ok, msg) = EqualizerApoService.ApplyPreset(p.Name);
                        _statusLabel.Text = ok ? string.Format(LocalizationManager.T("Status.AudioPresetSystemWideFormat"), p.Name) : $"Equalizer APO: {msg}";
                    }
                }))).ToArray();
            presetButtons = AddToggleButtonGrid(panel, presetItems);
            MarkActive(presetButtons, settings.AudioEnhancementPreset);
            AddText(panel, LocalizationManager.T("AudioEnhancement.PresetsNote"));

            BuildEqStudioSection(panel, apoInstalled);

            AddSection(panel, LocalizationManager.T("AudioEnhancement.SectionVisualizer"));
            var equalizer = new EqualizerControl { Width = 700, Height = 200, Style = "WMP", Margin = new Padding(0, 0, 0, 8) };
            panel.Controls.Add(equalizer);

            var styleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var wmpBtn = new HoverButton { Text = "WMP Legacy", Width = 130, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            var winampBtn = new HoverButton { Text = "Winamp", Width = 130, Height = 30, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
            wmpBtn.FlatAppearance.BorderSize = 0; winampBtn.FlatAppearance.BorderSize = 0;
            wmpBtn.Click += (_, _) => { equalizer.Style = "WMP"; wmpBtn.BackColor = wmpBtn.BaseColor = UiTheme.SurfaceHover; winampBtn.BackColor = winampBtn.BaseColor = UiTheme.Surface; };
            winampBtn.Click += (_, _) => { equalizer.Style = "Winamp"; winampBtn.BackColor = winampBtn.BaseColor = UiTheme.SurfaceHover; wmpBtn.BackColor = wmpBtn.BaseColor = UiTheme.Surface; };
            styleRow.Controls.Add(wmpBtn);
            styleRow.Controls.Add(winampBtn);
            panel.Controls.Add(styleRow);

            _enhancementSpectrum?.Dispose();
            var spectrum = new AudioSpectrumService(20);
            _enhancementSpectrum = spectrum;
            spectrum.ApplyPreset(settings.AudioEnhancementPreset);
            var timer = new System.Windows.Forms.Timer { Interval = 40 };
            // Το closure πιάνει το δικό του "spectrum" local (όχι το κοινόχρηστο πεδίο
            // _enhancementSpectrum) ώστε ΚΑΘΕ επίσκεψη στη σελίδα να έχει το δικό της, απομονωμένο
            // instance — ακόμη κι αν ο Timer μιας παλιότερης επίσκεψης καθυστερήσει να σταματήσει.
            timer.Tick += (_, _) => { if (!equalizer.IsDisposed) equalizer.PushBands(spectrum.GetBands()); };
            timer.Start();
            panel.Disposed += (_, _) =>
            {
                timer.Stop(); timer.Dispose(); spectrum.Dispose();
                if (ReferenceEquals(_enhancementSpectrum, spectrum)) _enhancementSpectrum = null;
            };

            SetPage("AudioEnhancement.Title", panel);
        }


        // ---- Ισοσταθμιστής 10 ζωνών ΣΕ ΟΛΟ ΤΟ ΣΥΣΤΗΜΑ (Equalizer APO) + ανταλλαγή presets με το Waveframe ----
        private void BuildEqStudioSection(FlowLayoutPanel panel, bool apoInstalled)
        {
            string Ld(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            AddSection(panel, Ld("Ισοσταθμιστής 10 ζωνών (σε όλο το σύστημα)", "10-band equalizer (system-wide)"));
            AddText(panel, Ld(
                "Σύρε τις ζώνες (διπλό κλικ = μηδέν). Εφαρμόζεται σε ΟΛΟΝ τον ήχο των Windows μέσω Equalizer APO. Μπορείς να εισάγεις presets από το Waveframe (αρχείο .txt ή .json) και να εξάγεις τα δικά σου.",
                "Drag the bands (double-click = zero). Applied to ALL Windows audio through Equalizer APO. You can import presets from Waveframe (.txt or .json) and export your own."));

            var settings = AppSettings.Load();
            var eq = new EqBandsControl { Width = 700, Height = 220, Margin = new Padding(0, 0, 0, 8) };
            var current = new AudioEqProfile { Name = Ld("Τρέχον", "Current") };
            var saved = settings.CustomEqProfiles.FirstOrDefault(p => p.Name == settings.ActiveEqProfile);
            if (saved != null) current = saved.Clone();
            eq.Bands = current.Bands;
            panel.Controls.Add(eq);

            var applyTimer = new System.Windows.Forms.Timer { Interval = 400 };
            void ApplyNow()
            {
                applyTimer.Stop();
                current.Bands = eq.Bands;
                if (!apoInstalled) { _statusLabel.Text = LocalizationManager.T("AudioEnhancement.ApoMissing"); return; }
                var (ok, msg) = EqualizerApoService.ApplyProfile(current);
                _statusLabel.Text = ok ? Ld($"Ισοσταθμιστής εφαρμόστηκε σε όλο το σύστημα: {current.Name}", $"Equalizer applied system-wide: {current.Name}") : $"Equalizer APO: {msg}";
            }
            applyTimer.Tick += (_, _) => ApplyNow();
            eq.BandsChanged += (_, _) => { applyTimer.Stop(); applyTimer.Start(); };   // debounce: ένα αρχείο ανά "σταμάτημα" του σύρματος
            panel.Disposed += (_, _) => { applyTimer.Stop(); applyTimer.Dispose(); };

            HoverButton Btn(string text, int width, EventHandler click)
            {
                var b = new HoverButton { Text = text, Width = width, Height = 32, Margin = new Padding(0, 0, 8, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                b.FlatAppearance.BorderSize = 0; b.Click += click; return b;
            }

            // Presets: τα 16 του Waveframe + τα δικά σου
            var combo = new FlatComboBox { Width = 260, Margin = new Padding(0, 0, 8, 6) };
            List<(string Label, AudioEqProfile Profile)> entries = new();
            void FillCombo(string? select)
            {
                entries = AudioEqProfile.BuiltIn.Select(b => (Ld(b.El, b.En), new AudioEqProfile { Name = Ld(b.El, b.En), Bands = (double[])b.Bands.Clone() })).ToList();
                foreach (var c in AppSettings.Load().CustomEqProfiles) entries.Add(("★ " + c.Name, c.Clone()));
                combo.SetItems(entries.Select(x => x.Label).ToArray(), select != null && entries.Any(x => x.Label == select) ? select : null);
            }
            FillCombo(null);
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0 || combo.SelectedIndex >= entries.Count) return;
                current = entries[combo.SelectedIndex].Profile.Clone();
                eq.Bands = current.Bands;
                var a2 = AppSettings.Load(); a2.ActiveEqProfile = current.Name; a2.Save();
                ApplyNow();
            };

            var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row1.Controls.Add(combo);
            row1.Controls.Add(Btn(Ld("Μηδενισμός", "Reset to flat"), 120, (_, _) => { current = new AudioEqProfile { Name = Ld("Επίπεδο", "Flat") }; eq.Bands = current.Bands; ApplyNow(); }));
            row1.Controls.Add(Btn(Ld("Αποθήκευση ως…", "Save as…"), 130, (_, _) =>
            {
                string? name = PromptForText(Ld("Όνομα preset", "Preset name"), current.Name.Length > 0 ? current.Name : Ld("Το preset μου", "My preset"));
                if (string.IsNullOrWhiteSpace(name)) return;
                var st = AppSettings.Load();
                var p = current.Clone(); p.Name = name.Trim(); p.Bands = eq.Bands;
                st.CustomEqProfiles.RemoveAll(x => x.Name == p.Name); st.CustomEqProfiles.Add(p); st.ActiveEqProfile = p.Name; st.Save();
                current = p; FillCombo("★ " + p.Name);
            }));
            row1.Controls.Add(Btn(Ld("Διαγραφή", "Delete"), 100, (_, _) =>
            {
                var st = AppSettings.Load();
                if (st.CustomEqProfiles.RemoveAll(x => x.Name == current.Name) > 0) { st.Save(); FillCombo(null); }
            }));
            panel.Controls.Add(row1);

            var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            row2.Controls.Add(Btn(Ld("Εισαγωγή (Waveframe / Equalizer APO)…", "Import (Waveframe / Equalizer APO)…"), 290, (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "Waveframe / Equalizer APO (*.txt;*.json)|*.txt;*.json|*.*|*.*", Title = Ld("Εισαγωγή EQ", "Import EQ") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(dlg.FileName);
                    if (info.Length > 256 * 1024) { _statusLabel.Text = Ld("Το αρχείο είναι πολύ μεγάλο για preset EQ.", "The file is too large to be an EQ preset."); return; }
                    var imported = AudioEqProfile.Import(File.ReadAllText(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName));
                    if (imported == null) { _statusLabel.Text = Ld("Δεν βρέθηκαν φίλτρα EQ σε αυτό το αρχείο.", "No EQ filters found in this file."); return; }
                    current = imported; eq.Bands = current.Bands;
                    _statusLabel.Text = Ld($"Εισήχθη: {current.Name}" + (current.ExtraLines.Count > 0 ? $" (+{current.ExtraLines.Count} επιπλέον φίλτρα)" : ""), $"Imported: {current.Name}" + (current.ExtraLines.Count > 0 ? $" (+{current.ExtraLines.Count} extra filters)" : ""));
                    ApplyNow();
                }
                catch (Exception ex) { _statusLabel.Text = ex.Message; }
            }));
            row2.Controls.Add(Btn(Ld("Εξαγωγή (.txt)…", "Export (.txt)…"), 150, (_, _) =>
            {
                using var dlg = new SaveFileDialog { Filter = "Equalizer APO (*.txt)|*.txt", FileName = "opsis-eq.txt", Title = Ld("Εξαγωγή EQ", "Export EQ") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { current.Bands = eq.Bands; File.WriteAllText(dlg.FileName, current.ToEqualizerApo()); _statusLabel.Text = Ld("Αποθηκεύτηκε.", "Saved."); }
                catch (Exception ex) { _statusLabel.Text = ex.Message; }
            }));
            panel.Controls.Add(row2);
        }

        private string? PromptForText(string prompt, string initial)
        {
            using var f = new Form { Text = "Opsis", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, Width = 380, Height = 160, MinimizeBox = false, MaximizeBox = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
            var l = new Label { Text = prompt, Left = 12, Top = 12, AutoSize = true };
            var t = new TextBox { Left = 12, Top = 36, Width = 340, Text = initial };
            var ok = new Button { Text = "OK", Left = 196, Top = 72, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = LocalizationManager.T("Common.Cancel"), Left = 277, Top = 72, Width = 75, DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[] { l, t, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }


        // Παράθυρο επιλογής: ποια εικονίδια ΜΕΝΟΥΝ όταν το διπλό κλικ κρύβει την επιφάνεια εργασίας.
        private void ShowKeepIconsDialog()
        {
            string Ld(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;
            var settings = AppSettings.Load();
            using var dialog = new Form
            {
                Text = Ld("Εικονίδια που μένουν ορατά", "Icons that stay visible"),
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(460, 470), FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary
            };
            var intro = new Label { Text = Ld("Τσέκαρε ό,τι θέλεις να ΜΕΝΕΙ ορατό όταν κάνεις διπλό κλικ σε κενό σημείο της επιφάνειας εργασίας. Τα υπόλοιπα κρύβονται (και ξαναφαίνονται με νέο διπλό κλικ).",
                "Tick what should STAY visible when you double-click an empty spot of the desktop. Everything else is hidden (and comes back with another double-click)."),
                Left = 12, Top = 10, Width = 436, Height = 52, ForeColor = UiTheme.TextSecondary };
            var list = new CheckedListBox { Left = 12, Top = 66, Width = 436, Height = 340, CheckOnClick = true, BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };

            var special = new (string Label, Guid Id, Func<bool> Get, Action<bool> Set)[]
            {
                (Ld("Αυτός ο υπολογιστής", "This PC"), DesktopIconVisibilityEngine.ThisPcId, () => settings.QuickHideKeepThisPc, v => settings.QuickHideKeepThisPc = v),
                (Ld("Φάκελος χρήστη", "User folder"), DesktopIconVisibilityEngine.ProfileId, () => settings.QuickHideKeepUserFolder, v => settings.QuickHideKeepUserFolder = v),
                (Ld("Πίνακας Ελέγχου", "Control Panel"), DesktopIconVisibilityEngine.ControlPanelId, () => settings.QuickHideKeepControlPanel, v => settings.QuickHideKeepControlPanel = v),
                (Ld("Κάδος Ανακύκλωσης", "Recycle Bin"), DesktopIconVisibilityEngine.RecycleBinId, () => settings.QuickHideKeepRecycleBin, v => settings.QuickHideKeepRecycleBin = v),
            };
            var specialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sp in special)
            {
                string shown = DesktopIconVisibilityEngine.DisplayNameOf(sp.Id) ?? sp.Label;
                specialNames.Add(shown); specialNames.Add(sp.Label);
                list.Items.Add(shown + "   ★", sp.Get());
            }
            foreach (var extra in DesktopIconVisibilityEngine.ListDesktopItemNames())
            {
                if (specialNames.Contains(extra) || DesktopIconVisibilityEngine.ThisPcFallback.Contains(extra, StringComparer.OrdinalIgnoreCase)
                    || DesktopIconVisibilityEngine.ControlPanelFallback.Contains(extra, StringComparer.OrdinalIgnoreCase)
                    || DesktopIconVisibilityEngine.RecycleBinFallback.Contains(extra, StringComparer.OrdinalIgnoreCase) || string.Equals(extra, Environment.UserName, StringComparison.OrdinalIgnoreCase)) continue;
                list.Items.Add(extra, settings.QuickHideExtraKept.Contains(extra, StringComparer.OrdinalIgnoreCase));
            }
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 262, Top = 424, Width = 90, Height = 32 };
            var cancel = new Button { Text = LocalizationManager.T("Common.Cancel"), DialogResult = DialogResult.Cancel, Left = 358, Top = 424, Width = 90, Height = 32 };
            dialog.Controls.AddRange(new Control[] { intro, list, ok, cancel });
            dialog.AcceptButton = ok; dialog.CancelButton = cancel;
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            for (int i = 0; i < special.Length; i++) special[i].Set(list.GetItemChecked(i));
            var extras = new List<string>();
            for (int i = special.Length; i < list.Items.Count; i++) if (list.GetItemChecked(i)) extras.Add(list.Items[i]?.ToString() ?? "");
            settings.QuickHideExtraKept = extras.Where(x => x.Length > 0).ToList();
            settings.Save();
        }

        private void ShowAbout()
        {
            _currentPageKey = "About";
            var panel = CreatePagePanel();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AddText(panel, string.Format(LocalizationManager.T("About.VersionLine"), version?.ToString(3) ?? "1.0.0"));
            AddText(panel, LocalizationManager.T("About.Body"));

            // Κάρτες ανά θέμα (πρώην ξεχωριστή σελίδα "Βοήθεια") — ζητήθηκε ρητά να ενσωματωθεί
            // εδώ αφού είχαν ίδιες πληροφορίες με τη σελίδα Σχετικά (About.InstructionsList/
            // ShortcutsList παρακάτω ήδη καλύπτουν τα ίδια, αναλυτικά, με βήματα).
            Panel BuildTopicCard(string titleKey, string bodyKey)
            {
                string body = LocalizationManager.T(bodyKey);
                var font = UiTheme.FontBody;
                var bodySize = TextRenderer.MeasureText(body, font, new Size(650, int.MaxValue), TextFormatFlags.WordBreak);
                var card = new Panel { Width = 700, Height = 34 + bodySize.Height + 20, BackColor = UiTheme.Surface, Padding = new Padding(16, 12, 16, 12), Margin = new Padding(0, 0, 0, 10) };
                UiTheme.ApplyRoundedRegion(card, 8);
                card.Controls.Add(new Label { Text = LocalizationManager.T(titleKey), Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = UiTheme.AccentCyan, AutoSize = true, Location = new Point(16, 10) });
                card.Controls.Add(new Label { Text = body, Font = font, ForeColor = UiTheme.TextSecondary, MaximumSize = new Size(650, 0), AutoSize = true, Location = new Point(16, 34) });
                return card;
            }
            panel.Controls.Add(BuildTopicCard("Nav.Widgets", "Widgets.Intro"));
            panel.Controls.Add(BuildTopicCard("Nav.DeskZones", "DeskZones.Intro"));
            panel.Controls.Add(BuildTopicCard("DeskZones.SectionContainers", "DeskZones.ContainersIntro"));
            panel.Controls.Add(BuildTopicCard("About.DeskContainersGuideTitle", "About.DeskContainersGuide"));
            panel.Controls.Add(BuildTopicCard("Nav.Wallpaper", "Wallpaper.Intro"));
            panel.Controls.Add(BuildTopicCard("Nav.Personalization", "Personalization.Intro"));
            panel.Controls.Add(BuildTopicCard("Nav.Automation", "Automation.Intro"));

            AddSection(panel, LocalizationManager.T("About.SectionCapabilities"));
            AddText(panel, LocalizationManager.T("About.CapabilitiesList"));
            AddText(panel, string.Format(LocalizationManager.T("About.RuntimeLine"), WidgetHostEngine.Instance.GetActiveWidgets().Count));
            AddButton(panel, LocalizationManager.T("VersionHistory.Button"), (_, _) => ShowVersionHistory());

            // ---- plugins (π.χ. News Crawler): ιδιότητες, βοήθεια, οδηγίες, ιστορικό και άδεια ζουν ΕΔΩ, όχι στη σελίδα του plugin
            var pluginInfos = Services.PluginHost.Instance.GetAboutInfos();
            if (pluginInfos.Count > 0)
            {
                AddSection(panel, LocalizationManager.T("About.SectionPlugins"));
                bool greek = LocalizationManager.CurrentLanguage == "el";
                foreach (var (id, info) in pluginInfos)
                {
                    var captured = info;
                    var capturedId = id;
                    AddText(panel, $"{captured.Name}  v{captured.Version}\n{(greek ? captured.DescriptionEl : captured.DescriptionEn)}\n{captured.Author} · {captured.License}");

                    // Ζητήθηκε ρητά: ξεχωριστό κουμπί ενεργοποίησης/απενεργοποίησης ΓΙΑ ΑΥΤΟ το plugin
                    // (όχι μόνο ο γενικός διακόπτης "Φόρτωση plugins" στις Ρυθμίσεις). Αλλαγές στα
                    // στοιχεία πλοήγησης/tray χρειάζονται επανεκκίνηση· το ίδιο το plugin σταματά/ξεκινά αμέσως.
                    var enableToggle = new FlatCheckBox
                    {
                        Text = LocalizationManager.T("About.PluginEnabled"),
                        AutoSize = true,
                        Checked = Services.PluginHost.Instance.IsPluginEnabled(capturedId),
                        BackColor = UiTheme.Background,
                        Margin = new Padding(0, 0, 0, 6)
                    };
                    enableToggle.CheckedChanged += (_, _) => Services.PluginHost.Instance.SetPluginEnabled(capturedId, enableToggle.Checked);
                    panel.Controls.Add(enableToggle);

                    // Ζητήθηκε (σύγκριση με τον μηχανισμό plugin του Kombos): αφαίρεση plugin από
                    // μέσα στο Opsis, όχι μόνο ενεργό/ανενεργό. Ο φάκελος διαγράφεται αμέσως αν
                    // γίνεται (δεν θα γίνεται συνήθως — το .dll είναι ήδη φορτωμένο σε αυτή τη
                    // διεργασία), αλλιώς προγραμματίζεται καθαρή διαγραφή στην επόμενη εκκίνηση.
                    var uninstallBtn = NewHoverButton(LocalizationManager.T("About.PluginUninstall"), () =>
                    {
                        var choice = MessageBox.Show(
                            string.Format(LocalizationManager.T("About.PluginUninstallConfirmBody"), captured.Name),
                            LocalizationManager.T("About.PluginUninstallConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (choice != DialogResult.Yes) return;
                        bool removedNow = Services.PluginHost.Instance.UninstallPlugin(capturedId);
                        _statusLabel.Text = LocalizationManager.T(removedNow ? "About.PluginUninstalledNow" : "About.PluginUninstallQueued");
                        ShowAbout();
                    });
                    uninstallBtn.Margin = new Padding(0, 0, 0, 6);
                    panel.Controls.Add(uninstallBtn);

                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(740, 0), Margin = new Padding(0, 0, 0, 12) };
                    foreach (var section in captured.Sections)
                    {
                        var s = section;
                        var title = greek ? s.TitleEl : s.TitleEn;
                        row.Controls.Add(NewHoverButton(title, () => ShowTextDialog(captured.Name + " — " + title, captured.Name + " v" + captured.Version, s.Text)));
                    }
                    panel.Controls.Add(row);
                }
            }

            // ---- updates: self-update βάσει GearWin (ζητήθηκε ρητά· ήδη portαρισμένο/ζωντανά
            // δοκιμασμένο στο OpsisNewsCrawler) — κατεβάζει+εγκαθιστά ΜΟΝΟ μετά από κλικ του χρήστη
            // ΚΑΙ επιβεβαίωση, μόνο από τα releases αυτού του repo, επαληθευμένο με SHA-256.
            AddSection(panel, LocalizationManager.T("About.UpdateHeading"));
            var updateStatus = new Label { Text = string.Format(LocalizationManager.T("About.UpdateCurrentVersion"), UpdateCheckService.CurrentVersion.ToString(3)), AutoSize = true, MaximumSize = new Size(740, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 6) };
            var autoUpdate = new FlatCheckBox { Text = LocalizationManager.T("About.UpdateAuto"), AutoSize = true, Checked = AppSettings.Load().UpdateCheckEnabled, BackColor = UiTheme.Background, Margin = new Padding(0, 2, 0, 6) };
            autoUpdate.CheckedChanged += (_, _) => { var a = AppSettings.Load(); a.UpdateCheckEnabled = autoUpdate.Checked; a.Save(); };
            panel.Controls.Add(autoUpdate);

            var updateRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            UpdateCheckService.UpdateInfo? pendingUpdate = null;
            bool installingUpdate = false;

            var updateProgress = new ProgressBar { Width = 400, Height = 18, Minimum = 0, Maximum = 100, Visible = false, Margin = new Padding(0, 0, 0, 6) };

            var whatsNew = NewHoverButton(LocalizationManager.T("About.UpdateOpen"), () =>
            {
                if (pendingUpdate != null)
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pendingUpdate.PageUrl) { UseShellExecute = true }); } catch (Exception) { }
            });
            whatsNew.Visible = false;

            HoverButton? installBtn = null;
            installBtn = NewHoverButton(LocalizationManager.T("About.UpdateInstall"), async () =>
            {
                if (pendingUpdate is not { } u || installingUpdate) return;
                if (!u.CanInstallAutomatically)
                {
                    // Τίποτα να επαληθευτεί απέναντι — δεν τρέχει τίποτα αυτόματα, ανοίγει η σελίδα της έκδοσης.
                    updateStatus.Text = LocalizationManager.T("About.UpdateNoDigest");
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.PageUrl) { UseShellExecute = true }); } catch (Exception) { }
                    return;
                }
                if (MessageBox.Show(this, string.Format(LocalizationManager.T("About.UpdateConfirm"), u.Version.ToString(3)),
                        LocalizationManager.T("About.UpdateConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                installingUpdate = true;
                installBtn!.Enabled = false;
                updateProgress.Value = 0;
                updateProgress.Visible = true;
                try
                {
                    var progress = new Progress<double>(p =>
                    {
                        if (updateProgress.IsDisposed) return;
                        updateProgress.Value = Math.Clamp((int)Math.Round(p * 100), 0, 100);
                        updateStatus.Text = string.Format(LocalizationManager.T("About.UpdateDownloading"), updateProgress.Value);
                    });
                    var (path, sha256) = await UpdateInstallerService.DownloadAsync(u, progress);
                    if (updateStatus.IsDisposed) return;
                    updateStatus.Text = LocalizationManager.T("About.UpdateScheduled");
                    UpdateInstallerService.LaunchAfterExit(path, sha256);
                    MotionDesk.Widgets.TrayApplicationContext.RequestExit();
                }
                catch (InvalidDataException) { if (!updateStatus.IsDisposed) updateStatus.Text = LocalizationManager.T("About.UpdateVerifyFailed"); }
                catch (InvalidOperationException) { if (!updateStatus.IsDisposed) updateStatus.Text = LocalizationManager.T("About.UpdateNoDigest"); }
                catch (Exception) { if (!updateStatus.IsDisposed) updateStatus.Text = LocalizationManager.T("About.UpdateFailed"); }
                finally
                {
                    installingUpdate = false;
                    if (!installBtn!.IsDisposed) installBtn.Enabled = true;
                }
            });
            installBtn.Visible = false;

            var checkNow = NewHoverButton(LocalizationManager.T("About.UpdateCheckNow"), async () =>
            {
                updateStatus.Text = LocalizationManager.T("About.UpdateChecking");
                installBtn!.Visible = false; whatsNew.Visible = false; pendingUpdate = null;
                var result = await UpdateCheckService.CheckAsync();
                if (updateStatus.IsDisposed) return;
                if (!result.Succeeded) updateStatus.Text = LocalizationManager.T("About.UpdateFailed");
                else if (result.Update != null)
                {
                    pendingUpdate = result.Update;
                    updateStatus.Text = string.Format(LocalizationManager.T("About.UpdateNewer"), result.Update.Version.ToString(3));
                    whatsNew.Visible = true;
                    installBtn.Visible = true;
                }
                else updateStatus.Text = string.Format(LocalizationManager.T("About.UpdateUpToDate"), UpdateCheckService.CurrentVersion.ToString(3));

                // Ζητήθηκε ρητά: ο έλεγχος ενημερώσεων του Opsis να ελέγχει ΚΑΙ τα dependencies
                // (FFmpeg/WebView2) στο ίδιο πέρασμα, αντί ο χρήστης να πρέπει να θυμάται να πάει
                // ξεχωριστά στις Ρυθμίσεις. Δεν κατεβάζει τίποτα αυτόματα εδώ — μόνο ενημερώνει.
                var missing = DependencyManagerService.All.Where(d => !d.IsInstalled()).Select(d => d.Name).ToList();
                if (missing.Count > 0 && !updateStatus.IsDisposed)
                    updateStatus.Text += "\n" + string.Format(LocalizationManager.T("About.UpdateDependenciesMissing"), string.Join(", ", missing));
            });
            updateRow.Controls.Add(checkNow);
            updateRow.Controls.Add(installBtn);
            updateRow.Controls.Add(whatsNew);
            panel.Controls.Add(updateRow);
            panel.Controls.Add(updateProgress);
            panel.Controls.Add(updateStatus);

            AddSection(panel, LocalizationManager.T("About.SectionInstructions"));
            AddText(panel, LocalizationManager.T("About.InstructionsList"));

            AddSection(panel, LocalizationManager.T("About.SectionShortcuts"));
            panel.Controls.Add(new ShortcutsCard(LocalizationManager.T("About.ShortcutsList")) { Margin = new Padding(0, 0, 0, 14) });

            AddSection(panel, LocalizationManager.T("About.SectionLicense"));
            AddText(panel, LocalizationManager.T("About.LicenseSummary"));
            // Ζητήθηκε ρητά "το κουμπί της άδειας χρήσης να εμφανίζεται όπως στο GearWin" — χωρίς
            // ακριβές screenshot αναφοράς του GearWin εδώ, τουλάχιστον διαφοροποιείται οπτικά από
            // τα γενικά full-width κουμπιά ενεργειών της σελίδας (accent-χρωματισμένο "pill", auto-
            // sized) αντί να μοιάζει με απλό κουμπί ενέργειας. Αν έχεις screenshot του πραγματικού
            // GearWin κουμπιού, πες μου να το ταιριάξω ακριβώς.
            // ΧΩΡΙΣ εικονίδιο emoji (📄): η γραμματοσειρά UI (Segoe UI) δεν έχει έγχρωμο glyph για
            // αυτό, οπότε αποδιδόταν σαν απλό μονόχρωμο περίγραμμα εγγράφου — αυτό ήταν το
            // "μοιάζει με εικονίδιο html, δεν φαίνεται καλά" που ανέφερε ο χρήστης.
            var licenseBtn = new HoverButton
            {
                Text = LocalizationManager.T("About.LicenseButton"),
                AutoSize = true,
                Padding = new Padding(18, 8, 18, 8),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = UiTheme.AccentBlue,
                BaseColor = UiTheme.AccentBlue,
                HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f),
                Margin = new Padding(0, 4, 0, 3),
                TabStop = false,
                Cursor = Cursors.Hand,
                Font = new Font(UiTheme.FontBody.FontFamily, 9.5f, FontStyle.Bold)
            };
            licenseBtn.FlatAppearance.BorderSize = 0;
            // ΟΧΙ χειροκίνητο UiTheme.ApplyRoundedRegion εδώ: το HoverButton.OnSizeChanged το
            // κάνει ήδη αυτόματα με το ΤΕΛΙΚΟ μέγεθος μετά το AutoSize.
            // ΔΙΟΡΘΩΣΗ πραγματικού bug: το αρχείο LICENSE δεν έχει επέκταση, οπότε το
            // Process.Start(UseShellExecute=true) δεν έχει ΚΑΝΕΝΑΝ προεπιλεγμένο handler να
            // ανοίξει — τα Windows εμφανίζουν το δικό τους παράθυρο "Επιλέξτε μια εφαρμογή" αντί
            // για το αναμενόμενο popup με το κείμενο της άδειας (ακριβώς αυτό ανέφερε ο χρήστης
            // ως διαφορά από το GearWin). Το GearWin δείχνει το ΚΕΙΜΕΝΟ απευθείας σε δικό του
            // παράθυρο· τώρα κάνει το ίδιο εδώ, διαβάζοντας το αρχείο ο ίδιος αντί να το ανοίγει
            // με εξωτερικό πρόγραμμα.
            licenseBtn.Click += (_, _) => ShowLicenseDialog();
            panel.Controls.Add(licenseBtn);

            SetPage("Page.About.Title", panel);
        }

        // Ζητήθηκε ρητά "βελτίωσε το κείμενο και την εμφάνιση του παραθύρου άδειας χρήσης" — πριν
        // ήταν ένα flat TextBox με το LICENSE file χυμένο μέσα ως ένα αδιαφοροποίητο μπλοκ μονοσπέικ
        // κειμένου (χωρίς ιεραρχία ανάμεσα στην περίληψη, τη νομική ρήτρα και τα third-party
        // components). Τώρα: επικεφαλίδα με εικονίδιο+τίτλο, RichTextBox που ξεχωρίζει οπτικά τις 4
        // ενότητες του LICENSE (διαχωρισμένες ήδη με "----" γραμμές στο ίδιο το αρχείο) — επικεφαλίδα
        // κάθε ενότητας σε accent χρώμα/bold, το ίδιο το νομικό κείμενο MIT σε μικρότερη γραμματοσειρά
        // (είναι το λιγότερο σημαντικό για τον μέσο χρήστη να διαβάσει προσεκτικά) — plus pill κουμπί
        // συνεπές με το υπόλοιπο MediaPlayerPro-style πέρασμα της εφαρμογής.
        // Γενικό themed παράθυρο κειμένου (βοήθεια/οδηγίες/ιστορικό/άδεια plugin). Ίδια αισθητική με το παράθυρο άδειας.
        private void ShowTextDialog(string title, string subtitle, string text)
        {
            using var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(720, 640),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };

            var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = UiTheme.Surface };
            header.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, AutoSize = true, Location = new Point(20, 8) });
            header.Controls.Add(new Label { Text = subtitle, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(20, 34) });

            var box = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10f), DetectUrls = false, Text = text.Replace("\r\n", "\n")
            };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = UiTheme.Background };
            var ok = new HoverButton { Text = "OK", Width = 110, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = UiTheme.AccentCyan, ForeColor = Color.White, Cursor = Cursors.Hand, DialogResult = DialogResult.OK, Location = new Point(590, 10) };
            ok.FlatAppearance.BorderSize = 0;
            footer.Controls.Add(ok);
            dialog.Controls.Add(box);
            dialog.Controls.Add(footer);
            dialog.Controls.Add(header);
            dialog.AcceptButton = ok;
            dialog.ShowDialog(this);
        }

        private void ShowLicenseDialog()
        {
            var licensePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE");
            string text;
            try { text = File.Exists(licensePath) ? File.ReadAllText(licensePath) : "LICENSE file not found."; }
            catch (IOException) { text = "Could not read the LICENSE file."; }

            using var dialog = new Form
            {
                Text = LocalizationManager.T("About.LicenseButton"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(640, 600),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch (DllNotFoundException) { } };
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);

            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = UiTheme.Surface, Padding = new Padding(20, 0, 20, 0) };
            // ΔΙΟΡΘΩΣΗ "το εικονίδιο επικαλύπτει το MIT": το "⚖" (U+2696) δεν υπάρχει αξιόπιστα στη
            // Segoe UI — ο χαρακτήρας πέφτει σε font fallback με απρόβλεπτο πλάτος μέτρησης, οπότε
            // το AutoSize του Label (και άρα το σταθερό X=54 του τίτλου δίπλα του) υπολόγιζε λάθος
            // πλάτος και ο τίτλος ξεκινούσε πάνω στο ίδιο το εικονίδιο (ίδια κατηγορία προβλήματος
            // με τα εικονίδια σημαιών — βλ. FlagIcons.cs). Διόρθωση: σταθερό, γενναιόδωρο κουτί
            // (TextAlign=MiddleCenter) αντί για AutoSize, ώστε όποιο κι αν είναι το πραγματικό
            // μετρημένο πλάτος του glyph να μένει μέσα στο κουτί — ο τίτλος ξεκινάει πάντα στο ίδιο,
            // σταθερό X.
            var headerIcon = new Label { Text = "⚖", Font = new Font("Segoe UI Symbol", 16f), ForeColor = UiTheme.AccentCyan, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(20, 10), Size = new Size(34, 44) };
            var headerTitle = new Label { Text = "MIT License", Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, AutoSize = true, Location = new Point(64, 10) };
            var headerSub = new Label { Text = "Opsis — free, open-source, no warranty", Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(64, 34) };
            header.Controls.Add(headerIcon); header.Controls.Add(headerTitle); header.Controls.Add(headerSub);

            var rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(20),
                DetectUrls = false
            };
            rtb.HandleCreated += (_, _) => GlassBlur.ApplyScrollbarTheme(rtb, UiTheme.Background.GetBrightness() < 0.5f);
            PopulateLicenseRichText(rtb, text);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = UiTheme.Background, Padding = new Padding(20, 0, 20, 14) };
            var closeBtn = new HoverButton { Text = "OK", Width = 110, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = UiTheme.AccentCyan, ForeColor = Color.White, Cursor = Cursors.Hand, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            closeBtn.FlatAppearance.BorderSize = 0;
            closeBtn.Location = new Point(footer.ClientSize.Width - closeBtn.Width - 20, 12);
            closeBtn.HandleCreated += (_, _) => UiTheme.ApplyRoundedRegion(closeBtn, closeBtn.Height / 2);
            footer.Resize += (_, _) => closeBtn.Location = new Point(footer.ClientSize.Width - closeBtn.Width - 20, 12);
            footer.Controls.Add(closeBtn);

            var bodyWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 0) };
            bodyWrap.Controls.Add(rtb);

            dialog.Controls.Add(bodyWrap);
            dialog.Controls.Add(footer);
            dialog.Controls.Add(header);
            dialog.AcceptButton = closeBtn;
            dialog.CancelButton = closeBtn;
            dialog.ShowDialog(this);
        }

        private void PopulateLicenseRichText(RichTextBox rtb, string text)
        {
            rtb.Clear();
            var headingFont = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            var bodyFont = UiTheme.FontBody;
            // ΔΙΟΡΘΩΣΗ "το κείμενο είναι σαν από το σημειωματάριο": η Consolas (μονοσπέικ) έκανε
            // ακριβώς το μπλοκ που έπρεπε να φαίνεται λιγότερο σημαντικό να μοιάζει με απλό .txt σε
            // Notepad αντί για μέρος ενός σχεδιασμένου dialog. Ίδια κανονική γραμματοσειρά με το
            // υπόλοιπο κείμενο, μόνο μικρότερη + δευτερεύον χρώμα διατηρεί την ιεραρχία χωρίς την
            // αισθητική του plaintext.
            var legalFont = new Font(bodyFont.FontFamily, 8.5f);
            // Το ίδιο το LICENSE αρχείο χωρίζεται ήδη σε 4 ενότητες με γραμμές "----" — η πρώτη
            // (περίληψη/αποποίηση ευθύνης) μένει στη βασική γραμματοσειρά, η 2η (το ίδιο το νομικό
            // κείμενο MIT) μικραίνει (λιγότερο σημαντικό να διαβαστεί λέξη-λέξη), οι επόμενες
            // (Third-Party Components, Weather data) ξαναγυρνούν στη βασική γραμματοσειρά.
            var sections = text.Replace("\r\n", "\n").Split(new[] { "----------------------------------------------------------------------" }, StringSplitOptions.None);
            for (int i = 0; i < sections.Length; i++)
            {
                string section = sections[i].Trim('\n');
                if (section.Length == 0) continue;
                var lines = section.Split('\n');
                bool isLegalBlock = i == 1; // the raw MIT boilerplate block
                for (int li = 0; li < lines.Length; li++)
                {
                    string line = lines[li];
                    bool looksLikeHeading = li == 0 && !isLegalBlock && line.Length < 40 && !line.Contains('.');
                    rtb.SelectionFont = looksLikeHeading ? headingFont : (isLegalBlock ? legalFont : bodyFont);
                    rtb.SelectionColor = looksLikeHeading ? UiTheme.AccentCyan : (isLegalBlock ? UiTheme.TextSecondary : UiTheme.TextPrimary);
                    rtb.AppendText(line + "\n");
                }
                if (i < sections.Length - 1) rtb.AppendText("\n");
            }
            rtb.SelectionStart = 0;
        }

        private void ShowChangelogAfterUpdate()
        {
            try
            {
                var a = AppSettings.Load();
                string cur = UpdateCheckService.CurrentVersion.ToString(3);
                if (a.LastRunVersion == cur) return;
                bool upgraded = !string.IsNullOrEmpty(a.LastRunVersion);       // first ever run: nothing to show
                a.LastRunVersion = cur;
                a.Save();
                if (upgraded) BeginInvoke(new Action(ShowVersionHistory));
            }
            catch (Exception) { /* cosmetic */ }
        }

        private void ShowVersionHistory()
        {
            using var dialog = new Form
            {
                Text = LocalizationManager.T("VersionHistory.Title"),
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(720, 680),
                BackColor = UiTheme.Background,
                ForeColor = UiTheme.TextPrimary,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            dialog.HandleCreated += (_, _) => { int d = UiTheme.Background.GetBrightness() < 0.5f ? 1 : 0; try { DwmSetWindowAttribute(dialog.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int)); } catch { } };
            dialog.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dialog.Handle);

            const int contentWidth = 630;
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22, 18, 22, 18), BackColor = UiTheme.Background };
            GlassBlur.ApplyScrollbarTheme(list, UiTheme.Background.GetBrightness() < 0.5f);
            list.Controls.Add(new Label { Text = LocalizationManager.T("VersionHistory.Title"), Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
            list.Controls.Add(new Label { Text = LocalizationManager.T("VersionHistory.Subtitle"), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, MaximumSize = new Size(contentWidth, 0), AutoSize = true, Margin = new Padding(0, 0, 0, 14) });

            // Στυλ GearWin: η τρέχουσα έκδοση σε κάρτα γεμάτη με το accent (λευκό κείμενο), οι παλιότερες σε απλές κάρτες που ανοίγουν με κλικ.
            bool isLatest = true;
            foreach (var entry in VersionHistory)
            {
                list.Controls.Add(BuildVersionCard(entry.Version, entry.Date, isLatest, contentWidth));
                isLatest = false;
            }
            dialog.Controls.Add(list);
            dialog.ShowDialog(this);
        }

        // Σημειώσεις έκδοσης: κάθε γραμμή του locale κλειδιού ξεκινά με "+ " (προσθήκη), "~ " (βελτίωση/διόρθωση) ή "= " (σημείωση που δεν μετράται).
        private static (int Added, int Improved, string? Summary, List<(char Type, string Text)> Items) ParseVersionNotes(string notes)
        {
            int added = 0, improved = 0;
            string? summary = null;
            var items = new List<(char, string)>();
            foreach (var raw in notes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length < 3 || line[1] != ' ') { summary = summary == null ? line : summary + " " + line; continue; }
                var text = line[2..].Trim();
                switch (line[0])
                {
                    case '+': added++; items.Add(('+', text)); break;
                    case '~': improved++; items.Add(('~', text)); break;
                    default: summary = summary == null ? text : summary + " " + text; break;
                }
            }
            return (added, improved, summary, items);
        }

        private static string VersionCount(int n, string oneKey, string manyKey) =>
            string.Format(LocalizationManager.T(n == 1 ? oneKey : manyKey), n);

        private static Control BuildVersionCard(string version, string date, bool isLatest, int width)
        {
            var parsed = ParseVersionNotes(LocalizationManager.T("VersionNotes." + version));
            var bg = isLatest ? UiTheme.AccentCyan : UiTheme.Surface;
            var titleColor = isLatest ? Color.White : UiTheme.TextPrimary;
            var textColor = isLatest ? Color.FromArgb(240, 255, 255, 255) : UiTheme.TextSecondary;
            var addColor = isLatest ? Color.White : Color.FromArgb(76, 175, 80);
            var impColor = isLatest ? Color.White : UiTheme.AccentCyan;
            const int pad = 16;
            int inner = width - pad * 2;

            var card = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0),
                BackColor = bg, Padding = new Padding(pad, 12, pad, 14), Margin = new Padding(0, 0, 0, 10)
            };
            card.SizeChanged += (_, _) => UiTheme.ApplyRoundedRegion(card, 8);

            // ----- επικεφαλίδα (κλικ = άνοιγμα/κλείσιμο για παλιότερες εκδόσεις) -----
            var header = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0), BackColor = Color.Transparent };
            string prefix = LocalizationManager.T("VersionHistory.VersionPrefix");
            var arrow = new Label { Text = isLatest ? "" : "▸", AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = UiTheme.AccentCyan, Margin = new Padding(0, 0, 6, 0) };
            var title = new Label
            {
                Text = $"{prefix} {version}   •   {date}" + (isLatest ? "   •   " + LocalizationManager.T("VersionHistory.Current") : ""),
                AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = titleColor, Margin = new Padding(0)
            };
            var titleRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0), BackColor = Color.Transparent };
            if (!isLatest) titleRow.Controls.Add(arrow);
            titleRow.Controls.Add(title);
            header.Controls.Add(titleRow);

            var counts = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 4, 0, 0), BackColor = Color.Transparent };
            if (parsed.Added > 0)
                counts.Controls.Add(new Label { Text = "✚ " + VersionCount(parsed.Added, "VersionHistory.AddedOne", "VersionHistory.AddedMany"), AutoSize = true, Font = new Font("Segoe UI Symbol", 9.5f, FontStyle.Bold), ForeColor = addColor, Margin = new Padding(0, 0, 18, 0) });
            if (parsed.Improved > 0)
                counts.Controls.Add(new Label { Text = "▲ " + VersionCount(parsed.Improved, "VersionHistory.ImprovedOne", "VersionHistory.ImprovedMany"), AutoSize = true, Font = new Font("Segoe UI Symbol", 9.5f, FontStyle.Bold), ForeColor = impColor, Margin = new Padding(0) });
            if (counts.Controls.Count > 0) header.Controls.Add(counts);
            card.Controls.Add(header);

            // ----- σώμα: περίληψη + δύο ομάδες (Προστέθηκαν / Βελτιώθηκαν) -----
            var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 10, 0, 0), BackColor = Color.Transparent, Visible = isLatest };
            if (!string.IsNullOrWhiteSpace(parsed.Summary))
                body.Controls.Add(new Label { Text = parsed.Summary, AutoSize = true, MaximumSize = new Size(inner, 0), Font = new Font("Segoe UI", 9.5f, FontStyle.Italic), ForeColor = textColor, Margin = new Padding(0, 0, 0, 6) });

            void AddGroup(char type, string sectionKey, Color glyphColor, string glyph)
            {
                var group = parsed.Items.Where(i => i.Type == type).ToList();
                if (group.Count == 0) return;
                body.Controls.Add(new Label { Text = UiTheme.UpperNoAccents(LocalizationManager.T(sectionKey)), AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = glyphColor, Margin = new Padding(0, 6, 0, 3) });
                foreach (var item in group)
                {
                    var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 3), BackColor = Color.Transparent };
                    row.Controls.Add(new Label { Text = glyph, AutoSize = false, Size = new Size(20, 20), Font = new Font("Segoe UI Symbol", 8f), ForeColor = glyphColor, Margin = new Padding(0, 1, 0, 0) });
                    row.Controls.Add(new Label { Text = item.Text, AutoSize = true, MaximumSize = new Size(inner - 26, 0), Font = UiTheme.FontBody, ForeColor = textColor, Margin = new Padding(0) });
                    body.Controls.Add(row);
                }
            }
            AddGroup('+', "VersionHistory.SectionAdded", addColor, "✚");
            AddGroup('~', "VersionHistory.SectionImproved", impColor, "▲");
            card.Controls.Add(body);

            if (!isLatest)
            {
                void Toggle()
                {
                    body.Visible = !body.Visible;
                    arrow.Text = body.Visible ? "▾" : "▸";
                }
                foreach (Control c in new Control[] { card, header, titleRow, title, arrow, counts })
                {
                    c.Cursor = Cursors.Hand;
                    c.Click += (_, _) => Toggle();
                }
                foreach (Control c in counts.Controls) { c.Cursor = Cursors.Hand; c.Click += (_, _) => Toggle(); }
            }
            return card;
        }

        private FlowLayoutPanel CreatePagePanel()
        {
            var panel = new FlowLayoutPanel
            {
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4),
                BackColor = UiTheme.Background
            };
            // Ζητήθηκε ρητά "να μην είναι το scrollbar λευκό στο dark mode".
            GlassBlur.ApplyScrollbarTheme(panel, UiTheme.Background.GetBrightness() < 0.5f);
            // ΔΙΟΡΘΩΣΗ πραγματικού bug, live-verified ("scroll up issues στις καρτέλες"): τα Windows
            // στέλνουν WM_MOUSEWHEEL στο control ΚΑΤΩ από τον κέρσορα, όχι απαραίτητα στο
            // AutoScroll FlowLayoutPanel της σελίδας — μόλις ο κέρσορας βρεθεί πάνω από ένα Button/
            // CheckBox/Label (πρακτικά αναπόφευκτο σε μια σελίδα γεμάτη κουμπιά), η κύλιση φαινόταν
            // να "κολλάει" εκεί, ενώ στην πραγματικότητα απλώς δεν έφτανε ΠΟΤΕ στο panel. Λύση:
            // προώθηση του MouseWheel από ΚΑΘΕ child control (καθώς προστίθεται, αναδρομικά) πίσω
            // στο scroll του page panel — εκτός από controls με δικό τους νόημα για το wheel
            // (TextBox/ComboBox/NumericUpDown, ή ήδη ξεχωριστά AutoScroll containers).
            EnableWheelPassthrough(panel);
            return panel;
        }

        private static void EnableWheelPassthrough(ScrollableControl scrollHost)
        {
            void Hook(Control c)
            {
                bool hasOwnWheelMeaning = c is TextBoxBase || c is ListControl || c is NumericUpDown
                    || (c is ScrollableControl inner && !ReferenceEquals(inner, scrollHost) && inner.AutoScroll);
                if (!ReferenceEquals(c, scrollHost) && !hasOwnWheelMeaning)
                {
                    c.MouseWheel += (_, e) =>
                    {
                        if (e is HandledMouseEventArgs hme) hme.Handled = true;
                        var pos = scrollHost.AutoScrollPosition;
                        int newY = Math.Max(0, -pos.Y - e.Delta);
                        scrollHost.AutoScrollPosition = new Point(-pos.X, newY);
                    };
                }
                if (!hasOwnWheelMeaning)
                {
                    c.ControlAdded += (_, ce) => { if (ce.Control != null) Hook(ce.Control); };
                    foreach (Control child in c.Controls) Hook(child);
                }
            }
            Hook(scrollHost);
        }

        private static void AddHeading(Control parent, string text)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Width = 760,
                Text = text,
                Font = UiTheme.FontHeading,
                ForeColor = UiTheme.TextPrimary,
                Margin = new Padding(0, 0, 0, 4)
            });
        }

        private static void AddText(Control parent, string text)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(780, 0),
                Text = text,
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 0, 0, 14)
            });
        }

        private static void AddSection(Control parent, string text)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiTheme.UpperNoAccents(text),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = UiTheme.AccentCyan,
                Margin = new Padding(0, 14, 0, 6)
            });
        }

        // Επανασχεδιασμός σελίδας Εξατομίκευση (task tracker t36, "εμπλούτισε/άλλαξε το περιεχόμενο
        // της καρτέλας εξατομίκευση"): κάθε ενότητα μέσα σε δική της οπτικά ξεχωριστή "κάρτα"
        // (φόντο + στρογγυλεμένες γωνίες), αντί για μια απλή επικεφαλίδα πάνω στο γυμνό φόντο της
        // σελίδας — το μόνο που άλλαξε είναι το ΠΕΡΙΒΛΗΜΑ· η λογική κάθε γραμμής μένει ίδια.
        // Επιστρέφει το ΕΣΩΤΕΡΙΚΟ panel όπου ο καλών προσθέτει τις δικές του γραμμές.
        // ΔΙΟΡΘΩΣΗ "τα πλαίσια των κατηγοριών να είναι στο ίδιο πλάτος": το Panel.MaximumSize δεν
        // εμποδίζει αξιόπιστα το AutoSize να μεγαλώσει το πλάτος όταν το περιεχόμενο (π.χ. ένα
        // button grid με WrapContents) ζητάει περισσότερο χώρο - γνωστή ιδιορρυθμία του WinForms.
        // Η αξιόπιστη λύση είναι να παρακαμφθεί το ίδιο το GetPreferredSize ώστε το πλάτος να μένει
        // πάντα σταθερό, ενώ το ύψος συνεχίζει να υπολογίζεται κανονικά από το περιεχόμενο.
        private sealed class FixedWidthPanel : Panel
        {
            public FixedWidthPanel() { DoubleBuffered = true; }
            public int FixedWidth { get; set; }
            public override Size GetPreferredSize(Size proposedSize)
            {
                var baseSize = base.GetPreferredSize(new Size(FixedWidth, proposedSize.Height));
                return new Size(FixedWidth, baseSize.Height);
            }
        }

        // Ίδιο ακριβώς πρόβλημα/διόρθωση με το FixedWidthPanel, αλλά για FlowLayoutPanel - εδώ το
        // "inner" (η στήλη περιεχομένου μέσα στην κάρτα) έπαθε το ΙΔΙΟ: ένα button grid με
        // WrapContents=true από μέσα του το μεγάλωνε αντί να τυλίγεται σε 2η γραμμή, αφού ΚΙ αυτό
        // ήταν AutoSize+GrowAndShrink με απλή ιδιότητα Width (όχι πραγματικά "κλειδωμένο" πλάτος).
        private sealed class FixedWidthFlowPanel : FlowLayoutPanel
        {
            public FixedWidthFlowPanel() { DoubleBuffered = true; }
            public int FixedWidth { get; set; }
            public override Size GetPreferredSize(Size proposedSize)
            {
                var baseSize = base.GetPreferredSize(new Size(FixedWidth, proposedSize.Height));
                return new Size(FixedWidth, baseSize.Height);
            }
        }

        private static FlowLayoutPanel AddCardSection(Control parent, string title, int width = 760)
        {
            var inner = new FixedWidthFlowPanel { FixedWidth = width - 36, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = width - 36 };
            inner.Controls.Add(new Label
            {
                AutoSize = true,
                Text = UiTheme.UpperNoAccents(title),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = UiTheme.AccentCyan,
                Margin = new Padding(0, 0, 0, 10)
            });

            var card = new FixedWidthPanel
            {
                FixedWidth = width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UiTheme.Surface,
                Padding = new Padding(18, 14, 18, 16),
                Margin = new Padding(0, 0, 0, 16)
            };
            card.Controls.Add(inner);
            // Το μέγεθος της κάρτας αλλάζει καθώς ο καλών προσθέτει γραμμές ΜΕΤΑ την επιστροφή αυτής
            // της μεθόδου — η στρογγυλεμένη περιοχή πρέπει να ξανα-υπολογίζεται σε κάθε αλλαγή μεγέθους.
            card.Resize += (_, _) => UiTheme.ApplyRoundedRegion(card, 12);
            parent.Controls.Add(card);
            return inner;
        }

        private static StatCard AddCard(Control parent, string title, string value, Action? onClick = null)
        {
            var card = new StatCard(title, value) { Width = 760, Height = 80, Margin = new Padding(0, 0, 0, 10) };
            if (onClick != null)
            {
                card.Cursor = Cursors.Hand;
                card.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) onClick(); };
            }
            parent.Controls.Add(card);
            return card;
        }

        private static MeterCard AddMeter(Control parent, string title, double initial)
        {
            var card = new MeterCard(title, initial) { Width = 760, Height = 90, Margin = new Padding(0, 0, 0, 10) };
            parent.Controls.Add(card);
            return card;
        }

        // Πλήρως owner-draw στατιστική κάρτα (τίτλος + τιμή) — τα παιδιά-Label μέσα σε
        // Panel/FlowLayoutPanel εμφάνιζαν σποραδικά αόρατο κείμενο σε αυτό το build/runtime
        // (ίδιας οικογένειας quirk με το NavButton), οπότε ζωγραφίζουμε το κείμενο απευθείας.
        // Συντομεύσεις πληκτρολογίου σε ΕΝΑ ενιαίο στοιχείο: ομάδες, "πλήκτρα" (keycaps) αριστερά και περιγραφή ευθυγραμμισμένη δεξιά.
        // Πηγή: το locale κλειδί About.ShortcutsList — γραμμή "# Τίτλος" = νέα ομάδα, αλλιώς "Πλήκτρα  Περιγραφή" (διπλό κενό ως διαχωριστικό).
        private sealed class ShortcutsCard : Control
        {
            private readonly List<(bool IsHeader, string Left, string Right)> _rows = new();
            private const int CardWidth = 760, KeysX = 20, DescX = 330, RowH = 32, HeaderH = 36, PadTop = 12, PadBottom = 14;

            public ShortcutsCard(string source)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                foreach (var raw in source.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("# ")) { _rows.Add((true, line[2..].Trim(), "")); continue; }
                    int sep = line.IndexOf("  ", StringComparison.Ordinal);
                    if (sep <= 0) continue;
                    _rows.Add((false, line[..sep].Trim(), line[(sep + 2)..].Trim()));
                }
                int h = PadTop + PadBottom + _rows.Sum(r => r.IsHeader ? HeaderH : RowH);
                Size = new Size(CardWidth, h);
            }

            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UiTheme.ApplyRoundedRegion(this, 8); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels

                using var headerFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using var keyFont = new Font("Segoe UI Semibold", 9f);
                using var descFont = new Font("Segoe UI", 10f);
                using var accent = new SolidBrush(UiTheme.AccentCyan);
                using var descBrush = new SolidBrush(UiTheme.TextSecondary);
                using var keyText = new SolidBrush(UiTheme.TextPrimary);
                using var keyFill = new SolidBrush(UiTheme.Background);
                using var keyBorder = new Pen(UiTheme.Border, 1f);
                using var plusBrush = new SolidBrush(UiTheme.TextMuted);
                using var sepPen = new Pen(Color.FromArgb(40, UiTheme.TextMuted), 1f);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

                int y = PadTop;
                bool first = true;
                foreach (var row in _rows)
                {
                    if (row.IsHeader)
                    {
                        if (!first) g.DrawLine(sepPen, KeysX, y + 2, Width - KeysX, y + 2);
                        g.DrawString(UiTheme.UpperNoAccents(row.Left), headerFont, accent, KeysX, y + 12);
                        y += HeaderH;
                        first = false;
                        continue;
                    }
                    first = false;
                    // keycaps: κάθε κομμάτι πριν/μετά το '+' ως ξεχωριστό "πλήκτρο"
                    int x = KeysX;
                    var parts = row.Left.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (row.Left.EndsWith("+,")) parts = row.Left[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(",").ToArray();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var size = TextRenderer.MeasureText(parts[i], keyFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                        int w = Math.Max(26, size.Width + 16);
                        var capRect = new Rectangle(x, y + 3, w, RowH - 8);
                        using (var path = UiTheme.RoundedPath(capRect, 6))
                        {
                            g.FillPath(keyFill, path);
                            g.DrawPath(keyBorder, path);
                        }
                        g.DrawString(parts[i], keyFont, keyText, new RectangleF(capRect.X, capRect.Y, capRect.Width, capRect.Height), sf);
                        x += w;
                        if (i < parts.Length - 1) { g.DrawString("+", descFont, plusBrush, x + 1, y + 5); x += 16; }
                    }
                    g.DrawString(row.Right, descFont, descBrush, new RectangleF(DescX, y + 3, Width - DescX - KeysX, RowH - 6), new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    y += RowH;
                }
            }
        }

        private sealed class StatCard : Panel
        {
            private readonly string _title;
            private string _value;

            public StatCard(string title, string value)
            {
                _title = UiTheme.UpperNoAccents(title);
                _value = value;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public new string Text
            {
                get => _value;
                set { _value = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels

                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 12);

                using var valueFont = new Font("Segoe UI", 13f);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_value, valueFont, valueBrush, 18, 36);
            }
        }

        // Κάρτα μέτρησης με δυναμική, χρωματιστή μπάρα (πράσινο → κίτρινο → κόκκινο ανάλογα με %).
        // Πραγματικό sparkline chart (area fill + γραμμή + τελευταίο σημείο τονισμένο) αντί για
        // απλό κείμενο — ζητήθηκε ρητά "όμορφα γραφήματα" για Network/Processes/Power. Στηρίζεται
        // αυτόματα στη μεγαλύτερη τιμή του ιστορικού (min floor) ώστε να μη χρειάζεται fixed cap.
        private sealed class SparklineCard : Panel
        {
            private readonly string _title;
            private readonly double _minScaleFloor;
            private readonly int _maxPoints;
            private readonly Queue<double> _series1 = new();
            private readonly Queue<double>? _series2;
            private readonly bool _hasSeries2;
            private string _valueText = "";

            public SparklineCard(string title, int seriesCount, double minScaleFloor, int maxPoints = 30)
            {
                _title = title;
                _minScaleFloor = minScaleFloor;
                _maxPoints = maxPoints;
                _hasSeries2 = seriesCount >= 2;
                if (_hasSeries2) _series2 = new Queue<double>();
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public void Push(double value1, double? value2, string valueText)
            {
                _series1.Enqueue(value1);
                while (_series1.Count > _maxPoints) _series1.Dequeue();
                if (_hasSeries2 && value2.HasValue)
                {
                    _series2!.Enqueue(value2.Value);
                    while (_series2.Count > _maxPoints) _series2.Dequeue();
                }
                _valueText = valueText;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels
                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 10);
                using var valueFont = new Font("Segoe UI", 11.5f);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_valueText, valueFont, valueBrush, 18, 28);

                if (_series1.Count < 2) return;
                int chartX = 18, chartY = 54, chartW = Math.Max(10, Width - 36), chartH = Math.Max(10, Height - 66);

                using (var gridPen = new Pen(UiTheme.Border))
                    for (int i = 0; i <= 2; i++)
                    {
                        int y = chartY + chartH * i / 2;
                        g.DrawLine(gridPen, chartX, y, chartX + chartW, y);
                    }

                double scaleMax = Math.Max(_minScaleFloor, Math.Max(MaxOf(_series1), _hasSeries2 ? MaxOf(_series2!) : 0));
                DrawSeries(g, _series1, chartX, chartY, chartW, chartH, scaleMax, UiTheme.AccentCyan, true);
                if (_hasSeries2) DrawSeries(g, _series2!, chartX, chartY, chartW, chartH, scaleMax, UiTheme.AccentBlue, false);
            }

            private static double MaxOf(Queue<double> q) { double m = 0; foreach (var v in q) if (v > m) m = v; return m; }

            private void DrawSeries(Graphics g, Queue<double> series, int chartX, int chartY, int chartW, int chartH, double scaleMax, Color color, bool fillArea)
            {
                var values = series.ToArray();
                var pts = new PointF[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    float x = chartX + chartW * i / (float)Math.Max(1, _maxPoints - 1);
                    double v = scaleMax > 0 ? Math.Clamp(values[i] / scaleMax, 0, 1) : 0;
                    pts[i] = new PointF(x, chartY + chartH - (float)(v * chartH));
                }
                if (pts.Length < 2) return;

                if (fillArea)
                {
                    var areaPts = new PointF[pts.Length + 2];
                    Array.Copy(pts, areaPts, pts.Length);
                    areaPts[pts.Length] = new PointF(pts[^1].X, chartY + chartH);
                    areaPts[pts.Length + 1] = new PointF(pts[0].X, chartY + chartH);
                    using var areaBrush = new LinearGradientBrush(new RectangleF(chartX, chartY, chartW, chartH), Color.FromArgb(70, color), Color.FromArgb(0, color), 90f);
                    g.FillPolygon(areaBrush, areaPts);
                }

                using (var linePen = new Pen(color, 1.8f) { LineJoin = LineJoin.Round })
                    g.DrawLines(linePen, pts);

                var last = pts[^1];
                using var dotBrush = new SolidBrush(color);
                g.FillEllipse(dotBrush, last.X - 3, last.Y - 3, 6, 6);
            }
        }

        private sealed class MeterCard : Panel
        {
            private readonly string _title;
            private double _percent;
            private string _valueText;

            public MeterCard(string title, double percent)
            {
                _title = UiTheme.UpperNoAccents(title);
                _percent = percent;
                _valueText = "";
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public void SetValue(double percent, string valueText)
            {
                _percent = Math.Clamp(percent, 0, 100);
                _valueText = valueText;
                Invalidate();
            }

            private static Color BarColor(double percent)
            {
                if (percent < 50) return Color.FromArgb(46, 204, 113);   // green
                if (percent < 80) return Color.FromArgb(241, 196, 15);   // yellow
                return Color.FromArgb(231, 76, 60);                      // red
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                g.Clear(UiTheme.Surface); // Clear, not an antialiased FillRectangle: that leaves a faint line on the edge pixels
                using (var accent = new SolidBrush(UiTheme.AccentCyan))
                    g.FillRectangle(accent, 0, 0, 3, Height);

                // Ζητήθηκε ρητά "τα στατιστικά απόδοσης δίπλα στον τίτλο και όχι από κάτω" —
                // τίτλος αριστερά, τιμή δεξιά-στοιχισμένη, στην ΙΔΙΑ γραμμή αντί για από κάτω.
                using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                using (var titleBrush = new SolidBrush(UiTheme.TextMuted))
                    g.DrawString(_title, titleFont, titleBrush, 18, 14);

                using var valueFont = new Font("Segoe UI", 13f);
                using var valueFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                var valueRect = new RectangleF(18, 2, Width - 36, 28);
                using (var valueBrush = new SolidBrush(UiTheme.TextPrimary))
                    g.DrawString(_valueText, valueFont, valueBrush, valueRect, valueFormat);

                var barRect = new Rectangle(18, Height - 22, Width - 36, 8);
                using (var track = new SolidBrush(UiTheme.SurfaceHover))
                using (var trackPath = UiTheme.RoundedPath(barRect, 4))
                    g.FillPath(track, trackPath);

                int fillWidth = (int)(barRect.Width * (_percent / 100.0));
                if (fillWidth > 4)
                {
                    var fillRect = new Rectangle(barRect.X, barRect.Y, fillWidth, barRect.Height);
                    using var fillBrush = new SolidBrush(BarColor(_percent));
                    using var fillPath = UiTheme.RoundedPath(fillRect, 4);
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }

        // Δύο-στηλη σειρά κουμπιών (π.χ. Quick Launch / Workspace) για εξοικονόμηση κάθετου χώρου.
        private static void AddButtonGrid(Control parent, params (string Text, EventHandler Action)[] items)
        {
            var grid = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            foreach (var (text, action) in items)
            {
                var button = new HoverButton
                {
                    Text = text,
                    // ΔΙΟΡΘΩΣΗ "μέσα στα πλαίσια έχεις μεγάλο μέγεθος κουμπιών, δεν φαίνεται το 3ο
                    // κουμπί": 372px x2 + περιθώριο ξεπερνούσε το (πλέον σταθερό, βλ. AddCardSection)
                    // εσωτερικό πλάτος της κάρτας (724px) - δύο κουμπιά δίπλα-δίπλα ήδη δεν χωρούσαν
                    // καθαρά, πόσο μάλλον τρία να τυλιχτούν σε 2η γραμμή. 350px x2 + 8px περιθώριο =
                    // 708px, χωράει άνετα μέσα στο 724px.
                    Width = 350,
                    Height = 40,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = UiTheme.TextPrimary,
                    BackColor = UiTheme.Surface,
                    Margin = new Padding(0, 3, 8, 3),
                    TabStop = false,
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderSize = 0;
                UiTheme.ApplyRoundedRegion(button, button.Height / 2);
                button.Click += (sender, e) => action(sender, e);
                grid.Controls.Add(button);
            }
            parent.Controls.Add(grid);
        }

        // Ίδιο με το AddButtonGrid, αλλά επιστρέφει τα ίδια τα κουμπιά ώστε ο caller να μπορεί να
        // "σημαδέψει" ποιο/ποια είναι αυτή τη στιγμή ενεργά (π.χ. το τρέχον performance mode) —
        // ζητήθηκε ρητά: ενεργές επιλογές πρέπει να έχουν αντίστοιχη οπτική σήμανση στο κουμπί.
        private static Dictionary<string, HoverButton> AddToggleButtonGrid(Control parent, params (string Text, EventHandler Action)[] items)
        {
            var map = new Dictionary<string, HoverButton>();
            var grid = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
            foreach (var (text, action) in items)
            {
                var button = new HoverButton
                {
                    Text = text,
                    Width = 180,
                    Height = 38,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = UiTheme.TextPrimary,
                    BackColor = UiTheme.Surface,
                    Margin = new Padding(0, 3, 8, 3),
                    TabStop = false,
                    Cursor = Cursors.Hand
                };
                button.FlatAppearance.BorderSize = 0;
                UiTheme.ApplyRoundedRegion(button, button.Height / 2);
                button.Click += (sender, e) => action(sender, e);
                grid.Controls.Add(button);
                map[text] = button;
            }
            parent.Controls.Add(grid);
            return map;
        }

        // Εφαρμόζει οπτική σήμανση στο ενεργό κουμπί ενός AddToggleButtonGrid — ζητήθηκε ρητά
        // "ενσωμάτωσε την παρουσίαση ενός media player": το ενεργό κουμπί γεμίζει πλήρως με το
        // accent χρώμα (λευκό κείμενο), ίδιο "hero button" στυλ με το γεμάτο Play/Pause κουμπί
        // του MediaPlayerPro, αντί για το προηγούμενο ημιδιάφανο tint + λεπτό περίγραμμα.
        private static void MarkActive(Dictionary<string, HoverButton> buttons, string? activeKey)
        {
            foreach (var (key, btn) in buttons)
            {
                bool active = key == activeKey;
                // ΔΙΟΡΘΩΣΗ πραγματικού bug ("μόνο το τικ φαίνεται, όχι το μπλε χρώμα"): εδώ οριζόταν
                // μόνο το BackColor απευθείας - το HoverButton.MouseLeave όμως το επαναφέρει πάντα σε
                // BaseColor (βλ. σχόλιο στο HoverButton), που έμενε στο προεπιλεγμένο Surface αφού
                // κανείς δεν το ενημέρωνε ποτέ εδώ. Αποτέλεσμα: το μπλε χρωμα του επιλεγμένου κουμπιού
                // εξαφανιζόταν μόλις το ποντίκι έμπαινε και έβγαινε από πάνω του - έμενε μόνο το "✓".
                // Τώρα ενημερώνονται ΚΑΙ το BaseColor/HoverBackColor, ώστε το επιλεγμένο κουμπί να
                // παραμένει μπλε (με ελαφρώς πιο ανοιχτό μπλε στο hover αντί να πέφτει πίσω σε γκρι).
                btn.BaseColor = active ? UiTheme.AccentCyan : UiTheme.Surface;
                btn.HoverBackColor = active ? ControlPaint.Light(UiTheme.AccentCyan, 0.15f) : UiTheme.SurfaceHover;
                btn.BackColor = btn.BaseColor;
                btn.ForeColor = active ? Color.White : UiTheme.TextPrimary;
                btn.FlatAppearance.BorderSize = 0;
                btn.Text = active ? "✓  " + key : key;
            }
        }

        // Ίδια διάταξη δύο-στηλών με το AddButtonGrid, αλλά με διανυσματικό εικονίδιο (IconRenderer)
        // αντί για μόνο κείμενο — π.χ. τα κουμπιά "+ System Monitor" της Widget Gallery δεν είχαν
        // κανένα πραγματικό εικονίδιο, μόνο τον χαρακτήρα "＋".
        private static void AddIconButtonGrid(Control parent, params (string IconKey, string Text, EventHandler Action)[] items)
        {
            var grid = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            foreach (var (iconKey, text, action) in items)
            {
                var button = new IconButton(iconKey, text) { Width = 350, Height = 44, Margin = new Padding(0, 3, 8, 3) };
                button.Click += (_, _) => action(button, EventArgs.Empty);
                grid.Controls.Add(button);
            }
            parent.Controls.Add(grid);
        }

        private sealed class IconButton : Panel
        {
            private readonly string _iconKey;
            private bool _hover;
            public new event EventHandler? Click;

            public IconButton(string iconKey, string text)
            {
                _iconKey = iconKey;
                Text = text;
                Cursor = Cursors.Hand;
                TabStop = false;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                MouseEnter += (_, _) => { _hover = true; Invalidate(); };
                MouseLeave += (_, _) => { _hover = false; Invalidate(); };
                MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Click?.Invoke(this, EventArgs.Empty); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Ζητήθηκε ρητά "pill-shaped κουμπιά" (MediaPlayerPro / Orama Media Suite) σε ΟΛΕΣ
                // τις καρτέλες — αυτό το IconButton τροφοδοτεί το AddIconButtonGrid, που
                // χρησιμοποιείται σχεδόν παντού (Dashboard, Widgets, Wallpaper, DeskZones,
                // Εξατομίκευση κ.λπ.), γι' αυτό ήταν το πιο ορατό σημείο που είχε ξεφύγει.
                using (var bgBrush = new SolidBrush(_hover ? UiTheme.SurfaceHover : UiTheme.Surface))
                using (var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), Height / 2))
                    g.FillPath(bgBrush, path);

                IconRenderer.Draw(g, _iconKey, new RectangleF(14, (Height - 22) / 2f, 22, 22), UiTheme.AccentCyan);

                using var textFont = new Font("Segoe UI", 10f);
                using var textBrush = new SolidBrush(UiTheme.TextPrimary);
                var textSize = g.MeasureString(Text, textFont);
                g.DrawString(Text, textFont, textBrush, 46, (Height - textSize.Height) / 2f);
            }
        }

        private static void AddButton(Control parent, string text, EventHandler action)
        {
            var button = new HoverButton
            {
                Text = text,
                Width = 300,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                ForeColor = UiTheme.TextPrimary,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 3, 0, 3),
                TabStop = false,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            UiTheme.ApplyRoundedRegion(button, button.Height / 2);
            button.Click += (sender, e) => action(sender, e);
            parent.Controls.Add(button);
        }

        // Απλό flat button με ελαφρύ hover recolor, ώστε τα κουμπιά να μη νιώθουν στατικά.
        // Ζητήθηκε ρητά: όσα κουμπιά είναι ορθογώνια να γίνουν "pills" (πλήρως στρογγυλεμένα άκρα)
        // — εφαρμόζεται καθολικά εδώ αντί σε κάθε σημείο κλήσης ξεχωριστά, αφού το HoverButton
        // χρησιμοποιείται σε δεκάδες σημεία σε όλη την εφαρμογή. Selectable=false αποτρέπει το
        // ίδιο bug που βρέθηκε στο PillButton του ZoneLayoutEditor: το τετράγωνο keyboard-focus
        // rectangle ενός Button προεξέχει έξω από ένα στρογγυλεμένο Region αν δεν απενεργοποιηθεί.
        private sealed class HoverButton : Button
        {
            // ΔΙΟΡΘΩΣΗ πραγματικού bug: τα Mouse Enter/Leave έκαναν πάντα hardcoded reset σε
            // UiTheme.Surface/SurfaceHover — οποιοδήποτε κουμπί με ΔΙΑΦΟΡΕΤΙΚΟ, σκόπιμο BackColor
            // (π.χ. accent-χρωματισμένο CTA) γύριζε αμέσως πίσω σε γκρι με το πρώτο hover, χωρίς
            // κανένα τρόπο να παραμείνει το επιθυμητό χρώμα. Αυτό ήταν ακριβώς γιατί το κουμπί
            // "Άδεια χρήσης" φαινόταν "ξεθωριασμένο" γκρι αντί για μπλε — αρκούσε ένα πέρασμα του
            // δείκτη από πάνω του. Τώρα το βασικό/hover χρώμα είναι ρυθμιζόμενο ανά instance, με
            // προεπιλογή Surface/SurfaceHover (ίδια συμπεριφορά με πριν για όλα τα υπόλοιπα σημεία
            // κλήσης που δεν τα αλλάζουν ρητά).
            public Color BaseColor { get; set; } = UiTheme.Surface;
            public Color HoverBackColor { get; set; } = UiTheme.SurfaceHover;

            public HoverButton()
            {
                MouseEnter += (_, _) => BackColor = HoverBackColor;
                MouseLeave += (_, _) => BackColor = BaseColor;
                SetStyle(ControlStyles.Selectable, false);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (Width > 0 && Height > 0) UiTheme.ApplyRoundedRegion(this, Height / 2);
            }
        }

        private static void SaveAppSettings()
        {
            var a = AppSettings.Load();
            a.GridSnap = WidgetSnapEngine.EnableGridSnap;
            a.SnapThreshold = WidgetSnapEngine.SnapThreshold;
            a.GridSize = WidgetSnapEngine.GridSize;
            a.Save();
        }

        private void UpdateStatus()
        {
            var m = SystemMonitorService.Instance.GetSnapshot();
            _statusLabel.Text = string.Format(LocalizationManager.T("Status.SystemFormat"), m.CpuPercent.ToString("0.0"), m.AvailableMemoryMb.ToString("0"), RamUsedPercent(m.AvailableMemoryMb, m.TotalMemoryMb));
        }

        // Ποσοστό χρήσης RAM (χρησιμοποιούμενη/συνολική) — εμφανίζεται δίπλα στα MB στην Αρχική, στη γραμμή κατάστασης, στο Performance και στο widget.
        private static string RamUsedPercent(double availableMb, double totalMb) =>
            totalMb > 0 ? Math.Clamp((totalMb - availableMb) / totalMb * 100.0, 0, 100).ToString("0") : "0";

        private sealed class SettingsView : Panel
        {
            public SettingsView()
            {
                DoubleBuffered = true;
                BackColor = UiTheme.Background;
                AutoScroll = true;
                GlassBlur.ApplyScrollbarTheme(this, UiTheme.Background.GetBrightness() < 0.5f);
                var stack = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    Padding = new Padding(4),
                    BackColor = UiTheme.Background
                };
                GlassBlur.ApplyScrollbarTheme(stack, UiTheme.Background.GetBrightness() < 0.5f);
                Controls.Add(stack);

                AddText(stack, LocalizationManager.T("Settings.Intro"));

                AddSection(stack, LocalizationManager.T("Settings.Appearance"));
                stack.Controls.Add(BuildThemeRow());
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.FollowWindowsAccent"), AppSettings.Load().FollowWindowsAccent,
                    v => ThemeManager.SetFollowWindowsAccent(v)));
                stack.Controls.Add(BuildLanguageRow());
                stack.Controls.Add(BuildOpacityRow());
                stack.Controls.Add(BuildDarkIntensityRow());
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.UiSoundCues"), AppSettings.Load().UiSoundsEnabled,
                    v => { var a = AppSettings.Load(); a.UiSoundsEnabled = v; a.Save(); }));

                AddSection(stack, LocalizationManager.T("Settings.SectionSnapping"));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.EnableGridSnap"), AppSettings.Load().GridSnap, v => { WidgetSnapEngine.EnableGridSnap = v; SaveAppSettings(); }));
                var snapRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                snapRow.Controls.Add(NumericRow(LocalizationManager.T("Settings.SnapThreshold"), AppSettings.Load().SnapThreshold, 5, 50,
                    v => { WidgetSnapEngine.SnapThreshold = v; SaveAppSettings(); }));
                snapRow.Controls.Add(NumericRow(LocalizationManager.T("Settings.GridSize"), Math.Clamp(AppSettings.Load().GridSize, 5, 100), 5, 100,
                    v => { WidgetSnapEngine.GridSize = v; SaveAppSettings(); }, marginLeft: 24));
                stack.Controls.Add(snapRow);

                AddSection(stack, LocalizationManager.T("Settings.SectionStartup"));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.StartWithWindows"), StartupManager.IsStartupEnabled(), StartupManager.SetStartup));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.RestoreLastSession"), AppSettings.Load().RestoreLastSession,
                    v => { var a = AppSettings.Load(); a.RestoreLastSession = v; a.Save(); }));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.EnableAnimations"), AppSettings.Load().EnableAnimations,
                    v => { var a = AppSettings.Load(); a.EnableAnimations = v; a.Save(); AnimationsSettingChanged?.Invoke(); }));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.MinimizeToTray"), AppSettings.Load().MinimizeToTray,
                    v => { var a = AppSettings.Load(); a.MinimizeToTray = v; a.Save(); }));
                // Plugins (π.χ. News Crawler): ισχύει στην επόμενη εκκίνηση.
                BuildDiscordRows(stack);
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.PluginsEnabled"), AppSettings.Load().PluginsEnabled,
                    v => { var a = AppSettings.Load(); a.PluginsEnabled = v; a.Save(); }));

                // Ζητήθηκε ρητά "ρύθμιση για διαφάνεια παραθύρων των Windows και γρήγορη εκκίνηση
                // στον πίνακα ελέγχου" — πραγματικές ρυθμίσεις των ίδιων των Windows (μητρώο),
                // όχι δικές μας· βλ. WindowsSystemSettings.cs.
                AddSection(stack, LocalizationManager.T("Settings.SectionWindows"));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.TransparencyEffects"), WindowsSystemSettings.GetTransparencyEffectsEnabled(),
                    v => WindowsSystemSettings.SetTransparencyEffectsEnabled(v)));
                stack.Controls.Add(Checkbox(LocalizationManager.T("Settings.FastStartup"), WindowsSystemSettings.GetFastStartupEnabled(),
                    v =>
                    {
                        if (!WindowsSystemSettings.TrySetFastStartupEnabled(v))
                            MessageBox.Show(this, LocalizationManager.T("Settings.FastStartupAdminRequired"), LocalizationManager.T("Page.Settings.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));

                // Ζητήθηκε ρητά: ο installer εγκαθιστά ήδη το WebView2 Runtime/.NET 10 Runtime μία
                // φορά, αλλά ο χρήστης πρέπει να μπορεί να δει/εγκαταστήσει ξανά τα εξωτερικά
                // dependencies (π.χ. FFmpeg για .wmv) και από μέσα από την εφαρμογή, χωρίς να
                // ξανατρέξει τον installer.
                AddSection(stack, LocalizationManager.T("Settings.SectionDependencies"));
                foreach (var dep in DependencyManagerService.All)
                    stack.Controls.Add(BuildDependencyRow(dep));
            }

            private static Panel BuildDependencyRow(DependencyInfo dep)
            {
                var row = new Panel { Width = 700, Height = 56, BackColor = UiTheme.Surface, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 8) };
                UiTheme.ApplyRoundedRegion(row, 10);

                bool installed = dep.IsInstalled();
                var nameLabel = new Label { Text = dep.Name, AutoSize = true, Location = new Point(0, 2), Font = new Font(UiTheme.FontBody.FontFamily, 10, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
                var descLabel = new Label { Text = dep.Description, AutoSize = true, Location = new Point(0, 22), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary };
                var statusLabel = new Label
                {
                    Text = LocalizationManager.T(installed ? "Settings.DependencyInstalled" : "Settings.DependencyMissing"),
                    AutoSize = true,
                    Location = new Point(420, 8),
                    Font = new Font(UiTheme.FontBody.FontFamily, 9, FontStyle.Bold),
                    ForeColor = installed ? Color.FromArgb(60, 210, 140) : Color.FromArgb(235, 120, 66)
                };
                row.Controls.Add(nameLabel);
                row.Controls.Add(descLabel);
                row.Controls.Add(statusLabel);

                if (dep.WingetId != null)
                {
                    var installBtn = new HoverButton { Text = LocalizationManager.T(installed ? "Settings.DependencyReinstall" : "Settings.DependencyInstall"), Width = 140, Height = 30, Location = new Point(540, 6), FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
                    installBtn.FlatAppearance.BorderSize = 0;
                    installBtn.Click += async (_, _) =>
                    {
                        installBtn.Enabled = false;
                        statusLabel.Text = LocalizationManager.T("Settings.DependencyInstalling");
                        statusLabel.ForeColor = UiTheme.TextMuted;
                        var (success, output) = await DependencyManagerService.InstallViaWingetAsync(dep.WingetId);
                        bool nowInstalled = dep.IsInstalled();
                        statusLabel.Text = LocalizationManager.T(nowInstalled ? "Settings.DependencyInstalled" : "Settings.DependencyMissing");
                        statusLabel.ForeColor = nowInstalled ? Color.FromArgb(60, 210, 140) : Color.FromArgb(235, 120, 66);
                        installBtn.Text = LocalizationManager.T(nowInstalled ? "Settings.DependencyReinstall" : "Settings.DependencyInstall");
                        installBtn.Enabled = true;
                        if (!success && !nowInstalled)
                            MessageBox.Show(string.Format(LocalizationManager.T("Settings.DependencyInstallFailedFormat"), dep.Name, output), "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    };
                    row.Controls.Add(installBtn);
                }
                return row;
            }

            internal static Panel BuildWidgetsThemeRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WidgetsThemeLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 260 };
                // ΔΙΟΡΘΩΣΗ "δυσκολεύεσαι όταν ακολουθεί το θέμα των Windows να είναι full λευκό ή
                // μαύρο": αντί να προσπαθεί να γίνει ΠΡΑΓΜΑΤΙΚΟ blur-through το περιεχόμενο (απαιτεί
                // WS_EX_LAYERED σε κάθε widget control - μεγάλη αλλαγή αρχιτεκτονικής, μεγάλο ρίσκο),
                // νέα ρητή επιλογή "Πλήρης διαφάνεια": απλά ΔΕΝ ζωγραφίζεται ΚΑΝΕΝΑ φόντο στο
                // περιεχόμενο του widget - ό,τι ήδη δείχνει το glass-blur του ίδιου του παραθύρου
                // (GlassBlur.Apply, το ίδιο μηχανισμό με το ήδη σωστό DeskStrip) μένει ορατό χωρίς
                // κανένα συμπαγές μπλε/άσπρο/μαύρο πλαίσιο από πάνω.
                string[] modes = { "App", "Windows", "Dark", "Light", "Transparent" };
                string[] labels = { LocalizationManager.T("Settings.WidgetsThemeFollowApp"), LocalizationManager.T("Settings.WidgetsThemeFollowWindows"), LocalizationManager.T("Settings.WidgetsThemeAlwaysDark"), LocalizationManager.T("Settings.WidgetsThemeAlwaysLight"), LocalizationManager.T("Settings.WidgetsThemeTransparent") };
                var current = AppSettings.Load().WidgetsThemeMode;
                int idx = Array.IndexOf(modes, current); if (idx < 0) idx = 0;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    var a = AppSettings.Load(); a.WidgetsThemeMode = modes[Math.Max(0, combo.SelectedIndex)]; a.Save();
                    ThemeManager.NotifyChanged();
                };
                row.Controls.Add(combo);
                return row;
            }

            // Χειροκίνητο override του HardwareProfileService (v2.0 handoff, Φάση 1 §Α) — "Αυτόματο"
            // αφήνει την ανίχνευση RAM/πυρήνων/GPU να αποφασίσει, οι άλλες επιλογές το παρακάμπτουν
            // μόνιμα (αποθηκεύεται, όχι μόνο για αυτή τη συνεδρία).
            internal static Panel BuildHardwareProfileRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.HardwareProfileLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 260 };
                string[] modes = { "Auto", "Low", "Balanced", "High" };
                string[] labels =
                {
                    LocalizationManager.T("Settings.HardwareProfileAuto"),
                    LocalizationManager.T("Settings.HardwareProfileLow"),
                    LocalizationManager.T("Settings.HardwareProfileBalanced"),
                    LocalizationManager.T("Settings.HardwareProfileHigh"),
                };
                var current = HardwareProfileService.GetOverride()?.ToString() ?? "Auto";
                int idx = Array.IndexOf(modes, current); if (idx < 0) idx = 0;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    var selected = modes[Math.Max(0, combo.SelectedIndex)];
                    HardwareProfileService.SetOverride(selected == "Auto" ? null : Enum.Parse<HardwareTier>(selected));
                };
                row.Controls.Add(combo);
                return row;
            }

            internal static Panel BuildWmvOutputRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WmvOutputLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var pathLabel = new Label { Text = WmvConversionService.CacheDir, AutoSize = false, Width = 380, Height = 24, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, AutoEllipsis = true, Padding = new Padding(0, 4, 0, 0) };
                row.Controls.Add(pathLabel);
                var changeBtn = new HoverButton { Text = LocalizationManager.T("Settings.ChangeFolder"), Width = 100, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.Surface, Cursor = Cursors.Hand };
                changeBtn.FlatAppearance.BorderSize = 0;
                changeBtn.Click += (_, _) =>
                {
                    using var dlg = new FolderBrowserDialog { Description = LocalizationManager.T("Settings.WmvOutputFolderPrompt"), SelectedPath = WmvConversionService.CacheDir };
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        var a = AppSettings.Load(); a.WmvConversionOutputDir = dlg.SelectedPath; a.Save();
                        pathLabel.Text = WmvConversionService.CacheDir;
                    }
                };
                row.Controls.Add(changeBtn);
                return row;
            }

            private static Panel BuildThemeRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.Theme") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var combo = new FlatComboBox { Width = 200 };
                var current = AppSettings.Load().ThemeMode;
                // v2.0.0: + 4 νέα ονομασμένα θέματα (Midnight/Forest/Dawn/Contrast, βλ. UiPalette.Named).
                string[] modes = { "Follow", "Light", "Dark", "Midnight", "Forest", "Dawn", "Contrast" };
                string[] labels = { LocalizationManager.T("Settings.ThemeFollow"), LocalizationManager.T("Settings.ThemeLight"), LocalizationManager.T("Settings.ThemeDark"),
                    "Midnight", "Forest", "Dawn", "High contrast" };
                int idx = Array.IndexOf(modes, current); if (idx < 0) idx = 0;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) => ThemeManager.SetMode(modes[Math.Max(0, combo.SelectedIndex)]);
                row.Controls.Add(combo);
                return row;
            }

            // Πραγματικές emoji σημαίες: χρειάζονται τη γραμματοσειρά "Segoe UI Emoji" για να
            // αποδοθούν ως έγχρωμα εικονίδια αντί για γράμματα περιφερειακού δείκτη (GR/GB) —
            // αυτό ήταν το ζητούμενο πρόβλημα ("δεν έχεις βάλει τις σημαίες στο dropdown").
            private static Panel BuildLanguageRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 10) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.Language") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                // Σημαίες ως πραγματικά bitmap (βλ. FlagIcons.cs) - τα unicode emoji σημαίες δεν αποδίδονται έγχρωμα σε WinForms.
                // 1.7.8: ΟΛΕΣ οι 14 γλώσσες του GearWin + "Follow Windows" (πρώτη θέση).
                var combo = new FlatComboBox { Width = 240 };
                var codes = new List<string> { "Follow" };
                var labels = new List<string> { LocalizationManager.T("Settings.LanguageFollow") };
                var icons = new List<Image> { FlagIcons.Globe() };
                foreach (var (code, _, native) in LocalizationManager.Supported)
                {
                    codes.Add(code);
                    labels.Add(native);
                    icons.Add(FlagIcons.For(code));
                }
                var currentLang = AppSettings.Load().Language;
                var idx = codes.IndexOf(currentLang);
                combo.SetItems(labels, icons, labels[idx < 0 ? 0 : idx]);
                combo.SelectedIndexChanged += (_, _) => LocalizationManager.SetLanguage(codes[combo.SelectedIndex]);
                row.Controls.Add(combo);
                return row;
            }

            // Ζητήθηκε ρητά "ενσωμάτωσε την παρουσίαση ενός media player" (MediaPlayerPro, αδελφό
            // project) στα checkboxes/κουμπιά — FlatCheckBox (custom-σχεδιασμένο, στο ίδιο πνεύμα)
            // αντικαθιστά εδώ το προεπιλεγμένο CheckBox των Windows σε ΟΛΗ τη σελίδα Ρυθμίσεων με
            // μία αλλαγή, αφού κάθε checkbox της σελίδας περνάει από αυτό το ίδιο helper.
            internal static CheckBox Checkbox(string text, bool initial, Action<bool> onChange)
            {
                var box = new FlatCheckBox
                {
                    Text = text,
                    AutoSize = true,
                    Checked = initial,
                    BackColor = UiTheme.Background,
                    Margin = new Padding(0, 4, 0, 4)
                };
                box.CheckedChanged += (_, _) => onChange(box.Checked);
                return box;
            }

            // "Διαφάνεια εφαρμογής" ανάλογη της γνωστής επιλογής διαφάνειας παραθύρων των Windows —
            // εδώ εφαρμόζεται στο ΚΥΡΙΟ παράθυρο μέσω Form.Opacity (πραγματική λειτουργία, όχι
            // απλή προσομοίωση), με ζωντανό preview καθώς σέρνεις το slider.
            private FlowLayoutPanel BuildOpacityRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.WindowOpacityLabel"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                int initial = Math.Clamp((int)Math.Round(AppSettings.Load().WindowOpacity * 100), 60, 100);
                var track = new FlatSlider { Minimum = 60, Maximum = 100, Value = initial, Width = 200, SmallChange = 1, LargeChange = 5 };
                var valueLabel = new Label { Text = $"{initial}%", AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) };
                track.Scroll += (_, _) =>
                {
                    valueLabel.Text = $"{track.Value}%";
                    // Ζωντανή προεπισκόπηση μόνο — η αποθήκευση γίνεται στο άφημα (πριν: εγγραφή αρχείου σε κάθε tick).
                    if (FindForm() is { } form) form.Opacity = track.Value / 100.0;
                };
                void SaveOpacity() { var a = AppSettings.Load(); a.WindowOpacity = track.Value / 100.0; a.Save(); }
                track.MouseUp += (_, _) => SaveOpacity();
                track.KeyUp += (_, _) => SaveOpacity();
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            // Πόσο σκούρο είναι το dark theme — ζητήθηκε ρητά. 0 = πιο ανοιχτό σκούρο, 100 = σχεδόν
            // μαύρο, 50 = η αρχική, αμετάβλητη βάση.
            //
            // Bug fix: πριν, κάθε Scroll tick (δεκάδες/δευτερόλεπτο ενώ σέρνεις) καλούσε απευθείας
            // το ΑΚΡΙΒΟ SetDarkIntensity (save σε δίσκο + πλήρης ανακατασκευή σελίδας/sidebar) —
            // ανέφερε "τρεμόπαιγμα" και "πηγαίνει κάθε 5%" επειδή το ίδιο το TrackBar καταστρεφόταν
            // και ξαναφτιαχνόταν ενώ ο χρήστης ακόμη το έσερνε. Τώρα: Scroll κάνει μόνο ΦΤΗΝΗ
            // προεπισκόπηση (PreviewDarkIntensity — Invalidate, καμία ανακατασκευή), και η ακριβή
            // εφαρμογή/αποθήκευση γίνεται ΜΙΑ φορά στο MouseUp (όταν αφήνει το slider).
            private FlowLayoutPanel BuildDarkIntensityRow()
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Settings.DarkIntensity") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                int initial = Math.Clamp((int)Math.Round(AppSettings.Load().DarkIntensity * 100), 0, 100);
                var track = new FlatSlider { Minimum = 0, Maximum = 100, Value = initial, Width = 200, SmallChange = 1, LargeChange = 5 };
                var valueLabel = new Label { Text = $"{initial}%", AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) };
                track.Scroll += (_, _) =>
                {
                    valueLabel.Text = $"{track.Value}%";
                    ThemeManager.PreviewDarkIntensity(track.Value / 100.0);
                };
                track.MouseUp += (_, _) => ThemeManager.SetDarkIntensity(track.Value / 100.0);
                track.KeyUp += (_, _) => ThemeManager.SetDarkIntensity(track.Value / 100.0); // πληκτρολόγιο/βελάκια: δεν έχουν MouseUp
                row.Controls.Add(track);
                row.Controls.Add(valueLabel);
                return row;
            }

            internal static FlowLayoutPanel NumericRow(string label, int value, int min, int max, Action<int> onChange, int marginLeft = 0, string unit = "px")
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(marginLeft, 2, 0, 4) };
                row.Controls.Add(new Label { Text = label + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 8, 0) });
                var numeric = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 80 };
                numeric.ValueChanged += (_, _) => onChange((int)numeric.Value);
                row.Controls.Add(numeric);
                row.Controls.Add(new Label { Text = unit, AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Padding = new Padding(8, 6, 0, 0) });
                return row;
            }
        }

        private static Icon LoadApplicationIcon()
        {
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Opsis.ico");
            return File.Exists(path) ? new Icon(path) : SystemIcons.Application;
        }
    }
}
