using IWshRuntimeLibrary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static System.Net.Mime.MediaTypeNames;
using Color = System.Drawing.Color;

namespace OpsisContainers
{
    public static class ContainerManager
    {
        internal static readonly System.Collections.Generic.Dictionary<string, Action> RenameStarters = new System.Collections.Generic.Dictionary<string, Action>();

        // Opsis: expand-on-hover roll-up (the Stardock/DeskContainers behaviour). When "Auto roll-up on hover" is on, a container rolls up
        // 0.7 s after the mouse leaves it and unrolls when the mouse comes back, reusing the normal roll-up code (toggleRoll).
        internal static readonly System.Collections.Generic.Dictionary<string, Action> RollToggles = new System.Collections.Generic.Dictionary<string, Action>();
        private static System.Windows.Threading.DispatcherTimer _hoverRollTimer;
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _leftAt = new System.Collections.Generic.Dictionary<string, DateTime>();

        private static void EnsureHoverRollTimer()
        {
            if (_hoverRollTimer != null) return;
            _hoverRollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _hoverRollTimer.Tick += (s, e) =>
            {
                if (!SettingsManager.AutoRollUpOnHover || System.Windows.Application.Current == null) return;
                foreach (var w in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList())
                {
                    if (!(w.Tag is string id) || !RollToggles.TryGetValue(id, out var toggle) || _containersInTransition.Contains(id)) continue;
                    dynamic f = ContainerDataManager.ContainerData.FirstOrDefault(x => x.Id?.ToString() == id);
                    if (f == null || f.ItemsType?.ToString() == "Note") continue;                       // note containers keep their text visible
                    bool rolled = f.IsRolled?.ToString().ToLower() == "true";
                    bool over = w.IsMouseOver || w.IsKeyboardFocusWithin || Mouse.LeftButton == MouseButtonState.Pressed;
                    try
                    {
                        if (over) { _leftAt.Remove(id); if (rolled) toggle(); }
                        else if (rolled) _leftAt.Remove(id);
                        else if (!_leftAt.TryGetValue(id, out var t)) _leftAt[id] = DateTime.UtcNow;
                        else if ((DateTime.UtcNow - t).TotalMilliseconds > 700) { _leftAt.Remove(id); toggle(); }
                    }
                    catch (Exception) { }
                }
            };
            _hoverRollTimer.Start();
        }

        // Opsis: Left/Right arrow keys scroll the contents of the container under the mouse (the mouse wheel already scrolls natively).
        // The containers never take keyboard focus, so this comes from the global keyboard hook; it only acts when the pointer is over a container
        // that no application window covers.
        internal static readonly System.Collections.Generic.Dictionary<string, ScrollViewer> ContentScrollers = new System.Collections.Generic.Dictionary<string, ScrollViewer>();

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);

        private static bool DesktopOrContainerIsForeground()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return true;
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, 64);
            string cls = sb.ToString();
            return cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd";
        }

        public static void ScrollHoveredContainer(int direction)
        {
            if (System.Windows.Application.Current == null) return;   // IsMouseOver below already means no application window covers the container at the pointer
            foreach (var w in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>())
            {
                if (!w.IsMouseOver || !(w.Tag is string id) || !ContentScrollers.TryGetValue(id, out var sv)) continue;
                double step = 70;                                                   // about one row of icons
                sv.ScrollToVerticalOffset(Math.Max(0, Math.Min(sv.ScrollableHeight, sv.VerticalOffset + direction * step)));
                return;
            }
        }

        // Opsis: "Sort by" for Data containers (from the old DeskContainers). One-shot: reorders the stored items (current tab) and reloads.
        internal static void SortContainerItems(string containerId, string mode)
        {
            try
            {
                int idx = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (idx < 0) return;
                JObject container = ContainerDataManager.ContainerData[idx] as JObject ?? JObject.FromObject(ContainerDataManager.ContainerData[idx]);
                if (container["ItemsType"]?.ToString() != "Data") return;
                JArray items = container["Items"] as JArray;
                if (string.Equals(container["TabsEnabled"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase) && container["Tabs"] is JArray tabs)
                {
                    int ci = container["CurrentTab"]?.ToObject<int>() ?? 0;
                    if (ci >= 0 && ci < tabs.Count) items = tabs[ci]["Items"] as JArray ?? items;
                }
                if (items == null || items.Count < 2) return;

                string P(JToken t) => t["Filename"]?.ToString() ?? "";
                string Name(JToken t) => t["DisplayName"]?.ToString() is string d && d.Length > 0 ? d : System.IO.Path.GetFileNameWithoutExtension(P(t));
                bool IsDir(JToken t) => t["IsFolder"]?.ToObject<bool>() == true;
                DateTime When(JToken t) { try { string p = P(t); return IsDir(t) ? System.IO.Directory.GetLastWriteTime(p) : System.IO.File.GetLastWriteTime(p); } catch { return DateTime.MinValue; } }
                long Size(JToken t) { try { return System.IO.File.Exists(P(t)) ? new System.IO.FileInfo(P(t)).Length : 0; } catch { return 0; } }

                IEnumerable<JToken> ordered;
                switch (mode)
                {
                    case "type": ordered = items.OrderByDescending(IsDir).ThenBy(t => System.IO.Path.GetExtension(P(t)), StringComparer.OrdinalIgnoreCase).ThenBy(Name, StringComparer.CurrentCultureIgnoreCase); break;
                    case "date": ordered = items.OrderByDescending(When); break;
                    case "size": ordered = items.OrderByDescending(Size); break;
                    default: ordered = items.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase); break;
                }
                var sorted = ordered.Select(t => t.DeepClone()).ToList();
                items.Clear();
                foreach (var t in sorted) items.Add(t);
                ContainerDataManager.ContainerData[idx] = container;
                ContainerDataManager.SaveContainerData();
                ReloadContainers();
            }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, "SortContainerItems failed: " + ex.Message); }
        }

        // Opsis: a container flagged "ExcludeFromAutoHide" stays visible when the idle auto-hide hides the others.
        private static bool IsExcludedFromAutoHide(NonActivatingWindow w)
        {
            try
            {
                string id = w.Tag?.ToString();
                var f = ContainerDataManager.ContainerData.FirstOrDefault(x => x.Id?.ToString() == id);
                if (f == null) return false;
                string v = Utility.ContainerProp((object)f, "ExcludeFromAutoHide");
                return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Opsis: create a container from the Opsis settings page (near the top-left of the primary screen, cascading).
        public static void CreateNewContainerAt(string itemsType)
        {
            int n = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().Count();
            CreateNewContainer("", itemsType, 140 + (n % 6) * 36, 140 + (n % 6) * 36);
        }

        // Opsis: F2 -> rename the container currently under the mouse pointer.
        // Opsis: first-run welcome note (replaces the upstream "Opsis DeskContainers+ Startup Tips").
        private static bool OpsisGreek => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el";

        private static string OpsisWelcomeTitle() => OpsisGreek ? "Καλώς ήρθες στα Opsis DeskContainers" : "Welcome to Opsis DeskContainers";

        private static string OpsisWelcomeText() => OpsisGreek
            ? "ΚΑΛΩΣ ΗΡΘΕΣ ΣΤΑ OPSIS DESKCONTAINERS\r\n" +
              "---------------------------\r\n" +
              "• Σύρε αρχεία, φακέλους και συντομεύσεις πάνω σε ένα container για να τα ομαδοποιήσεις.\r\n" +
              "• Μενού: πάτα το εικονίδιο ☰ (πάνω αριστερά) για Options, νέα containers, portals και σημειώσεις.\r\n" +
              "• Μετονομασία: F2 πάνω από ένα container, Enter για αποθήκευση.\r\n" +
              "• Μάζεμα/άνοιγμα: διπλό κλικ στη γραμμή τίτλου.\r\n" +
              "• Λουκέτο (πάνω δεξιά): κλείδωσε το container στη θέση του.\r\n" +
              "• Αναζήτηση παντού: Ctrl + `.\r\n" +
              "• Δεξί κλικ στη γραμμή τίτλου → Customize (χρώμα, εικονίδια, κείμενο).\r\n" +
              "• Καρτέλες: ☰ → Enable Tabs On This Container.\r\n\r\n" +
              "Τα Opsis DeskContainers βασίζονται στο ανοιχτού κώδικα Opsis DeskContainers (MIT)."
            : "WELCOME TO OPSIS DESKCONTAINERS\r\n" +
              "---------------------------\r\n" +
              "• Drag files, folders and shortcuts onto a container to group them.\r\n" +
              "• Menu: click the ☰ icon (top-left) for Options, new containers, portals and notes.\r\n" +
              "• Rename: F2 over a container, Enter to save.\r\n" +
              "• Roll up/down: double-click the title bar.\r\n" +
              "• Padlock (top-right): lock the container in place.\r\n" +
              "• Search everything: Ctrl + `.\r\n" +
              "• Right-click the title bar → Customize (colour, icons, text).\r\n" +
              "• Tabs: ☰ → Enable Tabs On This Container.\r\n\r\n" +
              "Opsis DeskContainers is based on the open-source Opsis DeskContainers (MIT).";

        public static void RenameHoveredContainer()
        {
            if (System.Windows.Application.Current == null) return;
            foreach (var w in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>())
                if (w.IsMouseOver && w.Tag is string id && RenameStarters.TryGetValue(id, out var start)) { start(); return; }
        }


        // --- NEW: Centralized Presets ---
        private static readonly Dictionary<string, string> _standardPresets = new Dictionary<string, string>
        {
            { "Images", "*.jpg; *.jpeg; *.png; *.gif; *.bmp; *.webp" },
            { "Documents", "*.doc*; *.pdf; *.txt; *.rtf; *.xls*; *.ppt*" },
            { "Executables", "*.exe; *.bat; *.msi; *.cmd; *.ps1" },
            { "Archives", "*.zip; *.rar; *.7z; *.tar; *.gz" },
            { "Media", "*.mp3; *.wav; *.mp4; *.mkv; *.avi" },
            { "Hide System", ">*.tmp; >desktop.ini; >~$*" }
        };
        // -------------------------------

        // Temporary override flag for the "Screen Bound" button
        public static bool IsManualRepositioning = false;



        // --- WM_GETMINMAXINFO Implementation ---
        private const int WM_GETMINMAXINFO = 0x0024;

        private const int WM_SYSCOMMAND = 0x0112; // <--- NEW
        private const int SC_MAXIMIZE = 0xF030;   // <--- NEW

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }


        // Tracks temporary navigation paths for Portal Containers. Key: ContainerID, Value: CurrentPath
        private static Dictionary<string, string> _portalNavigationStates = new Dictionary<string, string>();

        // Track icon state to prevent GDI leaks from constant re-extraction
        // Key: FilePath
        // Value: (LastWriteTime of the .lnk/file, IsBroken state)
        private static Dictionary<string, (DateTime LastWrite, bool IsBroken)> _iconStates = new Dictionary<string, (DateTime, bool)>();

        private static dynamic _options;
        private static Dictionary<dynamic, PortalContainerManager> _portalContainers = new Dictionary<dynamic, PortalContainerManager>();

        // Stores heart TextBlock references for each container to enable efficient ContextMenu updates
        private static readonly Dictionary<dynamic, TextBlock> _heartTextBlocks = new Dictionary<dynamic, TextBlock>();

        //resize feedback
        private static Window _sizeFeedbackWindow;
        private static System.Windows.Threading.DispatcherTimer _hideTimer;

        // Add near other static fields
        private static TargetChecker _currentTargetChecker;

        #region Auto-Hide Containers Engine
        private static DispatcherTimer _masterAutoHideTimer;
        public static bool _areContainersAutoHidden = false;
        private static int _hideSequenceId = 0;

        public static void InitializeAutoHideTimer()
        {
            if (_masterAutoHideTimer == null)
            {
                _masterAutoHideTimer = new DispatcherTimer();
                _masterAutoHideTimer.Tick += (s, e) => ExecuteAutoHideSequence();
            }
            ResetAutoHideTimer();
        }

        public static void ResetAutoHideTimer()
        {
            if (_masterAutoHideTimer == null) return;
            _masterAutoHideTimer.Stop();

            if (SettingsManager.AutoHideContainers && SettingsManager.AutoHideTime > 0)
            {
                _masterAutoHideTimer.Interval = TimeSpan.FromSeconds(SettingsManager.AutoHideTime);
                _masterAutoHideTimer.Start();
            }
        }

        public static void WakeUpContainers()
        {
            _hideSequenceId++;
            _areContainersAutoHidden = false;
            ResetAutoHideTimer();

            var containers = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
            foreach (var container in containers)
            {
                if (container.Visibility == Visibility.Visible && container.Opacity >= 0.99) continue;

                // --- BUG FIX: Skip Individually Hidden Containers (Safe Extraction) ---
                string containerId = container.Tag?.ToString();
                if (!string.IsNullOrEmpty(containerId))
                {
                    var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                    if (currentContainer != null)
                    {
                        bool isIndividuallyHidden = false;
                        try
                        {
                            if (currentContainer is Newtonsoft.Json.Linq.JObject jContainer)
                                isIndividuallyHidden = jContainer["IsHidden"]?.ToString().ToLower() == "true";
                            else
                                isIndividuallyHidden = (currentContainer.IsHidden?.ToString() ?? "false").ToLower() == "true";
                        }
                        catch { }

                        if (isIndividuallyHidden) continue; // Leave it hidden in the tray
                    }
                }
                // ------------------------------------------------------------------

                // --- WPF 1-FRAME FLASH FIX ---
                // Set the local value to 0 FIRST, then drop the animation.
                // --- WPF 1-FRAME FLASH FIX ---
                // Set the local value to 0 FIRST, then drop the animation. 
                // This prevents WPF from snapping back to 1.0 for a single frame.
                container.Opacity = 0.0;
                container.BeginAnimation(UIElement.OpacityProperty, null);

                container.Visibility = Visibility.Visible;

                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(250),
                    FillBehavior = FillBehavior.Stop
                };

                fadeIn.Completed += (s, e) =>
                {
                    container.Opacity = 1.0;
                };

                container.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Containers faded in and restored.");
        }

        private static async void ExecuteAutoHideSequence()
        {
            _masterAutoHideTimer.Stop();
            if (_areContainersAutoHidden) return;

            int currentSequence = ++_hideSequenceId;

            bool isDialogOpen = System.Windows.Application.Current.Windows
                .OfType<System.Windows.Window>()
                .Any(w => w.Visibility == Visibility.Visible
                       && !(w is NonActivatingWindow)
                       && w.Width > 0
                       && !string.IsNullOrWhiteSpace(w.Title));

            if (isDialogOpen)
            {
                ResetAutoHideTimer();
                return;
            }

            var visibleContainers = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>()
                .Where(w => w.Visibility == Visibility.Visible && !IsExcludedFromAutoHide(w))
                .ToList();

            if (visibleContainers.Count == 0) return;
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Auto-Hide: Executing hide sequence.");

            if (SettingsManager.HideFlashEffect)
            {
                for (int i = 0; i < 5; i++)
                {
                    foreach (var win in visibleContainers) win.Opacity = 0.3;
                    await Task.Delay(30);
                    if (currentSequence != _hideSequenceId) return;

                    foreach (var win in visibleContainers) win.Opacity = 1.0;
                    await Task.Delay(30);
                    if (currentSequence != _hideSequenceId) return;
                }
            }

            var fadeOutAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new System.Windows.Duration(TimeSpan.FromMilliseconds(200))
            };

            foreach (var win in visibleContainers)
            {
                win.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOutAnim);
            }

            await Task.Delay(200);

            if (currentSequence != _hideSequenceId) return;

            foreach (var win in visibleContainers)
            {
                // --- WPF 1-FRAME FLASH FIX ---
                // Must hard-set to 0.0 BEFORE removing the animation precedence!
                win.Opacity = 0.0;
                win.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                win.Visibility = Visibility.Hidden;
            }

            _areContainersAutoHidden = true;
        }
        #endregion

        // Track containers currently in rollup/rolldown transition to prevent event conflicts
        private static readonly HashSet<string> _containersInTransition = new HashSet<string>();
        // Emergency cleanup timer to prevent permanently stuck transition states
        private static System.Windows.Threading.DispatcherTimer _transitionCleanupTimer;



        public static void ReloadContainers()
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Reloading all containers...");

                // 1. Life Support
                Window lifeSupport = MessageBoxesManager.CreateWaitWindow("Opsis DeskContainers", "Refreshing configuration...");
                lifeSupport.Show();
                System.Windows.Forms.Application.DoEvents();

                // 2. Clean up Old State
                if (TrayManager.Instance != null)
                {
                    TrayManager.Instance.ClearHiddenContainers(); // Ensure Tray is clean
                }

                var openContainers = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                foreach (var win in openContainers) win.Close();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                _heartTextBlocks.Clear();
                foreach (var portal in _portalContainers.Values) try { portal.Dispose(); } catch { }
                _portalContainers.Clear();
                ContainerDataManager.ContainerData?.Clear();
                _currentTargetChecker?.Stop();

                // 3. Re-Load
                _currentTargetChecker = new TargetChecker(1000);
                LoadAndCreateContainers(_currentTargetChecker);

                // Allow a brief moment for WPF to register the new windows
                System.Windows.Forms.Application.DoEvents();

                // ==================== DEBUG FIX START ====================
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Starting Hidden Container Registration...");

                if (ContainerDataManager.ContainerData != null)
                {
                    // Use 'dynamic' to bypass JValue compile errors
                    foreach (dynamic data in ContainerDataManager.ContainerData)
                    {
                        // A. Safe Title Extraction (Trim spaces)
                        string jsonTitle = (string)data.Title;
                        if (jsonTitle != null) jsonTitle = jsonTitle.Trim();

                        // B. Safe 'IsHidden' Extraction
                        // This handles: true (bool), "true" (string), 1 (int)
                        string rawHidden = (string)data.IsHidden?.ToString();
                        bool isHidden = false;
                        if (!string.IsNullOrEmpty(rawHidden))
                        {
                            bool.TryParse(rawHidden, out isHidden);
                        }

                        // C. Debug Log
                        if (isHidden)
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Container '{jsonTitle}' is marked HIDDEN in JSON. Searching for window...");

                            // D. Find Window
                            var containerWindow = System.Windows.Application.Current.Windows
                                .OfType<NonActivatingWindow>()
                                .FirstOrDefault(w => w.Title != null && w.Title.Trim() == jsonTitle);

                            if (containerWindow != null)
                            {
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"-> MATCH FOUND. Hiding '{jsonTitle}'");
                                TrayManager.AddHiddenContainer(containerWindow);
                            }
                            else
                            {
                                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"-> WARNING: Could not find open window for '{jsonTitle}'. Window list count: {System.Windows.Application.Current.Windows.Count}");
                            }
                        }
                    }
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, "ContainerDataManager.ContainerData is NULL after load.");
                }
                // ==================== DEBUG FIX END ====================

                lifeSupport.Close();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Containers reloaded successfully.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Error reloading containers: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error reloading containers: {ex.Message}", "Error");
            }
        }
  


        /// <summary>
        /// Helper to detect UNC Roots (e.g. \\Server or \\192.168.1.10).
        /// These technically aren't "Directories" in .NET, but act as Folders in Windows.
        /// </summary>
        private static bool IsUncRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Must start with \\ and NOT contain a drive letter (like C:\)
            if (path.StartsWith(@"\\") && !path.Contains(@":\"))
            {
                string clean = path.Substring(2);
                // If no more slashes (Server) or just one trailing slash (Server\), it's a root.
                int slash = clean.IndexOf('\\');
                return (slash < 0 || slash == clean.Length - 1);
            }
            return false;
        }
        // PORTAL FEATURE: Logic to switch folders
        public static void NavigatePortalContainer(dynamic container, string newPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath) || !System.IO.Directory.Exists(newPath)) return;

                string containerId = container.Id?.ToString();

                // --- BUG FIX: Pull Live Container for Accurate Base Path ---
                var liveContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId) ?? container;
                string basePath = liveContainer.Path?.ToString(); // The permanent "Home"

                // --- BUG FIX: Strict Path Normalization ---
                string normNew = newPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                string normBase = basePath?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) ?? "";

                // Update State
                if (string.Equals(normNew, normBase, StringComparison.OrdinalIgnoreCase))
                {
                    _portalNavigationStates.Remove(containerId); // We returned Home
                }
                else
                {
                    _portalNavigationStates[containerId] = normNew; // We are Deep
                }

                // Tell the Manager to switch
                var portalEntry = _portalContainers.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == containerId);
                if (portalEntry.Value != null)
                {
                    portalEntry.Value.NavigateTo(normNew);
                }

                // Update UI (Show/Hide Bar)
                var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                var win = windows.FirstOrDefault(w => w.Tag?.ToString() == containerId);
                if (win != null)
                {
                    RefreshPortalNavBar(win, liveContainer);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Portal Navigation Error: {ex.Message}");
            }
        }

        // PORTAL FEATURE: The UI Strip [ <  Path...  ⚓ ]
        // v2.5.4 .186: Precision Alignment (Anchor under Filter)
        public static void RefreshPortalNavBar(NonActivatingWindow containerWindow, dynamic container)
        {
            try
            {
                var border = containerWindow.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                if (dockPanel == null) return;

                // 1. Cleanup Old Bar(s) - Safe Multi-Remove
                var oldBars = dockPanel.Children.OfType<Grid>()
                    .Where(g => g.Tag?.ToString() == "PORTAL_NAV_BAR").ToList();
                foreach (var bar in oldBars) dockPanel.Children.Remove(bar);

                // 2. Check Logic
                if (container.ItemsType?.ToString() != "Portal") return;

                string containerId = container.Id?.ToString();

                // --- BUG FIX: Pull Live Container for Accurate Base Path ---
                var liveContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId) ?? container;
                string basePath = liveContainer.Path?.ToString();

                // Normalize paths to fix the "Bar won't disappear" bug
                string normBase = basePath?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) ?? "";
                string currentPath = _portalNavigationStates.ContainsKey(containerId) ? _portalNavigationStates[containerId] : normBase;
                string normCurrent = currentPath?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) ?? "";

                // Only show bar if we are NOT at home
                bool isNavigating = !string.Equals(normCurrent, normBase, StringComparison.OrdinalIgnoreCase);
                if (!isNavigating) return;

                // Path Logic - Generate the Breadcrumb text
                string displayPath = new System.IO.DirectoryInfo(normCurrent).Name;
                try
                {
                    if (normCurrent.StartsWith(normBase, StringComparison.OrdinalIgnoreCase))
                    {
                        string rootName = new System.IO.DirectoryInfo(normBase).Name;
                        string relativePart = normCurrent.Substring(normBase.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                        displayPath = string.IsNullOrEmpty(relativePart) ? rootName : System.IO.Path.Combine(rootName, relativePart);
                    }
                }
                catch { }

                // 3. Create UI (Styled & Aligned)
                Grid navGrid = new Grid
                {
                    Tag = "PORTAL_NAV_BAR",
                    Height = 24,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
                    // FIX: Right Margin 35 (30px Lock + 5px Gap) aligns the bar perfectly under the Filter Icon
                    Margin = new Thickness(5, 0, 35, 2)
                };

                navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); // Back Arrow
                navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Path Text
                navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); // Anchor Icon

                // [ < ] Back Button
                Button btnBack = new Button
                {
                    Content = Loc.T("‹"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = Loc.T("Go Up")
                };
                btnBack.Click += (s, e) => NavigatePortalContainer(liveContainer, System.IO.Directory.GetParent(normCurrent)?.FullName);

                // [ Path ] Label (Left Aligned)
                TextBlock lblPath = new TextBlock
                {
                    Text = displayPath,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = normCurrent,
                    Margin = new Thickness(5, 0, 5, 0)
                };

                // [ ⚓ ] Anchor Button
                Button btnSetBase = new Button
                {
                    Content = Loc.T("⚓"),
                    FontSize = 10,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.Orange,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = Loc.T("Set current view as the new Home for this container")
                };
                btnSetBase.Click += (s, e) =>
                {
                    if (MessageBoxesManager.ShowCustomYesNoMessageBox($"Set this folder as the new home?\n\n{normCurrent}", "Update Portal Container"))
                    {
                        UpdateContainerProperty(liveContainer, "Path", normCurrent, "Updated Portal Path");
                        _portalNavigationStates.Remove(containerId);
                        RefreshPortalNavBar(containerWindow, liveContainer);
                    }
                };

                navGrid.Children.Add(btnBack); Grid.SetColumn(btnBack, 0);
                navGrid.Children.Add(lblPath); Grid.SetColumn(lblPath, 1);
                navGrid.Children.Add(btnSetBase); Grid.SetColumn(btnSetBase, 2);

                // 4. Smart Insertion (Under Title)
                DockPanel.SetDock(navGrid, Dock.Top);

                int insertIndex = 0;
                bool titleFound = false;

                for (int i = 0; i < dockPanel.Children.Count; i++)
                {
                    if (dockPanel.Children[i] is Grid g &&
                        g.Children.OfType<TextBlock>().Any(tb => tb.Name == "ContainerLockIcon"))
                    {
                        insertIndex = i + 1;
                        titleFound = true;
                        break;
                    }
                }

                if (!titleFound) insertIndex = 1;
                if (insertIndex > dockPanel.Children.Count) insertIndex = dockPanel.Children.Count;

                dockPanel.Children.Insert(insertIndex, navGrid);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing Nav Bar: {ex.Message}");
            }
        }
        public static void ShowPortalToast(NonActivatingWindow win, string message)
        {
            try
            {
                var cborder = win.Content as Border;
                if (cborder == null) return;

                Grid mainGrid = cborder.Child as Grid;
                if (mainGrid == null)
                {
                    // Convert DockPanel to Grid on the fly to support overlays
                    var dp = cborder.Child as DockPanel;
                    if (dp != null)
                    {
                        mainGrid = new Grid();
                        cborder.Child = mainGrid;
                        mainGrid.Children.Add(dp);
                    }
                    else return;
                }

                // Find or create toast
                Border toast = mainGrid.Children.OfType<Border>().FirstOrDefault(b => b.Name == "PortalToast");
                TextBlock toastText;

                if (toast == null)
                {
                    toast = new Border
                    {
                        Name = "PortalToast",
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 30, 30, 30)),
                        CornerRadius = new CornerRadius(15),
                        Padding = new Thickness(15, 6, 15, 6),
                        Margin = new Thickness(0, 0, 0, 15),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        IsHitTestVisible = false, // Prevents blocking mouse clicks
                        Opacity = 0
                    };

                    // Add subtle shadow for a premium feel
                    toast.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = 0.5 };

                    toastText = new TextBlock
                    {
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center
                    };
                    toast.Child = toastText;
                    mainGrid.Children.Add(toast);
                }
                else
                {
                    toastText = toast.Child as TextBlock;
                }

                if (toastText != null) toastText.Text = message;

                // Stop any running animation
                toast.BeginAnimation(UIElement.OpacityProperty, null);

                // Create the Fade In -> Hold -> Fade Out animation (2 seconds total)
                var fadeInOut = new DoubleAnimationUsingKeyFrames();
                fadeInOut.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fadeInOut.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                fadeInOut.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500)))); // Hold for 1.5s
                fadeInOut.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000)), new QuadraticEase { EasingMode = EasingMode.EaseIn })); // Fade out

                toast.BeginAnimation(UIElement.OpacityProperty, fadeInOut);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error showing toast: {ex.Message}");
            }
        }

        /// <summary>
        /// Surgically reloads only specific containers by closing and recreating their windows
        /// Used by: ItemMoveDialog to prevent duplicate windows during move operations
        /// Category: Selective Window Management
        /// </summary>
        public static void ReloadSpecificContainers(dynamic sourceContainer, dynamic targetContainer)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Reloading specific containers: '{sourceContainer?.Title}' and '{targetContainer?.Title}'");

                // Get container IDs for identification
                string sourceContainerId = sourceContainer?.Id?.ToString();
                string targetContainerId = targetContainer?.Id?.ToString();

                // Check if we're about to close all containers (which would terminate the app)
                bool isSourceTargetSame = sourceContainerId == targetContainerId;
                int totalContainerCount = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().Count();
                int containersToClose = isSourceTargetSame ? 1 : 2;
                bool needsLifeSupport = (containersToClose >= totalContainerCount);

                Window lifeSupportWindow = null;

                // Show "life support" wait window if we're about to close all containers
                if (needsLifeSupport)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Creating life support window - about to close {containersToClose} of {totalContainerCount} total containers");

                    lifeSupportWindow = MessageBoxesManager.CreateWaitWindow("Opsis DeskContainers", "Refreshing containers, please wait...");
                    lifeSupportWindow.Show();
                }

                // Find and close only the specific container windows
                var allWindows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                var windowsToClose = new List<NonActivatingWindow>();

                foreach (var window in allWindows)
                {
                    string windowContainerId = window.Tag?.ToString();
                    if (!string.IsNullOrEmpty(windowContainerId) &&
                        (windowContainerId == sourceContainerId || windowContainerId == targetContainerId))
                    {
                        windowsToClose.Add(window);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            $"Marking container window for reload: {window.Title} (ID: {windowContainerId})");
                    }
                }

                // Close the specific windows
                foreach (var window in windowsToClose)
                {
                    try
                    {
                        window.Close();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            $"Closed container window: {window.Title}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                            $"Error closing window {window.Title}: {ex.Message}");
                    }
                }

                // Clean up portal container managers for the specific containers
                var portalContainersToRemove = _portalContainers.Keys
                    .Where(container => container?.Id?.ToString() == sourceContainerId || container?.Id?.ToString() == targetContainerId)
                    .ToList();

                foreach (var container in portalContainersToRemove)
                {
                    _portalContainers.Remove(container);
                }

                // Recreate only the specific containers with proper TargetChecker
                var containerData = ContainerDataManager.ContainerData;
                var containersToRecreate = containerData.Where(f =>
                    f.Id?.ToString() == sourceContainerId || f.Id?.ToString() == targetContainerId).ToList();

                foreach (var container in containersToRecreate)
                {
                    try
                    {
                        CreateContainer(container, _currentTargetChecker ?? new TargetChecker(1000));
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate,
                            $"Recreated container: {container.Title}");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                            $"Error recreating container {container.Title}: {ex.Message}");
                    }
                }

                // Close life support window if we used it
                if (lifeSupportWindow != null)
                {
                    try
                    {
                        lifeSupportWindow.Close();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            "Closed life support window");
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                            $"Error closing life support window: {ex.Message}");
                    }
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    "Specific container reload completed successfully");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error in ReloadSpecificContainers: {ex.Message}");
                throw;
            }
        }


        private static double GetDpiScaleFactor(Window window)
        {
            // Get the screen where the window is located based on its position
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)window.Left, (int)window.Top));

            // Use Graphics to get the screen's DPI
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                float dpiX = graphics.DpiX; // Horizontal DPI
                return dpiX / 96.0; // Standard DPI is 96, so scale factor = dpiX / 96
            }
        }
        private static void AdjustContainerPositionToScreen(NonActivatingWindow win)
        {
            // 1. CONTROL CHECK: 
            // If Auto-Reposition is OFF ... AND ... we are NOT manually forcing it -> EXIT.
            if (!SettingsManager.AllowAutoReposition && !IsManualRepositioning)
            {
                return;
            }

            // Get the DPI scale factor for the window
            double dpiScale = GetDpiScaleFactor(win);

            // Determine the screen based on the current position in pixels
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)(win.Left * dpiScale), (int)(win.Top * dpiScale)));

            // Get the screen's working area in pixels (excludes taskbars)
            var workingArea = screen.WorkingArea;

            // Convert window position and size from DIUs to pixels
            double winLeftPx = win.Left * dpiScale;
            double winTopPx = win.Top * dpiScale;
            double winWidthPx = win.Width * dpiScale;
            double winHeightPx = win.Height * dpiScale;

            // Calculate new position in pixels
            double newLeftPx = winLeftPx;
            double newTopPx = winTopPx;

            // Ensure the right edge doesn't exceed the working area's right boundary
            if (newLeftPx + winWidthPx > workingArea.Right)
            {
                newLeftPx = workingArea.Right - winWidthPx;
            }
            // Ensure the left edge isn't off-screen to the left
            if (newLeftPx < workingArea.Left)
            {
                newLeftPx = workingArea.Left;
            }

            // Ensure the bottom edge doesn't exceed the working area's bottom boundary
            if (newTopPx + winHeightPx > workingArea.Bottom)
            {
                newTopPx = workingArea.Bottom - winHeightPx;
            }
            // Ensure the top edge isn't off-screen to the top
            if (newTopPx < workingArea.Top)
            {
                newTopPx = workingArea.Top;
            }

            // Convert the adjusted position back to DIUs
            double newLeft = newLeftPx / dpiScale;
            double newTop = newTopPx / dpiScale;

            // Apply the new position if it has changed
            if (newLeft != win.Left || newTop != win.Top)
            {
                win.Left = newLeft;
                win.Top = newTop;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ImportExport, $"Adjusted container '{win.Title}' position to ({newLeft}, {newTop}) to fit within screen bounds.");
            }
            ContainerDataManager.SaveContainerData();
  
        }




        public static void ForceRepositionAllContainers()
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Manual maintenance: Forcing screen bound check.");

                // 1. Alter variable to allow execution
                IsManualRepositioning = true;

                // 2. Run the logic (it will now pass the check)
                foreach (var container in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>())
                {
                    AdjustContainerPositionToScreen(container);
                }

                ContainerDataManager.SaveContainerData();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Error, $"Force reposition failed: {ex.Message}");
            }
            finally
            {
                // 3. Return variable to neutral
                IsManualRepositioning = false;
            }
        }





        private static int _registryMonitorTickCount = 0;




        // Builds the heart ContextMenu for a container with consistent items and dynamic state
        // v2.5.4 .183: Swapped Tabs/Delete position for better UX safety
        private static ContextMenu BuildHeartContextMenu(dynamic container, bool showTabsOption = false)
        {
            // DEBUG: Log menu building
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                $"Building heart context menu for container '{container.Title}'");

            var menu = new ContextMenu();

            // --- AUTO-CLOSE TIMER ---
            System.Windows.Threading.DispatcherTimer menuTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };

            menuTimer.Tick += (s, e) =>
            {
                if (menu.IsOpen && !menu.IsMouseOver)
                {
                    menu.IsOpen = false;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Heart Menu auto-closed by timer");
                }
                menuTimer.Stop();
            };

            menu.Opened += (s, e) => { menuTimer.Start(); };
            menu.Closed += (s, e) => menuTimer.Stop();
            menu.MouseEnter += (s, e) => menuTimer.Stop();
            menu.MouseLeave += (s, e) => { menuTimer.Stop(); menuTimer.Start(); };
            // ------------------------

            // Settings: every DeskContainers setting lives in this submenu tree (see SettingsMenu.cs)
            menu.Items.Add(SettingsMenu.Build(menu));

            // Separator
            menu.Items.Add(new Separator());

            // New Container items
            var newContainerItem = new MenuItem { Header = Loc.T("New Container") };
            newContainerItem.Click += (s, e) =>
            {
                var mousePosition = System.Windows.Forms.Cursor.Position;
                CreateNewContainer("", "Data", mousePosition.X, mousePosition.Y);
            };
            menu.Items.Add(newContainerItem);

            var newPortalContainerItem = new MenuItem { Header = Loc.T("New Portal Container") };
            newPortalContainerItem.Click += (s, e) =>
            {
                var mousePosition = System.Windows.Forms.Cursor.Position;
                CreateNewContainer("New Portal Container", "Portal", mousePosition.X, mousePosition.Y);
            };
            menu.Items.Add(newPortalContainerItem);

            MenuItem newNoteContainerItem = new MenuItem { Header = Loc.T("New Note Container") };
            newNoteContainerItem.Click += (s, e) =>
            {
                var mousePosition = System.Windows.Forms.Cursor.Position;
                CreateNewContainer("", "Note", mousePosition.X, mousePosition.Y);
            };
            menu.Items.Add(newNoteContainerItem);

            menu.Items.Add(new Separator());

            // --- REORDERED: Tabs Option First ---
            // TABS FEATURE (Native Checkbox Logic)
            bool isDataContainer = container.ItemsType?.ToString() == "Data";
            if (isDataContainer)
            {
                bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";

                var enableTabsItem = new MenuItem
                {
                    Header = Loc.T("Enable Tabs On This Container"),
                    IsCheckable = true,   // Shows checkbox gutter
                    IsChecked = tabsEnabled // Visual checkmark
                };

                enableTabsItem.Click += (s, e) => ToggleContainerTabs(container);
                menu.Items.Add(enableTabsItem);

                // Separator AFTER tabs to separate it from the Delete option
                menu.Items.Add(new Separator());
            }

            // --- REORDERED: Delete Option Second ---
            // Delete this container
            var deleteThisContainer = new MenuItem { Header = Loc.T("Delete this Container") };
            deleteThisContainer.Click += (s, e) =>
            {
                bool result = MessageBoxesManager.ShowCustomMessageBoxForm();
                if (result == true)
                {
                    if (SettingsManager.ExportShortcutsOnContainerDeletion && container.ItemsType?.ToString() == "Data")
                    {
                        ExportAllIconsToDesktop(container, false);
                    }

                    BackupManager.BackupDeletedContainer(container);

                    DesktopOriginals.RestoreFor(Newtonsoft.Json.Linq.JToken.FromObject((object)container));   // the desktop icons come back, whatever happens next
                    string deletedId = container.Id?.ToString();
                    ContainerDataManager.ContainerData.Remove(container);
                    if (deletedId != null) ContainerDataManager.ContainerData.RemoveAll(f => f.Id?.ToString() == deletedId);   // by Id: reference equality can miss
                    _heartTextBlocks.Remove(container);

                    // --- BUG FIX: Avoid JObject HashCode Mutation ---
                    var targetPortal = _portalContainers.FirstOrDefault(kvp => kvp.Key?.Id?.ToString() == container.Id?.ToString());
                    if (targetPortal.Value != null)
                    {
                        targetPortal.Value.Dispose();
                        _portalContainers.Remove(targetPortal.Key);
                    }

                    ContainerDataManager.SaveContainerData();

                    var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                    var win = windows.FirstOrDefault(w => w.Tag?.ToString() == container.Id?.ToString());
                    if (win != null) win.Close();

                    UpdateAllHeartContextMenus();
                }
            };
            menu.Items.Add(deleteThisContainer);

            menu.Items.Add(new Separator());

            // Export/Import Group
            var exportItem = new MenuItem { Header = Loc.T("Export this Container") };
            exportItem.Click += (s, e) => BackupManager.ExportContainer(container);
            menu.Items.Add(exportItem);

            var importItem = new MenuItem { Header = Loc.T("Import a Container...") };
            importItem.Click += (s, e) => BackupManager.ImportContainer();
            menu.Items.Add(importItem);

            // Restore Container item
            var restoreItem = new MenuItem
            {
                Header = Loc.T("Restore Last Deleted Container"),
                Visibility = BackupManager.IsRestoreAvailable ? Visibility.Visible : Visibility.Collapsed
            };
            restoreItem.Click += (s, e) => BackupManager.RestoreLastDeletedContainer();
            menu.Items.Add(restoreItem);

            // Separator
            menu.Items.Add(new Separator());

            // Exit item
            var exitItem = new MenuItem { Header = Loc.T("Exit") };
            exitItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
            menu.Items.Add(exitItem);

            return menu;
        }



      
        /// <summary>
        /// Centralized method to attach the standard Context Menu to an icon.
        /// Layout: Edit/Move/Remove -> Copy -> Admin -> Path
        /// Includes LIVE data lookup to ensure Checkable items stay synced.
        /// </summary>
        public static void AttachIconContextMenu(StackPanel sp, dynamic item, dynamic container, NonActivatingWindow window)
        {
            try
            {
                // Initial snapshot for static properties (Path, Folder status)
                IDictionary<string, object> iconDict = item is IDictionary<string, object> dict ?
                    dict : ((JObject)item).ToObject<IDictionary<string, object>>();

                string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
                bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];

                // HELPER: Function to find the LIVE item in memory (Handles Tabs vs Main)
                // We need this because 'item' becomes stale after an update.
                Func<dynamic> GetLiveItem = () =>
                {
                    try
                    {
                        string containerId = container.Id?.ToString();
                        var liveContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                        if (liveContainer == null) return null;

                        JArray itemsArray = liveContainer.Items as JArray ?? new JArray();

                        // Handle Tabs
                        bool tabsEnabled = liveContainer.TabsEnabled?.ToString().ToLower() == "true";
                        if (tabsEnabled)
                        {
                            var tabs = liveContainer.Tabs as JArray ?? new JArray();
                            int currentTabIndex = Convert.ToInt32(liveContainer.CurrentTab?.ToString() ?? "0");
                            if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                            {
                                var activeTab = tabs[currentTabIndex] as JObject;
                                itemsArray = activeTab?["Items"] as JArray ?? itemsArray;
                            }
                        }

                        // Find specific item by filename
                        return itemsArray.FirstOrDefault(i => string.Equals(
                            System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                            System.IO.Path.GetFullPath(filePath),
                            StringComparison.OrdinalIgnoreCase));
                    }
                    catch { return null; }
                };

                ContextMenu iconContextMenu = new ContextMenu();

                // --- GROUP 1: MANIPULATION ---
                MenuItem miEdit = new MenuItem { Header = Loc.T("Edit...") };
                MenuItem miMove = new MenuItem { Header = Loc.T("Move...") };
                MenuItem miRemove = new MenuItem { Header = Loc.T("Remove") };

                iconContextMenu.Items.Add(miEdit);
                iconContextMenu.Items.Add(miMove);
                iconContextMenu.Items.Add(miRemove);

                bool isSpacer = filePath != null && filePath.StartsWith("INTERNAL_BLANK_");

                // --- GROUP 2: CLIPBOARD ---
                Separator sepClipboard = new Separator();
                MenuItem miCopyItem = new MenuItem { Header = Loc.T("Copy Item") };

                iconContextMenu.Items.Add(sepClipboard);
                iconContextMenu.Items.Add(miCopyItem);

                if (isSpacer)
                {
                    sepClipboard.Visibility = Visibility.Collapsed;
                    miCopyItem.Visibility = Visibility.Collapsed;
                }

                // --- GROUP 3: EXECUTION (Conditional) ---
                bool isEligibleForAdmin = !isFolder && !string.IsNullOrEmpty(filePath) &&
                    (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk" || System.IO.Path.GetExtension(filePath).ToLower() == ".exe");

                MenuItem miAlwaysAdmin = null;
                MenuItem miRunAsDifferentUser = null;
                MenuItem miAlwaysRunAsDifferentUser = null;

                if (isEligibleForAdmin)
                {
                    iconContextMenu.Items.Add(new Separator());

                    MenuItem miRunAsAdmin = new MenuItem { Header = Loc.T("Run as administrator") };
                    miAlwaysAdmin = new MenuItem
                    {
                        Header = Loc.T("Always run as administrator"),
                        IsCheckable = true
                    };

                    iconContextMenu.Items.Add(miRunAsAdmin);
                    iconContextMenu.Items.Add(miAlwaysAdmin);

                 
                    // Run as Admin Logic
                    miRunAsAdmin.Click += (s, e) => {
                        string target = Utility.GetShortcutTarget(filePath);
                        string args = Utility.GetShortcutArguments(filePath); // Extract arguments

                        ProcessStartInfo psi = new ProcessStartInfo { FileName = target, UseShellExecute = true, Verb = "runas" };

                        if (!string.IsNullOrEmpty(args))
                        {
                            psi.Arguments = args; // Pass arguments to admin launch
                        }

                        if (System.IO.File.Exists(target))
                        {
                            string wd = System.IO.Path.GetDirectoryName(target);
                            if (!string.IsNullOrEmpty(wd)) psi.WorkingDirectory = wd;
                        }
                        try { Process.Start(psi); }
                        catch (Exception ex)
                        {
                            MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error running as admin: {ex.Message}", "Error");
                        }
                    };

                    // START- HERE

                    // Toggle Always Admin Logic
                    miAlwaysAdmin.Click += (sender, e) => {
                        var liveItem = GetLiveItem();
                        if (liveItem != null)
                        {
                            bool newVal = !Convert.ToBoolean(liveItem["AlwaysRunAsAdmin"] ?? false);
                            liveItem["AlwaysRunAsAdmin"] = newVal;
                            miAlwaysAdmin.IsChecked = newVal;

                            // Mutually exclusive: Uncheck Different User if Admin is checked
                            if (newVal)
                            {
                                liveItem["AlwaysRunAsDifferentUser"] = false;
                                if (miAlwaysRunAsDifferentUser != null) miAlwaysRunAsDifferentUser.IsChecked = false;
                            }

                            ContainerDataManager.SaveContainerData();
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Toggled AlwaysRunAsAdmin to {newVal} for {filePath}");
                        }
                    };
                }

                // --- GROUP 4: PATH / TARGET ---
                Separator sepPath = new Separator();
                iconContextMenu.Items.Add(sepPath);

                MenuItem miCopyPathRoot = new MenuItem { Header = Loc.T("Copy path") };
                MenuItem miCopyFolder = new MenuItem { Header = Loc.T("Folder path") };
                MenuItem miCopyFullPath = new MenuItem { Header = Loc.T("Full path") };

                miCopyPathRoot.Items.Add(miCopyFolder);
                miCopyPathRoot.Items.Add(miCopyFullPath);

                MenuItem miFindTarget = new MenuItem { Header = Loc.T("Open target folder...") };

                iconContextMenu.Items.Add(miCopyPathRoot);
                iconContextMenu.Items.Add(miFindTarget);

                if (isSpacer)
                {
                    sepPath.Visibility = Visibility.Collapsed;
                    miCopyPathRoot.Visibility = Visibility.Collapsed;
                    miFindTarget.Visibility = Visibility.Collapsed;
                }

                // --- EVENT HANDLERS ---
                miEdit.Click += (s, e) => EditItem(item, container, window);
                miMove.Click += (s, e) => ItemMoveDialog.ShowMoveDialog(item, container, window.Dispatcher);

                miRemove.Click += (s, e) =>
                {
                    try
                    {
                        // Use the LIVE lookup to find the item to remove
                        var liveItem = GetLiveItem(); // Returns the JObject from the array

                        // We need the Array itself to remove the item
                        string containerId = container.Id?.ToString();
                        var liveContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);

                        if (liveContainer != null && liveItem != null)
                        {
                            // Logic to locate the array containing liveItem
                            JArray targetArray = liveContainer.Items as JArray; // Default to main

                            bool tabsEnabled = liveContainer.TabsEnabled?.ToString().ToLower() == "true";
                            if (tabsEnabled)
                            {
                                var tabs = liveContainer.Tabs as JArray;
                                int tabIdx = Convert.ToInt32(liveContainer.CurrentTab?.ToString() ?? "0");
                                if (tabs != null && tabIdx < tabs.Count)
                                {
                                    targetArray = tabs[tabIdx]["Items"] as JArray;
                                }
                            }

                            if (targetArray != null)
                            {
                                targetArray.Remove(liveItem);
                                ContainerDataManager.SaveContainerData();
                                var wp = VisualTreeHelper.GetParent(sp) as WrapPanel;
                                if (wp != null) wp.Children.Remove(sp);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error removing: {ex.Message}");
                    }
                };

                miCopyItem.Click += (s, e) => CopyPasteManager.CopyItem(item, container);

                miFindTarget.Click += (s, e) => {
                    string target = Utility.GetShortcutTarget(filePath);
                    if (!string.IsNullOrEmpty(target) && (System.IO.File.Exists(target) || System.IO.Directory.Exists(target)))
                        Process.Start("explorer.exe", $"/select,\"{target}\"");
                };

                miCopyFolder.Click += (s, e) => {
                    string target = Utility.GetShortcutTarget(filePath);
                    if (!string.IsNullOrEmpty(target)) Clipboard.SetText(System.IO.Path.GetDirectoryName(target));
                };
                miCopyFullPath.Click += (s, e) => {
                    string target = Utility.GetShortcutTarget(filePath);
                    if (!string.IsNullOrEmpty(target)) Clipboard.SetText(target);
                };

                // --- DYNAMIC UPDATES (On Open) ---
                MenuItem miSendToDesktop = null;
       

                iconContextMenu.Opened += (s, e) => {
                    // Fetch live item ONCE per open to prevent variable shadowing errors
                    var currentLiveItem = GetLiveItem();
                    bool isAlwaysDiffUser = currentLiveItem != null && Convert.ToBoolean(currentLiveItem["AlwaysRunAsDifferentUser"] ?? false);

                    bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                    // Clear dynamic items
                    if (miSendToDesktop != null && iconContextMenu.Items.Contains(miSendToDesktop))
                        iconContextMenu.Items.Remove(miSendToDesktop);
                    if (miRunAsDifferentUser != null && iconContextMenu.Items.Contains(miRunAsDifferentUser))
                        iconContextMenu.Items.Remove(miRunAsDifferentUser);
                    if (miAlwaysRunAsDifferentUser != null && iconContextMenu.Items.Contains(miAlwaysRunAsDifferentUser))
                        iconContextMenu.Items.Remove(miAlwaysRunAsDifferentUser);

                    // 1. Send to Desktop (Ctrl only, Skip for spacers)
                    if (isCtrl && !isSpacer)
                    {
                        miSendToDesktop = new MenuItem { Header = Loc.T("Send to Desktop") };
                        miSendToDesktop.Click += (sender, args) => CopyPasteManager.SendToDesktop(item);
                        int idxCopy = iconContextMenu.Items.IndexOf(miCopyItem);
                        if (idxCopy != -1) iconContextMenu.Items.Insert(idxCopy + 1, miSendToDesktop);
                    }

                    // 2. Different User Options (Ctrl OR if setting is already checked)
                    if (isEligibleForAdmin && (isCtrl || isAlwaysDiffUser))
                    {
                        miRunAsDifferentUser = new MenuItem { Header = Loc.T("Run as different user...") };
                        miAlwaysRunAsDifferentUser = new MenuItem { Header = Loc.T("Always run as different user"), IsCheckable = true };

                        miRunAsDifferentUser.Click += (sender, args) => {
                            string target = Utility.GetShortcutTarget(filePath);
                            string argsStr = Utility.GetShortcutArguments(filePath);
                            ProcessStartInfo psi = new ProcessStartInfo { FileName = target, UseShellExecute = true, Verb = "runasuser" };
                            if (!string.IsNullOrEmpty(argsStr)) psi.Arguments = argsStr;
                            if (System.IO.File.Exists(target))
                            {
                                string wd = System.IO.Path.GetDirectoryName(target);
                                if (!string.IsNullOrEmpty(wd)) psi.WorkingDirectory = wd;
                            }
                            try { Process.Start(psi); }
                            catch (Exception ex) { if (!ex.Message.Contains("canceled")) LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error running as different user: {ex.Message}"); }
                        };

                        miAlwaysRunAsDifferentUser.Click += (sender, args) => {
                            var liveItem = GetLiveItem();
                            if (liveItem != null)
                            {
                                bool newVal = !Convert.ToBoolean(liveItem["AlwaysRunAsDifferentUser"] ?? false);
                                liveItem["AlwaysRunAsDifferentUser"] = newVal;
                                miAlwaysRunAsDifferentUser.IsChecked = newVal;

                                // Mutually exclusive: Uncheck Admin if Different User is checked
                                if (newVal && miAlwaysAdmin != null)
                                {
                                    liveItem["AlwaysRunAsAdmin"] = false;
                                    miAlwaysAdmin.IsChecked = false;
                                }
                                ContainerDataManager.SaveContainerData();
                            }
                        };

                        int idxAdmin = iconContextMenu.Items.IndexOf(miAlwaysAdmin);
                        if (idxAdmin != -1)
                        {
                            iconContextMenu.Items.Insert(idxAdmin + 1, miRunAsDifferentUser);
                            iconContextMenu.Items.Insert(idxAdmin + 2, miAlwaysRunAsDifferentUser);
                        }
                    }

                    // 3. REFRESH CHECK STATES
                    if (currentLiveItem != null)
                    {
                        if (miAlwaysAdmin != null)
                            miAlwaysAdmin.IsChecked = Convert.ToBoolean(currentLiveItem["AlwaysRunAsAdmin"] ?? false);

                        if (miAlwaysRunAsDifferentUser != null && iconContextMenu.Items.Contains(miAlwaysRunAsDifferentUser))
                            miAlwaysRunAsDifferentUser.IsChecked = Convert.ToBoolean(currentLiveItem["AlwaysRunAsDifferentUser"] ?? false);
                    }
                };


                sp.ContextMenu = iconContextMenu;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error attaching context menu: {ex.Message}");
            }
        }





        // Updates all heart ContextMenus across all containers using stored TextBlock references
        public static void UpdateAllHeartContextMenus()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var entry in _heartTextBlocks)
                {
                    var container = entry.Key;
                    var heart = entry.Value;
                    if (heart != null)
                    {
                        // heart.ContextMenu = BuildHeartContextMenu(container);
                        heart.ContextMenu = BuildHeartContextMenu(container, false); // Normal menu by default
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Updated heart ContextMenu for container '{container.Title}'");
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Skipped update for container '{container.Title}': heart TextBlock is null");
                    }
                }
            });
        }

        private static void CreateWebLinkShortcut(string targetUrl, string shortcutPath, string displayName)
        {
            try
            {
                // For web links, create a .url file instead of .lnk file
                // Change the extension to .url if it's not already
                string urlFilePath = shortcutPath;
                if (System.IO.Path.GetExtension(shortcutPath).ToLower() == ".lnk")
                {
                    urlFilePath = System.IO.Path.ChangeExtension(shortcutPath, ".url");
                }

                // Create a .url file content
                string urlFileContent = $"[InternetShortcut]\r\nURL={targetUrl}\r\nIconIndex=0\r\n";

                // Write the .url file
                System.IO.File.WriteAllText(urlFilePath, urlFileContent, System.Text.Encoding.ASCII);

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Created web link URL file: {urlFilePath} -> {targetUrl}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling, $"Error creating web link shortcut {shortcutPath}: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// Exports all icons from a Data container to the desktop
        /// </summary>
        /// <param name="container">The container to export icons from</param>
        /// <param name="showConfirmation">If true, shows a message box on completion. False for silent automation.</param>
        private static void ExportAllIconsToDesktop(dynamic container, bool showConfirmation = true)
        {
            try
            {
                // Verify this is a Data container
                if (container.ItemsType?.ToString() != "Data")
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        $"Export all icons attempted on non-Data container: {container.ItemsType}");
                    return;
                }

                int exportedCount = 0;
                int totalCount = 0;

                // Check if container has tabs
                bool hasTabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";

                if (hasTabsEnabled && container.Tabs != null)
                {
                    // Handle tabbed container - export from all tabs
                    var tabs = container.Tabs as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();

                    foreach (var tab in tabs)
                    {
                        var tabObj = tab as Newtonsoft.Json.Linq.JObject;
                        var tabItems = tabObj?["Items"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();

                        foreach (var item in tabItems)
                        {
                            totalCount++;
                            try
                            {
                                CopyPasteManager.SendToDesktop(item);
                                exportedCount++;
                            }
                            catch (Exception itemEx)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                                    $"Error exporting item to desktop: {itemEx.Message}");
                            }
                        }
                    }
                }
                else
                {
                    // Handle regular container (no tabs)
                    var items = container.Items as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();

                    foreach (var item in items)
                    {
                        totalCount++;
                        try
                        {
                            CopyPasteManager.SendToDesktop(item);
                            exportedCount++;
                        }
                        catch (Exception itemEx)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                                $"Error exporting item to desktop: {itemEx.Message}");
                        }
                    }
                }

                // Show result message after a brief delay to allow desktop refresh
                string resultMessage = $"Exported {exportedCount} of {totalCount} icons to desktop.";
                if (exportedCount != totalCount)
                {
                    resultMessage += $" {totalCount - exportedCount} items failed to export.";
                }

                // Use DispatcherTimer for UI thread delay
                var delayTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1700) // 1.7 second delay
                };
                delayTimer.Tick += (s, e) =>
                {
                    delayTimer.Stop();

                    // FIX: Check flag before showing message
                    if (showConfirmation)
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(resultMessage, "Export Complete");
                    }
                };
                delayTimer.Start();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Export all icons completed for container '{container.Title}': {exportedCount}/{totalCount} successful");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error in ExportAllIconsToDesktop: {ex.Message}");

                // Always show errors, even in silent mode, so user knows why it failed
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error exporting icons: {ex.Message}", "Export Error");
            }
        }

        /// <summary>
        /// Extracts URL from different browser drop data formats
        /// </summary>
        private static string ExtractUrlFromDropData(IDataObject dataObject)
        {
            try
            {
                // Try UniformResourceLocator format first (most browsers)
                if (dataObject.GetDataPresent("UniformResourceLocator"))
                {
                    object urlData = dataObject.GetData("UniformResourceLocator");
                    if (urlData is System.IO.MemoryStream stream)
                    {
                        byte[] bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, (int)stream.Length);
                        string url = System.Text.Encoding.ASCII.GetString(bytes).Trim('\0');
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                            $"Extracted URL from UniformResourceLocator: {url}");
                        return url;
                    }
                }

                // Try Firefox format
                if (dataObject.GetDataPresent("text/x-moz-url"))
                {
                    string mozUrl = dataObject.GetData("text/x-moz-url") as string;
                    if (!string.IsNullOrEmpty(mozUrl))
                    {
                        string[] parts = mozUrl.Split('\n');
                        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                                $"Extracted URL from text/x-moz-url: {parts[0]}");
                            return parts[0].Trim();
                        }
                    }
                }

                // Try HTML format  
                if (dataObject.GetDataPresent(DataFormats.Html))
                {
                    string html = dataObject.GetData(DataFormats.Html) as string;
                    if (!string.IsNullOrEmpty(html))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(html, @"href=['""]([^'""]+)['""]");
                        if (match.Success)
                        {
                            string url = match.Groups[1].Value;
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                                $"Extracted URL from HTML: {url}");
                            return url;
                        }
                    }
                }

                // Try plain text format (last resort)
                if (dataObject.GetDataPresent(DataFormats.Text))
                {
                    string text = dataObject.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrEmpty(text) && IsValidWebUrl(text.Trim()))
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                            $"Extracted URL from text: {text.Trim()}");
                        return text.Trim();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error extracting URL from drop data: {ex.Message}");
                return null;
            }
        }

        ///// <summary>
        ///// Validates if a string is a valid web URL
        ///// </summary>
        //private static bool IsValidWebUrl(string url)
        //{
        //    if (string.IsNullOrWhiteSpace(url)) return false;

        //    return Uri.TryCreate(url, UriKind.Absolute, out Uri validUri) &&
        //           (validUri.Scheme == Uri.UriSchemeHttp || validUri.Scheme == Uri.UriSchemeHttps ||
        //            validUri.Scheme.Equals("steam", StringComparison.OrdinalIgnoreCase));
        //}


        /// <summary>
        /// Validates if a string is a valid web URL
        /// </summary>
        private static bool IsValidWebUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // Accept ANY valid URI scheme except local files when dropping. 
            // This allows spotify:, steam://, etc., to be successfully created as shortcuts.
            return Uri.TryCreate(url, UriKind.Absolute, out Uri validUri) &&
                   validUri.Scheme != Uri.UriSchemeFile;
        }



        /// <summary>
        /// Adds a URL shortcut to the container from dropped URL
        /// </summary>
        private static void AddUrlShortcutToContainer(string url, dynamic container, WrapPanel wrapPanel)
        {
            try
            {
                // Create shortcuts directory if it doesn't exist
                if (!System.IO.Directory.Exists("Shortcuts"))
                {
                    System.IO.Directory.CreateDirectory("Shortcuts");
                }

                // Generate shortcut name from URL
                Uri uri = new Uri(url);
                string displayName = uri.Host.Replace("www.", "");
                string baseShortcutName = System.IO.Path.Combine("Shortcuts", $"{displayName}.url");
                string shortcutPath = baseShortcutName;

                // Ensure unique filename
                int counter = 1;
                while (System.IO.File.Exists(shortcutPath))
                {
                    shortcutPath = System.IO.Path.Combine("Shortcuts", $"{displayName} ({counter++}).url");
                }

                // Create the URL shortcut file  
                CreateWebLinkShortcut(url, shortcutPath, displayName);

                // Create item data for the container
                dynamic newItem = new System.Dynamic.ExpandoObject();
                IDictionary<string, object> newItemDict = newItem;
                newItemDict["Filename"] = shortcutPath;
                newItemDict["IsFolder"] = false;
                newItemDict["IsLink"] = true;
                newItemDict["IsNetwork"] = false;
                newItemDict["DisplayName"] = displayName;
                newItemDict["AlwaysRunAsAdmin"] = false;

                // Find the container in ContainerDataManager.ContainerData and update it properly
                string containerId = container.Id?.ToString();
                int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);

                if (containerIndex >= 0)
                {
                    // Get the actual container from ContainerDataManager.ContainerData
                    dynamic actualContainer = ContainerDataManager.ContainerData[containerIndex];
                    var actualItems = actualContainer.Items as JArray ?? new JArray();

                    // Handle tabs if enabled
                    bool tabsEnabled = actualContainer.TabsEnabled?.ToString().ToLower() == "true";
                    JArray targetList = actualItems;

                    if (tabsEnabled)
                    {
                        var tabs = actualContainer.Tabs as JArray ?? new JArray();
                        int currentTab = Convert.ToInt32(actualContainer.CurrentTab?.ToString() ?? "0");

                        if (currentTab >= 0 && currentTab < tabs.Count)
                        {
                            var activeTab = tabs[currentTab] as JObject;
                            if (activeTab != null)
                            {
                                var tabItems = activeTab["Items"] as JArray ?? new JArray();
                                tabItems.Add(JObject.FromObject(newItem));
                                targetList = tabItems; // Mark as target for ordering
                            }
                        }
                    }
                    else
                    {
                        actualItems.Add(JObject.FromObject(newItem));
                    }

                    // Set DisplayOrder
                    newItemDict["DisplayOrder"] = targetList.Count - 1;

                    // Save container data BEFORE creating the icon
                    ContainerDataManager.SaveContainerData();

                    // Add icon to UI using ContainerManager.AddIcon for proper sizing
                    ContainerManager.AddIcon(newItem, wrapPanel);
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                        $"Could not find container with ID {containerId} in ContainerDataManager.ContainerData");
                }

                // Add event handlers to the new icon
                StackPanel sp = wrapPanel.Children[wrapPanel.Children.Count - 1] as StackPanel;
                if (sp != null)
                {
                    ClickEventAdder(sp, shortcutPath, false);

                    // FIX: Use the FULL Context Menu (Edit, Move, Copy, etc.)
                    // We need to find the parent window to pass it to the menu builder
                    NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(wrapPanel);
                    if (parentWindow != null)
                    {
                        AttachIconContextMenu(sp, newItem, container, parentWindow);
                    }
                    else
                    {
                        // Fallback (rare) - Should not happen if UI is loaded
                        CreateBasicContextMenu(sp, newItem, shortcutPath);
                    }
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    $"Successfully added URL shortcut: {displayName} -> {url}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error adding URL shortcut to container: {ex.Message}");
            }
        }





        /// <summary>
        /// Gets the next display order for new items
        /// </summary>
        private static int GetNextDisplayOrder(dynamic container)
        {
            try
            {
                var items = container.Items as JArray ?? new JArray();
                int maxOrder = items.Count > 0 ? items.Max(i => i["DisplayOrder"]?.Value<int>() ?? 0) : -1;
                return maxOrder + 1;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Creates basic context menu for URL shortcuts
        /// </summary>
        private static void CreateBasicContextMenu(StackPanel sp, dynamic item, string filePath)
        {
            try
            {
                ContextMenu iconContextMenu = new ContextMenu();
                MenuItem miRemove = new MenuItem { Header = Loc.T("Remove") };

        
                miRemove.Click += (sender, e) =>
                {
                    try
                    {
                        NonActivatingWindow parentWin = FindVisualParent<NonActivatingWindow>(sp);
                        if (parentWin != null)
                        {
                            string containerId = parentWin.Tag?.ToString();
                            var container = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == containerId);
                            if (container != null)
                            {
                                var items = container.Items as JArray;
                                var itemToRemove = items?.FirstOrDefault(i => i["Filename"]?.ToString() == filePath);
                                if (itemToRemove != null)
                                {
                                    items.Remove(itemToRemove);
                                    ContainerDataManager.SaveContainerData();

                                    WrapPanel wrapPanel = FindVisualParent<WrapPanel>(sp);
                                    wrapPanel?.Children.Remove(sp);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                            $"Error removing URL shortcut: {ex.Message}");
                    }
                };




                iconContextMenu.Items.Add(miRemove);

                bool alwaysAdmin = Convert.ToBoolean(item["AlwaysRunAsAdmin"] ?? false);
                MenuItem miAlwaysAdmin = new MenuItem
                {
                    Header = Loc.T("Always run as administrator"),
                    IsCheckable = true,
                    IsChecked = alwaysAdmin
                };
                miAlwaysAdmin.Click += (sender, e) => {
                    try
                    {
                        NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(sp);
                        if (parentWindow != null)
                        {
                            string containerId = parentWindow.Tag?.ToString();
                            if (!string.IsNullOrEmpty(containerId))
                            {
                                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                                if (currentContainer != null && currentContainer.ItemsType?.ToString() == "Data")
                                {
                                    var items = currentContainer.Items as JArray ?? new JArray();
                                    bool tabsEnabled = currentContainer.TabsEnabled?.ToString().ToLower() == "true";
                                    if (tabsEnabled)
                                    {
                                        var tabs = currentContainer.Tabs as JArray ?? new JArray();
                                        int currentTabIndex = Convert.ToInt32(currentContainer.CurrentTab?.ToString() ?? "0");
                                        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                                        {
                                            var currentTab = tabs[currentTabIndex] as JObject;
                                            items = currentTab?["Items"] as JArray ?? items;
                                        }
                                    }
                                    var matchingItem = items.FirstOrDefault(i => string.Equals(
                                        System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                                        System.IO.Path.GetFullPath(filePath),
                                        StringComparison.OrdinalIgnoreCase));
                                    if (matchingItem != null)
                                    {
                                        bool currentValue = Convert.ToBoolean(matchingItem["AlwaysRunAsAdmin"] ?? false);
                                        bool newValue = !currentValue;
                                        matchingItem["AlwaysRunAsAdmin"] = newValue;
                                        miAlwaysAdmin.IsChecked = newValue;
                                        ContainerDataManager.SaveContainerData();
                                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Toggled AlwaysRunAsAdmin to {newValue} for item: {filePath}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error toggling AlwaysRunAsAdmin for {filePath}: {ex.Message}");
                    }
                };
                // Refresh IsChecked on menu open
                iconContextMenu.Opened += (s, ev) => {
                    try
                    {
                        NonActivatingWindow parentWindow = FindVisualParent<NonActivatingWindow>(sp);
                        if (parentWindow != null)
                        {
                            string containerId = parentWindow.Tag?.ToString();
                            if (!string.IsNullOrEmpty(containerId))
                            {
                                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                                if (currentContainer != null)
                                {
                                    var items = currentContainer.Items as JArray ?? new JArray();
                                    bool tabsEnabled = currentContainer.TabsEnabled?.ToString().ToLower() == "true";
                                    if (tabsEnabled)
                                    {
                                        var tabs = currentContainer.Tabs as JArray ?? new JArray();
                                        int currentTabIndex = Convert.ToInt32(currentContainer.CurrentTab?.ToString() ?? "0");
                                        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                                        {
                                            var currentTab = tabs[currentTabIndex] as JObject;
                                            items = currentTab?["Items"] as JArray ?? items;
                                        }
                                    }
                                    var matchingItem = items.FirstOrDefault(i => string.Equals(
                                        System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                                        System.IO.Path.GetFullPath(filePath),
                                        StringComparison.OrdinalIgnoreCase));
                                    if (matchingItem != null)
                                    {
                                        miAlwaysAdmin.IsChecked = Convert.ToBoolean(matchingItem["AlwaysRunAsAdmin"] ?? false);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Error refreshing AlwaysRunAsAdmin IsChecked for {filePath}: {ex.Message}");
                    }
                };
                iconContextMenu.Items.Add(miAlwaysAdmin);


          sp.ContextMenu = iconContextMenu;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error creating context menu: {ex.Message}");
            }
        }
             
        public static List<dynamic> GetContainerData()
        {
            return ContainerDataManager.ContainerData;
        }

                /// <summary>
        /// Public access to portal containers dictionary for CustomizeContainerForm
        /// </summary>
        public static Dictionary<dynamic, PortalContainerManager> GetPortalContainers()
        {
            return _portalContainers;
        }




 
        /// <summary>
        /// Updates the filter history for a container using LRU (Least Recently Used) logic.
        /// Max 5 items. Duplicates move to top.
        /// </summary>
        private static void UpdateFilterHistory(dynamic container, string newFilter)
        {
            if (string.IsNullOrWhiteSpace(newFilter)) return;

            // Don't save if it matches a Standard Preset value exactly
            if (_standardPresets.ContainsValue(newFilter)) return;

            try
            {
                // 1. GET FRESH DATA (Fixes the "Replace" bug)
                // We cannot rely on the 'container' parameter because it might be stale 
                // (referencing the state from when the window opened).
                string containerId = container.Id?.ToString();
                if (string.IsNullOrEmpty(containerId)) return;

                int index = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (index < 0) return;

                // Grab the LIVE object from the master list
                dynamic liveContainer = ContainerDataManager.ContainerData[index];

                // 2. Convert to Dictionary for modification
                IDictionary<string, object> containerDict = liveContainer is IDictionary<string, object> dict
                    ? dict
                    : ((JObject)liveContainer).ToObject<IDictionary<string, object>>();

                // 3. Get existing history
                JArray history;
                if (containerDict.ContainsKey("FilterHistory") && containerDict["FilterHistory"] is JArray existingArr)
                {
                    history = existingArr;
                }
                else
                {
                    history = new JArray();
                }

                // 4. Update Logic (LRU)
                // Remove existing if present (to bump to top)
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(history[i].ToString(), newFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        history.RemoveAt(i);
                    }
                }

                // Insert at top
                history.Insert(0, newFilter);

                // Cap at 5 items
                while (history.Count > 5)
                {
                    history.RemoveAt(history.Count - 1);
                }

                // 5. Save back to Master List
                containerDict["FilterHistory"] = history;
                ContainerDataManager.ContainerData[index] = JObject.FromObject(containerDict);
                ContainerDataManager.SaveContainerData();

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings,
                    $"Updated Filter History for {containerId}: {string.Join(", ", history)}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings, $"History Error: {ex.Message}");
            }
        }


        /// <summary>
        /// Resets visual customizations for all containers to default values.
        /// Preserves position, size, content, and system state (Locked/Rolled).
        /// </summary>
        public static void ResetAllCustomizations()
        {
            try
            {
                bool modificationsMade = false;

                // FIX: Use .ToList() to create a snapshot copy for iteration.
                // This prevents "Collection was modified" errors when we update the real list inside the loop.
                var containersSnapshot = ContainerDataManager.ContainerData.ToList();

                foreach (dynamic container in containersSnapshot)
                {
                    // Convert to JObject/Dictionary for safe property access
                    IDictionary<string, object> containerDict = container is IDictionary<string, object> dict
                        ? dict
                        : ((JObject)container).ToObject<IDictionary<string, object>>();

                    // --- RESET VISUALS TO DEFAULTS ---

                    // Container Appearance
                    containerDict["CustomColor"] = null;
                    containerDict["CustomLaunchEffect"] = null;
                    containerDict["ContainerBorderColor"] = null;
                    containerDict["ContainerBorderThickness"] = 2; // Default thickness

                    // Title Appearance
                    containerDict["TitleTextColor"] = null;
                    containerDict["TitleTextSize"] = "Medium";
                    containerDict["BoldTitleText"] = "false";

                    // Icon Appearance
                    containerDict["TextColor"] = null;
                    containerDict["DisableTextShadow"] = "false";
                    containerDict["IconSize"] = "Desktop";
                    containerDict["IconSpacing"] = 5;
                    containerDict["GrayscaleIcons"] = "false";

                    // Update the object in the REAL list
                    int index = ContainerDataManager.ContainerData.IndexOf(container);
                    if (index >= 0)
                    {
                        ContainerDataManager.ContainerData[index] = JObject.FromObject(containerDict);
                        modificationsMade = true;
                    }
                }

                if (modificationsMade)
                {
                    ContainerDataManager.SaveContainerData();

                    // Reload to reflect changes immediately
                    ReloadContainers();

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings,
                        "All container customizations have been reset to defaults.");

                    MessageBoxesManager.ShowOKOnlyMessageBoxForm(
                        "All visual customizations have been reset.\nYour icons and container positions were preserved.",
                        "Reset Complete");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Error,
                    $"Error resetting customizations: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error resetting customizations: {ex.Message}", "Error");
            }
        }



        // Update container property, save to JSON, and apply runtime changes
        public static void UpdateContainerProperty(dynamic container, string propertyName, string value, string logMessage)
        {
            try
            {


                string containerId = container.Id?.ToString();
                if (string.IsNullOrEmpty(containerId))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Container '{container.Title}' has no Id");
                    return;
                }

                // Skip updates if container is in transition (except for IsRolled and UnrolledHeight which are rollup-specific)
                if (_containersInTransition.Contains(containerId) && propertyName != "IsRolled" && propertyName != "UnrolledHeight")
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerUpdate, $"Skipping {propertyName} update for container '{containerId}' - in transition");
                    return;
                }

                // Find by GUID instead of title or reference
                int index = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (index >= 0)
                {
                    // Get the container from ContainerDataManager.ContainerData
                    dynamic actualContainer = ContainerDataManager.ContainerData[index];





                    // Convert to dictionary safely
                    IDictionary<string, object> containerDict = actualContainer as IDictionary<string, object> ?? ((JObject)actualContainer).ToObject<IDictionary<string, object>>();

                    // Handle IsHidden specifically to store as string to match JSON format
                    if (propertyName == "IsHidden")
                    {
                        // Convert boolean-like string input to string "true" or "false"
                        bool parsedValue = value?.ToLower() == "true";
                        containerDict[propertyName] = parsedValue.ToString().ToLower(); // Store as "true" or "false"
                    }
                    else
                    {
                        // Update other properties as provided
                        containerDict[propertyName] = value;
                    }

                    // Update the container in ContainerDataManager.ContainerData
                    ContainerDataManager.ContainerData[index] = JObject.FromObject(containerDict);
                    ContainerDataManager.SaveContainerData();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"{logMessage} for container '{container.Title}'");

                    // Find the window to apply runtime changes
                    var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                    //var win = windows.FirstOrDefault(w => w.Title == container.Title.ToString());
                    var win = windows.FirstOrDefault(w => w.Tag?.ToString() == containerId);
                    if (win != null)
                    {
                        // Apply runtime changes
                        if (propertyName == "CustomColor")
                        {
                            Utility.ApplyTintAndColorToContainer(win, string.IsNullOrEmpty(value) ? _options.SelectedColor : value);
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Applied color '{value ?? "Default"}' to container '{container.Title}' at runtime");
                        }
                        else if (propertyName == "IsHidden")
                        {
                            // Update visibility based on IsHidden
                            bool isHidden = value?.ToLower() == "true";
                            win.Visibility = isHidden ? Visibility.Hidden : Visibility.Visible;
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Set visibility to {(isHidden ? "Hidden" : "Visible")} for container '{container.Title}'");
                        }
                        //step 7
                        else if (propertyName == "IsRolled")
                        {
                            bool isRolled = value?.ToLower() == "true";
                            double targetHeight = isRolled ? SettingsManager.TitleBarHeight : Convert.ToDouble(actualContainer.UnrolledHeight?.ToString() ?? "130"); //rolled height
                            var heightAnimation = new DoubleAnimation(win.Height, targetHeight, TimeSpan.FromSeconds(0.3))
                            {
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                            };
                            win.BeginAnimation(Window.HeightProperty, heightAnimation);
                            // Update WrapPanel visibility
                            var border = win.Content as Border;
                            if (border != null)
                            {
                                var dockPanel = border.Child as DockPanel;
                                if (dockPanel != null)
                                {
                                    var scrollViewer = dockPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
                                    if (scrollViewer != null)
                                    {
                                        var wpcont = scrollViewer.Content as WrapPanel;
                                        if (wpcont != null)
                                        {
                                            wpcont.Visibility = isRolled ? Visibility.Collapsed : Visibility.Visible;
                                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Set WrapPanel visibility to {(isRolled ? "Collapsed" : "Visible")} for container '{actualContainer.Title}'");
                                        }
                                    }
                                }
                            }
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Set height to {targetHeight} for container '{actualContainer.Title}' (IsRolled={isRolled})");
                        }
                        else if (propertyName == "UnrolledHeight")
                        {
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Set UnrolledHeight to {value} for container '{actualContainer.Title}'");
                        }



                        // Update context menu checkmarks
                        if (win.ContextMenu != null)
                        {
                            var customizeMenu = win.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == Loc.T("Customize"));
                            if (customizeMenu != null)
                            {
                                var submenu = propertyName == "CustomColor"
                                    ? customizeMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == Loc.T("Color"))
                                    : customizeMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header.ToString() == Loc.T("Launch Effect"));
                                if (submenu != null)
                                {
                                    foreach (MenuItem item in submenu.Items)
                                    {
                                        item.IsChecked = item.Tag?.ToString() == value || (value == null && item.Header.ToString() == Loc.T("Default"));
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerUpdate, $"Failed to find window for container '{container.Title}' to apply {propertyName}");
                    }
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerUpdate, $"Failed to find container '{container.Title}' in ContainerDataManager.ContainerData for {propertyName} update");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerUpdate, $"Error updating {propertyName} for container '{container.Title}': {ex.Message}");
            }
        }



        // TABS FEATURE: Toggle tabs with strict Data Migration and Effect Cleanup
        private static void ToggleContainerTabs(dynamic container)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Toggling tabs for container '{container.Title}'");

                string containerId = container.Id?.ToString();
                int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (containerIndex < 0) return;

                dynamic liveContainer = ContainerDataManager.ContainerData[containerIndex];
                IDictionary<string, object> containerDict = liveContainer is IDictionary<string, object> dict
                    ? dict : ((JObject)liveContainer).ToObject<IDictionary<string, object>>();

                bool currentTabsEnabled = containerDict.ContainsKey("TabsEnabled") && containerDict["TabsEnabled"]?.ToString().ToLower() == "true";
                bool newTabsEnabled = !currentTabsEnabled;

                var mainItems = containerDict["Items"] as JArray ?? new JArray();
                var tabs = containerDict["Tabs"] as JArray ?? new JArray();

                // 1. Data Migration Logic
                if (newTabsEnabled)
                {
                    // ENABLING TABS: Move Main -> Tab
                    if (tabs.Count == 0)
                    {
                        // Create first tab with Herb Naming
                        dynamic newTab = new JObject();
                        string herbName = ContainerUtilities.GenerateRandomHerbName();
                        newTab.TabName = $"0. {herbName}";
                        newTab.Items = mainItems;
                        tabs.Add(newTab);
                        containerDict["CurrentTab"] = 0;
                    }
                    else
                    {
                        int targetIdx = Convert.ToInt32(containerDict["CurrentTab"]?.ToString() ?? "0");
                        if (targetIdx < 0 || targetIdx >= tabs.Count) targetIdx = 0;

                        var targetTab = tabs[targetIdx] as JObject;
                        var targetItems = targetTab["Items"] as JArray ?? new JArray();

                        foreach (var item in mainItems) targetItems.Add(item);
                        targetTab["Items"] = targetItems;
                        containerDict["CurrentTab"] = targetIdx;
                    }
                    // Clear main items after moving them to tab
                    containerDict["Items"] = new JArray();
                    containerDict["Tabs"] = tabs;
                }
                else
                {
                    // DISABLING TABS: Move Active Tab -> Main
                    int currentTabIdx = Convert.ToInt32(containerDict["CurrentTab"]?.ToString() ?? "0");
                    if (tabs.Count > 0 && currentTabIdx < tabs.Count && currentTabIdx >= 0)
                    {
                        var activeTab = tabs[currentTabIdx] as JObject;
                        var activeItems = activeTab["Items"] as JArray;
                        if (activeItems != null)
                        {
                            // FIX: Clear mainItems first! 
                            // This prevents duplication because mainItems might already contain stale copies 
                            // (especially from Tab 0 syncing).
                            mainItems.Clear();

                            foreach (var item in activeItems) mainItems.Add(item);
                            activeTab["Items"] = new JArray();
                        }
                    }
                    containerDict["Items"] = mainItems;
                }

                // 2. Save Updates
                containerDict["TabsEnabled"] = newTabsEnabled.ToString().ToLower();

                var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                var currentWindow = windows.FirstOrDefault(w => w.Tag?.ToString() == containerId);
                if (currentWindow != null)
                {
                    containerDict["X"] = currentWindow.Left;
                    containerDict["Y"] = currentWindow.Top;
                    containerDict["Width"] = currentWindow.Width;
                    containerDict["Height"] = currentWindow.Height;
                }

                ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                ContainerDataManager.SaveContainerData();

                // 3. Reload Window & CLEANUP EFFECTS
                if (currentWindow != null)
                {
                    // Cleanup Legendary Effects
                    string currentTitle = container.Title?.ToString() ?? "";
                    if (false)
                    {
                        InterCore.ProcessTitleChange(liveContainer, "RESET_EFFECT_TEMP", "");
                    }

                    _heartTextBlocks.Remove(container);
                    if (_portalContainers.ContainsKey(container))
                    {
                        _portalContainers[container].Dispose();
                        _portalContainers.Remove(container);
                    }
                    currentWindow.Close();
                }

                var updatedContainer = ContainerDataManager.ContainerData[containerIndex];

                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CreateContainer(updatedContainer, new TargetChecker(1000));
                }));
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error toggling tabs: {ex.Message}");
            }
        }




        // TABS FEATURE: Dynamically refresh the tab strip UI (Ghost Arrows)
        // v2.5. 4.181: Adds smart left/right chevrons that appear only when tabs are hidden.
        public static void RefreshTabStripUI(NonActivatingWindow containerWindow, dynamic container)
        {
            try
            {
                var border = containerWindow.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                if (dockPanel == null) return;

                // 1. CAPTURE SCROLL STATE
                double previousScrollOffset = 0;
                var oldContainer = dockPanel.Children.OfType<Grid>()
                    .FirstOrDefault(g => g.Tag?.ToString() == "TAB_STRIP_CONTAINER");
                if (oldContainer != null)
                {
                    var oldScroll = oldContainer.Children.OfType<ScrollViewer>().FirstOrDefault();
                    if (oldScroll != null) previousScrollOffset = oldScroll.HorizontalOffset;
                }

                // 2. CLEANUP
                var existingStrips = dockPanel.Children.OfType<FrameworkElement>()
                    .Where(c => c is Grid g && g.Tag?.ToString() == "TAB_STRIP_CONTAINER" ||
                                c is StackPanel sp && sp.Height == 20)
                    .ToList();
                foreach (var oldStrip in existingStrips) dockPanel.Children.Remove(oldStrip);

                // 3. CHECK ENABLED
                bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";
                if (!tabsEnabled) return;

                // 4. COLOR ANALYSIS (For Arrow Visibility)
                string containerColorName = container.CustomColor?.ToString() ?? SettingsManager.SelectedColor;
                string effectiveColor = !string.IsNullOrEmpty(containerColorName) ? containerColorName : SettingsManager.SelectedColor;

                System.Windows.Media.Color baseColor = System.Windows.Media.Colors.Gray;
                try
                {
                    var drawingColor = Utility.GetColorFromName(effectiveColor);
                    baseColor = System.Windows.Media.Color.FromArgb(255, drawingColor.R, drawingColor.G, drawingColor.B);
                }
                catch { }

                string c = effectiveColor?.ToLower() ?? "";
                bool isExplicitDark = c.Contains("blue") || c.Contains("teal") || c.Contains("black") ||
                                      c.Contains("red") || c.Contains("green") || c.Contains("purple") ||
                                      c.Contains("bismark") || c.Contains("fuchsia") || c.Contains("default");

                double brightness = Math.Sqrt(
                    (0.299 * baseColor.R * baseColor.R) +
                    (0.587 * baseColor.G * baseColor.G) +
                    (0.114 * baseColor.B * baseColor.B)
                );

                bool isDarkTheme = isExplicitDark || brightness < 160;
                SolidColorBrush arrowBrush = isDarkTheme ? System.Windows.Media.Brushes.White : new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50));

                // 5. CREATE NEW GRID STRUCTURE
                // Cols: [LeftArrow] [ScrollViewer*] [RightArrow] [PlusButton]
                Grid containerGrid = new Grid
                {
                    Tag = "TAB_STRIP_CONTAINER",
                    Height = 20,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 0, 0, 0)),
                    Margin = new Thickness(0, 1, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };

                containerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 0: Left Arrow
                containerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 1: Tabs
                containerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 2: Right Arrow
                containerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 3: Plus Button

                // 6. CREATE SCROLLVIEWER
                ScrollViewer scrollViewer = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    PanningMode = PanningMode.HorizontalOnly,
                    CanContentScroll = true
                };

                scrollViewer.PreviewMouseWheel += (s, e) =>
                {
                    if (e.Delta > 0) scrollViewer.LineLeft();
                    else scrollViewer.LineRight();
                    e.Handled = true;
                };

                StackPanel tabStack = new StackPanel { Orientation = Orientation.Horizontal };
                scrollViewer.Content = tabStack;

                // 7. CREATE GHOST ARROWS
                TextBlock leftArrow = new TextBlock
                {
                    Text = Loc.T("‹"), // Elegant chevron
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = arrowBrush,
                    Opacity = 0.6,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, -2, 2, 0), // Slight nudge up
                    Visibility = Visibility.Collapsed // Hidden by default
                };
                leftArrow.MouseLeftButtonDown += (s, e) => { scrollViewer.LineLeft(); e.Handled = true; };

                TextBlock rightArrow = new TextBlock
                {
                    Text = Loc.T("›"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = arrowBrush,
                    Opacity = 0.6,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, -2, 2, 0),
                    Visibility = Visibility.Collapsed
                };
                rightArrow.MouseLeftButtonDown += (s, e) => { scrollViewer.LineRight(); e.Handled = true; };

                // 8. WIRE UP SCROLL LOGIC
                // This updates visibility whenever the scroll position or size changes
                scrollViewer.ScrollChanged += (s, e) =>
                {
                    // Only show left if we have scrolled right
                    leftArrow.Visibility = scrollViewer.HorizontalOffset > 0
                        ? Visibility.Visible : Visibility.Collapsed;

                    // Only show right if there is scrollable content remaining
                    rightArrow.Visibility = scrollViewer.HorizontalOffset < scrollViewer.ScrollableWidth
                        ? Visibility.Visible : Visibility.Collapsed;
                };

                // Add Elements to Grid
                containerGrid.Children.Add(leftArrow); Grid.SetColumn(leftArrow, 0);
                containerGrid.Children.Add(scrollViewer); Grid.SetColumn(scrollViewer, 1);
                containerGrid.Children.Add(rightArrow); Grid.SetColumn(rightArrow, 2);

                // 9. POPULATE TABS
                var tabs = container.Tabs as JArray ?? new JArray();
                int currentTab = Convert.ToInt32(container.CurrentTab?.ToString() ?? "0");

                for (int i = 0; i < tabs.Count; i++)
                {
                    var tab = tabs[i] as JObject;
                    if (tab == null) continue;

                    string tabName = tab["TabName"]?.ToString() ?? $"Tab {i + 1}";
                    bool isActiveTab = (i == currentTab);
                    int capturedIndex = i;

                    Button tabButton = new Button
                    {
                        Content = tabName,
                        Tag = i,
                        Height = 18,
                        MinWidth = 50,
                        Margin = new Thickness(1, 0, 1, 0),
                        Padding = new Thickness(10, 2, 10, 2),
                        FontSize = 10,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                        Cursor = Cursors.Hand,
                        Focusable = false
                    };

                    ApplyTabStyle(tabButton, isActiveTab, containerColorName);

                    tabButton.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        containerWindow.Focus();
                        System.Windows.Input.Keyboard.ClearFocus();

                        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        {
                            e.Handled = true;
                            RenameTab(container, capturedIndex, containerWindow);
                        }
                        else
                        {
                            SwitchTabByContainer(container, capturedIndex, containerWindow);
                            e.Handled = true;
                        }
                    };

                    tabButton.PreviewMouseRightButtonDown += (s, e) => SwitchTabByContainer(container, capturedIndex, containerWindow);

                    // Context Menu
                    ContextMenu tabContextMenu = new ContextMenu();
                    MenuItem miAddTab = new MenuItem { Header = Loc.T("Add New Tab") };
                    MenuItem miRenameTab = new MenuItem { Header = Loc.T("Rename Tab") };
                    MenuItem miDeleteTab = new MenuItem { Header = Loc.T("Delete Tab") };
                    MenuItem miMoveLeft = new MenuItem { Header = Loc.T("Move Left") };
                    MenuItem miMoveRight = new MenuItem { Header = Loc.T("Move Right") };

                    tabContextMenu.Items.Add(miAddTab);
                    tabContextMenu.Items.Add(new Separator());
                    tabContextMenu.Items.Add(miRenameTab);
                    tabContextMenu.Items.Add(miDeleteTab);
                    tabContextMenu.Items.Add(new Separator());
                    tabContextMenu.Items.Add(miMoveLeft);
                    tabContextMenu.Items.Add(miMoveRight);

                    miAddTab.Click += (s, e) => AddNewTab(container, containerWindow);
                    miRenameTab.Click += (s, e) => RenameTab(container, capturedIndex, containerWindow);
                    miDeleteTab.Click += (s, e) => DeleteTab(container, capturedIndex, containerWindow);
                    miMoveLeft.Click += (s, e) => MoveTab(container, capturedIndex, -1, containerWindow);
                    miMoveRight.Click += (s, e) => MoveTab(container, capturedIndex, 1, containerWindow);

                    tabContextMenu.Opened += (s, e) =>
                    {
                        miMoveLeft.IsEnabled = capturedIndex > 0;
                        miMoveRight.IsEnabled = capturedIndex < tabs.Count - 1;
                    };

                    tabButton.ContextMenu = tabContextMenu;
                    tabStack.Children.Add(tabButton);
                }

                // 10. POPULATE PINNED [+] BUTTON
                Button addTabButton = new Button
                {
                    Content = Loc.T("+"),
                    Tag = "ADD_TAB",
                    Height = 18,
                    Width = 25,
                    Margin = new Thickness(3, 0, 1, 0),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = Loc.T("Add new tab (Ctrl+Click to Import)"), // Updated Tooltip
                    Focusable = false
                };

                ApplyTabStyle(addTabButton, false, containerColorName, true);

                bool isAddingTab = false;
                addTabButton.PreviewMouseLeftButtonDown += async (s, e) =>
                {
                    containerWindow.Focus();
                    e.Handled = true;

                    // --- NEW IMPORT LOGIC ---
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        // Advanced: Import Tab
                        ImportTabManager.HandleImportRequest(container, containerWindow);
                        return;
                    }
                    // ------------------------

                    if (isAddingTab) return;
                    isAddingTab = true;
                    try { AddNewTab(container, containerWindow); }
                    finally { await System.Threading.Tasks.Task.Delay(500); isAddingTab = false; }
                };

                containerGrid.Children.Add(addTabButton);
                Grid.SetColumn(addTabButton, 3); // Col 3 is for the Button

                // 11. SURGICAL INSERTION
                DockPanel.SetDock(containerGrid, Dock.Top);

                int insertIndex = 0;
                bool titleFound = false;
                for (int i = 0; i < dockPanel.Children.Count; i++)
                {
                    if (dockPanel.Children[i] is Grid g)
                    {
                        if (g.Children.OfType<TextBlock>().Any(tb => tb.Name == "ContainerLockIcon"))
                        {
                            insertIndex = i + 1;
                            titleFound = true;
                            break;
                        }
                    }
                }
                if (!titleFound) insertIndex = 0;

                if (insertIndex < dockPanel.Children.Count &&
                    dockPanel.Children[insertIndex] is Grid potentialFilter &&
                    potentialFilter.Children.OfType<ComboBox>().Any())
                {
                    insertIndex++;
                }

                if (insertIndex > dockPanel.Children.Count) insertIndex = dockPanel.Children.Count;
                dockPanel.Children.Insert(insertIndex, containerGrid);

                // 12. RESTORE SCROLL (Async to allow layout pass)
                if (previousScrollOffset > 0)
                {
                    containerWindow.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                    {
                        scrollViewer.ScrollToHorizontalOffset(previousScrollOffset);
                    }));
                }

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Refreshed Tab Strip with Ghost Arrows");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing tab strip UI: {ex.Message}");
            }
        }      // TABS FEATURE: Add new tab with random herb name
        public static void AddNewTab(dynamic container, NonActivatingWindow containerWindow)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"AddNewTab called for container '{container.Title}'");

                // Get fresh container data
                string containerId = container.Id?.ToString();
                if (string.IsNullOrEmpty(containerId))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Cannot add tab: container ID missing");
                    return;
                }

                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Cannot add tab: container with ID '{containerId}' not found");
                    return;
                }

                var tabs = currentContainer.Tabs as JArray ?? new JArray();

                // Generate random herb name with tab index
                string herbName = ContainerUtilities.GenerateRandomHerbName();
                string newTabName = $"{tabs.Count}. {herbName}";

                // Create new tab object
                var newTab = new JObject();
                newTab["TabName"] = newTabName;
                newTab["Items"] = new JArray();

                // Add to tabs array
                tabs.Add(newTab);

                // Update container data properly
                int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (containerIndex >= 0)
                {
                    IDictionary<string, object> containerDict = currentContainer is IDictionary<string, object> dict ?
                        dict : ((JObject)currentContainer).ToObject<IDictionary<string, object>>();

                    containerDict["Tabs"] = tabs; // Store JArray directly
                    containerDict["CurrentTab"] = tabs.Count - 1; // Switch to new tab

                    ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                    ContainerDataManager.SaveContainerData();

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Added new tab '{newTabName}' to container '{currentContainer.Title}'");

                    // Get updated container and refresh the display
                    var updatedContainer = ContainerDataManager.ContainerData[containerIndex];
                    int newTabIndex = tabs.Count - 1;

                    // Refresh content and styling
                    RefreshContainerContentSimple(containerWindow, updatedContainer, newTabIndex);
                    RefreshTabStyling(containerWindow, newTabIndex);

                    // Refresh the entire tab strip to show new tab
                    RefreshTabStripUI(containerWindow, updatedContainer);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "New tab added successfully");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error adding new tab: {ex.Message}");
            }
        }


        // TABS FEATURE: Rename tab with inline editing (in-button, focus-enabled)
        // v2.5.4.187: Fixed Visual Tree traversal to support ScrollViewer/Ghost Arrows structure
        public static void RenameTab(dynamic container, int tabIndex, NonActivatingWindow containerWindow)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"RenameTab called for container '{container.Title}', tab {tabIndex}");

                // 1. Find the main DockPanel
                var border = containerWindow.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                if (dockPanel == null) return;

                // 2. FIX: Find the new Grid Container first (Tag: TAB_STRIP_CONTAINER)
                var containerGrid = dockPanel.Children.OfType<Grid>()
                    .FirstOrDefault(g => g.Tag?.ToString() == "TAB_STRIP_CONTAINER");

                if (containerGrid == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "RenameTab: Tab strip container not found.");
                    return;
                }

                // 3. FIX: Find the ScrollViewer inside the Grid
                var scrollViewer = containerGrid.Children.OfType<ScrollViewer>().FirstOrDefault();
                if (scrollViewer == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "RenameTab: ScrollViewer not found.");
                    return;
                }

                // 4. FIX: Get the StackPanel from the ScrollViewer content
                var tabStrip = scrollViewer.Content as StackPanel;
                if (tabStrip == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "RenameTab: Tab StackPanel not found.");
                    return;
                }

                // 5. Find the button for this tab index
                Button targetButton = null;
                foreach (Button btn in tabStrip.Children.OfType<Button>())
                {
                    if (btn.Tag is int buttonTabIndex && buttonTabIndex == tabIndex)
                    {
                        targetButton = btn;
                        break;
                    }
                }

                if (targetButton == null) return;

                // Get current tab data
                string containerId = container.Id?.ToString();
                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer == null) return;

                var tabs = currentContainer.Tabs as JArray ?? new JArray();
                if (tabIndex < 0 || tabIndex >= tabs.Count) return;

                var tab = tabs[tabIndex] as JObject;
                if (tab == null) return;

                string currentName = tab["TabName"]?.ToString() ?? $"Tab {tabIndex}";

                // Temporarily increase button height to accommodate TextBox properly
                double originalHeight = targetButton.Height;
                targetButton.Height = 22; // Slightly taller for TextBox

                // Create TextBox for inline editing
                TextBox editTextBox = new TextBox
                {
                    Text = currentName,
                    FontSize = targetButton.FontSize,
                    FontFamily = targetButton.FontFamily,
                    FontWeight = FontWeights.Normal,
                    Background = System.Windows.Media.Brushes.White,
                    Foreground = System.Windows.Media.Brushes.Black,
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 70, 130, 180)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                // Store original button properties
                object originalContent = targetButton.Content;
                var originalBackground = targetButton.Background;
                var originalForeground = targetButton.Foreground;
                var originalBorderBrush = targetButton.BorderBrush;

                targetButton.Content = editTextBox;
                targetButton.Background = System.Windows.Media.Brushes.White;
                targetButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 70, 130, 180));

                // CRITICAL: Disable NonActivatingWindow focus prevention during editing and force focus
                containerWindow.BeginKeyboardInteractiveEdit(editTextBox);
                bool editingComplete = false;

                // Action to complete editing and restore NonActivatingWindow behavior
                Action<bool> completeEditing = (save) =>
                {
                    if (editingComplete) return;
                    editingComplete = true;

                    try
                    {
                        if (save && !string.IsNullOrWhiteSpace(editTextBox.Text))
                        {
                            string newName = editTextBox.Text.Trim();

                            // Validate name length
                            if (newName.Length > 30)
                            {
                                newName = newName.Substring(0, 30);
                            }

                            // Update tab name in data
                            tab["TabName"] = newName;

                            // Save to JSON
                            int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                            if (containerIndex >= 0)
                            {
                                IDictionary<string, object> containerDict = currentContainer is IDictionary<string, object> dict ?
                                    dict : ((JObject)currentContainer).ToObject<IDictionary<string, object>>();

                                containerDict["Tabs"] = tabs;
                                ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                                ContainerDataManager.SaveContainerData();

                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                                    $"Renamed tab from '{currentName}' to '{newName}'");

                                // Update button with new name
                                targetButton.Content = newName;
                            }
                        }
                        else
                        {
                            // Cancel - restore original content
                            targetButton.Content = originalContent;
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Tab rename cancelled");
                        }

                        // Restore original button properties
                        targetButton.Background = originalBackground;
                        targetButton.Foreground = originalForeground;
                        targetButton.BorderBrush = originalBorderBrush;
                        targetButton.Height = originalHeight;

                        // CRITICAL: Re-enable NonActivatingWindow focus prevention
                        containerWindow.EndKeyboardInteractiveEdit();
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                            $"Error completing tab rename: {ex.Message}");

                        // Restore everything on error
                        targetButton.Content = originalContent;
                        targetButton.Background = originalBackground;
                        targetButton.Foreground = originalForeground;
                        targetButton.BorderBrush = originalBorderBrush;
                        targetButton.Height = originalHeight;
                        containerWindow.EndKeyboardInteractiveEdit();
                    }
                };

                // Handle Enter key (save) and Escape (cancel)
                editTextBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        completeEditing(true);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        completeEditing(false);
                        e.Handled = true;
                    }
                };

                // Handle focus loss (save)
                editTextBox.LostFocus += (s, e) =>
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => completeEditing(true))
                    );
                };

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Started inline editing for tab '{currentName}' with focus enabled");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error starting tab rename: {ex.Message}");

                // Ensure focus prevention is restored on any error
                containerWindow.EnableFocusPrevention(true);
            }
        }

        // TABS FEATURE: Delete tab with confirmation, Auto-Export, and Sync Fix
        public static void DeleteTab(dynamic container, int tabIndex, NonActivatingWindow containerWindow)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"DeleteTab called for container '{container.Title}', tab {tabIndex}");

                // 1. Get Fresh Data
                string containerId = container.Id?.ToString();
                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer == null) return;

                var tabs = currentContainer.Tabs as JArray ?? new JArray();

                // 2. Validation: Don't allow deleting the last tab
                if (tabs.Count <= 1)
                {
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm("Cannot delete the last remaining tab.", "Delete Tab");
                    return;
                }

                if (tabIndex < 0 || tabIndex >= tabs.Count) return;

                var tab = tabs[tabIndex] as JObject;
                if (tab == null) return;

                string tabName = tab["TabName"]?.ToString() ?? $"Tab {tabIndex}";
                var items = tab["Items"] as JArray ?? new JArray();

                // 3. Confirmation
                if (items.Count > 0)
                {
                    bool result = MessageBoxesManager.ShowTabDeleteConfirmationForm(tabName, items.Count);
                    if (!result)
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"User cancelled tab deletion for '{tabName}'");
                        return;
                    }
                }

                // --- Auto-Export Logic (Tabs follow Container Settings) ---
                if (SettingsManager.ExportShortcutsOnContainerDeletion && items.Count > 0)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Auto-exporting {items.Count} items from tab '{tabName}' before deletion.");

                    int exportCount = 0;
                    foreach (var item in items)
                    {
                        try
                        {
                            CopyPasteManager.SendToDesktop(item);
                            exportCount++;
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Failed to export item from tab: {ex.Message}");
                        }
                    }
                }
                // -----------------------------------------------------------

                // 4. Remove Tab
                tabs.RemoveAt(tabIndex);

                // 5. Calculate New Active Tab
                int currentTab = Convert.ToInt32(currentContainer.CurrentTab?.ToString() ?? "0");
                int newCurrentTab = currentTab;

                // Shift logic:
                // If we deleted the active tab -> Go to previous (or 0)
                // If we deleted a tab BEFORE the active one -> Active tab shifts down by 1
                // If we deleted a tab AFTER the active one -> Active index stays same
                if (tabIndex <= currentTab)
                {
                    newCurrentTab = Math.Max(0, currentTab - 1);
                }

                // 6. Save Data
                int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (containerIndex >= 0)
                {
                    IDictionary<string, object> containerDict = currentContainer is IDictionary<string, object> dict ?
                        dict : ((JObject)currentContainer).ToObject<IDictionary<string, object>>();

                    containerDict["Tabs"] = tabs;
                    containerDict["CurrentTab"] = newCurrentTab;

                    ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                    ContainerDataManager.SaveContainerData();

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Deleted tab '{tabName}' from container '{currentContainer.Title}'. New Active Tab: {newCurrentTab}");

                    // --- BUG FIX: FORCE SYNC WITH MAIN ITEMS ---
                    // Now that the tabs have shifted, "Tab 0" might be different.
                    // We must force 'Main.Items' to mirror the NEW 'Tab 0' to prevent "Ghost Icons" 
                    // from appearing if the user later disables tabs.
                    if (tabs.Count > 0)
                    {
                        SynchronizeTab0Content(containerId, "tab0", "full");
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Forced Tab0 synchronization after deletion.");
                    }
                    // -------------------------------------------

                    // 7. REFRESH UI
                    var updatedContainer = ContainerDataManager.ContainerData[containerIndex];

                    // A. Refresh the Icons (Content)
                    RefreshContainerContentSimple(containerWindow, updatedContainer, newCurrentTab);

                    // B. Refresh the Tab Buttons (Strip)
                    RefreshTabStripUI(containerWindow, updatedContainer);

                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Tab deletion UI refresh complete");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error deleting tab: {ex.Message}");
            }
        }


        // TABS FEATURE: Switch tab using container ID
        // FIXED: Now calls RefreshTabStripUI instead of RefreshTabStyling to force a clean redraw.
        public static void SwitchTabByContainer(dynamic container, int newTabIndex, NonActivatingWindow containerWindow)
        {
            try
            {
                // Get fresh container data by ID to avoid stale references
                string containerId = container.Id?.ToString();
                if (string.IsNullOrEmpty(containerId)) return;

                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer == null) return;

                // Validate tab index
                var tabs = currentContainer.Tabs as JArray ?? new JArray();
                if (newTabIndex < 0 || newTabIndex >= tabs.Count) return;

                // Check current tab
                int currentTabIndex = Convert.ToInt32(currentContainer.CurrentTab?.ToString() ?? "0");
                if (currentTabIndex == newTabIndex) return; // Already there

                // Update Data
                IDictionary<string, object> containerDict = currentContainer as IDictionary<string, object> ??
                    ((JObject)currentContainer).ToObject<IDictionary<string, object>>();
                containerDict["CurrentTab"] = newTabIndex;

                // Save
                int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (containerIndex >= 0)
                {
                    ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                    ContainerDataManager.SaveContainerData();
                }

                var freshContainer = ContainerDataManager.ContainerData[containerIndex];

                // 1. Refresh Icons (Content)
                RefreshContainerContentSimple(containerWindow, freshContainer, newTabIndex);

                // 2. Refresh Tabs (UI Redraw) - THE FIX
                // We use RefreshTabStripUI instead of RefreshTabStyling.
                // This destroys the old buttons and creates new ones with the correct "Active" state baked in.
                RefreshTabStripUI(containerWindow, freshContainer);

                string tabName = tabs[newTabIndex]["TabName"]?.ToString() ?? $"Tab {newTabIndex}";
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Switched to tab '{tabName}'");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error switching tab: {ex.Message}");
            }
        }







        // TABS FEATURE: Move Tab (Left/Right)
        public static void MoveTab(dynamic container, int fromIndex, int direction, NonActivatingWindow containerWindow)
        {
            try
            {
                // Direction: -1 for Left, +1 for Right
                int toIndex = fromIndex + direction;

                // 1. Get Fresh Data
                string containerId = container.Id?.ToString();
                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer == null) return;

                var tabs = currentContainer.Tabs as JArray;
                if (tabs == null) return;

                // 2. Validate Bounds
                if (toIndex < 0 || toIndex >= tabs.Count) return;

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Moving tab {fromIndex} to {toIndex} for container '{currentContainer.Title}'");

                // 3. Swap Tabs
                var tempTab = tabs[fromIndex];
                tabs[fromIndex] = tabs[toIndex];
                tabs[toIndex] = tempTab;

                // 4. Update CurrentTab Index if necessary
                // If we moved the active tab, follow it. 
                // If we moved a tab into the active slot, update the index to stay on the same content.
                int currentTabIdx = Convert.ToInt32(currentContainer.CurrentTab?.ToString() ?? "0");

                if (currentTabIdx == fromIndex)
                {
                    currentContainer.CurrentTab = toIndex; // Follow the moved tab
                }
                else if (currentTabIdx == toIndex)
                {
                    currentContainer.CurrentTab = fromIndex; // The other tab swapped into our slot
                }

                // 5. Save & Refresh
                int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (containerIndex >= 0)
                {
                    IDictionary<string, object> containerDict = currentContainer is IDictionary<string, object> dict ?
                        dict : ((JObject)currentContainer).ToObject<IDictionary<string, object>>();

                    containerDict["Tabs"] = tabs;
                    containerDict["CurrentTab"] = currentContainer.CurrentTab; // Updated index

                    ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                    ContainerDataManager.SaveContainerData();

                    // Refresh Strip (Buttons)
                    RefreshTabStripUI(containerWindow, ContainerDataManager.ContainerData[containerIndex]);

                    // Refresh Content (Icons) - just in case the active tab index changed logic
                    RefreshContainerContentSimple(containerWindow, ContainerDataManager.ContainerData[containerIndex], (int)containerDict["CurrentTab"]);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error moving tab: {ex.Message}");
            }
        }






        // TABS FEATURE: Tab0-Container Content Synchronization Manager
        private static bool _isSynchronizing = false; // Prevent circular sync operations

        /// <summary>
        /// Synchronizes content between Tab0 and main Items to ensure they remain identical
        /// Called whenever items are added/removed from either location
        /// </summary>
        /// <param name="containerId">The container ID to synchronize</param>
        /// <param name="sourceLocation">Where the change originated: "tab0" or "main"</param>
        /// <param name="operationType">Type of operation: "add", "remove", "full"</param>
        private static void SynchronizeTab0Content(string containerId, string sourceLocation, string operationType = "full")
        {
            if (_isSynchronizing)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    "Sync already in progress, skipping to prevent circular operation");
                return;
            }

            try
            {
                _isSynchronizing = true;

                var container = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (container == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                        $"Cannot sync: container {containerId} not found");
                    return;
                }

                bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";
                var tabs = container.Tabs as JArray ?? new JArray();
                var mainItems = container.Items as JArray ?? new JArray();

                // Only sync if tabs are enabled and Tab0 exists
                if (!tabsEnabled || tabs.Count == 0) return;

                var tab0 = tabs[0] as JObject;
                if (tab0 == null) return;

                var tab0Items = tab0["Items"] as JArray ?? new JArray();

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Synchronizing {operationType} from {sourceLocation} for container '{container.Title}' - Tab0: {tab0Items.Count} items, Main: {mainItems.Count} items");

                bool syncPerformed = false;

                // Determine sync direction and perform synchronization
                if (sourceLocation == "tab0")
                {
                    // Tab0 changed - sync to main Items
                    if (!AreItemArraysEqual(tab0Items, mainItems))
                    {
                        container.Items = JArray.FromObject(tab0Items.ToArray());
                        syncPerformed = true;
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Synced {tab0Items.Count} items from Tab0 to main Items for container '{container.Title}'");
                    }
                }
                else if (sourceLocation == "main")
                {
                    // Main Items changed - sync to Tab0
                    if (!AreItemArraysEqual(mainItems, tab0Items))
                    {
                        tab0["Items"] = JArray.FromObject(mainItems.ToArray());
                        syncPerformed = true;
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Synced {mainItems.Count} items from main Items to Tab0 for container '{container.Title}'");
                    }
                }

                // Save changes if synchronization was performed
                if (syncPerformed)
                {
                    int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                    if (containerIndex >= 0)
                    {
                        ContainerDataManager.ContainerData[containerIndex] = container;
                        ContainerDataManager.SaveContainerData();
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error in Tab0 synchronization: {ex.Message}");
            }
            finally
            {
                _isSynchronizing = false;
            }
        }

        /// <summary>
        /// Helper method to compare two JArrays for equality
        /// </summary>
        private static bool AreItemArraysEqual(JArray array1, JArray array2)
        {
            if (array1.Count != array2.Count) return false;

            for (int i = 0; i < array1.Count; i++)
            {
                var item1 = array1[i] as JObject;
                var item2 = array2[i] as JObject;

                if (item1 == null || item2 == null) return false;

                // Compare essential properties (Filename is the key identifier)
                string filename1 = item1["Filename"]?.ToString();
                string filename2 = item2["Filename"]?.ToString();

                if (filename1 != filename2) return false;
            }

            return true;
        }

        // TABS FEATURE: Simplified content refresh that reuses existing infrastructure

        public static void RefreshContainerContentSimple(NonActivatingWindow containerWindow, dynamic container, int tabIndex)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"RefreshContainerContentSimple: switching to tab {tabIndex}");

                // 1. Find the WrapPanel
                var border = containerWindow.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                var wrapPanel = scrollViewer?.Content as WrapPanel;

                if (wrapPanel == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Cannot find WrapPanel for content refresh");
                    return;
                }

                // 2. Clear existing content
                wrapPanel.Children.Clear();

                // 3. Get items from the specified tab
                var tabs = container.Tabs as JArray ?? new JArray();
                if (tabIndex >= 0 && tabIndex < tabs.Count)
                {
                    var activeTab = tabs[tabIndex] as JObject;
                    if (activeTab != null)
                    {
                        var items = activeTab["Items"] as JArray ?? new JArray();
                        string tabName = activeTab["TabName"]?.ToString() ?? $"Tab {tabIndex}";

                        // 4. Sort items by DisplayOrder
                        var sortedItems = items
                            .OfType<JObject>()
                            .OrderBy(item => item["DisplayOrder"]?.Type == JTokenType.Integer ? item["DisplayOrder"].Value<int>() : 0)
                            .ToList();

                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Loading {sortedItems.Count} items from tab '{tabName}'");

                        // 5. Iterate and Add Icons (Using Unified Logic)
                        foreach (dynamic icon in sortedItems)
                        {
                            // FIX A: Use the main ContainerManager.AddIcon method!
                            // This ensures network indicators, clean icons, and custom sizes are applied.
                            AddIcon(icon, wrapPanel, container);

                            StackPanel sp = wrapPanel.Children[wrapPanel.Children.Count - 1] as StackPanel;
                            if (sp != null)
                            {
                                // FIX B: Extract Properties safely
                                IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict
                                    ? dict : ((JObject)icon).ToObject<IDictionary<string, object>>();

                                string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
                                bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];

                                // FIX C: Extract Arguments
                                string arguments = null;
                                if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
                                {
                                    try
                                    {
                                        WshShell shell = new WshShell();
                                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                                        arguments = shortcut.Arguments;
                                    }
                                    catch { }
                                }

                                // FIX D: Attach Events
                                ClickEventAdder(sp, filePath, isFolder, arguments);

                                // FIX E: Attach Centralized Context Menu (Crucial for Right-Click)
                                AttachIconContextMenu(sp, icon, container, containerWindow);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error in RefreshContainerContentSimple: {ex.Message}");
            }
        }






        // TABS FEATURE: Remove item from current tab
        private static void RemoveItemFromTab(dynamic item, dynamic container, int tabIndex, WrapPanel wrapPanel)
        {
            try
            {
                var tabs = container.Tabs as JArray ?? new JArray();
                if (tabIndex >= 0 && tabIndex < tabs.Count)
                {
                    var activeTab = tabs[tabIndex] as JObject;
                    if (activeTab != null)
                    {
                        var items = activeTab["Items"] as JArray ?? new JArray();
                        string itemFilename = item.Filename?.ToString();

                        // Find and remove the item
                        for (int i = items.Count - 1; i >= 0; i--)
                        {
                            var currentItem = items[i] as JObject;
                            if (currentItem != null && currentItem["Filename"]?.ToString() == itemFilename)
                            {
                                items.RemoveAt(i);
                                break;
                            }
                        }

                        // Update the tab
                        activeTab["Items"] = items;

                        // Save changes
                        string containerId = container.Id?.ToString();
                        int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                        if (containerIndex >= 0)
                        {
                            ContainerDataManager.ContainerData[containerIndex] = container;
                            ContainerDataManager.SaveContainerData();
                            // ENHANCED: Add Tab0 synchronization after removal
                            if (tabIndex == 0)
                            {
                               // string containerId = container.Id?.ToString();
                                if (!string.IsNullOrEmpty(containerId))
                                {
                                    SynchronizeTab0Content(containerId, "tab0", "remove");
                                }
                            }

                        }

                        // Refresh the display
                        var containerWindow = FindVisualParent<NonActivatingWindow>(wrapPanel);
                        if (containerWindow != null)
                        {
                            RefreshContainerContentSimple(containerWindow, container, tabIndex);
                        }

                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Removed item '{itemFilename}' from tab {tabIndex}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error removing item from tab: {ex.Message}");
            }
        }

        // TABS FEATURE: Update tab button styling
        // Updated to support the new ScrollViewer structure
        public static void RefreshTabStyling(NonActivatingWindow containerWindow, int activeTabIndex)
        {
            try
            {
                var border = containerWindow.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                if (dockPanel == null) return;

                // 1. Find the Container (Grid)
                var containerGrid = dockPanel.Children.OfType<Grid>()
                    .FirstOrDefault(g => g.Tag?.ToString() == "TAB_STRIP_CONTAINER");

                if (containerGrid == null) return;

                // 2. Find the Tab StackPanel (Inside ScrollViewer)
                var scrollViewer = containerGrid.Children.OfType<ScrollViewer>().FirstOrDefault();
                var tabStack = scrollViewer?.Content as StackPanel;

                // 3. Find the [+] Button (Direct child of Grid)
                var plusButton = containerGrid.Children.OfType<Button>()
                    .FirstOrDefault(b => b.Tag?.ToString() == "ADD_TAB");

                string containerId = containerWindow.Tag?.ToString();
                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer == null) return;

                string containerColorName = currentContainer.CustomColor?.ToString();
                if (string.IsNullOrEmpty(containerColorName)) containerColorName = SettingsManager.SelectedColor;

                // 4. Update Tab Buttons
                if (tabStack != null)
                {
                    foreach (var child in tabStack.Children)
                    {
                        if (child is Button tabButton && tabButton.Tag is int idx)
                        {
                            bool isActive = (idx == activeTabIndex);
                            ApplyTabStyle(tabButton, isActive, containerColorName);
                        }
                    }
                }

                // 5. Update [+] Button
                if (plusButton != null)
                {
                    ApplyTabStyle(plusButton, false, containerColorName, true);
                }
            }
            catch { }
        }





        // TABS FEATURE: Create custom button template with rounded top corners only
        private static ControlTemplate CreateTabButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            // Create the border with rounded top corners
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4, 4, 0, 0)); // Top corners rounded
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            // Create the content presenter
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(Button.ContentProperty));

            border.AppendChild(contentPresenter);
            template.VisualTree = border;

            // Add hover trigger for better interaction feedback
            var trigger = new Trigger();
            trigger.Property = Button.IsMouseOverProperty;
            trigger.Value = true;
            trigger.Setters.Add(new Setter(Button.OpacityProperty, 0.8));

            template.Triggers.Add(trigger);

            return template;
        }


        public static void RefreshTabColors(NonActivatingWindow containerWindow, string newColorName)
        {
            try
            {
                var border = containerWindow.Content as Border;
                if (border == null) return;
                var dockPanel = border.Child as DockPanel;
                if (dockPanel == null) return;
                var tabStrip = dockPanel.Children.OfType<StackPanel>().FirstOrDefault(sp => sp.Orientation == Orientation.Horizontal && sp.Height == 20);
                if (tabStrip == null) return;

                string containerId = containerWindow.Tag?.ToString();
                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer == null) return;

                int currentTab = Convert.ToInt32(currentContainer.CurrentTab?.ToString() ?? "0");

                foreach (var child in tabStrip.Children)
                {
                    if (child is Button tabButton)
                    {
                        if (tabButton.Tag is int idx)
                        {
                            bool isActive = (idx == currentTab);
                            ApplyTabStyle(tabButton, isActive, newColorName);
                        }
                        else if (tabButton.Tag?.ToString() == "ADD_TAB")
                        {
                            ApplyTabStyle(tabButton, false, newColorName, true);
                        }
                    }
                }
            }
            catch { }
        }




        // Helper event handlers to avoid lambda capture issues
        private static void TabButton_MouseEnter(object sender, RoutedEventArgs e) { }

        private static void TabButton_MouseLeave(object sender, RoutedEventArgs e) { }

        private static void MigrateLegacyJson()
        {
            try
            {
                bool jsonModified = false;
                var validColors = new HashSet<string> { "Red", "Green", "Teal", "Blue", "Bismark", "White", "Beige", "Gray", "Black", "Purple", "Fuchsia", "Yellow", "Orange" };
                var validEffects = Enum.GetNames(typeof(LaunchEffectsManager.LaunchEffect)).ToHashSet();

                for (int i = 0; i < ContainerDataManager.ContainerData.Count; i++)
                {
                    dynamic container = ContainerDataManager.ContainerData[i];
                    IDictionary<string, object> containerDict = container is IDictionary<string, object> dict
                        ? dict : ((JObject)container).ToObject<IDictionary<string, object>>();

                    // --- 1. CORE VALIDATION (VITAL) ---
                    // Add GUID if missing
                    if (!containerDict.ContainsKey("Id"))
                    {
                        containerDict["Id"] = Guid.NewGuid().ToString();
                        jsonModified = true;
                    }

                    // --- 2. PORTAL CONTAINER MIGRATION ---
                    if (container.ItemsType?.ToString() == "Portal")
                    {
                        string portalPath = container.Items?.ToString();
                        if (!string.IsNullOrEmpty(portalPath) && !System.IO.Directory.Exists(portalPath))
                        {
                            containerDict["IsFolder"] = true;
                            jsonModified = true;
                        }
                    }
                    else
                    {
                        var items = container.Items as JArray ?? new JArray();
                        bool itemsModified = false;
                        foreach (var item in items)
                        {
                            var itemDict = item as JObject;
                            if (itemDict != null)
                            {
                                if (itemDict["IsFolder"] == null)
                                {
                                    string path = itemDict["Filename"]?.ToString();
                                    itemDict["IsFolder"] = System.IO.Directory.Exists(path);
                                    itemsModified = true;
                                }
                                // Ensure IsLink exists
                                if (!itemDict.ContainsKey("IsLink"))
                                {
                                    itemDict["IsLink"] = false;
                                    itemsModified = true;
                                }
                                // Ensure IsNetwork exists
                                if (!itemDict.ContainsKey("IsNetwork"))
                                {
                                    string fname = itemDict["Filename"]?.ToString() ?? "";
                                    itemDict["IsNetwork"] = IsNetworkPath(fname);
                                    itemsModified = true;
                                }
                                // Ensure AlwaysRunAsAdmin exists
                                if (!itemDict.ContainsKey("AlwaysRunAsAdmin"))
                                {
                                    itemDict["AlwaysRunAsAdmin"] = false;
                                    itemsModified = true;
                                }
                            }
                        }
                        if (itemsModified)
                        {
                            containerDict["Items"] = items;
                            jsonModified = true;
                        }
                    }

                    // --- 3. PROPERTY INITIALIZATION (DEFAULTS) ---
                    // Visuals
                    if (!containerDict.ContainsKey("IsLocked")) { containerDict["IsLocked"] = "false"; jsonModified = true; }
                    if (!containerDict.ContainsKey("IsRolled")) { containerDict["IsRolled"] = "false"; jsonModified = true; }
                    if (!containerDict.ContainsKey("UnrolledHeight"))
                    {
                        double height = containerDict.ContainsKey("Height") ? Convert.ToDouble(containerDict["Height"]) : 130;
                        containerDict["UnrolledHeight"] = height;
                        jsonModified = true;
                    }

                    if (!containerDict.ContainsKey("IconSize")) { containerDict["IconSize"] = "Desktop"; jsonModified = true; }
                    if (!containerDict.ContainsKey("IconSpacing")) { containerDict["IconSpacing"] = 5; jsonModified = true; }
                    if (!containerDict.ContainsKey("CustomColor")) { containerDict["CustomColor"] = null; jsonModified = true; }
                    if (!containerDict.ContainsKey("CustomLaunchEffect")) { containerDict["CustomLaunchEffect"] = null; jsonModified = true; }
                    if (!containerDict.ContainsKey("IsHidden")) { containerDict["IsHidden"] = "false"; jsonModified = true; }

                    // Text
                    if (!containerDict.ContainsKey("TextColor")) { containerDict["TextColor"] = null; jsonModified = true; }
                    if (!containerDict.ContainsKey("TitleTextColor")) { containerDict["TitleTextColor"] = null; jsonModified = true; }
                    if (!containerDict.ContainsKey("TitleTextSize")) { containerDict["TitleTextSize"] = "Medium"; jsonModified = true; }
                    if (!containerDict.ContainsKey("BoldTitleText")) { containerDict["BoldTitleText"] = "false"; jsonModified = true; }
                    if (!containerDict.ContainsKey("DisableTextShadow")) { containerDict["DisableTextShadow"] = "false"; jsonModified = true; }
                    if (!containerDict.ContainsKey("GrayscaleIcons")) { containerDict["GrayscaleIcons"] = "false"; jsonModified = true; }

                    // Border
                    if (!containerDict.ContainsKey("ContainerBorderColor")) { containerDict["ContainerBorderColor"] = null; jsonModified = true; }
                    if (!containerDict.ContainsKey("ContainerBorderThickness")) { containerDict["ContainerBorderThickness"] = 0; jsonModified = true; }

                    // --- 4. TABS FEATURE (Structure Only, No Merge) ---
                    if (!containerDict.ContainsKey("TabsEnabled"))
                    {
                        containerDict["TabsEnabled"] = "false";
                        jsonModified = true;
                    }
                    if (!containerDict.ContainsKey("CurrentTab"))
                    {
                        containerDict["CurrentTab"] = 0;
                        jsonModified = true;
                    }
                    if (!containerDict.ContainsKey("Tabs"))
                    {
                        containerDict["Tabs"] = new JArray();
                        jsonModified = true;
                    }

                    // --- 5. TAB VALIDATION (Structure Repair) ---
                    // Ensure existing tabs have valid structure (Name, Items Array)
                    // but DO NOT move items around or merge lists.
                    var tabs = containerDict["Tabs"] as JArray ?? new JArray();
                    if (tabs.Count > 0)
                    {
                        bool tabsModified = false;
                        for (int t = 0; t < tabs.Count; t++)
                        {
                            var tab = tabs[t] as JObject;
                            if (tab == null) continue;

                            if (tab["TabName"] == null)
                            {
                                tab["TabName"] = $"Tab {t + 1}";
                                tabsModified = true;
                            }
                            if (tab["Items"] == null || !(tab["Items"] is JArray))
                            {
                                tab["Items"] = new JArray();
                                tabsModified = true;
                            }
                        }
                        if (tabsModified)
                        {
                            containerDict["Tabs"] = tabs;
                            jsonModified = true;
                        }
                    }

                    // Update Master List
                    ContainerDataManager.ContainerData[i] = JObject.FromObject(containerDict);
                }

                if (jsonModified)
                {
                    ContainerDataManager.SaveContainerData();
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "Migrated containers.json with updated fields (Validations Only)");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Error migrating containers.json: {ex.Message}");
            }
        }


        public static void LoadAndCreateContainers(TargetChecker targetChecker)
        {
            // Get current program version from assembly
            string currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

            RegistryHelper.SetProgramManagementValues(currentVersion);

            // CRITICAL FIX: Load settings EARLY so the single instance check knows the user's preference
            ContainerDataManager.Initialize();
            SettingsManager.LoadSettings();

            // === PRE-FLIGHT CHECK FOR SINGLE INSTANCE SETTING ===
            // Directly read JSON to ensure we get the active profile's preference before UI fully boots
            bool disableSingleInstance = SettingsManager.DisableSingleInstance;
            try
            {
                string baseDir = OpsisContainers.DataRoot.Dir;
                string optionsPath = System.IO.Path.Combine(baseDir, "options.json"); // Legacy fallback
                string masterOptionsPath = System.IO.Path.Combine(baseDir, "MasterOptions.json");

                if (System.IO.File.Exists(masterOptionsPath))
                {
                    optionsPath = masterOptionsPath;
                }
                else
                {
                    string activeProfilePath = System.IO.Path.Combine(baseDir, "ActiveProfile.txt");
                    if (System.IO.File.Exists(activeProfilePath))
                    {
                        string activeProfile = System.IO.File.ReadAllText(activeProfilePath).Trim();
                        string profileOptionsPath = System.IO.Path.Combine(baseDir, "Profiles", activeProfile, "options.json");
                        if (System.IO.File.Exists(profileOptionsPath))
                        {
                            optionsPath = profileOptionsPath;
                        }
                    }
                }

                if (System.IO.File.Exists(optionsPath))
                {
                    string jsonContent = System.IO.File.ReadAllText(optionsPath);
                    var jObj = JObject.Parse(jsonContent);
                    if (jObj["DisableSingleInstance"] != null)
                    {
                        disableSingleInstance = jObj["DisableSingleInstance"].Value<bool>();
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Pre-flight check failed: {ex.Message}");
            }

            // === SINGLE INSTANCE CHECK (WITH DISABLE OPTION) ===
            try
            {
                // Check if single instance enforcement is disabled
                if (disableSingleInstance)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "SingleInstance: Single instance enforcement disabled. Multiple instances allowed.");
                }
                else
                {
                    // Small delay to ensure process visibility
                    System.Threading.Thread.Sleep(100);

                    Process currentProcess = Process.GetCurrentProcess();
                    string processName = Path.GetFileNameWithoutExtension(currentProcess.ProcessName);
                    Process[] allInstances = Process.GetProcessesByName(processName);

                    // Check if this is a duplicate instance
                    if (allInstances.Length > 1)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                            $"SingleInstance: Duplicate instance detected. Found {allInstances.Length} instances. Writing trigger and exiting.");

                        // Write registry trigger for the original instance
                        bool registryWritten = RegistryHelper.WriteTrigger();

                        if (registryWritten)
                        {
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                                "SingleInstance: Registry trigger written successfully. Exiting duplicate instance.");
                        }
                        else
                        {
                            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                                "SingleInstance: Failed to write registry trigger. Still exiting duplicate instance.");
                        }

                        Environment.Exit(0);
                        return;
                    }

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "SingleInstance: This is the first instance. Continuing with normal startup.");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"SingleInstance: Error in single instance check: {ex.Message}. Continuing startup.");
            }





            // REMOVED: Legacy path override. 
            // ContainerDataManager.Initialize() now sets the correct Profile path internally.

            // Below added for reload function
            _currentTargetChecker = targetChecker;

            // Delete previous log file if setting is enabled
            if (SettingsManager.DeletePreviousLogOnStart)
            {
                try
                {
                    string logPath = System.IO.Path.Combine(OpsisContainers.DataRoot.Dir, "OpsisContainers.log");
                    if (System.IO.File.Exists(logPath))
                    {
                        System.IO.File.Delete(logPath);
                        // Note: Can't log this deletion since we just deleted the log file
                    }
                }
                catch
                {
                    // Silently ignore deletion errors to avoid startup issues
                }
            }


            BackupManager.CleanLastDeletedFolder();
            InterCore.Initialize();

            _options = new
            {
                IsSnapEnabled = SettingsManager.IsSnapEnabled,
                ShowBackgroundImageOnPortalContainers = SettingsManager.ShowBackgroundImageOnPortalContainers,
                Showintray = SettingsManager.ShowInTray,
                EnableSounds = SettingsManager.EnableSounds,
                TintValue = SettingsManager.TintValue,
                MenuTintValue = SettingsManager.MenuTintValue,
                MenuIcon = SettingsManager.MenuIcon,
                LockIcon = SettingsManager.LockIcon,
                SelectedColor = SettingsManager.SelectedColor,
                IsLogEnabled = SettingsManager.IsLogEnabled,
                singleClickToLaunch = SettingsManager.SingleClickToLaunch,
                LaunchEffect = SettingsManager.LaunchEffect,
                CheckNetworkPaths = false 
            };

            bool jsonLoadSuccessful = false;

            if (System.IO.File.Exists(ContainerDataManager.JsonFilePath))
            {
                jsonLoadSuccessful = LoadContainerDataFromJson();
            }

            // If JSON loading failed or file doesn't exist, initialize with defaults
            if (!jsonLoadSuccessful || ContainerDataManager.ContainerData == null || ContainerDataManager.ContainerData.Count == 0)
            {
                InitializeDefaultContainer();
            }
            else
            {
                // Only migrate if we successfully loaded existing data
                MigrateLegacyJson();
            }

            // Sanitize Portal Containers with missing target folders
            var invalidContainers = new List<dynamic>();
            foreach (dynamic container in ContainerDataManager.ContainerData.ToList()) // Use ToList to avoid collection modification issues
            {
                if (container.ItemsType?.ToString() == "Portal")
                {
                    string targetPath = container.Path?.ToString();
                    if (string.IsNullOrEmpty(targetPath) || !System.IO.Directory.Exists(targetPath))
                    {
                        invalidContainers.Add(container);
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerCreation, $"Marked Portal Container '{container.Title}' for removal due to missing target folder: {targetPath ?? "null"}");
                    }
                }
            }

            // Remove invalid containers and save
            if (invalidContainers.Any())
            {
                foreach (var container in invalidContainers)
                {
                    ContainerDataManager.ContainerData.Remove(container);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Removed Portal Container '{container.Title}' from ContainerDataManager.ContainerData");
                }
                ContainerDataManager.SaveContainerData();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Saved updated containers.json after removing {invalidContainers.Count} invalid Portal Containers");
            }

            // Clear any stuck transition states from previous session
            ClearAllTransitionStates();

            // Start emergency cleanup timer if not already running
            if (_transitionCleanupTimer == null)
            {
                _transitionCleanupTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(10) // Check every 10 seconds
                };
                _transitionCleanupTimer.Tick += (s, e) =>
                {
                    if (_containersInTransition.Count > 0)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerUpdate, $"Emergency cleanup: Found {_containersInTransition.Count} containers stuck in transition state");
                        ClearAllTransitionStates();
                    }
                };
                _transitionCleanupTimer.Start();
            }

            foreach (dynamic container in ContainerDataManager.ContainerData.ToList())
            {
                CreateContainer(container, targetChecker);
            }

            // --- NEW: Initialize Auto-Hide Engine ---
            InitializeAutoHideTimer();



            // Initialize Auto-Backup Timer
            BackupManager.InitializeAutoBackup();

        }



        // --- NEW: Draw Mode Integration ---

        public static void StartDrawMode()
        {
            // Called by App.xaml.cs (Context Menu) or InterCore (IPC)
            var overlay = new DrawContainerOverlay();
            overlay.Show();
        }

        public static void CreateContainerFromDraw(Rect r)
        {
            // 1. Generate Random Unique Name (Adjective + Place pattern)
            string defaultName = CoreUtilities.GenerateUniqueContainerName();

            // 2. Ask for Name (Pre-filled with the random name)
            string name = Microsoft.VisualBasic.Interaction.InputBox("Enter name for new Container:", "Create Container", defaultName);
            if (string.IsNullOrWhiteSpace(name)) return;

            // 2. Create Data
            // FIX: Changed "Standard" to "Data" so it functions correctly
            var container = ContainerDataManager.CreateNewContainer(name, "Data", r.X, r.Y);

            // 3. Update Dimensions & Title
            // CreateNewContainer uses defaults (230x130) and generates a random name for Data containers.
            // We must override these with the User's Input and the Drawn Size.
            var containerDict = container as System.Collections.Generic.IDictionary<string, object>;
            if (containerDict != null)
            {
                containerDict["Width"] = r.Width;
                containerDict["Height"] = r.Height;

                // FIX: Force set the title to what the user actually typed
                containerDict["Title"] = name;

                ContainerDataManager.SaveContainerData();
            }

            // 4. Refresh UI
            // Your ReloadContainers() correctly rebuilds the entire UI state
            ReloadContainers();
        }




        private static bool LoadContainerDataFromJson()
        {
            try
            {
                string jsonContent = System.IO.File.ReadAllText(ContainerDataManager.JsonFilePath);

                // Check if the file is empty or contains only whitespace
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerCreation,
                        "JSON file is empty or contains only whitespace. Using default container configuration.");
                    return false;
                }

                // First, try to parse as a list of containers
                try
                {
                    ContainerDataManager.ContainerData = JsonConvert.DeserializeObject<List<dynamic>>(jsonContent);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                        $"Successfully loaded {ContainerDataManager.ContainerData?.Count ?? 0} containers from JSON array.");
                    return ContainerDataManager.ContainerData != null;
                }
                catch (JsonSerializationException)
                {
                    // If that fails, try to parse as a single container object
                    try
                    {
                        var singleContainer = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                        if (singleContainer != null)
                        {
                            ContainerDataManager.ContainerData = new List<dynamic> { singleContainer };
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                                "Successfully loaded single container from JSON object.");
                            return true;
                        }
                    }
                    catch (JsonSerializationException innerEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                            $"Failed to parse JSON as single container object: {innerEx.Message}");
                    }
                }
            }
            catch (System.IO.IOException ioEx)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"IO error reading containers.json: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException accessEx)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Access denied reading containers.json: {accessEx.Message}");
            }
            catch (JsonReaderException jsonEx)
            {
                // Handle malformed JSON (syntax errors, invalid characters, etc.)
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Malformed JSON detected in containers.json: {jsonEx.Message}");

                // Optionally, create a backup of the corrupted file
                CreateCorruptedFileBackup();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Unexpected error loading containers.json: {ex.Message}");
            }

            return false;
        }

        private static void CreateCorruptedFileBackup()
        {
            try
            {
                string backupPath = ContainerDataManager.JsonFilePath + ".corrupted." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                System.IO.File.Copy(ContainerDataManager.JsonFilePath, backupPath, true);
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    $"Created backup of corrupted JSON file: {backupPath}");
            }
            catch (Exception backupEx)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Failed to create backup of corrupted JSON file: {backupEx.Message}");
            }
        }

        //private static void InitializeDefaultContainer()
        //{
        //    string defaultJson = "[{\"Id\":\"" + Guid.NewGuid().ToString() + "\",\"Title\":\"New Container\",\"X\":20,\"Y\":20,\"Width\":230,\"Height\":130,\"ItemsType\":\"Data\",\"Items\":[],\"IsLocked\":\"false\",\"IsHidden\":\"false\",\"CustomColor\":null,\"CustomLaunchEffect\":null,\"IsRolled\":\"false\",\"UnrolledHeight\":130,\"TabsEnabled\":\"false\",\"CurrentTab\":0,\"Tabs\":[]}]";
        //    System.IO.File.WriteAllText(ContainerDataManager.JsonFilePath, defaultJson);
        //    ContainerDataManager.ContainerData = JsonConvert.DeserializeObject<List<dynamic>>(defaultJson);
        //}
        //Replacement on v 2.5 .3. 141


        private static void InitializeDefaultContainer()
        {
            try
            {
                // 1. Create the Standard Data Container (For user icons)
                var dataContainer = new
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = Loc.T("New Container - Drop your shortcuts here"),
                    X = 20.0,
                    Y = 20.0,
                    Width = 360.0,
                    Height = 180.0,
                    ItemsType = "Data",
                    Items = new JArray(),

                    // Defaults
                    IsLocked = "false",
                    IsHidden = "false",
                    IsRolled = "false",
                    UnrolledHeight = 130.0,
                    TabsEnabled = "false",
                    CurrentTab = 0,
                    Tabs = new JArray(),

                    // Visual Defaults
                    CustomColor = (string)null,
                    ContainerBorderThickness = 2
                };

                // 2. Create the "Startup Tips" Note Container
                var noteContainer = new
                {
                    Id = Guid.NewGuid().ToString(), // Unique ID
                    Title = OpsisWelcomeTitle(), // Explicit Name
                    X = 20.0,   // Positioned below the data container
                    Y = 200.0,  // Data container ends then this container begins
                    Width = 555.0,
                    Height = 318.0,
                    ItemsType = "Note",
                    Items = new JArray(),

                    // Visuals from your spec
                    CustomColor = (string)null, // Default
                    TextColor = "Teal",         // Teal text

                    // Note Settings
                    NoteContent = OpsisWelcomeText(),

                    NoteFontSize = "Medium",
                    NoteFontFamily = "Segoe UI",
                    WordWrap = "true",
                    SpellCheck = "false",

                    // Standard Properties
                    IsHidden = "false",
                    IsLocked = "false",
                    IsRolled = "false",
                    UnrolledHeight = 318.0,
                    BoldTitleText = "false",
                    DisableTextShadow = "false",
                    IconSize = "Desktop",
                    IconSpacing = 5,
                    ContainerBorderThickness = 2
                };

                // 3. Combine and Save
                var containers = new List<object> { dataContainer };   // Opsis: no welcome note; the instructions live in the Opsis About page
                string defaultJson = JsonConvert.SerializeObject(containers, Formatting.Indented);

                System.IO.File.WriteAllText(ContainerDataManager.JsonFilePath, defaultJson);

                // Load into memory
                ContainerDataManager.ContainerData = JsonConvert.DeserializeObject<List<dynamic>>(defaultJson);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    "Initialized default configuration with Data Container and Startup Tips");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error initializing default containers: {ex.Message}");

                // Fallback to empty list if something explodes
                ContainerDataManager.ContainerData = new List<dynamic>();
            }
        }


        public static void CreateContainer(dynamic container, TargetChecker targetChecker)
        {

            // --- FIX: Declare Title TextBox EARLY ---
            TextBox titletb = new TextBox
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Background = System.Windows.Media.Brushes.White,
                Foreground = System.Windows.Media.Brushes.Black,
                Padding = new Thickness(2)
            };

            // --- NEW: Declare Commit Action for robust saving ---
            Action CommitRename = null;
            // ---------------------------------------------------

            // Check for valid Portal Container target folder
            if (container.ItemsType?.ToString() == "Portal")
            {
                string targetPath = container.Path?.ToString();
                if (string.IsNullOrEmpty(targetPath) || !System.IO.Directory.Exists(targetPath))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation, $"Skipping creation of Portal Container '{container.Title}' due to missing target folder: {targetPath ?? "null"}");
                    ContainerDataManager.ContainerData.Remove(container);
                    ContainerDataManager.SaveContainerData();
                    return;
                }
            }
            DockPanel dp = new DockPanel();
            // Get container border color and thickness from container data
            System.Windows.Media.Brush borderBrush = null;
            double borderThickness = 0;
            try
            {
                string borderColorName = container.ContainerBorderColor?.ToString();
                int customThickness = Convert.ToInt32(container.ContainerBorderThickness?.ToString() ?? "2");
                if (!string.IsNullOrEmpty(borderColorName))
                {
                    var borderColor = Utility.GetColorFromName(borderColorName);
                    borderBrush = new SolidColorBrush(borderColor);
                    borderThickness = customThickness; // Use custom thickness
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Applied border color '{borderColorName}' with thickness {borderThickness} to container '{container.Title}'");
                }
                else if (customThickness > 0)
                {
                    // Even without color, apply thickness with default color
                    borderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray);
                    borderThickness = customThickness;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Applied default border with thickness {borderThickness} to container '{container.Title}'");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Error applying border: {ex.Message}");
                borderBrush = null;
                borderThickness = 0;
            }
            Border cborder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 0, 0, 0)),
                CornerRadius = new CornerRadius(6),
                BorderBrush = borderBrush, // Apply border color
                BorderThickness = new Thickness(borderThickness), // Apply border thickness
                Child = dp
            };
            // Add Double Click Handler
            cborder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    SearchFormManager.ToggleSearch();
                    e.Handled = true;
                }
            };


            //  Add heart symbol in top-left corner
            string MenuSymbol = "⋯";

            if (SettingsManager.MenuIcon == 0)
            {
                MenuSymbol = "⋯";
              
            }
            else if (SettingsManager.MenuIcon == 1)
            {
                MenuSymbol = "☰";
            }
            else if (SettingsManager.MenuIcon == 2)
            {
                MenuSymbol = "≣";
            }
            else if (SettingsManager.MenuIcon == 3)
            {
                MenuSymbol = "𓃑";
            }

            TextBlock heart = new TextBlock
            {
                // Text = Loc.T("♥"),

                Name = "ContainerMenuIcon", // New! Name
                Text = MenuSymbol,
                FontSize = 18,
                Foreground = System.Windows.Media.Brushes.White, // Match title and icon text color
                Margin = new Thickness(5, -3, 0, 0), // Position top-left, aligned with title
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                Opacity = (double)SettingsManager.MenuTintValue / 100 // 0.3 // Lower tint by default

            };
  
            _heartTextBlocks[container] = heart;



            heart.MouseEnter += (s, e) =>
            {
                // Remove previous animation 
                heart.BeginAnimation(UIElement.OpacityProperty, null);

                heart.Opacity = 1.0;
            };

            heart.MouseLeave += (s, e) =>
            {
                double targetOpacity = (double)SettingsManager.MenuTintValue / 100;

                DoubleAnimation fadeBack = new DoubleAnimation
                {
                    From = 1.0,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(300),
                    BeginTime = TimeSpan.FromMilliseconds(800)
                };

                heart.BeginAnimation(UIElement.OpacityProperty, fadeBack);
            };


            // Opsis: the menu icon now lives inside the title grid (see below) so the title band spans the whole container width.
            Panel.SetZIndex(heart, 100); // Ensure heart is above titleGrid to receive clicks

            // Store heart TextBlock reference for this container
            _heartTextBlocks[container] = heart;
            // Create and assign heart ContextMenu using centralized builder
            // heart.ContextMenu = BuildHeartContextMenu(container);
            heart.ContextMenu = BuildHeartContextMenu(container, false); // Normal menu by default
                                                                     //// Handle left-click to open heart context menu
                                                                     //heart.MouseLeftButtonDown += (s, e) =>
                                                                     //{
                                                                     // if (e.ChangedButton == MouseButton.Left && heart.ContextMenu != null)
                                                                     // {
                                                                     // heart.ContextMenu.IsOpen = true;
                                                                     // e.Handled = true;
                                                                     // }
                                                                     //};
                                                                     // Handle left-click to open heart context menu
            heart.MouseLeftButtonDown += (s, e) =>
            {


                // FIX: Directly call CommitRename logic
                if (titletb.IsVisible)
                {
                    CommitRename?.Invoke();
                    return;
                }


                if (e.ChangedButton == MouseButton.Left && heart.ContextMenu != null)
                {
                    // Check if Ctrl is pressed for extended menu
                    bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    // DEBUG: Log the Ctrl state
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Heart clicked - Ctrl pressed: {isCtrlPressed}");
                    // Rebuild context menu based on Ctrl state
                    heart.ContextMenu = BuildHeartContextMenu(container, isCtrlPressed);
                    heart.ContextMenu.IsOpen = true;
                    e.Handled = true;
                }
            };
            // Add a protection symbol in top-right corner


            string LockSymbol = "🛡️";

            if (SettingsManager.LockIcon == 0)
            {
                LockSymbol = "🛡️";
            }
            else if (SettingsManager.LockIcon == 1)
            {
                LockSymbol = "🔑";
            }
            else if (SettingsManager.LockIcon == 2)
            {
                LockSymbol = "🔐";
            }
            else if (SettingsManager.LockIcon == 3)
            {
                LockSymbol = "🔒";
            }

            //MessageBox.Show(SettingsManager.LockIcon +" " + LockSymbol.ToString());


            TextBlock lockIcon = new TextBlock
           
            {

                //     Text = Loc.T("🔐"),
                //     Text = Loc.T("🔑"),
                //     Text = Loc.T("🔒"),
                //     Text = Loc.T("🔓"),
                Name = "ContainerLockIcon", // New! Name
                Text = LockSymbol,//"🛡️",
                                FontSize = 14,
                Foreground = container.IsLocked?.ToString().ToLower() == "true" ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 3, 2, 0), // Adjusted for top-right positioning
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand,
                ToolTip = container.IsLocked?.ToString().ToLower() == "true" ? "Container is locked (click to unlock)" : "Container is unlocked (click to lock)",
                Opacity = (double)SettingsManager.MenuTintValue / 100
            };

  
            lockIcon.MouseEnter += (s, e) =>
            {
                // Remove previous animation
                lockIcon.BeginAnimation(UIElement.OpacityProperty, null);

                lockIcon.Opacity = 1.0;
            };

            lockIcon.MouseLeave += (s, e) =>
            {
                double targetOpacity = (double)SettingsManager.MenuTintValue / 100;

                DoubleAnimation fadeBack = new DoubleAnimation
                {
                    From = 1.0,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(300),
                    BeginTime = TimeSpan.FromMilliseconds(800)
                };

                lockIcon.BeginAnimation(UIElement.OpacityProperty, fadeBack);
            };




            // Set initial state without saving to JSON
            UpdateLockState(lockIcon, container, null, saveToJson: false);
            // Lock icon click handler
            lockIcon.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {

                    // FIX: Directly call CommitRename logic
                    if (titletb.IsVisible)
                    {
                        CommitRename?.Invoke();
                        return;
                    }

                    // Get the NonActivatingWindow to find the container by Id
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(lockIcon);
                    if (win == null)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Could not find NonActivatingWindow for lock icon click in container '{container.Title}'");
                        return;
                    }
                    // Find the container in ContainerDataManager.ContainerData using the window's Tag (Id)
                    string containerId = win.Tag?.ToString();
                    if (string.IsNullOrEmpty(containerId))
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Container Id is missing for window '{win.Title}'");
                        return;
                    }
                    dynamic currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                    if (currentContainer == null)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Container with Id '{containerId}' not found in ContainerDataManager.ContainerData");
                        return;
                    }
                    // Toggle the lock state
                    bool currentState = currentContainer.IsLocked?.ToString().ToLower() == "true";
                    bool newState = !currentState;
                    // Update UI and JSON on the main thread
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateLockState(lockIcon, currentContainer, newState, saveToJson: true);
                    });
                }
            };



            // Create a Grid for the titlebar - move here to ensure it is created before mouse handler
            Grid titleGrid = new Grid
            {
                Name = "ContainerTitleGrid",
                MinHeight = SettingsManager.TitleBarHeight,
                Background = Utility.GetTitleBarBrush(Utility.ContainerProp((object)container, "CustomColor"), Utility.ContainerProp((object)container, "TitleBarColor"))
            };
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Pixel) }); // Col 0: Spacer
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: Title
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // Col 2: Filter Icon (Auto width)
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Pixel) }); // Col 3: Lock Icon
                                                                                                                      // End of ctrl+click handler
            ContextMenu CnMnContainerManager = new ContextMenu();
            // MenuItem miNewContainer = new MenuItem { Header = Loc.T("New Container") };
            // MenuItem miNewPortalContainer = new MenuItem { Header = Loc.T("New Portal Container") };
            MenuItem miNewNoteContainer = new MenuItem { Header = Loc.T("New Note Container") };
            // MenuItem miRemoveContainer = new MenuItem { Header = Loc.T("Delete Container") };
            // MenuItem miXT = new MenuItem { Header = Loc.T("Exit") };
            MenuItem miHide = new MenuItem { Header = Loc.T("Hide Container") }; // New Hide Container item
                                                                      // CnMnContainerManager.Items.Add(miRemoveContainer);
            CnMnContainerManager.Items.Add(miHide); // Add Hide Container
                                                // Add Note container specific context menu items if this is a Note container
            if (container.ItemsType?.ToString() == "Note")
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                    $"Adding Note container context menu items for '{container.Title}'");
                // We'll add the TextBox reference after the window is created
                // For now, just mark that this will need Note menu items
            }
            NonActivatingWindow win = new NonActivatingWindow
            {
                ContextMenu = CnMnContainerManager,
                AllowDrop = true,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Title = container.Title?.ToString() ?? "New Container", // Handle null title
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = cborder,
                ResizeMode = container.IsLocked?.ToString().ToLower() == "true" ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip,
                // ResizeMode = ResizeMode.CanResizeWithGrip,
                Width = (double)container.Width,
                Height = (double)container.Height,
                Top = (double)container.Y,
                Left = (double)container.X,
                Tag = container.Id?.ToString() ?? Guid.NewGuid().ToString() // Ensure ID exists
            };
            // Add Note container specific context menu items after window creation
            if (container.ItemsType?.ToString() == "Note")
            {
                // The TextBox will be created in InitContent(), so we need to add menu items after that
                // We'll modify the context menu after InitContent() is called
            }
            //Peek behind container
            // Opsis: Sort by (Data containers) and Exclude from auto-hide, ported from the old DeskContainers.
            if (container.ItemsType?.ToString() == "Data")
            {
                MenuItem miSortBy = new MenuItem { Header = Loc.T("Sort by") };
                foreach (var (label, key) in new[] { ("Name", "name"), ("Type", "type"), ("Date modified", "date"), ("Size", "size") })
                {
                    var sortItem = new MenuItem { Header = label };
                    string capturedKey = key;
                    sortItem.Click += (s, e) => SortContainerItems(win.Tag?.ToString(), capturedKey);
                    miSortBy.Items.Add(sortItem);
                }
                CnMnContainerManager.Items.Add(miSortBy);
            }
            MenuItem miExcludeAutoHide = new MenuItem { Header = Loc.T("Exclude from auto-hide"), IsCheckable = true, IsChecked = IsExcludedFromAutoHide(win) };
            miExcludeAutoHide.Click += (s, e) => UpdateContainerProperty(container, "ExcludeFromAutoHide", miExcludeAutoHide.IsChecked ? "true" : "false", $"Auto-hide exclusion for '{container.Title}'");
            CnMnContainerManager.Items.Add(miExcludeAutoHide);
            MenuItem miPeekBehind = new MenuItem { Header = Loc.T("Peek Behind") };
            CnMnContainerManager.Items.Add(miPeekBehind);
            miPeekBehind.Click += (s, e) =>
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Initiating Peek Behind for container '{container.Title}'");
                // Create a separate transparent window for the countdown
                var countdownWindow = new Window
                {
                    Width = 60,
                    Height = 40,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Left = win.Left + (win.Width - 60) / 2, // Center horizontally
                    Top = win.Top + (win.Height - 40) / 2 // Center vertically
                };
                // Create countdown label
                Label countdownLabel = new Label
                {
                    Content = Loc.T("10"),
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = System.Windows.Media.Brushes.Black,
                    Opacity = 0.7,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4)
                };
                countdownWindow.Content = countdownLabel;
                // Show the countdown window
                countdownWindow.Show();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Created countdown window for container '{container.Title}' at ({countdownWindow.Left}, {countdownWindow.Top})");
                // Fade out animation for the container
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                // Countdown timer
                int countdownSeconds = 10;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (timerSender, timerArgs) =>
                {
                    countdownSeconds--;
                    countdownLabel.Content = countdownSeconds.ToString();
                    if (countdownSeconds <= 0)
                    {
                        timer.Stop();
                        // Fade in animation for the container
                        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.8))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        };
                        win.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        // Close the countdown window
                        countdownWindow.Close();
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Peek Behind completed for container '{container.Title}'");
                    }
                };
                // Start fade out and timer
                win.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                timer.Start();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Started Peek Behind fade-out and countdown for container '{container.Title}'");
            };



            // --- DYNAMIC MENU ITEMS ---

            // 1. Clear Dead Shortcuts (Initially Hidden)
            MenuItem miClearDeadShortcuts = null;
            Separator sepClearDead = null;

            if (container.ItemsType?.ToString() == "Data")
            {
                sepClearDead = new Separator { Visibility = Visibility.Collapsed };
                CnMnContainerManager.Items.Add(sepClearDead);

                miClearDeadShortcuts = new MenuItem { Header = Loc.T("Clear Dead Shortcuts"), Visibility = Visibility.Collapsed };
                CnMnContainerManager.Items.Add(miClearDeadShortcuts);

                miClearDeadShortcuts.Click += (s, e) =>
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Clear Dead Shortcuts clicked for container '{container.Title}'");
                    try
                    {
                        int removedCount = FilePathUtilities.ClearDeadShortcutsFromContainer(container);
                        if (removedCount > 0)
                        {
                            ContainerDataManager.SaveContainerData();
                            RefreshContainerUsingFormApproach(win, container);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerUpdate, $"Error in Clear Dead Shortcuts: {ex.Message}");
                    }
                };
            }

            // 2. Open Folder (Portal Only)
            if (container.ItemsType?.ToString() == "Portal")
            {
                MenuItem miOpenFolder = new MenuItem { Header = Loc.T("Open container folder") };
                miOpenFolder.Click += (s, e) =>
                {
                    try
                    {
                        string folderPath = container.Path?.ToString();
                        if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                            Process.Start("explorer.exe", folderPath);
                    }
                    catch { }
                };
                CnMnContainerManager.Items.Add(miOpenFolder);
            }

            CnMnContainerManager.Items.Add(new Separator());

            // 3. Paste Item (Initially Hidden)
            MenuItem miPasteItem = new MenuItem { Header = Loc.T("Paste Item"), Visibility = Visibility.Collapsed };
            miPasteItem.Click += (s, e) => CopyPasteManager.PasteItem(container);
            CnMnContainerManager.Items.Add(miPasteItem);

 


            //  CnMnContainerManager.Items.Add(new Separator());

            //CnMnContainerManager.Items.Add(miNewCustomize);
            MenuItem miCustomize = new MenuItem { Header = Loc.T("Customize...") };
            miCustomize.Click += (s, e) =>
            {
                try
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Opening CustomizeContainerFormManager for container '{container.Title}'");
                    var customizeForm = new CustomizeContainerFormManager(container);
                    customizeForm.ShowDialog();
                    if (customizeForm.DialogResult)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"User saved changes in CustomizeContainerFormManager for container '{container.Title}'");
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"User cancelled CustomizeContainerFormManager for container '{container.Title}'");
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error opening CustomizeContainerFormManager: {ex.Message}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error opening customize form: {ex.Message}", "Form Error");
                }
            };
            CnMnContainerManager.Items.Add(miCustomize);
            // CnMnContainerManager.Items.Add(new Separator());
            // CnMnContainerManager.Items.Add(new Separator());
            // CnMnContainerManager.Items.Add(miXT);
            // Handle both JObject and ExpandoObject access
            string isHiddenString = "false"; // Default value
            try
            {
                if (container is JObject jContainer)
                {
                    // Existing container from JSON
                    isHiddenString = jContainer["IsHidden"]?.ToString().ToLower() ?? "false";
                }
                else
                {
                    // New ExpandoObject container
                    isHiddenString = (container.IsHidden?.ToString() ?? "false").ToLower();
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Error, $"Error reading IsHidden: {ex.Message}");
            }
            bool isHidden = isHiddenString == "true";
            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Container '{container.Title}' IsHidden state: {isHidden}");
            // Adjust the container position to ensure it fits within screen bounds
            AdjustContainerPositionToScreen(win);
            win.Loaded += (s, e) =>
            {
                UpdateLockState(lockIcon, container, null, saveToJson: false);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Applied lock state for container '{container.Title}' on load: IsLocked={container.IsLocked?.ToString().ToLower()}");
                // Apply IsRolled state
                bool isRolled = container.IsRolled?.ToString().ToLower() == "true";
                double targetHeight = SettingsManager.TitleBarHeight; // Default for rolled-up state  //rolled height
                if (!isRolled)
                {
                    double unrolledHeight = (double)container.Height; // Default to container.Height
                    if (container.UnrolledHeight != null)
                    {
                        if (double.TryParse(container.UnrolledHeight.ToString(), out double parsedHeight))
                        {
                            if (parsedHeight > 0)
                            {
                                unrolledHeight = parsedHeight;
                            }
                            else
                            {
                                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"UnrolledHeight {parsedHeight} is invalid (non-positive) for container '{container.Title}', using Height={unrolledHeight}");
                            }
                        }
                        else
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Failed to parse UnrolledHeight '{container.UnrolledHeight}' for container '{container.Title}', using Height={unrolledHeight}");
                        }
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"UnrolledHeight is null for container '{container.Title}', using Height={unrolledHeight}");
                    }
                    targetHeight = unrolledHeight;
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation, $"Applied rolled-down state for container '{container.Title}' on load: Height={targetHeight}");
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation, $"Applied rolled-up state for container '{container.Title}' on load: Height={targetHeight}");
                }
                win.Height = targetHeight;
                // Apply WrapPanel visibility
                var border = win.Content as Border;
                if (border != null)
                {
                    var dockPanel = border.Child as DockPanel;
                    if (dockPanel != null)
                    {
                        var scrollViewer = dockPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
                        if (scrollViewer != null)
                        {
                            var wpcont = scrollViewer.Content as WrapPanel;
                            if (wpcont != null)
                            {
                                wpcont.Visibility = isRolled ? Visibility.Collapsed : Visibility.Visible;
                                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Set initial WrapPanel visibility to {(isRolled ? "Collapsed" : "Visible")} for container '{container.Title}'");
                            }
                        }
                    }
                }
                if (isHidden)
                {
                    win.Visibility = Visibility.Hidden;
                    TrayManager.AddHiddenContainer(win);
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation, $"Hid container '{container.Title}' after loading at startup");
                }
            };
            // Step 5
            if (isHidden)
            {
                TrayManager.AddHiddenContainer(win);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Added container '{container.Title}' to hidden list at startup");
            }
            // Hide click
            miHide.Click += (s, e) =>
            {
                UpdateContainerProperty(container, "IsHidden", "true", $"Hid container '{container.Title}'");
                TrayManager.AddHiddenContainer(win);
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Triggered Hide Container for '{container.Title}'");
            };
            // Handle manual resize to update both Height and UnrolledHeight
            win.SizeChanged += (s, e) =>
            {
                // Get current container reference by ID to avoid stale references
                string containerId = win.Tag?.ToString();
                if (string.IsNullOrEmpty(containerId))
                {

                    return;
                }

                // Skip updates if this container is currently in a rollup/rolldown transition
                if (_containersInTransition.Contains(containerId))
                {

                    return;
                }
                // Find the current container in ContainerDataManager.ContainerData using ID
                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer != null)
                {

                    double newHeight = e.NewSize.Height;
                    double newWidth = e.NewSize.Width;
                    double oldHeight = Convert.ToDouble(currentContainer.Height?.ToString() ?? "0");
                    double oldUnrolledHeight = Convert.ToDouble(currentContainer.UnrolledHeight?.ToString() ?? "0");
                    bool isRolled = currentContainer.IsRolled?.ToString().ToLower() == "true";

                    // Update Width and Height with the actual new values
                    currentContainer.Width = newWidth;
                    currentContainer.Height = newHeight;

                    // Handle UnrolledHeight update
                    if (!isRolled)
                    {

                        if (Math.Abs(newHeight - SettingsManager.TitleBarHeight) > 5) // Only if height is significantly different from rolled-up height   //rolled height
                        {
                            double heightDifference = Math.Abs(newHeight - oldUnrolledHeight);
                            // DebugLog("LOGIC", containerId, $"Height difference from old UnrolledHeight: {heightDifference:F1}");
                            currentContainer.UnrolledHeight = newHeight;
                            // DebugLog("UPDATE", containerId, $"UPDATED UnrolledHeight from {oldUnrolledHeight:F1} to {newHeight:F1}");
                        }
                        else
                        {

                        }
                    }
                    else
                    {

                    }
                    // Save to JSON
                 //   MessageBox.Show("Debug: SizeChanged handler called. Saving container data.");
                    ContainerDataManager.SaveContainerData();
                    var verifyContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                    double verifyHeight = Convert.ToDouble(verifyContainer.Height?.ToString() ?? "0");
                    double verifyUnrolled = Convert.ToDouble(verifyContainer.UnrolledHeight?.ToString() ?? "0");

                }
                else
                {

                }
            };
            win.KeyDown += (sender, e) =>
            {
                if (IconDragDropManager.IsDragging && e.Key == Key.Escape)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Escape pressed during drag, cancelling operation");
                    IconDragDropManager.CancelDrag();
                    e.Handled = true;
                }


            };

            // --- NEW: Auto-Hide Interaction Hook ---
            win.PreviewMouseDown += (s, e) => { if (SettingsManager.AutoResetHideTimer) ResetAutoHideTimer(); };
            win.PreviewMouseWheel += (s, e) => { if (SettingsManager.AutoResetHideTimer) ResetAutoHideTimer(); };

            // --- NEW: Smart CTRL Overlay Engine ---
            DispatcherTimer ctrlPollTimer = null;
            bool isOverlayActive = false;

            void ToggleOverlays(bool turnOn)
            {
                if (isOverlayActive == turnOn) return; // Prevent redundant drawing
                isOverlayActive = turnOn;

                var borderNode = win.Content as Border;
                var dockNode = borderNode?.Child as DockPanel;
                var scrollNode = dockNode?.Children.OfType<ScrollViewer>().FirstOrDefault();
                var wpNode = scrollNode?.Content as WrapPanel;

                if (wpNode == null) return;

                foreach (StackPanel itemPanel in wpNode.Children.OfType<StackPanel>())
                {
                    dynamic tagData = itemPanel.Tag;
                    if (tagData == null) continue;

                    string fp = tagData.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();

                    if (turnOn)
                    {
                        if (fp != null && fp.StartsWith("INTERNAL_BLANK_"))
                        {
                            // Spacer Overlay: Faint White/Gray
                            itemPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
                        }
                        else
                        {
                            // Real Icon Overlay: Faint Blue
                            itemPanel.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 100, 150, 255));
                        }
                    }
                    else
                    {
                        // Revert to transparent (but still hit-testable)
                        itemPanel.Background = System.Windows.Media.Brushes.Transparent;
                    }
                }
            }

            win.MouseEnter += (s, e) =>
            {
                if (SettingsManager.AutoResetHideTimer) ResetAutoHideTimer();

                // --- NEW: Disable layout highlight for Portal Containers (since they auto-sort and cannot be manually arranged)
                if (container.ItemsType?.ToString() != "Portal")
                {
                    if (ctrlPollTimer == null)
                    {
                        ctrlPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                        ctrlPollTimer.Tick += (ts, te) =>
                        {
                            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                            ToggleOverlays(isCtrl);
                        };
                    }
                    ctrlPollTimer.Start();
                }
            };

            win.MouseLeave += (s, e) =>
            {
                if (container.ItemsType?.ToString() != "Portal")
                {
                    if (ctrlPollTimer != null) ctrlPollTimer.Stop();
                    ToggleOverlays(false); // Instantly hide if mouse leaves the container
                }
            };



            // Make window focusable for key events during drag
            win.Focusable = true;
            win.Show();



            // --- FIX START ---

            // 1. Install the Message Hook (Enforces the 90% size limit)
            win.SourceInitialized += (s, e) =>
            {
                var source = HwndSource.FromHwnd(new WindowInteropHelper(win).Handle);
                source?.AddHook(WndProc);
            };
            // (Safety: if already initialized)
            if (PresentationSource.FromVisual(win) is HwndSource existingSource)
            {
                existingSource.AddHook(WndProc);
            }

            // 2. Reactive State Fix (The "Trick")
            // If Snap Assist sets state to Maximized, we immediately force it back to Normal.
            // This ensures the Resize Grip (bottom-right) remains visible and functional.
            win.StateChanged += (sender, args) =>
            {
                if (win.WindowState == WindowState.Maximized)
                {
                    // We use Dispatcher to let the OS finish its "snap" calculation first, 
                    // then we immediately override the mode back to Normal.
                    win.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        win.WindowState = WindowState.Normal;

                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            "Snap Assist intercepted: Reverted Maximized state to Normal to preserve controls.");
                    }));
                }
            };


            // --- FIX END ---





            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
        $"Container '{container.Title}' successfully created and displayed");
  



            bool isBoldTitle = false;
            try
            {
                isBoldTitle = container.BoldTitleText?.ToString().ToLower() == "true";
            }
            catch { /* Safe fallback */ }
            // Get title text color or use default white
            System.Windows.Media.Brush titleTextBrush = System.Windows.Media.Brushes.White; // Default
            try
            {
                string titleColorName = container.TitleTextColor?.ToString();
                if (!string.IsNullOrEmpty(titleColorName))
                {
                    var titleColor = Utility.GetColorFromName(titleColorName);
                    titleTextBrush = new SolidColorBrush(titleColor);
                }
            }
            catch
            {
                titleTextBrush = System.Windows.Media.Brushes.White; // Fallback
            }
            // Get title text size from container data
            double titleFontSize = 12; // Default Medium size
            try
            {
                titleFontSize = Utility.TitleSizeToPoints(container.TitleTextSize?.ToString() ?? "Medium");
            }
            catch
            {
                titleFontSize = 12; // Fallback to Medium
            }
            Label titlelabel = new Label
            {
                Content = container.Title.ToString(),
                Foreground = titleTextBrush, // Changed from hardcoded White
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.SizeAll,
                FontFamily = new System.Windows.Media.FontFamily(string.IsNullOrEmpty(Utility.ContainerProp((object)container, "TitleFontFamily")) || Utility.ContainerProp((object)container, "TitleFontFamily") == "Default" ? "Segoe UI" : Utility.ContainerProp((object)container, "TitleFontFamily")),
                FontWeight = isBoldTitle ? FontWeights.Bold : FontWeights.SemiBold,
                FontSize = titleFontSize // Apply custom title text size
            };
            Grid.SetColumn(titlelabel, 1);
            titleGrid.Children.Add(titlelabel);

            // --- DYNAMIC STATE UPDATES (ON MENU OPEN) ---
            MenuItem miExportAllToDesktop = null;
            MenuItem miAddSpacerMenu = null; // REPLACED: Spacer Submenu
            Separator sepAfterSpacer = null; // NEW: Layout Separator
            MenuItem miNameAfterPath = null; // New: For Portal Renaming
            Separator sepNameAfterPath = null; // New: Separator for layout

            CnMnContainerManager.Opened += (contextSender, contextArgs) =>
            {
                // A. Update Paste Visibility
                bool hasCopiedItem = CopyPasteManager.HasCopiedItem();
                miPasteItem.Visibility = hasCopiedItem ? Visibility.Visible : Visibility.Collapsed;

                // B. Update Clear Dead Shortcuts Visibility
                if (miClearDeadShortcuts != null)
                {
                    bool hasDead = HasDeadShortcuts(container);
                    miClearDeadShortcuts.Visibility = hasDead ? Visibility.Visible : Visibility.Collapsed;
                    if (sepClearDead != null) sepClearDead.Visibility = hasDead ? Visibility.Visible : Visibility.Collapsed;
                }

                // --- CTRL + RIGHT CLICK LOGIC ---
                bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool isDataContainer = container.ItemsType?.ToString() == "Data";
                bool isPortalContainer = container.ItemsType?.ToString() == "Portal";



                // Clean up previous dynamic items to prevent duplicates
                if (miExportAllToDesktop != null && CnMnContainerManager.Items.Contains(miExportAllToDesktop))
                    CnMnContainerManager.Items.Remove(miExportAllToDesktop);

                if (miAddSpacerMenu != null && CnMnContainerManager.Items.Contains(miAddSpacerMenu))
                    CnMnContainerManager.Items.Remove(miAddSpacerMenu);

                if (sepAfterSpacer != null && CnMnContainerManager.Items.Contains(sepAfterSpacer))
                    CnMnContainerManager.Items.Remove(sepAfterSpacer);

                if (miNameAfterPath != null && CnMnContainerManager.Items.Contains(miNameAfterPath))


                    CnMnContainerManager.Items.Remove(miNameAfterPath);

                if (sepNameAfterPath != null && CnMnContainerManager.Items.Contains(sepNameAfterPath))
                    CnMnContainerManager.Items.Remove(sepNameAfterPath);

                // C. Export All & Spacer Menu (Data Container + Ctrl)
                if (isCtrlPressed && isDataContainer)
                {
                    miExportAllToDesktop = new MenuItem { Header = Loc.T("Export all icons to desktop") };
                    miExportAllToDesktop.Click += (s, e) => ExportAllIconsToDesktop(container);

                    miAddSpacerMenu = new MenuItem { Header = Loc.T("Add spacer") };

                    MenuItem miSpacerBlank = new MenuItem { Header = Loc.T("Blank") };
                    MenuItem miSpacerDot = new MenuItem { Header = Loc.T("Dot") };

                    // Centralized Spacer Creation Logic
                    Action<string> CreateSpacer = (prefix) =>
                    {
                        try
                        {
                            string id = container.Id?.ToString();
                            var liveContainer = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == id);
                            if (liveContainer == null) return;

                            JArray items = null;
                            bool tabsEnabled = liveContainer.TabsEnabled?.ToString().ToLower() == "true";
                            if (tabsEnabled)
                            {
                                var tabs = liveContainer.Tabs as JArray ?? new JArray();
                                int currentTab = Convert.ToInt32(liveContainer.CurrentTab?.ToString() ?? "0");
                                if (currentTab >= 0 && currentTab < tabs.Count)
                                {
                                    var activeTab = tabs[currentTab] as JObject;
                                    items = activeTab?["Items"] as JArray ?? new JArray();
                                }
                            }
                            if (items == null) items = liveContainer.Items as JArray ?? new JArray();

                            int nextDisplayOrder = items.Count > 0 ? items.Max(i => i["DisplayOrder"]?.Value<int>() ?? 0) + 1 : 0;

                            dynamic newItem = new System.Dynamic.ExpandoObject();
                            IDictionary<string, object> newItemDict = newItem;
                            newItemDict["Filename"] = prefix + "_" + Guid.NewGuid().ToString();
                            newItemDict["DisplayName"] = "";
                            newItemDict["IsFolder"] = false;
                            newItemDict["IsLink"] = false;
                            newItemDict["IsNetwork"] = false;
                            newItemDict["AlwaysRunAsAdmin"] = false;
                            newItemDict["DisplayOrder"] = nextDisplayOrder;

                            items.Add(JObject.FromObject(newItem));

                            if (tabsEnabled && Convert.ToInt32(liveContainer.CurrentTab?.ToString() ?? "0") == 0)
                                SynchronizeTab0Content(id, "tab0", "add");
                            else if (!tabsEnabled)
                                SynchronizeTab0Content(id, "main", "add");

                            ContainerDataManager.SaveContainerData();
                            RefreshContainerUsingFormApproach(win, liveContainer);
                        }
                        catch (Exception ex)
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error adding spacer: {ex.Message}");
                        }
                    };

                    miSpacerBlank.Click += (s, e) => CreateSpacer("INTERNAL_BLANK_EMPTY");
                    miSpacerDot.Click += (s, e) => CreateSpacer("INTERNAL_BLANK_DOT");

                    miAddSpacerMenu.Items.Add(miSpacerBlank);
                    miAddSpacerMenu.Items.Add(miSpacerDot);

                    sepAfterSpacer = new Separator();

                    // Insert before Customize (safe lookup)
                    int insertIndex = CnMnContainerManager.Items.Count - 1;
                    var customizeItem = CnMnContainerManager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Loc.T("Customize..."));
                    if (customizeItem != null) insertIndex = CnMnContainerManager.Items.IndexOf(customizeItem);

                    CnMnContainerManager.Items.Insert(insertIndex, miExportAllToDesktop);
                    CnMnContainerManager.Items.Insert(insertIndex + 1, miAddSpacerMenu);
                    CnMnContainerManager.Items.Insert(insertIndex + 2, sepAfterSpacer);
                }
                // D. Name After Target (Portal Container + Ctrl)
                if (isCtrlPressed && isPortalContainer)
                {
                    miNameAfterPath = new MenuItem { Header = Loc.T("Name Container After Target Path") };
                    miNameAfterPath.Click += (s, e) =>
                    {
                        // Get Base Path (Not navigation path)
                        string targetPath = container.Path?.ToString();

                        if (!string.IsNullOrEmpty(targetPath))
                        {
                            // 1. Update Live Object (Global List)
                            string id = container.Id?.ToString();
                            var liveContainer = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == id);

                            if (liveContainer != null)
                            {
                                if (liveContainer is JObject jContainer) jContainer["Title"] = targetPath;
                                else liveContainer.Title = targetPath;

                                container.Title = targetPath; // Update local reference
                            }

                            // 2. Update UI
                            titlelabel.Content = targetPath;
                            win.Title = targetPath;
                            titletb.Text = targetPath; // Update hidden textbox too

                            // 3. Save
                            ContainerDataManager.SaveContainerData();
                            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Renamed portal container to target: {targetPath}");
                        }
                    };

                    sepNameAfterPath = new Separator();

                    // Insert before Customize
                    int insertIndex = CnMnContainerManager.Items.Count - 1;
                    var customizeItem = CnMnContainerManager.Items.OfType<MenuItem>()
                        .FirstOrDefault(m => m.Header.ToString() == Loc.T("Customize..."));

                    if (customizeItem != null)
                    {
                        insertIndex = CnMnContainerManager.Items.IndexOf(customizeItem);
                        // Insert Order: Name -> Separator -> Customize
                        CnMnContainerManager.Items.Insert(insertIndex, miNameAfterPath);
                        CnMnContainerManager.Items.Insert(insertIndex + 1, sepNameAfterPath);
                    }
                    else
                    {
                        CnMnContainerManager.Items.Add(miNameAfterPath);
                        CnMnContainerManager.Items.Add(sepNameAfterPath);
                    }
                }

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Updated container menu - Ctrl:{isCtrlPressed}");
            };


            Grid.SetColumn(titletb, 1);
            titleGrid.Children.Add(titletb);

            // --- FILTER UI START ---
            // Only add Filter UI for Portal Containers
            Grid filterBar = null;
            if (container.ItemsType?.ToString() == "Portal")
            {
                // 1. The Filter Icon (Funnel)
                // 1. The Filter Icon (Funnel)
                string initialFilter = GetSafeProperty(container, "FilterString");
                bool hasInitialFilter = !string.IsNullOrWhiteSpace(initialFilter);

                TextBlock filterIcon = new TextBlock
                {
                    Name = "ContainerFilterIcon", // New! Name
                    Text = Loc.T("❖"),
                    FontSize = 18,
                    Foreground = hasInitialFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = Loc.T("Filter files (e.g. '*.jpg' or '>*.tmp' to exclude)"),
                    Opacity = hasInitialFilter ? 1.0 : ((double)SettingsManager.MenuTintValue / 100)
                };

                filterIcon.MouseEnter += (s, e) =>
                {
                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = 1.0;
                };

                filterIcon.MouseLeave += (s, e) =>
                {
                    // Skip fade out if a filter is actively applied
                    if (filterIcon.Foreground != System.Windows.Media.Brushes.Orange)
                    {
                        double targetOpacity = (double)SettingsManager.MenuTintValue / 100;
                        DoubleAnimation fadeBack = new DoubleAnimation
                        {
                            From = 1.0,
                            To = targetOpacity,
                            Duration = TimeSpan.FromMilliseconds(300),
                            BeginTime = TimeSpan.FromMilliseconds(800)
                        };
                        filterIcon.BeginAnimation(UIElement.OpacityProperty, fadeBack);
                    }
                };

                Grid.SetColumn(filterIcon, 2);
                titleGrid.Children.Add(filterIcon);




                // 2. The Filter Bar (Stable Version)
                filterBar = new Grid
                {
                    Height = 26,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 0, 0, 0)),
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                ComboBox cmbFilter = new ComboBox
                {
                    IsEditable = true,
                    Height = 24,
                    StaysOpenOnEdit = true,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(5, 0, 5, 0),
                    Text = GetSafeProperty(container, "FilterString")
                };

                // Helper: Only Repopulate when needed (prevents freeze loop)
                Action repopulateDropdown = () =>
                {
                    cmbFilter.Items.Clear();

                    // A. History
                    try
                    {
                        string currentId = container.Id?.ToString();
                        var liveContainer = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == currentId);
                        if (liveContainer != null)
                        {
                            Newtonsoft.Json.Linq.JArray history = null;
                            if (liveContainer is Newtonsoft.Json.Linq.JObject jContainer)
                                history = jContainer["FilterHistory"] as Newtonsoft.Json.Linq.JArray;
                            else
                                history = liveContainer.GetType().GetProperty("FilterHistory")?.GetValue(liveContainer) as Newtonsoft.Json.Linq.JArray;

                            if (history != null)
                            {
                                foreach (var item in history)
                                {
                                    cmbFilter.Items.Add(new ComboBoxItem
                                    {
                                        Content = item.ToString(),
                                        Tag = item.ToString(),
                                        FontWeight = FontWeights.Bold
                                    });
                                }
                            }
                        }
                    }
                    catch { }

                    // B. Separator (if needed)
                    if (cmbFilter.Items.Count > 0) cmbFilter.Items.Add(new Separator());

                    // C. Standard Presets (From static dictionary)
                    foreach (var kvp in _standardPresets)
                    {
                        cmbFilter.Items.Add(new ComboBoxItem { Content = kvp.Key, Tag = kvp.Value });
                    }
                };

                // FIX 1: Only populate on OPEN, never during selection/typing
                cmbFilter.DropDownOpened += (s, e) => repopulateDropdown();

                // Clear Button
                Button btnClearFilter = new Button
                {
                    Content = Loc.T("✕"),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Width = 20,
                    ToolTip = Loc.T("Clear filter and close")
                };

                filterBar.Children.Add(cmbFilter);
                filterBar.Children.Add(btnClearFilter);
                Grid.SetColumn(btnClearFilter, 1);

                // --- LOGIC SETUP ---

                // Helper to execute/save
                Action<string> commitFilter = (text) =>
                {
                    bool hasFilter = !string.IsNullOrWhiteSpace(text);
                    filterIcon.Foreground = hasFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White;

                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = hasFilter ? 1.0 : ((double)SettingsManager.MenuTintValue / 100);

                    if (_portalContainers.ContainsKey(container))
                        _portalContainers[container].ApplyFilter(text);

                    UpdateContainerProperty(container, "FilterString", text, "Updated filter");

                    // Update history (logic now handles ignoring presets)
                    UpdateFilterHistory(container, text);
                };

                // Typing Timer
                System.Windows.Threading.DispatcherTimer debounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                debounceTimer.Tick += (s, e) =>
                {
                    debounceTimer.Stop();
                    // Visual update only while typing
                    if (_portalContainers.ContainsKey(container)) _portalContainers[container].ApplyFilter(cmbFilter.Text);

                    bool hasFilter = !string.IsNullOrWhiteSpace(cmbFilter.Text);
                    filterIcon.Foreground = hasFilter ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.White;

                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = hasFilter ? 1.0 : ((double)SettingsManager.MenuTintValue / 100);
                };

                // 1. STYLE FIX (Internal TextBox)
                cmbFilter.Loaded += (s, e) =>
                {
                    var textBox = (TextBox)cmbFilter.Template.FindName("PART_EditableTextBox", cmbFilter);
                    if (textBox != null)
                    {
                        textBox.Background = System.Windows.Media.Brushes.Transparent;
                        textBox.Foreground = System.Windows.Media.Brushes.White;
                        textBox.CaretBrush = System.Windows.Media.Brushes.White;
                        textBox.BorderThickness = new Thickness(0);
                    }
                };

                // 2. TYPING LOGIC
                cmbFilter.KeyUp += (s, e) =>
                {
                    if (e.Key == Key.Up || e.Key == Key.Down) return;

                    if (e.Key == Key.Enter)
                    {
                        commitFilter(cmbFilter.Text); // Save history on Enter
                        cmbFilter.IsDropDownOpen = false;
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                        e.Handled = true;
                        return;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                        e.Handled = true;
                        return;
                    }

                    debounceTimer.Stop();
                    debounceTimer.Start();
                };

                // 3. SELECTION LOGIC (FIXED)
                // We do NOT repopulate here. We just apply the value.
                cmbFilter.SelectionChanged += (s, e) =>
                {
                    if (cmbFilter.SelectedItem is ComboBoxItem item && item.Tag != null)
                    {
                        string selectedFilter = item.Tag.ToString();

                        // Break the event loop by running this later
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            cmbFilter.IsDropDownOpen = false;

                            // Important: Don't set SelectedItem = null here, it causes flickering.
                            // Just force the text and run logic.
                            cmbFilter.Text = selectedFilter;

                            debounceTimer.Stop();
                            commitFilter(selectedFilter);
                        }));
                    }
                };

                // 4. CLEAR LOGIC
                btnClearFilter.Click += (s, e) =>
                {
                    cmbFilter.Text = "";
                    if (_portalContainers.ContainsKey(container)) _portalContainers[container].ApplyFilter("");
                    UpdateContainerProperty(container, "FilterString", "", "Cleared filter");
                    filterIcon.Foreground = System.Windows.Media.Brushes.White;
                    filterIcon.BeginAnimation(UIElement.OpacityProperty, null);
                    filterIcon.Opacity = (double)SettingsManager.MenuTintValue / 100;
                    filterBar.Visibility = Visibility.Collapsed;
                    win.EndKeyboardInteractiveEdit();
                };

                // 5. TOGGLE BAR
                filterIcon.MouseLeftButtonDown += (s, e) =>
                {
                    if (filterBar.Visibility == Visibility.Visible)
                    {
                        if (!string.IsNullOrWhiteSpace(cmbFilter.Text)) commitFilter(cmbFilter.Text); // Save on close
                        filterBar.Visibility = Visibility.Collapsed;
                        win.EndKeyboardInteractiveEdit();
                    }
                    else
                    {
                        filterBar.Visibility = Visibility.Visible;
                        win.BeginKeyboardInteractiveEdit(cmbFilter);
                    }
                    e.Handled = true;
                };

                // Initial Apply
                if (!string.IsNullOrEmpty(cmbFilter.Text))
                {
                    filterIcon.Foreground = System.Windows.Media.Brushes.Orange;
                    filterIcon.Opacity = 1.0; // Ensure it starts fully visible if active

                    win.Loaded += (s, e) =>
                    {
                        if (_portalContainers.ContainsKey(container))
                            _portalContainers[container].ApplyFilter(cmbFilter.Text);
                    };
                }
            }
            // --- FILTER UI END ---


            // --- STEP 4 FIX: Centralized Rename Logic ---

            // 1. Define the Logic ONCE
            CommitRename = () =>
            {
                // Only run if actually editing
                if (titletb.Visibility != Visibility.Visible) return;

                string originalTitle = container.Title.ToString();
                string newTitle = titletb.Text;
                string finalTitle = InterCore.ProcessTitleChange(container, newTitle, originalTitle);

                // Update Data
                string id = container.Id?.ToString();
                var liveContainer = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == id);

                if (liveContainer != null)
                {
                    if (liveContainer is Newtonsoft.Json.Linq.JObject jContainer)
                        jContainer["Title"] = finalTitle;
                    else
                        liveContainer.Title = finalTitle;
                    container.Title = finalTitle;
                }

                // Update UI
                titlelabel.Content = finalTitle;
                win.Title = finalTitle;
                titletb.Visibility = Visibility.Collapsed;
                titlelabel.Visibility = Visibility.Visible;

                // Save
                ContainerDataManager.SaveContainerData();
                win.EndKeyboardInteractiveEdit();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Rename committed: {finalTitle}");
            };

            // 2. Wire up Events to use the central logic
            titletb.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitRename(); // Call shared logic
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    // Cancel Logic
                    titletb.Text = container.Title.ToString();
                    titletb.Visibility = Visibility.Collapsed;
                    titlelabel.Visibility = Visibility.Visible;
                    win.EndKeyboardInteractiveEdit();
                    e.Handled = true;
                }
            };

            titletb.LostFocus += (sender, e) =>
            {
                CommitRename(); // Call shared logic
            };



            // --- STEP 4 START: Configure and Add Events ---
            titletb.HorizontalContentAlignment = HorizontalAlignment.Center;
            titletb.Visibility = Visibility.Collapsed;

            // 1. Handle Keys (Enter = Save, Escape = Cancel)
            titletb.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    string originalTitle = container.Title.ToString();
                    string newTitle = titletb.Text;
                    string finalTitle = InterCore.ProcessTitleChange(container, newTitle, originalTitle);

                    // Update LIVE Data
                    string id = container.Id?.ToString();
                    var liveContainer = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == id);

                    if (liveContainer != null)
                    {
                        if (liveContainer is Newtonsoft.Json.Linq.JObject jContainer)
                            jContainer["Title"] = finalTitle;
                        else
                            liveContainer.Title = finalTitle;
                        container.Title = finalTitle;
                    }

                    titlelabel.Content = finalTitle;
                    win.Title = finalTitle;
                    titletb.Visibility = Visibility.Collapsed;
                    titlelabel.Visibility = Visibility.Visible;

                    ContainerDataManager.SaveContainerData();
                    win.EndKeyboardInteractiveEdit();
                }
                else if (e.Key == Key.Escape)
                {
                    // ESCAPE: Cancel and Revert
                    titletb.Text = container.Title.ToString();
                    titletb.Visibility = Visibility.Collapsed;
                    titlelabel.Visibility = Visibility.Visible;
                    win.EndKeyboardInteractiveEdit();
                    e.Handled = true;

                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Rename cancelled via Escape");
                    }
                }
                ;

            // 2. Handle Focus Loss (Save when clicking away)
            titletb.LostFocus += (sender, e) =>
            {
                // If invisible, we already handled it (e.g. via Escape)
                if (titletb.Visibility != Visibility.Visible) return;

                string originalTitle = container.Title.ToString();
                string newTitle = titletb.Text;
                string finalTitle = InterCore.ProcessTitleChange(container, newTitle, originalTitle);

                string id = container.Id?.ToString();
                var liveContainer = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == id);

                if (liveContainer != null)
                {
                    if (liveContainer is Newtonsoft.Json.Linq.JObject jContainer)
                        jContainer["Title"] = finalTitle;
                    else
                        liveContainer.Title = finalTitle;
                    container.Title = finalTitle;
                }

                titlelabel.Content = finalTitle;
                win.Title = finalTitle;
                titletb.Visibility = Visibility.Collapsed;
                titlelabel.Visibility = Visibility.Visible;

                ContainerDataManager.SaveContainerData();
                win.EndKeyboardInteractiveEdit();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Rename saved via LostFocus: {finalTitle}");
            };
            // --- STEP 4 END ---


            // Move lockIcon to the Grid
            Grid.SetColumn(lockIcon, 3); // Moved to Column 3
            Grid.SetRow(lockIcon, 0);
            titleGrid.Children.Add(lockIcon);
            Grid.SetColumnSpan(heart, 4);
            titleGrid.Children.Add(heart);
            // Add the titleGrid to the DockPanel
            DockPanel.SetDock(titleGrid, Dock.Top);
            dp.Children.Add(titleGrid);
            if (filterBar != null)
            {
                DockPanel.SetDock(filterBar, Dock.Top);
                dp.Children.Add(filterBar);
            }




            // TABS FEATURE: Add TabStrip
            // We delegate to the shared method to ensure consistent "Scrollable" layout on startup.
            RefreshTabStripUI(win, container);

           string containerId = win.Tag?.ToString();
            if (string.IsNullOrEmpty(containerId))
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Container Id is missing for window '{win.Title}'");
                return;
            }
            dynamic currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
            if (currentContainer == null)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Container with Id '{containerId}' not found in ContainerDataManager.ContainerData");
                return;
            }
            bool isLocked = currentContainer.IsLocked?.ToString().ToLower() == "true";
            // Opsis: F2 renames the container under the mouse (same inline edit as Ctrl+Click on the title).
            RenameStarters[containerId] = () =>
            {
                var rw = FindVisualParent<NonActivatingWindow>(titlelabel);
                if (rw == null || titletb.IsVisible) return;
                titletb.Text = titlelabel.Content.ToString();
                titlelabel.Visibility = Visibility.Collapsed;
                titletb.Visibility = Visibility.Visible;
                rw.BeginKeyboardInteractiveEdit(titletb);
            };
            // Opsis: roll-up toggle extracted so the hover auto-roll-up can reuse the exact same animation/state code.
            Action toggleRoll = () =>
            {
                    // Roll-up/roll-down logic (swapped from Ctrl+Click)
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(titlelabel);
                    string containerId = win?.Tag?.ToString();
                    // DebugLog("IMMEDIATE", containerId ?? "UNKNOWN", $"Ctrl+Click FIRST LINE - win.Height:{win?.Height ?? -1:F1}");
                    if (string.IsNullOrEmpty(containerId) || win == null)
                    {
                        // DebugLog("ERROR", containerId ?? "UNKNOWN", "Missing window or containerId in Ctrl+Click");
                        return;
                    }
                    // DebugLog("EVENT", containerId, "Ctrl+Click TRIGGERED");
                    // DebugLogContainerState("CTRL_CLICK_START", containerId, win);
                    dynamic currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                    if (currentContainer == null)
                    {
                        // DebugLog("ERROR", containerId, "Container not found in ContainerDataManager.ContainerData for Ctrl+Click");
                        return;
                    }
                    bool isRolled = currentContainer.IsRolled?.ToString().ToLower() == "true";
                    // Get container data height (always accurate from SizeChanged handler)
                    double containerHeight = Convert.ToDouble(currentContainer.Height?.ToString() ?? "130");
                    double windowHeight = win.Height;
                    // DebugLog("SYNC_CHECK", containerId, $"Before sync - ContainerHeight:{containerHeight:F1} | WindowHeight:{windowHeight:F1} | IsRolled:{isRolled}");
                    if (!isRolled)
                    {
                        // ROLLUP: Use container data height (always current)
                        // DebugLog("ACTION", containerId, "Starting ROLLUP");
                        _containersInTransition.Add(containerId);
                        // DebugLog("TRANSITION", containerId, "Added to transition state");
                        // FINAL FIX: Use container data height which is always accurate from SizeChanged handler
                        double currentHeight = containerHeight;

                        // --- BUG FIX: The "Stuck at 28" Safeguard ---
                        // If currentHeight is suspiciously small (like the title bar height), it means the 
                        // WPF layout data is stale. We reject it and use the last known good UnrolledHeight.
                        if (currentHeight <= 35)
                        {
                            currentHeight = Convert.ToDouble(currentContainer.UnrolledHeight?.ToString() ?? "130");
                            if (currentHeight <= 35) currentHeight = 130; // Ultimate fallback
                        }

                        // DebugLog("ROLLUP_HEIGHT_SOURCE", containerId, $"Using container.Height:{containerHeight:F1} (win.Height was stale:{win.Height:F1})");
                        // Save current container height as UnrolledHeight
                        IDictionary<string, object> containerDict = currentContainer as IDictionary<string, object> ?? ((JObject)currentContainer).ToObject<IDictionary<string, object>>();
                        containerDict["UnrolledHeight"] = currentHeight;
                        containerDict["IsRolled"] = "true";
                        int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                        if (containerIndex >= 0)
                        {
                            ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                        }
                        ContainerDataManager.SaveContainerData();
                        // DebugLog("SAVE", containerId, $"Saved ROLLUP state | UnrolledHeight:{currentHeight:F1} | IsRolled:true");
                        // Roll up animation - starts from current height
                        double targetHeight = SettingsManager.TitleBarHeight;   //rolled height
                        var heightAnimation = new DoubleAnimation(currentHeight, targetHeight, TimeSpan.FromSeconds(0.3))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        heightAnimation.Completed += (animSender, animArgs) =>
                        {
                            // DebugLog("ANIMATION", containerId, "ROLLUP animation completed");
                            // Update WrapPanel visibility
                            var border = win.Content as Border;
                            if (border != null)
                            {
                                var dockPanel = border.Child as DockPanel;
                                if (dockPanel != null)
                                {
                                    var scrollViewer = dockPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
                                    if (scrollViewer != null)
                                    {
                                        var wpcont = scrollViewer.Content as WrapPanel;
                                        if (wpcont != null)
                                        {
                                            wpcont.Visibility = Visibility.Collapsed;
                                            // DebugLog("UI", containerId, "Set WrapPanel visibility to Collapsed");
                                        }
                                    }
                                }
                            }
                            _containersInTransition.Remove(containerId);

                            // FIX: Release animation lock so manual resizes track correctly later
                            win.BeginAnimation(Window.HeightProperty, null);
                            win.Height = targetHeight;
                        };
                        win.BeginAnimation(Window.HeightProperty, heightAnimation);
                        // DebugLog("ANIMATION", containerId, $"Started ROLLUP animation from {currentHeight:F1} to height {targetHeight:F1}");
                    }
                    else
                    {
                        // ROLLDOWN: Roll down to UnrolledHeight
                        double unrolledHeight = Convert.ToDouble(currentContainer.UnrolledHeight?.ToString() ?? "130");
                        // // DebugLog("ACTION", containerId, $"Starting ROLLDOWN to {unrolledHeight:F1}");
                        _containersInTransition.Add(containerId);
                        // DebugLog("TRANSITION", containerId, "Added to transition state");
                        IDictionary<string, object> containerDict = currentContainer as IDictionary<string, object> ?? ((JObject)currentContainer).ToObject<IDictionary<string, object>>();
                        containerDict["IsRolled"] = "false";
                        int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                        if (containerIndex >= 0)
                        {
                            ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                        }
                        ContainerDataManager.SaveContainerData();
                        // DebugLog("SAVE", containerId, $"Saved ROLLDOWN state | IsRolled:false | TargetHeight:{unrolledHeight:F1}");
                        var heightAnimation = new DoubleAnimation(win.Height, unrolledHeight, TimeSpan.FromSeconds(0.3))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                        };
                        heightAnimation.Completed += (animSender, animArgs) =>
                        {
                            // DebugLog("ANIMATION", containerId, "ROLLDOWN animation completed");
                            // Update WrapPanel visibility
                            var border = win.Content as Border;
                            if (border != null)
                            {
                                var dockPanel = border.Child as DockPanel;
                                if (dockPanel != null)
                                {
                                    var scrollViewer = dockPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
                                    if (scrollViewer != null)
                                    {
                                        var wpcont = scrollViewer.Content as WrapPanel;
                                        if (wpcont != null)
                                        {
                                            wpcont.Visibility = Visibility.Visible;
                                            // DebugLog("UI", containerId, "Set WrapPanel visibility to Visible");
                                        }
                                    }
                                }
                            }
                            _containersInTransition.Remove(containerId);

                            // FIX: Release animation lock and force real height so SizeChanged updates the JSON properly
                            win.BeginAnimation(Window.HeightProperty, null);
                            win.Height = unrolledHeight;
                        };
                        win.BeginAnimation(Window.HeightProperty, heightAnimation);
                        // DebugLog("ANIMATION", containerId, $"Started ROLLDOWN animation to height {unrolledHeight:F1}");
                    }
            };
            RollToggles[containerId] = toggleRoll;
            EnsureHoverRollTimer();
            titlelabel.MouseDown += (sender, e) =>
            {
                // FIX: Directly call CommitRename logic
                if (titletb.IsVisible)
                {
                    CommitRename?.Invoke();
                    return;
                }

                if (e.ClickCount == 2)
                {
                    toggleRoll();
                    e.Handled = true;
                }
                else if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        // Rename container (swapped from double-click)
                        titletb.Text = titlelabel.Content.ToString();
                        titlelabel.Visibility = Visibility.Collapsed;
                        titletb.Visibility = Visibility.Visible;
                        win.BeginKeyboardInteractiveEdit(titletb);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Focus set to title textbox for container: {container.Title}");
                        e.Handled = true;
                    }
                    else
                    {
                        string containerId = win.Tag?.ToString();
                        if (string.IsNullOrEmpty(containerId))
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Container Id is missing for window '{win.Title}' during MouseDown");
                            return;
                        }
                        dynamic currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                        if (currentContainer == null)
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Container with Id '{containerId}' not found in ContainerDataManager.ContainerData during MouseDown");
                            return;
                        }
                        bool isLocked = currentContainer.IsLocked?.ToString().ToLower() == "true";
                        if (!isLocked)
                        {
                            win.DragMove();
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Dragging container '{currentContainer.Title}'");
                        }
                        else
                        {
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"DragMove blocked for locked container '{currentContainer.Title}'");
                        }
                    }
                }
            };



            titletb.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    string originalTitle = container.Title.ToString();
                    string newTitle = titletb.Text;

                    // Process through InterCore for special triggers
                    string finalTitle = InterCore.ProcessTitleChange(container, newTitle, originalTitle);

                    // --- FIX START: Update LIVE Data ---
                    // Get the ID to find the fresh object in the global list
                    string id = container.Id?.ToString();
                    var liveContainer = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == id);

                    if (liveContainer != null)
                    {
                        // Update the live object in the list
                        // Handle both JObject (JSON) and ExpandoObject (New Container)
                        if (liveContainer is Newtonsoft.Json.Linq.JObject jContainer)
                            jContainer["Title"] = finalTitle;
                        else
                            liveContainer.Title = finalTitle;

                        // Also update the local reference just in case
                        container.Title = finalTitle;
                    }
                    // --- FIX END ---

                    // Update UI
                    titlelabel.Content = finalTitle;
                    win.Title = finalTitle;
                    titletb.Visibility = Visibility.Collapsed;
                    titlelabel.Visibility = Visibility.Visible;

                    // Save the global list which now contains the updated title
                    ContainerDataManager.SaveContainerData();

                    win.ShowActivated = false;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Exited edit mode via Enter, final title for container: {finalTitle}");
                    win.Focus();
                }
                else if (e.Key == Key.Escape)
                {
                    // FIX: ESCAPE Logic (Cancel)
                    titletb.Text = container.Title.ToString(); // Revert text
                    titletb.Visibility = Visibility.Collapsed;
                    titlelabel.Visibility = Visibility.Visible;

                    Keyboard.ClearFocus(); // Drop focus
                    win.ShowActivated = false;
                    e.Handled = true;

                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Rename cancelled via Escape");
                }
            };

            // 2. Handle Focus Loss (Auto-Save)
            titletb.LostFocus += (sender, e) =>
            {
                // Don't save if we are cancelling (Escape key handles UI)
                if (titletb.Visibility != Visibility.Visible) return;

                string originalTitle = container.Title.ToString();
                string newTitle = titletb.Text;
                string finalTitle = InterCore.ProcessTitleChange(container, newTitle, originalTitle);

                string id = container.Id?.ToString();
                var liveContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == id);

                if (liveContainer != null)
                {
                    IDictionary<string, object> containerDict = liveContainer as IDictionary<string, object> ??
                        ((JObject)liveContainer).ToObject<IDictionary<string, object>>();
                    containerDict["Title"] = finalTitle;

                    int index = ContainerDataManager.ContainerData.IndexOf(liveContainer);
                    if (index >= 0) ContainerDataManager.ContainerData[index] = JObject.FromObject(containerDict);

                    container.Title = finalTitle;
                }

                titlelabel.Content = finalTitle;
                win.Title = finalTitle;
                titletb.Visibility = Visibility.Collapsed;
                titlelabel.Visibility = Visibility.Visible;

                ContainerDataManager.SaveContainerData();

                win.ShowActivated = false;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Rename saved via LostFocus: {finalTitle}");
            };






            // WrapPanel wpcont = new WrapPanel();

            // --- FIX START ---
            WrapPanel wpcont = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = double.NaN,
                ItemHeight = double.NaN,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0)
            };

            // CRITICAL: Tag the panel with the Container ID so AddIcon can find settings
            // even before the panel is attached to the window.
            wpcont.Tag = container.Id?.ToString();
            // --- FIX END ---

            ScrollViewer wpcontscr = new ScrollViewer
            {
                Content = wpcont,
                VerticalScrollBarVisibility = SettingsManager.DisableContainerScrollbars ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto,
                // HorizontalScrollBarVisibility = SettingsManager.DisableContainerScrollbars ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto
            };

            if (container.Id != null) ContentScrollers[container.Id.ToString()] = wpcontscr;
            // --- PERFORMANCE TWEAK: Hardware Acceleration & Safe UI Culling ---
            IconManager.OptimizeContainerPanel(wpcont, wpcontscr);

            // Προσθήκη watermark για Portal Containers
            if (container.ItemsType?.ToString() == "Portal")
            {
                if (_options.ShowBackgroundImageOnPortalContainers ?? true)
                {
                    double opacity = (SettingsManager.PortalBackgroundOpacity / 100.0);
                    wpcontscr.Background = new ImageBrush
                    {
                        ImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/portal.png")),
                        // Opacity = 0.2,
                        Opacity = opacity,
                        Stretch = Stretch.UniformToFill
                    };
                }
            }
            dp.Children.Add(wpcontscr);

            void InitContent()
            {
                // 1. Handle Note containers - they don't use WrapPanel
                if (container.ItemsType?.ToString() == "Note")
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation, $"Creating Note container content for '{container.Title}'");
                    dp.Children.Remove(wpcontscr); // Remove ScrollViewer
                    TextBox noteTextBox = NoteContainerManager.CreateNoteContent(container, dp);

                    bool isNoteRolled = container.IsRolled?.ToString().ToLower() == "true";
                    noteTextBox.Visibility = isNoteRolled ? Visibility.Collapsed : Visibility.Visible;
                    return;
                }

                // 2. Handle Data/Portal Containers
                bool isRolled = container.IsRolled?.ToString().ToLower() == "true";
                wpcont.Visibility = isRolled ? Visibility.Collapsed : Visibility.Visible;
                wpcont.Children.Clear();

                // 2a. Data Containers (The complex logic)
                if (container.ItemsType?.ToString() == "Data")
                {
                    JArray items = null;
                    bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";

                    // Load Items (Tabs vs Main)
                    if (tabsEnabled)
                    {
                        try
                        {
                            var tabs = container.Tabs as JArray ?? new JArray();
                            int currentTab = Convert.ToInt32(container.CurrentTab?.ToString() ?? "0");
                            if (currentTab >= 0 && currentTab < tabs.Count)
                            {
                                var activeTab = tabs[currentTab] as JObject;
                                items = activeTab?["Items"] as JArray ?? new JArray();
                            }
                            else
                            {
                                items = container.Items as JArray ?? new JArray();
                            }
                        }
                        catch { items = container.Items as JArray ?? new JArray(); }
                    }
                    else
                    {
                        items = container.Items as JArray ?? new JArray();
                    }

                    if (items != null)
                    {
                        // Sort
                        var sortedItems = items.OfType<JObject>()
                            .OrderBy(item => item["DisplayOrder"]?.Type == JTokenType.Integer ? item["DisplayOrder"].Value<int>() : 0)
                            .ToList();

                        foreach (dynamic icon in sortedItems)
                        {
                            // FIX: Pass 'container' context for customization
                            AddIcon(icon, wpcont, container);

                            StackPanel sp = wpcont.Children[wpcont.Children.Count - 1] as StackPanel;
                            if (sp != null)
                            {
                                // --- 1. DEFINE VARIABLES (Scope Fix) ---
                                // We extract these immediately so they are available for ALL blocks below
                                IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict
                                    ? dict : ((JObject)icon).ToObject<IDictionary<string, object>>();

                                string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";

                                // CRITICAL: Define these here to avoid CS0103 errors
                                bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];
                                bool isLink = iconDict.ContainsKey("IsLink") && (bool)iconDict["IsLink"];

                                // --- 2. Extract Arguments ---
                                string arguments = null;
                                if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
                                {
                                    try
                                    {
                                        WshShell shell = new WshShell();
                                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                                        arguments = shortcut.Arguments;
                                    }
                                    catch { }
                                }

                                // --- 3. Attach Click Event ---
                                ClickEventAdder(sp, filePath, isFolder, arguments);

                                // --- 4. Target Checker Logic (Robust & Safe) ---
                                // Fail-Safe: Check network status live if flag is missing
                                bool isNetwork = iconDict.ContainsKey("IsNetwork") && (bool)iconDict["IsNetwork"];
                                if (!isNetwork && !string.IsNullOrEmpty(filePath))
                                {
                                    isNetwork = IsNetworkPath(filePath);
                                }

                                bool allowNetworkChecking = _options.CheckNetworkPaths ?? false;

                                // Logic: Add to checker ONLY IF:
                                // A. It is NOT a web link (using 'isLink')
                                // B. AND (It is NOT a network path OR the user explicitly allowed checks)
                                if (!isLink && (!isNetwork || allowNetworkChecking))
                                {
                                    // Safe to add to background checker (using 'isFolder')
                                    targetChecker.AddCheckAction(filePath, () => UpdateIcon(sp, filePath, isFolder), isFolder);

                                    if (isNetwork && allowNetworkChecking)
                                    {
                                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"User enabled checks: Monitoring network path {filePath}");
                                    }
                                }
                                else
                                {
                                    // Logging for exclusion
                                    if (isLink)
                                    {
                                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Excluded web link {filePath} from target checking");
                                    }

                                    if (isNetwork && !allowNetworkChecking)
                                    {
                                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Safety Exclusion: Skipped background check for network path {filePath}");
                                    }
                                }

                                // --- 5. Attach Context Menu ---
                                AttachIconContextMenu(sp, icon, container, win);
                            }
                        }
                    }
                }
                // 2b. Portal Containers
                else if (container.ItemsType?.ToString() == "Portal")
                {
                    try
                    {
                        var portalManager = new PortalContainerManager(container, wpcont);
                        _portalContainers[container] = portalManager;

                        // --- NEW: Cycle Sort Mode on CTRL + Click ---
                        win.PreviewMouseLeftButtonUp += (s, e) =>
                        {
                            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                            {
                                // Ensure they didn't click an actual icon (which might trigger drag or admin launch)
                                DependencyObject parent = e.OriginalSource as DependencyObject;
                                while (parent != null && parent != win)
                                {
                                    if (parent is StackPanel sp && sp.Tag != null) return;
                                    parent = VisualTreeHelper.GetParent(parent);
                                }

                                string newMode = portalManager.CycleSortMode();
                                ShowPortalToast(win, $"Sorted by: {newMode}");

                                e.Handled = true;
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Failed to initialize Portal Container: {ex.Message}", "Error");
                        ContainerDataManager.ContainerData.Remove(container);
                        ContainerDataManager.SaveContainerData();
                        win.Close();
                    }
                }
            }


            win.Drop += (sender, e) =>
            {
                e.Handled = true;
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] droppedFiles = null;
                    try
                    {
                        droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                            $"Drop handler received {droppedFiles?.Length ?? 0} files");
                    }
                    catch (Exception dataEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                            $"Error getting drop data: {dataEx.Message}");
                        return;
                    }
                    if (droppedFiles == null) return;

                    foreach (string droppedFile in droppedFiles)
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                            $"Processing dropped item: '{droppedFile}'");
                        try
                        {
                            // Path Validation (Existence / Folder Check)
                            bool fileExists = false;
                            bool directoryExists = false;
                            bool isFolderFlag = false;

                            try
                            {
                                FileAttributes attrs = System.IO.File.GetAttributes(droppedFile);
                                isFolderFlag = attrs.HasFlag(FileAttributes.Directory);
                                directoryExists = isFolderFlag;
                                fileExists = !isFolderFlag;
                            }
                            catch (Exception pathEx)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation, $"Path validation error: {pathEx.Message}");
                                continue;
                            }

                            if (!fileExists && !directoryExists)
                            {
                                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Invalid file or directory: {droppedFile}", "Error");
                                continue;
                            }

                            // --- DATA CONTAINER LOGIC ---


                            // --- DATA CONTAINER LOGIC ---
                            if (container.ItemsType?.ToString() == "Data")
                            {
                                if (!System.IO.Directory.Exists("Shortcuts")) System.IO.Directory.CreateDirectory("Shortcuts");
                                string baseShortcutName = System.IO.Path.Combine("Shortcuts", System.IO.Path.GetFileName(droppedFile));
                                string shortcutName = baseShortcutName;
                                int counter = 1;

                                bool isDroppedShortcut = System.IO.Path.GetExtension(droppedFile).ToLower() == ".lnk";
                                bool isDroppedUrlFile = System.IO.Path.GetExtension(droppedFile).ToLower() == ".url";

                                // FIX: Trust the extension. If it ends in .url, it IS a link.
                                // This bypasses content checks that fail on files with custom headers.
                                bool isWebLink = isDroppedUrlFile || CoreUtilities.IsWebLinkShortcut(droppedFile);

                                string targetPath;
                                bool isFolder = false;
                                string webUrl = null;

                                if (isWebLink)
                                {
                                    // Try to extract clean URL, but if it fails (weird header), fallback to file path
                                    try { webUrl = CoreUtilities.ExtractWebUrlFromFile(droppedFile); } catch { }

                                    targetPath = !string.IsNullOrEmpty(webUrl) ? webUrl : droppedFile;
                                    isFolder = false;
                                }
                                else
                                {
                                    if (isDroppedShortcut)
                                    {
                                        targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(droppedFile);
                                        if (string.IsNullOrEmpty(targetPath))
                                        {
                                            isFolder = false;
                                        }
                                        else
                                        {
                                            isFolder = System.IO.Directory.Exists(targetPath);
                                            if (!isFolder && string.IsNullOrEmpty(System.IO.Path.GetExtension(targetPath)))
                                            {
                                                isFolder = System.IO.Directory.Exists(targetPath);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        targetPath = droppedFile;
                                        isFolder = System.IO.Directory.Exists(targetPath);
                                    }
                                }

                                if (!isDroppedShortcut && !isDroppedUrlFile)
                                {
                                    // CASE A: Creating new shortcut from raw file/folder
                                    shortcutName = baseShortcutName + ".lnk";
                                    while (System.IO.File.Exists(shortcutName))
                                    {
                                        shortcutName = System.IO.Path.Combine("Shortcuts", $"{System.IO.Path.GetFileNameWithoutExtension(droppedFile)} ({counter++}).lnk");
                                    }

                                    try
                                    {
                                        WshShell shell = new WshShell();
                                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutName);
                                        shortcut.TargetPath = droppedFile;
                                        if (isFolder) shortcut.WorkingDirectory = droppedFile;
                                        shortcut.Save();
                                    }
                                    catch { continue; }
                                }
                                else
                                {
                                    // CASE B: Copying existing shortcut (LNK or URL)
                                    // FIX: Determine correct extension based on type
                                    // If it's a Web Link, MUST remain .url. If it's a Shortcut, MUST remain .lnk.
                                    string ext = isWebLink ? ".url" : ".lnk";

                                    // Ensure base name has correct extension
                                    if (!baseShortcutName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                                        baseShortcutName = System.IO.Path.ChangeExtension(baseShortcutName, ext);

                                    shortcutName = baseShortcutName;

                                    // Handle Duplicates while preserving extension
                                    while (System.IO.File.Exists(shortcutName))
                                    {
                                        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                        shortcutName = System.IO.Path.Combine("Shortcuts", $"{nameNoExt} ({counter++}){ext}");
                                    }

                                    if (isWebLink)
                                    {
                                        // FIX: Never rewrite .url files on drop. This destroys custom game/app icons (Steam/Spotify).
                                        // ALWAYS copy the original file exactly as-is to preserve IconFile and IconIndex properties.
                                        System.IO.File.Copy(droppedFile, shortcutName, true);
                                    }
                                    else
                                    {
                                        System.IO.File.Copy(droppedFile, shortcutName, true);
                                    }
                                }

                                dynamic newItem = new System.Dynamic.ExpandoObject();
                                IDictionary<string, object> newItemDict = newItem;

                                newItemDict["Filename"] = shortcutName;
                                newItemDict["IsFolder"] = isFolder;
                                newItemDict["IsLink"] = isWebLink;
                                newItemDict["IsNetwork"] = IsNetworkPath(shortcutName);

                                string displayFileName = isFolder
                                    ? System.IO.Path.GetFileName(droppedFile)
                                    : System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                newItemDict["DisplayName"] = displayFileName;
                                newItemDict["AlwaysRunAsAdmin"] = false;

                                // TABS FEATURE: Get fresh container data
                                string containerId = container.Id?.ToString();
                                var freshContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                                JArray items = null;
                                bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";
                                bool addedToTab0 = false;

                                if (tabsEnabled && freshContainer != null)
                                {
                                    int currentTab = Convert.ToInt32(freshContainer.CurrentTab?.ToString() ?? "0");
                                    var tabs = freshContainer.Tabs as JArray ?? new JArray();
                                    if (currentTab >= 0 && currentTab < tabs.Count)
                                    {
                                        var activeTab = tabs[currentTab] as JObject;
                                        if (activeTab != null)
                                        {
                                            items = activeTab["Items"] as JArray ?? new JArray();
                                            addedToTab0 = (currentTab == 0);
                                        }
                                    }
                                    if (items == null) items = freshContainer.Items as JArray ?? new JArray();
                                }
                                else
                                {
                                    items = (freshContainer ?? container).Items as JArray ?? new JArray();
                                }

                                int nextDisplayOrder = items.Count;
                                newItemDict["DisplayOrder"] = nextDisplayOrder;
                                items.Add(JObject.FromObject(newItem));

                                if (!string.IsNullOrEmpty(containerId))
                                {
                                    if (addedToTab0) SynchronizeTab0Content(containerId, "tab0", "add");
                                    else if (!tabsEnabled) SynchronizeTab0Content(containerId, "main", "add");
                                }

                                if (freshContainer != null)
                                {
                                    int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                                    if (containerIndex >= 0)
                                    {
                                        ContainerDataManager.ContainerData[containerIndex] = freshContainer;
                                        ContainerDataManager.SaveContainerData();
                                    }
                                }

                                AddIcon(newItem, wpcont);
                                StackPanel sp = wpcont.Children[wpcont.Children.Count - 1] as StackPanel;
                                if (sp != null)
                                {
                                    // FIX: Extract arguments for the newly created shortcut
                                    string args = Utility.GetShortcutArguments(shortcutName);

                                    ClickEventAdder(sp, shortcutName, isFolder, args);

                                    // Target Checker Logic
                                    bool isRealNetwork = IsNetworkPath(shortcutName);
                                    bool allowNetworkChecking = _options.CheckNetworkPaths ?? false;

                                    if (!isRealNetwork || allowNetworkChecking)
                                    {
                                        targetChecker.AddCheckAction(shortcutName, () => UpdateIcon(sp, shortcutName, isFolder), isFolder);
                                        if (isRealNetwork) LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Monitoring network path: {shortcutName}");
                                    }
                                    else
                                    {
                                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Skipped background check for: {shortcutName}");
                                    }

                                    // Attach Context Menu
                                    AttachIconContextMenu(sp, newItem, container, win);

                                    DesktopOriginals.OnDropped(shortcutName, droppedFile);   // Opsis: the desktop icon moves into the container (hidden, restorable)
                                    // --- HIDDEN TWEAK: Delete Original Shortcut On Drop ---
                                    if (SettingsManager.DeleteOriginalShortcutsOnDrop && isDroppedShortcut && !isFolder && !isWebLink)
                                    {
                                        try
                                        {
                                            string fileDir = System.IO.Path.GetDirectoryName(droppedFile);
                                            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                                            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

                                            bool isFromDesktop = string.Equals(fileDir, userDesktop, StringComparison.OrdinalIgnoreCase) ||
                                                                 string.Equals(fileDir, commonDesktop, StringComparison.OrdinalIgnoreCase);

                                            if (isFromDesktop) System.IO.File.Delete(droppedFile);
                                        }
                                        catch { }
                                    }
                                }
                            }






                            // --- PORTAL CONTAINER LOGIC (RESTORED) ---
                            else if (container.ItemsType?.ToString() == "Portal")
                            {
                                IDictionary<string, object> containerDict = container is IDictionary<string, object> dict ? dict : ((JObject)container).ToObject<IDictionary<string, object>>();
                                string destinationFolder = containerDict.ContainsKey("Path") ? containerDict["Path"]?.ToString() : null;

                                if (string.IsNullOrEmpty(destinationFolder))
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"No destination folder defined for this Portal Container.", "Error");
                                    continue;
                                }
                                if (!System.IO.Directory.Exists(destinationFolder))
                                {
                                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"The destination folder '{destinationFolder}' no longer exists.", "Error");
                                    continue;
                                }

                                string destinationPath = System.IO.Path.Combine(destinationFolder, System.IO.Path.GetFileName(droppedFile));
                                int counter = 1;
                                string baseName = System.IO.Path.GetFileNameWithoutExtension(droppedFile);
                                string extension = System.IO.Path.GetExtension(droppedFile);

                                while (System.IO.File.Exists(destinationPath) || System.IO.Directory.Exists(destinationPath))
                                {
                                    destinationPath = System.IO.Path.Combine(destinationFolder, $"{baseName} ({counter++}){extension}");
                                }

                                if (System.IO.File.Exists(droppedFile))
                                {
                                    System.IO.File.Copy(droppedFile, destinationPath, false);
                                }
                                else if (System.IO.Directory.Exists(droppedFile))
                                {
                                    BackupManager.CopyDirectory(droppedFile, destinationPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Failed to add {droppedFile}: {ex.Message}", "Error");
                        }
                    }
                    ContainerDataManager.SaveContainerData();
                }
                // --- URL DROP LOGIC (RESTORED) ---
                else if (e.Data.GetDataPresent(DataFormats.Text) ||
                         e.Data.GetDataPresent(DataFormats.Html) ||
                         e.Data.GetDataPresent("UniformResourceLocator"))
                {
                    try
                    {
                        string droppedUrl = ExtractUrlFromDropData(e.Data);
                        if (!string.IsNullOrEmpty(droppedUrl) && IsValidWebUrl(droppedUrl))
                        {
                            if (container.ItemsType?.ToString() == "Data")
                            {
                                AddUrlShortcutToContainer(droppedUrl, container, wpcont);
                            }
                            else
                            {
                                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation, "URL drops not supported for Portal containers");
                            }
                        }
                    }
                    catch (Exception urlEx)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation, $"Error processing URL drop: {urlEx.Message}");
                    }
                }
            };


      




            if (SettingsManager.EnableDimensionSnap)
            {
                win.SizeChanged += UpdateSizeFeedback;
            }
            win.LocationChanged += (s, e) =>
            {
                // Get current container reference by ID to avoid stale references
                string containerId = win.Tag?.ToString();
                if (string.IsNullOrEmpty(containerId))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerUpdate, $"Container Id missing during position change for window '{win.Title}'");
                    return;
                }
                // Find the current container in ContainerDataManager.ContainerData using ID
                var currentContainer = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (currentContainer != null)
                {
                    // Update position and save immediately
                    currentContainer.X = win.Left;
                    currentContainer.Y = win.Top;
                    ContainerDataManager.SaveContainerData();
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerUpdate, $"Position updated for container '{currentContainer.Title}' to X={win.Left}, Y={win.Top}");
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerUpdate, $"Container with Id '{containerId}' not found during position change");
                }
            };
            InitContent();
            // Add Note container specific context menu items after content is initialized
            if (container.ItemsType?.ToString() == "Note")
            {
                // Use a small delay to ensure the TextBox is fully created and added to the visual tree
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Find the TextBox that was created in InitContent()
                        var border = win.Content as Border;
                        var dockPanel = border?.Child as DockPanel;
                        var noteTextBox = dockPanel?.Children.OfType<TextBox>().FirstOrDefault();
                        if (noteTextBox != null)
                        {
                            NoteContainerManager.AddNoteContextMenuItems(CnMnContainerManager, container, noteTextBox);
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                                $"Added Note context menu items for container '{container.Title}'");
                        }
                        else
                        {
                            LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                                $"CRITICAL: Could not find TextBox for Note container '{container.Title}' - checking DockPanel children");
                            // Debug: Log what children actually exist
                            if (dockPanel != null)
                            {
                                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                                    $"DockPanel has {dockPanel.Children.Count} children:");
                                for (int i = 0; i < dockPanel.Children.Count; i++)
                                {
                                    var child = dockPanel.Children[i];
                                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                                        $" Child {i}: {child.GetType().Name}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                            $"Error adding Note context menu: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            win.Show();







            // Check for persistent Legendary Mode (Nikos or >:)
            string fTitle = container.Title?.ToString() ?? "";
            if (false)
            {
                // Defer slightly to ensure window is loaded
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // We call ProcessTitleChange to trigger the visual effect
                    InterCore.ProcessTitleChange(container, fTitle, "");
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            {
                // Defer slightly to ensure window is loaded
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // We call ProcessTitleChange with same names just to trigger the logic
                    InterCore.ProcessTitleChange(container, container.Title.ToString(), "");
                }), System.Windows.Threading.DispatcherPriority.Background);
            }







            IDictionary<string, object> containerDict = container is IDictionary<string, object> dict ? dict : ((JObject)container).ToObject<IDictionary<string, object>>();
            SnapManager.AddSnapping(win, containerDict);
            // Apply custom color if present, otherwise use global
            string customColor = container.CustomColor?.ToString();
            Utility.ApplyTintAndColorToContainer(win, string.IsNullOrEmpty(customColor) ? SettingsManager.SelectedColor : customColor);
            targetChecker.Start();
        }

        public static void AddIcon(dynamic icon, WrapPanel wpcont, dynamic containerContext = null)
        {
            // 1. EXTRACT DATA
            IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict
                ? dict
                : ((JObject)icon).ToObject<IDictionary<string, object>>();

            string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
            bool isFolder = iconDict.ContainsKey("IsFolder") && (bool)iconDict["IsFolder"];
            bool isLink = iconDict.ContainsKey("IsLink") && (bool)iconDict["IsLink"];

            // Fail-Safe Network Detection
            bool isNetwork = iconDict.ContainsKey("IsNetwork") && (bool)iconDict["IsNetwork"];
            if (!isNetwork && !string.IsNullOrEmpty(filePath))
            {
                isNetwork = IsNetworkPath(filePath);
            }

            // --- STEP 1: Determine Settings Context ---
            dynamic settings = containerContext;
            if (settings == null)
            {
                try
                {
                    if (wpcont.Tag != null)
                    {
                        string containerId = wpcont.Tag.ToString();
                        settings = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == containerId);
                    }
                    if (settings == null)
                    {
                        NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(wpcont);
                        string containerId = win?.Tag?.ToString();
                        if (!string.IsNullOrEmpty(containerId))
                        {
                            settings = GetContainerData().FirstOrDefault(f => f.Id?.ToString() == containerId);
                        }
                    }
                }
                catch { }
            }

            // --- NEW: Identify Container Type (Portal vs Data) ---
            bool isPortal = false;
            if (settings != null)
            {
                try
                {
                    // Handle both JObject (JSON) and ExpandoObject (Runtime)
                    if (settings is JObject jObj)
                        isPortal = jObj["ItemsType"]?.ToString() == "Portal";
                    else
                        isPortal = settings.ItemsType?.ToString() == "Portal";
                }
                catch { }
            }

            // --- STEP 2: Read Customization Settings ---
            int iconSpacing = 5;
            double iconWidth = 40, iconHeight = 40;
            bool grayscale = false;
            string textColorName = null;
            bool disableShadow = false;

            if (settings != null)
            {
                try { iconSpacing = Convert.ToInt32(settings.IconSpacing?.ToString() ?? "5"); } catch { }

                string sizeVal = settings.IconSize?.ToString() ?? "Medium";
                switch (sizeVal)
                {
                    case "Tiny": iconWidth = iconHeight = 24; break;
                    case "Small": iconWidth = iconHeight = 32; break;
                    case "Large": iconWidth = iconHeight = 48; break;
                    case "Huge": iconWidth = iconHeight = 64; break;
                    case "Desktop": iconWidth = iconHeight = CoreUtilities.DesktopIconDips(); break;   // same size as the icons on the Windows desktop
                    default: iconWidth = iconHeight = 40; break;
                }

                try { grayscale = settings.GrayscaleIcons?.ToString().ToLower() == "true"; } catch { }
                try { textColorName = settings.TextColor?.ToString(); } catch { }
                try { disableShadow = settings.DisableTextShadow?.ToString().ToLower() == "true"; } catch { }
            }

            // --- STEP 3: Create UI Elements ---
            StackPanel sp = new StackPanel
            {
                Margin = new Thickness(iconSpacing),
                Width = 60 + (iconSpacing * 2)
            };

            System.Windows.Controls.Image ico = new System.Windows.Controls.Image
            {
                Width = iconWidth,
                Height = iconHeight,
                Margin = new Thickness(5)
            };
            if (SettingsManager.IconVisibilityEffect != IconVisibilityEffect.None)
            {
                ico.Effect = Utility.CreateIconEffect(SettingsManager.IconVisibilityEffect);
            }
            // --- NEW: Intercept Internal Blank Spacer ---
            if (filePath != null && filePath.StartsWith("INTERNAL_BLANK_"))
            {
                string resImage = filePath.StartsWith("INTERNAL_BLANK_DOT") ? "dot.png" : "empty.png";
                ico.Source = new BitmapImage(new Uri($"pack://application:,,,/Resources/{resImage}", UriKind.Absolute));
                sp.Children.Add(ico);

                // Add empty label to maintain exact grid alignment
                TextBlock blankLbl = new TextBlock { Text = Loc.T(" ") };
                sp.Children.Add(blankLbl);

                // CRITICAL: Must be transparent to catch mouse clicks/drags
                sp.Background = System.Windows.Media.Brushes.Transparent;

                sp.Tag = new { FilePath = filePath, IsFolder = false, Arguments = (string)null };
                sp.ToolTip = new ToolTip { Content = Loc.T("Blank Spacer (Hold CTRL to move)") };
                wpcont.Children.Add(sp);
                return; // Skip standard extraction
            }

  



            // --- ICON EXTRACTION LOGIC ---
            ImageSource shortcutIcon = null;
            bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";
            bool isUrlFile = System.IO.Path.GetExtension(filePath).ToLower() == ".url";

            // Variables to track target state
            string targetPath = null;
            bool targetIsUncRoot = false;

            // FIX: Unified Custom Icon Extraction for Links AND Shortcuts
            // We check for a custom icon FIRST. If one exists, we use it immediately.
            if (isShortcut || isLink || isUrlFile)
            {
                // NEW: Special handling for .url files using manual parser
                if (isUrlFile || isLink)
                {
                    var urlIcon = GetUrlCustomIcon(filePath);
                    if (urlIcon.Path != null)
                    {
                        shortcutIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                    }
                }

                // Existing .lnk logic
                if (shortcutIcon == null)
                {


                    try
                    {
                        WshShell shell = new WshShell();
                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(System.IO.Path.GetFullPath(filePath));
                        targetPath = shortcut.TargetPath;

                        // 1. Try Custom Icon (Properties -> Change Icon)
                        // Check if IconLocation is valid and NOT ",0" (which implies default)
                        if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                        {
                            string[] iconParts = shortcut.IconLocation.Split(',');
                            string iconPath = iconParts[0];
                            int iconIndex = 0;
                            if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex))
                                iconIndex = parsedIndex;

                            if (System.IO.File.Exists(iconPath))
                            {
                                shortcutIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                            }
                        }
                    }


                    catch { }
                }
                }

            // 2. Fallback: Determine Icon if no custom icon found
            if (shortcutIcon == null)
            {
                if (isLink || isUrlFile)
                {
                    // --- NEW: Smart Protocol Detection ---
                    string urlTarget = targetPath; // Inherit from .lnk extraction if available

                    if (string.IsNullOrEmpty(urlTarget))
                    {
                        try
                        {
                            // Extract the actual URL from the .url file
                            var lines = System.IO.File.ReadAllLines(filePath);
                            var urlLine = lines.FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                            if (urlLine != null) urlTarget = urlLine.Substring(4).Trim();
                        }
                        catch { }
                    }

                    // If it's a custom app protocol (e.g., spotify:, steam:), let IconManager grab the specific internal icon
                    if (!string.IsNullOrEmpty(urlTarget) &&
                        !urlTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !urlTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        shortcutIcon = IconManager.GetIconForFile(targetPath, filePath, isFolder, isLink, isShortcut, iconDict);
                    }

                    // If it's a standard web link, or the custom extraction failed, enforce the unified white theme
                    if (shortcutIcon == null)
                    {
                        shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png")); //check
                    }
                }
                else if (isShortcut)
                {
                    try
                    {
                        // FIX: Use the robust Unicode-Safe reader (IShellLink)
                        // This handles tricky paths better than WshShell
                        if (string.IsNullOrEmpty(targetPath))
                            targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);

                        // Fallback to WshShell only if utility failed and we haven't tried yet
                        if (string.IsNullOrEmpty(targetPath))
                        {
                            try
                            {
                                WshShell shell = new WshShell();
                                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(System.IO.Path.GetFullPath(filePath));
                                targetPath = shortcut.TargetPath;
                            }
                            catch { }
                        }

                        // FIX 1: RUNTIME CORRECTION (Server Root)
                        // If target is \\192.168.1.10, force isFolder=true immediately.
                        if (IsUncRoot(targetPath))
                        {
                            targetIsUncRoot = true;
                            isFolder = true;   // Force folder treatment!
                            isNetwork = true;  // Force network flag
                        }
                    }
                    catch { }

                    // A. Standard Checks
                    bool targetIsFolder = false;
                    bool targetExists = false;

                    if (!string.IsNullOrEmpty(targetPath))
                    {
                        targetIsFolder = System.IO.Directory.Exists(targetPath) || targetIsUncRoot;
                        targetExists = targetIsFolder || System.IO.File.Exists(targetPath);
                    }

                    // B. SCRAPING FALLBACK
                    // If standard checks failed, scan the file content for \\Server pattern
                    if (!targetExists)
                    {
                        if (ScrapeLnkForNetworkRoot(filePath))
                        {
                            targetIsFolder = true;
                            isNetwork = true;      // Network Symbol
                            isFolder = true;       // Folder Shape
                            targetIsUncRoot = true; // CRITICAL: Force Validity (No White X)
                        }
                    }

                    // C. Icon Selection Logic
                    if (!isPortal && (isFolder || targetIsFolder))
                    {
                        shortcutIcon = null; // Force fall-through to White Theme
                    }
                    else if (targetExists)
                    {
                        shortcutIcon = Utility.GetShellIcon(targetPath, targetIsFolder);
                    }
                    else
                    {
                        shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
                    }
                }
                else
                {
                    // Standard File/Folder (Not a shortcut/link)
                    if (!isPortal && isFolder)
                    {
                        shortcutIcon = null;
                    }
                    else
                    {
                        shortcutIcon = Utility.GetShellIcon(filePath, isFolder);
                    }
                }
            }

            // Final Fallback (The White Theme Logic)
            if (shortcutIcon == null)
            {
                if (isFolder)
                {
                    // FIX 2: VALIDATION OVERRIDE
                    // If it is a UNC Root (either detected via shortcut target or direct path), consider it "Valid"
                    bool isUncRoot = targetIsUncRoot || IsUncRoot(filePath);
                    bool valid = isUncRoot || FilePathUtilities.DoesFolderExist(filePath, isFolder);

                    // Double check target if shortcut
                    if (!valid && isShortcut && !string.IsNullOrEmpty(targetPath))
                    {
                        valid = IsUncRoot(targetPath) || System.IO.Directory.Exists(targetPath);
                    }

                    if (valid)
                        shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
                    else
                        shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
                }
                else
                {
                    shortcutIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                }
            }

            ico.Source = shortcutIcon;










            // Apply Grayscale
            if (grayscale && shortcutIcon is BitmapSource bmp)
            {
                ico.Source = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, BitmapPalettes.Gray256, 0);
            }
            else if (grayscale)
            {
                ico.Opacity = 0.6;
                ico.Effect = new DropShadowEffect { Color = Colors.Gray, BlurRadius = 0, ShadowDepth = 0 };
            }

            // --- STEP 4: Network Overlay ---
            if (isNetwork)
            {
                Grid iconGrid = new Grid { Width = iconWidth + 8, Height = iconHeight + 8 };
                iconGrid.Children.Add(ico);

                TextBlock networkIndicator = new TextBlock
                {
                    Text = Loc.T("🔗"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 135, 225)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 2, 0, 0),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, Opacity = 0.7 }
                };

                iconGrid.Children.Add(networkIndicator);
                sp.Children.Add(iconGrid);
            }
            else
            {
                sp.Children.Add(ico);
            }

            // --- STEP 5: Text Label ---
            string displayName = (!iconDict.ContainsKey("DisplayName") || iconDict["DisplayName"] == null)
                ? System.IO.Path.GetFileNameWithoutExtension(filePath)
                : (string)iconDict["DisplayName"];

            if (displayName.Length > SettingsManager.MaxDisplayNameLength)
                displayName = displayName.Substring(0, SettingsManager.MaxDisplayNameLength) + "...";

            System.Windows.Media.Brush textBrush = System.Windows.Media.Brushes.White;
            if (!string.IsNullOrEmpty(textColorName))
            {
                try { textBrush = new SolidColorBrush(Utility.GetColorFromName(textColorName)); } catch { }
            }

            TextBlock lbl = new TextBlock
            {
                Text = displayName,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = textBrush,
                MaxWidth = 70
            };

            if (!disableShadow)
            {
                lbl.Effect = new DropShadowEffect { Color = Colors.Black, Direction = 315, ShadowDepth = 2, BlurRadius = 3, Opacity = 0.8 };
            }

            sp.Children.Add(lbl);
            sp.Tag = new { FilePath = filePath, IsFolder = isFolder, Arguments = (string)(iconDict.ContainsKey("Arguments") ? iconDict["Arguments"] : null) };

            // FIX: Show the final target in the tooltip instead of the internal shortcut path
            string toolTipText = displayName;
            string resolvedTarget = filePath;

            if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
            {
                resolvedTarget = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
            }
            else if (System.IO.Path.GetExtension(filePath).ToLower() == ".url")
            {
                try
                {
                    // Try to extract the true web URL or protocol from the file
                    string content = System.IO.File.ReadAllText(filePath);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"URL=([^\r\n]+)");
                    if (match.Success) resolvedTarget = match.Groups[1].Value.Trim();
                }
                catch { }
            }

            // Fallback in case extraction fails
            if (string.IsNullOrEmpty(resolvedTarget)) resolvedTarget = filePath;

            string displayTarget = resolvedTarget;

            // Clean up MS Store App AUMID targets (e.g. Microsoft.WindowsCalculator_8wekyb3d8bbwe!App)
            if (!string.IsNullOrEmpty(displayTarget) && displayTarget.Contains("!") && !displayTarget.Contains(":\\"))
            {
                string packageId = displayTarget.Split('!')[0];
                int hashIndex = packageId.IndexOf('_');
                if (hashIndex > 0) packageId = packageId.Substring(0, hashIndex);
                displayTarget = $"Windows App ({packageId})";
            }

            if (resolvedTarget != filePath)
            {
                toolTipText += $"\nTarget: {displayTarget}";
            }
            else
            {
                toolTipText += $"\nLocation: {displayTarget}";
            }

            sp.ToolTip = new ToolTip { Content = toolTipText };
            wpcont.Children.Add(sp);
        }



        private static void CreateNewContainer(string title, string itemsType, double x = 20, double y = 20, string customColor = null, string customLaunchEffect = null)
        {
            // Generate random name instead of using the passed title
            string containerName = CoreUtilities.GenerateRandomName();

            dynamic newContainer = new System.Dynamic.ExpandoObject();
            newContainer.Id = Guid.NewGuid().ToString();
            IDictionary<string, object> newContainerDict = newContainer;
            // newContainerDict["Title"] = title;


            //  newContainerDict["Title"] = containerName; // Use random name
            //Option to set Portal containers fodler nane
            // Only use random name for non-Portal containers
            if (itemsType != "Portal")
            {
                newContainerDict["Title"] = containerName; // Use random name
            }

            newContainerDict["X"] = x;
            newContainerDict["Y"] = y;
            newContainerDict["Width"] = 230;
            newContainerDict["Height"] = 130;
            newContainerDict["ItemsType"] = itemsType;
            newContainerDict["Items"] = itemsType == "Portal" ? "" : new JArray();
            newContainerDict["CustomColor"] = customColor; // Use passed value
            newContainerDict["CustomLaunchEffect"] = customLaunchEffect; // Use passed value
            newContainerDict["IsHidden"] = false; // Use passed value
            newContainerDict["IsLocked"] = false; // Init ISLocked

            // Initialize ALL container properties with defaults to match JSON structure
            newContainerDict["IsLocked"] = "false";
            newContainerDict["IsHidden"] = "false";
            newContainerDict["CustomColor"] = customColor;
            newContainerDict["CustomLaunchEffect"] = customLaunchEffect;
            newContainerDict["IsRolled"] = "false";
            newContainerDict["UnrolledHeight"] = 130;
            newContainerDict["TextColor"] = null;
            newContainerDict["BoldTitleText"] = "false";
            newContainerDict["TitleTextColor"] = null;
            newContainerDict["DisableTextShadow"] = "false";
            newContainerDict["IconSize"] = "Desktop";
            newContainerDict["GrayscaleIcons"] = "false";
            newContainerDict["IconSpacing"] = 5;
            newContainerDict["TitleTextSize"] = "Medium";
            newContainerDict["ContainerBorderColor"] = null;
            newContainerDict["ContainerBorderThickness"] = 2;
            // TABS FEATURE: Initialize tab properties for new containers
            newContainerDict["TabsEnabled"] = "false";  // Default to no tabs
            newContainerDict["CurrentTab"] = 0;         // Default to first tab
            newContainerDict["Tabs"] = new JArray();    // Empty tabs array

            if (itemsType == "Portal")
                if (itemsType == "Portal")
                {
                    using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        dialog.Description = "Select the folder to monitor for this Portal Container";
                        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            newContainerDict["Path"] = dialog.SelectedPath;

                            // Get the folder name from the selected path
                            // should add and option to be checked for the below
                            // Set title to folder name for Portal containers
                            string folderName = System.IO.Path.GetFileName(dialog.SelectedPath);
                            newContainerDict["Title"] = folderName;

                        }
                        else
                        {
                            return;
                        }
                    }
                }
            ContainerDataManager.ContainerData.Add(newContainer);
            ContainerDataManager.SaveContainerData();
            CreateContainer(newContainer, new TargetChecker(1000));
        }


        /// <summary>
        /// HELPER: Extracts a Network Root path (e.g. \\Server) from a .lnk file by reading raw content.
        /// Used when Windows APIs fail to resolve the target.
        /// </summary>
        private static string GetScrapedNetworkPath(string lnkPath)
        {
            try
            {
                if (string.IsNullOrEmpty(lnkPath) || !System.IO.File.Exists(lnkPath)) return null;

                string fileContent;
                // Read with Share.ReadWrite to avoid locking issues
                using (var fs = new System.IO.FileStream(lnkPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.Default))
                {
                    fileContent = sr.ReadToEnd();
                }

                // Regex to capture \\Servername or \\1.2.3.4
                // Matches start of line or null-preceded, followed by \\, then valid host chars, ending with null or newline
                var regex = new System.Text.RegularExpressions.Regex(@"(^|\0)\\\\([a-zA-Z0-9\.\-_]+)(\x00|$)", System.Text.RegularExpressions.RegexOptions.Multiline);
                var match = regex.Match(fileContent);

                if (match.Success)
                {
                    // Clean up the match (remove leading nulls if any) to get just "\\Server"
                    string raw = match.Value.Trim('\0', '\r', '\n');
                    if (raw.StartsWith(@"\\")) return raw;
                }

                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Scrape path failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// SAFE FALLBACK: Scans the raw binary content of a .lnk file for Network Root patterns (e.g. \\192.168.1.10).
        /// This bypasses Windows API validation failures for server roots or offline locations.
        /// </summary>
        private static bool ScrapeLnkForNetworkRoot(string lnkPath)
        {
            try
            {
                if (string.IsNullOrEmpty(lnkPath) || !System.IO.File.Exists(lnkPath)) return false;

                // 1. Read file safely with a timeout mechanism (handled by standard stream opening)
                // Use default encoding to capture ANSI/ASCII strings embedded in the binary
                string fileContent;
                using (var fs = new System.IO.FileStream(lnkPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new System.IO.StreamReader(fs, System.Text.Encoding.Default))
                {
                    fileContent = sr.ReadToEnd();
                }

                // 2. Define Regex for UNC Root: Matches \\Server or \\192.168.1.1
                // Explanation:
                // \\\\       -> Literal "\\"
                // [^\\]+     -> Any character that is NOT a backslash (The server name/IP)
                // (\x00|$)   -> Ends with a null byte or end of string (typical in binary formats)
                var networkRootRegex = new System.Text.RegularExpressions.Regex(@"^\\\\([a-zA-Z0-9\.\-_]+)(\x00|$)", System.Text.RegularExpressions.RegexOptions.Multiline);

                // 3. Scan the content
                // We scan for specific "unc" prefixes often found in LNK structures or just the raw path
                if (networkRootRegex.IsMatch(fileContent)) return true;

                // Fallback simple string check for the user's specific case (\\IP) if regex is too strict on binary noise
                // We look for "\\" followed by a digit (IP) that appears in the file
                int index = fileContent.IndexOf(@"\\");
                if (index >= 0 && index + 3 < fileContent.Length)
                {
                    char nextChar = fileContent[index + 2];
                    // If it looks like \\1... or \\a... it's likely a path. 
                    // To be safe, we check if it DOESN'T look like a file path (no ":\")
                    bool hasColon = fileContent.IndexOf(@":\", index) == index + 2; // e.g. C:\ check
                    if (!hasColon) return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                // SANITIZATION: Never let a scraping error break the UI loading
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Lnk scraping suppressed error: {ex.Message}");
                return false;
            }
        }



        private static void UpdateLockState(TextBlock lockIcon, dynamic container, bool? forceState = null, bool saveToJson = true)
        {
            // Get the actual container from ContainerDataManager.ContainerData using Id to ensure correct reference
            string containerId = container.Id?.ToString();
            if (string.IsNullOrEmpty(containerId))
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Container '{container.Title}' has no Id, cannot update lock state");
                return;
            }

            int index = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
            if (index < 0)
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Container '{container.Title}' not found in ContainerDataManager.ContainerData, cannot update lock state");
                return;
            }

            dynamic actualContainer = ContainerDataManager.ContainerData[index];
            bool isLocked = forceState ?? (actualContainer.IsLocked?.ToString().ToLower() == "true");

            // Only update JSON if explicitly requested (e.g., during toggle, not initialization)
            if (saveToJson)
            {
                UpdateContainerProperty(actualContainer, "IsLocked", isLocked.ToString().ToLower(), $"Container {(isLocked ? "locked" : "unlocked")}");
            }

            // Update UI on the main thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>


            {
                // Update lock icon
                lockIcon.Foreground = isLocked ? System.Windows.Media.Brushes.DeepPink : System.Windows.Media.Brushes.White;
                lockIcon.Text = isLocked ? "🔒" : "🔓";   // Opsis: closed / open padlock instead of the shield
                lockIcon.ToolTip = isLocked ? "Container is locked (click to unlock)" : "Container is unlocked (click to lock)";

                // Find the NonActivatingWindow
                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(lockIcon);
                if (win == null)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Could not find NonActivatingWindow for container '{actualContainer.Title}'");
                    return;
                }

                // Update ResizeMode
                win.ResizeMode = isLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerUpdate, $"Set ResizeMode to {win.ResizeMode} for container '{actualContainer.Title}'");
            });

            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Updated lock state for container '{actualContainer.Title}': IsLocked={isLocked}");
        }



        // --- HELPER: Manual Icon Extraction for .url Files ---
        private static (string Path, int Index) GetUrlCustomIcon(string urlPath)
        {
            try
            {
                if (!System.IO.File.Exists(urlPath)) return (null, 0);
                var lines = System.IO.File.ReadAllLines(urlPath);
                string iconFile = null;
                int iconIndex = 0;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        iconFile = trimmed.Substring(9).Trim();
                    else if (trimmed.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(trimmed.Substring(10).Trim(), out iconIndex);
                }

                if (!string.IsNullOrEmpty(iconFile)) return (iconFile, iconIndex);
            }
            catch { }
            return (null, 0);
        }




        public static void EditItem(dynamic icon, dynamic container, NonActivatingWindow win)
        {
            IDictionary<string, object> iconDict = icon is IDictionary<string, object> dict ? dict : ((JObject)icon).ToObject<IDictionary<string, object>>();
            string filePath = iconDict.ContainsKey("Filename") ? (string)iconDict["Filename"] : "Unknown";
            string displayName = iconDict.ContainsKey("DisplayName") ? (string)iconDict["DisplayName"] : System.IO.Path.GetFileNameWithoutExtension(filePath);

            // Allow .lnk and .url
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            bool isEditable = ext == ".lnk" || ext == ".url";

            if (!isEditable)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxForm("Edit is not available for this item type.", "Info");
                return;
            }

            var editWindow = new EditShortcutWindow(filePath, displayName);

            // Note: The EditWindow calls 'UpdateContainerDataForIcon' internally when Save is clicked.
            // We don't need to duplicate the saving logic here.
            if (editWindow.ShowDialog() == true)
            {
                string newDisplayName = editWindow.NewDisplayName;

                // Update local memory reference immediately for responsiveness
                iconDict["DisplayName"] = newDisplayName;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"EditItem: Local display name updated to {newDisplayName}");

                // UI Refresh Logic
                if (win == null) return;

                WrapPanel wpcont = null;
                var border = win.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                if (scrollViewer != null) wpcont = scrollViewer.Content as WrapPanel;

                if (wpcont == null) wpcont = ContainerUtilities.FindWrapPanel(win);

                if (wpcont != null)
                {
                    var sp = wpcont.Children.OfType<StackPanel>()
                        .FirstOrDefault(s => s.Tag != null && s.Tag.GetType().GetProperty("FilePath")?.GetValue(s.Tag)?.ToString() == filePath);

                    if (sp != null)
                    {
                        RefreshSingleIconComplete(sp, filePath, newDisplayName, win);
                    }
                    else
                    {
                        // Fallback: Rebuild if not found (e.g. Tab switch might have hidden it)
                        // In most cases RefreshSingleIconComplete handles it, or a full container refresh will catch it later.
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, "StackPanel not found for immediate refresh, data is saved though.");
                    }
                }
            }
        }



        // --- PROFESSIONAL UX FINAL v13: The "Sorcery" Fix ---
        // 1. Reverts Padding to 'TemplateBinding' so the [+] button isn't crushed.
        // 2. Ensures the [+] button (Width 25) gets its 0 padding, while Text tabs get 10.
        private static void ApplyTabStyle(Button btn, bool isActive, string colorName, bool isPlusButton = false)
        {
            try
            {
                // 1. RESET
                btn.Style = null;
                btn.FocusVisualStyle = null;
                btn.Focusable = false;

                btn.ClearValue(Button.BackgroundProperty);
                btn.ClearValue(Button.ForegroundProperty);
                btn.ClearValue(Button.BorderBrushProperty);
                btn.ClearValue(Button.FontWeightProperty);
                btn.ClearValue(Button.PaddingProperty); // Clear local padding to be safe

                btn.MouseEnter -= Tab_MouseEnter_Lambda;
                btn.MouseLeave -= Tab_MouseLeave_Lambda;

                // 2. COLOR CALCULATION
                string effectiveColor = !string.IsNullOrEmpty(colorName) ? colorName : SettingsManager.SelectedColor;
                System.Windows.Media.Color baseColor = System.Windows.Media.Colors.Gray;
                try
                {
                    var drawingColor = Utility.GetColorFromName(effectiveColor);
                    baseColor = System.Windows.Media.Color.FromArgb(255, drawingColor.R, drawingColor.G, drawingColor.B);
                }
                catch { }

                string c = effectiveColor?.ToLower() ?? "";
                bool isExplicitDark = c.Contains("blue") || c.Contains("teal") || c.Contains("black") ||
                                      c.Contains("red") || c.Contains("green") || c.Contains("purple") ||
                                      c.Contains("bismark") || c.Contains("fuchsia") || c.Contains("default");

                double brightness = Math.Sqrt(
                    (0.299 * baseColor.R * baseColor.R) +
                    (0.587 * baseColor.G * baseColor.G) +
                    (0.114 * baseColor.B * baseColor.B)
                );

                bool isDarkTheme = isExplicitDark || brightness < 160;

                // 3. PALETTE DEFINITION
                SolidColorBrush bgActive, bgInactive, bgHover;
                SolidColorBrush textActive, textInactive;
                SolidColorBrush borderActive, borderInactive;

                if (isDarkTheme)
                {
                    bgActive = new SolidColorBrush(baseColor);
                    bgInactive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
                    bgHover = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 255, 255));

                    textActive = System.Windows.Media.Brushes.White;
                    textInactive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255));

                    borderActive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 255, 255));
                    borderInactive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255));
                }
                else
                {
                    bgActive = System.Windows.Media.Brushes.White;
                    bgInactive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 0, 0, 0));
                    bgHover = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 0, 0));

                    textActive = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 20));
                    textInactive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0, 0, 0));

                    borderActive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 0, 0));
                    borderInactive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 0, 0));
                }

                // 4. BUTTON CONFIGURATION
                if (isPlusButton)
                {
                    // Special Case: [+] Button needs 0 padding to center the text in 25px width
                    btn.Padding = new Thickness(0);

                    if (!isActive)
                    {
                        bgInactive = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                            (byte)(bgInactive.Color.A / 2),
                            bgInactive.Color.R,
                            bgInactive.Color.G,
                            bgInactive.Color.B));
                    }
                }
                else
                {
                    // Standard Tab: Needs padding for breathing room
                    btn.Padding = new Thickness(10, 2, 10, 2);
                }

                // 5. TEMPLATE GENERATION
                ControlTemplate template = new ControlTemplate(typeof(Button));
                FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
                border.Name = "Border";
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4, 4, 0, 0));

                // CRITICAL FIX: Bind Padding to the Button's property instead of hardcoding it.
                // This allows the [+] button to have 0 padding and Text tabs to have 10 padding.
                border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

                FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
                content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

                border.AppendChild(content);
                template.VisualTree = border;

                // 6. APPLY STYLE
                Style style = new Style(typeof(Button));
                style.Setters.Add(new Setter(Button.TemplateProperty, template));

                if (isActive)
                {
                    // ACTIVE
                    style.Setters.Add(new Setter(Button.BackgroundProperty, bgActive));
                    style.Setters.Add(new Setter(Button.ForegroundProperty, textActive));
                    style.Setters.Add(new Setter(Button.BorderBrushProperty, borderActive));
                    style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1, 1, 1, 0)));
                    style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.Bold));
                    style.Setters.Add(new Setter(Button.OpacityProperty, 1.0));

                    Trigger staticTrigger = new Trigger { Property = Button.IsEnabledProperty, Value = true };
                    staticTrigger.Setters.Add(new Setter(Border.BackgroundProperty, bgActive, "Border"));
                    staticTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, borderActive, "Border"));
                    staticTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0), "Border"));
                    template.Triggers.Add(staticTrigger);

                    if (isDarkTheme)
                        style.Setters.Add(new Setter(Button.EffectProperty, new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Color = System.Windows.Media.Colors.Black, Opacity = 0.5 }));
                }
                else
                {
                    // INACTIVE
                    style.Setters.Add(new Setter(Button.BackgroundProperty, bgInactive));
                    style.Setters.Add(new Setter(Button.ForegroundProperty, textInactive));
                    style.Setters.Add(new Setter(Button.BorderBrushProperty, borderInactive));
                    style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1, 1, 1, 1)));
                    style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.Normal));

                    Trigger baseTrigger = new Trigger { Property = Button.IsEnabledProperty, Value = true };
                    baseTrigger.Setters.Add(new Setter(Border.BackgroundProperty, bgInactive, "Border"));
                    baseTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, borderInactive, "Border"));
                    baseTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 1), "Border"));
                    template.Triggers.Add(baseTrigger);

                    Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                    hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, bgHover, "Border"));
                    hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, borderActive, "Border"));
                    hoverTrigger.Setters.Add(new Setter(Button.ForegroundProperty, textActive));
                    hoverTrigger.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
                    template.Triggers.Add(hoverTrigger);
                }

                btn.Style = style;
            }
            catch { }
        }


        // Dummy handlers to allow -= syntax (prevents compiler errors if we were using named methods)
        // Since we use lambdas above, we don't strictly need these, but good for safety if refactoring.
        private static void Tab_MouseEnter_Lambda(object sender, System.Windows.Input.MouseEventArgs e) { }
        private static void Tab_MouseLeave_Lambda(object sender, System.Windows.Input.MouseEventArgs e) { }
        



        private static void BackupOrRestoreShortcut(string filePath, bool targetExists, bool isFolder)
        {
            // FIX: Use ProfileManager to get the path relative to the active profile
            string tempShortcutsDir = ProfileManager.GetProfileFilePath("Temp Shortcuts");

            string backupFileName = System.IO.Path.GetFileName(filePath);
            string backupPath = System.IO.Path.Combine(tempShortcutsDir, backupFileName);

            try
            {
                // Ensure TempShortcuts directory exists
                if (!Directory.Exists(tempShortcutsDir))
                {
                    Directory.CreateDirectory(tempShortcutsDir);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Created TempShortcuts directory: {tempShortcutsDir}");
                }

                if (!targetExists)
                {
                    // Backup shortcut if target is missing and not already backed up
                    if (System.IO.File.Exists(filePath) && !System.IO.File.Exists(backupPath))
                    {
                        System.IO.File.Copy(filePath, backupPath, true);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Backed up shortcut {filePath} to {backupPath}");
                    }
                }
                else
                {
                    // Restore shortcut from backup if target exists
                    if (System.IO.File.Exists(backupPath))
                    {
                        // Verify the backup has a custom icon before restoring
                        WshShell shell = new WshShell();
                        IWshShortcut backupShortcut = (IWshShortcut)shell.CreateShortcut(backupPath);
                        if (!string.IsNullOrEmpty(backupShortcut.IconLocation))
                        {
                            System.IO.File.Copy(backupPath, filePath, true);
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Restored shortcut {filePath} from {backupPath} with custom icon");
                        }
                        // Delete backup
                        System.IO.File.Delete(backupPath);
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Deleted backup {backupPath} after restoration");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error in BackupOrRestoreShortcut for {filePath}: {ex.Message}");
            }
        }

       /// <summary>
        /// Refresh container using the same approach as CustomizeContainerForm
        /// This ensures consistent sizing and behavior
        /// </summary>
        public static void RefreshContainerUsingFormApproach(NonActivatingWindow win, dynamic container)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Refreshing container using form approach for '{container.Title}'");

                // Find the WrapPanel
                var wrapPanel = FindWrapPanel(win);
                if (wrapPanel == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, "Cannot find WrapPanel for container refresh");
                    return;
                }

                // Clear existing icons
                wrapPanel.Children.Clear();

                // TABS FEATURE: Check if tabs are enabled and load from appropriate source
                bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";
                JArray items = null;

                if (tabsEnabled)
                {
                    // Load from current tab
                    var tabs = container.Tabs as JArray ?? new JArray();
                    int currentTab = Convert.ToInt32(container.CurrentTab?.ToString() ?? "0");

                    if (currentTab >= 0 && currentTab < tabs.Count)
                    {
                        var activeTab = tabs[currentTab] as JObject;
                        if (activeTab != null)
                        {
                            items = activeTab["Items"] as JArray ?? new JArray();
                            string tabName = activeTab["TabName"]?.ToString() ?? $"Tab {currentTab}";
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                                $"Refreshing icons from tab '{tabName}' for container '{container.Title}'");
                        }
                    }
                }
                else
                {
                    // Load from main Items array (existing behavior)
                    items = container.Items as JArray;
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        $"Refreshing icons from main Items for container '{container.Title}'");
                }

                if (items != null)
                {
                    // Sort items by DisplayOrder and add them (SAME AS FORM)
                    var sortedItems = items
                        .OfType<JObject>()
                        .OrderBy(item => item["DisplayOrder"]?.Value<int>() ?? 0)
                        .ToList();

                    foreach (dynamic item in sortedItems)
                    {
                        // Use the SAME method as the working form
                        AddIcon(item, wrapPanel);

                        // Add basic event handlers (SAME AS FORM)
                        if (wrapPanel.Children.Count > 0)
                        {
                            var sp = wrapPanel.Children[wrapPanel.Children.Count - 1] as StackPanel;
                            if (sp != null)
                            {
                                string filePath = item.Filename?.ToString() ?? "Unknown";
                                bool isFolder = item.IsFolder?.ToString().ToLower() == "true";
                                ClickEventAdder(sp, filePath, isFolder);

                                // FIX: Attach the missing Context Menu
                                AttachIconContextMenu(sp, item, container, win);
                            }
                        }

                    }

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Successfully refreshed {sortedItems.Count} icons for container '{container.Title}' using form approach");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error refreshing container using form approach: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to find WrapPanel in container window
        /// </summary>
        private static WrapPanel FindWrapPanel(NonActivatingWindow win)
        {
            try
            {
                var border = win.Content as Border;
                var dockPanel = border?.Child as DockPanel;
                var scrollViewer = dockPanel?.Children.OfType<ScrollViewer>().FirstOrDefault();
                return scrollViewer?.Content as WrapPanel;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error finding WrapPanel: {ex.Message}");
                return null;
            }
        }



        private static void UpdateIcon(StackPanel sp, string filePath, bool isFolder)
        {
            if (System.Windows.Application.Current == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // --- NEW: Skip background check for internal placeholders ---
                if (filePath != null && filePath.StartsWith("INTERNAL_BLANK_")) return;
                // 1. OPTIMIZATION: Cache Check
                bool fileExists = System.IO.File.Exists(filePath) || System.IO.Directory.Exists(filePath);
                if (!fileExists) return;

                DateTime currentLastWrite;
                try { currentLastWrite = System.IO.File.GetLastWriteTime(filePath); }
                catch { return; }

                bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";
                bool isUrlFile = System.IO.Path.GetExtension(filePath).ToLower() == ".url";
                bool targetValid = true;

                if (isShortcut && !Utility.IsStoreAppShortcut(filePath))
                {
                    string tPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);
                    targetValid = !string.IsNullOrEmpty(tPath) &&
                                 (System.IO.File.Exists(tPath) || System.IO.Directory.Exists(tPath));
                }
                else if (!isShortcut)
                {
                    targetValid = System.IO.File.Exists(filePath) || System.IO.Directory.Exists(filePath);
                }

                bool isNowBroken = !targetValid;

                if (_iconStates.TryGetValue(filePath, out var lastState))
                {
                    if (lastState.LastWrite == currentLastWrite && lastState.IsBroken == isNowBroken)
                        return; // Cache hit
                }
                _iconStates[filePath] = (currentLastWrite, isNowBroken);

                // 2. Identify Web Link (Do NOT return early anymore)
                bool isWebLink = isUrlFile;
                if (!isWebLink)
                {
                    try
                    {
                        foreach (var container in ContainerDataManager.ContainerData)
                        {
                            if (container.ItemsType?.ToString() == "Data")
                            {
                                bool CheckList(JArray list)
                                {
                                    if (list == null) return false;
                                    foreach (var item in list)
                                    {
                                        string itemPath = item["Filename"]?.ToString();
                                        if (!string.IsNullOrEmpty(itemPath) && string.Equals(itemPath, filePath, StringComparison.OrdinalIgnoreCase))
                                            return item["IsLink"]?.ToObject<bool>() ?? false;
                                    }
                                    return false;
                                }
                                if (CheckList(container.Items as JArray)) { isWebLink = true; break; }
                                var tabs = container.Tabs as JArray;
                                if (tabs != null)
                                {
                                    foreach (var tab in tabs) if (CheckList(tab["Items"] as JArray)) { isWebLink = true; goto FoundLink; }
                                }
                            }
                        }
                    FoundLink:;
                    }
                    catch { }
                }

                System.Windows.Controls.Image ico = sp.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (ico == null) return;

                // 3. MAIN ICON LOGIC
                ImageSource newIcon = null;
                string targetPath = filePath;

                if (isShortcut) targetPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(filePath);

                bool targetExists = System.IO.File.Exists(targetPath) || System.IO.Directory.Exists(targetPath);
                bool isTargetFolder = System.IO.Directory.Exists(targetPath);
                if (isShortcut && isTargetFolder) isFolder = true;

                if (isShortcut) BackupOrRestoreShortcut(filePath, targetExists, isFolder);

                
                // --- PRIORITY LOGIC START ---
                // CASE A: WEB LINKS
                if (isWebLink)
                {
                    // 1. Try Custom Icon first
                    var urlIcon = GetUrlCustomIcon(filePath); // <--- USE NEW HELPER
                    if (urlIcon.Path != null)
                    {
                        newIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                    }
                    else
                    {
                        // Try WshShell only if manual failed (legacy fallback)
                        try { /* existing WshShell logic */ } catch { }
                    }

                    // 2. Fallback to Theme or Custom Protocol
                    if (newIcon == null)
                    {
                        // Recheck the actual target inside the file
                        string fileText = "";
                        try { fileText = System.IO.File.ReadAllText(filePath).ToLower(); } catch { }

                        if (fileText.Contains("spotify:") || fileText.Contains("spotify.com"))
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/spotify-White.png"));
                        }
                        else if (fileText.Contains("steam://"))
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/steam-White.png"));
                        }
                        else
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));
                        }
                    }
                }
                // CASE B: FOLDERS
                else if (isFolder)
                {
                    // 1. Broken Target -> White X
                    if (!FilePathUtilities.DoesFolderExist(filePath, isFolder))
                    {
                        newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
                    }

                    // 2. Custom Icon
                    if (newIcon == null && isShortcut)
                    {
                        try
                        {
                            WshShell shell = new WshShell();
                            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                            if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                            {
                                string[] iconParts = shortcut.IconLocation.Split(',');
                                string iconPath = iconParts[0];
                                int iconIndex = 0;
                                if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex))
                                    iconIndex = parsedIndex;

                                if (System.IO.File.Exists(iconPath))
                                    newIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                            }
                        }
                        catch { }
                    }

                    // 3. Theme Fallback
                    if (newIcon == null)
                    {
                        NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                        string containerId = win?.Tag?.ToString();
                        var container = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                        bool isPortal = container != null && container.ItemsType?.ToString() == "Portal";

                        if (isPortal) newIcon = Utility.GetShellIcon(filePath, true);
                        else newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
                    }
                }
                // CASE C: BROKEN FILES
                else if (!targetExists)
                {
                    if (Utility.IsStoreAppShortcut(filePath))
                        newIcon = Utility.GetShellIcon(filePath, isFolder);
                    else
                        newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                }
                // CASE D: SHORTCUT FILES
                else if (isShortcut)
                {
                    try
                    {
                        WshShell shell = new WshShell();
                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                        if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                        {
                            string[] iconParts = shortcut.IconLocation.Split(',');
                            string iconPath = iconParts[0];
                            int iconIndex = 0;
                            if (iconParts.Length == 2 && int.TryParse(iconParts[1], out int parsedIndex)) iconIndex = parsedIndex;

                            if (System.IO.File.Exists(iconPath))
                                newIcon = IconManager.ExtractIconFromFile(iconPath, iconIndex);
                        }
                    }
                    catch { }

                    if (newIcon == null)
                    {
                        // Clean icon fallback
                        if (targetPath != null && System.IO.File.Exists(targetPath))
                            newIcon = Utility.GetShellIcon(targetPath, false);
                        else
                            newIcon = Utility.GetShellIcon(filePath, false);
                    }
                }
                // CASE E: STANDARD FILES
                //else
                //{
                //    try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                //    catch { newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png")); }
                //}
                // CASE E: STANDARD FILES
                else
                {
                    try { newIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource(); }
                    catch
                    {
                        // FIX: Office/AppX Icon Crash (.pptx, etc.). Fallback to Shell API.
                        newIcon = Utility.GetShellIcon(filePath, false);
                        if (newIcon == null)
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                        }
                    }
                }


                // Apply
                if (ico.Source != newIcon && newIcon != null)
                {
                    ico.Source = newIcon;
                    if (IconManager.IconCache.ContainsKey(filePath))
                    {
                        IconManager.IconCache[filePath] = newIcon;
                    }
                }
            });
        }

        



        // Safety method to ensure no containers are stuck in transition state
        public static void ClearAllTransitionStates()
        {
            if (_containersInTransition.Count > 0)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerUpdate, $"Clearing {_containersInTransition.Count} stuck transition states");
                _containersInTransition.Clear();
            }
        }

        public static void UpdateOptionsAndClickEvents()
        {


            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Updating options, new singleClickToLaunch={SettingsManager.SingleClickToLaunch}");

            // Update _options
            _options = new
            {
                IsSnapEnabled = SettingsManager.IsSnapEnabled,
                ShowBackgroundImageOnPortalContainers = SettingsManager.ShowBackgroundImageOnPortalContainers,
                Showintray = SettingsManager.ShowInTray,
                EnableSounds = SettingsManager.EnableSounds,
                TintValue = SettingsManager.TintValue,
                MenuTintValue = SettingsManager.MenuTintValue,
                MenuIcon = SettingsManager.MenuIcon,
                LockIcon = SettingsManager.LockIcon,
                SelectedColor = SettingsManager.SelectedColor,
                IsLogEnabled = SettingsManager.IsLogEnabled,
                singleClickToLaunch = SettingsManager.SingleClickToLaunch,
                LaunchEffect = SettingsManager.LaunchEffect,
                CheckNetworkPaths = false // Keep this as is
            };


            if (System.Windows.Application.Current != null)
            {

                // Force UI update on the main thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                int updatedItems = 0;
                foreach (var win in System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>())
                {
                    var wpcont = ((win.Content as Border)?.Child as DockPanel)?.Children
                        .OfType<ScrollViewer>().FirstOrDefault()?.Content as WrapPanel;
                    if (wpcont != null)
                    {
                        foreach (var sp in wpcont.Children.OfType<StackPanel>())
                        {
                            string path = sp.Tag as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                bool isFolder = Directory.Exists(path) ||
                                    (System.IO.Path.GetExtension(path).ToLower() == ".lnk" &&
                                     Directory.Exists(Utility.GetShortcutTarget(path)));
                                string arguments = null;
                                if (System.IO.Path.GetExtension(path).ToLower() == ".lnk")
                                {
                                    WshShell shell = new WshShell();
                                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(path);
                                    arguments = shortcut.Arguments;
                                }
                                ClickEventAdder(sp, path, isFolder, arguments);
                                updatedItems++;
                            }
                        }
                    }
                }
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, $"Updated click events for {updatedItems} items");
            });
            }
            else
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings, "Application.Current is null, cannot update icon.");
            }
        }



        public static void ClickEventAdder(StackPanel sp, string path, bool isFolder, string arguments = null)
        {
            // Store only path, isFolder, and arguments in Tag
            sp.Tag = new { FilePath = path, IsFolder = isFolder, Arguments = arguments };

            // Check if path is a shortcut and correct isFolder for folder shortcuts
            bool isShortcut = System.IO.Path.GetExtension(path).ToLower() == ".lnk";
            string targetPath = isShortcut ? Utility.GetShortcutTarget(path) : path;
            if (isShortcut && System.IO.Directory.Exists(targetPath))
            {
                isFolder = true;
            }

            // --- NAMED LOCAL FUNCTIONS FOR EVENTS ---
            void MouseDownHandler(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;

                // Runtime Correction for Extension Mismatch
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                {
                    if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string potentialUrlPath = System.IO.Path.ChangeExtension(path, ".url");
                        if (System.IO.File.Exists(potentialUrlPath))
                        {
                            path = potentialUrlPath;
                        }
                    }
                }

                // CTRL + CLICK LOGIC
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                    string containerId = win?.Tag?.ToString();
                    dynamic container = null;
                    if (!string.IsNullOrEmpty(containerId))
                        container = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);

                    if (container != null && container.ItemsType?.ToString() == "Portal")
                    {
                        if (isFolder)
                        {
                            NavigatePortalContainer(container, path);
                            e.Handled = true;
                            return;
                        }
                        else
                        {
                            e.Handled = true;
                            return;
                        }
                    }

                    System.Windows.Point mousePosition = e.GetPosition(sp);
                    IconDragDropManager.StartIconDrag(sp, mousePosition);
                    e.Handled = true;
                    return;
                }

                bool singleClickToLaunch = SettingsManager.SingleClickToLaunch;

                try
                {
                    bool isShortcutLocal = System.IO.Path.GetExtension(path).ToLower() == ".lnk";
                    bool targetExists;
                    string resolvedPath = path;

                    if (isShortcutLocal)
                    {
                        resolvedPath = FilePathUtilities.GetShortcutTargetUnicodeSafe(path);
                        if (string.IsNullOrEmpty(resolvedPath))
                        {
                            targetExists = false;
                        }
                        else
                        {
                            targetExists = isFolder ? System.IO.Directory.Exists(resolvedPath) : System.IO.File.Exists(resolvedPath);
                            if (!isFolder && System.IO.Directory.Exists(resolvedPath))
                            {
                                isFolder = true;
                                targetExists = true;
                            }
                        }
                    }
                    else
                    {
                        targetExists = isFolder ? System.IO.Directory.Exists(path) : System.IO.File.Exists(path);
                    }

                    bool isStoreApp = false;
                    string scrapedPath = null;

                    if (!targetExists && isShortcutLocal)
                    {
                        isStoreApp = Utility.IsStoreAppShortcut(path);
                        if (!isStoreApp)
                        {
                            scrapedPath = GetScrapedNetworkPath(path);
                            if (!string.IsNullOrEmpty(scrapedPath))
                            {
                                targetExists = true;
                            }
                        }
                    }

                    if (!targetExists && !isStoreApp) return;

                    if ((singleClickToLaunch && e.ClickCount == 1) || (!singleClickToLaunch && e.ClickCount == 2))
                    {
                        LaunchItem(sp, path, isFolder, arguments);
                        e.Handled = true;
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error checking target existence: {ex.Message}");
                }
            }

            void MouseMoveHandler(object sender, MouseEventArgs e)
            {
                if (IconDragDropManager.IsDragging)
                {
                    try
                    {
                        System.Windows.Point screenPosition = sp.PointToScreen(e.GetPosition(sp));
                        IconDragDropManager.HandleDragMove(screenPosition);
                    }
                    catch { }
                }
            }

            void MouseUpHandler(object sender, MouseButtonEventArgs e)
            {
                if (IconDragDropManager.IsDragging)
                {
                    try
                    {
                        WrapPanel wrapPanel = FindVisualParent<WrapPanel>(sp);
                        if (wrapPanel != null)
                        {
                            System.Windows.Point finalPosition = e.GetPosition(wrapPanel);
                            IconDragDropManager.CompleteDrag(finalPosition);
                        }
                        else IconDragDropManager.CancelDrag();
                        e.Handled = true;
                    }
                    catch
                    {
                        IconDragDropManager.CancelDrag();
                    }
                }
            }

            void KeyUpHandler(object sender, KeyEventArgs e)
            {
                if (IconDragDropManager.IsDragging &&
                    (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) &&
                    !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    IconDragDropManager.CancelDrag();
                    e.Handled = true;
                }
            }

            // --- SAFELY REMOVE PREVIOUS HANDLERS (WPF APPROACH) ---
            sp.RemoveHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(MouseDownHandler));
            sp.RemoveHandler(UIElement.MouseMoveEvent, new MouseEventHandler(MouseMoveHandler));
            sp.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(MouseUpHandler));
            sp.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(KeyUpHandler));

            // --- ATTACH FRESH HANDLERS ---
            sp.MouseLeftButtonDown += MouseDownHandler;
            sp.MouseMove += MouseMoveHandler;
            sp.MouseLeftButtonUp += MouseUpHandler;
            sp.KeyUp += KeyUpHandler;
        }



        private static void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var sp = sender as StackPanel;
            if (sp == null || sp.Tag == null) return;

            // 1. Extract Data safely using dynamic
            dynamic data = sp.Tag;
            string path = data.FilePath;
            bool isFolder = data.IsFolder;
            string arguments = data.Arguments;

            // 2. Extension Mismatch Fix (Legacy Logic)
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
            {
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string urlPath = System.IO.Path.ChangeExtension(path, ".url");
                    if (System.IO.File.Exists(urlPath))
                    {
                        path = urlPath;
                        // Update Tag for consistency
                        sp.Tag = new { FilePath = path, IsFolder = isFolder, Arguments = arguments };
                    }
                }
            }


            // replaced on 2.5. 4.185
            //// 3. Ctrl + Click (Drag Start)
            //if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            //{
            //    IconDragDropManager.StartIconDrag(sp, e.GetPosition(sp));
            //    e.Handled = true;
            //    return;
            //}


            // 3. Ctrl + Click Logic (Navigation vs Drag)
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                // Detect Container Context
                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);
                string containerId = win?.Tag?.ToString();
                dynamic container = null;
                if (!string.IsNullOrEmpty(containerId))
                    container = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);

                // --- NEW: PORTAL NAVIGATION LOGIC ---
                if (container != null && container.ItemsType?.ToString() == "Portal")
                {
                    // Only navigate if it is a Folder
                    if (isFolder)
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Portal Navigation triggered for: {path}");
                        NavigatePortalContainer(container, path);
                        e.Handled = true;
                        return;
                    }
                    else
                    {
                        // It's a file in a Portal Container. 
                        // Do NOT drag (unsupported). Do NOT navigate.
                        // Future: Open With...
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Ctrl+Click on Portal File ignored (No Drag support)");
                        e.Handled = true;
                        return;
                    }
                }
                // ------------------------------------

                // DEFAULT: Data Container Drag
                IconDragDropManager.StartIconDrag(sp, e.GetPosition(sp));
                e.Handled = true;
                return;
            }







            // 4. LAUNCH LOGIC (Reads LIVE Settings directly)
            bool singleClick = SettingsManager.SingleClickToLaunch;

            // Target Validation Logic
            bool isShortcut = System.IO.Path.GetExtension(path).ToLower() == ".lnk";
            bool targetExists = false;

            if (isShortcut)
            {
                string resolved = FilePathUtilities.GetShortcutTargetUnicodeSafe(path);
                if (string.IsNullOrEmpty(resolved)) resolved = GetScrapedNetworkPath(path);

                if (!string.IsNullOrEmpty(resolved))
                {
                    targetExists = isFolder ? System.IO.Directory.Exists(resolved) : System.IO.File.Exists(resolved);
                    // Correction: If we thought it was a file but it's a folder, update state
                    if (!isFolder && System.IO.Directory.Exists(resolved)) { isFolder = true; targetExists = true; }
                }
            }
            else
            {
                targetExists = isFolder ? System.IO.Directory.Exists(path) : System.IO.File.Exists(path);
            }

            // Check Special Cases
            bool isStoreApp = isShortcut && Utility.IsStoreAppShortcut(path);
            bool isScraped = !targetExists && isShortcut && !string.IsNullOrEmpty(GetScrapedNetworkPath(path));

            if (targetExists || isStoreApp || isScraped)
            {
                if (singleClick && e.ClickCount == 1)
                {
                    LaunchItem(sp, path, isFolder, arguments);
                    e.Handled = true;
                }
                else if (!singleClick && e.ClickCount == 2)
                {
                    LaunchItem(sp, path, isFolder, arguments);
                    e.Handled = true;
                }
            }
        }

        private static void Icon_MouseMove(object sender, MouseEventArgs e)
        {
            if (IconDragDropManager.IsDragging && sender is StackPanel sp)
            {
                try { IconDragDropManager.HandleDragMove(sp.PointToScreen(e.GetPosition(sp))); } catch { }
            }
        }

        private static void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IconDragDropManager.IsDragging && sender is StackPanel sp)
            {
                try
                {
                    var wrapPanel = FindVisualParent<WrapPanel>(sp);
                    if (wrapPanel != null) IconDragDropManager.CompleteDrag(e.GetPosition(wrapPanel));
                    else IconDragDropManager.CancelDrag();
                    e.Handled = true;
                }
                catch { IconDragDropManager.CancelDrag(); }
            }
        }

        private static void Icon_KeyUp(object sender, KeyEventArgs e)
        {
            if (IconDragDropManager.IsDragging &&
               (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) &&
               !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            {
                IconDragDropManager.CancelDrag();
                e.Handled = true;
            }
        }


        // --- NEW: Unified Manual Hide (Used by Tray Icon) ---
        public static void ForceHideContainers()
        {
            _areContainersAutoHidden = true;

            var containers = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
            foreach (var container in containers)
            {
                // --- ANTI-FLICKER SAFEGUARD ---
                // Skip containers that are already hidden or currently invisible
                if (container.Visibility != Visibility.Visible || container.Opacity == 0.0) continue;

                // Animate the fade out
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(250)
                };

                // When the animation finishes, physically hide the window
                fadeOut.Completed += (s, e) =>
                {
                    container.Visibility = Visibility.Hidden;
                };

                container.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "Containers faded out and hidden.");
        }






        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }


        private static void LaunchItem(StackPanel sp, string path, bool isFolder, string arguments)
        {
            try
            {
                // --- NEW: Ignore clicks on internal placeholders ---
                if (path != null && path.StartsWith("INTERNAL_BLANK_")) return;
                // 1. Visual Feedback
                NonActivatingWindow win = FindVisualParent<NonActivatingWindow>(sp);

                // FIX: Use ID (Tag) lookup instead of Title (Robust)
                string containerId = win?.Tag?.ToString();
                dynamic container = null;

                if (!string.IsNullOrEmpty(containerId))
                {
                    container = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                }

                // Fallback to Title only if ID lookup failed
                if (container == null)
                {
                    container = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Title == win?.Title);
                }

                // FIX: Read LIVE Global Setting directly from SettingsManager
                // This bypasses the stale _options cache.
                LaunchEffectsManager.LaunchEffect effect = SettingsManager.LaunchEffect;

                // Check for Container-Specific Override
                string customEffect = container?.CustomLaunchEffect?.ToString();
                if (!string.IsNullOrEmpty(customEffect))
                {
                    try
                    {
                        effect = (LaunchEffectsManager.LaunchEffect)Enum.Parse(typeof(LaunchEffectsManager.LaunchEffect), customEffect, true);
                    }
                    catch { }
                }

                LaunchEffectsManager.ExecuteLaunchEffect(sp, effect);

                // 2. Path Resolution
                string fullPath = path;
                try { fullPath = System.IO.Path.GetFullPath(path); } catch { }

                string extension = System.IO.Path.GetExtension(fullPath).ToLower();
                bool isUrlFile = extension == ".url";
                bool isLnk = extension == ".lnk";

                string targetPath = fullPath;
                string workingDirectory = "";
                string finalArguments = arguments ?? "";

                // 3. Resolve Target
                if (isUrlFile)
                {
                    targetPath = ExtractUrlFromUrlFile(fullPath);
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Invalid .url file: {path}", "Error");
                        return;
                    }
                }
                else if (isLnk)
                {
                    string resolved = FilePathUtilities.GetShortcutTargetUnicodeSafe(fullPath);
                    if (string.IsNullOrEmpty(resolved)) resolved = GetScrapedNetworkPath(fullPath);

                    if (!string.IsNullOrEmpty(resolved))
                    {
                        targetPath = resolved;
                        try
                        {
                            WshShell shell = new WshShell();
                            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(fullPath);
                            string rawTarget = shortcut.TargetPath?.ToLower() ?? "";
                            if (!rawTarget.EndsWith("explorer.exe"))
                            {
                                if (string.IsNullOrEmpty(finalArguments)) finalArguments = shortcut.Arguments;
                                workingDirectory = shortcut.WorkingDirectory;
                            }
                        }
                        catch { }
                    }
                }

                // 4. Classification
                bool isWebUrl = IsWebUrl(targetPath);
                bool isStoreApp = isLnk && Utility.IsStoreAppShortcut(fullPath);
                bool isNetworkRoot = !string.IsNullOrEmpty(targetPath) && targetPath.StartsWith(@"\\") && !targetPath.Contains(@":\");
                bool isSpecialPath = IsSpecialWindowsPath(targetPath);

                bool isTargetFolder = false;
                bool targetExists = false;

                if (!isWebUrl && !isSpecialPath && !isNetworkRoot && !isStoreApp)
                {
                    isTargetFolder = System.IO.Directory.Exists(targetPath);
                    targetExists = isTargetFolder || System.IO.File.Exists(targetPath);
                }

                // 5. Validation Guard
                if (!targetExists && !isWebUrl && !isSpecialPath && !isStoreApp && !isNetworkRoot)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"Target not found: {targetPath}");
                    MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Target not found:\n{targetPath}", "Launch Error");
                    return;
                }

                // 6. Admin & Different User Logic
                bool alwaysRunAsAdmin = false;
                bool alwaysRunAsDifferentUser = false;
                try
                {
                    if (container != null && container.ItemsType?.ToString() == "Data")
                    {
                        var items = container.Items as JArray ?? new JArray();
                        bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";

                        if (tabsEnabled)
                        {
                            var tabs = container.Tabs as JArray ?? new JArray();
                            int currentTabIndex = Convert.ToInt32(container.CurrentTab?.ToString() ?? "0");
                            if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
                            {
                                var currentTab = tabs[currentTabIndex] as JObject;
                                items = currentTab?["Items"] as JArray ?? items;
                            }
                        }

                        var matchingItem = items.FirstOrDefault(i => string.Equals(
                            System.IO.Path.GetFullPath(i["Filename"]?.ToString() ?? ""),
                            fullPath, StringComparison.OrdinalIgnoreCase));

                        if (matchingItem != null)
                        {
                            alwaysRunAsAdmin = Convert.ToBoolean(matchingItem["AlwaysRunAsAdmin"] ?? false);
                            alwaysRunAsDifferentUser = Convert.ToBoolean(matchingItem["AlwaysRunAsDifferentUser"] ?? false);
                        }
                    }
                }
                catch { }

                // 7. Execution
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.UseShellExecute = true;

                if (!isWebUrl && !isSpecialPath && !isTargetFolder && !isNetworkRoot)
                {
                    if (alwaysRunAsAdmin)
                        psi.Verb = "runas";
                    else if (alwaysRunAsDifferentUser)
                        psi.Verb = "runasuser";
                }

                if (isStoreApp)
                {
                    psi.FileName = "explorer.exe";
                    psi.Arguments = $"\"{fullPath}\"";
                    psi.WorkingDirectory = "";
                }
                else if (isWebUrl)
                {
                    psi.FileName = targetPath;
                }
                else if (isTargetFolder || isNetworkRoot)
                {
                    psi.FileName = "explorer.exe";
                    psi.Arguments = $"\"{targetPath}\"";
                }
                else
                {
                    psi.FileName = targetPath;
                    if (!string.IsNullOrEmpty(finalArguments)) psi.Arguments = finalArguments;

                    if (!string.IsNullOrEmpty(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
                        psi.WorkingDirectory = workingDirectory;
                    else
                    {
                        string dir = System.IO.Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                            psi.WorkingDirectory = dir;
                    }
                }

                Process.Start(psi);
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Launched: {targetPath}");
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("canceled"))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Launch Error: {ex.Message}");
                    if (!SettingsManager.SuppressLaunchWarnings)
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error launching: {ex.Message}", "Launch Error");
                }
            }
        }



        //// Determines if a path is a web URL
        //private static bool IsWebUrl(string path)
        //{
        //    if (string.IsNullOrEmpty(path)) return false;

        //    return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        //   path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);
        //}

        // Determines if a path is a web URL
        private static bool IsWebUrl(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (Uri.TryCreate(path, UriKind.Absolute, out Uri uriResult))
            {
                string scheme = uriResult.Scheme.ToLower();
                return scheme == Uri.UriSchemeHttp ||
                       scheme == Uri.UriSchemeHttps ||
                       scheme == Uri.UriSchemeFtp ||
                       scheme == Uri.UriSchemeMailto;
            }
            return false;
        }


        //// Determines if a path is a special Windows path
        //private static bool IsSpecialWindowsPath(string path)
        //{
        //    if (string.IsNullOrEmpty(path)) return false;

        //    return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
        //           path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ||
        //           path.Contains("::") ||
        //           path.StartsWith("control", StringComparison.OrdinalIgnoreCase);
        //}


        // Determines if a path is a special Windows path
        private static bool IsSpecialWindowsPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Support Custom App Protocols globally (steam://, spotify:, discord:, etc.)
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri uriResult))
            {
                string scheme = uriResult.Scheme.ToLower();
                if (scheme != Uri.UriSchemeFile &&
                    scheme != Uri.UriSchemeHttp &&
                    scheme != Uri.UriSchemeHttps &&
                    scheme != Uri.UriSchemeFtp &&
                    scheme != Uri.UriSchemeMailto)
                {
                    return true;
                }
            }

            return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("::") ||
                   path.StartsWith("control", StringComparison.OrdinalIgnoreCase);
        }


        // Extracts the URL from a .url file
        private static string ExtractUrlFromUrlFile(string urlFilePath)
        {
            try
            {
                if (!System.IO.File.Exists(urlFilePath))
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"URL file not found: {urlFilePath}");
                    return null;
                }

                string[] lines = System.IO.File.ReadAllLines(urlFilePath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = line.Substring(4).Trim(); // Remove "URL=" prefix
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, $"Extracted URL from {urlFilePath}: {url}");
                        return url;
                    }
                }

                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General, $"No URL= line found in .url file: {urlFilePath}");
                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Error reading .url file {urlFilePath}: {ex.Message}");
                return null;
            }
        }

        // Helper method for network path detection
        public static bool IsNetworkPath(string filePath)
        {
            try
            {
                bool isShortcut = System.IO.Path.GetExtension(filePath).ToLower() == ".lnk";

                if (isShortcut)
                {
                    // For shortcuts, check the target path
                    if (System.IO.File.Exists(filePath))
                    {
                        string targetPath = Utility.GetShortcutTarget(filePath);
                        if (!string.IsNullOrEmpty(targetPath))
                        {
                            // Check if target is UNC path
                            bool isUncPath = targetPath.StartsWith("\\\\");
                            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Shortcut {filePath} targets {targetPath}, IsUNC: {isUncPath}");
                            return isUncPath;
                        }
                    }
                }
                else
                {
                    // For direct paths, check if it's UNC
                    bool isUncPath = filePath.StartsWith("\\\\");
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.IconHandling, $"Direct path {filePath}, IsUNC: {isUncPath}");
                    return isUncPath;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.IconHandling, $"Error checking if {filePath} is network path: {ex.Message}");
            }
            return false;
        }

        // Public method to refresh an icon's click handlers after shortcut editing
        // Called by EditShortcutWindow to ensure immediate argument updates
        public static void RefreshIconClickHandlers(string shortcutPath, string newDisplayName)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Refreshing click handlers for edited shortcut: {shortcutPath}");

                // Find all container windows and locate the icon
                var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>();
                bool iconFound = false;

                foreach (var window in windows)
                {
                    // Find the WrapPanel containing icons
                    var wrapPanel = ContainerUtilities.FindWrapPanel(window);

                    if (wrapPanel == null) continue;

                    // Find the specific icon StackPanel
                    foreach (StackPanel iconPanel in wrapPanel.Children.OfType<StackPanel>())
                    {
                        var tagData = iconPanel.Tag;
                        if (tagData != null)
                        {
                            string filePath = tagData.GetType().GetProperty("FilePath")?.GetValue(tagData)?.ToString();
                            if (!string.IsNullOrEmpty(filePath) &&
                                string.Equals(System.IO.Path.GetFullPath(filePath), System.IO.Path.GetFullPath(shortcutPath), StringComparison.OrdinalIgnoreCase))
                            {
                                // Found it! Refresh this icon completely
                                RefreshSingleIconComplete(iconPanel, shortcutPath, newDisplayName, window);
                                iconFound = true;
                                break;
                            }
                        }
                    }
                    if (iconFound) break;
                }

                if (!iconFound)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        $"Could not find icon to refresh for: {shortcutPath}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error refreshing icon click handlers: {ex.Message}");
            }
        }

        // Completely refreshes a single icon with fresh data from the .lnk fil
        private static void RefreshSingleIconComplete(StackPanel iconPanel, string shortcutPath, string newDisplayName, NonActivatingWindow parentWindow)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Performing complete refresh of icon: {shortcutPath}");

                // 1. Read fresh data
                string freshTargetPath = shortcutPath;
                string freshArguments = "";
                bool isFolder = false;
                string workingDirectory = "";

                // Identify Type
                bool isUrl = System.IO.Path.GetExtension(shortcutPath).ToLower() == ".url";
                bool isLnk = System.IO.Path.GetExtension(shortcutPath).ToLower() == ".lnk";

                if (isLnk)
                {
                    try
                    {
                        WshShell shell = new WshShell();
                        IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                        freshTargetPath = shortcut.TargetPath ?? shortcutPath;
                        freshArguments = shortcut.Arguments ?? "";
                        workingDirectory = shortcut.WorkingDirectory ?? "";
                        isFolder = System.IO.Directory.Exists(freshTargetPath);
                    }
                    catch { }
                }
                else if (isUrl)
                {
                    // For URLs, we can try to extract the clean URL, but IsFolder is always false
                    string url = CoreUtilities.ExtractWebUrlFromFile(shortcutPath);
                    if (!string.IsNullOrEmpty(url)) freshTargetPath = url;
                }

                // 2. Update Tag
                iconPanel.Tag = new
                {
                    FilePath = shortcutPath,
                    IsFolder = isFolder,
                    Arguments = freshArguments
                };

                // 3. Update Text
                if (!string.IsNullOrEmpty(newDisplayName))
                {
                    var textBlock = iconPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null)
                    {
                        string displayText = newDisplayName.Length > SettingsManager.MaxDisplayNameLength
                            ? newDisplayName.Substring(0, SettingsManager.MaxDisplayNameLength) + "..."
                            : newDisplayName;
                        textBlock.Text = displayText;
                    }
                }

                // 4. Update ToolTip
                string displayTarget = freshTargetPath;

                // Clean up MS Store App targets for the tooltip
                if (!string.IsNullOrEmpty(displayTarget) && displayTarget.Contains("!") && !displayTarget.Contains(":\\"))
                {
                    string packageId = displayTarget.Split('!')[0];
                    int hashIndex = packageId.IndexOf('_');
                    if (hashIndex > 0) packageId = packageId.Substring(0, hashIndex);
                    displayTarget = $"Windows App ({packageId})";
                }

                string toolTipText = $"{newDisplayName ?? System.IO.Path.GetFileNameWithoutExtension(shortcutPath)}\nTarget: {displayTarget}";
                if (!string.IsNullOrEmpty(freshArguments)) toolTipText += $"\nArguments: {freshArguments}";
                iconPanel.ToolTip = new ToolTip { Content = toolTipText };

                // 5. UPDATE ICON IMAGE (The Critical Fix)
                var ico = iconPanel.Children.OfType<System.Windows.Controls.Image>().FirstOrDefault();
                if (ico != null)
                {
                    ImageSource newIcon = null;

                    // A. Try Manual Extraction for .url (Fixes Runtime Update)
                    if (isUrl)
                    {
                        var urlIcon = GetUrlCustomIcon(shortcutPath);
                        if (urlIcon.Path != null)
                        {
                            newIcon = IconManager.ExtractIconFromFile(urlIcon.Path, urlIcon.Index);
                        }
                    }
                    // B. Try WshShell for .lnk
                    else if (isLnk)
                    {
                        try
                        {
                            WshShell shell = new WshShell();
                            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                            if (!string.IsNullOrEmpty(shortcut.IconLocation) && shortcut.IconLocation != ",0")
                            {
                                string[] parts = shortcut.IconLocation.Split(',');
                                if (System.IO.File.Exists(parts[0]))
                                {
                                    int idx = 0;
                                    if (parts.Length > 1) int.TryParse(parts[1], out idx);
                                    newIcon = IconManager.ExtractIconFromFile(parts[0], idx);
                                }
                            }
                        }
                        catch { }
                    }

                    // C. Fallback to Theme/Shell Icon
                    if (newIcon == null)
                    {
                        if (isUrl)
                        {
                            newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/link-White.png"));
                        }
                        else if (isFolder)
                        {
                            // Check for broken target
                            if (!FilePathUtilities.DoesFolderExist(shortcutPath, true))
                                newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-WhiteX.png"));
                            else
                                newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/folder-White.png"));
                        }
                        else
                        {
                            // Standard file fallback
                            newIcon = Utility.GetShellIcon(freshTargetPath, false);
                            if (newIcon == null) newIcon = new BitmapImage(new Uri("pack://application:,,,/Resources/file-WhiteX.png"));
                        }
                    }

                    // Apply and Update Cache
                    if (newIcon != null)
                    {
                        ico.Source = newIcon;
                        IconManager.IconCache[shortcutPath] = newIcon; // Force update cache
                    }
                }

                // 6. Re-attach Events and Update Data
                ClickEventAdder(iconPanel, shortcutPath, isFolder, freshArguments);
                UpdateContainerDataForIcon(shortcutPath, newDisplayName, parentWindow);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, $"Complete icon refresh successful for: {shortcutPath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error in complete icon refresh: {ex.Message}");
            }
        }
        // Updates container JSON data after icon refresh


        // Updates container JSON data after icon refresh
        private static void UpdateContainerDataForIcon(string shortcutPath, string newDisplayName, NonActivatingWindow parentWindow)
        {
            try
            {
                if (string.IsNullOrEmpty(newDisplayName)) return;

                string containerId = parentWindow.Tag?.ToString();
                if (string.IsNullOrEmpty(containerId)) return;

                var container = ContainerDataManager.ContainerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
                if (container == null) return;

                // FIX: Search for the item in BOTH Main Items and ALL Tabs
                bool found = false;

                // Helper function to search and update a list
                bool SearchList(JArray list)
                {
                    if (list == null) return false;
                    foreach (var item in list)
                    {
                        string itemFilename = item["Filename"]?.ToString();
                        if (!string.IsNullOrEmpty(itemFilename) &&
                            string.Equals(System.IO.Path.GetFullPath(itemFilename), System.IO.Path.GetFullPath(shortcutPath), StringComparison.OrdinalIgnoreCase))
                        {
                            item["DisplayName"] = newDisplayName;
                            return true; // Found and updated
                        }
                    }
                    return false;
                }

                // 1. Check Main Items
                if (SearchList(container.Items as JArray)) found = true;

                // 2. Check Tabs (Always check tabs to keep Tab 0 and Main Items completely synced)
                var tabs = container.Tabs as JArray;
                if (tabs != null)
                {
                    foreach (var tab in tabs)
                    {
                        if (SearchList(tab["Items"] as JArray))
                        {
                            found = true;
                            // We purposefully do NOT break here to ensure all instances across all tabs are updated
                        }
                    }
                }

                if (found)
                {
                    ContainerDataManager.SaveContainerData();
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Updated JSON DisplayName for: {shortcutPath}");
                }
                else
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"Could not find item in JSON to update name: {shortcutPath}");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error updating container JSON: {ex.Message}");
            }
        }







        // Size feedback during resizing
        private static void ShowSizeFeedback(double width, double height)
        {
            if (_sizeFeedbackWindow == null)
            {
                _sizeFeedbackWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Width = 100,
                    Height = 30,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                var label = new Label
                {
                    Content = "",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = System.Windows.Media.Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _sizeFeedbackWindow.Content = label;
            }

            var labelContent = (Label)_sizeFeedbackWindow.Content;
            labelContent.Content = $"{Math.Round(width)} x {Math.Round(height)}";

            var mousePos = System.Windows.Forms.Cursor.Position;
            _sizeFeedbackWindow.Left = mousePos.X + 10;
            _sizeFeedbackWindow.Top = mousePos.Y + 10;

            _sizeFeedbackWindow.Show();

            // --- BUG FIX: Unified Debounce Timer ---
            // This guarantees the indicator will ALWAYS disappear 1.5 seconds 
            // after the last call, preventing orphaned windows on screen.
            if (_hideTimer == null)
            {
                _hideTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                _hideTimer.Tick += (s, e) => HideSizeFeedback();
            }
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private static void HideSizeFeedback()
        {
            if (_sizeFeedbackWindow != null)
            {
                _sizeFeedbackWindow.Hide();
            }
            if (_hideTimer != null)
            {
                _hideTimer.Stop();
            }
        }

        public static void OnResizingStarted(NonActivatingWindow container)
        {
            if (SettingsManager.EnableDimensionSnap)
            {
                container.SizeChanged += UpdateSizeFeedback;
                ShowSizeFeedback(container.Width, container.Height);
            }
        }

        public static void OnResizingEnded(NonActivatingWindow container)
        {
            if (SettingsManager.EnableDimensionSnap)
            {
                container.SizeChanged -= UpdateSizeFeedback;

                double snappedWidth = Math.Round(container.Width / 10.0) * 10;
                double snappedHeight = Math.Round(container.Height / 10.0) * 10;

                container.Width = snappedWidth;
                container.Height = snappedHeight;

                dynamic containerData = GetContainerData().FirstOrDefault(f => f.Title == container.Title);
                if (containerData != null)
                {
                    containerData.Width = snappedWidth;
                    containerData.Height = snappedHeight;
                    ContainerDataManager.SaveContainerData();
                }

                // Show one last time. The unified timer in ShowSizeFeedback will clean it up automatically.
                ShowSizeFeedback(snappedWidth, snappedHeight);
            }
        }





        // --- HELPER: Efficient Dead Shortcut Check using Cached States ---
        private static bool HasDeadShortcuts(dynamic container)
        {
            try
            {
                // Only Data containers track dead shortcuts
                if (container.ItemsType?.ToString() != "Data") return false;

                bool tabsEnabled = container.TabsEnabled?.ToString().ToLower() == "true";
                if (tabsEnabled)
                {
                    var tabs = container.Tabs as JArray;
                    if (tabs != null)
                    {
                        foreach (var tab in tabs)
                        {
                            var items = tab["Items"] as JArray;
                            if (items != null && CheckItemsForDead(items)) return true;
                        }
                    }
                }
                else
                {
                    var items = container.Items as JArray;
                    if (items != null && CheckItemsForDead(items)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool CheckItemsForDead(JArray items)
        {
            foreach (var item in items)
            {
                string path = item["Filename"]?.ToString();
                if (!string.IsNullOrEmpty(path))
                {
                    // Check our cache which is updated by the background TargetChecker
                    // This avoids scanning the disk on the UI thread
                    if (_iconStates.TryGetValue(path, out var state))
                    {
                        if (state.IsBroken) return true;
                    }
                }
            }
            return false;
        }










        // Add this static method inside your ContainerManager class
        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;

            if (msg == WM_GETMINMAXINFO)
            {
                // Get the screen information for the current monitor
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (screen != null)
                {
                    // Define the limit: 90% of the working area
                    int maxWidth = (int)(screen.WorkingArea.Width * 0.90);
                    int maxHeight = (int)(screen.WorkingArea.Height * 0.90);

                    // Marshal the structure
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

                    // Force the "Maximized" size to be 90%, not 100%
                    mmi.ptMaxSize.x = maxWidth;
                    mmi.ptMaxSize.y = maxHeight;

                    // Force the user-draggable limit to be 90%
                    mmi.ptMaxTrackSize.x = maxWidth;
                    mmi.ptMaxTrackSize.y = maxHeight;

                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }



        private static string GetSafeProperty(dynamic obj, string propName)
        {
            try
            {
                if (obj is JObject jObj && jObj[propName] != null) return jObj[propName].ToString();
                return obj.GetType().GetProperty(propName)?.GetValue(obj, null)?.ToString() ?? "";
            }
            catch { return ""; }
        }



        private static void UpdateSizeFeedback(object sender, SizeChangedEventArgs e)
        {
            var container = sender as NonActivatingWindow;
            if (container != null)
            {
                // --- BUG FIX: Ignore programmatic animations ---
                // Do not show the resizing indicator if the container is just rolling up or down
                string containerId = container.Tag?.ToString();
                if (!string.IsNullOrEmpty(containerId) && _containersInTransition.Contains(containerId)) return;

                ShowSizeFeedback(container.Width, container.Height);
            }
        }


    }

}