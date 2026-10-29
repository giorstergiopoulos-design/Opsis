using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace OpsisContainers
{
    // Opsis: ALL DeskContainers settings live in the three-bars menu of every container (Settings > General / Appearance / Tools / Profiles /
    // Hotkeys / Smart desktop / Logging). They used to be cards on the Opsis DeskZones page. Every entry writes the engine's options through
    // SettingsManager and applies the change live; texts come from the Opsis locale files (Loc.K).
    internal static class SettingsMenu
    {
        private static readonly string[] Colors = { "Gray", "Black", "White", "Beige", "Green", "Purple", "Fuchsia", "Yellow", "Orange", "Red", "Blue", "Bismark", "Teal" };
        private static readonly string[] MenuIcons = { "⋯", "☰", "≣", "𓃑" };
        private static readonly string[] Modifiers = { "Control", "Alt", "Shift", "Control, Alt", "Control, Shift", "Alt, Shift", "Control, Alt, Shift", "Win" };
        private static readonly string[] LogLevels = { "Debug", "Info", "Warn", "Error" };
        private static readonly string[] LogCategories = { "General", "ContainerCreation", "ContainerUpdate", "UI", "IconHandling", "ImportExport", "Settings", "BackgroundValidation" };

        private static string L(string key, string en) => Loc.K(key, en);

        private static void Save() { try { SettingsManager.SaveSettings(); } catch { } }
        /// <summary>Set by the self-test: apply-callbacks (window reloads, hotkey re-registration...) are skipped, only the persistence is checked.</summary>
        internal static bool SuppressSideEffects;
        private static void SafeAfter(Action after) { if (SuppressSideEffects) return; try { after?.Invoke(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Settings menu action failed: " + ex.Message); } }

        private static void Toggle(ItemCollection parent, ContextMenu owner, string key, string en, Func<bool> get, Action<bool> set, Action after = null)
        {
            var mi = new MenuItem { Header = L(key, en), IsCheckable = true, IsChecked = get() };
            mi.Click += (s, e) => { set(mi.IsChecked); Save(); SafeAfter(after); };
            owner.Opened += (s, e) => mi.IsChecked = get();           // stays in sync with changes made elsewhere
            parent.Add(mi);
        }

        private static void Choice<T>(ItemCollection parent, ContextMenu owner, string key, string en, IEnumerable<(string Label, T Value)> options, Func<T> get, Action<T> set, Action after = null)
        {
            var sub = new MenuItem { Header = L(key, en) };
            var items = new List<(MenuItem Item, T Value)>();
            foreach (var (label, value) in options)
            {
                var v = value;
                var mi = new MenuItem { Header = label, IsCheckable = true, StaysOpenOnClick = false, Tag = "choice" };
                mi.Click += (s, e) => { set(v); Save(); SafeAfter(after); foreach (var it in items) it.Item.IsChecked = EqualityComparer<T>.Default.Equals(it.Value, v); };
                items.Add((mi, v));
                sub.Items.Add(mi);
            }
            void Sync() { var cur = get(); foreach (var it in items) it.Item.IsChecked = EqualityComparer<T>.Default.Equals(it.Value, cur); }
            owner.Opened += (s, e) => Sync();
            Sync();
            parent.Add(sub);
        }

        private static IEnumerable<(string, int)> Steps(int from, int to, int step, string fmtKey, string fmtEn)
        {
            for (int v = from; v <= to; v += step) yield return (string.Format(L(fmtKey, fmtEn), v), v);
        }

        private static void Command(ItemCollection parent, string key, string en, Action run)
        {
            var mi = new MenuItem { Header = L(key, en) };
            mi.Click += (s, e) => { try { run(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Menu command failed: " + ex.Message); } };
            parent.Add(mi);
        }

        private static void HotkeyGroup(ItemCollection parent, ContextMenu owner, string key, string en, Func<bool> getOn, Action<bool> setOn, Func<string> getMod, Action<string> setMod, Func<int> getKey, Action<int> setKey)
        {
            var group = new MenuItem { Header = L(key, en) };
            Toggle(group.Items, owner, "DCM.Enabled", "Enabled", getOn, setOn, RestartHotkeys);
            Choice(group.Items, owner, "DCM.Modifier", "Modifier", Modifiers.Select(m => (m.Replace("Control", "Ctrl").Replace(", ", "+"), m)), getMod, setMod, RestartHotkeys);
            if (getKey != null)
            {
                var keySub = new MenuItem { Header = L("DCM.Key", "Key") };
                void Section(string k, string e, IEnumerable<(string, int)> keys)
                {
                    Choice(keySub.Items, owner, k, e, keys, getKey, setKey, RestartHotkeys);
                }
                Section("DCM.KeysLetters", "Letters", Enumerable.Range('A', 26).Select(c => (((char)c).ToString(), c)));
                Section("DCM.KeysDigits", "Digits", Enumerable.Range('0', 10).Select(c => (((char)c).ToString(), c)));
                Section("DCM.KeysFunction", "Function keys", Enumerable.Range(1, 12).Select(i => ("F" + i, 0x6F + i)));
                Section("DCM.KeysOther", "Other", new[] { (",", 0xBC), (".", 0xBE), ("`", 192), ("Space", 32), ("Tab", 9), ("Enter", 13), ("Esc", 27) });
                group.Items.Add(keySub);
            }
            parent.Add(group);
        }

        private static void RestartHotkeys()
        {
            try { GlobalHotkeyManager.StopMonitoring(); GlobalHotkeyManager.StartMonitoring(); } catch { }
        }

        public static MenuItem Build(ContextMenu owner)
        {
            var root = new MenuItem { Header = L("DCM.Settings", "Settings") };

            // ---- General
            var general = new MenuItem { Header = L("DCM.General", "General") };
            Toggle(general.Items, owner, "DCM.SingleClick", "Single click to launch", () => SettingsManager.SingleClickToLaunch, v => SettingsManager.SingleClickToLaunch = v);
            Toggle(general.Items, owner, "DCM.OriginalsLeave", "Dropped desktop icons leave the desktop", () => SettingsManager.HideDesktopOriginalsOnDrop, v => SettingsManager.HideDesktopOriginalsOnDrop = v);
            Toggle(general.Items, owner, "DCM.Snap", "Snap near other containers", () => SettingsManager.IsSnapEnabled, v => SettingsManager.IsSnapEnabled = v);
            Toggle(general.Items, owner, "DCM.DimSnap", "Dimension snap", () => SettingsManager.EnableDimensionSnap, v => SettingsManager.EnableDimensionSnap = v);
            Toggle(general.Items, owner, "DCM.RecycleBin", "Use the Recycle Bin on Portal containers", () => SettingsManager.UseRecycleBin, v => SettingsManager.UseRecycleBin = v);
            Toggle(general.Items, owner, "DCM.ContextMenu", "Show \"New DeskContainer\" in the desktop menu", () => SettingsManager.EnableContextMenu, v => SettingsManager.EnableContextMenu = v, () => RegistryHelper.ToggleContextMenu(SettingsManager.EnableContextMenu));
            Toggle(general.Items, owner, "DCM.AutoHide", "Auto-hide after inactivity", () => SettingsManager.AutoHideContainers, v => SettingsManager.AutoHideContainers = v, () => ContainerManager.ResetAutoHideTimer());
            Choice(general.Items, owner, "DCM.IdleTime", "Idle time before hiding", new[] { 15, 30, 60, 120, 300 }.Select(v => (string.Format(L("DCM.Seconds", "{0} seconds"), v), v)), () => SettingsManager.AutoHideTime, v => SettingsManager.AutoHideTime = v, () => ContainerManager.ResetAutoHideTimer());
            root.Items.Add(general);

            // ---- Appearance
            var look = new MenuItem { Header = L("DCM.Appearance", "Appearance") };
            Toggle(look.Items, owner, "DCM.Watermark", "Watermark on Portal containers", () => SettingsManager.ShowBackgroundImageOnPortalContainers, v => SettingsManager.ShowBackgroundImageOnPortalContainers = v, () => TrayManager.reloadAllContainers());
            Toggle(look.Items, owner, "DCM.NoScrollbars", "Hide scrollbars", () => SettingsManager.DisableContainerScrollbars, v => SettingsManager.DisableContainerScrollbars = v, () => TrayManager.reloadAllContainers());
            Toggle(look.Items, owner, "DCM.Sounds", "Sounds", () => SettingsManager.EnableSounds, v => SettingsManager.EnableSounds = v);
            Toggle(look.Items, owner, "DCM.Chameleon", "Chameleon (colour from the wallpaper)", () => SettingsManager.EnableChameleonMode, v => SettingsManager.EnableChameleonMode = v, () => Utility.UpdateContainerVisuals());
            Toggle(look.Items, owner, "DCM.AutoRoll", "Auto roll-up (expand on hover)", () => SettingsManager.AutoRollUpOnHover, v => SettingsManager.AutoRollUpOnHover = v);
            Choice(look.Items, owner, "DCM.Tint", "Container tint", Steps(0, 100, 10, "DCM.Percent", "{0}%"), () => SettingsManager.TintValue / 10 * 10, v => SettingsManager.TintValue = v, () => Utility.UpdateContainerVisuals());
            Choice(look.Items, owner, "DCM.MenuTint", "Menu icon opacity", Steps(0, 100, 10, "DCM.Percent", "{0}%"), () => SettingsManager.MenuTintValue / 10 * 10, v => SettingsManager.MenuTintValue = v, () => Utility.UpdateContainerVisuals());
            Choice(look.Items, owner, "DCM.Colour", "Colour", Colors.Select(c => (L("DCM.Color." + c, c), c)), () => SettingsManager.SelectedColor, v => SettingsManager.SelectedColor = v, () => Utility.UpdateContainerVisuals());
            Choice(look.Items, owner, "DCM.TitleColour", "Title bar colour", new[] { "Auto" }.Concat(Colors).Select(c => (c == "Auto" ? L("DCM.Auto", "Auto") : L("DCM.Color." + c, c), c)), () => SettingsManager.TitleBarColor, v => SettingsManager.TitleBarColor = v, () => Utility.UpdateContainerVisuals());
            Choice(look.Items, owner, "DCM.TitleOpacity", "Title bar opacity", Steps(0, 100, 10, "DCM.Percent", "{0}%"), () => SettingsManager.TitleBarOpacity / 10 * 10, v => SettingsManager.TitleBarOpacity = v, () => Utility.UpdateContainerVisuals());
            Choice(look.Items, owner, "DCM.TitleHeight", "Title bar height", new[] { 24, 28, 32, 40, 48, 64 }.Select(v => ($"{v} px", v)), () => SettingsManager.TitleBarHeight, v => SettingsManager.TitleBarHeight = v, () => TrayManager.reloadAllContainers());
            Choice(look.Items, owner, "DCM.LaunchEffect", "Launch effect", Enum.GetValues(typeof(LaunchEffectsManager.LaunchEffect)).Cast<LaunchEffectsManager.LaunchEffect>().Select(e => (L("DCM.Effect." + e, e.ToString()), e)), () => SettingsManager.LaunchEffect, v => SettingsManager.LaunchEffect = v);
            Choice(look.Items, owner, "DCM.MenuIcon", "Menu icon", MenuIcons.Select((g, i) => (g, i)), () => SettingsManager.MenuIcon, v => SettingsManager.MenuIcon = v, () => TrayManager.reloadAllContainers());
            root.Items.Add(look);

            // ---- Tools
            var tools = new MenuItem { Header = L("DCM.Tools", "Tools") };
            Command(tools.Items, "DCM.Backup", "Back up now", () => BackupManager.BackupData());
            Command(tools.Items, "DCM.Restore", "Restore from a backup...", () => OptionsFormManager.RestoreBackup());
            Command(tools.Items, "DCM.OpenBackups", "Open the backups folder", () => BackupManager.OpenBackupsFolder());
            Toggle(tools.Items, owner, "DCM.AutoBackup", "Automatic daily backup", () => SettingsManager.EnableAutoBackup, v => SettingsManager.EnableAutoBackup = v);
            tools.Items.Add(new Separator());
            Command(tools.Items, "DCM.ScreenBound", "Bring containers back on screen", () => ContainerManager.ForceRepositionAllContainers());
            Command(tools.Items, "DCM.ResetStyles", "Reset appearance", () =>
            {
                if (System.Windows.MessageBox.Show(L("DCM.ConfirmResetStyles", "Reset all visual customizations of the containers?"), L("DCM.Title", "Opsis DeskContainers"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
                    ContainerManager.ResetAllCustomizations();
            });
            Command(tools.Items, "DCM.ClearData", "Delete all DeskContainers data...", () => OptionsFormManager.PerformFullFactoryReset());
            root.Items.Add(tools);

            // ---- Profiles
            var profiles = new MenuItem { Header = L("DCM.Profiles", "Profiles") };
            Command(profiles.Items, "DCM.ManageProfiles", "Manage profiles...", () => new ProfileManagerForm().ShowDialog());
            Command(profiles.Items, "DCM.ManageAutomation", "Manage automation rules...", () => new AutomationRulesForm().ShowDialog());
            Toggle(profiles.Items, owner, "DCM.ProfileAutomation", "Switch profiles automatically (automation rules)", () => SettingsManager.EnableProfileAutomation, v => SettingsManager.EnableProfileAutomation = v, () => { if (SettingsManager.EnableProfileAutomation) AutomationManager.Start(); });
            root.Items.Add(profiles);

            // ---- Hotkeys
            var keys = new MenuItem { Header = L("DCM.Hotkeys", "Hotkeys") };
            HotkeyGroup(keys.Items, owner, "DCM.HkProfiles", "Profile switching: direct [0-9]", () => SettingsManager.EnableProfileHotkeys, v => SettingsManager.EnableProfileHotkeys = v, () => SettingsManager.ProfileSwitchModifier, v => SettingsManager.ProfileSwitchModifier = v, null, null);
            HotkeyGroup(keys.Items, owner, "DCM.HkPrev", "Previous profile", () => SettingsManager.EnableProfileHotkeys, v => SettingsManager.EnableProfileHotkeys = v, () => SettingsManager.ProfilePrevModifier, v => SettingsManager.ProfilePrevModifier = v, () => SettingsManager.ProfilePrevKey, v => SettingsManager.ProfilePrevKey = v);
            HotkeyGroup(keys.Items, owner, "DCM.HkNext", "Next profile", () => SettingsManager.EnableProfileHotkeys, v => SettingsManager.EnableProfileHotkeys = v, () => SettingsManager.ProfileNextModifier, v => SettingsManager.ProfileNextModifier = v, () => SettingsManager.ProfileNextKey, v => SettingsManager.ProfileNextKey = v);
            HotkeyGroup(keys.Items, owner, "DCM.HkFocus", "Focus a container", () => SettingsManager.EnableFocusContainerHotkey, v => SettingsManager.EnableFocusContainerHotkey = v, () => SettingsManager.FocusContainerModifier, v => SettingsManager.FocusContainerModifier = v, () => SettingsManager.FocusContainerKey, v => SettingsManager.FocusContainerKey = v);
            HotkeyGroup(keys.Items, owner, "DCM.HkSpot", "Spot Search", () => SettingsManager.EnableSpotSearchHotkey, v => SettingsManager.EnableSpotSearchHotkey = v, () => SettingsManager.SpotSearchModifier, v => SettingsManager.SpotSearchModifier = v, () => SettingsManager.SpotSearchKey, v => SettingsManager.SpotSearchKey = v);
            root.Items.Add(keys);

            // ---- Smart desktop
            var smart = new MenuItem { Header = L("DCM.Smart", "Smart desktop (auto-organize)") };
            Toggle(smart.Items, owner, "DCM.AutoOrganize", "Enable auto-organize", () => SettingsManager.EnableAutoOrganize, v => SettingsManager.EnableAutoOrganize = v, () => { if (SettingsManager.EnableAutoOrganize) AutoOrganizeManager.Start(); else AutoOrganizeManager.Stop(); });
            Toggle(smart.Items, owner, "DCM.AutoOrgToasts", "Notifications when a rule runs", () => SettingsManager.EnableAutoOrganizeNotifications, v => SettingsManager.EnableAutoOrganizeNotifications = v);
            Command(smart.Items, "DCM.SmartRules", "Rules...", () => new AutoOrganizeForm().ShowDialog());
            Command(smart.Items, "DCM.OrganizeNow", "Organize now", () =>
            {
                if (System.Windows.MessageBox.Show(L("DCM.ConfirmOrganize", "Files on your desktop will be moved to their target folders according to your rules. Continue?"), L("DCM.Title", "Opsis DeskContainers"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
                    AutoOrganizeManager.ProcessDesktopNow();
            });
            root.Items.Add(smart);

            // ---- Logging
            var log = new MenuItem { Header = L("DCM.Logging", "Logging") };
            Toggle(log.Items, owner, "DCM.LogEnable", "Enable logging", () => SettingsManager.IsLogEnabled, v => SettingsManager.IsLogEnabled = v);
            Choice(log.Items, owner, "DCM.LogLevel", "Level", LogLevels.Select(l => (l, l)), () => SettingsManager.MinLogLevel.ToString(), v => { if (Enum.TryParse<LogManager.LogLevel>(v, out var lvl)) SettingsManager.MinLogLevel = lvl; });
            var cats = new MenuItem { Header = L("DCM.LogCategories", "Categories") };
            foreach (var c in LogCategories)
            {
                string name = c;
                Toggle(cats.Items, owner, "", name, () => SettingsManager.EnabledLogCategories.Any(x => x.ToString() == name), on =>
                {
                    if (!Enum.TryParse<LogManager.LogCategory>(name, out var cat)) return;
                    if (on && !SettingsManager.EnabledLogCategories.Contains(cat)) SettingsManager.EnabledLogCategories.Add(cat);
                    else if (!on) SettingsManager.EnabledLogCategories.Remove(cat);
                });
            }
            log.Items.Add(cats);
            Command(log.Items, "DCM.OpenLog", "Open the log file", () => OptionsFormManager.OpenLogFile());
            root.Items.Add(log);

            root.Items.Add(new Separator());
            Command(root.Items, "DCM.OpenOpsis", "Opsis settings...", () => OpsisBridge.OpenOpsisSettings());
            return root;
        }
    }
}
