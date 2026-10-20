using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    // Ημιδιάφανο overlay που δείχνει τις ζώνες μιας οθόνης — εμφανίζεται ΜΟΝΟ όσο ο χρήστης κρατάει
    // Shift ενώ σέρνει ένα παράθυρο (βλ. ZoneSnapEngine), ακριβώς όπως το πραγματικό FancyZones
    // Editor overlay. Click-through (WS_EX_TRANSPARENT) + WS_EX_NOACTIVATE: δεν πρέπει ΠΟΤΕ να
    // κλέψει το mouse capture ή το focus από το drag operation που ήδη τρέχει σε άλλη εφαρμογή.
    //
    // ΔΙΟΡΘΩΣΗ πραγματικού bug ("κάνε τα ζωνάκια τουλάχιστον διάφανα" — ο χρήστης το έβλεπε σαν
    // συμπαγές, "έντονο" ορθογώνιο πάνω στο παράθυρο που έσερνε): η παλιά υλοποίηση χρησιμοποιούσε
    // Form.TransparencyKey (chroma-key) — αυτό κάνει αόρατο ΜΟΝΟ το ακριβές χρώμα-κλειδί
    // (μαύρο)· οποιοδήποτε ΑΛΛΟ χρώμα, ΑΚΟΜΑ κι αν ζωγραφίστηκε με alpha<255 στο GDI+, γίνεται
    // ΤΕΛΙΚΑ ένα συμπαγές, αδιαφανές pixel στην οθόνη — το transparency key δεν κάνει καθόλου
    // per-pixel alpha blending απέναντι σε ό,τι βρίσκεται πίσω από το παράθυρο. Πραγματική
    // διαφάνεια χρειάζεται layered window με πραγματικό per-pixel alpha (UpdateLayeredWindow) —
    // τεκμηριωμένη, καθιερωμένη τεχνική των ίδιων των Windows για ακριβώς αυτή τη δουλειά (OSDs,
    // splash screens, tooltips), όχι κάτι αντιγραμμένο από συγκεκριμένο εργαλείο.
    public sealed class ZoneOverlayWindow : Form
    {
        private ZoneLayoutData _layout = new();
        private Rectangle _workingArea;
        private int _highlightIndex = -1;

        public ZoneOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
        }

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                return cp;
            }
        }

        // Το ShowWithoutActivation=true (built-in WinForms hook) εγγυάται ότι το Show() παρακάτω
        // ΔΕΝ θα πάρει focus/activation — το drag operation του χρήστη σε ΑΛΛΟ παράθυρο συνεχίζει
        // απρόσκοπτο.
        protected override bool ShowWithoutActivation => true;

        public void ShowForScreen(Screen screen, ZoneLayoutData layout)
        {
            _layout = layout;
            _workingArea = screen.WorkingArea;
            Bounds = _workingArea;
            _highlightIndex = -1;
            if (!Visible) Show();
            Render();
        }

        public void UpdateHighlight(int zoneIndex)
        {
            if (_highlightIndex == zoneIndex) return;
            _highlightIndex = zoneIndex;
            Render();
        }

        public void HideOverlay()
        {
            if (Visible) Hide();
        }

        // Δίνει το απόλυτο ορθογώνιο (screen coords) της ζώνης που είναι αυτή τη στιγμή highlighted,
        // ή null αν ο δείκτης δεν είναι πάνω σε καμία ζώνη — αυτό είναι το σημείο "κουμπώματος".
        // Ζητήθηκε για το occupancy tracking (ZoneOccupancy) — ώστε να καταγράφεται ΠΟΙΑ ζώνη (όχι
        // μόνο τα pixel bounds της) κούμπωσε το παράθυρο, για το neighbor-resize feature.
        public int HighlightedIndex => _highlightIndex;

        public Rectangle? GetHighlightedZoneBounds() =>
            _highlightIndex >= 0 && _highlightIndex < _layout.Zones.Count
                ? _layout.Zones[_highlightIndex].ToAbsolute(_workingArea)
                : null;

        public int FindZoneIndexAt(Point screenPt)
        {
            for (int i = 0; i < _layout.Zones.Count; i++)
                if (_layout.Zones[i].ToAbsolute(_workingArea).Contains(screenPt)) return i;
            return -1;
        }

        // Χτίζει ένα πραγματικό ARGB (premultiplied) bitmap του overlay και το σπρώχνει στην οθόνη
        // μέσω UpdateLayeredWindow — ΚΑΘΕ pixel έχει τη ΔΙΚΗ ΤΟΥ διαφάνεια (τα κενά ανάμεσα σε
        // ζώνες είναι 100% αόρατα, τα γεμίσματα ζωνών πραγματικά ημιδιάφανα πάνω σε ό,τι υπάρχει
        // από κάτω), αντί για το "όλο ή τίποτα" chroma-key που είχε πριν.
        private void Render()
        {
            if (_workingArea.Width <= 0 || _workingArea.Height <= 0) return;

            using var bmp = new Bitmap(_workingArea.Width, _workingArea.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < _layout.Zones.Count; i++)
                {
                    var rect = _layout.Zones[i].ToAbsolute(_workingArea);
                    rect.Offset(-_workingArea.X, -_workingArea.Y);
                    bool highlighted = i == _highlightIndex;

                    // Χαμηλότερο alpha παντού — ζητήθηκε ρητά "κάνε τα τουλάχιστον διάφανα": η μη-
                    // επιλεγμένη ζώνη είναι πλέον μόλις ορατή (περίγραμμα μόνο, σχεδόν χωρίς γέμισμα),
                    // η επιλεγμένη παραμένει ευδιάκριτη αλλά πραγματικά ημιδιάφανη, όχι συμπαγής.
                    using var fill = new SolidBrush(highlighted
                        ? Color.FromArgb(80, UiTheme.AccentCyan)
                        : Color.FromArgb(16, UiTheme.AccentCyan));
                    using var border = new Pen(highlighted ? Color.FromArgb(230, UiTheme.AccentCyan) : Color.FromArgb(110, UiTheme.AccentCyan), highlighted ? 2.5f : 1.2f);
                    using var path = UiTheme.RoundedPath(rect, 10);
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);

                    // zone number in the centre (1, 2, 3 …) — makes layouts easy to read and to talk about
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    float px = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.26f, 14f, 72f);
                    using var numFont = new Font("Segoe UI Semibold", px, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var numBrush = new SolidBrush(Color.FromArgb(highlighted ? 235 : 120, 255, 255, 255));
                    using var numFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString((i + 1).ToString(), numFont, numBrush, rect, numFormat);
                }
            }

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);
            try
            {
                var size = new SIZE { cx = _workingArea.Width, cy = _workingArea.Height };
                var dst = new POINT { X = _workingArea.X, Y = _workingArea.Y };
                var src = new POINT { X = 0, Y = 0 };
                var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x00000002;

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    }
}
