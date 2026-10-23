using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon = null!;
        private MainWindow? _mainWindow;
        private readonly HotkeyManager _hotkeys = new();
        private readonly ToolStripMenuItem _wallpaperToggleItem;
        private bool _wallpaperEnabled;
        // E2 (v2.0 handoff PHASE0_REPORT): η μετάφραση της taskbar (GlassBlur.SetTaskbarTranslucency)
        // εφαρμοζόταν ΜΟΝΟ μία φορά στο Program.cs, στην εκκίνηση του Opsis — ποτέ ξανά αν η ίδια η
        // Explorer επανεκκινήσει (crash, "Restart Explorer" από Task Manager, Windows theme service).
        // Ξεχωριστό instance από αυτό που ήδη χρησιμοποιεί το wallpaper (WallpaperWindow) — αυτό εδώ
        // πρέπει να ζει όσο ζει η ίδια η εφαρμογή, ανεξάρτητα από το αν το wallpaper είναι ενεργό.
        private readonly ExplorerRestartWatcher _taskbarWatcher = new();

        public static event Action? ExitRequested;
        public static void RequestExit() => ExitRequested?.Invoke();

        public TrayApplicationContext()
        {
            // Safe mode (v2.0 handoff, Φάση 1 §S): αν ενεργό, παραλείπονται plugins/wallpaper/
            // "Last Session" παρακάτω — μόνο το βασικό UI. Ο μετρητής crash μηδενίζεται μετά από
            // 30s επιτυχούς λειτουργίας (one-shot timer πιο κάτω), όχι αμέσως, ώστε ένα crash λίγο
            // μετά την εκκίνηση να εξακολουθεί να μετράει.
            bool safeMode = SafeModeService.IsSafeMode;
            if (safeMode)
            {
                RotatingLogger.Write("Starting in SAFE MODE - plugins/wallpaper/last-session skipped.");
                new SafeModeBanner().Show();
            }
            var healthyTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            healthyTimer.Tick += (_, _) => { SafeModeService.MarkStartupHealthy(); healthyTimer.Stop(); healthyTimer.Dispose(); };
            healthyTimer.Start();

            // Plugin (π.χ. News Crawler): ξεκινούν πριν χτιστεί το μενού ώστε να προσθέσουν τα δικά τους στοιχεία.
            PluginHost.Instance.NavigationRequested += key => ShowMainWindow("plugin:" + key);
            if (!safeMode) PluginHost.Instance.StartAll();

            // Δημιουργία Context Menu για το Tray Icon — ομαδοποιημένο σε: παράθυρο, DeskZones,
            // wallpaper, γρήγορα προφίλ, γρήγορες ρυθμίσεις, έξοδος.
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(LocalizationManager.T("Tray.Open"), null, (s, e) => ShowMainWindow());
            contextMenu.Items.Add(LocalizationManager.T("Tray.CommandPalette"), null, (s, e) => { ShowMainWindow(); _mainWindow?.OpenCommandPalette(); });
            contextMenu.Items.Add(LocalizationManager.T("Tray.Settings"), null, (s, e) => ShowMainWindow("Settings"));
            contextMenu.Items.Add(new ToolStripSeparator());

            // Τα "Work"/"Gaming"/"Focus" παραμένουν ως έχουν σε κάθε γλώσσα — είναι τα ίδια τα
            // ονόματα των προεπιλεγμένων προφίλ (ταυτίζονται με το string ID που περνάει στο
            // ApplyProfile/WorkspaceProfileService), όχι γενικό κείμενο UI προς μετάφραση.
            var profilesMenu = new ToolStripMenuItem(LocalizationManager.T("Tray.QuickProfile"));
            profilesMenu.DropDownItems.Add("Work", null, (s, e) => ApplyProfile("Work"));
            profilesMenu.DropDownItems.Add("Gaming", null, (s, e) => ApplyProfile("Gaming"));
            profilesMenu.DropDownItems.Add("Focus", null, (s, e) => ApplyProfile("Focus"));
            contextMenu.Items.Add(profilesMenu);
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add(LocalizationManager.T("Tray.DeskZonesEdit"), null, (s, e) => AddDeskZone());
            contextMenu.Items.Add(new ToolStripSeparator());

            _wallpaperToggleItem = new ToolStripMenuItem(LocalizationManager.T("Tray.WallpaperToggle"));
            _wallpaperToggleItem.Click += (s, e) => ToggleWallpaper();
            contextMenu.Items.Add(_wallpaperToggleItem);
            contextMenu.Items.Add(LocalizationManager.T("Tray.WallpaperAddVideo"), null, (s, e) => ChooseWallpaperVideo());
            contextMenu.Items.Add(LocalizationManager.T("Tray.WallpaperAddFolder"), null, (s, e) => ChooseWallpaperFolder());
            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add("DeskFlip  (Ctrl+Alt+F)", null, (s, e) => DeskFlipEngine.Show());

            ToolStripMenuItem gridSnapItem = new ToolStripMenuItem(LocalizationManager.T("Tray.GridSnap"));
            gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            gridSnapItem.Click += (s, e) => {
                WidgetSnapEngine.EnableGridSnap = !gridSnapItem.Checked;
                gridSnapItem.Checked = WidgetSnapEngine.EnableGridSnap;
            };
            contextMenu.Items.Add(gridSnapItem);

            ToolStripMenuItem startupItem = new ToolStripMenuItem(LocalizationManager.T("Tray.Startup"));
            startupItem.Checked = StartupManager.IsStartupEnabled();
            startupItem.Click += (s, e) => {
                bool newState = !startupItem.Checked;
                StartupManager.SetStartup(newState);
                startupItem.Checked = newState;
            };
            contextMenu.Items.Add(startupItem);

            foreach (var pluginMenu in PluginHost.Instance.GetTrayMenuItems())
            {
                if (pluginMenu.IsSeparator) { contextMenu.Items.Add(new ToolStripSeparator()); continue; }
                var invoke = pluginMenu.Invoke;
                contextMenu.Items.Add(LocalizationManager.CurrentLanguage == "el" ? pluginMenu.TitleEl : pluginMenu.TitleEn, null, (s, e) => { try { invoke(); } catch (Exception) { } });
            }

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(LocalizationManager.T("Common.Exit"), null, (s, e) => PerformExit());
            // Επιτρέπει σε ΟΠΟΙΟΔΗΠΟΤΕ άλλο κομμάτι της εφαρμογής (π.χ. το κουμπί "Λήψη & εγκατάσταση"
            // στη σελίδα About) να ζητήσει τον ΙΔΙΟ, πλήρη καθαρισμό εξόδου αντί για ένα γυμνό
            // Application.Exit() που θα παρέκαμπτε το save session/plugin stop/hotkey dispose — ίδιο
            // μοτίβο με το host.RequestExit()/ExitRequested του ήδη δουλεμένου OpsisNewsCrawler.
            ExitRequested += () => PerformExit();

            // Αρχικοποίηση NotifyIcon — στατικό εικονίδιο (η "ζωντανή" λάμψη μετακόμισε στο
            // λογότυπο του sidebar μέσα στο κύριο παράθυρο, όχι πλέον στο tray).
            _trayIcon = new NotifyIcon()
            {
                Icon = LoadTrayIcon(),
                ContextMenuStrip = contextMenu,
                Text = "Opsis",
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();

            // ---- high-usage alerts + update check (GearWin-style self-update, βλ. AutoCheckForUpdatesAsync)
            _uiContext = System.Threading.SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _alertTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _alertTimer.Tick += (_, _) => UsageAlertTick();
            _alertTimer.Start();
            _trayIcon.BalloonTipClicked += (_, _) => OpenPendingUpdate();
            _ = AutoCheckForUpdatesAsync();
            // Σε ΚΑΘΕ εκκίνηση (παραπάνω) ΚΑΙ μετά κάθε ~12 ώρες (ίδια λογική με το self-update του
            // GearWin, ήδη portαρισμένο/δοκιμασμένο στο OpsisNewsCrawler) — αντικαθιστά το
            // προηγούμενο "μία φορά τον μήνα". Το Opsis είναι tray-resident εφαρμογή που συχνά μένει
            // ανοιχτή για πολύ καιρό χωρίς restart, οπότε χρειάζεται ΚΑΙ περιοδικό επαναέλεγχο, όχι
            // μόνο στην εκκίνηση.
            _updateCheckTimer = new System.Windows.Forms.Timer { Interval = (int)UpdateCheckService.AutoCheckInterval.TotalMilliseconds };
            _updateCheckTimer.Tick += (_, _) => { _ = AutoCheckForUpdatesAsync(); };
            _updateCheckTimer.Start();

            _hotkeys.RegisterCtrlAlt('F', () => DeskFlipEngine.Show());
            // v2.2.0 (DesktopFences SpotSearch): Ctrl+` - προαιρετικό (προεπιλογή ΚΛΕΙΣΤΟ) γιατί είναι
            // παγκόσμιο και συγκρούεται με π.χ. το τερματικό του VS Code.
            if (AppSettings.Load().ContainersSpotSearch) _hotkeys.RegisterCtrl(Keys.Oemtilde, () => DeskContainerSpotSearch.Open());

            // Fences-style: διπλό-κλικ σε κενό σημείο της επιφάνειας εργασίας κρύβει/επαναφέρει
            // όλα τα εικονίδια εκτός του Ο Υπολογιστής μου/φάκελος χρήστη/Πίνακας Ελέγχου/Κάδος
            // Ανακύκλωσης — ζητήθηκε ρητά. Τρέχει σε όλη τη διάρκεια ζωής της εφαρμογής, εκτός αν ο
            // χρήστης το απενεργοποίησε ρητά (DeskZones page, "AppSettings.QuickHideDoubleClickEnabled").
            _taskbarWatcher.ExplorerRestarted += () =>
            {
                try { GlassBlur.SetTaskbarTranslucency(WindowsSystemSettings.GetTransparencyEffectsEnabled()); }
                catch (Exception) { } // σιωπηλό fallback — ζητήθηκε ρητά, βλ. E2
            };

            if (!safeMode && AppSettings.Load().QuickHideDoubleClickEnabled) DesktopIconVisibilityEngine.Instance.Start();
            AppActivity.Start();
            if (!safeMode && FencesEngine.Enabled) FencesEngine.Start();

            // Restore lightweight persistent preferences before showing the manager.
            var appSettings = AppSettings.Load();
            WidgetSnapEngine.EnableGridSnap = appSettings.GridSnap;
            WidgetSnapEngine.SnapThreshold = Math.Clamp(appSettings.SnapThreshold, 5, 50);
            WidgetSnapEngine.GridSize = Math.Clamp(appSettings.GridSize, 5, 100);
            _wallpaperEnabled = !safeMode && WallpaperHostEngine.Instance.IsEnabled;

            if (!safeMode)
            {
                // Time-of-day schedule (may switch the wallpaper mode before the wallpaper starts)
                WallpaperHostEngine.Instance.ApplyScheduleNow();

                if (appSettings.StartWallpaperWithWindows)
                {
                    WallpaperHostEngine.Instance.Enable();
                    _wallpaperEnabled = true;
                }

                // The clock/date/temperature overlay keeps working over the normal Windows wallpaper when the animated wallpaper is off
                WallpaperHostEngine.Instance.UpdateOverlayOnly();

                CheckLockScreenFollowsWallpaper();
            }

            // "--background": η αυτόματη εκκίνηση με τα Windows (StartupManager.SetStartup) περνάει
            // αυτό το flag — ζητήθηκε ρητά ότι όταν υπάρχουν αποθηκευμένα widgets/DeskContainers, η
            // εφαρμογή πρέπει να ανοίγει στο παρασκήνιο (μόνο tray icon), όχι με το κύριο παράθυρο
            // διαχείρισης να αναδύεται κάθε φορά. Το MainWindow ΔΗΜΙΟΥΡΓΕΙΤΑΙ κανονικά (ώστε να
            // τρέξει η επαναφορά του "Last Session" — widgets/DeskContainers/wallpaper μέσα στον
            // constructor του), απλά δεν καλείται Show() πάνω του.
            bool launchedInBackground = Environment.GetCommandLineArgs().Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (launchedInBackground)
            {
                _mainWindow = new MainWindow();
                _mainWindow.FormClosed += (s, e) => _mainWindow = null;
            }
            else
            {
                // "--page <key>": ανοίγει κατευθείαν σε μια σελίδα (χρήσιμο για δοκιμές και συντομεύσεις), π.χ. --page plugin:OpsisNewsCrawler
                var cmd = Environment.GetCommandLineArgs();
                int pageArg = Array.FindIndex(cmd, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
                ShowMainWindow(pageArg >= 0 && pageArg + 1 < cmd.Length ? cmd[pageArg + 1] : null);
            }
        }

        private void ShowMainWindow(string? page = null)
        {
            if (_mainWindow == null || _mainWindow.IsDisposed)
            {
                _mainWindow = new MainWindow();
                _mainWindow.FormClosed += (s, e) => _mainWindow = null;
                _mainWindow.Show();
            }
            else
            {
                if (_mainWindow.WindowState == FormWindowState.Minimized)
                    _mainWindow.WindowState = FormWindowState.Normal;

                _mainWindow.Show();
                _mainWindow.BringToFront();
                _mainWindow.Activate();
            }

            if (page != null) _mainWindow.NavigateTo(page);
        }

        // "Auto-detect failure" για το MD roadmap item "lock screen matches wallpaper, με
        // self-heal": ΧΩΡΙΣ Scheduled Task (ρητή απόφαση του χρήστη — ένα scheduled task που
        // ξανατρέχει με highest privileges σε κάθε logon μοιάζει με τεχνική persistence που
        // χρησιμοποιεί και malware). Αντ' αυτού, σε κάθε κανονική εκκίνηση του Opsis με τα Windows,
        // αν η ρύθμιση είναι ενεργή αλλά το πραγματικό registry key δεν ταιριάζει πλέον με το
        // αναμενόμενο αρχείο (π.χ. το επανέφερε ένα Windows update ή το άλλαξε ο χρήστης από το
        // Settings app), ρωτάμε με pop-up αν θέλει να το ξαναενεργοποιήσει — ποτέ σιωπηλή επανα-ανύψωση.
        private async void CheckLockScreenFollowsWallpaper()
        {
            var settings = AppSettings.Load();
            if (!settings.LockScreenFollowsWallpaper || string.IsNullOrEmpty(settings.LockScreenSnapshotPath)) return;
            if (LockScreenEngine.MatchesExpected(settings.LockScreenSnapshotPath)) return;
            if (!File.Exists(settings.LockScreenSnapshotPath)) return;

            var choice = MessageBox.Show(
                LocalizationManager.T("Dialog.LockScreenDriftedBody"),
                LocalizationManager.T("Dialog.LockScreenDriftedTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes)
            {
                settings.LockScreenFollowsWallpaper = false;
                settings.Save();
                return;
            }

            string error = string.Empty;
            string path = settings.LockScreenSnapshotPath;
            bool ok = await Task.Run(() => LockScreenEngine.TrySetLockScreenImageElevated(path, out error));
            if (!ok && error != "cancelled")
            {
                // Μη κρίσιμο αν αποτύχει ξανά — ο χρήστης το είδε ήδη και το ενέκρινε, απλά δεν
                // ξαναενοχλούμε σε αυτή τη συνεδρία.
            }
        }

        private void ApplyProfile(string name)
        {
            WorkspaceProfileService.Load(name);
            _trayIcon.ShowBalloonTip(1500, "Opsis", string.Format(LocalizationManager.T("Tray.ProfileAppliedFormat"), name), ToolTipIcon.Info);
        }

        private void ToggleWallpaper()
        {
            _wallpaperEnabled = !_wallpaperEnabled;
            _wallpaperToggleItem.Checked = _wallpaperEnabled;

            if (_wallpaperEnabled)
                WallpaperHostEngine.Instance.Enable();
            else
                WallpaperHostEngine.Instance.Disable();
        }

        private void ChooseWallpaperVideo()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = LocalizationManager.T("Filter.Video") + "|" + LocalizationManager.T("Filter.AllFiles"),
                Title = LocalizationManager.T("Wallpaper.AddVideoTitleTray"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                _ = AddVideosAsync(dialog.FileNames);
        }

        // Τα .wmv μετατρέπονται αυτόματα σε .mp4 μέσω FFmpeg (WmvConversionService) πριν
        // προστεθούν — ο ενσωματωμένος player (WebView2/Chromium) δεν έχει decoder για τον παλιό
        // codec WMV3/VC-1. Βλ. αναλυτικό σχόλιο στο WmvConversionService.
        private async Task AddVideosAsync(string[] fileNames)
        {
            var wmvFiles = fileNames.Where(f => string.Equals(Path.GetExtension(f), ".wmv", StringComparison.OrdinalIgnoreCase)).ToArray();
            var finalPaths = fileNames.Except(wmvFiles).ToList();

            if (wmvFiles.Length > 0)
            {
                if (!WmvConversionService.IsFfmpegAvailable)
                {
WmvConversionService.PromptInstallFfmpeg(wmvFiles.Length);
                }
                else
                {
                    foreach (var wmv in wmvFiles)
                    {
                        string? mp4 = await WmvConversionService.ConvertToMp4Async(wmv);
                        if (mp4 != null) finalPaths.Add(mp4);
                    }
                }
            }

            if (finalPaths.Count == 0) return;
            var settings = WallpaperSettings.Load();
            settings.AddVideoFiles(finalPaths.ToArray());
            settings.Mode = "Video";
            settings.Save();
            if (!_wallpaperEnabled) ToggleWallpaper();
            WallpaperHostEngine.Instance.Enable();
            // Βλ. αναλυτικό σχόλιο στο MainWindow.AddWallpaperVideosAsync — χωρίς αυτό, ένα ήδη
            // ανοιχτό wallpaper window δεν μαθαίνει ποτέ ότι το Mode/playlist άλλαξε.
            _ = WallpaperHostEngine.Instance.RefreshAllAsync();
        }

        private System.Threading.SynchronizationContext _uiContext = null!;
        private System.Windows.Forms.Timer? _alertTimer;
        private System.Windows.Forms.Timer? _updateCheckTimer;
        private int _cpuHighTicks, _ramHighTicks;
        private DateTime _lastAlertUtc = DateTime.MinValue;
        private bool _pendingUpdateAvailable;

        // Called every 5 s: CPU or memory >= 90% for 12 consecutive ticks (one minute) -> one tray balloon, then a 10-minute pause
        private void UsageAlertTick()
        {
            try
            {
                if (!AppSettings.Load().UsageAlertsEnabled) { _cpuHighTicks = _ramHighTicks = 0; return; }
                var m = SystemMonitorService.Instance.GetSnapshot();
                double ramPct = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                _cpuHighTicks = m.CpuPercent >= 90 ? _cpuHighTicks + 1 : 0;
                _ramHighTicks = ramPct >= 90 ? _ramHighTicks + 1 : 0;
                if (DateTime.UtcNow - _lastAlertUtc < TimeSpan.FromMinutes(10)) return;
                if (_ramHighTicks >= 12)
                {
                    _lastAlertUtc = DateTime.UtcNow; _ramHighTicks = 0;
                    var top = TopProcessesService.TopByMemory(1).FirstOrDefault();
                    _trayIcon.ShowBalloonTip(8000, LocalizationManager.T("Alert.Title"), string.Format(LocalizationManager.T("Alert.RamHigh"), top.Name ?? "?"), ToolTipIcon.Warning);
                }
                else if (_cpuHighTicks >= 12)
                {
                    _lastAlertUtc = DateTime.UtcNow; _cpuHighTicks = 0;
                    _trayIcon.ShowBalloonTip(8000, LocalizationManager.T("Alert.Title"), LocalizationManager.T("Alert.CpuHigh"), ToolTipIcon.Warning);
                }
            }
            catch (Exception) { /* an alert must never break the tray */ }
        }

        // Ίδια λογική με το self-update του GearWin (ζητήθηκε ρητά· ήδη portαρισμένο/ζωντανά
        // δοκιμασμένο στο OpsisNewsCrawler, βλ. TickerHost.AutoCheckForUpdatesAsync): έλεγχος σε
        // κάθε εκκίνηση ΚΑΙ μετά κάθε ~12 ώρες, offline retry (1 λεπτό, έως 4 φορές), ΜΙΑ
        // ειδοποίηση ανά ΈΚΔΟΣΗ (όχι ανά check — AppSettings.LastNotifiedUpdateTag).
        private bool _autoCheckingUpdate;

        private async Task AutoCheckForUpdatesAsync()
        {
            if (_autoCheckingUpdate) return;
            _autoCheckingUpdate = true;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(25)); // do not compete with the startup work
                if (!AppSettings.Load().UpdateCheckEnabled) return;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    var result = await UpdateCheckService.CheckAsync();
                    if (result.Succeeded)
                    {
                        var s = AppSettings.Load();
                        s.LastUpdateCheckUtc = DateTime.UtcNow;
                        if (result.Update != null && !string.Equals(s.LastNotifiedUpdateTag, result.Update.Tag, StringComparison.Ordinal))
                        {
                            s.LastNotifiedUpdateTag = result.Update.Tag;
                            s.Save();
                            var found = result.Update;
                            _uiContext.Post(_ =>
                            {
                                _pendingUpdateAvailable = true;
                                _trayIcon.ShowBalloonTip(10000, LocalizationManager.T("Update.Title"),
                                    string.Format(LocalizationManager.T("Update.Available"), found.Version.ToString(3)), ToolTipIcon.Info);
                            }, null);
                        }
                        else s.Save();
                        return;
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1)); // offline: wait for the network
                }
            }
            catch (Exception) { /* offline etc. */ }
            finally { _autoCheckingUpdate = false; }
        }

        // Το κλικ στο balloon ανοίγει την κάρτα ενημερώσεων μέσα στην εφαρμογή (About), όχι
        // απευθείας το browser — εκεί υπάρχει πλέον και το κουμπί "Λήψη & εγκατάσταση".
        private void OpenPendingUpdate()
        {
            if (!_pendingUpdateAvailable) return;
            _pendingUpdateAvailable = false;
            ShowMainWindow("About");
        }

        // Κοινός, πλήρης καθαρισμός εξόδου — καλείται ΚΑΙ από το μενού "Έξοδος" ΚΑΙ από ένα
        // self-update install (μέσω RequestExit/ExitRequested) ώστε και στις δύο περιπτώσεις να
        // αποθηκεύεται η συνεδρία και να σταματούν σωστά τα plugins/hotkeys πριν το Application.Exit().
        private void PerformExit()
        {
            try { WorkspaceProfileService.Save("Last Session"); WorkspaceProfileService.ExitSaveDone = true; } catch { }
            PluginHost.Instance.StopAll();
            _hotkeys.Dispose();
            _taskbarWatcher.Dispose();
            DesktopIconVisibilityEngine.Instance.Stop();
            _trayIcon.Visible = false;
            Application.Exit();
        }

        private void ChooseWallpaperFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = LocalizationManager.T("Wallpaper.ChooseFolder") };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                int added = WallpaperHostEngine.Instance.AddVideoFolder(dialog.SelectedPath);
                if (added > 0 && !_wallpaperEnabled) ToggleWallpaper();
            }
        }

        private static Icon LoadTrayIcon()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "Opsis.ico");
            return File.Exists(path) ? new Icon(path) : SystemIcons.Application;
        }

        // Το DeskZones έγινε πραγματικό FancyZones-style στυλ (κανένα μόνιμα ορατό
        // παράθυρο-ζώνη πλέον) — το tray μενού ανοίγει απευθείας τον editor διάταξης.
        private void AddDeskZone()
        {
            using var editor = new ZoneLayoutEditorForm();
            editor.ShowDialog();
        }
    }
}
