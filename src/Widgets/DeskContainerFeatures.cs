using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;
using UiTheme = MotionDesk.UI.WidgetTheme;

namespace MotionDesk.Widgets
{
    // Features modelled on DesktopFences (MIT, see THIRD_PARTY_NOTICES.md): tabs, note fences, blank spacers.
    public sealed class ContainerTabState
    {
        public string Name { get; set; } = "Tab";
        public List<ContainerItem> Items { get; set; } = new();
    }

    public sealed partial class DeskContainerWindow
    {
        // ---- blank spacers: an item whose "path" is a sentinel, drawn as an empty cell ----
        internal const string SpacerPrefix = "::spacer::";
        internal static bool IsSpacer(string path) => path.StartsWith(SpacerPrefix, StringComparison.Ordinal);

        private void AddSpacer()
        {
            if (IsPortal) return;
            _items.Add(new ContainerItem { Path = SpacerPrefix + Guid.NewGuid().ToString("N")[..8] });
            RebuildGrid();
            QueueSave();
        }

        // ---- tabs: _items is always the active tab's list; _tabs stays empty while the container has a single tab ----
        private List<ContainerTabState> _tabs = new();
        private int _activeTab;
        private Panel? _tabStrip;
        private const int TabStripHeight = 26;

        // ---- note fence: the container shows a free-text note instead of icons ----
        private TextBox? _noteBox;
        private string _noteText = "";
        public bool IsNote { get; private set; }

        private void SyncTabs() { if (_tabs.Count > 0 && _activeTab < _tabs.Count) _tabs[_activeTab].Items = _items; }

        private void LoadFeatureState(DeskContainerState s)
        {
            if (s.Tabs is { Count: > 1 })
            {
                _tabs = s.Tabs;
                _activeTab = Math.Clamp(s.ActiveTab, 0, _tabs.Count - 1);
                _items = _tabs[_activeTab].Items ??= new List<ContainerItem>();
            }
            IsNote = s.IsNote;
            _noteText = s.NoteText ?? "";
        }

        private void SaveFeatureState(DeskContainerState s)
        {
            SyncTabs();
            if (_tabs.Count > 1) { s.Tabs = _tabs; s.ActiveTab = _activeTab; }
            s.IsNote = IsNote;
            s.NoteText = _noteText;
        }

        private string TabName(int i) => i == 0 && _tabs[0].Name.Length == 0 ? _title : _tabs[i].Name;

        private List<Rectangle> TabRects(out Rectangle plus)
        {
            var rects = new List<Rectangle>();
            int x = 8;
            using var font = new Font("Segoe UI", 9f);
            for (int i = 0; i < _tabs.Count; i++)
            {
                int w = Math.Min(160, TextRenderer.MeasureText(TabName(i), font).Width + 22);
                rects.Add(new Rectangle(x, 2, w, TabStripHeight - 4));
                x += w + 4;
            }
            plus = new Rectangle(x, 2, 24, TabStripHeight - 4);
            return rects;
        }

        private void InitFeatures()
        {
            _tabStrip = new Panel { Location = new Point(0, TitleBarHeight), Size = new Size(Width, TabStripHeight), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.Transparent, Visible = false, AllowDrop = true };
            _tabStrip.Paint += (_, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var rects = TabRects(out var plus);
                using var font = new Font("Segoe UI", 9f);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                for (int i = 0; i < rects.Count; i++)
                {
                    bool active = i == _activeTab;
                    if (active)
                    {
                        using var bg = new SolidBrush(Color.FromArgb(55, 255, 255, 255));
                        using var path = MotionDesk.UI.UiTheme.RoundedPath(rects[i], 4);
                        g.FillPath(bg, path);
                    }
                    using var br = new SolidBrush(Color.FromArgb(active ? 255 : 170, 255, 255, 255));
                    g.DrawString(TabName(i), font, br, rects[i], sf);
                    if (active) { using var pen = new Pen(Color.White, 2f); g.DrawLine(pen, rects[i].Left + 4, rects[i].Bottom, rects[i].Right - 4, rects[i].Bottom); }
                }
                using var plusBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                using var plusFont = new Font("Segoe UI", 11f);
                g.DrawString("+", plusFont, plusBrush, plus, sf);
            };
            _tabStrip.MouseDown += (_, e) =>
            {
                var rects = TabRects(out var plus);
                int hit = rects.FindIndex(r => r.Contains(e.Location));
                if (e.Button == MouseButtons.Left)
                {
                    if (hit >= 0) SwitchTab(hit);
                    else if (plus.Contains(e.Location)) AddTabPrompt();
                }
                else if (e.Button == MouseButtons.Right && hit >= 0) ShowTabMenu(hit, e.Location);
            };
            // dropping files on a tab puts them in that tab
            _tabStrip.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            _tabStrip.DragDrop += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
                var rects = TabRects(out Rectangle _);
                int hit = rects.FindIndex(r => r.Contains(_tabStrip.PointToClient(new Point(e.X, e.Y))));
                if (hit < 0 || hit == _activeTab) { AddPaths(files); return; }
                foreach (var f in files)
                    if (!_tabs[hit].Items.Any(i => string.Equals(i.Path, f, StringComparison.OrdinalIgnoreCase)))
                        _tabs[hit].Items.Add(new ContainerItem { Path = f, DateAdded = DateTime.Now });
                QueueSave();
            };
            Controls.Add(_tabStrip);
            _tabStrip.BringToFront();

            _noteBox = new TextBox
            {
                Multiline = true, AcceptsReturn = true, AcceptsTab = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
                BackColor = BackColor, ForeColor = Color.White, Font = new Font("Segoe UI", 10f), Text = _noteText, Visible = false,
                Location = new Point(10, TitleBarHeight + 4), Size = new Size(Math.Max(40, Width - 20), Math.Max(40, Height - TitleBarHeight - 16)),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _noteBox.TextChanged += (_, _) => { _noteText = _noteBox.Text; QueueSave(); };
            Controls.Add(_noteBox);
            _noteBox.BringToFront();

            UpdateFeatureLayout();
        }

        private void UpdateFeatureLayout()
        {
            if (_tabStrip == null || _noteBox == null) return;
            bool strip = _tabs.Count > 1 && !IsNote && !IsPortal;
            _tabStrip.Visible = strip;
            _tabStrip.Invalidate();
            _grid.Padding = new Padding(6, 4 + (strip ? TabStripHeight + 2 : 0), 6, 6);
            _noteBox.Visible = IsNote;
            _grid.Visible = !IsNote && (!IsRolledUp || _hoverExpanded);
            if (IsNote) _noteBox.BringToFront();
        }

        public void SetNote(bool on)
        {
            if (IsNote == on) return;
            IsNote = on;
            UpdateFeatureLayout();
            QueueSave();
        }

        private void SwitchTab(int i)
        {
            if (i < 0 || i >= _tabs.Count || i == _activeTab) return;
            SyncTabs();
            _activeTab = i;
            _items = _tabs[i].Items;
            _selected.Clear();
            _filter = "";
            RebuildGrid();
            _tabStrip?.Invalidate();
            QueueSave();
        }

        private void AddTab(string name)
        {
            if (IsPortal) return;
            SyncTabs();
            if (_tabs.Count == 0) { _tabs.Add(new ContainerTabState { Name = "", Items = _items }); _activeTab = 0; }
            _tabs.Add(new ContainerTabState { Name = name, Items = new List<ContainerItem>() });
            UpdateFeatureLayout();
            SwitchTab(_tabs.Count - 1);
        }

        private string Lx(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;

        private void AddTabPrompt()
        {
            var name = PromptText(Lx("Νέα καρτέλα", "New tab"), Lx("Όνομα καρτέλας:", "Tab name:"), Lx("Καρτέλα ", "Tab ") + (Math.Max(2, _tabs.Count + 1)));
            if (!string.IsNullOrWhiteSpace(name)) AddTab(name);
        }

        private void RenameTab(int i)
        {
            var name = PromptText(Lx("Μετονομασία καρτέλας", "Rename tab"), Lx("Όνομα καρτέλας:", "Tab name:"), TabName(i));
            if (string.IsNullOrWhiteSpace(name)) return;
            _tabs[i].Name = name; _tabStrip?.Invalidate(); QueueSave();
        }

        private void RemoveTab(int i)
        {
            if (_tabs.Count < 2) return;
            if (_tabs[i].Items.Count > 0 &&
                MessageBox.Show(this, Lx("Η καρτέλα έχει στοιχεία. Να αφαιρεθεί (τα αρχεία δεν διαγράφονται);", "This tab has items. Remove it (files are not deleted)?"), _title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SyncTabs();
            _tabs.RemoveAt(i);
            _activeTab = Math.Clamp(_activeTab > i ? _activeTab - 1 : _activeTab, 0, _tabs.Count - 1);
            _items = _tabs[_activeTab].Items;
            if (_tabs.Count == 1) { _tabs.Clear(); _activeTab = 0; }
            _selected.Clear();
            UpdateFeatureLayout();
            RebuildGrid();
            QueueSave();
        }

        private void MoveTab(int i, int delta)
        {
            int j = i + delta;
            if (j < 0 || j >= _tabs.Count) return;
            SyncTabs();
            var t = _tabs[i]; _tabs.RemoveAt(i); _tabs.Insert(j, t);
            if (_activeTab == i) _activeTab = j; else if (_activeTab == j) _activeTab = i;
            _tabStrip?.Invalidate(); QueueSave();
        }

        private void ShowTabMenu(int i, Point at)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(Lx("Μετονομασία…", "Rename…"), null, (_, _) => RenameTab(i));
            menu.Items.Add(Lx("Μετακίνηση αριστερά", "Move left"), null, (_, _) => MoveTab(i, -1)).Enabled = i > 0;
            menu.Items.Add(Lx("Μετακίνηση δεξιά", "Move right"), null, (_, _) => MoveTab(i, 1)).Enabled = i < _tabs.Count - 1;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lx("Αφαίρεση καρτέλας", "Remove tab"), null, (_, _) => RemoveTab(i));
            menu.Closed += (_, _) => menu.Dispose();
            menu.Show(_tabStrip!, at);
        }

        // Settings-dialog tab for the features above.
        private TabPage BuildFeaturesPage(Form dialog)
        {
            var page = new TabPage(Lx("Καρτέλες & σημείωση", "Tabs & note")) { BackColor = UiTheme.Surface };
            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
            page.Controls.Add(stack);

            var addTabBtn = new Button { Text = Lx("Προσθήκη καρτέλας…", "Add tab…"), AutoSize = true, Enabled = !IsPortal };
            addTabBtn.Click += (_, _) => { dialog.Close(); AddTabPrompt(); };
            var renameTabBtn = new Button { Text = Lx("Μετονομασία τρέχουσας καρτέλας…", "Rename current tab…"), AutoSize = true, Enabled = _tabs.Count > 1 };
            renameTabBtn.Click += (_, _) => { dialog.Close(); RenameTab(_activeTab); };
            var removeTabBtn = new Button { Text = Lx("Αφαίρεση τρέχουσας καρτέλας", "Remove current tab"), AutoSize = true, Enabled = _tabs.Count > 1 };
            removeTabBtn.Click += (_, _) => { dialog.Close(); RemoveTab(_activeTab); };
            var spacerBtn = new Button { Text = Lx("Προσθήκη κενού (blank spacer)", "Add blank spacer"), AutoSize = true, Enabled = !IsPortal };
            spacerBtn.Click += (_, _) => AddSpacer();
            var noteCheck = new FlatCheckBox { Text = Lx("Λειτουργία σημείωσης (note fence)", "Note fence mode"), Checked = IsNote, AutoSize = true, BackColor = UiTheme.Surface };
            noteCheck.CheckedChanged += (_, _) => SetNote(noteCheck.Checked);

            stack.Controls.Add(Row(addTabBtn));
            stack.Controls.Add(Row(renameTabBtn));
            stack.Controls.Add(Row(removeTabBtn));
            stack.Controls.Add(Row(spacerBtn));
            stack.Controls.Add(Row(noteCheck));
            return page;
        }
    }

    // Named container layouts ("profiles", DesktopFences) — a profile is a copy of every active container's config.
    public static class DeskContainerProfiles
    {
        public static string Root => Path.Combine(AppPaths.AppDataFolder, "deskcontainers_profiles");
        private static string ConfigDir => Path.Combine(AppPaths.AppDataFolder, "deskcontainers");

        private static string Safe(string name) => string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        public static IReadOnlyList<string> List() =>
            Directory.Exists(Root) ? Directory.GetDirectories(Root).Select(d => Path.GetFileName(d)!).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList() : new List<string>();

        public static bool Save(string name)
        {
            name = Safe(name);
            if (name.Length == 0) return false;
            try
            {
                var active = DeskContainerHostEngine.Instance.GetActiveContainers().Where(c => !c.IsDisposed).ToArray();
                foreach (var c in active) c.SaveNow();
                var dir = Path.Combine(Root, name);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                foreach (var c in active)
                {
                    var src = Path.Combine(ConfigDir, $"{c.ContainerId}_config.json");
                    if (File.Exists(src)) File.Copy(src, Path.Combine(dir, Path.GetFileName(src)), true);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }

        public static int Apply(string name)
        {
            var dir = Path.Combine(Root, Safe(name));
            return Directory.Exists(dir) ? DeskContainerExtras.Restore(dir) : 0;
        }

        public static void Delete(string name)
        {
            try { var dir = Path.Combine(Root, Safe(name)); if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }

        // Automation: a profile can be bound to a display layout (monitor count + resolutions) and applies itself when that layout appears.
        public static string DisplaySignature() =>
            string.Join(";", Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}"));

        private static string? _lastSignature;

        public static void CheckAutomation()
        {
            string sig = DisplaySignature();
            if (_lastSignature == null) { _lastSignature = sig; return; }       // never re-arrange anything at startup
            if (sig == _lastSignature) return;
            _lastSignature = sig;
            var map = AppSettings.Load().ContainerProfileByDisplay;
            if (map.TryGetValue(sig, out var profile) && List().Contains(profile)) Apply(profile);
        }
    }
}
