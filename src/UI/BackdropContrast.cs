using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI
{
    /// <summary>
    /// Decides whether the text of the fully transparent widgets must be dark or light, from the brightness of the desktop behind the
    /// widgets (what is really on screen: the Windows wallpaper or the live Opsis wallpaper, moving or not). The pixels under each widget are read
    /// straight from the screen every few seconds, with the widget windows excluded from the capture while it happens, so nothing on the
    /// wallpaper (clock overlay, WebView) is ever touched or redrawn. Hysteresis keeps the colour from flickering.
    /// </summary>
    internal static class BackdropContrast
    {
        public static bool UseDarkText { get; private set; }

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        private const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        private static System.Windows.Forms.Timer? _timer;
        private static double _smoothed = 0.2;

        public static void EnsureStarted()
        {
            if (_timer != null) return;
            _timer = new System.Windows.Forms.Timer { Interval = 3000 };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        private static void Tick()
        {
            try
            {
                var settings = AppSettings.Load();
                if (settings.WidgetsThemeMode != "Transparent" || !settings.WidgetsAutoContrast) { Set(false); return; }
                var widgets = WidgetHostEngine.Instance.GetActiveWidgets().Where(w => !w.IsDisposed && w.Visible && w.IsHandleCreated).ToList();
                if (widgets.Count == 0) return;

                double? lum = SampleLuminance(widgets);
                if (lum == null) return;
                double v = lum.Value;
                // a tinted widget has its own (dark or light) surface: judge the text against tint + backdrop
                int tint = settings.WidgetsTintPercent;
                if (tint > 0)
                {
                    var tc = WidgetTheme.NamedColor(settings.WidgetsTintColor);
                    double tl = (0.2126 * tc.R + 0.7152 * tc.G + 0.0722 * tc.B) / 255.0;
                    v = v * (1 - tint / 100.0) + tl * (tint / 100.0);
                }
                _smoothed = _smoothed * 0.5 + v * 0.5;
                Set(UseDarkText ? _smoothed > 0.46 : _smoothed > 0.60);      // hysteresis: dark text above 0.60, back to light below 0.46
            }
            catch (Exception ex) { RotatingLogger.Write("BackdropContrast: " + ex.Message); }
        }

        private static double? SampleLuminance(List<WidgetWindow> widgets)
        {
            var screen = SystemInformation.VirtualScreen;
            double sum = 0; long n = 0;
            // take the widgets out of the screen capture for the few milliseconds the read takes (they stay visible on the monitor)
            foreach (var w in widgets) SetWindowDisplayAffinity(w.Handle, WDA_EXCLUDEFROMCAPTURE);
            try
            {
                foreach (var w in widgets)
                {
                    var area = Rectangle.Intersect(w.Bounds, screen);
                    if (area.Width < 8 || area.Height < 8) continue;
                    using var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        var row = new byte[Math.Abs(data.Stride)];
                        for (int y = 0; y < bmp.Height; y += 4)
                        {
                            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                            for (int x = 0; x < bmp.Width; x += 4)
                            {
                                int i = x * 4;
                                sum += (0.2126 * row[i + 2] + 0.7152 * row[i + 1] + 0.0722 * row[i]) / 255.0; n++;
                            }
                        }
                    }
                    finally { bmp.UnlockBits(data); }
                }
            }
            finally { foreach (var w in widgets) SetWindowDisplayAffinity(w.Handle, WDA_NONE); }
            return n == 0 ? null : sum / n;
        }

        private static void Set(bool dark)
        {
            if (UseDarkText == dark) return;
            UseDarkText = dark;
            ThemeManager.RaiseRepainted();                    // every widget re-applies its colours
        }
    }
}
