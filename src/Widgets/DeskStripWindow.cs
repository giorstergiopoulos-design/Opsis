using MotionDesk.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    public enum DockPosition { Bottom, Top, Left, Right }

    /// <summary>Ρυθμίσεις του DeskStrip (αποθηκεύονται μαζί με τα καρφιτσωμένα στο deskstrip\config.json).</summary>
    public sealed class DeskStripSettings
    {
        public List<string> Paths { get; set; } = new();
        public DockPosition Position { get; set; } = DockPosition.Bottom;
        public bool AutoHide { get; set; } = false;
        public int IconSize { get; set; } = 44;               // 28..72
        public bool Magnify { get; set; } = true;
        public bool ShowRunning { get; set; } = true;          // τρέχουσες (μη καρφιτσωμένες) εφαρμογές στο dock
        public bool ShowPreviews { get; set; } = true;         // προεπισκόπηση παραθύρου στο hover
    }

    // "DeskStrip" — dock εφαρμογών στο πνεύμα του Weg του SeelenUI: καρφιτσωμένες + ΤΡΕΧΟΥΣΕΣ εφαρμογές με ένδειξη
    // ενεργού παραθύρου, μεγέθυνση στο hover, προεπισκόπηση παραθύρου (DWM thumbnail), auto-hide από οποιαδήποτε ακμή,
    // μενού δεξιού κλικ (άνοιγμα, διαχειριστής, τοποθεσία αρχείου, κλείσιμο, pin), σύρσιμο για αναδιάταξη και απόθεση αρχείων.
    // ΕΝΑ custom-painted παράθυρο (όχι ένα control ανά εικονίδιο): ένας μόνο animation timer που τρέχει ΜΟΝΟ όσο υπάρχει κίνηση.
    // Σε "Low" hardware tier: χωρίς μεγέθυνση/προεπισκοπήσεις και πιο αραιό polling — για αδύναμα συστήματα.
    public sealed class DeskStripWindow : Form
    {
        // ---- δεδομένα ---------------------------------------------------------------------------------------------
        private sealed class Item
        {
            public string Key = "";                 // διαδρομή καρφιτσώματος ή exe διαδρομή
            public string Display = "";
            public string? Target;                  // πραγματικό exe (λύνει .lnk)
            public bool Pinned;
            public List<IntPtr> Windows = new();
            public Bitmap? Icon;
            public double Scale = 1, Entrance = 1;
            public double Dot;                      // 0..1 fade της ένδειξης
            public bool Active;
            public string Title = "";
        }

        private readonly DeskStripSettings _s = new();
        private readonly List<Item> _items = new();
        private readonly Dictionary<string, string?> _lnkCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Bitmap?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingEntrance = new(StringComparer.OrdinalIgnoreCase);

        private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 350 };
        private readonly System.Windows.Forms.Timer _pollTimer;                 // τρέχουσες εφαρμογές + ενεργό παράθυρο
        private readonly System.Windows.Forms.Timer _animTimer = new();         // μόνο όσο υπάρχει κίνηση
        private readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 120 };
        private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 380 };
        private DockPopup? _popup;

        private PointF? _mouse;
        private int _hoverIndex = -1;
        private int _dropIndex = -1;
        private Point _dragStart; private int _dragIndex = -1;
        private bool _shown, _hiddenByAutoHide, _hiddenByFullscreen;
        private DateTime _leftAt = DateTime.MinValue;
        private Point _slideFrom, _slideTo; private double _slideT = 1;
        private bool _dragActive, _contextOpen;

        private const string ReorderFormat = "application/x-opsis-deskstrip-item";
        private bool Low => HardwareProfileService.CurrentTier == HardwareTier.Low;
        private bool Vertical => _s.Position is DockPosition.Left or DockPosition.Right;
        private double Cell => _s.IconSize * 1.32;
        private double MaxScale => _s.Magnify && !Low ? 1.5 : 1.0;
        private const int Pad = 10, IndicatorSpace = 12, Gap = 10;

        public DeskStripWindow()
        {
            MotionDesk.UI.Flicker.Attach(this);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(20, 20, 20);
            AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };
            _animTimer.Interval = Low ? 33 : 16;
            _animTimer.Tick += (_, _) => AnimateStep();
            _pollTimer = new System.Windows.Forms.Timer { Interval = Low ? 3500 : 1500 };
            _pollTimer.Tick += (_, _) => PollTick();
            _hideTimer.Tick += (_, _) => AutoHideTick();
            _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); ShowPopupForHover(); };

            LoadState();
            SyncItems();
            ApplyLayout(animate: false);
            _pollTimer.Start();
            if (_s.AutoHide) _hideTimer.Start();
            _shown = true;

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnScreenChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnScreenChanged;
            FormClosed += (_, _) =>
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnScreenChanged;
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnScreenChanged;
                _saveTimer.Stop(); _pollTimer.Stop(); _animTimer.Stop(); _hideTimer.Stop(); _hoverTimer.Stop();
                _popup?.Close();
                SaveState();
                foreach (var t in new[] { _saveTimer, _pollTimer, _animTimer, _hideTimer, _hoverTimer }) t.Dispose();
                foreach (var b in _iconCache.Values) b?.Dispose();
            };
        }

        public DeskStripSettings Settings => _s;
        public IReadOnlyList<string> PinnedApps => _s.Paths;

        /// <summary>Αλλαγή ρυθμίσεων από το UI: εφαρμόζεται αμέσως και αποθηκεύεται.</summary>
        public void UpdateSettings(Action<DeskStripSettings> change)
        {
            var oldPos = _s.Position; var oldSize = _s.IconSize;
            change(_s);
            _s.IconSize = Math.Clamp(_s.IconSize, 28, 72);
            _s.Paths = _s.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (_s.IconSize != oldSize) { foreach (var b in _iconCache.Values) b?.Dispose(); _iconCache.Clear(); foreach (var it in _items) it.Icon = null; }
            if (_s.AutoHide) _hideTimer.Start(); else { _hideTimer.Stop(); _hiddenByAutoHide = false; }
            SyncItems();
            ApplyLayout(animate: oldPos == _s.Position);
            QueueSave();
        }

        private void OnScreenChanged(object? sender, EventArgs e)
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => ApplyLayout(animate: false)));
        }

        // ---- ανίχνευση παραθύρων ----------------------------------------------------------------------------------
        private void PollTick()
        {
            bool fs = AppActivity.IsFullscreenAppActive;
            if (fs != _hiddenByFullscreen) { _hiddenByFullscreen = fs; Visible = !fs; if (!fs) Invalidate(); }
            if (fs) return;
            if (SyncItems()) ApplyLayout(animate: true);
            else UpdateActiveFlags();
        }

        /// <summary>Συγχρονίζει τη λίστα (καρφιτσωμένα + τρέχοντα). Επιστρέφει true αν άλλαξε το πλήθος των στοιχείων.</summary>
        private bool SyncItems()
        {
            var windows = _s.ShowRunning || _s.Paths.Count > 0 ? WinApi.EnumerateAppWindows() : new List<WinApi.AppWindow>();
            int before = _items.Count;
            var previous = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in _items) previous[i.Key] = i;
            var list = new List<Item>();

            foreach (var path in _s.Paths)
            {
                if (!previous.TryGetValue(path, out var it) || !it.Pinned)
                    it = new Item { Key = path, Pinned = true, Display = Path.GetFileNameWithoutExtension(path), Entrance = _pendingEntrance.Contains(path) ? 0 : 1 };
                it.Target ??= ResolveTarget(path);
                it.Icon ??= IconFor(path);
                it.Windows = new List<IntPtr>();
                list.Add(it);
            }
            foreach (var w in windows)
            {
                var pinned = list.FirstOrDefault(i => i.Pinned && SameApp(i, w.ExePath));
                if (pinned != null) { pinned.Windows.Add(w.Handle); pinned.Title = w.Title; continue; }
                if (!_s.ShowRunning) continue;
                // Οι UWP εφαρμογές (Αριθμομηχανή, Ρυθμίσεις...) τρέχουν όλες μέσα στο ApplicationFrameHost.exe: κάθε παράθυρο είναι δική του εφαρμογή.
                bool uwp = Path.GetFileName(w.ExePath).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
                string key = uwp ? w.ExePath + "#" + w.Handle.ToInt64() : w.ExePath;
                var run = list.FirstOrDefault(i => !i.Pinned && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
                if (run == null)
                {
                    if (!previous.TryGetValue(key, out run) || run.Pinned)
                        run = new Item { Key = key, Pinned = false, Target = w.ExePath, Entrance = 0 };
                    run.Display = uwp ? w.Title : Path.GetFileNameWithoutExtension(w.ExePath);
                    run.Windows = new List<IntPtr>();
                    run.Icon ??= uwp ? WinApi.WindowIcon(w.Handle) ?? IconFor(w.ExePath) : IconFor(w.ExePath) ?? WinApi.WindowIcon(w.Handle);
                    list.Add(run);
                }
                run.Windows.Add(w.Handle); run.Title = w.Title;
            }
            _pendingEntrance.Clear();
            _items.Clear(); _items.AddRange(list);
            UpdateActiveFlags();
            return _items.Count != before;
        }

        private static bool SameApp(Item item, string exe) =>
            string.Equals(item.Target, exe, StringComparison.OrdinalIgnoreCase) || string.Equals(item.Key, exe, StringComparison.OrdinalIgnoreCase);

        private void UpdateActiveFlags()
        {
            IntPtr fg = WinApi.GetForegroundWindow();
            bool changed = false;
            foreach (var i in _items)
            {
                bool active = i.Windows.Contains(fg);
                double target = i.Windows.Count > 0 ? 1 : 0;
                if (active != i.Active || Math.Abs(i.Dot - target) > 0.01) { i.Active = active; changed = true; }
            }
            if (changed) StartAnim();
        }

        private string? ResolveTarget(string path)
        {
            if (_lnkCache.TryGetValue(path, out var cached)) return cached;
            string? target = path;
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                target = null;
                try
                {
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                    dynamic lnk = shell.CreateShortcut(path);
                    string t = lnk.TargetPath;
                    if (!string.IsNullOrWhiteSpace(t)) target = t;
                }
                catch (Exception) { }
            }
            _lnkCache[path] = target;
            return target;
        }

        // ---- εικονίδια --------------------------------------------------------------------------------------------
        private Bitmap? IconFor(string path)
        {
            if (_iconCache.TryGetValue(path, out var c)) return c;
            Bitmap? bmp = null;
            try
            {
                int size = Math.Max(64, (int)(_s.IconSize * 1.6));
                string source = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? (ResolveTarget(path) ?? path) : path;
                if (File.Exists(source) && (source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)))
                {
                    using var big = Icon.ExtractIcon(source, 0, size);
                    if (big != null) bmp = big.ToBitmap();
                }
                if (bmp == null && (File.Exists(path) || Directory.Exists(path)))
                {
                    using var ic = Icon.ExtractAssociatedIcon(path);
                    if (ic != null) bmp = ic.ToBitmap();
                }
            }
            catch (Exception) { }
            _iconCache[path] = bmp;
            return bmp;
        }

        // ---- διάταξη ----------------------------------------------------------------------------------------------
        private int SeparatorIndex() => _items.FindLastIndex(i => i.Pinned) + 1;      // πρώτο μη καρφιτσωμένο
        private bool HasSeparator => _items.Any(i => i.Pinned) && _items.Any(i => !i.Pinned);

        private double AxisLength() => Pad * 2 + _items.Count * Cell + (HasSeparator ? Gap : 0);
        private double Thickness() => Pad + _s.IconSize * MaxScale + IndicatorSpace + Pad;

        private double CenterOf(int i)
        {
            double c = Pad + Cell * (i + 0.5);
            if (HasSeparator && i >= SeparatorIndex()) c += Gap;
            return c;
        }

        private Rectangle TargetBounds(bool hidden)
        {
            var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            int len = (int)Math.Max(Cell * 2 + Pad * 2, AxisLength()), th = (int)Thickness();
            const int margin = 8;
            Rectangle r = _s.Position switch
            {
                DockPosition.Bottom => new Rectangle(area.X + (area.Width - len) / 2, area.Bottom - th - margin, len, th),
                DockPosition.Top => new Rectangle(area.X + (area.Width - len) / 2, area.Y + margin, len, th),
                DockPosition.Left => new Rectangle(area.X + margin, area.Y + (area.Height - len) / 2, th, len),
                _ => new Rectangle(area.Right - th - margin, area.Y + (area.Height - len) / 2, th, len),
            };
            if (!hidden) return r;
            const int sliver = 3;
            return _s.Position switch
            {
                DockPosition.Bottom => new Rectangle(r.X, area.Bottom - sliver, r.Width, r.Height),
                DockPosition.Top => new Rectangle(r.X, area.Y + sliver - r.Height, r.Width, r.Height),
                DockPosition.Left => new Rectangle(area.X + sliver - r.Width, r.Y, r.Width, r.Height),
                _ => new Rectangle(area.Right - sliver, r.Y, r.Width, r.Height),
            };
        }

        private void ApplyLayout(bool animate)
        {
            var r = TargetBounds(_hiddenByAutoHide);
            Size = r.Size;
            UpdateRegion();
            if (!animate || !_shown || !IsHandleCreated) { _slideT = 1; Location = r.Location; Invalidate(); return; }
            _slideFrom = Location; _slideTo = r.Location; _slideT = 0;
            StartAnim();
        }

        private void UpdateRegion()
        {
            try { using var p = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), 20); Region = new Region(p); } catch (Exception) { }
        }

        // ---- animation --------------------------------------------------------------------------------------------
        private void StartAnim() { if (!_animTimer.Enabled) _animTimer.Start(); }

        private void AnimateStep()
        {
            bool active = false;
            if (_slideT < 1)
            {
                _slideT = Math.Min(1, _slideT + (Low ? 0.2 : 0.12));
                double e = 1 - Math.Pow(1 - _slideT, 3);
                Location = new Point((int)(_slideFrom.X + (_slideTo.X - _slideFrom.X) * e), (int)(_slideFrom.Y + (_slideTo.Y - _slideFrom.Y) * e));
                active = true;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                double target = TargetScale(i);
                if (Math.Abs(it.Scale - target) > 0.004) { it.Scale += (target - it.Scale) * (Low ? 0.5 : 0.35); active = true; } else it.Scale = target;
                if (it.Entrance < 1) { it.Entrance = Math.Min(1, it.Entrance + 0.12); active = true; }
                double dot = it.Windows.Count > 0 ? 1 : 0;
                if (Math.Abs(it.Dot - dot) > 0.02) { it.Dot += (dot - it.Dot) * 0.3; active = true; } else it.Dot = dot;
            }
            Invalidate();
            if (!active && _mouse == null) _animTimer.Stop();
        }

        private double TargetScale(int i)
        {
            if (_mouse == null || MaxScale <= 1.0) return 1;
            double axis = Vertical ? _mouse.Value.Y : _mouse.Value.X;
            double d = axis - CenterOf(i), sigma = Cell * 1.05;
            return 1 + (MaxScale - 1) * Math.Exp(-(d * d) / (2 * sigma * sigma));
        }

        // ---- auto-hide --------------------------------------------------------------------------------------------
        private void AutoHideTick()
        {
            if (!_s.AutoHide || _hiddenByFullscreen) return;
            var p = Cursor.Position;
            var shown = TargetBounds(false);
            var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
            bool inDock = shown.Contains(p);
            bool atEdge = _s.Position switch
            {
                DockPosition.Bottom => p.Y >= area.Bottom - 2 && p.X >= shown.Left && p.X <= shown.Right,
                DockPosition.Top => p.Y <= area.Top + 1 && p.X >= shown.Left && p.X <= shown.Right,
                DockPosition.Left => p.X <= area.Left + 1 && p.Y >= shown.Top && p.Y <= shown.Bottom,
                _ => p.X >= area.Right - 2 && p.Y >= shown.Top && p.Y <= shown.Bottom,
            };
            if (_hiddenByAutoHide && (atEdge || inDock)) { _hiddenByAutoHide = false; ApplyLayout(animate: true); }
            else if (!_hiddenByAutoHide)
            {
                if (inDock || _popup is { Visible: true } || _contextOpen || _dragActive) _leftAt = DateTime.UtcNow;
                else if (DateTime.UtcNow - _leftAt > TimeSpan.FromMilliseconds(650)) { _hiddenByAutoHide = true; HidePopup(); ApplyLayout(animate: true); }
            }
        }

        // ---- hit-testing ------------------------------------------------------------------------------------------
        private int HitTest(Point p)
        {
            double axis = Vertical ? p.Y : p.X;
            for (int i = 0; i < _items.Count; i++)
                if (Math.Abs(axis - CenterOf(i)) <= Cell / 2) return i;
            return -1;
        }

        private int InsertionIndex(Point p)
        {
            double axis = Vertical ? p.Y : p.X;
            int pinned = _items.Count(i => i.Pinned);
            for (int i = 0; i < pinned; i++) if (axis < CenterOf(i)) return i;
            return pinned;
        }

        // ---- ζωγραφική --------------------------------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var bgPath = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 20))
            {
                using var tint = new SolidBrush(Color.FromArgb(Low ? 215 : 70, 24, 26, 34));
                g.FillPath(tint, bgPath);
                using var pen = new Pen(Color.FromArgb(70, 255, 255, 255));
                g.DrawPath(pen, bgPath);
            }

            Color accent = MotionDesk.UI.UiTheme.AccentCyan;
            int size = _s.IconSize;
            double edge = _s.Position switch   // συντεταγμένη της "βάσης" των εικονιδίων στον κάθετο άξονα
            {
                DockPosition.Bottom => Height - Pad - IndicatorSpace,
                DockPosition.Top => Pad + IndicatorSpace,
                DockPosition.Left => Pad + IndicatorSpace,
                _ => Width - Pad - IndicatorSpace,
            };

            if (HasSeparator)
            {
                double sx = Pad + Cell * SeparatorIndex() + Gap / 2.0;
                using var sep = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f);
                if (Vertical) g.DrawLine(sep, Pad + 6, (float)sx, Width - Pad - 6, (float)sx);
                else g.DrawLine(sep, (float)sx, Pad + 6, (float)sx, Height - Pad - 6);
            }

            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                double ent = 1 - Math.Pow(1 - it.Entrance, 3);
                double s = it.Scale * ent;
                double px = size * s;
                double c = CenterOf(i);
                RectangleF r = _s.Position switch
                {
                    DockPosition.Bottom => new RectangleF((float)(c - px / 2), (float)(edge - px), (float)px, (float)px),
                    DockPosition.Top => new RectangleF((float)(c - px / 2), (float)edge, (float)px, (float)px),
                    DockPosition.Left => new RectangleF((float)edge, (float)(c - px / 2), (float)px, (float)px),
                    _ => new RectangleF((float)(edge - px), (float)(c - px / 2), (float)px, (float)px),
                };

                if (i == _hoverIndex)
                {
                    using var hb = new SolidBrush(Color.FromArgb(38, 255, 255, 255));
                    var hr = RectangleF.Inflate(r, 5, 5);
                    using var hp = MotionDesk.UI.UiTheme.RoundedPath(Rectangle.Round(hr), 11);
                    g.FillPath(hb, hp);
                }
                if (it.Icon != null && px >= 1) g.DrawImage(it.Icon, r);
                else
                {
                    using var fb = new SolidBrush(Color.FromArgb(90, accent));
                    g.FillEllipse(fb, r);
                }

                if (it.Dot > 0.02)
                {
                    int alpha = (int)(235 * it.Dot);
                    PointF center = _s.Position switch
                    {
                        DockPosition.Bottom => new PointF((float)c, Height - Pad + 1),
                        DockPosition.Top => new PointF((float)c, Pad - 1),
                        DockPosition.Left => new PointF(Pad - 1, (float)c),
                        _ => new PointF(Width - Pad + 1, (float)c),
                    };
                    float w = it.Active ? 16 : 5, h = 4;
                    var ir = Vertical ? new RectangleF(center.X - h / 2, center.Y - w / 2, h, w) : new RectangleF(center.X - w / 2, center.Y - h / 2, w, h);
                    using var db = new SolidBrush(Color.FromArgb(alpha, it.Active ? accent : Color.FromArgb(230, 230, 235)));
                    using var dp = MotionDesk.UI.UiTheme.RoundedPath(Rectangle.Round(ir), 2);
                    g.FillPath(db, dp);
                    if (it.Windows.Count > 1)
                    {
                        using var cf = new Font("Segoe UI", 7f, FontStyle.Bold);
                        using var cb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                        var rect = Vertical ? new RectangleF(r.Right - 4, r.Top - 2, 14, 12) : new RectangleF(r.Right - 8, r.Top - 4, 14, 12);
                        g.DrawString(it.Windows.Count.ToString(), cf, cb, rect);
                    }
                }
            }

            if (_dropIndex >= 0)   // δείκτης σημείου απόθεσης κατά το σύρσιμο
            {
                int pinnedCount = _items.Count(i => i.Pinned);
                double pos = _dropIndex < pinnedCount ? CenterOf(_dropIndex) - Cell / 2 : (pinnedCount > 0 ? CenterOf(pinnedCount - 1) + Cell / 2 : Pad);
                using var dp = new Pen(accent, 3f);
                if (Vertical) g.DrawLine(dp, Pad, (float)pos, Width - Pad, (float)pos); else g.DrawLine(dp, (float)pos, Pad, (float)pos, Height - Pad);
            }
        }

        // ---- ποντίκι ----------------------------------------------------------------------------------------------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = e.Location;
            int idx = HitTest(e.Location);
            if (idx != _hoverIndex) { _hoverIndex = idx; HidePopup(); if (idx >= 0) { _hoverTimer.Stop(); _hoverTimer.Start(); } }
            if (e.Button == MouseButtons.Left && _dragIndex >= 0 && _dragIndex < _items.Count && _items[_dragIndex].Pinned)
            {
                if (Math.Abs(e.X - _dragStart.X) >= SystemInformation.DragSize.Width || Math.Abs(e.Y - _dragStart.Y) >= SystemInformation.DragSize.Height)
                {
                    int from = _dragIndex; _dragIndex = -1; _dragActive = true; HidePopup();
                    DoDragDrop(new DataObject(ReorderFormat, _items[from].Key), DragDropEffects.Move);
                    _dragActive = false; _dropIndex = -1; Invalidate();
                }
            }
            StartAnim();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouse = null; _hoverIndex = -1; _hoverTimer.Stop();
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) HidePopup();
            StartAnim();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { _dragIndex = HitTest(e.Location); _dragStart = e.Location; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragIndex = -1;
            int idx = HitTest(e.Location);
            if (idx < 0 || idx >= _items.Count) return;
            HidePopup();
            if (e.Button == MouseButtons.Left) ActivateItem(_items[idx]);
            else if (e.Button == MouseButtons.Middle) Launch(_items[idx], admin: false);
            else if (e.Button == MouseButtons.Right) ShowMenu(_items[idx], e.Location);
        }

        /// <summary>Κλικ: αν τρέχει → ενεργοποίηση (ή ελαχιστοποίηση αν είναι ήδη το μοναδικό ενεργό, κύκλος ανάμεσα σε πολλά), αλλιώς εκκίνηση.</summary>
        private void ActivateItem(Item it)
        {
            if (it.Windows.Count == 0) { Launch(it, admin: false); return; }
            IntPtr fg = WinApi.GetForegroundWindow();
            int at = it.Windows.IndexOf(fg);
            if (at >= 0 && it.Windows.Count == 1) WinApi.Minimize(fg);
            else WinApi.BringToFront(it.Windows[(at + 1) % it.Windows.Count]);
        }

        private void Launch(Item it, bool admin)
        {
            try
            {
                var psi = new ProcessStartInfo(it.Pinned ? it.Key : (it.Target ?? it.Key)) { UseShellExecute = true };
                if (admin) psi.Verb = "runas";
                Process.Start(psi);
                it.Entrance = 0.6; StartAnim();
            }
            catch (Exception) { /* ακυρώθηκε το UAC ή λείπει το αρχείο */ }
        }

        private void ShowMenu(Item it, Point at)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            menu.Items.Add(new ToolStripMenuItem(it.Display) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(it.Windows.Count > 0 ? "Νέο παράθυρο / New window" : LocalizationManager.T("DCMenu.Open"), null, (_, _) => Launch(it, false));
            menu.Items.Add("Εκτέλεση ως διαχειριστής / Run as administrator", null, (_, _) => Launch(it, true));
            string file = it.Target ?? it.Key;
            if (File.Exists(file) || Directory.Exists(file))
                menu.Items.Add("Άνοιγμα τοποθεσίας αρχείου / Open file location", null, (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); } catch (Exception) { }
                });
            if (it.Windows.Count > 0)
                menu.Items.Add(it.Windows.Count > 1 ? "Κλείσιμο όλων των παραθύρων / Close all windows" : "Κλείσιμο παραθύρου / Close window", null, (_, _) => { foreach (var w in it.Windows.ToList()) WinApi.Close(w); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(it.Pinned ? LocalizationManager.T("DCMenu.Unpin") : "Καρφίτσωμα στο dock / Pin to dock", null, (_, _) =>
            {
                if (it.Pinned) UnpinApp(it.Key); else PinApp(it.Target ?? it.Key);
            });
            _contextOpen = true;
            menu.Closed += (_, _) => { _contextOpen = false; menu.Dispose(); };
            menu.Show(this, at);
        }

        // ---- προεπισκόπηση / tooltip -----------------------------------------------------------------------------
        private void ShowPopupForHover()
        {
            if (_hoverIndex < 0 || _hoverIndex >= _items.Count || _dragActive || _contextOpen) return;
            var it = _items[_hoverIndex];
            _popup ??= new DockPopup();
            var windows = it.Windows.Take(_s.ShowPreviews && !Low ? 3 : 0).ToList();
            _popup.ShowFor(it.Windows.Count == 1 && it.Title.Length > 0 ? it.Title : it.Display, windows, ItemScreenRect(_hoverIndex), _s.Position);
        }

        private Rectangle ItemScreenRect(int i)
        {
            double c = CenterOf(i), half = _s.IconSize / 2.0 + 4;
            var topLeft = Vertical ? new Point(0, (int)(c - half)) : new Point((int)(c - half), 0);
            var size = Vertical ? new Size(Width, (int)(half * 2)) : new Size((int)(half * 2), Height);
            return new Rectangle(PointToScreen(topLeft), size);
        }

        private void HidePopup() { _hoverTimer.Stop(); _popup?.HidePopup(); }

        // ---- drag & drop ------------------------------------------------------------------------------------------
        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = e.Data?.GetDataPresent(ReorderFormat) == true ? DragDropEffects.Move
                     : e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            _dragActive = true;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            _dropIndex = InsertionIndex(PointToClient(new Point(e.X, e.Y)));
            Invalidate();
        }

        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); _dropIndex = -1; _dragActive = false; Invalidate(); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            int at = InsertionIndex(PointToClient(new Point(e.X, e.Y)));
            _dropIndex = -1; _dragActive = false;
            if (e.Data?.GetData(ReorderFormat) is string key) MovePinned(key, at);
            else if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files, at);
            Invalidate();
        }

        private void MovePinned(string key, int insertAt)
        {
            int from = _s.Paths.FindIndex(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase));
            if (from < 0) return;
            _s.Paths.RemoveAt(from);
            if (insertAt > from) insertAt--;
            _s.Paths.Insert(Math.Clamp(insertAt, 0, _s.Paths.Count), key);
            SyncItems(); ApplyLayout(false); QueueSave();
        }

        private void AddFiles(string[] files, int insertAt = -1)
        {
            bool changed = false;
            foreach (var f in files)
            {
                if (_s.Paths.Contains(f, StringComparer.OrdinalIgnoreCase)) continue;
                if (insertAt >= 0 && insertAt <= _s.Paths.Count) _s.Paths.Insert(insertAt++, f); else _s.Paths.Add(f);
                _pendingEntrance.Add(f); changed = true;
            }
            if (changed) { SyncItems(); ApplyLayout(true); QueueSave(); StartAnim(); }
        }

        // ---- API προς τα έξω --------------------------------------------------------------------------------------
        public void PinApp(string path) => AddFiles(new[] { path });

        public void UnpinApp(string path)
        {
            if (_s.Paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0) { SyncItems(); ApplyLayout(true); QueueSave(); }
        }

        // ---- παράθυρο: blur, DPI ----------------------------------------------------------------------------------
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 /*WS_EX_NOACTIVATE*/ | 0x80 /*TOOLWINDOW: όχι Alt-Tab*/; return cp; }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyBlur();
        }

        private const int WM_DPICHANGED = 0x02E0;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_DPICHANGED && IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => ApplyLayout(false)));
        }

        private void ApplyBlur()
        {
            if (Low) return;   // σε αδύναμα συστήματα: συμπαγές χρώμα, χωρίς acrylic
            var accent = new ACCENT_POLICY { AccentState = 3, GradientColor = unchecked((int)0x66202228) };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, SizeOfData = size, Data = ptr };
            SetWindowCompositionAttribute(Handle, ref data);
            Marshal.FreeHGlobal(ptr);
        }

        // ---- αποθήκευση -------------------------------------------------------------------------------------------
        private static string ConfigPath => Path.Combine(AppPaths.AppDataFolder, "deskstrip", "config.json");
        private void QueueSave() { _saveTimer.Stop(); _saveTimer.Start(); }

        private void SaveState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(_s));
            }
            catch (IOException) { }
        }

        private void LoadState()
        {
            if (!File.Exists(ConfigPath))
            {
                _s.Paths.AddRange(TaskbarPinsReader.GetPinnedAppPaths());     // πρώτη φορά: όσα είναι καρφιτσωμένα στην taskbar
                return;
            }
            try
            {
                var state = JsonSerializer.Deserialize<DeskStripSettings>(AtomicFile.ReadAllText(ConfigPath));
                if (state == null) return;
                _s.Paths = state.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                _s.Position = Enum.IsDefined(state.Position) ? state.Position : DockPosition.Bottom;
                _s.AutoHide = state.AutoHide; _s.IconSize = Math.Clamp(state.IconSize == 0 ? 44 : state.IconSize, 28, 72);
                _s.Magnify = state.Magnify; _s.ShowRunning = state.ShowRunning; _s.ShowPreviews = state.ShowPreviews;
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        [StructLayout(LayoutKind.Sequential)] private struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] private struct WINDOWCOMPOSITIONATTRIBDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    }

    /// <summary>Πλωτό tooltip/προεπισκόπηση πάνω από ένα εικονίδιο του dock: τίτλος + DWM thumbnails των παραθύρων.</summary>
    internal sealed class DockPopup : Form
    {
        private readonly List<IntPtr> _thumbs = new();
        private string _title = "";
        private const int ThumbW = 220, ThumbH = 140;
        private readonly Font _font = new("Segoe UI Semibold", 9.5f);

        public DockPopup()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(30, 32, 40); DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } }

        public void ShowFor(string title, List<IntPtr> windows, Rectangle itemScreenRect, DockPosition position)
        {
            ClearThumbs();
            _title = title;
            int thumbs = windows.Count;
            var textSize = TextRenderer.MeasureText(title, _font);
            int w = Math.Max(textSize.Width + 24, thumbs > 0 ? thumbs * (ThumbW + 8) + 8 : 0);
            int h = 30 + (thumbs > 0 ? ThumbH + 10 : 0);
            Size = new Size(w, h);
            var loc = position switch
            {
                DockPosition.Bottom => new Point(itemScreenRect.X + itemScreenRect.Width / 2 - w / 2, itemScreenRect.Top - h - 8),
                DockPosition.Top => new Point(itemScreenRect.X + itemScreenRect.Width / 2 - w / 2, itemScreenRect.Bottom + 8),
                DockPosition.Left => new Point(itemScreenRect.Right + 8, itemScreenRect.Y + itemScreenRect.Height / 2 - h / 2),
                _ => new Point(itemScreenRect.Left - w - 8, itemScreenRect.Y + itemScreenRect.Height / 2 - h / 2),
            };
            var wa = Screen.FromRectangle(itemScreenRect).WorkingArea;
            Location = new Point(Math.Clamp(loc.X, wa.X + 4, Math.Max(wa.X + 4, wa.Right - w - 4)), Math.Clamp(loc.Y, wa.Y + 4, Math.Max(wa.Y + 4, wa.Bottom - h - 4)));
            try { using var p = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(0, 0, w, h), 12); Region = new Region(p); } catch (Exception) { }
            if (!Visible) Show();
            Invalidate();
            for (int i = 0; i < thumbs; i++) AddThumb(windows[i], new Rectangle(8 + i * (ThumbW + 8), 30, ThumbW, ThumbH));
        }

        public void HidePopup() { ClearThumbs(); if (Visible) Hide(); }

        private void AddThumb(IntPtr src, Rectangle dest)
        {
            if (WinApi.DwmRegisterThumbnail(Handle, src, out IntPtr thumb) != 0) return;
            if (WinApi.DwmQueryThumbnailSourceSize(thumb, out WinApi.SIZEL sz) == 0 && sz.X > 0 && sz.Y > 0)
            {
                double k = Math.Min(dest.Width / (double)sz.X, dest.Height / (double)sz.Y);
                int tw = (int)(sz.X * k), th = (int)(sz.Y * k);
                dest = new Rectangle(dest.X + (dest.Width - tw) / 2, dest.Y + (dest.Height - th) / 2, tw, th);
            }
            var props = new WinApi.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = 0x1 | 0x4 | 0x8,  // RECTDESTINATION | OPACITY | VISIBLE
                rcDestination = new WinApi.RECT { Left = dest.Left, Top = dest.Top, Right = dest.Right, Bottom = dest.Bottom },
                opacity = 255, fVisible = true,
            };
            WinApi.DwmUpdateThumbnailProperties(thumb, ref props);
            _thumbs.Add(thumb);
        }

        private void ClearThumbs() { foreach (var t in _thumbs) WinApi.DwmUnregisterThumbnail(t); _thumbs.Clear(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using var pen = new Pen(Color.FromArgb(70, 255, 255, 255));
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, _title, _font, new Rectangle(10, 5, Width - 20, 22), Color.White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing) { if (disposing) { ClearThumbs(); _font.Dispose(); } base.Dispose(disposing); }
    }

    /// <summary>Win32 βοηθητικά του dock (απαρίθμηση "Alt-Tab" παραθύρων, ενεργοποίηση, εικονίδια, DWM thumbnails).</summary>
    internal static class WinApi
    {
        internal sealed record AppWindow(IntPtr Handle, string Title, string ExePath);

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr h, int i);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder s, ref int n);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct DWM_THUMBNAIL_PROPERTIES { public int dwFlags; public RECT rcDestination, rcSource; public byte opacity; [MarshalAs(UnmanagedType.Bool)] public bool fVisible; [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly; }
        [StructLayout(LayoutKind.Sequential)] internal struct SIZEL { public int X, Y; }
        [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES p);
        [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZEL size);

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        private const int DWMWA_CLOAKED = 14;

        private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
            { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow" };

        /// <summary>Τα παράθυρα που θα εμφανίζονταν στο Alt-Tab (ορατά, χωρίς owner, όχι tool windows, όχι cloaked).</summary>
        public static List<AppWindow> EnumerateAppWindows()
        {
            var list = new List<AppWindow>();
            uint me = (uint)Environment.ProcessId;
            var cls = new StringBuilder(128); var title = new StringBuilder(256);
            EnumWindows((h, _) =>
            {
                try
                {
                    if (!IsWindowVisible(h)) return true;
                    long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                    bool app = (ex & WS_EX_APPWINDOW) != 0;
                    if (!app && ((ex & WS_EX_TOOLWINDOW) != 0 || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero)) return true;
                    if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                    cls.Clear(); GetClassName(h, cls, 128);
                    if (IgnoredClasses.Contains(cls.ToString())) return true;
                    title.Clear(); GetWindowText(h, title, 256);
                    if (title.Length == 0) return true;
                    GetWindowThreadProcessId(h, out uint pid);
                    if (pid == me && title.ToString() != "Opsis") return true;           // τα δικά μας widgets/overlays όχι
                    string? exe = ImagePath(pid);
                    if (exe == null) return true;
                    list.Add(new AppWindow(h, title.ToString(), exe));
                }
                catch (Exception) { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private static string? ImagePath(uint pid)
        {
            IntPtr h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return null;
            try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null; }
            finally { CloseHandle(h); }
        }

        public static void BringToFront(IntPtr h) { if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/); SetForegroundWindow(h); }
        public static void Minimize(IntPtr h) => ShowWindow(h, 6 /*SW_MINIMIZE*/);
        public static void Close(IntPtr h) => PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero);

        /// <summary>Εικονίδιο παραθύρου (για UWP/εφαρμογές όπου το exe δεν δίνει το σωστό εικονίδιο).</summary>
        public static Bitmap? WindowIcon(IntPtr h)
        {
            try
            {
                SendMessageTimeout(h, 0x7F /*WM_GETICON*/, new IntPtr(2) /*ICON_SMALL2*/, IntPtr.Zero, 0x2, 100, out IntPtr ic);
                if (ic == IntPtr.Zero) SendMessageTimeout(h, 0x7F, new IntPtr(1) /*ICON_BIG*/, IntPtr.Zero, 0x2, 100, out ic);
                if (ic == IntPtr.Zero) ic = GetClassLongPtr(h, -14 /*GCL_HICON*/);
                return ic == IntPtr.Zero ? null : Icon.FromHandle(ic).ToBitmap();
            }
            catch (Exception) { return null; }
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

        public IReadOnlyList<string> GetPinnedApps() => _window?.PinnedApps ?? GetSettings().Paths;

        /// <summary>Τρέχουσες ρυθμίσεις (ή οι αποθηκευμένες αν το dock είναι κλειστό).</summary>
        public DeskStripSettings GetSettings()
        {
            if (_window is { IsDisposed: false }) return _window.Settings;
            try
            {
                string p = Path.Combine(AppPaths.AppDataFolder, "deskstrip", "config.json");
                if (File.Exists(p)) return JsonSerializer.Deserialize<DeskStripSettings>(AtomicFile.ReadAllText(p)) ?? new DeskStripSettings();
            }
            catch (Exception) { }
            return new DeskStripSettings();
        }

        public void UpdateSettings(Action<DeskStripSettings> change)
        {
            if (_window is { IsDisposed: false }) { _window.UpdateSettings(change); return; }
            var s = GetSettings(); change(s);
            try
            {
                string p = Path.Combine(AppPaths.AppDataFolder, "deskstrip", "config.json");
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                AtomicFile.WriteAllText(p, JsonSerializer.Serialize(s));
            }
            catch (IOException) { }
        }
    }
}
