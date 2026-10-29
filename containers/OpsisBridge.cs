using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;

namespace Desktop_Fences
{
    // Opsis integration. The settings of the fences live in the Opsis application (DeskZones page), not in a separate window:
    //  * Opsis edits  Profiles\<active>\options.json  -> this class notices the change and applies it live ("reload" below);
    //  * Opsis drops small command files into  <data>\bridge\cmd_*.txt  (backup, restore, manage profiles...) -> executed here;
    //  * the "Options..." menu entries ask Opsis to open its settings page by writing  <data>\bridge\to_opsis.txt.
    internal static class OpsisBridge
    {
        private static FileSystemWatcher _optionsWatcher, _commandWatcher, _themeWatcher;
        private static DispatcherTimer _debounce;
        private static string _lastHash = "";
        private static bool _started;

        public static string BridgeDir => Path.Combine(DataRoot.Dir, "bridge");

        public static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                Directory.CreateDirectory(BridgeDir);
                foreach (var old in Directory.GetFiles(BridgeDir, "cmd_*.txt")) { try { File.Delete(old); } catch { } }

                string dir = ProfileManager.CurrentProfileDir;
                _lastHash = HashOf(Path.Combine(dir, "options.json"));
                _optionsWatcher = new FileSystemWatcher(dir, "options.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
                _optionsWatcher.Changed += (s, e) => Dispatch(() => { _debounce.Stop(); _debounce.Start(); });

                _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _debounce.Tick += (s, e) => { _debounce.Stop(); ApplyExternalOptions(); };

                var themeWatcher = new FileSystemWatcher(BridgeDir, "theme.txt") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
                void ReadTheme() { try { System.Threading.Thread.Sleep(60); var spec = File.ReadAllText(Path.Combine(BridgeDir, "theme.txt")).Trim(); if (spec.Length > 0) OpsisTheme.Reload(spec); } catch { } }
                themeWatcher.Changed += (s, e) => ReadTheme();
                themeWatcher.Created += (s, e) => ReadTheme();
                _themeWatcher = themeWatcher;

                _commandWatcher = new FileSystemWatcher(BridgeDir, "cmd_*.txt") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
                _commandWatcher.Created += (s, e) => Dispatch(() => RunCommandFile(e.FullPath));
                _commandWatcher.Changed += (s, e) => Dispatch(() => RunCommandFile(e.FullPath));
            }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, "OpsisBridge start failed: " + ex.Message); }
        }

        private static void Dispatch(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d != null) d.BeginInvoke(a);
        }

        private static string HashOf(string path)
        {
            try { using var sha = SHA1.Create(); using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToBase64String(sha.ComputeHash(fs)); }
            catch { return ""; }
        }

        // ---- options edited by Opsis ----
        private static void ApplyExternalOptions()
        {
            try
            {
                string path = Path.Combine(ProfileManager.CurrentProfileDir, "options.json");
                string h = HashOf(path);
                if (h.Length == 0 || h == _lastHash) return;

                bool tray = SettingsManager.ShowInTray, ctx = SettingsManager.EnableContextMenu;
                int tbHeight = SettingsManager.TitleBarHeight;
                bool scroll = SettingsManager.DisableFenceScrollbars, watermark = SettingsManager.ShowBackgroundImageOnPortalFences;
                bool autoOrg = SettingsManager.EnableAutoOrganize, profAuto = SettingsManager.EnableProfileAutomation;

                SettingsManager.LoadSettings();                       // re-reads the file (and re-hydrates missing keys)
                _lastHash = HashOf(path);

                if (ctx != SettingsManager.EnableContextMenu) RegistryHelper.ToggleContextMenu(SettingsManager.EnableContextMenu);
                if (tray != SettingsManager.ShowInTray && TrayManager.Instance != null)
                    TrayManager.Instance.GetType().GetField("Showintray", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(TrayManager.Instance, SettingsManager.ShowInTray);
                FenceManager.ResetAutoHideTimer();
                if (profAuto != SettingsManager.EnableProfileAutomation)
                {
                    TrayManager.Instance?.UpdateAutomationMenuCheck(SettingsManager.EnableProfileAutomation);
                    if (SettingsManager.EnableProfileAutomation) AutomationManager.Start();
                }
                TrayManager.Instance?.UpdateTrayIcon();
                Utility.UpdateFenceVisuals();
                if (tbHeight != SettingsManager.TitleBarHeight || scroll != SettingsManager.DisableFenceScrollbars || watermark != SettingsManager.ShowBackgroundImageOnPortalFences)
                    TrayManager.reloadAllFences();
            }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, "Applying Opsis options failed: " + ex.Message); }
        }

        // ---- commands from Opsis ----
        private static void RunCommandFile(string file)
        {
            string cmd;
            try
            {
                System.Threading.Thread.Sleep(60);
                cmd = File.ReadAllText(file).Trim();
                File.Delete(file);
            }
            catch { return; }
            try { Execute(cmd); }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Opsis command '{cmd}' failed: {ex.Message}"); }
        }

        private static void Execute(string cmd)
        {
            // commands with an argument: "profile:<name>", "create-profile:<name>", "show-hidden:<title>"
            int colon = cmd.IndexOf(':');
            if (colon > 0)
            {
                string verb = cmd.Substring(0, colon), arg = cmd.Substring(colon + 1);
                switch (verb)
                {
                    case "profile": ProfileManager.SwitchToProfile(arg); ProfileManager.SetManualBaseProfile(arg); TrayManager.Instance?.UpdateProfilesMenu(); return;
                    case "create-profile": if (ProfileManager.CreateProfile(arg)) TrayManager.Instance?.UpdateProfilesMenu(); return;
                    case "show-hidden": TrayManager.ShowHiddenFenceByTitle(arg); return;
                }
            }
            switch (cmd)
            {
                case "quick-hide": FenceManager.ForceHideFences(); break;
                case "quick-show": FenceManager.WakeUpFences(); break;
                case "focus-fence": new FenceFocusFormManager().ShowDialog(); break;
                case "toggle-automation": SettingsManager.EnableProfileAutomation = !SettingsManager.EnableProfileAutomation; try { SettingsManager.SaveSettings(); } catch { } if (SettingsManager.EnableProfileAutomation) AutomationManager.Start(); PublishState(); break;
                case "toggle-autoorganize": SettingsManager.EnableAutoOrganize = !SettingsManager.EnableAutoOrganize; try { SettingsManager.SaveSettings(); } catch { } if (SettingsManager.EnableAutoOrganize) AutoOrganizeManager.Start(); else AutoOrganizeManager.Stop(); PublishState(); break;
                case "backup": BackupManager.BackupData(); break;
                case "restore": OptionsFormManager.RestoreBackup(); break;
                case "open-backups": BackupManager.OpenBackupsFolder(); break;
                case "manage-profiles": new ProfileManagerForm().ShowDialog(); break;
                case "manage-automation": new AutomationRulesForm().ShowDialog(); break;
                case "smart-rules": new AutoOrganizeForm().ShowDialog(); break;
                case "organize-now": AutoOrganizeManager.ProcessDesktopNow(); break;
                case "screen-bound": FenceManager.ForceRepositionAllFences(); break;
                case "reset-styles": FenceManager.ResetAllCustomizations(); break;
                case "factory-reset": OptionsFormManager.PerformFullFactoryReset(); break;
                case "open-log": OptionsFormManager.OpenLogFile(); break;
                case "import-fence": BackupManager.ImportFence(); break;
                case "new-note": FenceManager.CreateNewFenceAt("Note"); break;
                case "new-fence": FenceManager.CreateNewFenceAt("Data"); break;
                case "reload-all": _ = TrayManager.reloadAllFences(); break;
                case "exit": DesktopOriginals.ShowAll(); Application.Current.Shutdown(); break;
            }
        }

        // ---- state published for the Opsis tray menu (profiles, switches, hidden containers) ----
        public static void PublishState()
        {
            try
            {
                Directory.CreateDirectory(BridgeDir);
                var state = new
                {
                    profile = ProfileManager.CurrentProfileName,
                    profiles = ProfileManager.GetProfiles().Select(p => p.Name).ToArray(),
                    automation = SettingsManager.EnableProfileAutomation,
                    autoOrganize = SettingsManager.EnableAutoOrganize,
                    hidden = TrayManager.HiddenTitles(),
                };
                string tmp = Path.Combine(BridgeDir, "state.json.tmp"), dst = Path.Combine(BridgeDir, "state.json");
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(state));
                File.Copy(tmp, dst, true); File.Delete(tmp);
            }
            catch { }
        }

        // ---- DeskContainers -> Opsis ----
        public static void OpenOpsisSettings()
        {
            try
            {
                Directory.CreateDirectory(BridgeDir);
                File.WriteAllText(Path.Combine(BridgeDir, "to_opsis.txt"), "open-settings " + DateTime.Now.Ticks);
            }
            catch { }
        }
    }
}
