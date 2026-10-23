using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using MotionDesk.Services;
using UiTheme = MotionDesk.UI.WidgetTheme;

namespace MotionDesk.Widgets
{
    // v2.2.0 - χαρακτηριστικά DeskContainers εμπνευσμένα/μεταφερμένα από το DesktopFences
    // (https://github.com/limbo666/DesktopFences, MIT License, Copyright (c) 2025 LIMBO666, που
    // με τη σειρά του περιλαμβάνει κώδικα του BirdyFences του HakanKokcu, επίσης MIT). Το
    // DesktopFences είναι WPF, το Opsis WinForms - ο κώδικας δεν μεταφέρεται αυτούσιος, μεταφέρονται
    // η λογική/οι αλγόριθμοι (Chameleon: κυρίαρχο χρώμα wallpaper σε 32x32 με κβάντιση 16 επιπέδων,
    // Peek Behind: fade + αντίστροφη μέτρηση 10", auto-hide αδράνειας, daily backup, import/export,
    // SpotSearch). Βλ. THIRD_PARTY_NOTICES.md για την πλήρη ειδοποίηση πνευματικών δικαιωμάτων.
    internal static class DeskContainerExtras
    {
        public static string L(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;

        // ---------------- Chameleon ----------------
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder buffer, uint winIni);
        private const uint SPI_GETDESKWALLPAPER = 0x0073;

        public static string? ActiveWallpaperPath()
        {
            var sb = new StringBuilder(260);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, 260, sb, 0))
            {
                var p = sb.ToString();
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
            var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return File.Exists(transcoded) ? transcoded : null;
        }

        public static Color? DominantColor(string imagePath)
        {
            try
            {
                byte[] bytes;
                using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytes = new byte[fs.Length];
                    int read = 0;
                    while (read < bytes.Length) { int n = fs.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                }
                using var ms = new MemoryStream(bytes);
                using var original = new Bitmap(ms);
                using var bmp = new Bitmap(original, new Size(32, 32));
                var counts = new Dictionary<int, int>();
                int best = 0, bestCount = 0;
                for (int x = 0; x < bmp.Width; x++)
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        var p = bmp.GetPixel(x, y);
                        if (p.GetSaturation() < 0.15f || p.GetBrightness() < 0.15f || p.GetBrightness() > 0.85f) continue;
                        int key = ((p.R / 16 * 16) << 16) | ((p.G / 16 * 16) << 8) | (p.B / 16 * 16);
                        counts.TryGetValue(key, out int c);
                        counts[key] = ++c;
                        if (c > bestCount) { bestCount = c; best = key; }
                    }
                if (bestCount == 0) return null;
                return Color.FromArgb(Math.Min(255, ((best >> 16) & 0xFF) + 20), Math.Min(255, ((best >> 8) & 0xFF) + 20), Math.Min(255, (best & 0xFF) + 20));
            }
            catch (Exception) { return null; }
        }

        // ---------------- Backup / Restore ----------------
        public static string BackupRoot => Path.Combine(AppPaths.AppDataFolder, "deskcontainers_backups");
        private static string ConfigDir => Path.Combine(AppPaths.AppDataFolder, "deskcontainers");

        public static string? BackupNow()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) return null;
                foreach (var c in DeskContainerHostEngine.Instance.GetActiveContainers().ToArray()) if (!c.IsDisposed) c.SaveNow();
                var dest = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(dest);
                foreach (var f in Directory.GetFiles(ConfigDir, "*_config.json")) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
                var old = Directory.GetDirectories(BackupRoot).OrderByDescending(d => d).Skip(14);
                foreach (var d in old) { try { Directory.Delete(d, true); } catch (IOException) { } }
                return dest;
            }
            catch (Exception) { return null; }
        }

        public static void DailyBackupIfDue()
        {
            if (!AppSettings.Load().ContainersDailyBackup) return;
            try
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                if (Directory.Exists(BackupRoot) && Directory.GetDirectories(BackupRoot).Any(d => Path.GetFileName(d).StartsWith(today, StringComparison.Ordinal))) return;
                if (Directory.Exists(ConfigDir) && Directory.GetFiles(ConfigDir, "*_config.json").Length > 0) BackupNow();
            }
            catch (Exception) { }
        }

        public static int Restore(string backupDir)
        {
            var files = Directory.GetFiles(backupDir, "*_config.json");
            if (files.Length == 0) return 0;
            DeskContainerHostEngine.Instance.CloseAll();
            Directory.CreateDirectory(ConfigDir);
            int n = 0;
            foreach (var f in files)
            {
                File.Copy(f, Path.Combine(ConfigDir, Path.GetFileName(f)), true);
                string id = Path.GetFileName(f).Replace("_config.json", "");
                try
                {
                    var s = JsonSerializer.Deserialize<DeskContainerState>(File.ReadAllText(f));
                    if (s == null) continue;
                    DeskContainerHostEngine.Instance.SpawnContainer(id, s.Title ?? id, s.X, s.Y, Math.Max(200, s.Width), Math.Max(140, s.Height));
                    n++;
                }
                catch (JsonException) { }
            }
            return n;
        }

        // ---------------- Import ----------------
        public static bool ImportContainer(string file, out string message)
        {
            message = "";
            try
            {
                var state = JsonSerializer.Deserialize<DeskContainerState>(File.ReadAllText(file));
                if (state == null) { message = L("Μη έγκυρο αρχείο.", "Invalid file."); return false; }
                string id = $"container{DateTime.Now.Ticks}";
                Directory.CreateDirectory(ConfigDir);
                // Νέο container σε ορατή θέση (το εξαγόμενο μπορεί να είναι από άλλο μηχάνημα/ανάλυση).
                state.X = 120 + (DeskContainerHostEngine.Instance.GetActiveContainers().Count % 6) * 40;
                state.Y = 120 + (DeskContainerHostEngine.Instance.GetActiveContainers().Count % 6) * 40;
                AtomicFile.WriteAllText(Path.Combine(ConfigDir, $"{id}_config.json"), JsonSerializer.Serialize(state));
                DeskContainerHostEngine.Instance.SpawnContainer(id, state.Title ?? id, state.X, state.Y, Math.Max(200, state.Width), Math.Max(140, state.Height));
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                message = ex.Message;
                return false;
            }
        }

        // ---------------- Auto-hide αδράνειας ----------------
        [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        public static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref li)) return 0;
            uint ms = unchecked((uint)Environment.TickCount - li.dwTime);
            return ms / 1000.0;
        }

        // Μικρό αντίστροφο-μέτρημα πάνω από ένα container που είναι κρυμμένο με "Peek Behind".
        public static void ShowCountdown(Rectangle around, int seconds, Action done)
        {
            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true, StartPosition = FormStartPosition.Manual,
                Size = new Size(64, 40), BackColor = Color.Black, Opacity = 0.75
            };
            form.Location = new Point(around.X + (around.Width - form.Width) / 2, around.Y + (around.Height - form.Height) / 2);
            var label = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 12, FontStyle.Bold), Text = seconds.ToString() };
            form.Controls.Add(label);
            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            int left = seconds;
            timer.Tick += (_, _) =>
            {
                left--; label.Text = left.ToString();
                if (left <= 0) { timer.Stop(); timer.Dispose(); form.Close(); done(); }
            };
            form.Show();
            timer.Start();
        }
    }

    // Αναζήτηση/εκκίνηση σε όλα τα στοιχεία όλων των ανοιχτών containers (SpotSearch του DesktopFences).
    internal sealed class DeskContainerSpotSearch : Form
    {
        private static DeskContainerSpotSearch? _open;
        private readonly ListBox _list = new();
        private readonly TextBox _box = new();
        private readonly List<(string Display, string Path)> _all = new();

        public static void Open()
        {
            if (_open != null && !_open.IsDisposed) { _open.Activate(); return; }
            _open = new DeskContainerSpotSearch();
            _open.Show();
            _open.Activate();
            _open._box.Focus();
        }

        private DeskContainerSpotSearch()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(520, 360);
            BackColor = UiTheme.Surface;
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(screen.X + (screen.Width - Width) / 2, screen.Y + screen.Height / 4);

            foreach (var c in DeskContainerHostEngine.Instance.GetActiveContainers().Where(c => !c.IsDisposed))
                foreach (var p in c.ItemPaths)
                    _all.Add(($"{System.IO.Path.GetFileNameWithoutExtension(p)}   -   {c.ContainerTitle}", p));

            _box.Dock = DockStyle.Top; _box.Font = new Font("Segoe UI", 14); _box.BackColor = UiTheme.Background; _box.ForeColor = UiTheme.TextPrimary; _box.BorderStyle = BorderStyle.FixedSingle;
            _list.Dock = DockStyle.Fill; _list.Font = new Font("Segoe UI", 11); _list.BackColor = UiTheme.Surface; _list.ForeColor = UiTheme.TextPrimary; _list.BorderStyle = BorderStyle.None; _list.IntegralHeight = false;
            Controls.Add(_list); Controls.Add(_box);
            Refill();
            _box.TextChanged += (_, _) => Refill();
            _box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Close(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Launch(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up && _list.Items.Count > 0) { _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0); e.SuppressKeyPress = true; }
            };
            _list.DoubleClick += (_, _) => Launch();
            Deactivate += (_, _) => { if (!IsDisposed) Close(); };
            FormClosed += (_, _) => _open = null;
        }

        private void Refill()
        {
            string q = _box.Text.Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var it in _all.Where(a => q.Length == 0 || a.Display.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(200)) _list.Items.Add(it.Display);
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _list.EndUpdate();
        }

        private void Launch()
        {
            string q = _box.Text.Trim();
            var matches = _all.Where(a => q.Length == 0 || a.Display.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
            if (_list.SelectedIndex < 0 || _list.SelectedIndex >= matches.Count) return;
            var path = matches[_list.SelectedIndex].Path;
            Close();
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
    }
}
