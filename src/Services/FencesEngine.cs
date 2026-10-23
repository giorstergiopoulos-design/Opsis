using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    // DeskContainers engine: the genuine DesktopFences code (MIT, see THIRD_PARTY_NOTICES.md) built as the separate WPF
    // project fences\OpsisFences.csproj and shipped in the "fences" subfolder. Opsis itself stays WinForms and only
    // starts/stops that process; its data lives in %AppData%\Opsis\fences (OPSIS_APPDATA is inherited by the child).
    internal static class FencesEngine
    {
        private const string ProcessName = "OpsisFences";
        private static bool _exitHooked;

        public static string ExePath => Path.Combine(AppContext.BaseDirectory, "fences", ProcessName + ".exe");
        public static bool IsAvailable => File.Exists(ExePath);
        public static bool IsRunning => Process.GetProcessesByName(ProcessName).Length > 0;

        /// <summary>True when the Fences engine replaces the legacy DeskContainers (setting on and the exe is present).</summary>
        public static bool Enabled => AppSettings.Load().UseFencesEngine && IsAvailable;

        public static void Start()
        {
            if (!IsAvailable || IsRunning) return;
            if (!_exitHooked) { _exitHooked = true; Application.ApplicationExit += (_, _) => Stop(); }
            try
            {
                var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExePath)! };
                psi.Environment["OPSIS_PARENT_PID"] = Environment.ProcessId.ToString();
                Process.Start(psi);
            }
            catch (Exception ex) { RotatingLogger.Write($"Fences engine failed to start: {ex.Message}"); }
        }

        public static void Stop()
        {
            foreach (var p in Process.GetProcessesByName(ProcessName))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch (Exception) { }
                finally { p.Dispose(); }
            }
        }
    }
}
