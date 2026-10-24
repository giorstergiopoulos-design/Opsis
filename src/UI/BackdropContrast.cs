using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI
{
    /// <summary>
    /// Decides whether the text of the fully transparent widgets must be dark or light, from the brightness of the desktop behind the
    /// widgets. The backdrop is the live Opsis wallpaper (snapshot of the current frame, so moving wallpapers are followed) or the
    /// Windows wallpaper image; it is sampled only under the widgets, every few seconds, with hysteresis so the colour never flickers.
    /// </summary>
    internal static class BackdropContrast
    {
        public static bool UseDarkText { get; private set; }

        private static System.Windows.Forms.Timer? _timer;
        private static bool _busy;
        private static double _smoothed = 0.2;

        public static void EnsureStarted()
        {
            if (_timer != null) return;
            _timer = new System.Windows.Forms.Timer { Interval = 2500 };
            _timer.Tick += async (_, _) => await TickAsync();
            _timer.Start();
            _ = TickAsync();
        }

        private static async Task TickAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var settings = AppSettings.Load();
                if (settings.WidgetsThemeMode != "Transparent" || !settings.WidgetsAutoContrast) { Set(false); return; }
                var rects = WidgetHostEngine.Instance.GetActiveWidgets().Where(w => !w.IsDisposed && w.Visible).Select(w => w.Bounds).ToList();
                if (rects.Count == 0) return;

                using var sample = await SnapshotAsync();
                if (sample == null) return;
                var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                double sum = 0; int n = 0;
                foreach (var r in rects)
                {
                    var area = Rectangle.Intersect(r, screen);
                    if (area.Width < 4 || area.Height < 4) continue;
                    int x0 = (int)((area.Left - screen.Left) * (double)sample.Width / screen.Width);
                    int x1 = (int)((area.Right - screen.Left) * (double)sample.Width / screen.Width);
                    int y0 = (int)((area.Top - screen.Top) * (double)sample.Height / screen.Height);
                    int y1 = (int)((area.Bottom - screen.Top) * (double)sample.Height / screen.Height);
                    for (int y = Math.Clamp(y0, 0, sample.Height - 1); y < Math.Clamp(y1, 1, sample.Height); y++)
                        for (int x = Math.Clamp(x0, 0, sample.Width - 1); x < Math.Clamp(x1, 1, sample.Width); x++)
                        {
                            var c = sample.GetPixel(x, y);
                            sum += (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; n++;
                        }
                }
                if (n == 0) return;
                double lum = sum / n;
                // a tinted widget has its own (dark or light) surface: judge the text against tint + backdrop
                int tint = settings.WidgetsTintPercent;
                if (tint > 0)
                {
                    var tc = WidgetTheme.NamedColor(settings.WidgetsTintColor);
                    double tl = (0.2126 * tc.R + 0.7152 * tc.G + 0.0722 * tc.B) / 255.0;
                    lum = lum * (1 - tint / 100.0) + tl * (tint / 100.0);
                }
                _smoothed = _smoothed * 0.5 + lum * 0.5;
                Set(UseDarkText ? _smoothed > 0.46 : _smoothed > 0.60);      // hysteresis: switch to dark text above 0.60, back to light below 0.46
            }
            catch (Exception ex) { RotatingLogger.Write("BackdropContrast: " + ex.Message); }
            finally { _busy = false; }
        }

        private static void Set(bool dark)
        {
            if (UseDarkText == dark) return;
            UseDarkText = dark;
            ThemeManager.RaiseRepainted();                    // every widget re-applies its colours
        }

        private static async Task<Bitmap?> SnapshotAsync()
        {
            // 1) the live Opsis wallpaper (waves / video / particles / weather ...): current frame
            if (WallpaperHostEngine.Instance.IsRunning)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "opsis_backdrop_" + Environment.ProcessId + ".png");
                try
                {
                    if (await WallpaperHostEngine.Instance.CaptureSnapshotAsync(tmp) && File.Exists(tmp))
                    {
                        using var full = new Bitmap(tmp);
                        return Shrink(full);
                    }
                }
                catch (Exception) { }
                finally { try { File.Delete(tmp); } catch (IOException) { } }
            }
            // 2) the Windows wallpaper image (static)
            string? path = DeskContainerExtras.ActiveWallpaperPath();
            if (path != null && File.Exists(path))
            {
                try { using var img = new Bitmap(path); return Shrink(img, Screen.PrimaryScreen?.Bounds.Size); }
                catch (Exception) { }
            }
            return null;
        }

        // low-resolution copy (about 160 wide); when a target screen size is given the image is fitted like "fill" so pixel positions line up with the screen
        private static Bitmap Shrink(Image src, Size? coverScreen = null)
        {
            int w = 160, h = coverScreen is { Width: > 0 } s ? Math.Max(1, (int)(160.0 * s.Height / s.Width)) : Math.Max(1, (int)(160.0 * src.Height / src.Width));
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            if (coverScreen == null) { g.DrawImage(src, 0, 0, w, h); return bmp; }
            double scale = Math.Max((double)w / src.Width, (double)h / src.Height);
            float dw = (float)(src.Width * scale), dh = (float)(src.Height * scale);
            g.DrawImage(src, (w - dw) / 2f, (h - dh) / 2f, dw, dh);
            return bmp;
        }
    }
}
