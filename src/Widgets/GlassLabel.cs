using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using UiTheme = MotionDesk.UI.WidgetTheme;

namespace MotionDesk.Widgets
{
    /// <summary>
    /// Ετικέτα για τα native widgets που ζωγραφίζει το κείμενο σωστά πάνω σε διάφανο/θολό φόντο.
    /// Το κλασικό Label (GDI) πάνω σε color-key παράθυρο αφήνει μαύρα "κρόσσια" γύρω από τα γράμματα και τα ClearType χρώματα
    /// γίνονται βρώμικα· εδώ: γκρι anti-aliasing (χωρίς ClearType) + διακριτική σκιά όταν το widget είναι σε "Πλήρη διαφάνεια".
    /// Έχει το ίδιο API με το Label που χρησιμοποιούσαν τα widgets (Text, Font, ForeColor, TextAlign, AutoEllipsis, Dock...).
    /// </summary>
    internal class GlassLabel : Control
    {
        private ContentAlignment _textAlign = ContentAlignment.TopLeft;
        private bool _autoEllipsis;

        public GlassLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public ContentAlignment TextAlign { get => _textAlign; set { _textAlign = value; Invalidate(); } }
        public bool AutoEllipsis { get => _autoEllipsis; set { _autoEllipsis = value; Invalidate(); } }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }
        protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); Invalidate(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (BackColor.A < 255) base.OnPaintBackground(e);          // ζωγραφίζει ό,τι υπάρχει πίσω (parent)
            else using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        internal static StringFormat FormatFor(ContentAlignment a, bool ellipsis)
        {
            var f = new StringFormat(ellipsis ? StringFormatFlags.NoClip | StringFormatFlags.NoWrap : StringFormatFlags.NoClip) { Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None };
            f.Alignment = a is ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft ? StringAlignment.Near
                        : a is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight ? StringAlignment.Far : StringAlignment.Center;
            f.LineAlignment = a is ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight ? StringAlignment.Near
                            : a is ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight ? StringAlignment.Far : StringAlignment.Center;
            return f;
        }

        /// <summary>Ζωγραφίζει κείμενο με τον ίδιο τρόπο σε οποιοδήποτε control (σκιά στην πλήρη διαφάνεια).</summary>
        internal static void DrawGlassText(Graphics g, string text, Font font, Color color, RectangleF rect, StringFormat fmt)
        {
            if (string.IsNullOrEmpty(text) || rect.Width < 2 || rect.Height < 2) return;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (UiTheme.IsFullyTransparent)
            {
                // Not pure black (that is the colour key and would become a hole). Dark text gets a light halo, light text a dark one,
                // drawn on the 4 diagonals so the letters stay readable over any wallpaper (bright, dark, busy or moving).
                bool darkText = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) < 120;
                using var halo = new SolidBrush(darkText ? Color.FromArgb(205, 246, 246, 250) : Color.FromArgb(215, 14, 14, 18));
                foreach (var (dx, dy) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f), (0f, 1.5f) })
                    g.DrawString(text, font, halo, new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height), fmt);
            }
            using var brush = new SolidBrush(color);
            g.DrawString(text, font, brush, rect, fmt);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (string.IsNullOrEmpty(Text)) return;
            var r = new RectangleF(Padding.Left, Padding.Top, Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
            try
            {
                using var fmt = FormatFor(_textAlign, _autoEllipsis);
                DrawGlassText(e.Graphics, Text, Font, ForeColor, r, fmt);
            }
            catch (Exception ex) { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "glasslabel_err.txt"), ex + Environment.NewLine); }
        }
    }
}
