using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    // Wallpaper modes "Page" (a website, a YouTube link or a local .html file) and "Image" (GIF / PNG / JPG).
    public sealed partial class MainWindow
    {
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
            HoverButton Btn(string text) => new()
            {
                Text = text, AutoSize = true, Padding = new Padding(16, 6, 16, 6), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = UiTheme.AccentBlue,
                BaseColor = UiTheme.AccentBlue, HoverBackColor = ControlPaint.Light(UiTheme.AccentBlue, 0.15f), TabStop = false, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 8, 0)
            };
            var browse = Btn(LocalizationManager.T("Wallpaper.Browse"));
            browse.FlatAppearance.BorderSize = 0;
            browse.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = page ? LocalizationManager.T("Wallpaper.FilterPage") : LocalizationManager.T("Wallpaper.FilterImage"),
                    Title = LocalizationManager.T("Wallpaper.Browse")
                };
                if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
            };
            var apply = Btn(LocalizationManager.T("Wallpaper.Apply"));
            apply.FlatAppearance.BorderSize = 0;
            apply.Click += (_, _) =>
            {
                var s = WallpaperSettings.Load();
                if (page) s.PageSource = box.Text.Trim(); else s.ImagePath = box.Text.Trim();
                s.Save();
                WallpaperHostEngine.Instance.Enable();
                WallpaperHostEngine.Instance.RefreshAll();
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
            }
        }
    }
}
