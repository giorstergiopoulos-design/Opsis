using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // "DeskCursors" — διαχειριστής δείκτη ποντικιού μέσω του επίσημου registry μηχανισμού των
    // ίδιων των Windows (HKCU\Control Panel\Cursors + SystemParametersInfo), όχι τρίτου κώδικα.
    // Οι ρόλοι/ονόματα εδώ επαληθεύτηκαν έναντι πραγματικού registry state στη μηχανή-στόχο πριν
    // γραφτούν (Arrow/IBeam/Hand/Wait/AppStarting/No — οι πιο συχνά αλλαγμένοι ρόλοι σε ένα scheme).
    internal static class DeskCursorsEngine
    {
        public static readonly (string RegistryName, string LabelKey)[] Roles =
        {
            ("Arrow", "Cursors.RoleArrow"),
            ("IBeam", "Cursors.RoleIBeam"),
            ("Hand", "Cursors.RoleHand"),
            ("Wait", "Cursors.RoleWait"),
            ("AppStarting", "Cursors.RoleAppStarting"),
            ("No", "Cursors.RoleNo"),
        };

        private const string CursorsKeyPath = @"Control Panel\Cursors";

        public static string? GetCursor(string registryName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(CursorsKeyPath);
            var v = key?.GetValue(registryName) as string;
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public static void SetCursor(string registryName, string? path)
        {
            using var key = Registry.CurrentUser.CreateSubKey(CursorsKeyPath);
            if (key == null) return;
            if (string.IsNullOrEmpty(path)) key.DeleteValue(registryName, false);
            else key.SetValue(registryName, path, RegistryValueKind.ExpandString);
        }

        public static void ApplyNow(string schemeName = "Opsis Custom")
        {
            using (var key = Registry.CurrentUser.OpenSubKey(CursorsKeyPath, true))
                key?.SetValue(string.Empty, schemeName, RegistryValueKind.String);
            // SPIF_SENDCHANGE broadcasts WM_SETTINGCHANGE and waits for EVERY top-level window: one hung program froze the Personalization page (and Opsis) for seconds.
            // The registry is already written; the system call runs on a worker thread.
            System.Threading.Tasks.Task.Run(() => { try { SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE); } catch (Exception) { } });
        }

        public static void ResetAllToWindowsDefault()
        {
            foreach (var (regName, _) in Roles) SetCursor(regName, null);
            using (var key = Registry.CurrentUser.OpenSubKey(CursorsKeyPath, true))
                key?.SetValue(string.Empty, "Windows Default", RegistryValueKind.String);
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_SENDCHANGE = 0x02;
    }
}
