using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using MotionDesk.Widgets;

namespace MotionDesk.Services
{
    // `Opsis.exe --diag` : headless διαγνωστικό για "το wallpaper / ο καιρός δεν φαίνεται". Γράφει αναφορά στο
    // %AppData%\Opsis\diagnostics.txt και την τυπώνει στην κονσόλα. Δεν αγγίζει ρυθμίσεις.
    //   - έκδοση Windows, WebView2 Runtime, οθόνες, hardware tier
    //   - δομή Progman/WorkerW/SHELLDLL_DefView (το σημείο που άλλαξε στα Windows 11 24H2/25H2+)
    //   - ΠΡΑΓΜΑΤΙΚΟ τεστ attach: ένα magenta παράθυρο μπαίνει πίσω από τα εικονίδια και διαβάζεται pixel της οθόνης
    //   - τεστ παρόχων καιρού (Open-Meteo, wttr.in)
    public static class DesktopDiagnostics
    {
        private static bool _showDesktop;
        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? cls, string? title);
        [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string? c, string? t);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr c, IntPtr p);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
        [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
        private static string Dpi(IntPtr h) => $"dpiAwareness={GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(h))}";
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr h, IntPtr r, IntPtr rg, uint f);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte a, uint f);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

        private static string Cls(IntPtr h) { var sb = new StringBuilder(128); GetClassName(h, sb, 128); return sb.ToString(); }
        private static string Describe(IntPtr h)
        {
            if (h == IntPtr.Zero) return "(none)";
            GetWindowRect(h, out var r);
            return $"0x{h.ToInt64():X} {Cls(h)} visible={IsWindowVisible(h)} exstyle=0x{GetWindowLongPtr(h, -20).ToInt64():X} rect=({r.L},{r.T},{r.R},{r.B})";
        }

        public static int Run(bool showDesktop = false)
        {
            _showDesktop = showDesktop;
            var sb = new StringBuilder();
            void L(string s) => sb.AppendLine(s);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => { try { File.WriteAllText(Path.Combine(AppPaths.AppDataFolder, "diag-crash.txt"), sb + Environment.NewLine + "UNHANDLED: " + e.ExceptionObject); } catch (Exception) { } };
            Application.ThreadException += (_, e) => { try { File.WriteAllText(Path.Combine(AppPaths.AppDataFolder, "diag-crash.txt"), sb + Environment.NewLine + "THREAD: " + e.Exception); } catch (Exception) { } };
            L($"Opsis diagnostics  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                L($"Windows: {k?.GetValue("ProductName")} {k?.GetValue("DisplayVersion")} build {k?.GetValue("CurrentBuildNumber")}.{k?.GetValue("UBR")}");
            }
            catch (Exception ex) { L("Windows: ? " + ex.Message); }
            L($"OS: {Environment.OSVersion}  64bit={Environment.Is64BitOperatingSystem}  cores={Environment.ProcessorCount}");
            string? wv = WebView2Support.GetRuntimeVersion();
            L($"WebView2 Runtime: {wv ?? "MISSING  <-- without it the wallpaper/widgets cannot render"}");
            L($"Hardware tier: {HardwareProfileService.CurrentTier}");
            foreach (var s in Screen.AllScreens) L($"Screen {s.DeviceName} {s.Bounds} primary={s.Primary} bpp={s.BitsPerPixel}");

            L("");
            L("--- Desktop window tree ---");
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) L("Progman: NOT FOUND (no explorer shell on this desktop)");
            else
            {
                L("Progman: " + Describe(progman));
                EnumChildWindows(progman, (h, _) => { L("   child " + Describe(h)); return true; }, IntPtr.Zero);
                EnumWindows((h, _) =>
                {
                    if (Cls(h) == "WorkerW")
                    {
                        L("WorkerW(top): " + Describe(h));
                        EnumChildWindows(h, (c, __) => { L("   child " + Describe(c)); return true; }, IntPtr.Zero);
                    }
                    return true;
                }, IntPtr.Zero);
            }

            L("");
            L("--- Attach test (real) ---");
            try { L(progman == IntPtr.Zero ? "SKIPPED (no Progman)" : AttachProbe()); } catch (Exception ex) { L("Attach probe threw: " + ex); }

            L("");
            L("--- Real wallpaper window test (WebView2 page) ---");
            try { L(progman == IntPtr.Zero ? "SKIPPED (no Progman)" : RealWallpaperProbe()); } catch (Exception ex) { L("Real wallpaper probe threw: " + ex); }

            L("");
            L("--- Plugins (loaded by the real PluginHost) ---");
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                PluginHost.Instance.StartAll();
                var infos = PluginHost.Instance.GetAboutInfos();
                if (infos.Count == 0) L("(no plugins found in the plugins folders)");
                foreach (var (id, info) in infos) L($"  {id}  {info.Name} {info.Version}");
                foreach (var nav in PluginHost.Instance.GetNavigationItems()) L($"  nav page: {nav.Key} ({nav.TitleEn}) -> control {nav.CreatePage().GetType().Name}");
                foreach (var tray in PluginHost.Instance.GetTrayMenuItems()) L($"  tray item: {tray.TitleEn}");
                PluginHost.Instance.StopAll();
            }
            catch (Exception ex) { L("Plugin host probe threw: " + ex); }

            L("");
            L("--- Weather providers ---");
            try { L(WeatherProbe()); } catch (Exception ex) { L("Weather probe threw: " + ex.Message); }

            L("");
            L("--- wallpaper-attach.log (tail) ---");
            try
            {
                string log = Path.Combine(AppPaths.AppDataFolder, "wallpaper-attach.log");
                if (File.Exists(log)) foreach (var line in File.ReadLines(log).TakeLast(25)) L(line);
                else L("(no file)");
            }
            catch (Exception ex) { L(ex.Message); }

            string report = sb.ToString();
            try { File.WriteAllText(Path.Combine(AppPaths.AppDataFolder, "diagnostics.txt"), report, Encoding.UTF8); } catch (Exception) { }
            Console.WriteLine(report);
            return report.Contains("RESULT=FAIL") || wv == null ? 1 : 0;
        }

        // Magenta παράθυρο -> attach όπως το wallpaper -> διάβασε pixel οθόνης. Αν είναι magenta, το attach δουλεύει και
        // το παράθυρο είναι ΟΡΑΤΟ πίσω από τα εικονίδια. Αλλιώς: attach "πέτυχε" αλλά δεν εμφανίζεται (το σύμπτωμα).
        private static string AttachProbe()
        {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            string? ctx = Environment.GetEnvironmentVariable("OPSIS_DIAG_DPI");
            if (!string.IsNullOrEmpty(ctx)) SetThreadDpiAwarenessContext(new IntPtr(long.Parse(ctx)));
            using var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                Bounds = screen.Bounds, BackColor = Color.FromArgb(255, 0, 255)
            };
            _ = form.Handle;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = WallpaperInterop.AttachToDesktop(form.Handle, screen.Bounds);
            var o = new StringBuilder($"DPI: progman {Dpi(FindWindow("Progman", null))}, ours {Dpi(form.Handle)}{Environment.NewLine}AttachToDesktop returned {ok} in {sw.ElapsedMilliseconds} ms; parent={Describe(WallpaperInterop.LastAttachedWorkerW)}{Environment.NewLine}");
            if (!ok) return o + "RESULT=FAIL (attach failed)";
            form.Show();
            WallpaperInterop.KeepBehindIcons(form.Handle);
            string verdict = "RESULT=INCONCLUSIVE (the desktop is fully covered by other windows; rerun with --diag --show-desktop)";
            dynamic? shell = null;
            if (_showDesktop) { try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; } }
            try
            {
                if (Environment.GetEnvironmentVariable("OPSIS_DIAG_VARIANTS") == "1")
                {
                    IntPtr fh = form.Handle, pm = FindWindow("Progman", null);
                    o.AppendLine("  z-order of Progman children (top->bottom) after attach:");
                    for (IntPtr c = FindWindowEx(pm, IntPtr.Zero, null, null); c != IntPtr.Zero; c = FindWindowEx(pm, c, null, null))
                        o.AppendLine("    " + Describe(c) + (c == fh ? "   <== OUR WINDOW" : ""));
                    var variants = new (string, Action)[]
                    {
                        ("A current (below DefView)", () => { }),
                        ("B HWND_BOTTOM", () => SetWindowPos(fh, new IntPtr(1), 0, 0, 0, 0, 0x13)),
                        ("C HWND_TOP", () => SetWindowPos(fh, IntPtr.Zero, 0, 0, 0, 0, 0x13)),
                        ("D remove WS_EX_LAYERED + BOTTOM", () => { long ex = GetWindowLongPtr(fh, -20).ToInt64(); SetWindowLongPtr(fh, -20, new IntPtr(ex & ~0x80000L)); SetWindowPos(fh, new IntPtr(1), 0, 0, 0, 0, 0x37); }),
                        ("A2 insert right below DefView + invalidate", () => { IntPtr dv = FindWindowEx(pm, IntPtr.Zero, "SHELLDLL_DefView", null); SetWindowPos(fh, dv, 0, 0, 0, 0, 0x13); RedrawWindow(fh, IntPtr.Zero, IntPtr.Zero, 0x185); }),
                        ("H child of system WorkerW + TOP", () => { IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); SetParent(fh, wk); SetWindowPos(fh, IntPtr.Zero, 0, 0, 0, 0, 0x13); RedrawWindow(fh, IntPtr.Zero, IntPtr.Zero, 0x185); }),
                        ("J1 ours right below DefView, system WorkerW sent to bottom", () => { IntPtr dv = FindWindowEx(pm, IntPtr.Zero, "SHELLDLL_DefView", null); IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); SetParent(fh, pm); SetWindowPos(fh, dv, 0, 0, 0, 0, 0x13); if (wk != IntPtr.Zero) SetWindowPos(wk, new IntPtr(1), 0, 0, 0, 0, 0x13); }),
                        ("J2 system WorkerW hidden", () => { IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); if (wk != IntPtr.Zero) ShowWindow(wk, 0); }),
                        ("J3 WorkerW shown again", () => { IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); if (wk != IntPtr.Zero) ShowWindow(wk, 5); }),
                        ("F CONTROL top-level topmost (ignores uncovered test)", () => { long st = GetWindowLongPtr(fh, -16).ToInt64(); SetParent(fh, IntPtr.Zero); SetWindowLongPtr(fh, -16, new IntPtr((st & ~0x40000000L) | unchecked((int)0x80000000))); SetWindowPos(fh, new IntPtr(-1), 0, 0, 2560, 1440, 0x40); }),
                        ("E layered alpha 254 + TOP", () => { long ex = GetWindowLongPtr(fh, -20).ToInt64(); SetWindowLongPtr(fh, -20, new IntPtr(ex | 0x80000L)); SetLayeredWindowAttributes(fh, 0, 254, 2); SetWindowPos(fh, IntPtr.Zero, 0, 0, 0, 0, 0x13); }),
                    };
                    foreach (var (name, act) in variants)
                    {
                        act();
                        var e3 = DateTime.UtcNow.AddMilliseconds(800);
                        while (DateTime.UtcNow < e3) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                        int un = 0, mg = 0;
                        for (int gx = 1; gx < 14; gx++) for (int gy = 1; gy < 9; gy++)
                        {
                            int x = screen.Bounds.X + screen.Bounds.Width * gx / 14, y = screen.Bounds.Y + screen.Bounds.Height * gy / 9;
                            string rc = Cls(GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2));
                            if (rc != "Progman" && rc != "WorkerW" && !name.StartsWith("F")) continue;
                            un++;
                            using var b1 = new Bitmap(1, 1);
                            using (var g1 = Graphics.FromImage(b1)) g1.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                            var c1 = b1.GetPixel(0, 0);
                            if (c1.R > 200 && c1.G < 60 && c1.B > 200) mg++;
                        }
                        o.AppendLine($"  variant {name}: uncovered={un} magenta={mg}");
                    }
                }
                for (int i = 0; i < 10; i++)
                {
                    var end2 = DateTime.UtcNow.AddMilliseconds(500);
                    while (DateTime.UtcNow < end2) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                    int uncovered = 0, magenta = 0;
                    for (int gx = 1; gx < 14; gx++)
                        for (int gy = 1; gy < 9; gy++)
                        {
                            int x = screen.Bounds.X + screen.Bounds.Width * gx / 14, y = screen.Bounds.Y + screen.Bounds.Height * gy / 9;
                            IntPtr root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2);
                            string rc = Cls(root);
                            if (rc != "Progman" && rc != "WorkerW") continue;   // πάνω από το σημείο υπάρχει άλλο παράθυρο/taskbar
                            uncovered++;
                            using var bmp = new Bitmap(1, 1);
                            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                            var c = bmp.GetPixel(0, 0);
                            if (c.R > 200 && c.G < 60 && c.B > 200) magenta++;
                        }
                    o.AppendLine($"  sample {i + 1}: uncovered desktop points={uncovered}, magenta={magenta}");
                    if (magenta > 0) { verdict = "RESULT=OK (window is visible behind the icons)"; break; }
                    if (uncovered > 0 && i >= 3) { verdict = "INFO (a plain GDI test window is not composited on a raised desktop - expected; the WebView2 test below is authoritative)"; break; }
                }
            try
            {
                using var shot = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
                using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);
                string png = Path.Combine(AppPaths.AppDataFolder, "diag-attach.png");
                shot.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                o.AppendLine("  screenshot: " + png);
            }
            catch (Exception) { }
            }
            finally { if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } } }
            form.Close();
            return o + verdict;
        }

        private static System.Collections.Generic.List<(int X, int Y, Color C)> Snapshot()
        {
            var list = new System.Collections.Generic.List<(int, int, Color)>();
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            for (int gx = 1; gx < 20; gx++) for (int gy = 1; gy < 12; gy++)
            {
                int x = screen.Bounds.X + screen.Bounds.Width * gx / 20, y = screen.Bounds.Y + screen.Bounds.Height * gy / 12;
                string rc = Cls(GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2));
                if (rc != "Progman" && rc != "WorkerW") continue;
                using var bmp = new Bitmap(1, 1);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                list.Add((x, y, bmp.GetPixel(0, 0)));
            }
            return list;
        }

        // Ενεργοποιεί το ΠΡΑΓΜΑΤΙΚΟ wallpaper του Opsis (WallpaperWindow + WebView2 + Waves) και συγκρίνει pixels της
        // επιφάνειας εργασίας πριν/μετά. (Με OPSIS_APPDATA απομονωμένο, δεν αγγίζει τις ρυθμίσεις του χρήστη.)
        private static string RealWallpaperProbe()
        {
            var o = new StringBuilder();
            dynamic? shell = null;
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            Pump(2500);   // άφησε τα Windows να ολοκληρώσουν το προηγούμενο restore του ToggleDesktop
            if (_showDesktop) { try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; } }
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Pump(2500);
                var before = Snapshot();
                o.AppendLine($"  baseline uncovered points: {before.Count}");
                if (before.Count == 0) return o + "RESULT=INCONCLUSIVE (desktop covered; rerun with --diag --show-desktop)";
                var engine = WallpaperHostEngine.Instance;
                string? mode = Environment.GetEnvironmentVariable("OPSIS_DIAG_MODE");
                string? video = Environment.GetEnvironmentVariable("OPSIS_DIAG_VIDEO");
                if (!string.IsNullOrEmpty(video)) engine.SetVideo(video);
                if (!string.IsNullOrEmpty(mode)) engine.SetMode(mode);
                engine.Enable();
                int changed = 0;
                for (int i = 0; i < int.Parse(Environment.GetEnvironmentVariable("OPSIS_DIAG_SECONDS") ?? "12"); i++)
                {
                    Pump(1000);
                    changed = 0;
                    foreach (var (x, y, c) in before)
                    {
                        using var bmp = new Bitmap(1, 1);
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                        var n = bmp.GetPixel(0, 0);
                        if (Math.Abs(n.R - c.R) + Math.Abs(n.G - c.G) + Math.Abs(n.B - c.B) > 30) changed++;
                    }
                    o.AppendLine($"  t={i + 1}s IsRunning={engine.IsRunning} changed pixels={changed}/{before.Count}");
                }
                try
                {
                    var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                    using var shot = new Bitmap(sc.Bounds.Width, sc.Bounds.Height);
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(sc.Bounds.Location, Point.Empty, sc.Bounds.Size);
                    string png = Path.Combine(AppPaths.AppDataFolder, $"diag-wallpaper-{mode ?? "default"}.png");
                    shot.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    o.AppendLine("  screenshot: " + png);
                }
                catch (Exception) { }
                engine.Disable();
                Pump(500);
                o.Append(changed > before.Count / 2 ? "RESULT=OK (the animated wallpaper is visible on the desktop)" : "RESULT=FAIL (wallpaper window running but NOT visible on the desktop)");
                return o.ToString();
            }
            finally { if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } } }
        }

        /// <summary>`Opsis.exe --render-sysmon <folder>`: αποδίδει το System Monitor widget σε PNG με δεδομένα δοκιμής (οπτικός έλεγχος).</summary>
        public static int RenderSysmon(string folder)
        {
            Directory.CreateDirectory(folder);
            var rnd = new Random(7);
            foreach (var (w, h) in new[] { (340, 280), (230, 430), (260, 160) })
            {
                using var c = new SystemMonitorRowsControl { Width = w, Height = h, BackColor = Color.FromArgb(20, 24, 34) };
                double cpu = 30, ram = 55, net = 200, gpu = 15;
                for (int i = 0; i < 60; i++)
                {
                    cpu = Math.Clamp(cpu + rnd.Next(-12, 13), 3, 96); ram = Math.Clamp(ram + rnd.Next(-3, 4), 30, 80);
                    net = Math.Clamp(net + rnd.Next(-150, 160), 0, 2500); gpu = Math.Clamp(gpu + rnd.Next(-10, 11), 0, 70);
                    c.SetCpu(cpu); c.SetRam(ram, $"{(100 - ram) * 160 / 100:0} / 16384 MB free"); c.SetNetwork(net, net / 5); c.SetGpu(gpu, "54°C");
                }
                using var bmp = new Bitmap(w, h);
                c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                bmp.Save(Path.Combine(folder, $"sysmon_{w}x{h}.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var eq = new MotionDesk.UI.EqBandsControl { Width = 700, Height = 220, BackColor = Color.FromArgb(20, 24, 34) })
            {
                eq.Bands = new double[] { 6, 5, 3, 1, 0, 0, 1, 2, 3, 4 };
                using var bmp = new Bitmap(700, 220);
                eq.DrawToBitmap(bmp, new Rectangle(0, 0, 700, 220));
                bmp.Save(Path.Combine(folder, "eq_bands.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)] private struct ACCENT { public int State, Flags, Gradient, Anim; }
        [StructLayout(LayoutKind.Sequential)] private struct WCAD { public int Attr; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);

        /// <summary>`--test-transparency <folder>`: 3 τεχνικές "διάφανου" widget πάνω στο ΠΡΑΓΜΑΤΙΚΟ desktop + screenshot.</summary>
        public static int TestTransparency(string folder)
        {
            Directory.CreateDirectory(folder);
            var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var forms = new System.Collections.Generic.List<Form>();
            Form Make(int x, string name, Color back, Color? key, int accentState)
            {
                var f = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                    Bounds = new Rectangle(sc.Bounds.X + x, sc.Bounds.Y + 420, 380, 220), BackColor = back, TopMost = true };
                if (key != null) f.TransparencyKey = key.Value;
                var lbl = new Label { Text = "22.1°C", AutoSize = false, Dock = DockStyle.Top, Height = 90, Font = new Font("Segoe UI Semibold", 38), ForeColor = Color.White, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
                var sub = new Label { Text = name, AutoSize = false, Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 14), ForeColor = Color.FromArgb(220, 255, 255, 255), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
                f.Controls.Add(sub); f.Controls.Add(lbl);
                f.Paint += (_, e) => { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var br = new SolidBrush(Color.FromArgb(0, 200, 230)); e.Graphics.FillEllipse(br, 20, 150, 340, 14); };
                f.Show();
                if (accentState != 0)
                {
                    var a = new ACCENT { State = accentState, Flags = 2, Gradient = 0 };
                    var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(a)); Marshal.StructureToPtr(a, ptr, false);
                    var d = new WCAD { Attr = 19, Data = ptr, Size = Marshal.SizeOf(a) };
                    SetWindowCompositionAttribute(f.Handle, ref d); Marshal.FreeHGlobal(ptr);
                }
                forms.Add(f); return f;
            }
            dynamic? shell = null;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; }
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            Pump(2500);
            Make(40, "A: key=Black", Color.Black, Color.Black, 0);
            Make(450, "B: key=(1,2,3)", Color.FromArgb(1, 2, 3), Color.FromArgb(1, 2, 3), 0);
            Make(860, "C: accent TRANSPARENTGRADIENT", Color.Black, null, 2);
            Make(1270, "D: accent ACRYLIC a=0", Color.Black, null, 4);
            Pump(2500);
            using var shot = new Bitmap(sc.Bounds.Width, 700);
            using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(sc.Bounds.X, sc.Bounds.Y + 380, 0, 0, shot.Size);
            shot.Save(Path.Combine(folder, "transparency.png"), System.Drawing.Imaging.ImageFormat.Png);
            foreach (var f in forms) f.Close();
            if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } }
            return 0;
        }

        /// <summary>`--test-widgets <folder> [mode]`: ανοίγει τα πραγματικά native widgets με το δοσμένο WidgetsThemeMode (π.χ. Transparent) πάνω στο desktop + screenshot.</summary>
        public static int TestWidgets(string folder, string mode)
        {
            Directory.CreateDirectory(folder);
            var st = AppSettings.Load(); st.WidgetsThemeMode = mode; st.Save();
            var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            dynamic? shell = null;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; }
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            Pump(2500);
            var ids = new[] { "clock", "sysmon", "weather", "network", "disk" };
            var wins = new System.Collections.Generic.List<Form>();
            int x = 700;
            foreach (var id in ids)
            {
                var w = new WidgetWindow(id, sc.Bounds.X + x, sc.Bounds.Y + 180, 300, 260);
                w.Show(); wins.Add(w); x += 320;
            }
            Pump(5000);
            using var shot = new Bitmap(sc.Bounds.Width, 520);
            using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(sc.Bounds.X, sc.Bounds.Y + 150, 0, 0, shot.Size);
            shot.Save(Path.Combine(folder, $"widgets_{mode}.png"), System.Drawing.Imaging.ImageFormat.Png);
            foreach (var f in wins) f.Close();
            if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } }
            return 0;
        }

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

        /// <summary>`--test-deskstrip <folder> [position]`: ανοίγει το νέο dock με πραγματικές εφαρμογές και βγάζει screenshots (κανονικό, hover+preview).</summary>
        public static int TestDeskStrip(string folder, string position)
        {
            Directory.CreateDirectory(folder);
            var started = new System.Collections.Generic.List<Process>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            GetCursorPos(out var oldPos);
            try
            {
                foreach (var exe in new[] { "charmap.exe" }) { var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); if (p != null) started.Add(p); }
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var engine = DeskStripHostEngine.Instance;
                engine.UpdateSettings(s =>
                {
                    s.Paths = new System.Collections.Generic.List<string> { Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "mspaint.exe"), Path.Combine(sys, "cmd.exe"), Path.Combine(sys, "taskmgr.exe") };
                    s.Position = Enum.Parse<DockPosition>(position); s.IconSize = 48; s.ShowRunning = true; s.Magnify = true;
                });
                engine.Enable();
                Pump(3500);
                var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var win = Application.OpenForms.OfType<DeskStripWindow>().First();
                void Shot(string name)
                {
                    var r = win.Bounds; r.Inflate(260, 260); r.Intersect(sc.Bounds);
                    using var bmp = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    bmp.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Shot("dock_" + position + "_idle");
                // hover πάνω από το 2ο εικονίδιο (καρφιτσωμένο, μη τρέχον) και μετά πάνω από το 1ο (τρέχον: preview)
                var b = win.Bounds;
                bool vert = position is "Left" or "Right";
                Point At(double frac) => vert ? new Point(b.Left + b.Width / 2, b.Top + (int)(b.Height * frac)) : new Point(b.Left + (int)(b.Width * frac), b.Top + b.Height / 2 + 4);
                var cp1 = At(0.30); MoveMouse(win, vert, 0.30); Pump(1500);
                Shot("dock_" + position + "_hover");
                MoveMouse(win, vert, 0.55); Pump(1800);
                Shot("dock_" + position + "_preview");
                Console.WriteLine("items shown; pinned=" + engine.GetPinnedApps().Count);
                engine.Disable();
            }
            finally
            {
                SetCursorPos(oldPos.X, oldPos.Y);
                foreach (var p in started) { try { if (!p.HasExited) p.Kill(); } catch (Exception) { } }
                foreach (var q in Process.GetProcessesByName("charmap")) { try { q.Kill(); } catch (Exception) { } }
            }
            return 0;
        }

        // Καλεί ΑΠΕΥΘΕΙΑΣ το OnMouseMove του dock (χωρίς να πειράξει τον πραγματικό δείκτη του χρήστη· τα Windows θα έστελναν
        // αμέσως WM_MOUSELEAVE αν στέλναμε μηνύματα ενώ ο δείκτης δεν είναι πάνω από το παράθυρο).
        private static void MoveMouse(Form dock, bool vertical, double frac)
        {
            int x = vertical ? dock.Width / 2 : (int)(dock.Width * frac), y = vertical ? (int)(dock.Height * frac) : dock.Height / 2 + 4;
            typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.None, 0, x, y, 0) });
        }

        /// <summary>`--test-containers <folder>`: Folder Portal (ζωντανή ενημέρωση, πλοήγηση, αντιγραφή), κανονικό container, επιλογή, screenshot.</summary>
        public static int TestContainers(string folder)
        {
            Directory.CreateDirectory(folder);
            string root = Path.Combine(Path.GetTempPath(), "opsis_portal_test_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(Path.Combine(root, "Photos"));
            File.WriteAllText(Path.Combine(root, "notes.txt"), "a"); File.WriteAllText(Path.Combine(root, "report.docx"), "b");
            File.WriteAllText(Path.Combine(root, "Photos", "trip.jpg"), "c");
            string extra = Path.Combine(Path.GetTempPath(), "opsis_portal_extra_" + Guid.NewGuid().ToString("N")[..6] + ".txt"); File.WriteAllText(extra, "x");
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            var results = new System.Collections.Generic.List<string>();
            void Check(string name, bool ok) { results.Add((ok ? "PASS " : "FAIL ") + name); }
            try
            {
                var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var engine = DeskContainerHostEngine.Instance;
                var portal = engine.SpawnContainer("diag_portal", "Portal", sc.Bounds.X + 300, sc.Bounds.Y + 150, 380, 300);
                portal.SetPortal(root);
                Pump(800);
                Check("portal lists folder content (3 entries, folders first)", portal.ItemPaths.Count == 3 && Directory.Exists(portal.ItemPaths[0]));
                File.WriteAllText(Path.Combine(root, "new_file.pdf"), "n");
                Pump(1500);
                Check("portal updates live when a file appears", portal.ItemPaths.Any(p => p.EndsWith("new_file.pdf")));
                portal.NavigatePortal(Path.Combine(root, "Photos")); Pump(500);
                Check("portal navigates into a subfolder", portal.ItemPaths.Count == 1 && portal.ItemPaths[0].EndsWith("trip.jpg"));
                portal.AddPaths(new[] { extra }); Pump(800);
                Check("dropping a file on a portal COPIES it into the folder", File.Exists(Path.Combine(root, "Photos", Path.GetFileName(extra))) && File.Exists(extra));
                portal.SetPortal(root); Pump(500);
                var normal = engine.SpawnContainer("diag_normal", "Normal", sc.Bounds.X + 720, sc.Bounds.Y + 150, 380, 300);
                normal.AddPaths(new[] { Path.Combine(root, "notes.txt"), Path.Combine(root, "report.docx"), extra, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe") });
                Pump(800);
                Check("normal container holds 4 items", normal.ItemPaths.Count == 4);
                normal.IconPx = 64; Pump(300);
                var all = new[] { portal, normal };
                using var shot = new Bitmap(900, 380);
                var origin = new Point(sc.Bounds.X + 280, sc.Bounds.Y + 130);
                using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(origin, Point.Empty, shot.Size);
                shot.Save(Path.Combine(folder, "containers.png"), System.Drawing.Imaging.ImageFormat.Png);
                Check("files untouched (originals still exist)", File.Exists(Path.Combine(root, "notes.txt")) && File.Exists(extra));
            }
            finally
            {
                foreach (var w in Application.OpenForms.OfType<DeskContainerWindow>().ToList()) { try { w.Close(); } catch (Exception) { } }
                try { Directory.Delete(root, true); File.Delete(extra); } catch (Exception) { }
                foreach (var f in Directory.GetFiles(Path.Combine(AppPaths.AppDataFolder, "deskcontainers"), "diag_*_config.json")) { try { File.Delete(f); } catch (Exception) { } }
            }
            foreach (var r in results) Console.WriteLine(r);
            return results.Any(r => r.StartsWith("FAIL")) ? 1 : 0;
        }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        /// <summary>`--test-iconhide`: διπλό κλικ σε κενό σημείο της επιφάνειας εργασίας (πραγματικά κλικ), με και χωρίς wallpaper, και έλεγχος αν κρύβονται/ξαναφαίνονται τα εικονίδια.</summary>
        public static int TestIconHide(string mode)
        {
            dynamic? shell = null;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; }
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            GetCursorPos(out var old);
            var results = new System.Collections.Generic.List<string>();
            string state = Path.Combine(AppPaths.AppDataFolder, "desktopicons_hidden_state.json");
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Pump(2500);
                var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                int x = sc.Bounds.X + sc.Bounds.Width * 55 / 100, y = sc.Bounds.Y + sc.Bounds.Height * 70 / 100;   // empty area (icons live at the left/right edges)
                DesktopIconVisibilityEngine.Instance.Start();
                void DoubleClick()
                {
                    SetCursorPos(x, y); Pump(150);
                    for (int i = 0; i < 2; i++) { mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); }
                    Pump(900);
                }
                string VisibleIcons()
                {
                    var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                    var t = typeof(DesktopIconVisibilityEngine);
                    var list = (IntPtr)t.GetMethod("FindDesktopListView", bf)!.Invoke(null, null)!;
                    GetWindowThreadProcessId(list, out uint pid);
                    IntPtr hp = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);
                    var kept = new System.Collections.Generic.List<string>(); int total = 0;
                    try
                    {
                        int count = (int)SendMessage(list, 0x1004 /*LVM_GETITEMCOUNT*/, IntPtr.Zero, IntPtr.Zero);
                        for (int i = 0; i < count; i++)
                        {
                            total++;
                            var pos = (Point)t.GetMethod("GetItemPositionRemote", bf)!.Invoke(null, new object[] { hp, list, i })!;
                            string name = (string)t.GetMethod("GetItemTextRemote", bf)!.Invoke(null, new object[] { hp, list, i })!;
                            if (pos.X > -5000 && pos.Y > -5000) kept.Add(name);
                        }
                    }
                    finally { CloseHandle(hp); }
                    try
                    {
                        var wl = (System.Collections.Generic.HashSet<string>)t.GetMethod("BuildWhitelist", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.Invoke(null, new object?[] { null })!;
                        File.WriteAllText(Path.Combine(Path.GetTempPath(), "opsis_iconhide_names.txt"), "WHITELIST:" + Environment.NewLine + string.Join(Environment.NewLine, wl) + Environment.NewLine + "VISIBLE:" + Environment.NewLine + string.Join(Environment.NewLine, kept), System.Text.Encoding.UTF8);
                    }
                    catch (Exception) { }
                    return $"{kept.Count}/{total} visible: [{string.Join(" | ", kept)}]";
                }
                void Round(string label)
                {
                    DoubleClick(); bool hidden = File.Exists(state);
                    results.Add($"{label}: 1st double-click -> icons hidden={hidden}; " + VisibleIcons());
                    DoubleClick(); bool restored = !File.Exists(state);
                    results.Add($"{label}: 2nd double-click -> icons restored={restored}");
                    if (File.Exists(state)) DesktopIconVisibilityEngine.Instance.RestoreIfHidden();   // never leave the user's icons off-screen
                }
                Round("no wallpaper");
                var ws = WallpaperSettings.Load(); ws.WeatherShowInfo = false; ws.Save();     // no clock/weather overlay: a visible window after Disable would then be a bug
                var w = WallpaperHostEngine.Instance;
                if (!string.IsNullOrEmpty(mode)) w.SetMode(mode);
                w.Enable(); Pump(6000);
                results.Add($"wallpaper enabled: IsRunning={w.IsRunning}");
                Round("wallpaper " + (mode ?? "Waves"));
                w.Disable(); Pump(1500);
                results.Add($"after ONE Disable(): IsEnabled={w.IsEnabled} visibleWallpaperWindows={VisibleWallpaperWindows()}   (must be 0)");
                // Race: Disable while the attach of a fresh window is still pending (what a quick Enable -> Disable does)
                w.Enable(); Pump(120); w.Disable(); Pump(7000);
                results.Add($"Enable then Disable 120 ms later, after 7 s: IsEnabled={w.IsEnabled} visibleWallpaperWindows={VisibleWallpaperWindows()}   (must be 0)");
                // Race: Disable while the OLD windows are still retiring during a rebuild
                w.Enable(); Pump(5000);
                typeof(WallpaperHostEngine).GetMethod("RebuildWindows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(w, null);
                Pump(300); w.Disable(); Pump(7000);
                results.Add($"Disable during a rebuild, after 7 s: IsEnabled={w.IsEnabled} visibleWallpaperWindows={VisibleWallpaperWindows()}   (must be 0)");
            }
            finally
            {
                try { DesktopIconVisibilityEngine.Instance.Stop(); } catch (Exception) { }
                SetCursorPos(old.X, old.Y);
                if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } }
            }
            foreach (var r in results) Console.WriteLine(r);
            return 0;
        }

        private static int VisibleWallpaperWindows() => Application.OpenForms.OfType<WallpaperWindow>().Count(f => !f.IsDisposed && f.Visible);

        private static string WeatherProbe()
        {
            var w = System.Diagnostics.Stopwatch.StartNew();
            string json = Task.Run(() => WeatherService.GetWallpaperWeatherJsonAsync(37.9838, 23.7275)).GetAwaiter().GetResult();   // thread pool: never block the UI sync context
            var o = new StringBuilder($"Wallpaper weather ({w.ElapsedMilliseconds} ms): {(json.Contains("\"ok\":true") ? "OK" : "FAILED")} {json[..Math.Min(json.Length, 160)]}\n");
            var n = Task.Run(() => WeatherService.GetNormalizedWeatherAsync(37.9838, 23.7275)).GetAwaiter().GetResult();
            o.Append($"Widget weather: {(n == null ? "FAILED" : $"OK {n.TemperatureC}C via {n.Source}")}");
            return o.ToString();
        }
    }
}
