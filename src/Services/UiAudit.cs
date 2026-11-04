using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    /// <summary>
    /// `Opsis.exe --ui-audit &lt;folder&gt;` (CI: workflow ui-click-audit; needs OPSIS_E2E=1 and an isolated OPSIS_APPDATA): opens the main window, visits EVERY page, screenshots it, and "clicks"
    /// every switch / check box / combo box on it (change, check, change back), recording exceptions, pages that take more than 3 s, controls that did not react or froze the UI.
    /// Report: ui-audit.md + ui-audit.json + one PNG per page. Exit 0 = no problem found.
    /// </summary>
    public static class UiAudit
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);

        // A modal message box opened by some control would wait for ever on a runner nobody watches: close every standard dialog (#32770) of this process and note it in the report.
        private static void StartDialogGuard(List<string> problems, Func<string> where)
        {
            int me = Environment.ProcessId;
            var seen = new HashSet<IntPtr>();
            var t = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(1500);
                    try
                    {
                        EnumWindows((h, _) =>
                        {
                            GetWindowThreadProcessId(h, out int pid);
                            if (pid != me || !IsWindowVisible(h)) return true;
                            var cls = new System.Text.StringBuilder(64); GetClassName(h, cls, 64);
                            if (cls.ToString() != "#32770") return true;
                            var title = new System.Text.StringBuilder(256); GetWindowText(h, title, 256);
                            if (seen.Add(h)) lock (problems) problems.Add($"modal dialog '{title}' opened near {where()} (answered by the audit)");
                            // a Yes/No box has no close button (WM_CLOSE is ignored): press No, then Cancel, then OK (IDNO=7, IDCANCEL=2, IDOK=1) - whichever button the box has
                            foreach (int id in new[] { 7, 2, 1 }) PostMessage(h, 0x0111 /* WM_COMMAND */, (IntPtr)id, IntPtr.Zero);
                            PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch (Exception) { }
                }
            }) { IsBackground = true };
            t.Start();
        }

        private static void Pump(int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            string nl = Environment.NewLine;
            if (Environment.GetEnvironmentVariable("OPSIS_E2E") != "1" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPSIS_APPDATA")))
            { File.WriteAllText(Path.Combine(folder, "ui-audit.md"), "refused: needs OPSIS_E2E=1 and OPSIS_APPDATA"); return 2; }

            var problems = new List<string>();
            var report = new StringBuilder("# Opsis UI click audit" + nl + nl + "| page | controls | clicked | slowest click ms | page load ms |" + nl + "|---|---|---|---|---|" + nl);
            Application.ThreadException += (_, e) => problems.Add("ThreadException: " + e.Exception);

            // the "where are you? (weather location)" question is asked once on the first visit of the Wallpaper page by design: answer it beforehand so it does not count as a freeze
            try { var ws = MotionDesk.Widgets.WallpaperSettings.Load(); ws.WeatherLocationPrompted = true; ws.Save(); } catch (Exception) { }
            string progress = Path.Combine(folder, "progress.txt"); string current = "start";
            void Mark(string s) { current = s; try { File.AppendAllText(progress, DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + s + nl); } catch (Exception) { } }
            StartDialogGuard(problems, () => current);
            var win = new MotionDesk.UI.MainWindow { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
            win.Show();
            Pump(2500);
            int totalClicked = 0;
            var pages = new List<object>();

            // OPSIS_AUDIT_PAGES=Wallpaper,Widgets limits the pages; OPSIS_AUDIT_NOCLICK=1 only opens them (safe to run on a personal machine with an isolated OPSIS_APPDATA)
            var only = (Environment.GetEnvironmentVariable("OPSIS_AUDIT_PAGES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool noClick = Environment.GetEnvironmentVariable("OPSIS_AUDIT_NOCLICK") == "1";
            foreach (var key in win.PageKeys.Where(k => only.Length == 0 || only.Contains(k)).ToList())
            {
                Mark("page " + key);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { win.NavigateTo(key); Pump(1200); }
                catch (Exception ex) { problems.Add($"[{key}] navigation failed: {ex.Message}"); }
                long load = sw.ElapsedMilliseconds - 1200;
                if (load > 3000) problems.Add($"[{key}] page took {load} ms to open");

                try
                {
                    using var bmp = new Bitmap(win.Width, win.Height);
                    win.DrawToBitmap(bmp, new Rectangle(0, 0, win.Width, win.Height));
                    bmp.Save(Path.Combine(folder, "page-" + key.Replace(':', '_') + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                catch (Exception ex) { problems.Add($"[{key}] screenshot failed: {ex.Message}"); }

                var controls = new List<Control>();
                void Collect(Control c)
                {
                    foreach (Control child in c.Controls)
                    {
                        if (child.Visible && child.Enabled && (child is Opsis.UI.FlatCheckBox || child is Opsis.UI.FlatComboBox)) controls.Add(child);
                        Collect(child);
                    }
                }
                Collect(win);

                int clicked = 0; long slowest = 0;
                foreach (var c in controls)
                {
                    if (win.IsDisposed || noClick) break;
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    Mark($"  [{key}] {c.GetType().Name} '{c.Text}'");
                    try
                    {
                        if (c is Opsis.UI.FlatCheckBox cb && !cb.IsDisposed)
                        {
                            bool before = cb.Checked;
                            cb.Checked = !before; Pump(80);
                            if (cb.Checked == before) problems.Add($"[{key}] '{cb.Text}' did not change");
                            cb.Checked = before; Pump(80);
                            clicked++;
                        }
                        else if (c is Opsis.UI.FlatComboBox combo && combo.Items.Count > 1 && !combo.IsDisposed)
                        {
                            int i = combo.SelectedIndex;
                            combo.SelectedIndex = (i + 1) % combo.Items.Count; Pump(80);
                            combo.SelectedIndex = i < 0 ? 0 : i; Pump(80);
                            clicked++;
                        }
                    }
                    catch (Exception ex) { problems.Add($"[{key}] control '{c.Name}' '{c.Text}' threw {ex.GetType().Name}: {ex.Message}"); }
                    slowest = Math.Max(slowest, t.ElapsedMilliseconds);
                    if (t.ElapsedMilliseconds > 3000) problems.Add($"[{key}] a control took {t.ElapsedMilliseconds} ms (UI freeze)");
                }
                totalClicked += clicked;
                report.Append($"| {key} | {controls.Count} | {clicked} | {slowest} | {load} |" + nl);
                pages.Add(new { page = key, controls = controls.Count, clicked, slowestMs = slowest, loadMs = load });
            }

            report.Append(nl + $"**Total controls clicked: {totalClicked}**" + nl + nl);
            report.Append(problems.Count == 0 ? "No problems found." + nl : "## Problems" + nl + nl + string.Join(nl, problems.Select(p => "- " + p)) + nl);
            File.WriteAllText(Path.Combine(folder, "ui-audit.md"), report.ToString());
            File.WriteAllText(Path.Combine(folder, "ui-audit.json"), System.Text.Json.JsonSerializer.Serialize(new { clicked = totalClicked, problems, pages }));
            try { win.Close(); } catch (Exception) { }
            return problems.Count == 0 ? 0 : 1;
        }
    }
}
