using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // Πακέτα θέματος (.theme) — ο επίσημος μηχανισμός των ίδιων των Windows: ένα .theme αρχείο
    // δένει μαζί wallpaper + δείκτη ποντικιού + σχήμα ήχων + χρώμα έμφασης, και εφαρμόζεται με ένα
    // διπλό-κλικ μέσω του ήδη υπάρχοντος OS handler (rundll32 themecpl.dll) — καμία δική μας
    // λογική εφαρμογής θέματος, μόνο δημιουργία/απαρίθμηση αρχείων .theme. Σκόπιμα ΔΕΝ αγγίζει
    // την ενότητα [VisualStyles] πέρα από το ήδη υπογεγραμμένο προεπιλεγμένο styles αρχείο του
    // συστήματος — πλήρες visual-style engine εξετάστηκε και αποκλείστηκε ρητά (risky tier).
    internal static class ThemePackageEngine
    {
        public static string UserThemesFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Themes");

        private static string BuiltInThemesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes");

        public sealed record ThemeEntry(string Name, string Path, bool BuiltIn);

        public static List<ThemeEntry> ListAvailableThemes()
        {
            var list = new List<ThemeEntry>();
            try
            {
                if (Directory.Exists(BuiltInThemesFolder))
                    foreach (var f in Directory.EnumerateFiles(BuiltInThemesFolder, "*.theme"))
                        list.Add(new ThemeEntry(System.IO.Path.GetFileNameWithoutExtension(f), f, true));
            }
            catch (IOException) { }
            try
            {
                if (Directory.Exists(UserThemesFolder))
                    foreach (var f in Directory.EnumerateFiles(UserThemesFolder, "*.theme"))
                        list.Add(new ThemeEntry(System.IO.Path.GetFileNameWithoutExtension(f), f, false));
            }
            catch (IOException) { }
            return list.OrderBy(t => t.BuiltIn ? 0 : 1).ThenBy(t => t.Name).ToList();
        }

        // Άνοιγμα ενός .theme αρχείου μέσω του δικού του OS association — ό,τι ακριβώς συμβαίνει
        // όταν ο χρήστης κάνει διπλό-κλικ σε ένα .theme μέσα στο Explorer.
        public static void ApplyTheme(string themeFilePath)
        {
            Process.Start(new ProcessStartInfo(themeFilePath) { UseShellExecute = true });
        }

        // wallpaperOverride: όταν δίνεται, γράφεται ΑΥΤΟ το path αντί για το τρέχον wallpaper των
        // Windows — χρησιμοποιείται από το "Tech Grid" theme preset (βλ. GenerateTechWallpaperPng)
        // ώστε να μην χρειάζεται να αλλάξει το πραγματικό wallpaper του χρήστη μόνο και μόνο για
        // να αποθηκευτεί ένα νέο πακέτο θέματος.
        public static string SaveCurrentAsTheme(string themeName, string? wallpaperOverride = null)
        {
            Directory.CreateDirectory(UserThemesFolder);
            string safeName = string.Join("_", themeName.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(UserThemesFolder, $"{safeName}.theme");

            var cursors = new List<KeyValuePair<string, string>>();
            using (var cursorsKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
            {
                if (cursorsKey != null)
                    foreach (var name in cursorsKey.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        if (cursorsKey.GetValue(name) is string v && !string.IsNullOrEmpty(v)) cursors.Add(new(name, v));
                    }
            }

            // ΔΙΟΡΘΩΣΗ: το παλιό πακέτο έγραφε μόνο aero.msstyles — εφαρμόζοντας ένα αποθηκευμένο θέμα τα Windows γύριζαν σε ΦΩΤΕΙΝΟ
            // με το προεπιλεγμένο accent, χάνοντας το σκούρο θέμα και το χρώμα που είχε ο χρήστης. Τώρα καταγράφονται και αυτά.
            bool? dark = null; int? colorization = null;
            try
            {
                using var pk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (pk?.GetValue("AppsUseLightTheme") is int light) dark = light == 0;
                using var dk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (dk?.GetValue("ColorizationColor") is int c) colorization = c;
            }
            catch (Exception) { }

            File.WriteAllText(path, BuildThemeText(themeName, wallpaperOverride ?? GetCurrentDesktopWallpaper(), cursors, GetCurrentSoundSchemeName(), dark, colorization), Encoding.Unicode);
            return path;
        }

        /// <summary>Το κείμενο ενός αρχείου .theme (καθαρή συνάρτηση, δοκιμάζεται με unit tests).</summary>
        internal static string BuildThemeText(string themeName, string? wallpaper, IEnumerable<KeyValuePair<string, string>> cursors, string? soundScheme, bool? dark, int? colorizationArgb)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; Δημιουργήθηκε από το Opsis — wallpaper, δείκτες, ήχοι, χρώμα και σκούρο/φωτεινό θέμα");
            sb.AppendLine("[Theme]");
            sb.AppendLine($"DisplayName={themeName.Replace('\r', ' ').Replace('\n', ' ')}");
            sb.AppendLine();

            sb.AppendLine("[Control Panel\\Desktop]");
            if (!string.IsNullOrEmpty(wallpaper)) sb.AppendLine($"Wallpaper={wallpaper}");
            sb.AppendLine("WallpaperStyle=10");
            sb.AppendLine("TileWallpaper=0");
            sb.AppendLine();

            sb.AppendLine("[Control Panel\\Cursors]");
            foreach (var kv in cursors) sb.AppendLine($"{kv.Key}={kv.Value}");
            sb.AppendLine();

            sb.AppendLine("[Sounds]");
            if (!string.IsNullOrEmpty(soundScheme)) sb.AppendLine($"SchemeName={soundScheme}");
            sb.AppendLine();

            sb.AppendLine("[VisualStyles]");
            sb.AppendLine(@"Path=%SystemRoot%\resources\Themes\aero\aero.msstyles");
            sb.AppendLine("ColorStyle=NormalColor");
            sb.AppendLine("Size=NormalSize");
            if (colorizationArgb is int argb)
            {
                sb.AppendLine("AutoColorization=0");
                sb.AppendLine($"ColorizationColor=0x{unchecked((uint)argb):X8}");
            }
            if (dark is bool d)
            {
                sb.AppendLine($"SystemMode={(d ? "Dark" : "Light")}");
                sb.AppendLine($"AppMode={(d ? "Dark" : "Light")}");
            }
            return sb.ToString();
        }

        // Στατική, "τεχνολογικού" ύφους εικόνα wallpaper — ζητήθηκε ρητά ένα νέο πακέτο θέματος με
        // tech-themed φόντο. Ένα αρχείο .theme των Windows μπορεί να αναφέρει μόνο ΣΤΑΤΙΚΗ εικόνα
        // (όχι το ζωντανό canvas engine μας) — αυτή η εικόνα είναι το "στατικό instantiation" του
        // ίδιου circuit-grid μοτίβου με το νέο ζωντανό στυλ "TechGrid" (βλ. wallpaper/index.html),
        // ώστε η επιφάνεια εργασίας να ταιριάζει οπτικά ακόμη και όταν το Opsis δεν τρέχει.
        public static string GenerateTechWallpaperPng()
        {
            string dir = Path.Combine(Services.AppPaths.AppDataFolder, "assets");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "tech_grid_wallpaper.png");

            int w = 2560, h = 1440;
            using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bgBrush = new LinearGradientBrush(new Point(0, 0), new Point(0, h), Color.FromArgb(2, 8, 5), Color.FromArgb(6, 20, 14)))
                    g.FillRectangle(bgBrush, 0, 0, w, h);

                var rng = new Random(1337);
                int cellSize = 80;
                using var gridPen = new Pen(Color.FromArgb(60, 57, 255, 148), 1f);
                for (int x = 0; x <= w; x += cellSize) g.DrawLine(gridPen, x, 0, x, h);
                for (int y = 0; y <= h; y += cellSize) g.DrawLine(gridPen, 0, y, w, y);

                using var nodeBrush = new SolidBrush(Color.FromArgb(220, 57, 255, 106));
                using var traceGlowPen = new Pen(Color.FromArgb(160, 0, 229, 255), 2.5f);
                int nodeCount = (w / cellSize) * (h / cellSize) / 8;
                for (int i = 0; i < nodeCount; i++)
                {
                    int col = rng.Next(w / cellSize), row = rng.Next(h / cellSize);
                    int nx = col * cellSize, ny = row * cellSize;
                    g.FillEllipse(nodeBrush, nx - 4, ny - 4, 8, 8);
                    // Λίγες τυχαίες "διαδρομές κυκλώματος" ξεκινώντας από κάθε κόμβο.
                    if (rng.NextDouble() < 0.5)
                    {
                        int len = 2 + rng.Next(4);
                        bool horizontal = rng.NextDouble() < 0.5;
                        int ex = nx + (horizontal ? len * cellSize : 0);
                        int ey = ny + (!horizontal ? len * cellSize : 0);
                        g.DrawLine(traceGlowPen, nx, ny, ex, ey);
                    }
                }
            }

            bmp.Save(path, ImageFormat.Png);
            return path;
        }

        private static string? GetCurrentSoundSchemeName()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes");
            return key?.GetValue(null) as string;
        }

        private static string GetCurrentDesktopWallpaper()
        {
            var sb = new StringBuilder(260);
            SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
            return sb.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, int uiParam, StringBuilder pvParam, uint fWinIni);
        private const uint SPI_GETDESKWALLPAPER = 0x0073;
    }
}
