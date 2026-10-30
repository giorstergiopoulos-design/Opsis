using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    // Wallpaper modes "Page" (a website, a YouTube link or a local .html wallpaper with its own properties) and "Image" (GIF / PNG / JPG),
    // wallpaper packages (.zip import / export) and the extra pause conditions of the Wallpaper page.
    public sealed partial class MainWindow
    {
        private static string L(string en, string el) => LocalizationManager.CurrentLanguage == "el" ? el : en;

        private HoverButton MediaButton(string text)
        {
            var b = new HoverButton
            {
                Text = text, AutoSize = true, Padding = new Padding(16, 6, 16, 6), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = UiTheme.AccentBlue,
                BaseColor = UiTheme.AccentBlue, HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f), TabStop = false, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 8, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void BuildMediaWallpaperSection(FlowLayoutPanel panel, WallpaperSettings settings)
        {
            bool page = settings.Mode == "Page";
            AddSection(panel, LocalizationManager.T(page ? "Wallpaper.SectionPage" : "Wallpaper.SectionImage"));
            AddText(panel, LocalizationManager.T(page ? "Wallpaper.PageIntro" : "Wallpaper.ImageIntro"));

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            var box = new TextBox
            {
                Width = 380, Text = page ? settings.PageSource : settings.ImagePath,
                BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, Margin = new Padding(0, 5, 10, 0)
            };
            var browse = MediaButton(LocalizationManager.T("Wallpaper.Browse"));
            browse.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = page ? LocalizationManager.T("Wallpaper.FilterPage") : LocalizationManager.T("Wallpaper.FilterImage"),
                    Title = LocalizationManager.T("Wallpaper.Browse")
                };
                if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
            };
            var apply = MediaButton(LocalizationManager.T("Wallpaper.Apply"));
            apply.Click += (_, _) =>
            {
                var s = WallpaperSettings.Load();
                if (page) s.PageSource = box.Text.Trim(); else s.ImagePath = box.Text.Trim();
                s.Save();
                WallpaperHostEngine.Instance.Enable();
                WallpaperHostEngine.Instance.RefreshAll();
                if (page) ShowWallpaper();      // the property list depends on the page
            };
            row.Controls.Add(box); row.Controls.Add(browse); row.Controls.Add(apply);
            panel.Controls.Add(row);

            if (!page)
            {
                var fit = new FlatComboBox { Width = 200, Margin = new Padding(0, 0, 0, 10) };
                fit.SetItems(new[] { "Cover", "Contain" }, string.IsNullOrEmpty(settings.ImageFit) ? "Cover" : settings.ImageFit);
                fit.SelectedIndexChanged += (_, _) =>
                {
                    var s = WallpaperSettings.Load(); s.ImageFit = fit.SelectedItem ?? "Cover"; s.Save();
                    WallpaperHostEngine.Instance.RefreshAll();
                };
                panel.Controls.Add(fit);
                return;
            }

            BuildPackageRow(panel);
            BuildPageProperties(panel, settings);
        }

        // ---- "Application" wallpaper -------------------------------------------------------------------------------------------------------------
        private void BuildApplicationSection(FlowLayoutPanel panel, WallpaperSettings settings)
        {
            AddSection(panel, L("Application wallpaper", "Wallpaper από εφαρμογή"));
            AddText(panel, L("Choose a program (.exe) with its own window: Opsis starts it, shows its window as the wallpaper behind the icons, pauses it when the wallpaper pauses and closes it when you change the mode. Works best with programs made to run full screen without input (scenes, visualisers).",
                             "Διάλεξε ένα πρόγραμμα (.exe) με δικό του παράθυρο: το Opsis το ανοίγει, δείχνει το παράθυρό του ως wallpaper πίσω από τα εικονίδια, το παγώνει όταν παγώνει το wallpaper και το κλείνει όταν αλλάζεις τρόπο. Ταιριάζει σε προγράμματα φτιαγμένα να τρέχουν σε πλήρη οθόνη χωρίς είσοδο (σκηνές, visualisers)."));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            var path = new TextBox { Width = 380, Text = settings.AppWallpaperPath, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, Margin = new Padding(0, 5, 10, 0) };
            var browse = MediaButton(LocalizationManager.T("Wallpaper.Browse"));
            browse.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = browse.Text };
                if (dlg.ShowDialog(this) == DialogResult.OK) path.Text = dlg.FileName;
            };
            row.Controls.Add(path); row.Controls.Add(browse);
            panel.Controls.Add(row);
            var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            row2.Controls.Add(new Label { Text = L("Arguments", "Παράμετροι"), AutoSize = false, Width = 90, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
            var args = new TextBox { Width = 290, Text = settings.AppWallpaperArgs, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, Margin = new Padding(0, 3, 10, 0) };
            var apply = MediaButton(LocalizationManager.T("Wallpaper.Apply"));
            apply.Click += (_, _) =>
            {
                var s = WallpaperSettings.Load(); s.AppWallpaperPath = path.Text.Trim().Trim('"'); s.AppWallpaperArgs = args.Text.Trim(); s.Save();
                WallpaperHostEngine.Instance.Enable();
                WallpaperHostEngine.Instance.RefreshAll();
            };
            row2.Controls.Add(args); row2.Controls.Add(apply);
            panel.Controls.Add(row2);
        }

        // ---- packages and samples ----------------------------------------------------------------------------------------------------------
        private void BuildPackageRow(FlowLayoutPanel panel)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 10), MaximumSize = new Size(760, 0) };
            var import = MediaButton(L("Import package (.zip)…", "Εισαγωγή πακέτου (.zip)…"));
            import.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "Wallpaper package (*.zip)|*.zip", Title = import.Text };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string index = WallpaperPackages.Import(dlg.FileName);
                    var s = WallpaperSettings.Load(); s.PageSource = index; s.Save();
                    WallpaperHostEngine.Instance.Enable(); WallpaperHostEngine.Instance.RefreshAll();
                    ShowWallpaper();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            var export = MediaButton(L("Export package…", "Εξαγωγή πακέτου…"));
            export.Click += (_, _) =>
            {
                string? index = MediaSource.LocalPagePath(WallpaperSettings.Load().PageSource);
                if (index == null) { MessageBox.Show(this, L("Choose a local .html wallpaper first.", "Διάλεξε πρώτα ένα τοπικό wallpaper .html."), "Opsis"); return; }
                using var dlg = new SaveFileDialog { Filter = "Wallpaper package (*.zip)|*.zip", FileName = Path.GetFileName(Path.GetDirectoryName(index)) + ".zip" };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { WallpaperPackages.Export(index, dlg.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            row.Controls.Add(import); row.Controls.Add(export);

            var samples = FindSamples();
            if (samples.Count > 0)
            {
                var combo = new FlatComboBox { Width = 220, Margin = new Padding(8, 0, 8, 0) };
                combo.SetItems(samples.Select(s => s.Name), samples[0].Name);
                var use = MediaButton(L("Use sample", "Χρήση δείγματος"));
                use.Click += (_, _) =>
                {
                    var pick = samples.FirstOrDefault(s => s.Name == combo.SelectedItem);
                    if (pick.Path == null) return;
                    var s = WallpaperSettings.Load(); s.PageSource = pick.Path; s.Save();
                    WallpaperHostEngine.Instance.Enable(); WallpaperHostEngine.Instance.RefreshAll();
                    ShowWallpaper();
                };
                row.Controls.Add(combo); row.Controls.Add(use);
            }
            panel.Controls.Add(row);
        }

        private static List<(string Name, string? Path)> FindSamples()
        {
            var list = new List<(string, string?)>();
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallpapers", "samples");
                if (!Directory.Exists(dir)) return list;
                foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(x => x))
                {
                    string index = Path.Combine(d, "index.html");
                    if (!File.Exists(index)) continue;
                    var m = WallpaperManifest.LoadFor(index);
                    list.Add((string.IsNullOrWhiteSpace(m?.Name) ? Path.GetFileName(d) : m!.Name, index));
                }
            }
            catch (Exception) { }
            return list;
        }

        // ---- properties of the loaded web wallpaper (opsis-wallpaper.json) ---------------------------------------------------------------------
        private void BuildPageProperties(FlowLayoutPanel panel, WallpaperSettings settings)
        {
            string? index = MediaSource.LocalPagePath(settings.PageSource);
            var manifest = WallpaperManifest.LoadFor(index);
            if (index == null || manifest == null || manifest.Properties.Count == 0) return;

            string title = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileName(Path.GetDirectoryName(index)) ?? "" : manifest.Name;
            AddSection(panel, string.Format(L("Settings of “{0}”", "Ρυθμίσεις του «{0}»"), title));
            if (!string.IsNullOrWhiteSpace(manifest.Description)) AddText(panel, manifest.Description);
            bool el = LocalizationManager.CurrentLanguage == "el";

            void Save(WallpaperProperty p, string value)
            {
                var s = WallpaperSettings.Load();
                WallpaperManifest.SetValue(s, index, p.Key, value);
                s.Save();
                WallpaperHostEngine.Instance.PushProperties();
            }

            foreach (var p in manifest.Properties)
            {
                var prop = p;
                string label = el && !string.IsNullOrWhiteSpace(prop.LabelEl) ? prop.LabelEl : (string.IsNullOrWhiteSpace(prop.Label) ? prop.Key : prop.Label);
                string current = WallpaperManifest.GetValue(settings, index, prop);
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                var caption = new Label { Text = label, AutoSize = false, Width = 220, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 8, 0) };
                row.Controls.Add(caption);

                switch (prop.Type)
                {
                    case "slider":
                    {
                        double step = prop.Step > 0 ? prop.Step : 1;
                        int min = (int)Math.Round(prop.Min / step), max = (int)Math.Round(prop.Max / step);
                        double.TryParse(current, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cur);
                        var slider = new FlatSlider { Width = 240, Height = 26, Minimum = min, Maximum = Math.Max(min + 1, max), Margin = new Padding(0, 2, 8, 0) };
                        slider.Value = Math.Clamp((int)Math.Round(cur / step), slider.Minimum, slider.Maximum);
                        var value = new Label { AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 6, 0, 0) };
                        string Fmt() => (slider.Value * step).ToString(step < 1 ? "0.##" : "0", System.Globalization.CultureInfo.InvariantCulture);
                        value.Text = Fmt();
                        var debounce = new System.Windows.Forms.Timer { Interval = 200 };
                        debounce.Tick += (_, _) => { debounce.Stop(); Save(prop, (slider.Value * step).ToString(System.Globalization.CultureInfo.InvariantCulture)); };
                        slider.ValueChanged += (_, _) => { value.Text = Fmt(); debounce.Stop(); debounce.Start(); };
                        row.Controls.Add(slider); row.Controls.Add(value);
                        break;
                    }
                    case "checkbox":
                    {
                        var cb = new FlatCheckBox { Text = "", AutoSize = false, Width = 24, Height = 24, Checked = current.Equals("true", StringComparison.OrdinalIgnoreCase), BackColor = UiTheme.Background, Margin = new Padding(0, 4, 0, 0) };
                        cb.CheckedChanged += (_, _) => Save(prop, cb.Checked ? "true" : "false");
                        row.Controls.Add(cb);
                        break;
                    }
                    case "color":
                    {
                        Color Parse(string t) { try { return ColorTranslator.FromHtml(t); } catch (Exception) { return Color.White; } }
                        var btn = new Button { Width = 60, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Parse(current), Margin = new Padding(0, 2, 0, 0), Cursor = Cursors.Hand, TabStop = false };
                        btn.FlatAppearance.BorderColor = UiTheme.Border;
                        btn.Click += (_, _) =>
                        {
                            using var dlg = new ColorDialog { FullOpen = true, Color = btn.BackColor };
                            if (dlg.ShowDialog(this) != DialogResult.OK) return;
                            btn.BackColor = dlg.Color;
                            Save(prop, $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
                        };
                        row.Controls.Add(btn);
                        break;
                    }
                    case "combo":
                    {
                        var combo = new FlatComboBox { Width = 220 };
                        var opts = prop.Options.Count > 0 ? prop.Options : new List<string> { current };
                        combo.SetItems(opts, opts.Contains(current) ? current : opts[0]);
                        combo.SelectedIndexChanged += (_, _) => Save(prop, combo.SelectedItem ?? "");
                        row.Controls.Add(combo);
                        break;
                    }
                    default:
                    {
                        var tb = new TextBox { Width = 260, Text = current, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, Margin = new Padding(0, 4, 0, 0) };
                        tb.Leave += (_, _) => Save(prop, tb.Text);
                        row.Controls.Add(tb);
                        break;
                    }
                }
                panel.Controls.Add(row);
            }
        }

        // ---- wallpaper elements (widget pieces on top of any mode) ------------------------------------------------------------------------------
        private static string ElementName(string id) => id switch
        {
            "analog" => L("Analog clock", "Αναλογικό ρολόι"),
            "cpu" => "CPU",
            "ram" => L("Memory", "Μνήμη"),
            "gpu" => "GPU",
            "network" => L("Network", "Δίκτυο"),
            "disks" => L("Disks", "Δίσκοι"),
            "battery" => L("Battery", "Μπαταρία"),
            "audio" => L("Audio spectrum", "Φάσμα ήχου"),
            _ => id,
        };

        private void BuildElementsSection(FlowLayoutPanel panel, WallpaperSettings settings)
        {
            settings.EnsureElements();
            AddSection(panel, L("Wallpaper elements", "Στοιχεία στο wallpaper"));
            AddText(panel, L("Choose which pieces of the widgets are drawn on the wallpaper, on the left or the right edge of the screen. They work over every wallpaper mode and stop with it when it pauses.",
                             "Διάλεξε ποια κομμάτια των widgets θα εμφανίζονται πάνω στο wallpaper, στην αριστερή ή στη δεξιά πλευρά της οθόνης. Δουλεύουν πάνω από κάθε τρόπο wallpaper και σταματούν όταν σταματά το wallpaper."));

            // one debounce for all sliders: save the whole list, then redraw only the elements (the wallpaper itself is not restarted)
            var debounce = new System.Windows.Forms.Timer { Interval = 150 };
            Action? pending = null;
            debounce.Tick += (_, _) => { debounce.Stop(); pending?.Invoke(); pending = null; };
            void Commit(Action<WallpaperSettings> change)
            {
                var s = WallpaperSettings.Load(); s.EnsureElements(); change(s); s.Save();
                WallpaperHostEngine.Instance.RefreshElements();
            }

            // side of the screen and vertical alignment: all elements form ONE column on the chosen edge
            var place = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            place.Controls.Add(new Label { Text = L("Show on the", "Εμφάνιση στη"), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 8, 6, 0) });
            var side = new FlatComboBox { Width = 180, Margin = new Padding(0, 0, 14, 0) };
            string[] sNames = { L("Left side", "Αριστερή πλευρά"), L("Right side", "Δεξιά πλευρά"), L("Centre", "Κέντρο") };
            side.SetItems(sNames, settings.ElementsSide == "Right" ? sNames[1] : settings.ElementsSide == "Center" ? sNames[2] : sNames[0]);
            side.SelectedIndexChanged += (_, _) => Commit(s => s.ElementsSide = side.SelectedIndex == 1 ? "Right" : side.SelectedIndex == 2 ? "Center" : "Left");
            var valign = new FlatComboBox { Width = 130 };
            string[] vNames = { L("Top", "Πάνω"), L("Middle", "Μέση"), L("Bottom", "Κάτω") };
            valign.SetItems(vNames, settings.ElementsVAlign == "Top" ? vNames[0] : settings.ElementsVAlign == "Bottom" ? vNames[2] : vNames[1]);
            valign.SelectedIndexChanged += (_, _) => Commit(s => s.ElementsVAlign = valign.SelectedIndex == 0 ? "Top" : valign.SelectedIndex == 2 ? "Bottom" : "Center");
            place.Controls.Add(side); place.Controls.Add(valign);
            panel.Controls.Add(place);

            foreach (var el in settings.Elements)
            {
                var id = el.Id;
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
                var cb = new FlatCheckBox { Text = ElementName(id), AutoSize = false, Width = 190, Height = 28, Checked = el.Enabled, BackColor = UiTheme.Background, Margin = new Padding(0, 4, 8, 0) };
                cb.CheckedChanged += (_, _) => Commit(s => s.Elements.First(e => e.Id == id).Enabled = cb.Checked);
                row.Controls.Add(cb);

                void AddSlider(string caption, int min, int max, int value, Action<WallpaperElement, int> set)
                {
                    row.Controls.Add(new Label { Text = caption, AutoSize = false, Width = 70, Height = 26, TextAlign = ContentAlignment.MiddleRight, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 2, 2, 0) });
                    var sl = new FlatSlider { Width = 110, Height = 26, Minimum = min, Maximum = max, Margin = new Padding(0, 2, 6, 0) };
                    sl.Value = Math.Clamp(value, min, max);
                    sl.ValueChanged += (_, _) => { int v = sl.Value; pending = () => Commit(s => set(s.Elements.First(e => e.Id == id), v)); debounce.Stop(); debounce.Start(); };
                    row.Controls.Add(sl);
                }
                AddSlider(L("Size", "Μέγεθος"), 40, 250, el.Scale, (e, v) => e.Scale = v);
                panel.Controls.Add(row);
            }

            var opt = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 10) };
            var card = new FlatToggle { RowWidth = 440, Text = L("Frosted card behind the elements", "Θολό πλαίσιο πίσω από τα στοιχεία"),  Checked = settings.ElementsCard, BackColor = UiTheme.Background, Margin = new Padding(0, 4, 8, 0) };
            card.CheckedChanged += (_, _) => Commit(s => s.ElementsCard = card.Checked);
            var accent = new Button { Width = 60, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 8, 0), Cursor = Cursors.Hand, TabStop = false };
            Color Parse(string t) { try { return ColorTranslator.FromHtml(t); } catch (Exception) { return Color.FromArgb(0, 210, 255); } }
            accent.BackColor = Parse(settings.ElementsAccent);
            accent.FlatAppearance.BorderColor = UiTheme.Border;
            accent.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = accent.BackColor };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                accent.BackColor = dlg.Color;
                Commit(s => s.ElementsAccent = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
            };
            opt.Controls.Add(card);
            opt.Controls.Add(new Label { Text = L("Accent colour", "Χρώμα έμφασης"), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 8, 6, 0) });
            opt.Controls.Add(accent);
            panel.Controls.Add(opt);

            // clock theme and bar colour (same theme names as the desktop clock widget)
            var look = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            look.Controls.Add(new Label { Text = L("Clock theme", "Θέμα ρολογιού"), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 8, 6, 0) });
            var clockTheme = new FlatComboBox { Width = 130, Margin = new Padding(0, 0, 14, 0) };
            string[] themes = { "Classic", "Minimal", "Neon", "Mono", "Retro" };
            clockTheme.SetItems(themes, Array.IndexOf(themes, settings.ElementsClockTheme) >= 0 ? settings.ElementsClockTheme : "Classic");
            clockTheme.SelectedIndexChanged += (_, _) => Commit(s => s.ElementsClockTheme = themes[Math.Clamp(clockTheme.SelectedIndex, 0, themes.Length - 1)]);
            var barColor = new Button { Width = 60, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 8, 0), Cursor = Cursors.Hand, TabStop = false };
            barColor.BackColor = string.IsNullOrEmpty(settings.ElementsBarColor) ? accent.BackColor : Parse(settings.ElementsBarColor);
            barColor.FlatAppearance.BorderColor = UiTheme.Border;
            barColor.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = barColor.BackColor };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                barColor.BackColor = dlg.Color;
                Commit(s => s.ElementsBarColor = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
            };
            var barReset = new Button { Text = L("Same as accent", "Ίδιο με την έμφαση"), AutoSize = true, FlatStyle = FlatStyle.Flat, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 0) };
            barReset.FlatAppearance.BorderColor = UiTheme.Border;
            barReset.Click += (_, _) => { barColor.BackColor = accent.BackColor; Commit(s => s.ElementsBarColor = ""); };
            look.Controls.Add(clockTheme);
            look.Controls.Add(new Label { Text = L("Bar colour", "Χρώμα μπαρών"), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 8, 6, 0) });
            look.Controls.Add(barColor); look.Controls.Add(barReset);
            panel.Controls.Add(look);
        }

        // ---- Discord Rich Presence (Settings page) ---------------------------------------------------------------------------------------------
        private static void BuildDiscordRows(Control stack)
        {
            var s = AppSettings.Load();
            stack.Controls.Add(SettingsView.Checkbox(L("Show what Opsis is doing on my Discord profile (Rich Presence)", "Εμφάνιση στο προφίλ μου στο Discord τι κάνει το Opsis (Rich Presence)"), s.DiscordPresenceEnabled,
                v => { var a = AppSettings.Load(); a.DiscordPresenceEnabled = v; a.Save(); DiscordPresence.Instance.Apply(); }));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label { Text = L("Discord Application ID", "Application ID του Discord"), AutoSize = false, Width = 210, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
            var box = new TextBox { Width = 230, Text = s.DiscordApplicationId, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, Margin = new Padding(0, 3, 0, 0) };
            box.Leave += (_, _) => { var a = AppSettings.Load(); a.DiscordApplicationId = box.Text.Trim(); a.Save(); DiscordPresence.Instance.Apply(); };
            row.Controls.Add(box);
            stack.Controls.Add(row);
            stack.Controls.Add(new Label
            {
                Text = L("Create a free application at discord.com/developers/applications and paste its Application ID. Only the wallpaper mode and whether it is paused are shown.",
                         "Φτιάξε μια δωρεάν εφαρμογή στο discord.com/developers/applications και κόλλησε εδώ το Application ID της. Εμφανίζεται μόνο ο τρόπος του wallpaper και αν είναι σε παύση."),
                AutoSize = false, Width = 640, Height = 38, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 8)
            });
        }

        // ---- extra pause conditions (Wallpaper page) -------------------------------------------------------------------------------------------
        private void BuildLoadPauseRows(FlowLayoutPanel panel)
        {
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.PauseOnScreensaver"), AppSettings.Load().PauseOnScreensaver,
                v => { var a = AppSettings.Load(); a.PauseOnScreensaver = v; a.Save(); }));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.KeepAwake"), AppSettings.Load().KeepPcAwake,
                v => { var a = AppSettings.Load(); a.KeepPcAwake = v; a.Save(); }));
            AddLoadRow(panel, L("Pause when CPU load is above", "Παύση όταν το φορτίο CPU ξεπερνά"), a => a.PauseCpuAbove, (a, v) => a.PauseCpuAbove = v);
            AddLoadRow(panel, L("Pause when GPU load is above", "Παύση όταν το φορτίο GPU ξεπερνά"), a => a.PauseGpuAbove, (a, v) => a.PauseGpuAbove = v);
            AddLoadRow(panel, L("Pause when memory use is above", "Παύση όταν η χρήση μνήμης ξεπερνά"), a => a.PauseRamAbove, (a, v) => a.PauseRamAbove = v);
        }

        private void AddLoadRow(FlowLayoutPanel panel, string label, Func<AppSettings, int> get, Action<AppSettings, int> set)
        {
            string off = L("Off", "Ανενεργό");
            var items = new[] { off, "60%", "70%", "80%", "90%" };
            int cur = get(AppSettings.Load());
            string selected = cur is 60 or 70 or 80 or 90 ? cur + "%" : off;
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label { Text = label, AutoSize = false, Width = 300, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
            var combo = new FlatComboBox { Width = 120 };
            combo.SetItems(items, selected);
            combo.SelectedIndexChanged += (_, _) =>
            {
                var a = AppSettings.Load();
                set(a, combo.SelectedItem is { } t && t.EndsWith('%') ? int.Parse(t.TrimEnd('%')) : 0);
                a.Save();
            };
            row.Controls.Add(combo);
            panel.Controls.Add(row);
        }
    }
}
