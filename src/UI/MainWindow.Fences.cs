using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    // Settings of the DeskContainers engine (the bundled fences), shown as cards on the DeskZones page instead of a separate
    // settings window. Values are read from / written to the engine's options.json through FencesBridge; the engine applies them live.
    public sealed partial class MainWindow
    {
        private static string Lx(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;

        private static readonly string[] FxColors = { "Gray", "Black", "White", "Beige", "Green", "Purple", "Fuchsia", "Yellow", "Orange", "Red", "Blue", "Bismark", "Teal" };
        private static readonly string[] FxEffects = { "Zoom", "Bounce", "FadeOut", "SlideUp", "Rotate", "Agitate", "GrowAndFly", "Pulse", "Elastic", "Flip3D", "Spiral", "Shockwave", "Matrix", "Supernova" };
        private static readonly string[] FxMenuIcons = { "⋯", "☰", "≣", "𓃑" };
        private static readonly string[] FxModifiers = { "Control", "Alt", "Shift", "Control, Alt", "Control, Shift", "Alt, Shift", "Control, Alt, Shift", "Win" };
        private static readonly string[] FxLogLevels = { "Debug", "Info", "Warn", "Error" };
        private static readonly string[] FxLogCategories = { "General", "FenceCreation", "FenceUpdate", "UI", "IconHandling", "ImportExport", "Settings", "BackgroundValidation" };

        private static readonly (string Name, int Vk)[] FxKeys = BuildFxKeys();
        private static (string, int)[] BuildFxKeys()
        {
            var list = new List<(string, int)>();
            for (char c = 'A'; c <= 'Z'; c++) list.Add((c.ToString(), c));
            for (char c = '0'; c <= '9'; c++) list.Add((c.ToString(), c));
            for (int i = 1; i <= 12; i++) list.Add(("F" + i, 0x6F + i));
            list.AddRange(new[] { ("Comma (,)", 0xBC), ("Period (.)", 0xBE), ("Tilde (~)", 192), ("Space", 32), ("Tab", 9), ("Enter", 13), ("Escape", 27) });
            return list.ToArray();
        }

        private static CheckBox CardCheck(string text, bool initial, Action<bool> onChange)
        {
            var box = SettingsView.Checkbox(text, initial, onChange);
            box.BackColor = UiTheme.Surface;
            return box;
        }

        private static void FxBool(Control card, string label, string key, bool def, Action<bool>? extra = null)
        {
            var box = SettingsView.Checkbox(label, FencesBridge.GetBool(key, def), v => { FencesBridge.Set(key, v); extra?.Invoke(v); });
            box.BackColor = UiTheme.Surface;                 // same colour as the card, no darker strip behind the label
            card.Controls.Add(box);
        }

        private static void FxNum(Control card, string label, string key, int def, int min, int max, string unit)
        {
            card.Controls.Add(SettingsView.NumericRow(label, Math.Clamp(FencesBridge.GetInt(key, def), min, max), min, max, v => FencesBridge.Set(key, v), 0, unit));
        }

        private static FlatComboBox FxCombo(Control card, string label, string[] items, string current, Action<string> onChange)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
            row.Controls.Add(new Label { Text = label + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 7, 10, 0), MinimumSize = new Size(170, 0) });
            var combo = new FlatComboBox { Width = 200 };
            combo.SetItems(items, items.Contains(current) ? current : items[0]);
            combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is string s) onChange(s); };
            row.Controls.Add(combo);
            card.Controls.Add(row);
            return combo;
        }

        private static void FxButtons(Control card, params (string Text, Action Act)[] items)
        {
            // pill buttons that stand out from the card surface (the stock grid buttons use the card colour and would be invisible here)
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, MaximumSize = new Size(700, 0), Margin = new Padding(0, 4, 0, 6) };
            foreach (var (text, act) in items)
            {
                int w = TextRenderer.MeasureText(text, UiTheme.FontBody).Width + 36;
                var b = new HoverButton { Text = text, Width = Math.Max(110, w), Height = 34, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BaseColor = UiTheme.SurfaceHover, BackColor = UiTheme.SurfaceHover, HoverBackColor = UiTheme.Border, Margin = new Padding(0, 0, 8, 6), TabStop = false, Cursor = Cursors.Hand };
                b.FlatAppearance.BorderSize = 0;
                UiTheme.ApplyRoundedRegion(b, b.Height / 2);
                var a = act;
                b.Click += (_, _) => a();
                row.Controls.Add(b);
            }
            card.Controls.Add(row);
        }

        // hotkey row: enable switch + modifier combo (+ key combo)
        private static void FxHotkey(Control card, string label, string enableKey, string modKey, string? keyKey, int defaultKey, string defaultMod)
        {
            FxBool(card, label, enableKey, true);
            FxCombo(card, Lx("Τροποποιητής", "Modifier"), FxModifiers, FencesBridge.GetString(modKey, defaultMod), v => FencesBridge.Set(modKey, v));
            if (keyKey == null) return;
            string cur = FxKeys.FirstOrDefault(k => k.Vk == FencesBridge.GetInt(keyKey, defaultKey)).Name ?? FxKeys[0].Name;
            FxCombo(card, Lx("Πλήκτρο", "Key"), FxKeys.Select(k => k.Name).ToArray(), cur, v => FencesBridge.Set(keyKey, FxKeys.First(k => k.Name == v).Vk));
        }


        // "Transparent" widgets: same colour/tint settings as the DeskContainers, and a text colour that follows the desktop behind them.
        private static Control BuildWidgetsTransparencyRows()
        {
            var col = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            col.Controls.Add(SettingsView.Checkbox(Lx("Πλήρης διαφάνεια: αυτόματη αντίθεση κειμένου με βάση το wallpaper πίσω από τα widgets (και όταν κινείται)", "Fully transparent: automatic text contrast from the wallpaper behind the widgets (also while it moves)"),
                AppSettings.Load().WidgetsAutoContrast, v => { var a = AppSettings.Load(); a.WidgetsAutoContrast = v; a.Save(); ThemeManager.RaiseRepainted(); }));
            FxCombo(col, Lx("Χρώμα επιφάνειας widgets", "Widget surface colour"), WidgetTheme.ColorNames, AppSettings.Load().WidgetsTintColor,
                v => { var a = AppSettings.Load(); a.WidgetsTintColor = v; a.Save(); ThemeManager.RaiseRepainted(); });
            col.Controls.Add(SettingsView.NumericRow(Lx("Απόχρωση (0 = πλήρως διάφανο)", "Tint (0 = fully transparent)"), Math.Clamp(AppSettings.Load().WidgetsTintPercent, 0, 100), 0, 100,
                v => { var a = AppSettings.Load(); a.WidgetsTintPercent = v; a.Save(); ThemeManager.NotifyChanged(); }, 0, "%"));
            return col;
        }
        private void BuildFencesSettings(Control panel)
        {
            // ---- Engine ----
            var engine = AddCardSection(panel, Lx("Μηχανή DeskContainers", "DeskContainers engine"));
            engine.Controls.Add(CardCheck(Lx("Χρήση της νέας μηχανής DeskContainers (προτεινόμενο)", "Use the new DeskContainers engine (recommended)"), AppSettings.Load().UseFencesEngine, v =>
            {
                var a = AppSettings.Load(); a.UseFencesEngine = v; a.Save();
                if (v) { DeskContainerHostEngine.Instance.CloseAll(); FencesEngine.Start(); }
                else FencesEngine.Stop();
                _statusLabel.Text = Lx("Άνοιξε ξανά αυτή τη σελίδα για να δεις τα αντίστοιχα χειριστήρια.", "Reopen this page to see the matching controls.");
            }));
            FxButtons(engine,
                (Lx("Έναρξη", "Start"), () => FencesEngine.Start()),
                (Lx("Διακοπή", "Stop"), () => FencesEngine.Stop()),
                (Lx("Επανεκκίνηση", "Restart"), () => { FencesEngine.Stop(); FencesEngine.Start(); }));

            if (!FencesEngine.Enabled) return;

            // ---- General ----
            var general = AddCardSection(panel, Lx("Γενικά", "General"));
            FxBool(general, Lx("Άνοιγμα με ένα κλικ", "Single click to launch"), "SingleClickToLaunch", true);
            FxBool(general, Lx("Το εικονίδιο που σύρεται μέσα σε DeskContainer φεύγει από την επιφάνεια εργασίας (κρύβεται, επανέρχεται όταν αφαιρεθεί)", "A desktop icon dropped into a DeskContainer leaves the desktop (hidden, restored when removed)"), "HideDesktopOriginalsOnDrop", true);
            FxBool(general, Lx("Μαγνητική στοίχιση κοντά σε άλλα fences", "Snap near fences"), "IsSnapEnabled", true);
            FxBool(general, Lx("Στοίχιση διαστάσεων", "Dimension snap"), "EnableDimensionSnap", false);
            FxBool(general, Lx("Εικονίδιο στην περιοχή ειδοποιήσεων", "Tray icon"), "ShowInTray", true);
            FxBool(general, Lx("Κάδος ανακύκλωσης στα Portal fences (διαγραφή στοιχείου)", "Use the Recycle Bin on Portal fences (delete item)"), "UseRecycleBin", true);
            FxBool(general, Lx("«Νέο Fence» στο μενού της επιφάνειας εργασίας", "Show 'New Fence' in the desktop context menu"), "EnableContextMenu", false);
            FxBool(general, Lx("Αυτόματη απόκρυψη fences μετά από αδράνεια", "Auto-hide fences after inactivity"), "AutoHideFences", false);
            FxNum(general, Lx("Χρόνος αδράνειας", "Idle time"), "AutoHideTime", 60, 1, 300, Lx("δευτ.", "sec"));

            // ---- Style ----
            var style = AddCardSection(panel, Lx("Εμφάνιση", "Style"));
            FxBool(style, Lx("Υδατογράφημα στα Portal fences", "Portal fences watermark"), "ShowBackgroundImageOnPortalFences", true);
            FxBool(style, Lx("Απόκρυψη scrollbars στα fences", "Disable fence scrollbars"), "DisableFenceScrollbars", false);
            FxBool(style, Lx("Ήχοι", "Sounds"), "EnableSounds", true);
            FxBool(style, Lx("Chameleon: χρώμα από το wallpaper", "Chameleon mode (match the wallpaper colour)"), "EnableChameleonMode", false);
            FxBool(style, Lx("Αυτόματο μάζεμα: τα fences μαζεύονται όταν φεύγει το ποντίκι και ανοίγουν με hover", "Auto roll-up (fences roll up when the mouse leaves and expand on hover)"), "AutoRollUpOnHover", false);
            FxNum(style, Lx("Απόχρωση fence", "Fence tint"), "TintValue", 80, 0, 100, "%");
            FxNum(style, Lx("Διαφάνεια εικονιδίων μενού", "Menu tint"), "MenuTintValue", 85, 0, 100, "%");
            FxCombo(style, Lx("Χρώμα", "Colour"), FxColors, FencesBridge.GetString("SelectedColor", "Black"), v => FencesBridge.Set("SelectedColor", v));
            FxCombo(style, Lx("Χρώμα γραμμής τίτλου", "Title bar colour"), new[] { "Auto" }.Concat(FxColors).ToArray(), FencesBridge.GetString("TitleBarColor", "Auto"), v => FencesBridge.Set("TitleBarColor", v));
            FxNum(style, Lx("Αδιαφάνεια γραμμής τίτλου", "Title bar opacity"), "TitleBarOpacity", 90, 0, 100, "%");
            FxNum(style, Lx("Ύψος γραμμής τίτλου", "Title bar height"), "TitleBarHeight", 28, 24, 64, "px");
            FxCombo(style, Lx("Εφέ εκκίνησης", "Launch effect"), FxEffects, FencesBridge.GetString("LaunchEffect", "Zoom"), v => FencesBridge.Set("LaunchEffect", v));
            FxCombo(style, Lx("Εικονίδιο μενού", "Menu icon"), FxMenuIcons, FxMenuIcons[Math.Clamp(FencesBridge.GetInt("MenuIcon", 1), 0, 3)], v => FencesBridge.Set("MenuIcon", Array.IndexOf(FxMenuIcons, v)));

            // ---- Tools ----
            var tools = AddCardSection(panel, Lx("Εργαλεία", "Tools"));
            FxButtons(tools,
                (Lx("Αντίγραφο ασφαλείας", "Backup"), () => FencesBridge.Command("backup")),
                (Lx("Επαναφορά…", "Restore…"), () => FencesBridge.Command("restore")),
                (Lx("Φάκελος αντιγράφων", "Open backups folder"), () => FencesBridge.Command("open-backups")));
            FxBool(tools, Lx("Αυτόματο καθημερινό αντίγραφο", "Automatic backup (daily)"), "EnableAutoBackup", true);
            FxButtons(tools,
                (Lx("Επαναφορά fences εντός οθόνης", "Screen bound fences"), () => FencesBridge.Command("screen-bound")),
                (Lx("Επαναφορά εμφάνισης", "Reset styles"), () =>
                {
                    if (MessageBox.Show(this, Lx("Να επαναφερθούν όλες οι προσαρμογές εμφάνισης των fences;", "Reset all visual customizations of the fences?"), "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        FencesBridge.Command("reset-styles");
                }),
                (Lx("Διαγραφή όλων των δεδομένων", "Clear all data"), () => FencesBridge.Command("factory-reset")));

            // ---- Profiles ----
            var profiles = AddCardSection(panel, Lx("Προφίλ", "Profiles"));
            FxButtons(profiles,
                (Lx("Διαχείριση προφίλ", "Manage profiles"), () => FencesBridge.Command("manage-profiles")),
                (Lx("Διαχείριση αυτοματισμών", "Manage automation"), () => FencesBridge.Command("manage-automation")));
            FxBool(profiles, Lx("Αυτόματη εναλλαγή προφίλ (αυτοματισμοί)", "Enable profile automation"), "EnableProfileAutomation", false);

            // ---- Hotkeys ----
            var keys = AddCardSection(panel, Lx("Συντομεύσεις πληκτρολογίου", "Hotkeys"));
            AddText(keys, Lx("Οι αλλαγές στις συντομεύσεις εφαρμόζονται μετά από επανεκκίνηση της μηχανής (κουμπί «Επανεκκίνηση» παραπάνω).", "Hotkey changes apply after the engine restarts (use 'Restart' above)."));
            FxHotkey(keys, Lx("Εναλλαγή προφίλ: άμεσα [0-9]", "Profile switching: direct [0-9]"), "EnableProfileHotkeys", "ProfileSwitchModifier", null, 0, "Control, Alt");
            FxCombo(keys, Lx("Προηγούμενο προφίλ", "Previous profile"), FxModifiers, FencesBridge.GetString("ProfilePrevModifier", "Control, Alt"), v => FencesBridge.Set("ProfilePrevModifier", v));
            FxCombo(keys, Lx("Πλήκτρο", "Key"), FxKeys.Select(k => k.Name).ToArray(), FxKeys.FirstOrDefault(k => k.Vk == FencesBridge.GetInt("ProfilePrevKey", 188)).Name ?? "Comma (,)", v => FencesBridge.Set("ProfilePrevKey", FxKeys.First(k => k.Name == v).Vk));
            FxCombo(keys, Lx("Επόμενο προφίλ", "Next profile"), FxModifiers, FencesBridge.GetString("ProfileNextModifier", "Control, Alt"), v => FencesBridge.Set("ProfileNextModifier", v));
            FxCombo(keys, Lx("Πλήκτρο", "Key"), FxKeys.Select(k => k.Name).ToArray(), FxKeys.FirstOrDefault(k => k.Vk == FencesBridge.GetInt("ProfileNextKey", 190)).Name ?? "Period (.)", v => FencesBridge.Set("ProfileNextKey", FxKeys.First(k => k.Name == v).Vk));
            FxHotkey(keys, Lx("Εστίαση σε fence", "Focus fence"), "EnableFocusFenceHotkey", "FocusFenceModifier", "FocusFenceKey", 90, "Control, Alt");
            FxHotkey(keys, Lx("Γρήγορη αναζήτηση (Spot Search)", "Spot Search"), "EnableSpotSearchHotkey", "SpotSearchModifier", "SpotSearchKey", 192, "Control");

            // ---- Smart Desktop ----
            var smart = AddCardSection(panel, Lx("Έξυπνη επιφάνεια εργασίας (αυτόματη οργάνωση)", "Smart desktop (auto-organize)"));
            FxBool(smart, Lx("Ενεργοποίηση αυτόματης οργάνωσης", "Enable auto-organize"), "EnableAutoOrganize", false);
            FxBool(smart, Lx("Ειδοποιήσεις όταν εφαρμόζεται κανόνας", "Show execution toast notifications"), "EnableAutoOrganizeNotifications", true);
            FxButtons(smart,
                (Lx("Κανόνες…", "Rules…"), () => FencesBridge.Command("smart-rules")),
                (Lx("Οργάνωση τώρα", "Organize now"), () =>
                {
                    if (MessageBox.Show(this, Lx("Τα υπάρχοντα αρχεία της επιφάνειας εργασίας θα μετακινηθούν στους φακέλους προορισμού σύμφωνα με τους κανόνες σου.\n\nΣυνέχεια;", "This moves existing files on your desktop to your target folders based on your rules.\n\nProceed?"), "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        FencesBridge.Command("organize-now");
                }));

            // ---- Logging ----
            var log = AddCardSection(panel, Lx("Αρχείο καταγραφής", "Logging"));
            FxBool(log, Lx("Ενεργοποίηση καταγραφής", "Enable logging"), "IsLogEnabled", false);
            FxCombo(log, Lx("Επίπεδο", "Level"), FxLogLevels, FencesBridge.GetString("MinLogLevel", "Info"), v => FencesBridge.Set("MinLogLevel", v));
            var cats = FencesBridge.GetStrings("EnabledLogCategories", new[] { "General", "Error", "ImportExport", "Settings" });
            foreach (var cat in FxLogCategories)
            {
                string captured = cat;
                log.Controls.Add(CardCheck(captured, cats.Contains(captured), on =>
                {
                    if (on && !cats.Contains(captured)) cats.Add(captured); else if (!on) cats.Remove(captured);
                    if (!cats.Contains("Error")) cats.Add("Error");
                    FencesBridge.SetStrings("EnabledLogCategories", cats);
                }));
            }
            FxButtons(log, (Lx("Άνοιγμα αρχείου καταγραφής", "Open log"), () => FencesBridge.Command("open-log")));
        }
    }
}
