using OpsisContainers;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OpsisContainers
{
    public partial class App : Application
    {
        private TrayManager _trayManager;
        private TargetChecker _targetChecker;
        private static bool _desktopIsShown = false;
        private static Mutex _mutex;
        private const string UNIQUE_APP_NAME = "Global\\OpsisContainers_Mutex_UniqueId_v2";



        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (e.Args.Any(a => a.Equals("--unhide-desktop", StringComparison.OrdinalIgnoreCase)))
            {
                DesktopOriginals.UnhideAllStandalone();     // used by the uninstaller
                Shutdown();
                return;
            }
            Exit += (s, ev) => DesktopOriginals.ShowAll();     // a normal exit gives the desktop icons back

            OpsisTheme.Init();
            // Opsis integration: exit together with the Opsis process that started this engine.
            if (int.TryParse(Environment.GetEnvironmentVariable("OPSIS_PARENT_PID"), out int parentPid))
            {
                var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                watch.Tick += (_, _) =>
                {
                    try { System.Diagnostics.Process.GetProcessById(parentPid).Dispose(); }
                    catch (ArgumentException) { Environment.Exit(0); }
                };
                watch.Start();
            }

            LegacyNames.MigrateFolder(DataRoot.Dir);     // data of earlier builds: fences.json -> containers.json, "Fence" keys -> "Container"
            LegacyNames.CleanRegistry();

            // --- 1. INITIALIZE PROFILES & SETTINGS FIRST ---
            // This ensures we know the user's true DisableSingleInstance preference immediately
            try
            {
                ProfileManager.Initialize();
                System.IO.Directory.SetCurrentDirectory(ProfileManager.CurrentProfileDir);
                SettingsManager.LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Profile Initialization Error: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            int origTest = Array.FindIndex(e.Args, a => a.Equals("--selftest-originals", StringComparison.OrdinalIgnoreCase));
            if (origTest >= 0 && origTest + 1 < e.Args.Length)
            {
                Environment.ExitCode = OriginalsSelfTest.Run(e.Args[origTest + 1]);
                Shutdown(Environment.ExitCode);
                return;
            }

            int selfTest = Array.FindIndex(e.Args, a => a.Equals("--selftest-settings", StringComparison.OrdinalIgnoreCase));
            if (selfTest >= 0 && selfTest + 1 < e.Args.Length)
            {
                Environment.ExitCode = SettingsMenuSelfTest.Run(e.Args[selfTest + 1]);
                Shutdown(Environment.ExitCode);
                return;
            }

            // --- 2. SINGLE INSTANCE PROTECTION START ---
            bool isNewInstance;
            // Opsis: an isolated data folder (automated tests) gets its own mutex so it never collides with the user's real engine.
            string isolated = Environment.GetEnvironmentVariable("OPSIS_APPDATA") ?? "";
            _mutex = new Mutex(true, isolated.Length == 0 ? UNIQUE_APP_NAME : UNIQUE_APP_NAME + "_" + (uint)isolated.ToLowerInvariant().GetHashCode(), out isNewInstance);

            // Only exit if it's not a new instance AND the user hasn't explicitly disabled the check
            if (!isNewInstance && !SettingsManager.DisableSingleInstance)
            {
                // --- DEBUGGING GHOSTS START ---
                try
                {
                    string debugLog = $"[{DateTime.Now}] Instance 2 Started.\n";
                    debugLog += $"Args (e.Args): {string.Join(" | ", e.Args)}\n";
                    debugLog += $"Args (Environment): {string.Join(" | ", Environment.GetCommandLineArgs())}\n";

                    bool isDrawCommand = e.Args.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0)
                                         || Environment.GetCommandLineArgs().Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isDrawCommand)
                    {
                        RegistryHelper.WriteTrigger($"CMD_DRAW|{Guid.NewGuid()}");
                    }
                    else
                    {
                        RegistryHelper.WriteTrigger(null);
                    }
                }
                catch { }
                // --- DEBUGGING GHOSTS END ---

                Shutdown();
                return;
            }
            // --- SINGLE INSTANCE PROTECTION END ---

            try
            {
                // --- NEW: Sanitize Registry on Startup ---
                RegistryHelper.DeleteTrigger();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"Startup: Working Directory set to {ProfileManager.CurrentProfileDir}");

                // 3. Continue Normal Startup
                {
                    // Initialize settings (Now loads from Profile/options.json)
                    SettingsManager.LoadSettings();

                    // --- NEW: Self-Heal Context Menu Path ---
                    // Ensures the registry key points to the current EXE location
                    RegistryHelper.RefreshContextMenuPath();

                    // Initialize InterCore system
                    InterCore.Initialize();

                    // --- CHAMELEON ENGINE ---
                    WallpaperColorManager.Initialize();
                    // --- AUTO-ORGANIZE ENGINE ---
                    AutoOrganizeManager.Initialize();
                    WallpaperColorManager.WallpaperColorChanged += (s, ev) =>
                    {
                        // Magically update all visuals live when Windows wallpaper changes
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Utility.UpdateContainerVisuals();
                        }));
                    };

                    // Initialize TrayManager BEFORE containers
                    _trayManager = new TrayManager();
                    _trayManager.InitializeTray();

         

                    // Initialize TargetChecker
                    _targetChecker = new TargetChecker(1000);
                    _targetChecker.Start();

                    // Start the Background Icon Loader Engine
                    LazyIconLoader.Start();

                    // Load containers (Now loads from Profile/containers.json)
                    ContainerManager.LoadAndCreateContainers(_targetChecker);
                    OpsisBridge.Start();
                    DesktopOriginals.HideAll();

                    // --- PRODUCTION START LOGIC ---
                    if (SettingsManager.EnableProfileAutomation)
                    {
                        AutomationManager.Start();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Startup: Profile Automation Engine ignited.");
                    }

                    // Ensure UI reflects the current state of profiles and automation
                    _trayManager.UpdateProfilesMenu();
                    _trayManager.UpdateTrayIcon();
                    _trayManager.UpdateHiddenContainersMenu();

                    // Initialize global hotkey monitoring
                    try
                    {
                        GlobalHotkeyManager.WindowsPlusDDetected += OnWindowsPlusDDetected;
                        GlobalHotkeyManager.StartMonitoring();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                            "GlobalHotkeyManager: Successfully initialized hotkey monitoring");
                    }
                    catch (System.Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                            $"GlobalHotkeyManager: Failed to initialize: {ex.Message}");
                    }



                    // --- NEW: Start Desktop Double-Click Listener ---
                    try
                    {
                        DesktopMouseHook.Start();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "DesktopMouseHook started.");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to start DesktopMouseHook: {ex.Message}");
                    }



                    // --- NEW: Direct Draw Mode Check ---
                    // If this MAIN instance was started via Context Menu, trigger draw mode now.
                    // Use the same robust check as above.
                    var allArgs = Environment.GetCommandLineArgs();
                    bool isDrawStartup = e.Args.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0)
                                         || allArgs.Any(arg => arg.IndexOf("-create", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isDrawStartup)
                    {
                        // Wait 500ms for UI to settle, then draw
                        Task.Delay(500).ContinueWith(t => Dispatcher.Invoke(() => ContainerManager.StartDrawMode()));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical Startup Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            InterCore.Cleanup();
            try
            {
                GlobalHotkeyManager.StopMonitoring();
            }
            catch { }

            // --- NEW: Stop Desktop Double-Click Listener ---
            try
            {
                DesktopMouseHook.Stop();
            }
            catch { }

            _trayManager?.Dispose();
            base.OnExit(e);
        }

        private static void OnWindowsPlusDDetected(object sender, System.EventArgs e)
        {
            try
            {
                _desktopIsShown = !_desktopIsShown;
                if (_desktopIsShown)
                {
                    var restoreTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(800)
                    };
                    restoreTimer.Tick += (timerSender, timerArgs) =>
                    {
                        restoreTimer.Stop();
                        RestoreAllContainerWindows();
                    };
                    restoreTimer.Start();
                }

                var resetTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(10)
                };
                resetTimer.Tick += (timerSender, timerArgs) =>
                {
                    resetTimer.Stop();
                    _desktopIsShown = false;
                };
                resetTimer.Start();
            }
            catch { }
        }

        private static void RestoreAllContainerWindows()
        {
            try
            {
                var containerWindows = System.Windows.Application.Current.Windows
                    .OfType<NonActivatingWindow>()
                    .ToList();

                foreach (var containerWindow in containerWindows)
                {
                    try
                    {
                        if (containerWindow.WindowState == WindowState.Minimized)
                            containerWindow.WindowState = WindowState.Normal;

                        if (!containerWindow.IsVisible)
                            containerWindow.Show();

                        containerWindow.Topmost = true;
                        containerWindow.Topmost = false;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}