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
    // Settings of the DeskContainers engine , shown as cards on the DeskZones page instead of a separate
    // settings window. Values are read from / written to the engine's options.json through ContainersBridge; the engine applies them live.
    public sealed partial class MainWindow
    {
        private static string Lx(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;

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
        // DeskContainers on the DeskZones page: only the engine on/off control. EVERY other DeskContainers setting lives in the
        // three-bars menu of a container (Settings > General / Appearance / Tools / Profiles / Hotkeys / Smart desktop / Logging).
        private void BuildContainersSettings(Control panel)
        {
            var engine = AddCardSection(panel, LocalizationManager.T("DCM.EngineCard"));
            engine.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("DCM.EngineEnabled"), AppSettings.Load().DeskContainersEnabled, v =>
            {
                var a = AppSettings.Load(); a.DeskContainersEnabled = v; a.Save();
                if (v) ContainersEngine.Start(); else ContainersEngine.Stop();
            }));
            AddText(engine, LocalizationManager.T("DCM.SettingsHint"));

            // The buttons of the former built-in containers, now driving the DeskContainers engine through its bridge.
            AddIconButtonGrid(panel,
                ("Container", LocalizationManager.T("DeskZones.NewContainerBtn"), (_, _) => ContainersBridge.Command("new-container")),
                ("Container", LocalizationManager.T("DCX.NewNote"), (_, _) => ContainersBridge.Command("new-note")),
                ("Close", LocalizationManager.T("DeskZones.CloseAllContainers"), (_, _) => ContainersBridge.Command("quick-hide")),
                ("Restore", LocalizationManager.T("DCX.ShowAll"), (_, _) => ContainersBridge.Command("quick-show")));

            // delete a container: its desktop icons come back to the desktop
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 10) };
            var pick = new FlatComboBox { Width = 260, Margin = new Padding(0, 0, 10, 0) };
            ContainersBridge.ContainerInfo[] known = Array.Empty<ContainersBridge.ContainerInfo>();
            void RefreshList()
            {
                known = ContainersBridge.ReadState().Containers;
                var names = known.Select((c, i) => (string.IsNullOrWhiteSpace(c.Title) ? LocalizationManager.T("DeskZones.NewContainerDefaultTitle") : c.Title) + "  (" + (i + 1) + ")").ToArray();
                pick.SetItems(names.Length > 0 ? names : new[] { LocalizationManager.T("DCX.NoContainers") }, names.Length > 0 ? names[0] : LocalizationManager.T("DCX.NoContainers"));
            }
            RefreshList();
            pick.MouseEnter += (_, _) => { if (!pick.Focused) RefreshList(); };
            var del = new HoverButton { Text = LocalizationManager.T("DCX.DeleteContainer"), Width = 230, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
            del.FlatAppearance.BorderSize = 0;
            del.Click += (_, _) =>
            {
                RefreshList();
                int i = Math.Max(0, pick.SelectedIndex);
                if (i >= known.Length) return;
                var c = known[i];
                string name = string.IsNullOrWhiteSpace(c.Title) ? c.Id : c.Title;
                if (MessageBox.Show(this, string.Format(LocalizationManager.T("DCX.DeleteConfirm"), name), LocalizationManager.T("DCX.DeleteTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                ContainersBridge.Command("delete-container:" + c.Id);
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (_, _) => { t.Stop(); t.Dispose(); RefreshList(); };
                t.Start();
            };
            row.Controls.Add(pick); row.Controls.Add(del);
            panel.Controls.Add(row);

            // profiles (named container layouts) - save the current layout / apply a saved one
            var prow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var profiles = new FlatComboBox { Width = 260, Margin = new Padding(0, 0, 10, 0) };
            void RefreshProfiles() { var st = ContainersBridge.ReadState(); var p = st.Profiles.Length > 0 ? st.Profiles : new[] { st.Profile }; profiles.SetItems(p, p.Contains(st.Profile) ? st.Profile : p[0]); }
            RefreshProfiles();
            profiles.MouseEnter += (_, _) => { if (!profiles.Focused) RefreshProfiles(); };
            var apply = new HoverButton { Text = LocalizationManager.T("DCX.ProfileApply"), Width = 190, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            apply.FlatAppearance.BorderSize = 0;
            apply.Click += (_, _) => { if (profiles.SelectedItem is { Length: > 0 } n) ContainersBridge.Command("profile:" + n); };
            var save = new HoverButton { Text = LocalizationManager.T("DCX.ProfileSave"), Width = 230, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand };
            save.FlatAppearance.BorderSize = 0;
            save.Click += (_, _) =>
            {
                using var f = new Form { Text = LocalizationManager.T("DCX.ProfileName"), StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(360, 110), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
                var box = new TextBox { Location = new Point(14, 14), Width = 332 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(266, 62), Size = new Size(80, 30) };
                f.Controls.AddRange(new Control[] { box, ok }); f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK || box.Text.Trim().Length == 0) return;
                ContainersBridge.Command("create-profile:" + box.Text.Trim());
            };
            prow.Controls.Add(profiles); prow.Controls.Add(apply); prow.Controls.Add(save);
            panel.Controls.Add(prow);
        }
    }
}
