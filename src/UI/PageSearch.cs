using System.Globalization;
using System.Text;
using System.Windows.Forms;
using MotionDesk.Services;
using Opsis.UI;

namespace MotionDesk.UI
{
    // Search box at the top of every settings page: type a word and only the cards / sections / rows that contain it stay visible.
    // The matching is accent- and case-insensitive (Greek tonos, German umlauts...) and pure, so it is unit-tested.
    internal static class PageSearch
    {
        public static string Normalize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString().Replace("&", "");
        }

        /// <summary>True when every word of the query occurs in the text.</summary>
        public static bool Matches(string? text, string? query)
        {
            var q = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (q.Length == 0) return true;
            var t = Normalize(text);
            foreach (var w in q) if (!t.Contains(w)) return false;
            return true;
        }

        private static void Collect(Control c, StringBuilder sb)
        {
            if (c is Label or ButtonBase) sb.Append(c.Text).Append(' ');
            if (c is FlatToggle t) sb.Append(t.Description).Append(' ');
            foreach (Control child in c.Controls) Collect(child, sb);
        }

        public static string TextOf(Control c) { var sb = new StringBuilder(); Collect(c, sb); return sb.ToString(); }

        private static bool IsSectionHeader(Control c) => c is Label l && l.Font.Bold && l.ForeColor == UiTheme.AccentCyan;

        /// <summary>Adds the search box as the first control of a page and wires the filtering.</summary>
        public static void Attach(FlowLayoutPanel page)
        {
            var box = new TextBox
            {
                Width = 360,
                PlaceholderText = LocalizationManager.T("Settings.SearchPlaceholder"),
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UiTheme.FontBody,
                Margin = new Padding(0, 0, 0, 10),
                Tag = "search"
            };
            var none = new Label
            {
                AutoSize = true,
                Text = LocalizationManager.T("Settings.SearchNoMatch"),
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextSecondary,
                Margin = new Padding(0, 8, 0, 0),
                Visible = false,
                Tag = "search"
            };
            page.Controls.Add(box);
            page.Controls.SetChildIndex(box, 0);
            page.Controls.Add(none);
            box.TextChanged += (_, _) => Filter(page, box.Text, none);
        }

        private static void Filter(FlowLayoutPanel page, string query, Label none)
        {
            var items = new List<Control>();
            foreach (Control c in page.Controls) if (!"search".Equals(c.Tag)) items.Add(c);
            page.SuspendLayout();
            try
            {
                if (string.IsNullOrWhiteSpace(query)) { foreach (var c in items) c.Visible = true; none.Visible = false; return; }
                // a group = a section header and the loose rows after it, or one card / one other control on its own
                var groups = new List<List<Control>>();
                foreach (var c in items)
                {
                    if (IsSectionHeader(c) || groups.Count == 0 || !IsSectionHeader(groups[^1][0])) groups.Add(new List<Control> { c });
                    else groups[^1].Add(c);
                }
                bool any = false;
                foreach (var g in groups)
                {
                    bool hit = g.Any(c => Matches(TextOf(c), query));
                    foreach (var c in g) c.Visible = hit;
                    any |= hit;
                }
                none.Visible = !any;
            }
            finally { page.ResumeLayout(true); }
        }
    }
}
