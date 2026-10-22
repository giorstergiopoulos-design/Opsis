using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MotionDesk.UI
{
    // Κεντρική αντι-flicker υποδομή. Τρεις πηγές flicker σε WinForms:
    //   1. container controls (Panel/FlowLayoutPanel/TableLayoutPanel/...) χωρίς double buffering — κάθε resize/layout
    //      ζωγραφίζει πρώτα το φόντο και μετά τα παιδιά (ορατό "αναβόσβημα"/λευκά τετραγωνάκια)
    //   2. ανακατασκευή σελίδας (Controls.Clear + Add) με ενδιάμεσα repaints
    //   3. WebView2 hosts: εκεί ΔΕΝ πρέπει να μπει double buffering/WS_EX_COMPOSITED (σπάει το DirectComposition)
    public static class Flicker
    {
        private static readonly PropertyInfo? DoubleBufferedProp =
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int WM_SETREDRAW = 0x000B;

        private static bool IsWebHost(Control c) => c.GetType().Name.Contains("WebView", StringComparison.OrdinalIgnoreCase);

        /// <summary>Ενεργοποιεί double buffering στο control ΚΑΙ σε κάθε μελλοντικό παιδί του (αναδρομικά).</summary>
        public static void Attach(Control root)
        {
            if (root == null || IsWebHost(root)) return;
            try { DoubleBufferedProp?.SetValue(root, true); } catch (Exception) { }
            foreach (Control child in root.Controls) Attach(child);
            root.ControlAdded -= OnControlAdded;   // idempotent
            root.ControlAdded += OnControlAdded;
        }

        private static void OnControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control != null) Attach(e.Control);
        }

        /// <summary>Εκτελεί μια μαζική αλλαγή (π.χ. αλλαγή σελίδας) χωρίς ενδιάμεσα repaints και ζωγραφίζει ΜΙΑ φορά στο τέλος.</summary>
        public static void BatchRedraw(Control c, Action work)
        {
            if (c == null || !c.IsHandleCreated) { work(); return; }
            SendMessage(c.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try { work(); }
            finally
            {
                SendMessage(c.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                c.Invalidate(true);
            }
        }
    }
}
