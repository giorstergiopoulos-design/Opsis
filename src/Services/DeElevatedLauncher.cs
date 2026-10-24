using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace MotionDesk.Services
{
    /// <summary>
    /// Starts a program with the normal (non-elevated) token of the desktop user even when Opsis itself runs elevated.
    /// The DeskContainers engine needs this: Windows (UIPI) silently blocks drag &amp; drop from Explorer into a window of an elevated
    /// process, so an engine that inherited Opsis' elevation would never receive dropped files.
    /// </summary>
    internal static class DeElevatedLauncher
    {
        public static bool IsElevated()
        {
            try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { return false; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb; public string? lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attrs, int impersonation, int type, out IntPtr newToken);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string? app, string commandLine, int creationFlags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008, TOKEN_ASSIGN_PRIMARY = 0x0001, TOKEN_ALL_ACCESS_FOR_START = 0xF01FF;
        private const int CREATE_UNICODE_ENVIRONMENT = 0x400;

        /// <summary>Returns the new process or null when the shell token could not be used (the caller then falls back to a normal start).</summary>
        public static Process? Start(string exePath, string workingDir, IDictionary extraEnvironment)
        {
            IntPtr hProc = IntPtr.Zero, hTok = IntPtr.Zero, hDup = IntPtr.Zero, env = IntPtr.Zero;
            try
            {
                var shell = Process.GetProcessesByName("explorer");
                if (shell.Length == 0) return null;
                hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, shell[0].Id);
                if (hProc == IntPtr.Zero) return null;
                if (!OpenProcessToken(hProc, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY, out hTok)) return null;
                if (!DuplicateTokenEx(hTok, TOKEN_ALL_ACCESS_FOR_START, IntPtr.Zero, 2 /*SecurityImpersonation*/, 1 /*TokenPrimary*/, out hDup)) return null;

                // environment block = this process' variables + the overrides (the engine reads OPSIS_* settings from it)
                var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) vars[(string)e.Key] = (string?)e.Value ?? "";
                foreach (DictionaryEntry e in extraEnvironment) vars[(string)e.Key] = (string?)e.Value ?? "";
                var sb = new StringBuilder();
                foreach (var kv in vars) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
                sb.Append('\0');
                env = Marshal.StringToHGlobalUni(sb.ToString());

                var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = "winsta0\\default" };
                if (!CreateProcessWithTokenW(hDup, 0, null, "\"" + exePath + "\"", CREATE_UNICODE_ENVIRONMENT, env, workingDir, ref si, out var pi)) return null;
                CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
                return Process.GetProcessById(pi.dwProcessId);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException) { return null; }
            finally
            {
                if (env != IntPtr.Zero) Marshal.FreeHGlobal(env);
                if (hDup != IntPtr.Zero) CloseHandle(hDup);
                if (hTok != IntPtr.Zero) CloseHandle(hTok);
                if (hProc != IntPtr.Zero) CloseHandle(hProc);
            }
        }
    }
}
