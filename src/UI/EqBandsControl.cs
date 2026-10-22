using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI
{
    /// <summary>Ισοσταθμιστής 10 ζωνών (σύρσιμο ανά ζώνη, διπλό κλικ = μηδέν, ροδέλα = ±0.5 dB). Owner-draw, χωρίς timers.</summary>
    public sealed class EqBandsControl : Control
    {
        private readonly double[] _bands = new double[10];
        private int _drag = -1;
        private const double Range = AudioEqProfile.MaxGainDb;
        private static readonly string[] Labels = { "32", "64", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

        public event EventHandler? BandsChanged;

        public EqBandsControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(700, 220);
            Cursor = Cursors.Hand;
        }

        public double[] Bands
        {
            get => (double[])_bands.Clone();
            set { for (int i = 0; i < 10; i++) _bands[i] = Snap(i < value.Length ? value[i] : 0); Invalidate(); }
        }

        private static double Snap(double v) => Math.Round(Math.Clamp(v, -Range, Range) * 2) / 2.0;     // βήμα 0.5 dB

        private RectangleF Plot => new(24, 22, Width - 48, Height - 22 - 24);
        private float CenterX(int i) => Plot.Left + Plot.Width * (i + 0.5f) / 10f;
        private float YOf(double gain) => Plot.Top + (float)((Range - gain) / (2 * Range)) * Plot.Height;
        private double GainAt(float y) => Snap(Range - (y - Plot.Top) / Plot.Height * 2 * Range);

        private int NearestBand(float x)
        {
            int best = 0; float d = float.MaxValue;
            for (int i = 0; i < 10; i++) { float dd = Math.Abs(CenterX(i) - x); if (dd < d) { d = dd; best = i; } }
            return d <= Plot.Width / 20f ? best : -1;
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _drag = NearestBand(e.X); if (_drag >= 0) Set(_drag, GainAt(e.Y)); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag >= 0) Set(_drag, GainAt(e.Y)); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = -1; }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); int b = NearestBand(e.X); if (b >= 0) Set(b, 0); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); int b = NearestBand(e.X); if (b >= 0) Set(b, _bands[b] + (e.Delta > 0 ? 0.5 : -0.5)); }

        private void Set(int band, double gain)
        {
            gain = Snap(gain);
            if (Math.Abs(_bands[band] - gain) < 0.001) return;
            _bands[band] = gain;
            Invalidate();
            BandsChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.Surface)) g.FillRectangle(bg, ClientRectangle);
            using var grid = new Pen(Color.FromArgb(60, UiTheme.Border), 1f);
            using var zero = new Pen(Color.FromArgb(150, UiTheme.TextMuted), 1.2f);
            using var small = new Font("Segoe UI", 8f);
            using var muted = new SolidBrush(UiTheme.TextMuted);
            foreach (int db in new[] { 12, 6, 0, -6, -12 })
            {
                float y = YOf(db);
                g.DrawLine(db == 0 ? zero : grid, Plot.Left, y, Plot.Right, y);
                g.DrawString(db > 0 ? $"+{db}" : db.ToString(), small, muted, 0, y - 7);
            }
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++) pts[i] = new PointF(CenterX(i), YOf(_bands[i]));
            // Καμπύλη απόκρισης (απλή, ομαλή) κάτω από τις λαβές
            using (var curve = new Pen(Color.FromArgb(200, UiTheme.AccentCyan), 2.2f)) g.DrawCurve(curve, pts, 0.45f);
            using var accent = new SolidBrush(UiTheme.AccentCyan);
            using var primary = new SolidBrush(UiTheme.TextPrimary);
            using var line = new Pen(Color.FromArgb(120, UiTheme.AccentCyan), 2f);
            for (int i = 0; i < 10; i++)
            {
                float x = CenterX(i), y = pts[i].Y;
                g.DrawLine(line, x, YOf(0), x, y);
                g.FillEllipse(accent, x - 7, y - 7, 14, 14);
                using (var ring = new Pen(UiTheme.Surface, 2f)) g.DrawEllipse(ring, x - 7, y - 7, 14, 14);
                string t = _bands[i] > 0 ? $"+{_bands[i]:0.#}" : $"{_bands[i]:0.#}";
                var ts = g.MeasureString(t, small);
                g.DrawString(t, small, primary, x - ts.Width / 2, Math.Max(2, y - 24));
                var ls = g.MeasureString(Labels[i], small);
                g.DrawString(Labels[i], small, muted, x - ls.Width / 2, Height - 18);
            }
        }
    }
}
