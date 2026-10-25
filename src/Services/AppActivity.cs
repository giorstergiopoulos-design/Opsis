using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MotionDesk.Services;

/// <summary>
/// Κεντρική γνώση του αν το desktop πρέπει να "ησυχάσει": εφαρμογή πλήρους οθόνης (παιχνίδι, video player),
/// κλειδωμένη συνεδρία, απομακρυσμένη (RDP) συνεδρία ή Battery Saver. Πριν, μόνο το wallpaper σταματούσε σε
/// fullscreen — όλα τα widgets (ρολόι, system monitor, audio visualizer στα 25Hz) συνέχιζαν να τρέχουν timers και
/// να ζωγραφίζουν κάτω από το παιχνίδι, κλέβοντας CPU/GPU ακριβώς όταν χρειάζεται όλη η ισχύς. Τώρα υπάρχει ΕΝΑ
/// shared state: τα widgets ελέγχουν <see cref="IsFullscreenAppActive"/> (= οποιοσδήποτε λόγος παύσης, το όνομα
/// μένει για συμβατότητα) και το wallpaper εγγράφεται στο <see cref="FullscreenChanged"/> και ρωτά
/// <see cref="IsPausedFor"/> ανά οθόνη. Πρέπει να δημιουργηθεί/ξεκινήσει από το UI thread.
/// Fullscreen: 1s polling, 2 συνεχόμενα θετικά δείγματα για είσοδο (όχι αναλαμπές), άμεση έξοδος.
/// </summary>
public static class AppActivity
{
    private static System.Windows.Forms.Timer? _timer;
    private static SynchronizationContext? _ui;
    private static int _fullscreenHits;
    private static int _slowTick;
    private static bool _fullscreen, _locked, _remote, _batterySaver, _screensaver, _highLoad, _awake;
    private static int _loadHits;
    private static string? _fullscreenDevice;

    /// <summary>True όταν υπάρχει οποιοσδήποτε λόγος παύσης (fullscreen, lock, RDP, battery saver).</summary>
    public static bool IsFullscreenAppActive { get; private set; }
    public static event Action<bool>? FullscreenChanged;

    public static string Describe() =>
        $"fullscreen={_fullscreen}({_fullscreenDevice ?? "-"}) locked={_locked} remote={_remote} batterySaver={_batterySaver} screensaver={_screensaver} highLoad={_highLoad}";

    /// <summary>Αν το rendering μιας συγκεκριμένης οθόνης πρέπει να είναι σταματημένο. Fullscreen σε άλλη οθόνη ΔΕΝ παύει αυτή.</summary>
    public static bool IsPausedFor(Screen? screen)
    {
        if (_locked || _remote || _batterySaver || _screensaver || _highLoad) return true;
        if (!_fullscreen) return false;
        return _fullscreenDevice == null || screen == null || string.Equals(_fullscreenDevice, screen.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    public static void Start()
    {
        if (_timer != null) return;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // SystemEvents καλεί από δικό του thread -> Post στο UI thread.
        _ui?.Post(_ =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) _locked = AppSettings.Load().PauseOnLock;
            else if (e.Reason == SessionSwitchReason.SessionUnlock) _locked = false;
            Publish();
        }, null);
    }

    private static void Poll()
    {
        try
        {
            var settings = AppSettings.Load();
            bool fsNow = IsForegroundWindowFullscreen(out string? device);
            _fullscreenHits = fsNow ? _fullscreenHits + 1 : 0;
            // είσοδος μετά από 2 συνεχόμενα δείγματα, έξοδος αμέσως (ή αν άλλαξε οθόνη το ίδιο fullscreen)
            bool fullscreen = _fullscreenHits >= 2 || (_fullscreen && fsNow);
            if (fullscreen) _fullscreenDevice = device;

            // Τα φθηνά-αλλά-όχι-δωρεάν checks (RDP, power) κάθε 5 δευτερόλεπτα.
            if (_slowTick++ % 5 == 0)
            {
                _remote = settings.PauseOnRemoteSession && GetSystemMetrics(SM_REMOTESESSION) != 0;
                _batterySaver = settings.PauseOnBatterySaver && IsBatterySaverOn();
                _screensaver = settings.PauseOnScreensaver && IsScreensaverRunning();
                UpdateLoad(settings);
            }
            if (!settings.PauseOnLock) _locked = false;
            _fullscreen = fullscreen;
            Publish();
            UpdateKeepAwake(settings);
        }
        catch (Exception ex) { RotatingLogger.Write("AppActivity: " + ex.Message); }
    }

    private static bool _lastPublished;
    private static string? _lastDevice;
    private static bool _lastLocked, _lastRemote, _lastSaver, _lastScreensaver, _lastLoad;

    private static void Publish()
    {
        bool now = _fullscreen || _locked || _remote || _batterySaver || _screensaver || _highLoad;
        bool changed = now != _lastPublished || _fullscreenDevice != _lastDevice
                       || _locked != _lastLocked || _remote != _lastRemote || _batterySaver != _lastSaver
                       || _screensaver != _lastScreensaver || _highLoad != _lastLoad;
        if (!changed) return;
        _lastPublished = now; _lastDevice = _fullscreenDevice;
        _lastLocked = _locked; _lastRemote = _remote; _lastSaver = _batterySaver; _lastScreensaver = _screensaver; _lastLoad = _highLoad;
        IsFullscreenAppActive = now;
        FullscreenChanged?.Invoke(now);
    }

    // CPU / GPU / memory threshold with hysteresis: in after two checks (10 s) above the limit, out when 10 points below it.
    private static void UpdateLoad(AppSettings s)
    {
        if (s.PauseCpuAbove <= 0 && s.PauseGpuAbove <= 0 && s.PauseRamAbove <= 0) { _highLoad = false; _loadHits = 0; return; }
        var m = SystemMonitorService.Instance.GetSnapshot();
        double ram = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100 : 0;
        double gpu = s.PauseGpuAbove > 0 ? GpuMonitorService.Instance.GetSnapshot().LoadPercent ?? 0 : 0;
        int margin = _highLoad ? 10 : 0;
        bool over = (s.PauseCpuAbove > 0 && m.CpuPercent > s.PauseCpuAbove - margin)
                 || (s.PauseGpuAbove > 0 && gpu > s.PauseGpuAbove - margin)
                 || (s.PauseRamAbove > 0 && ram > s.PauseRamAbove - margin);
        if (over) { if (++_loadHits >= 2) _highLoad = true; }
        else { _loadHits = 0; _highLoad = false; }
    }

    // Keeps the PC and the display awake while the wallpaper is running and not paused. Called from the UI-thread timer only
    // (SetThreadExecutionState belongs to the calling thread).
    private static void UpdateKeepAwake(AppSettings s)
    {
        bool want = s.KeepPcAwake && !IsFullscreenAppActive && MotionDesk.Widgets.WallpaperHostEngine.Instance.IsRunning;
        if (want == _awake) return;
        _awake = want;
        SetThreadExecutionState(want ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED) : ES_CONTINUOUS);
    }

    private static bool IsScreensaverRunning() => SystemParametersInfo(SPI_GETSCREENSAVERRUNNING, 0, out int running, 0) && running != 0;

    private static bool IsBatterySaverOn()
    {
        return GetSystemPowerStatus(out var s) && (s.SystemStatusFlag & 1) != 0;
    }

    private static bool IsForegroundWindowFullscreen(out string? device)
    {
        device = null;
        // Exclusive D3D fullscreen / παρουσίαση: ο shell το ξέρει με ακρίβεια (το rect-test δεν πιάνει exclusive mode σε κάποιες περιπτώσεις).
        bool shellSaysFullscreen = SHQueryUserNotificationState(out int qun) == 0 && (qun == 3 || qun == 4);

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return shellSaysFullscreen;
        if (!GetWindowRect(hwnd, out var rect)) return shellSaysFullscreen;

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        string cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        var screen = Screen.FromRectangle(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
        var b = screen.Bounds;
        bool covers = rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
        if (covers || shellSaysFullscreen) device = screen.DeviceName;
        return covers || shellSaysFullscreen;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    private const int SM_REMOTESESSION = 0x1000, SPI_GETSCREENSAVERRUNNING = 0x0072;
    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int uiAction, int uiParam, out int pvParam, int fWinIni);
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint esFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int pquns);
}
