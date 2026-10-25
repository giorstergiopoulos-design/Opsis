using System.Runtime.InteropServices;

namespace MotionDesk.Widgets
{
    /// <summary>
    /// "Heavy pause" for the WebView2 runtime: suspends the whole browser process tree (browser, GPU, renderers) with NtSuspendProcess,
    /// so a paused wallpaper costs 0% CPU/GPU and no wake-ups at all (the JS-level pause and TrySuspend still leave a few percent).
    /// Thaw() is idempotent, runs before anything talks to the WebView again and also at process exit. WebView2 processes die
    /// together with their host, so a crash of Opsis can never leave them frozen.
    /// </summary>
    internal static class ProcessFreezer
    {
        private static readonly List<IntPtr> _frozen = new();
        private static DateTime _thawedAt = DateTime.MinValue;
        private static bool _hooked;

        public static bool IsFrozen { get { lock (_frozen) return _frozen.Count > 0; } }
        /// <summary>True while frozen and for a few seconds after thawing (unresponsive-events of a just-thawed renderer are not crashes).</summary>
        public static bool IsFrozenOrRecent => IsFrozen || (DateTime.UtcNow - _thawedAt).TotalSeconds < 10;

        public static void Freeze(int rootPid)
        {
            if (rootPid <= 0) return;
            int count;
            lock (_frozen)
            {
                if (_frozen.Count > 0) return;
                if (!_hooked) { AppDomain.CurrentDomain.ProcessExit += (_, _) => Thaw(); _hooked = true; }
                foreach (int pid in Tree(rootPid))
                {
                    if (pid == Environment.ProcessId) continue;
                    IntPtr h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
                    if (h == IntPtr.Zero) continue;
                    if (NtSuspendProcess(h) == 0) _frozen.Add(h); else CloseHandle(h);
                }
                count = _frozen.Count;
            }
            MotionDesk.Services.AttachLog.Write($"heavy pause: froze {count} WebView2 process(es)");
        }

        public static void Thaw()
        {
            lock (_frozen)
            {
                if (_frozen.Count == 0) return;
                foreach (var h in _frozen) { try { NtResumeProcess(h); } catch (Exception) { } CloseHandle(h); }
                _frozen.Clear();
                _thawedAt = DateTime.UtcNow;
            }
            MotionDesk.Services.AttachLog.Write("heavy pause: thawed");
        }

        private static List<int> Tree(int rootPid)
        {
            var parents = new Dictionary<int, List<int>>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap != IntPtr.Zero && snap != (IntPtr)(-1))
            {
                try
                {
                    var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                    for (bool ok = Process32First(snap, ref e); ok; ok = Process32Next(snap, ref e))
                    {
                        int parent = (int)e.th32ParentProcessID;
                        if (!parents.TryGetValue(parent, out var l)) parents[parent] = l = new List<int>();
                        l.Add((int)e.th32ProcessID);
                    }
                }
                finally { CloseHandle(snap); }
            }
            var result = new List<int> { rootPid };
            for (int i = 0; i < result.Count; i++)
                if (parents.TryGetValue(result[i], out var kids)) foreach (int k in kids) if (!result.Contains(k)) result.Add(k);
            return result;
        }

        private const uint PROCESS_SUSPEND_RESUME = 0x0800, TH32CS_SNAPPROCESS = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr h);
    }
}
