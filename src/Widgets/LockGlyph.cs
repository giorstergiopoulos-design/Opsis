using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MotionDesk.Widgets
{
    /// <summary>
    /// Open / closed padlock drawn as a vector (instead of the 🔒/🔓 emoji, which the GDI text renderer shows as boxes or tiny blobs
    /// on the translucent and fully transparent widget shells). Painted in ForeColor, transparent background, anti-aliased.
    /// </summary>
    internal sealed class LockGlyph : Control
    {
        private bool _locked;

        public LockGlyph()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public bool Locked { get => _locked; set { _locked = value; Invalidate(); } }

        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { if (BackColor.A < 255) base.OnPaintBackground(e); else e.Graphics.Clear(BackColor); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(Width, Height);
            float w = s * 0.46f, h = s * 0.34f;                         // body
            float x = (Width - w) / 2f, y = (Height - s) / 2f + s * 0.46f;
            using var pen = new Pen(ForeColor, Math.Max(1.6f, s * 0.085f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var brush = new SolidBrush(ForeColor);

            // shackle: closed = full arch over the body, open = arch lifted and shifted to the right with a free left leg
            float r = w * 0.34f;
            float cx = Width / 2f;
            float top = y - s * (_locked ? 0.26f : 0.34f);
            if (_locked)
            {
                g.DrawArc(pen, cx - r, top, r * 2, r * 2, 180, 180);
                g.DrawLine(pen, cx - r, top + r, cx - r, y);
                g.DrawLine(pen, cx + r, top + r, cx + r, y);
            }
            else
            {
                float ox = r * 0.9f;                                    // swung open to the right
                g.DrawArc(pen, cx - r + ox, top, r * 2, r * 2, 180, 180);
                g.DrawLine(pen, cx + r + ox, top + r, cx + r + ox, y - s * 0.05f);
                g.DrawLine(pen, cx - r + ox, top + r, cx - r + ox, y - s * 0.14f);
            }
            using var path = new GraphicsPath();
            float rad = s * 0.07f;
            path.AddArc(x, y, rad * 2, rad * 2, 180, 90);
            path.AddArc(x + w - rad * 2, y, rad * 2, rad * 2, 270, 90);
            path.AddArc(x + w - rad * 2, y + h - rad * 2, rad * 2, rad * 2, 0, 90);
            path.AddArc(x, y + h - rad * 2, rad * 2, rad * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
            // keyhole
            using var hole = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            float kr = s * 0.045f;
            g.FillEllipse(hole, cx - kr, y + h * 0.5f - kr, kr * 2, kr * 2);
        }
    }
}
