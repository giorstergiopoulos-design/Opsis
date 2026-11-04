using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    /// <summary>One app of the Opsis family as listed in suite/apps.json.</summary>
    public sealed record FamilyApp(string Id, string Name, string Repo, string Tagline, string[] Paths, string Owner)
    {
        /// <summary>First existing install path (environment variables expanded), or null when the app is not installed.</summary>
        public string? InstalledPath => Paths.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

        public string ReleasesUrl => $"https://github.com/{Owner}/{Repo}/releases/latest";

        /// <summary>Reads the family list (pure, testable). Unknown / broken entries are skipped.</summary>
        public static IReadOnlyList<FamilyApp> Parse(string json, string language)
        {
            var list = new List<FamilyApp>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                string owner = doc.RootElement.TryGetProperty("owner", out var o) ? o.GetString() ?? "" : "";
                foreach (var a in doc.RootElement.GetProperty("apps").EnumerateArray())
                {
                    string id = a.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string repo = a.TryGetProperty("repo", out var r) ? r.GetString() ?? "" : "";
                    if (id.Length == 0 || name.Length == 0) continue;
                    string tagline = "";
                    if (a.TryGetProperty("tagline", out var t) && t.ValueKind == JsonValueKind.Object)
                        tagline = (t.TryGetProperty(language, out var tl) ? tl.GetString() : null) ?? (t.TryGetProperty("en", out var te) ? te.GetString() : null) ?? "";
                    var paths = a.TryGetProperty("paths", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : Array.Empty<string>();
                    list.Add(new FamilyApp(id, name, repo, tagline, paths, owner));
                }
            }
            catch (Exception) { }
            return list;
        }

        public static IReadOnlyList<FamilyApp> Load()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "suite", "apps.json");
                return File.Exists(path) ? Parse(File.ReadAllText(path), LocalizationManager.CurrentLanguage) : Array.Empty<FamilyApp>();
            }
            catch (Exception) { return Array.Empty<FamilyApp>(); }
        }
    }

    /// <summary>The "Opsis family" window and the "Welcome guide" window: both belong to Opsis itself (plugins do not offer them when they are hosted by Opsis).</summary>
    internal static class FamilyWindows
    {
        private static Form NewForm(string title, Size size) => new()
        {
            Text = title, Size = size, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, ShowInTaskbar = true, Icon = SystemIcons.Application,
        };

        public static void ShowWelcome()
        {
            var open = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.Tag as string == "opsis-welcome");
            if (open != null) { open.Activate(); return; }
            var f = NewForm(OpsisStrings.T("ui.welcomeTitle"), new Size(560, 420)); f.Tag = "opsis-welcome";
            var text = new Label { Text = LocalizationManager.T("Welcome.Body"), Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12), ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10.5f) };
            var close = new Opsis.UI.FlatButton { Text = OpsisStrings.T("ui.gotIt"), Primary = true, Width = 120, Height = 34, Dock = DockStyle.Bottom };
            close.Click += (_, _) => f.Close();
            f.Controls.Add(text); f.Controls.Add(close);
            f.Show();
        }

        public static void ShowFamily()
        {
            var open = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.Tag as string == "opsis-family");
            if (open != null) { open.Activate(); return; }
            var apps = FamilyApp.Load();
            var f = NewForm(OpsisStrings.T("ui.familyTitle"), new Size(640, Math.Min(760, 120 + Math.Max(1, apps.Count) * 78))); f.Tag = "opsis-family";
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
            foreach (var app in apps)
            {
                var row = new Panel { Width = 580, Height = 68, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 8) };
                row.Controls.Add(new Label { Text = app.Name, Location = new Point(14, 8), AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = UiTheme.TextPrimary });
                row.Controls.Add(new Label { Text = app.Tagline, Location = new Point(14, 32), Size = new Size(380, 30), ForeColor = UiTheme.TextSecondary });
                string? installed = app.InstalledPath;
                var btn = new Opsis.UI.FlatButton { Width = 150, Height = 32, Location = new Point(416, 18), Primary = installed == null, Text = installed != null ? OpsisStrings.T("ui.open") : OpsisStrings.T("ui.download") };
                var captured = app;
                btn.Click += (_, _) =>
                {
                    try
                    {
                        if (installed != null && SafeLaunch.IsAllowed(installed)) Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true });
                        else if (captured.Repo.Length > 0 && captured.Owner.Length > 0) Process.Start(new ProcessStartInfo(captured.ReleasesUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex) { RotatingLogger.Write("Family window: " + ex.Message); }
                };
                row.Controls.Add(btn);
                list.Controls.Add(row);
            }
            if (apps.Count == 0) list.Controls.Add(new Label { Text = LocalizationManager.T("Family.NoList"), AutoSize = true, ForeColor = UiTheme.TextSecondary });
            f.Controls.Add(list);
            f.Show();
        }
    }
}
