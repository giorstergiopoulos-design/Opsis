using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MotionDesk.Widgets
{
    /// <summary>
    /// "Application" wallpaper: starts a program (a game engine scene, a screensaver-like exe, any program with a window) and shows its window
    /// as the wallpaper, behind the desktop icons, inside the wallpaper window of the primary screen. The program is started and ended by Opsis
    /// (it is closed again when the mode changes, the wallpaper is disabled or Opsis exits) and suspended while the wallpaper is paused.
    /// </summary>
    internal static class ApplicationWallpaper
    {
        private static Process? _process;
        private static string _path = "";
        private static IntPtr _hwnd;
        private static System.Windows.Forms.Timer? _wait;
        private static bool _suspended, _hooked;

        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000, WS_POPUP = unchecked((int)0x80000000), WS_CHILD = 0x40000000;
        private const long WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;

        public static bool Running => _process is { HasExited: false };

        /// <summary>Makes the running program match the wanted state: started and embedded in <paramref name="host"/>, or closed when <paramref name="path"/> is empty.</summary>
        public static void Sync(Form host, string path, string args, bool paused)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { Stop(); return; }
            if (MotionDesk.Services.SafeLaunch.Reject(path, requireExe: true) is { } why) { MotionDesk.Services.AttachLog.Write("application wallpaper refused: " + why); Stop(); return; }
            if (Running && _path == path)
            {
                SetPaused(paused);
                if (_hwnd != IntPtr.Zero) Fit(host);
                return;
            }
            Stop();
            if (!_hooked) { AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop(); _hooked = true; }
            try
            {
                var psi = new ProcessStartInfo(path, args ?? "") { WorkingDirectory = Path.GetDirectoryName(path) ?? "", UseShellExecute = false };
                _process = Process.Start(psi);
                _path = path;
            }
            catch (Exception ex) { MotionDesk.Services.AttachLog.Write("application wallpaper failed to start: " + ex.Message); _process = null; return; }

            int tries = 0;
            _wait = new System.Windows.Forms.Timer { Interval = 500 };
            _wait.Tick += (_, _) =>
            {
                tries++;
                try
                {
                    if (_process == null || _process.HasExited) { StopWait(); return; }
                    _process.Refresh();
                    IntPtr h = _process.MainWindowHandle;
                    if (h == IntPtr.Zero) { if (tries > 40) StopWait(); return; }       // up to 20 s for the window to appear
                    StopWait();
                    Embed(host, h);
                    SetPaused(paused);
                }
                catch (Exception ex) { MotionDesk.Services.AttachLog.Write("application wallpaper embed failed: " + ex.Message); StopWait(); }
            };
            _wait.Start();
        }

        private static void Embed(Form host, IntPtr h)
        {
            _hwnd = h;
            long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_POPUP);
            style |= WS_CHILD;
            SetWindowLongPtr(h, GWL_STYLE, (IntPtr)style);
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(h, GWL_EXSTYLE, (IntPtr)((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
            SetParent(h, host.Handle);
            Fit(host);
            ShowWindow(h, 5);
            MotionDesk.Services.AttachLog.Write($"application wallpaper embedded: {Path.GetFileName(_path)}");
        }

        private static void Fit(Form host)
        {
            if (_hwnd == IntPtr.Zero) return;
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, host.ClientSize.Width, host.ClientSize.Height, 0x0004 | 0x0010 | 0x0040);   // NOZORDER | NOACTIVATE | SHOWWINDOW
        }

        public static void SetPaused(bool paused)
        {
            if (_process == null || _process.HasExited || paused == _suspended) return;
            try
            {
                IntPtr h = OpenProcess(0x0800, false, _process.Id);
                if (h == IntPtr.Zero) return;
                if (paused) NtSuspendProcess(h); else NtResumeProcess(h);
                CloseHandle(h);
                _suspended = paused;
            }
            catch (Exception) { }
        }

        public static void Stop()
        {
            StopWait();
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    SetPaused(false);
                    if (!_process.CloseMainWindow() || !_process.WaitForExit(1500)) _process.Kill(true);
                }
            }
            catch (Exception) { }
            _process?.Dispose(); _process = null; _hwnd = IntPtr.Zero; _path = ""; _suspended = false;
        }

        private static void StopWait() { _wait?.Stop(); _wait?.Dispose(); _wait = null; }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr h);
    }
}
