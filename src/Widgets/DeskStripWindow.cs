using MotionDesk.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    // "DeskStrip" — πλωτό dock συντομεύσεων στο κάτω-κέντρο της κύριας οθόνης, στο πνεύμα του
    // RocketDock/Winstep Nexus/ObjectDock/SeelenUI toolbar. Ίδιο βασικό μοτίβο owner-draw Form
    // χωρίς περίγραμμα με το DeskContainerWindow (ίδιο glass-blur effect, ίδιο DragDrop pattern,
    // ίδιο JSON persistence στο %AppData%). v1: πάντα ορατό (χωρίς edge-triggered auto-hide ακόμα —
    // σκόπιμα εκτός scope αυτού του περάσματος, βλ. roadmap), με μεγέθυνση εικονιδίου στο hover και
    // live ένδειξη αν η καρφιτσωμένη εφαρμογή τρέχει αυτή τη στιγμή.
    public sealed class DeskStripWindow : Form
    {
        private const int TileSize = 48;
        private const int TileGap = 10;
        private const int BarHeight = 72;

        private readonly List<string> _paths = new();
        // Paths που πρέπει να παίξουν "entrance pop" animation στο επόμενο RebuildTray — μόνο οι
        // ΝΕΕΣ προσθήκες (pin/drop), όχι reorder/αρχική φόρτωση, ώστε να μην "ξαναπαίζει" η
        // είσοδος όλων των ήδη υπαρχόντων πλακιδίων κάθε φορά που το tray ξαναχτίζεται.
        private readonly HashSet<string> _pendingEntrance = new(StringComparer.OrdinalIgnoreCase);
        private readonly FlowLayoutPanel _tray;
        private readonly System.Windows.Forms.Timer _saveTimer;
        private readonly System.Windows.Forms.Timer _runningCheckTimer;
        private System.Windows.Forms.Timer? _resizeAnimTimer;
        private bool _shown;

        public DeskStripWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(20, 20, 20);
            Height = BarHeight;
            AllowDrop = true;

            _tray = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Padding = new Padding(10, 8, 10, 8),
                AllowDrop = true
            };
            _tray.DragEnter += Tray_DragEnter;
            _tray.DragDrop += Tray_DragDrop;
            Controls.Add(_tray);
            DragEnter += Tray_DragEnter;
            DragDrop += Tray_DragDrop;

            _saveTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };

            _runningCheckTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _runningCheckTimer.Tick += (_, _) => RefreshRunningIndicators();
            _runningCheckTimer.Start();

            FormClosed += (_, _) =>
            {
                _saveTimer.Stop(); _saveTimer.Dispose();
                _runningCheckTimer.Stop(); _runningCheckTimer.Dispose();
                _resizeAnimTimer?.Stop(); _resizeAnimTimer?.Dispose();
                SaveState();
            };

            LoadState();
            RebuildTray();
            // animate:false — η πρώτη τοποθέτηση δεν πρέπει να "ξεκινήσει" από 0/προεπιλεγμένο
            // μέγεθος, μόνο οι ΜΕΤΑΓΕΝΕΣΤΕΡΕΣ αλλαγές (προσθήκη/αφαίρεση εφαρμογής) ζητήθηκε να
            // είναι ομαλές.
            RepositionToBottomCenter(animate: false);
            _shown = true;

            // Η περιοχή εργασίας αλλάζει όταν μια άλλη εφαρμογή δεσμεύει χώρο (π.χ. η μπάρα του News Crawler ως AppBar)
            // ή αλλάζουν οι οθόνες — ξανατοποθετούμαστε ώστε να μην πέφτουμε πάνω της.
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWorkAreaMayHaveChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnWorkAreaMayHaveChanged;
            Disposed += (_, _) =>
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnWorkAreaMayHaveChanged;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnWorkAreaMayHaveChanged;
            };
        }

        private void OnWorkAreaMayHaveChanged(object? sender, EventArgs e)
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => RepositionToBottomCenter(animate: false)));
        }

        // ΔΙΟΡΘΩΣΗ "δεν κάνει ομαλή επέκταση η μπάρα": η προηγούμενη εκδοχή άλλαζε Width/Location
        // απευθείας — στιγμιαίο "άλμα". Τώρα το νέο μέγεθος/θέση υπολογίζεται κανονικά, αλλά
        // εφαρμόζεται μέσω του ίδιου ease-out-cubic animation pattern που ήδη χρησιμοποιεί το
        // roll-up του DeskContainer (20 βήματα/10ms). Το Height δεν αλλάζει ποτέ εδώ — μόνο
        // Width/X, γι' αυτό υπολογίζεται μία φορά και μένει σταθερό στο animation.
        private void RepositionToBottomCenter(bool animate = true)
        {
            var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            int targetWidth = Math.Max(160, _paths.Count * (TileSize + TileGap) + 40);
            int targetX = area.X + (area.Width - targetWidth) / 2;
            int targetY = area.Bottom - Height - 8;

            if (!animate || !_shown || !IsHandleCreated)
            {
                _resizeAnimTimer?.Stop();
                Width = targetWidth;
                Location = new Point(targetX, targetY);
                return;
            }

            _resizeAnimTimer?.Stop();
            _resizeAnimTimer?.Dispose();
            int startWidth = Width;
            int startX = Location.X;
            const int totalSteps = 16;
            int step = 0;
            _resizeAnimTimer = new System.Windows.Forms.Timer { Interval = 10 };
            _resizeAnimTimer.Tick += (_, _) =>
            {
                step++;
                double t = Math.Min(1.0, step / (double)totalSteps);
                double eased = 1 - Math.Pow(1 - t, 3);
                Width = (int)(startWidth + (targetWidth - startWidth) * eased);
                Location = new Point((int)(startX + (targetX - startX) * eased), targetY);
                if (t >= 1.0)
                {
                    Width = targetWidth;
                    Location = new Point(targetX, targetY);
                    _resizeAnimTimer?.Stop();
                }
            };
            _resizeAnimTimer.Start();
        }

        private void Tray_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void Tray_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
        }

        // Κοινή λογική "πρόσθεσε αυτά τα αρχεία στο dock" — καλείται ΚΑΙ όταν η απόθεση γίνεται
        // στο κενό (_tray/Form) ΚΑΙ όταν γίνεται πάνω σε ένα ήδη υπάρχον πλακίδιο (βλ. DockTile πιο
        // κάτω — ΔΙΟΡΘΩΣΗ του "δεν γίνεται drag&drop": τα πλακίδια καλύπτουν σχεδόν όλο το πλάτος
        // της μπάρας μόλις υπάρχουν λίγες καρφιτσωμένες εφαρμογές, οπότε το παλιό "μόνο το κενό
        // δέχεται drop" σήμαινε ότι σχεδόν ΠΟΤΕ δεν υπήρχε αρκετός χώρος να πετύχει η απόθεση).
        private void AddFiles(string[] files)
        {
            bool changed = false;
            foreach (var f in files)
                if (!_paths.Contains(f, StringComparer.OrdinalIgnoreCase)) { _paths.Add(f); _pendingEntrance.Add(f); changed = true; }
            if (changed) { RebuildTray(); RepositionToBottomCenter(); QueueSave(); }
        }

        private void RebuildTray()
        {
            _tray.SuspendLayout();
            foreach (Control old in _tray.Controls.Cast<Control>().ToList()) old.Dispose(); // Clear() δεν κάνει Dispose
            _tray.Controls.Clear();
            foreach (var path in _paths.ToArray())
                _tray.Controls.Add(new DockTile(path, RemovePath, ReorderPath, AddFiles, playEntrance: _pendingEntrance.Contains(path)));
            _tray.ResumeLayout();
            _pendingEntrance.Clear();
        }

        private void RemovePath(string path)
        {
            if (_paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                RebuildTray();
                RepositionToBottomCenter();
                QueueSave();
            }
        }

        // Αναδιάταξη με σύρσιμο ενός πλακιδίου πάνω σε άλλο (ζητήθηκε ρητά ως μέρος του
        // "drag&drop δεν δουλεύει" / "όπως σε άλλες εφαρμογές") — αφαιρεί το source από τη θέση
        // του και το ξαναβάζει ακριβώς πριν από το target, idempotent αν τύχει να κληθεί με την
        // ίδια θέση.
        private void ReorderPath(string sourcePath, string targetPath)
        {
            int sourceIdx = _paths.FindIndex(p => string.Equals(p, sourcePath, StringComparison.OrdinalIgnoreCase));
            if (sourceIdx < 0 || string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase)) return;
            _paths.RemoveAt(sourceIdx);
            int insertAt = _paths.FindIndex(p => string.Equals(p, targetPath, StringComparison.OrdinalIgnoreCase));
            _paths.Insert(insertAt < 0 ? _paths.Count : insertAt, sourcePath);
            RebuildTray();
            QueueSave();
        }

        public void PinApp(string path)
        {
            if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
            _paths.Add(path);
            _pendingEntrance.Add(path);
            RebuildTray();
            RepositionToBottomCenter();
            QueueSave();
        }

        public void UnpinApp(string path) => RemovePath(path);

        public IReadOnlyList<string> PinnedApps => _paths;

        private void RefreshRunningIndicators()
        {
            foreach (Control c in _tray.Controls)
                if (c is DockTile tile) tile.RefreshRunningState();
        }

        private void QueueSave() { _saveTimer.Stop(); _saveTimer.Start(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyGlassBlur();
        }

        private void ApplyGlassBlur()
        {
            var accent = new ACCENT_POLICY { AccentState = 3, GradientColor = unchecked((int)0x99202020) };
            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, accentPtr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, SizeOfData = size, Data = accentPtr };
            SetWindowCompositionAttribute(Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var path = MotionDesk.UI.UiTheme.RoundedPath(ClientRectangle, 16);
            using var pen = new Pen(Color.FromArgb(60, 255, 255, 255));
            e.Graphics.DrawPath(pen, path);
        }

        private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "deskstrip", "config.json");

        private sealed class DeskStripState { public List<string> Paths { get; set; } = new(); }

        private void SaveState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                MotionDesk.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new DeskStripState { Paths = _paths }));
            }
            catch (IOException) { }
        }

        private void LoadState()
        {
            if (!File.Exists(ConfigPath))
            {
                // Πρώτη εκκίνηση, χωρίς αποθηκευμένη κατάσταση ακόμα — ζητήθηκε ρητά "όταν
                // εμφανίζεται από προεπιλογή να έχει τις εφαρμογές που είναι καρφιτσωμένες στην
                // taskbar". Αν δεν βρεθεί τίποτα (π.χ. καμία κλασική .lnk καρφίτσωμα), το dock
                // απλά ξεκινάει άδειο όπως πριν — καμία αλλαγή συμπεριφοράς σε αυτή την περίπτωση.
                _paths.AddRange(TaskbarPinsReader.GetPinnedAppPaths());
                return;
            }
            try
            {
                var state = JsonSerializer.Deserialize<DeskStripState>(MotionDesk.Services.AtomicFile.ReadAllText(ConfigPath));
                if (state?.Paths != null) _paths.AddRange(state.Paths.Where(p => File.Exists(p)));
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    }

    // Owner-draw πλακίδιο dock: μεγεθύνεται ομαλά (όχι πλέον απότομο jump) στο hover, "pop in" όταν
    // μόλις καρφιτσωθεί, μικρό "bounce" στην εκκίνηση της εφαρμογής, και η πράσινη τελεία "τρέχει
    // τώρα" κάνει ομαλό fade αντί να εμφανίζεται/εξαφανίζεται απότομα — όλα μέσω ενός κοινού,
    // ελαφρού per-tile animation timer που σταματάει μόνος του όταν δεν υπάρχει τίποτα να
    // ζωγραφιστεί (μηδενικό CPU cost στην ήσυχη κατάσταση). Διπλό-κλικ = εκκίνηση/BringToFront αν
    // ήδη τρέχει, δεξί-κλικ = αφαίρεση, σύρσιμο = αναδιάταξη ή αποδοχή νέου αρχείου.
    internal sealed class DockTile : Panel
    {
        private readonly string _path;
        private readonly Action<string> _onRemove;
        private readonly Action<string, string> _onReorder;
        private readonly Action<string[]> _onExternalFilesDropped;
        private readonly Icon? _icon;
        private bool _hover;
        private bool _running;

        // --- animated state (όλα 0..1 εκτός από runningDotAlpha, 0..230) ---
        private double _hoverProgress;      // 0 = κανονικό μέγεθος, 1 = πλήρως μεγεθυμένο
        private double _entranceProgress;   // 0 = μόλις μπήκε (μικρό/διάφανο), 1 = πλήρες μέγεθος
        private double _bounceProgress = 1; // 1 = ηρεμία· 0 ξεκινάει ένα πέρασμα bounce
        private double _runningDotAlpha;
        private double _runningDotTarget;
        private System.Windows.Forms.Timer? _animTimer;

        private Point _dragStart;
        private bool _dragArmed;

        public DockTile(string path, Action<string> onRemove, Action<string, string> onReorder, Action<string[]> onExternalFilesDropped, bool playEntrance)
        {
            _path = path;
            _onRemove = onRemove;
            _onReorder = onReorder;
            _onExternalFilesDropped = onExternalFilesDropped;
            Size = new Size(56, 64);
            Margin = new Padding(2, 0, 2, 0);
            Cursor = Cursors.Hand;
            AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            try { _icon = Icon.ExtractAssociatedIcon(path); } catch (Exception) { _icon = null; }

            _entranceProgress = playEntrance ? 0 : 1;
            if (playEntrance) EnsureAnimTimer();

            var tip = new ToolTip { InitialDelay = 300, AutoPopDelay = 4000 };
            tip.SetToolTip(this, Path.GetFileNameWithoutExtension(path));

            MouseEnter += (_, _) => { _hover = true; EnsureAnimTimer(); };
            MouseLeave += (_, _) => { _hover = false; EnsureAnimTimer(); };
            DoubleClick += (_, _) => Launch();

            MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _dragStart = e.Location; _dragArmed = true; } };
            MouseMove += (_, e) =>
            {
                if (!_dragArmed || e.Button != MouseButtons.Left) return;
                if (Math.Abs(e.X - _dragStart.X) < SystemInformation.DragSize.Width &&
                    Math.Abs(e.Y - _dragStart.Y) < SystemInformation.DragSize.Height) return;
                _dragArmed = false;
                DoDragDrop(new DataObject(ReorderFormat, _path), DragDropEffects.Move);
            };
            MouseUp += (_, e) =>
            {
                _dragArmed = false;
                if (e.Button == MouseButtons.Right)
                {
                    var menu = new ContextMenuStrip();
                    menu.Items.Add(LocalizationManager.T("DCMenu.Open"), null, (_, _) => Launch());
                    menu.Items.Add(LocalizationManager.T("DCMenu.Unpin"), null, (_, _) => _onRemove(_path));
                    menu.Show(this, e.Location);
                }
            };

            // ΔΙΟΡΘΩΣΗ "δεν γίνεται drag&drop": πριν, μόνο το _tray/Form δέχονταν απόθεση — αφού τα
            // πλακίδια καλύπτουν σχεδόν όλη την μπάρα μόλις υπάρχουν λίγες εφαρμογές, ένα drop
            // πάνω σε οποιοδήποτε ήδη υπάρχον πλακίδιο δεν έκανε τίποτα. Τώρα δέχεται ΚΑΙ εξωτερικά
            // αρχεία (pin) ΚΑΙ εσωτερικό σύρσιμο άλλου πλακιδίου (reorder).
            DragEnter += (_, e) =>
            {
                if (e.Data?.GetDataPresent(ReorderFormat) == true) e.Effect = DragDropEffects.Move;
                else if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
                else e.Effect = DragDropEffects.None;
            };
            DragDrop += (_, e) =>
            {
                if (e.Data?.GetDataPresent(ReorderFormat) == true)
                {
                    if (e.Data.GetData(ReorderFormat) is string sourcePath) _onReorder(sourcePath, _path);
                }
                else if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) _onExternalFilesDropped(files);
            };

            RefreshRunningState();
        }

        private const string ReorderFormat = "application/x-opsis-deskstrip-tile";

        public void RefreshRunningState()
        {
            bool wasRunning = _running;
            _running = IsProcessRunningForPath(_path);
            if (_running != wasRunning)
            {
                _runningDotTarget = _running ? 230 : 0;
                EnsureAnimTimer();
            }
        }

        private static bool IsProcessRunningForPath(string path)
        {
            try
            {
                return RunningProcessCache.IsRunningByPath(Path.GetFullPath(path));
            }
            catch (Exception) { }
            return false;
        }

        private void Launch()
        {
            // Μικρό "bounce" στην εκκίνηση — οπτική επιβεβαίωση ότι το κλικ "έπιασε", όπως στο
            // Windows taskbar/macOS Dock.
            _bounceProgress = 0;
            EnsureAnimTimer();
            try { Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        // Ένας μόνο, ελαφρύς per-tile timer (60fps) που τρέχει ΜΟΝΟ όσο υπάρχει κάτι να
        // ζωγραφιστεί (hover transition, entrance pop, bounce, ή fade της πράσινης τελείας) — όχι
        // ένας μόνιμος timer ανά πλακίδιο, ώστε πολλά καρφιτσωμένα παράθυρα σε ηρεμία να μην
        // κοστίζουν τίποτα.
        private void EnsureAnimTimer()
        {
            if (_animTimer != null) { _animTimer.Start(); return; }
            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (_, _) =>
            {
                bool active = false;

                double hoverTarget = _hover ? 1 : 0;
                if (Math.Abs(_hoverProgress - hoverTarget) > 0.01) { _hoverProgress += (hoverTarget - _hoverProgress) * 0.35; active = true; }
                else _hoverProgress = hoverTarget;

                if (_entranceProgress < 1) { _entranceProgress = Math.Min(1, _entranceProgress + 0.14); active = true; }

                if (_bounceProgress < 1) { _bounceProgress = Math.Min(1, _bounceProgress + 0.08); active = true; }

                if (Math.Abs(_runningDotAlpha - _runningDotTarget) > 1) { _runningDotAlpha += (_runningDotTarget - _runningDotAlpha) * 0.3; active = true; }
                else _runningDotAlpha = _runningDotTarget;

                Invalidate();
                if (!active) _animTimer?.Stop();
            };
            _animTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Ease-out στο entrance (ώστε η "γέννηση" του πλακιδίου να μην είναι γραμμική) — το ίδιο
            // cubic easing με τα υπόλοιπα animations στο project.
            double entranceEased = 1 - Math.Pow(1 - _entranceProgress, 3);

            double baseIconSize = 30 + 6 * _hoverProgress;        // 30 -> 36, ομαλά αντί για jump
            int iconSize = Math.Max(1, (int)Math.Round(baseIconSize * entranceEased));
            int iconX = (Width - iconSize) / 2;
            double baseIconY = 6 - 4 * _hoverProgress;            // 6 -> 2, ομαλά
            // Bounce: ένα μοναδικό τόξο προς τα πάνω-και-πίσω (ημιτονοειδές) πάνω από τη βασική θέση.
            double bounceOffset = _bounceProgress < 1 ? -6.0 * Math.Sin(_bounceProgress * Math.PI) : 0;
            int iconY = (int)Math.Round(baseIconY + bounceOffset);

            if (_hoverProgress > 0.01)
            {
                using var bg = new SolidBrush(Color.FromArgb((int)(50 * _hoverProgress), 255, 255, 255));
                using var bgPath = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(4, 0, Width - 8, Width - 8), 10);
                g.FillPath(bg, bgPath);
            }

            if (_icon != null && iconSize > 0) g.DrawIcon(_icon, new Rectangle(iconX, iconY, iconSize, iconSize));

            if (_runningDotAlpha > 1)
            {
                using var dot = new SolidBrush(Color.FromArgb((int)_runningDotAlpha, 90, 220, 140));
                g.FillEllipse(dot, Width / 2 - 3, Height - 8, 6, 6);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _icon?.Dispose();
                _animTimer?.Stop();
                _animTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public sealed class DeskStripHostEngine
    {
        private static DeskStripHostEngine? _instance;
        public static DeskStripHostEngine Instance => _instance ??= new DeskStripHostEngine();

        private DeskStripWindow? _window;
        public bool IsEnabled => _window is { IsDisposed: false };

        public void Enable()
        {
            if (IsEnabled) return;
            _window = new DeskStripWindow();
            _window.FormClosed += (_, _) => _window = null;
            _window.Show();
        }

        public void Disable()
        {
            if (_window is { IsDisposed: false }) _window.Close();
            _window = null;
        }

        public void PinApp(string path)
        {
            if (!IsEnabled) Enable();
            _window?.PinApp(path);
        }

        public void UnpinApp(string path) => _window?.UnpinApp(path);

        public IReadOnlyList<string> GetPinnedApps() => _window?.PinnedApps ?? Array.Empty<string>();
    }
}
