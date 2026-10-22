using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MotionDesk.UI
{
    // Διανυσματικά (όχι γραμματοσειράς) εικονίδια για το sidebar — αντικαθιστούν τα glyphs του
    // "Segoe Fluent Icons" που ζητήθηκε να αναβαθμιστούν σε υψηλότερη ποιότητα: σταθερή πάχους
    // γραμμή, χωρίς εξάρτηση από το αν είναι εγκατεστημένη συγκεκριμένη γραμματοσειρά συστήματος,
    // καθαρά σε κάθε DPI/μέγεθος. Όλα σχεδιάζονται σε ένα σταθερό 20x20 πλέγμα αναφοράς που
    // κλιμακώνεται στο πραγματικό ορθογώνιο-στόχο.
    public static class IconRenderer
    {
        public static void Draw(Graphics g, string key, RectangleF bounds, Color color, float strokeWidth = 1.6f)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);

            using var pen = new Pen(color, strokeWidth / (bounds.Width / 20f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var brush = new SolidBrush(color);

            switch (key)
            {
                case "Dashboard": DrawHome(g, pen); break;
                case string pluginKey when pluginKey.StartsWith("plugin:", StringComparison.Ordinal): DrawNews(g, pen); break; // σελίδες plugin
                case "Widgets": DrawGrid(g, pen); break;
                case "DeskZones": DrawZones(g, pen); break;
                case "Wallpaper": DrawImage(g, pen, brush); break;
                case "Performance": DrawBolt(g, pen); break;
                case "Settings": DrawGear(g, pen); break;
                case "Profiles": DrawPeople(g, pen); break;
                case "Automation": DrawCycle(g, pen); break;
                case "About": DrawInfo(g, pen, brush); break;
                case "Clock": DrawClock(g, pen); break;
                case "Network": DrawNetwork(g, pen, brush); break;
                case "AudioVisualizer": case "AudioEnhancement": DrawAudio(g, pen); break;
                case "Weather": DrawWeather(g, pen, brush); break;
                case "SystemMonitor": DrawPulse(g, pen); break;
                case "Disk": DrawDisk(g, pen, brush); break;
                case "Save": DrawSave(g, pen); break;
                case "Restore": DrawRestore(g, pen); break;
                case "Close": DrawClose(g, pen); break;
                case "Add": DrawAdd(g, pen); break;
                case "Delete": DrawDelete(g, pen); break;
                case "Command": DrawCommand(g, pen); break;
                case "Work": DrawBriefcase(g, pen); break;
                case "Gaming": DrawGameController(g, pen); break;
                case "Focus": DrawTarget(g, pen); break;
                case "Container": DrawContainer(g, pen); break;
                case "Rename": DrawRename(g, pen); break;
                case "Personalization": DrawPalette(g, pen, brush); break;
                case "Media": DrawMedia(g, pen, brush); break;
                case "Notifications": DrawNotifications(g, pen); break;
                case "Calendar": DrawCalendar(g, pen); break;
                case "Bluetooth": DrawBluetooth(g, pen); break;
            }

            g.Restore(state);
        }

        private static void DrawHome(Graphics g, Pen pen)
        {
            g.DrawLines(pen, new[] { new PointF(2.5f, 10.5f), new PointF(10, 3.5f), new PointF(17.5f, 10.5f) });
            g.DrawLine(pen, 4.5f, 9.5f, 4.5f, 17);
            g.DrawLine(pen, 15.5f, 9.5f, 15.5f, 17);
            g.DrawLine(pen, 4.5f, 17, 15.5f, 17);
            g.DrawLine(pen, 8.3f, 17, 8.3f, 12.5f);
            g.DrawLine(pen, 11.7f, 17, 11.7f, 12.5f);
            g.DrawLine(pen, 8.3f, 12.5f, 11.7f, 12.5f);
        }

        // Εικονίδιο "εφημερίδα/ticker" για τις σελίδες plugin (π.χ. News Crawler).
        private static void DrawNews(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 2.5f, 4f, 15, 12, 2);
            g.DrawLine(pen, 5.5f, 8f, 14.5f, 8f);
            g.DrawLine(pen, 5.5f, 11f, 11f, 11f);
        }

        private static void DrawGrid(Graphics g, Pen pen)
        {
            foreach (var (x, y) in new[] { (3f, 3f), (11f, 3f), (3f, 11f), (11f, 11f) })
                DrawRounded(g, pen, x, y, 6, 6, 1.6f);
        }

        private static void DrawZones(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 2.5f, 3f, 15, 14, 2);
            g.DrawLine(pen, 9.5f, 3f, 9.5f, 17f);
            g.DrawLine(pen, 9.5f, 10.5f, 17.5f, 10.5f);
        }

        private static void DrawImage(Graphics g, Pen pen, Brush brush)
        {
            DrawRounded(g, pen, 2.5f, 3.5f, 15, 13, 2);
            g.FillEllipse(brush, 5.2f, 6f, 2.6f, 2.6f);
            g.DrawLines(pen, new[] { new PointF(4f, 15.5f), new PointF(9f, 10.5f), new PointF(12f, 13.5f), new PointF(14.5f, 11f), new PointF(16.5f, 14.5f) });
        }

        private static void DrawBolt(Graphics g, Pen pen)
        {
            g.DrawLines(pen, new[]
            {
                new PointF(11.5f, 2.5f), new PointF(5f, 11.5f), new PointF(9.5f, 11.5f),
                new PointF(8.5f, 17.5f), new PointF(15f, 8f), new PointF(10.5f, 8f), new PointF(11.5f, 2.5f)
            });
        }

        private static void DrawGear(Graphics g, Pen pen)
        {
            const float cx = 10, cy = 10, r = 4.6f, tooth = 2.1f;
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                float x1 = cx + (float)(Math.Cos(a) * r);
                float y1 = cy + (float)(Math.Sin(a) * r);
                float x2 = cx + (float)(Math.Cos(a) * (r + tooth));
                float y2 = cy + (float)(Math.Sin(a) * (r + tooth));
                g.DrawLine(pen, x1, y1, x2, y2);
            }
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.DrawEllipse(pen, cx - 1.6f, cy - 1.6f, 3.2f, 3.2f);
        }

        private static void DrawPeople(Graphics g, Pen pen)
        {
            g.DrawEllipse(pen, 3.2f, 4.5f, 5, 5);
            g.DrawArc(pen, 2f, 11f, 7.4f, 7, 190, 160);
            g.DrawEllipse(pen, 11f, 6.5f, 4.2f, 4.2f);
            g.DrawArc(pen, 10.5f, 12f, 6.5f, 6, 200, 140);
        }

        private static void DrawCycle(Graphics g, Pen pen)
        {
            g.DrawArc(pen, 3.5f, 3.5f, 13, 13, -160, 250);
            g.DrawArc(pen, 3.5f, 3.5f, 13, 13, 20, 250);
            g.DrawLines(pen, new[] { new PointF(15.2f, 3.6f), new PointF(16.8f, 6.2f), new PointF(13.8f, 6.6f) });
            g.DrawLines(pen, new[] { new PointF(4.8f, 16.4f), new PointF(3.2f, 13.8f), new PointF(6.2f, 13.4f) });
        }

        private static void DrawInfo(Graphics g, Pen pen, Brush brush)
        {
            g.DrawEllipse(pen, 2.5f, 2.5f, 15, 15);
            g.FillEllipse(brush, 9.1f, 6f, 1.8f, 1.8f);
            g.DrawLine(pen, 10, 9.3f, 10, 14.5f);
        }

        private static void DrawClock(Graphics g, Pen pen)
        {
            g.DrawEllipse(pen, 2.5f, 2.5f, 15, 15);
            g.DrawLine(pen, 10, 10, 10, 5.5f);
            g.DrawLine(pen, 10, 10, 13.5f, 12f);
        }

        private static void DrawNetwork(Graphics g, Pen pen, Brush brush)
        {
            g.DrawArc(pen, 4f, 4f, 12, 12, 210, 120);
            g.DrawArc(pen, 6.5f, 6.5f, 7, 7, 210, 120);
            g.FillEllipse(brush, 8.9f, 12.5f, 2.2f, 2.2f);
        }

        private static void DrawAudio(Graphics g, Pen pen)
        {
            float[] heights = { 5f, 10f, 14f, 8f, 6f };
            for (int i = 0; i < heights.Length; i++)
            {
                float x = 3f + i * 3.5f;
                float h = heights[i];
                g.DrawLine(pen, x, 10 - h / 2, x, 10 + h / 2);
            }
        }

        private static void DrawWeather(Graphics g, Pen pen, Brush brush)
        {
            g.FillEllipse(brush, 3.5f, 3.5f, 3.6f, 3.6f);
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 2 + i * (Math.PI / 2);
                float cx = 5.3f, cy = 5.3f, r1 = 2.6f, r2 = 4.3f;
                g.DrawLine(pen, cx + (float)(Math.Cos(a) * r1), cy + (float)(Math.Sin(a) * r1), cx + (float)(Math.Cos(a) * r2), cy + (float)(Math.Sin(a) * r2));
            }
            g.DrawArc(pen, 6.5f, 8f, 10, 8, 200, 220);
            g.DrawArc(pen, 3f, 9.5f, 7, 6.5f, 160, 200);
            g.DrawLine(pen, 5.3f, 16.3f, 16f, 16.3f);
        }

        private static void DrawPulse(Graphics g, Pen pen)
        {
            g.DrawLines(pen, new[]
            {
                new PointF(2f, 11f), new PointF(6f, 11f), new PointF(8f, 5f),
                new PointF(11f, 16f), new PointF(13f, 11f), new PointF(18f, 11f)
            });
        }

        private static void DrawMedia(Graphics g, Pen pen, Brush brush)
        {
            g.FillEllipse(brush, 3f, 13f, 5f, 4f);
            g.DrawLine(pen, 7.8f, 15f, 7.8f, 3.5f);
            g.DrawLine(pen, 7.8f, 3.5f, 15f, 5.5f);
            g.DrawLine(pen, 15f, 5.5f, 15f, 10f);
        }

        private static void DrawNotifications(Graphics g, Pen pen)
        {
            g.DrawArc(pen, 5f, 2.5f, 10, 10, 180, 180);
            g.DrawLine(pen, 5f, 7.5f, 3.5f, 15f);
            g.DrawLine(pen, 15f, 7.5f, 16.5f, 15f);
            g.DrawLine(pen, 3.5f, 15f, 16.5f, 15f);
            g.DrawArc(pen, 8f, 15f, 4, 3, 0, 180);
        }

        private static void DrawCalendar(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 2.5f, 4f, 15, 13, 1.6f);
            g.DrawLine(pen, 2.5f, 8f, 17.5f, 8f);
            g.DrawLine(pen, 6f, 2.5f, 6f, 5.5f);
            g.DrawLine(pen, 14f, 2.5f, 14f, 5.5f);
        }

        private static void DrawBluetooth(Graphics g, Pen pen)
        {
            g.DrawLines(pen, new[] { new PointF(10, 3), new PointF(14, 7), new PointF(6, 13), new PointF(10, 17) });
            g.DrawLines(pen, new[] { new PointF(10, 3), new PointF(6, 7), new PointF(14, 13), new PointF(10, 17) });
        }

        private static void DrawSave(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 3f, 3f, 14, 14, 1.6f);
            g.DrawRectangle(pen, 6f, 3.2f, 8, 5);
            g.DrawRectangle(pen, 6.5f, 11f, 7, 5.5f);
        }

        private static void DrawRestore(Graphics g, Pen pen)
        {
            g.DrawArc(pen, 3f, 3f, 14, 14, -220, 250);
            g.DrawLines(pen, new[] { new PointF(3f, 5.5f), new PointF(3f, 2f), new PointF(6.3f, 3.6f) });
        }

        private static void DrawClose(Graphics g, Pen pen)
        {
            g.DrawEllipse(pen, 2.5f, 2.5f, 15, 15);
            g.DrawLine(pen, 7.3f, 7.3f, 12.7f, 12.7f);
            g.DrawLine(pen, 12.7f, 7.3f, 7.3f, 12.7f);
        }

        private static void DrawAdd(Graphics g, Pen pen)
        {
            g.DrawEllipse(pen, 2.5f, 2.5f, 15, 15);
            g.DrawLine(pen, 10, 6.5f, 10, 13.5f);
            g.DrawLine(pen, 6.5f, 10, 13.5f, 10);
        }

        private static void DrawDelete(Graphics g, Pen pen)
        {
            g.DrawLine(pen, 4f, 6f, 16f, 6f);
            g.DrawLine(pen, 7.5f, 6f, 7.5f, 3.6f);
            g.DrawLine(pen, 12.5f, 6f, 12.5f, 3.6f);
            g.DrawLine(pen, 7.5f, 3.6f, 12.5f, 3.6f);
            DrawRounded(g, pen, 5.2f, 6f, 9.6f, 11, 1.4f);
            g.DrawLine(pen, 8f, 8.7f, 8f, 14.3f);
            g.DrawLine(pen, 12f, 8.7f, 12f, 14.3f);
        }

        private static void DrawCommand(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 2.5f, 3.5f, 15, 13, 2);
            g.DrawLines(pen, new[] { new PointF(5.5f, 8f), new PointF(8.5f, 10.3f), new PointF(5.5f, 12.6f) });
            g.DrawLine(pen, 10.5f, 12.6f, 14.5f, 12.6f);
        }

        private static void DrawBriefcase(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 2.5f, 6.5f, 15, 10, 1.6f);
            g.DrawLine(pen, 2.5f, 10.5f, 17.5f, 10.5f);
            DrawRounded(g, pen, 7.3f, 3.5f, 5.4f, 3.4f, 1);
        }

        private static void DrawGameController(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 2f, 6.5f, 16, 8, 4);
            g.DrawLine(pen, 5.8f, 10.5f, 8.2f, 10.5f);
            g.DrawLine(pen, 7f, 9.3f, 7f, 11.7f);
            g.DrawEllipse(pen, 12.3f, 8f, 1.8f, 1.8f);
            g.DrawEllipse(pen, 14.5f, 10.2f, 1.8f, 1.8f);
        }

        private static void DrawTarget(Graphics g, Pen pen)
        {
            g.DrawEllipse(pen, 2.5f, 2.5f, 15, 15);
            g.DrawEllipse(pen, 6.2f, 6.2f, 7.6f, 7.6f);
            g.DrawEllipse(pen, 9.1f, 9.1f, 1.8f, 1.8f);
        }

        // Δίσκος/μονάδα αποθήκευσης — κύλινδρος (drive) με μια γραμμή χωρητικότητας από κάτω.
        private static void DrawDisk(Graphics g, Pen pen, Brush brush)
        {
            g.DrawEllipse(pen, 4f, 3f, 12, 4);
            g.DrawLine(pen, 4f, 5f, 4f, 15f);
            g.DrawLine(pen, 16f, 5f, 16f, 15f);
            g.DrawArc(pen, 4f, 13f, 12, 4, 0, 180);
            g.FillEllipse(brush, 9f, 8.5f, 2.2f, 2.2f);
        }

        private static void DrawContainer(Graphics g, Pen pen)
        {
            DrawRounded(g, pen, 2.5f, 4f, 15, 12, 2);
            g.DrawLine(pen, 2.5f, 7.5f, 17.5f, 7.5f);
            foreach (var (x, y) in new[] { (5.5f, 11.5f), (9.5f, 11.5f), (13.5f, 11.5f) })
                DrawRounded(g, pen, x - 1.3f, y - 1.3f, 2.6f, 2.6f, 0.6f);
        }

        // Παλέτα ζωγραφικής με τρύπα αντίχειρα + λίγα "χρώματα" — αντιπροσωπεύει την Εξατομίκευση
        // (IconAtlas/DeskCursors/DeskStrip/DeskSounds), όχι κάποια συγκεκριμένη υπο-λειτουργία.
        private static void DrawPalette(Graphics g, Pen pen, SolidBrush brush)
        {
            using var outline = new GraphicsPath();
            outline.AddArc(2f, 3f, 15f, 15f, 130, 280);
            outline.AddArc(11.5f, 12.5f, 4f, 4f, 50, -140);
            outline.CloseFigure();
            g.DrawPath(pen, outline);
            foreach (var (x, y) in new[] { (7f, 8f), (11.5f, 6.7f), (15f, 9.3f), (7.3f, 12.8f) })
                g.FillEllipse(brush, x - 1.15f, y - 1.15f, 2.3f, 2.3f);
        }

        private static void DrawRename(Graphics g, Pen pen)
        {
            g.DrawLine(pen, 3.5f, 16.5f, 6f, 16.5f);
            g.DrawLine(pen, 6f, 16.5f, 6f, 14f);
            g.DrawLine(pen, 6f, 14f, 14.5f, 5.5f);
            g.DrawLine(pen, 14.5f, 5.5f, 17f, 8f);
            g.DrawLine(pen, 17f, 8f, 8.5f, 16.5f);
            g.DrawLine(pen, 8.5f, 16.5f, 6f, 16.5f);
        }

        // Το GraphicsPath κρατά native μνήμη — πρέπει να γίνεται Dispose (πριν γινόταν "new" ανά σχεδίαση).
        private static void DrawRounded(Graphics g, Pen pen, float x, float y, float w, float h, float r)
        {
            using var path = RoundedRect(x, y, w, h, r);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            float d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
