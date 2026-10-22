using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.Widgets
{
    // Ορατό banner όταν το Opsis ξεκίνησε σε safe mode (v2.0 handoff, Φάση 1 §S) — παραμένει
    // ορατό όσο τρέχει η εφαρμογή, ώστε ο χρήστης να ξέρει ότι wallpaper/plugins/"Last Session"
    // ΔΕΝ φορτώθηκαν αυτή τη φορά. "Έξοδος από safe mode" μηδενίζει τον μετρητή crash και
    // επανεκκινεί κανονικά (απλούστερο και πιο ασφαλές από το να προσπαθήσουμε να αρχικοποιήσουμε
    // wallpaper/plugins εν κινήσει μέσα στην ίδια διεργασία).
    public sealed class SafeModeBanner : Form
    {
        public SafeModeBanner()
        {
            MotionDesk.UI.Flicker.Attach(this);
            var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(120, 40, 10);
            Height = 36;
            Width = area.Width;
            Location = new Point(area.X, area.Y);

            var label = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = BuildMessage()
            };
            Controls.Add(label);

            var exitBtn = new Button
            {
                Text = LocalizationManager.T("SafeMode.ExitButton"),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 255, 255, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Size = new Size(160, 26),
                Location = new Point(Width - 176, 5),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            exitBtn.FlatAppearance.BorderSize = 0;
            exitBtn.Click += (_, _) => ExitSafeMode();
            Controls.Add(exitBtn);
            exitBtn.BringToFront();
        }

        private static string BuildMessage() =>
            SafeModeService.TriggeredByShift
                ? LocalizationManager.T("SafeMode.BannerShift")
                : string.Format(LocalizationManager.T("SafeMode.BannerCrash"), SafeModeService.ConsecutiveFailedStartups);

        private static void ExitSafeMode()
        {
            SafeModeService.MarkStartupHealthy();
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe)) Process.Start(exe);
            }
            catch (Exception) { }
            Application.Exit();
        }
    }
}
