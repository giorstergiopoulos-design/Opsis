using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using WinRTMediaControl = Windows.Media.Control;
using WinRTNotifications = Windows.UI.Notifications;
using WinRTNotificationsMgmt = Windows.UI.Notifications.Management;
using WinRTDeviceEnum = Windows.Devices.Enumeration;
using WinRTBluetooth = Windows.Devices.Bluetooth;
using MotionDesk.Services;
using MotionDesk.UI;
// Ανακατευθύνει ΟΛΕΣ τις υπάρχουσες αναφορές "UiTheme.X" σε αυτό το αρχείο προς το WidgetTheme
// (ανεξάρτητο θέμα widgets — βλ. WidgetTheme.cs) αντί να αλλάξει το καθένα από τα δεκάδες σημεία
// ξεχωριστά. Ισχύει ΜΟΝΟ σε αυτό το αρχείο· το MainWindow.cs συνεχίζει να βλέπει το πραγματικό UiTheme.
using UiTheme = MotionDesk.UI.WidgetTheme;

namespace MotionDesk.Widgets
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    public class WidgetBridge
    {
        private readonly WidgetWindow _window;

        public WidgetBridge(WidgetWindow window)
        {
            _window = window;
        }

        public float GetCpuUsage() => (float)SystemMonitorService.Instance.GetSnapshot().CpuPercent;
        public float GetAvailableRam() => (float)SystemMonitorService.Instance.GetSnapshot().AvailableMemoryMb;

        public string GetSystemStatsJson()
        {
            var metrics = SystemMonitorService.Instance.GetSnapshot();
            return JsonSerializer.Serialize(new
            {
                cpu = metrics.CpuPercent,
                ramAvailableMb = metrics.AvailableMemoryMb,
                ramTotalMb = metrics.TotalMemoryMb,
                timestamp = metrics.Timestamp.ToString("HH:mm:ss")
            });
        }

        public async System.Threading.Tasks.Task<string> GetWeatherDataJson(double lat, double lon)
        {
            return await WeatherService.GetWeatherJsonAsync(lat, lon);
        }

        public void StartDrag()
        {
            if (!_window.IsLocked) _window.BeginNativeDrag();
        }

        public bool ToggleLock()
        {
            _window.IsLocked = !_window.IsLocked;
            _window.SaveWidgetState();
            return _window.IsLocked;
        }

        public bool GetIsLocked() => _window.IsLocked;
    }

    // Διαφανές top-most overlay που σχεδιάζει τις μπλε γραμμές ευθυγράμμισης κατά το drag.
    public class SnapOverlayWindow : Form
    {
        private static SnapOverlayWindow? _instance;
        public static SnapOverlayWindow Instance => _instance ??= new SnapOverlayWindow();

        private int? _vertLineX;
        private int? _horizLineY;

        private SnapOverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Bounds = SystemInformation.VirtualScreen;

            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void ShowGuides(int? x, int? y)
        {
            _vertLineX = x;
            _horizLineY = y;

            if (x.HasValue || y.HasValue)
            {
                if (!Visible) Show();
                Invalidate();
            }
            else if (Visible)
            {
                Hide();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(220, 0, 210, 255), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };

            if (_vertLineX.HasValue) e.Graphics.DrawLine(pen, _vertLineX.Value, 0, _vertLineX.Value, Height);
            if (_horizLineY.HasValue) e.Graphics.DrawLine(pen, 0, _horizLineY.Value, Width, _horizLineY.Value);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    // Υπολογίζει snap-to-edge (οθόνη) και snap-to-widget (γειτονικά widgets), με προαιρετικό grid snap.
    public static class WidgetSnapEngine
    {
        public static int SnapThreshold { get; set; } = 15;
        public static int GridSize { get; set; } = 20;
        public static bool EnableGridSnap { get; set; } = false;

        public static void ApplySnap(WidgetWindow currentWidget, ref RECT targetRect, IReadOnlyCollection<WidgetWindow> activeWidgets)
        {
            int width = targetRect.Right - targetRect.Left;
            int height = targetRect.Bottom - targetRect.Top;

            Rectangle workArea = Screen.FromHandle(currentWidget.Handle).WorkingArea;

            int? snapX = null;
            int? snapY = null;

            if (Math.Abs(targetRect.Left - workArea.Left) < SnapThreshold)
            {
                targetRect.Left = workArea.Left;
                targetRect.Right = targetRect.Left + width;
                snapX = workArea.Left;
            }
            else if (Math.Abs(targetRect.Right - workArea.Right) < SnapThreshold)
            {
                targetRect.Right = workArea.Right;
                targetRect.Left = targetRect.Right - width;
                snapX = workArea.Right;
            }

            if (Math.Abs(targetRect.Top - workArea.Top) < SnapThreshold)
            {
                targetRect.Top = workArea.Top;
                targetRect.Bottom = targetRect.Top + height;
                snapY = workArea.Top;
            }
            else if (Math.Abs(targetRect.Bottom - workArea.Bottom) < SnapThreshold)
            {
                targetRect.Bottom = workArea.Bottom;
                targetRect.Top = targetRect.Bottom - height;
                snapY = workArea.Bottom;
            }

            foreach (var other in activeWidgets)
            {
                if (other == currentWidget || !other.Visible) continue;
                Rectangle b = other.Bounds;

                if (Math.Abs(targetRect.Left - b.Right) < SnapThreshold) { targetRect.Left = b.Right; targetRect.Right = targetRect.Left + width; snapX = b.Right; }
                else if (Math.Abs(targetRect.Right - b.Left) < SnapThreshold) { targetRect.Right = b.Left; targetRect.Left = targetRect.Right - width; snapX = b.Left; }
                else if (Math.Abs(targetRect.Left - b.Left) < SnapThreshold) { targetRect.Left = b.Left; targetRect.Right = targetRect.Left + width; snapX = b.Left; }

                if (Math.Abs(targetRect.Top - b.Bottom) < SnapThreshold) { targetRect.Top = b.Bottom; targetRect.Bottom = targetRect.Top + height; snapY = b.Bottom; }
                else if (Math.Abs(targetRect.Bottom - b.Top) < SnapThreshold) { targetRect.Bottom = b.Top; targetRect.Top = targetRect.Bottom - height; snapY = b.Top; }
                else if (Math.Abs(targetRect.Top - b.Top) < SnapThreshold) { targetRect.Top = b.Top; targetRect.Bottom = targetRect.Top + height; snapY = b.Top; }
            }

            if (EnableGridSnap && GridSize > 0)
            {
                targetRect.Left = (int)(Math.Round((double)targetRect.Left / GridSize) * GridSize);
                targetRect.Right = targetRect.Left + width;
                targetRect.Top = (int)(Math.Round((double)targetRect.Top / GridSize) * GridSize);
                targetRect.Bottom = targetRect.Top + height;
            }

            targetRect.Left = Math.Clamp(targetRect.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
            targetRect.Top = Math.Clamp(targetRect.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
            targetRect.Right = targetRect.Left + width;
            targetRect.Bottom = targetRect.Top + height;
            SnapOverlayWindow.Instance.ShowGuides(snapX, snapY);
        }
    }

    public class WidgetWindow : Form
    {
        private const int WM_MOVING = 0x0216;
        private const int WM_EXITSIZEMOVE = 0x0232;

        private WebView2? _webView;
        private System.Windows.Forms.Timer? _nativeMonitorTimer;
        public string WidgetId { get; }
        public bool IsLocked { get; set; } = false;
        public string Variant { get; set; } = "Digital";
        public bool Clock24Hour { get; set; } = true;
        public bool ClockShowSeconds { get; set; } = true;
        // Ζητήθηκε ρητά "analog clock διαφορετικά θέματα, digital clock με διαφορετικές
        // γραμματοσειρές & χρώματα". AnalogTheme: "Classic" (η υπάρχουσα εμφάνιση) | "Neon"
        // (λαμπερά χέρια/ενδείξεις) | "Minimal" (μόνο 12/3/6/9, λεπτότερα χέρια). DigitalColorArgb
        // = -1 σημαίνει "ακολούθησε το θέμα" (προεπιλογή, ίδια συμπεριφορά με πριν).
        public string AnalogTheme { get; set; } = "Classic";
        public string DigitalFontFamily { get; set; } = "Segoe UI";
        public int DigitalColorArgb { get; set; } = -1;
        public string AudioStyle { get; set; } = "WMP";
        // Στυλ απεικόνισης Network: "Sparkline" (γράφημα, προεπιλογή) ή "Bars" (μπάρες σήματος
        // με πραγματικές τιμές download/upload) — ζητήθηκε ρητά "διαφορετικά στυλ απεικόνισης
        // των μπαρών" και "στις 3 μπάρες να υπάρχουν πληροφορίες του δικτύου".
        public string NetworkStyle { get; set; } = "Sparkline";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? LocationName { get; set; }
        private Panel? _themedPanel;
        private Action? _themeHandler;
        private GlassLabel? _titleLabelRef;
        // ΔΙΟΡΘΩΣΗ "η διαφάνεια να αφορά το παράθυρο όχι το περιεχόμενο" — βλ. ίδιο σχόλιο στο
        // DeskContainerWindow.cs. Δεν χρησιμοποιούμε πια Form.Opacity (ξεθωριάζει μαζί και το
        // κείμενο/εικονίδια)· το MotionDesk.UI.GlassBlur θολώνει μόνο ό,τι είναι πίσω από το widget.
        private double _backgroundOpacity = 1.0;

        public WidgetWindow(string widgetId, int defaultX, int defaultY, int width, int height)
        {
            Flicker.Attach(this);
            WidgetId = widgetId;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(width, height);
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            // Ζητήθηκε ρητά "ο χρήστης να μπορεί να αλλάζει" το μέγεθος — χωρίς κάτω όριο ένα
            // native (χωρίς-πλαίσιο) resize θα μπορούσε να συρρικνώσει το widget σε μηδέν.
            this.MinimumSize = new Size(160, 100);
            this.AllowDrop = true;
            this.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
            this.DragDrop += (_, e) =>
            {
                // Γενικό "άνοιγμα με..." για αρχεία/φακέλους που σέρνονται πάνω σε ΟΠΟΙΟΔΗΠΟΤΕ
                // widget (όχι μόνο DeskContainers) — ζητήθηκε ρητά. Δεν έχει νόημα κάθε widget να
                // "απορροφά" το αρχείο σαν να είναι container· απλά το ανοίγει με τον προεπιλεγμένο
                // χειριστή, ίδια συμπεριφορά με διπλό-κλικ σε εικονίδιο επιφάνειας εργασίας.
                if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
                foreach (var f in files)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(f) { UseShellExecute = true }); }
                    catch { }
                }
            };

            LoadWidgetState(defaultX, defaultY);
            if (string.Equals(widgetId, "sysmon", StringComparison.OrdinalIgnoreCase))
                InitializeNativeSystemMonitor();
            else if (string.Equals(widgetId, "clock", StringComparison.OrdinalIgnoreCase))
                InitializeNativeClock();
            else if (string.Equals(widgetId, "network", StringComparison.OrdinalIgnoreCase))
                InitializeNativeNetwork();
            else if (string.Equals(widgetId, "audio", StringComparison.OrdinalIgnoreCase))
                InitializeNativeAudio();
            else if (string.Equals(widgetId, "weather", StringComparison.OrdinalIgnoreCase))
                InitializeNativeWeather();
            else if (string.Equals(widgetId, "disk", StringComparison.OrdinalIgnoreCase))
                InitializeNativeDisk();
            else if (string.Equals(widgetId, "quicksettings", StringComparison.OrdinalIgnoreCase))
                InitializeNativeQuickSettings();
            else if (string.Equals(widgetId, "calendar", StringComparison.OrdinalIgnoreCase))
                InitializeNativeCalendar();
            else if (string.Equals(widgetId, "media", StringComparison.OrdinalIgnoreCase))
                _ = InitializeNativeMediaAsync();
            else if (string.Equals(widgetId, "notifications", StringComparison.OrdinalIgnoreCase))
                _ = InitializeNativeNotificationsAsync();
            else if (string.Equals(widgetId, "bluetooth", StringComparison.OrdinalIgnoreCase))
                _ = InitializeNativeBluetoothAsync();
            else
                _ = InitializeWebViewAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nativeMonitorTimer?.Stop();
                _nativeMonitorTimer?.Dispose();
                _nativeMonitorTimer = null;
                _webView?.Dispose();
                _webView = null;
                if (_themeHandler != null) { ThemeManager.Changed -= _themeHandler; ThemeManager.Repainted -= _themeHandler; }
                _themeHandler = null;
            }
            base.Dispose(disposing);
        }

        // ΔΙΟΡΘΩΣΗ "το λευκό πλαίσιο widget ΔΕΝ διορθώθηκε" (ο χρήστης επιβεβαίωσε με πριν/μετά
        // screenshot): το DisableNativeBorderAccent καλούνταν μόνο μέσα στο ApplyRoundedShell
        // (constructor/resize/theme-change) — ΟΧΙ σε OnHandleCreated. Αν το WinForms αναδημιουργήσει
        // ΚΑΠΟΤΕ το native handle του widget (π.χ. αλλαγή DPI/οθόνης), το νέο HWND δεν είχε ΠΟΤΕ
        // εφαρμοσμένο το DWMWA_BORDER_COLOR="none" και το προεπιλεγμένο λευκό περίγραμμα των Windows
        // 11 επανεμφανιζόταν — ακριβώς το ίδιο πρότυπο με το ήδη-σωστό DeskContainerWindow.
        // OnHandleCreated (βλ. DeskContainerWindow.cs), τώρα αντιγραμμένο εδώ 1-προς-1. Επίσης
        // επιτρέπει drag&drop να περνάει σωστά ακόμα κι αν η εφαρμογή τρέχει elevated (UIPI).
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            GlassBlur.DisableNativeBorderAccent(Handle);
            GlassBlur.AllowDragDropAcrossUipi(Handle);
        }

        public void BeginNativeDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        // Ζητήθηκε ρητά "ο χρήστης να μπορεί να αλλάζει" το μέγεθος των widgets — δεν υπήρχε
        // κανένας τρόπος, αφού το FormBorderStyle είναι None (χωρίς πλαίσιο συστήματος με λαβές
        // αλλαγής μεγέθους). Ίδιο ακριβώς κόλπο με το BeginNativeDrag (HTCAPTION) αλλά με
        // HTBOTTOMRIGHT (0x11) — αναθέτει το resize στον ίδιο τον Windows compositor.
        public void BeginNativeResize()
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x11, 0);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOVING && !IsLocked)
            {
                RECT rect = Marshal.PtrToStructure<RECT>(m.LParam);
                WidgetSnapEngine.ApplySnap(this, ref rect, WidgetHostEngine.Instance.GetActiveWidgets());
                Marshal.StructureToPtr(rect, m.LParam, false);
            }
            else if (m.Msg == WM_EXITSIZEMOVE)
            {
                SnapOverlayWindow.Instance.ShowGuides(null, null);
                SaveWidgetState();
            }

            base.WndProc(ref m);
        }

        public void SaveWidgetState()
        {
            try
            {
                var configPath = GetConfigFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                var state = new WidgetState
                {
                    WidgetId = WidgetId, X = Location.X, Y = Location.Y, Width = Width, Height = Height, IsLocked = IsLocked,
                    Variant = Variant, Latitude = Latitude, Longitude = Longitude, LocationName = LocationName,
                    Clock24Hour = Clock24Hour, ClockShowSeconds = ClockShowSeconds, AudioStyle = AudioStyle,
                    Opacity = _backgroundOpacity, NetworkStyle = NetworkStyle,
                    AnalogTheme = AnalogTheme, DigitalFontFamily = DigitalFontFamily, DigitalColorArgb = DigitalColorArgb
                };
                MotionDesk.Services.AtomicFile.WriteAllText(configPath, JsonSerializer.Serialize(state));
            }
            catch (IOException) { }
        }

        private void LoadWidgetState(int defaultX, int defaultY)
        {
            var configPath = GetConfigFilePath();
            if (File.Exists(configPath))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<WidgetState>(MotionDesk.Services.AtomicFile.ReadAllText(configPath));
                    if (state != null)
                    {
                        if (state.Width > 120 && state.Height > 80) Size = new Size(Math.Clamp(state.Width, 160, 1200), Math.Clamp(state.Height, 100, 900));
                        Location = ClampToAvailableScreen(new Point(state.X, state.Y));
                        IsLocked = state.IsLocked;
                        Variant = string.IsNullOrWhiteSpace(state.Variant) ? "Digital" : state.Variant;
                        Latitude = state.Latitude;
                        Longitude = state.Longitude;
                        LocationName = state.LocationName;
                        Clock24Hour = state.Clock24Hour;
                        ClockShowSeconds = state.ClockShowSeconds;
                        AudioStyle = string.IsNullOrWhiteSpace(state.AudioStyle) ? "WMP" : state.AudioStyle;
                        NetworkStyle = string.IsNullOrWhiteSpace(state.NetworkStyle) ? "Sparkline" : state.NetworkStyle;
                        _backgroundOpacity = state.Opacity is > 0.2 and <= 1.0 ? state.Opacity : 1.0;
                        AnalogTheme = string.IsNullOrWhiteSpace(state.AnalogTheme) ? "Classic" : state.AnalogTheme;
                        DigitalFontFamily = string.IsNullOrWhiteSpace(state.DigitalFontFamily) ? "Segoe UI" : state.DigitalFontFamily;
                        DigitalColorArgb = state.DigitalColorArgb;
                        return;
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }

            Location = ClampToAvailableScreen(new Point(defaultX, defaultY));
        }

        private Point ClampToAvailableScreen(Point location)
        {
            var screen = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains(location)) ?? Screen.PrimaryScreen;
            if (screen == null) return location;
            var area = screen.WorkingArea;
            return new Point(Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }

        private string GetConfigFilePath() =>
            Path.Combine(AppPaths.AppDataFolder, "widgets", $"{WidgetId}_config.json");

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
        {
            try
            {
                if (IsDisposed) return;
                _webView = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
                Controls.Add(_webView);
                await _webView.EnsureCoreWebView2Async(await WebView2Support.CreateEnvironmentAsync());
                if (IsDisposed || _webView.CoreWebView2 == null) return;
                WebView2Support.Harden(_webView.CoreWebView2);
                _webView.CoreWebView2.AddHostObjectToScript("motionDesk", new WidgetBridge(this));
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widgets", WidgetId, "index.html");
                if (File.Exists(htmlPath)) _webView.CoreWebView2.Navigate(htmlPath);
            }
            catch (Exception ex)
            {
                if (!IsDisposed) Text = $"Opsis - {WidgetId}";
                System.Diagnostics.Debug.WriteLine($"WebView2 widget initialization failed: {ex}");
            }
        }

        private void ShowWidgetMenu(Control owner)
        {
            // ΟΧΙ "using": το ContextMenuStrip.Show() δεν μπλοκάρει, οπότε ένα "using" εδώ θα το
            // Dispose-άρει αμέσως μετά το Show() -> flash-and-vanish πριν προλάβει κλικ ο χρήστης.
            var menu = new ContextMenuStrip();

            if (string.Equals(WidgetId, "clock", StringComparison.OrdinalIgnoreCase))
            {
                var digital = new ToolStripMenuItem("Digital", null, (_, _) => SetClockVariant("Digital")) { Checked = Variant == "Digital" };
                var analog = new ToolStripMenuItem("Analog", null, (_, _) => SetClockVariant("Analog")) { Checked = Variant == "Analog" };
                menu.Items.Add(digital);
                menu.Items.Add(analog);
                menu.Items.Add(new ToolStripSeparator());
                var h24 = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.Hour24"), null, (_, _) => SetClockOption(o => Clock24Hour = true)) { Checked = Clock24Hour };
                var h12 = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.Hour12"), null, (_, _) => SetClockOption(o => Clock24Hour = false)) { Checked = !Clock24Hour };
                var secsOn = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.SecondsOn"), null, (_, _) => SetClockOption(o => ClockShowSeconds = true)) { Checked = ClockShowSeconds };
                var secsOff = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.SecondsOff"), null, (_, _) => SetClockOption(o => ClockShowSeconds = false)) { Checked = !ClockShowSeconds };
                menu.Items.Add(h24);
                menu.Items.Add(h12);
                menu.Items.Add(secsOn);
                menu.Items.Add(secsOff);
                menu.Items.Add(new ToolStripSeparator());

                // Ζητήθηκε ρητά "analog clock διαφορετικά θέματα, digital clock με διαφορετικές
                // γραμματοσειρές & χρώματα".
                var analogThemeMenu = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.AnalogTheme"));
                foreach (var themeName in new[] { "Classic", "Neon", "Minimal", "Mono", "Retro" })
                {
                    var item = new ToolStripMenuItem(themeName) { Checked = AnalogTheme == themeName };
                    item.Click += (_, _) => SetClockOption(o => AnalogTheme = themeName);
                    analogThemeMenu.DropDownItems.Add(item);
                }
                menu.Items.Add(analogThemeMenu);

                var fontMenu = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.DigitalFont"));
                foreach (var fam in new[] { "Segoe UI", "Consolas", "Arial", "Times New Roman", "Comic Sans MS", "Georgia", "Verdana" })
                {
                    var item = new ToolStripMenuItem(fam) { Checked = DigitalFontFamily == fam };
                    item.Click += (_, _) => SetClockOption(o => DigitalFontFamily = fam);
                    fontMenu.DropDownItems.Add(item);
                }
                menu.Items.Add(fontMenu);

                var colorMenu = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.DigitalColor"));
                var colorOptions = new (string Name, int Argb)[]
                {
                    (LocalizationManager.T("WidgetMenu.ColorDefault"), -1),
                    (LocalizationManager.T("WidgetMenu.ColorWhite"), Color.White.ToArgb()),
                    (LocalizationManager.T("WidgetMenu.ColorCyan"), Color.FromArgb(0, 210, 255).ToArgb()),
                    (LocalizationManager.T("WidgetMenu.ColorGreen"), Color.FromArgb(60, 220, 140).ToArgb()),
                    (LocalizationManager.T("WidgetMenu.ColorOrange"), Color.FromArgb(255, 150, 60).ToArgb()),
                    (LocalizationManager.T("WidgetMenu.ColorPink"), Color.FromArgb(255, 90, 180).ToArgb()),
                    (LocalizationManager.T("WidgetMenu.ColorRed"), Color.FromArgb(235, 70, 60).ToArgb()),
                    (LocalizationManager.T("WidgetMenu.ColorGold"), Color.FromArgb(230, 180, 60).ToArgb()),
                };
                foreach (var (name, argb) in colorOptions)
                {
                    var item = new ToolStripMenuItem(name) { Checked = DigitalColorArgb == argb };
                    item.Click += (_, _) => SetClockOption(o => DigitalColorArgb = argb);
                    colorMenu.DropDownItems.Add(item);
                }
                menu.Items.Add(colorMenu);
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (string.Equals(WidgetId, "audio", StringComparison.OrdinalIgnoreCase))
            {
                var wmp = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.AudioWmp"), null, (_, _) => SetAudioStyle("WMP")) { Checked = AudioStyle == "WMP" };
                var winamp = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.AudioWinamp"), null, (_, _) => SetAudioStyle("Winamp")) { Checked = AudioStyle == "Winamp" };
                menu.Items.Add(wmp);
                menu.Items.Add(winamp);
                menu.Items.Add(new ToolStripSeparator());

                // "Audio Enhancement" presets (εμπνευσμένο από τη λογική του FXSound — βλ. σχόλιο
                // στο AudioSpectrumService.Presets) — κοινό preset με τη σελίδα Audio Enhancement.
                var currentPreset = AppSettings.Load().AudioEnhancementPreset;
                var presetMenu = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.EnhancePreset"));
                foreach (var (name, _) in AudioSpectrumService.Presets)
                {
                    var item = new ToolStripMenuItem(name) { Checked = currentPreset == name };
                    item.Click += (_, _) =>
                    {
                        var a = AppSettings.Load(); a.AudioEnhancementPreset = name; a.Save();
                        _audioPresetChangedHandler?.Invoke();
                        if (EqualizerApoService.IsInstalled) EqualizerApoService.ApplyPreset(name);
                    };
                    presetMenu.DropDownItems.Add(item);
                }
                menu.Items.Add(presetMenu);
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (string.Equals(WidgetId, "weather", StringComparison.OrdinalIgnoreCase))
            {
                menu.Items.Add(LocalizationManager.T("WidgetMenu.SetLocation"), null, (_, _) => PromptWeatherLocation());
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (string.Equals(WidgetId, "network", StringComparison.OrdinalIgnoreCase))
            {
                var sparklineItem = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.NetSparkline"), null, (_, _) => SetNetworkStyle("Sparkline")) { Checked = NetworkStyle == "Sparkline" };
                var barsItem = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.NetBars"), null, (_, _) => SetNetworkStyle("Bars")) { Checked = NetworkStyle == "Bars" };
                menu.Items.Add(sparklineItem);
                menu.Items.Add(barsItem);
                menu.Items.Add(new ToolStripSeparator());
            }

            // Ρύθμιση διαφάνειας — ζητήθηκε ρητά "στα widgets & containers προσθεσε ρυθμιση για
            // opacity οπως στην εφαρμογη", ίδιο σύνολο ποσοστών με το ήδη υπάρχον μενού των
            // DeskContainers για συνέπεια.
            var opacityMenu = new ToolStripMenuItem(LocalizationManager.T("WidgetMenu.Opacity"));
            foreach (int pct in new[] { 100, 85, 70, 55, 40, 25 })
            {
                var opacityItem = new ToolStripMenuItem($"{pct}%") { Checked = Math.Abs(_backgroundOpacity * 100 - pct) < 1 };
                opacityItem.Click += (_, _) => { _backgroundOpacity = pct / 100.0; ApplyGlassBlur(); SaveWidgetState(); };
                opacityMenu.DropDownItems.Add(opacityItem);
            }
            menu.Items.Add(opacityMenu);

            var lockItem = new ToolStripMenuItem(IsLocked ? LocalizationManager.T("WidgetMenu.Unlock") : LocalizationManager.T("WidgetMenu.Lock"));
            lockItem.Click += (_, _) => { IsLocked = !IsLocked; SaveWidgetState(); };
            menu.Items.Add(lockItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(LocalizationManager.T("WidgetMenu.Close"), null, (_, _) => Close());
            menu.Show(owner, owner.PointToClient(Cursor.Position));
        }

        private Action? _variantChangedHandler;
        public void OnVariantChanged(Action handler) => _variantChangedHandler = handler;

        private void SetClockVariant(string variant)
        {
            Variant = variant;
            SaveWidgetState();
            _variantChangedHandler?.Invoke();
        }

        private void SetClockOption(Action<WidgetWindow> apply)
        {
            apply(this);
            SaveWidgetState();
        }

        private Action? _audioStyleChangedHandler;
        public void OnAudioStyleChanged(Action handler) => _audioStyleChangedHandler = handler;

        private Action? _audioPresetChangedHandler;
        public void OnAudioPresetChanged(Action handler) => _audioPresetChangedHandler = handler;

        private void SetAudioStyle(string style)
        {
            AudioStyle = style;
            SaveWidgetState();
            _audioStyleChangedHandler?.Invoke();
        }

        private Action? _networkStyleChangedHandler;
        public void OnNetworkStyleChanged(Action handler) => _networkStyleChangedHandler = handler;

        private void SetNetworkStyle(string style)
        {
            NetworkStyle = style;
            SaveWidgetState();
            _networkStyleChangedHandler?.Invoke();
        }

        private void PromptWeatherLocation()
        {
            using var dlg = new Form
            {
                Text = LocalizationManager.T("Weather.SetLocationTitle"),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(320, 120),
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = UiTheme.Surface
            };
            var label = new Label { Text = LocalizationManager.T("Weather.CityName"), Location = new Point(14, 14), AutoSize = true, ForeColor = UiTheme.TextPrimary };
            var textBox = new TextBox { Location = new Point(14, 36), Width = 290, Text = LocationName ?? "" };
            var okBtn = new Button { Text = LocalizationManager.T("Common.OK"), Location = new Point(140, 74), DialogResult = DialogResult.OK };
            var cancelBtn = new Button { Text = LocalizationManager.T("Common.Cancel"), Location = new Point(228, 74), DialogResult = DialogResult.Cancel };
            dlg.Controls.Add(label); dlg.Controls.Add(textBox); dlg.Controls.Add(okBtn); dlg.Controls.Add(cancelBtn);
            dlg.AcceptButton = okBtn; dlg.CancelButton = cancelBtn;
            dlg.HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(dlg.Handle);

            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                _ = ApplyGeocodedLocationAsync(textBox.Text.Trim());
            }
        }

        private Func<string, System.Threading.Tasks.Task>? _geocodeHandler;
        public void OnGeocodeRequested(Func<string, System.Threading.Tasks.Task> handler) => _geocodeHandler = handler;

        private async System.Threading.Tasks.Task ApplyGeocodedLocationAsync(string cityName)
        {
            if (_geocodeHandler != null) await _geocodeHandler(cityName);
        }

        // Κοινό, θεματισμένο "κέλυφος" widget: στρογγυλεμένες γωνίες + χρώματα από το UiTheme
        // αντί για μόνιμο σχεδόν-μαύρο φόντο (ζητήθηκε ρητά — "τα widgets είναι μόνο μαύρα").
        // Ξανασχεδιάζεται αυτόματα σε κάθε αλλαγή θέματος/σκουρότητας μέσω ThemeManager.Changed/Repainted.
        private Panel CreateNativePanel(string title, out GlassLabel values)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            // ΡΙΖΙΚΗ ΑΙΤΙΑ του "Πλήρης διαφάνεια" που δεν δούλευε: εδώ το TransparencyKey μηδενιζόταν
            // πάντα (default) ακυρώνοντας το Color.Black του constructor, οπότε κανένα μαύρο pixel
            // δεν γινόταν ποτέ διαφανές. Τώρα διατηρείται μόνο στη λειτουργία Πλήρης διαφάνεια.
            bool fullyTransparent = UiTheme.IsFullyTransparent;
            BackColor = fullyTransparent ? Color.Black : UiTheme.Background;
            TransparencyKey = UiTheme.IsColorKey ? Color.Black : default;

            var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.SurfaceGlass, Padding = new Padding(14) };
            _themedPanel = panel;
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };

            var closeLabel = new GlassLabel { Text = "✕", AutoSize = false, Size = new Size(22, 22), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            closeLabel.MouseEnter += (_, _) => closeLabel.ForeColor = Color.FromArgb(231, 76, 60);
            closeLabel.MouseLeave += (_, _) => closeLabel.ForeColor = UiTheme.TextSecondary;
            closeLabel.Click += (_, _) => Close();

            var lockLabel = new LockGlyph { Locked = IsLocked, Size = new Size(22, 22), Dock = DockStyle.Right, ForeColor = UiTheme.TextSecondary };
            lockLabel.Click += (_, _) => { IsLocked = !IsLocked; lockLabel.Locked = IsLocked; SaveWidgetState(); };

            var menuLabel = new GlassLabel { Text = "☰", AutoSize = false, Size = new Size(22, 22), Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            menuLabel.MouseEnter += (_, _) => menuLabel.ForeColor = UiTheme.TextPrimary;
            menuLabel.MouseLeave += (_, _) => menuLabel.ForeColor = UiTheme.TextSecondary;
            menuLabel.Click += (_, _) => ShowWidgetMenu(menuLabel);

            var titleLabel = new GlassLabel { Dock = DockStyle.Fill, Text = title, AutoEllipsis = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, Cursor = Cursors.SizeAll, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
            _titleLabelRef = titleLabel;
            titleLabel.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && !IsLocked) BeginNativeDrag();
                else if (e.Button == MouseButtons.Right) ShowWidgetMenu(titleLabel);
            };

            titleBar.Controls.Add(titleLabel);
            titleBar.Controls.Add(lockLabel);
            titleBar.Controls.Add(closeLabel);
            titleBar.Controls.Add(menuLabel);

            panel.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && !IsLocked) BeginNativeDrag(); };
            values = new GlassLabel { Dock = DockStyle.Top, Height = 52, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10.5f), ForeColor = UiTheme.TextPrimary, BackColor = Color.Transparent };
            var valuesRef = values;
            panel.Controls.Add(values); panel.Controls.Add(titleBar);
            Controls.Add(panel);

            // Λαβή αλλαγής μεγέθους, κάτω-δεξιά γωνία — τοποθετείται απευθείας στη Form (όχι μέσα
            // στο panel Dock=Fill) ώστε το Anchor Bottom|Right να την κρατά πάντα στη σωστή γωνία
            // ανεξάρτητα από το layout του περιεχομένου. BringToFront ώστε να μένει πάνω από το panel.
            var resizeGrip = new GlassLabel
            {
                Text = "◢",
                AutoSize = false,
                Size = new Size(14, 14),
                ForeColor = UiTheme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8),
                TextAlign = ContentAlignment.BottomRight,
                Cursor = Cursors.SizeNWSE,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(Width - 16, Height - 16)
            };
            resizeGrip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left && !IsLocked) BeginNativeResize(); };
            Controls.Add(resizeGrip);
            resizeGrip.BringToFront();

            _themeHandler = () =>
            {
                if (IsDisposed) return;
                panel.BackColor = UiTheme.SurfaceGlass;
                BackColor = UiTheme.IsFullyTransparent ? Color.Black : UiTheme.Background;
                TransparencyKey = UiTheme.IsColorKey ? Color.Black : default;
                closeLabel.ForeColor = UiTheme.TextSecondary;
                lockLabel.ForeColor = UiTheme.TextSecondary;
                menuLabel.ForeColor = UiTheme.TextSecondary;
                titleLabel.ForeColor = UiTheme.TextPrimary;
                valuesRef.ForeColor = UiTheme.TextPrimary;
                resizeGrip.ForeColor = UiTheme.TextMuted;
                ApplyRoundedShell();
                Invalidate(true);
            };
            ThemeManager.Changed += _themeHandler;
            ThemeManager.Repainted += _themeHandler;

            ApplyRoundedShell();
            Resize += (_, _) => ApplyRoundedShell();
            return panel;
        }

        private void ApplyRoundedShell()
        {
            if (Width > 0 && Height > 0) UiTheme.ApplyRoundedRegion(this, 12);
            ApplyGlassBlur();
            GlassBlur.DisableNativeBorderAccent(Handle);
        }

        // Το Form.TransparencyKey (color-key σε Color.Black) αποδείχτηκε αναξιόπιστο εδώ - ακόμα
        // και με το blur-behind εντελώς απενεργοποιημένο, το περιεχόμενο έμενε συμπαγές μαύρο αντί
        // για πραγματική τρύπα διαφάνειας. Το ήδη αποδεδειγμένα σωστό DWM blur-behind (ίδιο με το
        // DeskStrip) παραμένει ενεργό και στην "Πλήρης διαφάνεια", απλά σε πολύ χαμηλή ένταση, ώστε
        // να μην υπάρχει συμπαγές μαύρο/λευκό πλαίσιο αλλά ένα πολύ ελαφρύ, σχεδόν άχρωμο θόλωμα.
        private void ApplyGlassBlur()
        {
            if (UiTheme.IsFullyTransparent)
            {
                // colour-key = pure see-through; with a tint the widget is a translucent coloured surface like the DeskContainers
                if (UiTheme.IsColorKey) GlassBlur.Disable(Handle); else GlassBlur.ApplyTint(Handle, UiTheme.TintColor, (int)Math.Round(UiTheme.TintAlpha * _backgroundOpacity));
                return;
            }
            GlassBlur.Apply(Handle, UiTheme.AccentCyan, _backgroundOpacity);
        }

        private void InitializeNativeClock()
        {
            var panel = CreateNativePanel("Clock", out var values);
            // Η κοινή "values" ετικέτα του CreateNativePanel είναι Dock=Top με σταθερό ύψος — δεν
            // κεντράρει ούτε αφήνει χώρο για ξεχωριστή γραμμή ημερομηνίας. Το Clock φτιάχνει το
            // δικό του layout από κάτω (ζητήθηκε ρητά: ώρα στο κέντρο του widget + ημερομηνία, και
            // στα δύο variants, με επιλογές εμφάνισης 12/24h και δευτερόλεπτα).
            values.Visible = false;

            var digitalHost = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Color.Transparent };
            digitalHost.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
            digitalHost.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
            var timeLabel = new GlassLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = UiTheme.TextPrimary, BackColor = Color.Transparent };
            var dateLabelDigital = new GlassLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10), ForeColor = UiTheme.TextSecondary, BackColor = Color.Transparent };
            digitalHost.Controls.Add(timeLabel, 0, 0);
            digitalHost.Controls.Add(dateLabelDigital, 0, 1);

            var analogHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };
            var analog = new AnalogClockControl { Dock = DockStyle.Fill };
            var dateLabelAnalog = new GlassLabel { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9), ForeColor = UiTheme.TextSecondary, BackColor = Color.Transparent };
            analogHost.Controls.Add(analog);
            analogHost.Controls.Add(dateLabelAnalog);

            panel.Controls.Add(digitalHost);
            panel.Controls.Add(analogHost);
            digitalHost.BringToFront();
            analogHost.BringToFront();

            string lastFontFamily = "";
            // Ζητήθηκε ρητά "digital clock με διαφορετικές γραμματοσειρές & χρώματα" — η επιλογή
            // χρώματος (DigitalColorArgb == -1 σημαίνει "ακολούθησε το θέμα") πρέπει να επιζεί από
            // κάθε αλλαγή θέματος, όχι να αντικαθίσταται πάντα από UiTheme.TextPrimary όπως πριν.
            void ApplyDigitalStyle()
            {
                if (IsDisposed) return;
                if (lastFontFamily != DigitalFontFamily)
                {
                    lastFontFamily = DigitalFontFamily;
                    // Πρώτα νέο font, μετά dispose του παλιού ΜΟΝΟ αν όντως άλλαξε το αντικείμενο (το Font.Equals συγκρίνει ιδιότητες:
                    // ίδιο όνομα/μέγεθος => το control κρατούσε το ήδη disposed παλιό και ζωγράφιζε κόκκινο X).
                    var oldFont = timeLabel.Font;
                    var newFont = new Font(DigitalFontFamily, 22, FontStyle.Bold);
                    timeLabel.Font = newFont;
                    if (!ReferenceEquals(timeLabel.Font, oldFont)) oldFont.Dispose(); else newFont.Dispose();
                }
                timeLabel.ForeColor = DigitalColorArgb == -1 ? UiTheme.TextPrimary : Color.FromArgb(DigitalColorArgb);
                dateLabelDigital.ForeColor = UiTheme.TextSecondary;
                dateLabelAnalog.ForeColor = UiTheme.TextSecondary;
                analog.Theme = AnalogTheme;
            }
            ThemeManager.Changed += ApplyDigitalStyle;
            ThemeManager.Repainted += ApplyDigitalStyle;
            FormClosed += (_, _) => { ThemeManager.Changed -= ApplyDigitalStyle; ThemeManager.Repainted -= ApplyDigitalStyle; };

            void ApplyVariant()
            {
                bool isAnalog = Variant == "Analog";
                analogHost.Visible = isAnalog;
                digitalHost.Visible = !isAnalog;
            }
            void Update()
            {
                var now = DateTime.Now;
                string timeFmt = Clock24Hour
                    ? (ClockShowSeconds ? "HH:mm:ss" : "HH:mm")
                    : (ClockShowSeconds ? "h:mm:ss tt" : "h:mm tt");
                timeLabel.Text = now.ToString(timeFmt);
                string dateText = now.ToString("dddd, dd MMMM");
                dateLabelDigital.Text = dateText;
                dateLabelAnalog.Text = dateText;
                ApplyDigitalStyle();
                analog.Invalidate();
            }
            OnVariantChanged(ApplyVariant);
            ApplyVariant();
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _nativeMonitorTimer.Tick += (_, _) => { if (!AppActivity.IsFullscreenAppActive) Update(); };
            _nativeMonitorTimer.Start();
        }

        private void InitializeNativeNetwork()
        {
            var panel = CreateNativePanel("Network", out var values);
            values.Height = 40;
            var sparkline = new NetworkSparklineControl { Dock = DockStyle.Fill };
            var bars = new NetworkBarsControl { Dock = DockStyle.Fill, Visible = false };
            panel.Controls.Add(sparkline);
            panel.Controls.Add(bars);
            sparkline.BringToFront();
            bars.BringToFront();

            void ApplyStyle()
            {
                bool useBars = NetworkStyle == "Bars";
                bars.Visible = useBars;
                sparkline.Visible = !useBars;
            }
            OnNetworkStyleChanged(ApplyStyle);
            ApplyStyle();

            void Update()
            {
                var m = AdvancedSystemMonitorService.Instance.GetSnapshot();
                values.Text = $"↓ {m.NetworkDownKbps:0.0} KB/s   ↑ {m.NetworkUpKbps:0.0} KB/s   ·   " + string.Format(LocalizationManager.T("WidgetText.ProcessesFormat"), m.ProcessCount);
                sparkline.Push(m.NetworkDownKbps, m.NetworkUpKbps);
                bars.Push(m.NetworkDownKbps, m.NetworkUpKbps);
            }
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _nativeMonitorTimer.Tick += (_, _) => { if (!AppActivity.IsFullscreenAppActive) Update(); };
            _nativeMonitorTimer.Start();
        }

        private void InitializeNativeAudio()
        {
            var panel = CreateNativePanel("Audio Enhancement", out var values);
            values.TextAlign = ContentAlignment.MiddleCenter;
            values.Height = 24;
            values.Font = new Font("Segoe UI", 9);

            var spectrum = new AudioSpectrumService(20);
            spectrum.ApplyPreset(AppSettings.Load().AudioEnhancementPreset);
            var equalizer = new EqualizerControl { Dock = DockStyle.Fill, Style = AudioStyle };
            panel.Controls.Add(equalizer);
            equalizer.BringToFront();
            OnAudioStyleChanged(() => equalizer.Style = AudioStyle);
            OnAudioPresetChanged(() => spectrum.ApplyPreset(AppSettings.Load().AudioEnhancementPreset));

            void Update()
            {
                var bands = spectrum.GetBands();
                equalizer.PushBands(bands);
                values.Text = spectrum.IsAvailable ? "" : LocalizationManager.T("WidgetText.NoAudioDevice");
            }
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _nativeMonitorTimer.Tick += (_, _) => { if (!AppActivity.IsFullscreenAppActive) Update(); };
            _nativeMonitorTimer.Start();
            FormClosed += (_, _) => spectrum.Dispose();
        }

        // Νέο widget "Quick Settings" — SeelenUI-inspired (το δικό τους αντίστοιχο popup συνδυάζει
        // ήχο/φωτεινότητα/δίκτυο σε ένα πάνελ). Ήχος μέσω NAudio (ήδη εξάρτηση), φωτεινότητα μέσω
        // WMI (μόνο αν η οθόνη το υποστηρίζει — συνήθως laptop panel, κρύβεται αλλιώς), σχέδιο
        // ενέργειας μέσω powercfg. Καμία λειτουργία δεν χρειάζεται elevation.
        private void InitializeNativeQuickSettings()
        {
            var panel = CreateNativePanel(LocalizationManager.T("Widgets.QuickSettings"), out var values);
            values.Visible = false;

            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent, Padding = new Padding(10, 6, 10, 6) };
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(stack);
            stack.BringToFront();

            // ---- Ήχος: κουμπί σίγασης + slider ----
            var volumeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Top };
            var muteBtn = new GlassLabel { Text = "🔊", Width = 28, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 12), Cursor = Cursors.Hand };
            var volumeSlider = new FlatSlider { Width = 150, Height = 24, Minimum = 0, Maximum = 100, Margin = new Padding(6, 0, 6, 0) };
            var volumeLabel = new GlassLabel { Width = 36, Height = 24, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody };
            volumeRow.Controls.Add(muteBtn);
            volumeRow.Controls.Add(volumeSlider);
            volumeRow.Controls.Add(volumeLabel);
            stack.Controls.Add(volumeRow, 0, 0);

            void RefreshVolumeUi()
            {
                bool muted = SystemControlService.GetMute();
                muteBtn.Text = muted ? "🔇" : "🔊";
                int pct = (int)Math.Round(SystemControlService.GetVolume() * 100);
                volumeSlider.Value = pct;
                volumeLabel.Text = muted ? LocalizationManager.T("WidgetText.Muted") : $"{pct}%";
            }
            muteBtn.Click += (_, _) => { SystemControlService.SetMute(!SystemControlService.GetMute()); RefreshVolumeUi(); };
            volumeSlider.ValueChanged += (_, _) => { SystemControlService.SetVolume(volumeSlider.Value / 100f); if (volumeSlider.Value > 0 && SystemControlService.GetMute()) SystemControlService.SetMute(false); RefreshVolumeUi(); };

            // ---- Φωτεινότητα: μόνο αν υποστηρίζεται από την οθόνη ----
            bool brightnessSupported = SystemControlService.IsBrightnessSupported();
            var brightnessRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Top, Visible = brightnessSupported };
            var brightnessIcon = new GlassLabel { Text = "☀", Width = 28, Height = 24, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 12) };
            var brightnessSlider = new FlatSlider { Width = 150, Height = 24, Minimum = 0, Maximum = 100, Margin = new Padding(6, 0, 6, 0) };
            var brightnessLabel = new GlassLabel { Width = 36, Height = 24, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody };
            brightnessRow.Controls.Add(brightnessIcon);
            brightnessRow.Controls.Add(brightnessSlider);
            brightnessRow.Controls.Add(brightnessLabel);
            stack.Controls.Add(brightnessRow, 0, 1);
            if (brightnessSupported)
            {
                brightnessSlider.Value = SystemControlService.GetBrightness();
                brightnessLabel.Text = $"{brightnessSlider.Value}%";
                brightnessSlider.ValueChanged += (_, _) => { SystemControlService.SetBrightness(brightnessSlider.Value); brightnessLabel.Text = $"{brightnessSlider.Value}%"; };
            }

            // ---- Σχέδιο ενέργειας: ένα κουμπί ανά διαθέσιμο σχέδιο, το ενεργό τονισμένο ----
            var powerPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 0) };
            stack.Controls.Add(powerPanel, 0, 2);
            void RefreshPowerPlans()
            {
                powerPanel.Controls.Clear();
                foreach (var plan in SystemControlService.GetPowerPlans())
                {
                    var btn = new GlassLabel
                    {
                        Text = plan.Name, AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 6, 6),
                        Cursor = Cursors.Hand, Font = UiTheme.FontBody,
                        ForeColor = plan.IsActive ? Color.White : UiTheme.TextSecondary,
                        BackColor = plan.IsActive ? UiTheme.AccentCyan : UiTheme.Surface,
                    };
                    var guid = plan.Guid;
                    btn.Click += (_, _) => { SystemControlService.SetActivePowerPlan(guid); RefreshPowerPlans(); };
                    powerPanel.Controls.Add(btn);
                }
            }

            RefreshVolumeUi();
            RefreshPowerPlans();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _nativeMonitorTimer.Tick += (_, _) => { if (!AppActivity.IsFullscreenAppActive) { RefreshVolumeUi(); RefreshPowerPlans(); } };
            _nativeMonitorTimer.Start();
        }

        // Νέο widget: χρήση/χωρητικότητα δίσκων συστήματος — ζητήθηκε ρητά.
        // Ζητήθηκε ρητά "στους δίσκους να είναι για κάθε ένα κύκλος η μπάρα με πληροφορίες του
        // δίσκου στο μέσο του κύκλου, και αντί για scroll να υπάρχουν κουμπιά δεξιά/αριστερά για
        // επιλογή του κάθε δίσκου" — αντικαταστάθηκε η scrollable λίστα με ΕΝΑ κυκλικό ring ανά
        // φορά + ◀/▶ σελιδοποίηση, αντί για FlowLayoutPanel με scroll.
        private void InitializeNativeDisk()
        {
            var panel = CreateNativePanel(LocalizationManager.T("Widgets.Disk"), out var values);
            values.Visible = false;

            // ΔΙΟΡΘΩΣΗ πραγματικού bug ("ο κύκλος κόβεται από τα κουμπιά"): το πραγματικό root
            // cause ΔΕΝ ήταν η σειρά στο Controls.Add, αλλά ότι το Dock=Fill container (diskArea)
            // ΔΕΝ είχε γίνει BringToFront — σε αυτό το codebase το Dock engine αποκλείει τον χώρο
            // των Top/Bottom/Left/Right αδερφών με βάση το Z-order, όχι τη σειρά προσθήκης (βλ. πώς
            // ΚΑΘΕ άλλο native widget με Dock=Fill περιεχόμενο κάνει explicit .BringToFront() μετά
            // την προσθήκη — icon/sparkline/rows/digitalHost παρακάτω σε αυτό το αρχείο). Χωρίς αυτό,
            // το diskArea υπολόγιζε το ClientSize του σαν να μην υπήρχε καθόλου το titleBar πάνω
            // του, οπότε το ring "πίστευε" ότι έχει 30px παραπάνω ύψος από ό,τι πραγματικά
            // φαινόταν — αυτά τα "χαμένα" 30px μετατόπιζαν το κάτω άκρο του κύκλου μέσα στα
            // κουμπιά ◀/▶. Επιπλέον, υπολογίζουμε τα Bounds του ring με το χέρι (αντί για δικό του
            // Dock=Fill) ώστε να μην εξαρτόμαστε ΚΑΘΟΛΟΥ από Dock engine ακροβατικά για τη
            // διάμετρό του.
            var diskArea = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var ring = new DiskRingControl();
            var navRow = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.Transparent };
            var prevBtn = new GlassLabel { Text = "◀", Dock = DockStyle.Left, Width = 36, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 12), Cursor = Cursors.Hand };
            var nextBtn = new GlassLabel { Text = "▶", Dock = DockStyle.Right, Width = 36, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 12), Cursor = Cursors.Hand };
            var pageLabel = new GlassLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody };
            navRow.Controls.Add(pageLabel);
            navRow.Controls.Add(prevBtn);
            navRow.Controls.Add(nextBtn);
            diskArea.Controls.Add(ring);
            diskArea.Controls.Add(navRow);
            void LayoutDiskArea()
            {
                ring.Bounds = new Rectangle(0, 0, diskArea.ClientSize.Width, Math.Max(0, diskArea.ClientSize.Height - navRow.Height));
            }
            diskArea.Resize += (_, _) => LayoutDiskArea();
            panel.Controls.Add(diskArea);
            diskArea.BringToFront();
            LayoutDiskArea();

            var drives = new List<DriveInfo>();
            int index = Math.Max(0, AppSettings.Load().DiskWidgetIndex);

            void Render()
            {
                if (drives.Count == 0) { ring.SetData("—", LocalizationManager.T("WidgetText.NoDrives"), 0, 0, 0); pageLabel.Text = ""; return; }
                index = ((index % drives.Count) + drives.Count) % drives.Count;
                var drive = drives[index];
                try
                {
                    double totalGb = drive.TotalSize / 1073741824.0;
                    double freeGb = drive.AvailableFreeSpace / 1073741824.0;
                    double usedGb = totalGb - freeGb;
                    double pct = totalGb > 0 ? usedGb / totalGb : 0;
                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? LocalizationManager.T("WidgetText.LocalDisk") : drive.VolumeLabel;
                    ring.SetData(drive.Name.TrimEnd('\\'), label, usedGb, totalGb, pct);
                }
                catch (IOException) { }
                pageLabel.Text = $"{index + 1} / {drives.Count}";
            }

            void Rescan()
            {
                drives.Clear();
                drives.AddRange(DriveInfo.GetDrives().Where(d => d.IsReady));
                Render();
            }

            void RememberDisk() { try { var a = AppSettings.Load(); a.DiskWidgetIndex = index; a.Save(); } catch { } }
            prevBtn.Click += (_, _) => { index--; Render(); RememberDisk(); };
            nextBtn.Click += (_, _) => { index++; Render(); RememberDisk(); };
            prevBtn.MouseEnter += (_, _) => prevBtn.ForeColor = UiTheme.AccentCyan;
            prevBtn.MouseLeave += (_, _) => prevBtn.ForeColor = UiTheme.TextSecondary;
            nextBtn.MouseEnter += (_, _) => nextBtn.ForeColor = UiTheme.AccentCyan;
            nextBtn.MouseLeave += (_, _) => nextBtn.ForeColor = UiTheme.TextSecondary;

            Rescan();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            _nativeMonitorTimer.Tick += (_, _) => Rescan();
            _nativeMonitorTimer.Start();
        }

        private void InitializeNativeWeather()
        {
            var panel = CreateNativePanel(LocationName != null ? string.Format(LocalizationManager.T("WidgetText.WeatherTitleFormat"), LocationName) : LocalizationManager.T("WidgetText.WeatherDefaultTitle"), out var values);
            values.TextAlign = ContentAlignment.MiddleCenter;
            values.Font = new Font("Segoe UI", 9.5f);
            values.Height = 44;
            values.Text = LocalizationManager.T("WidgetText.Loading");

            var icon = new WeatherIconControl { Dock = DockStyle.Fill };
            panel.Controls.Add(icon);
            icon.BringToFront();

            var animTimer = new System.Windows.Forms.Timer { Interval = 60 };
            animTimer.Tick += (_, _) => { if (!AppActivity.IsFullscreenAppActive) icon.AdvancePhase(); };
            animTimer.Start();
            FormClosed += (_, _) => animTimer.Dispose();

            async void Update()
            {
                bool ok = true;
                try
                {
                    double lat = Latitude ?? 37.9838;
                    double lon = Longitude ?? 23.7275;
                    // GetNormalizedWeatherAsync δοκιμάζει πρώτα Open-Meteo και, αν αποτύχει, πέφτει
                    // αυτόματα σε δεύτερο δωρεάν πάροχο (wttr.in, χωρίς API key) αντί να δείχνει
                    // μόνιμα "Weather unavailable" όταν ο πρώτος πάροχος έχει πρόβλημα.
                    var result = await WeatherService.GetNormalizedWeatherAsync(lat, lon);
                    if (result != null)
                    {
                        icon.Condition = result.Condition;
                        int beaufort = WeatherService.ToBeaufort(result.WindKmh);
                        string humidityText = result.HumidityPercent.HasValue ? $"{result.HumidityPercent:0}%" : "—";
                        values.Text = string.Format(LocalizationManager.T("WidgetText.WeatherLineFormat"), result.TemperatureC, result.WindKmh, beaufort, humidityText);
                    }
                    else { values.Text = LocalizationManager.T("WidgetText.WeatherUnavailable"); ok = false; }
                }
                catch { values.Text = LocalizationManager.T("WidgetText.WeatherUnavailable"); ok = false; }
                // Αν ο καιρός απέτυχε (π.χ. καμία σύνδεση στην εκκίνηση), ξαναδοκιμάζουμε σε 1 λεπτό αντί να
                // περιμένουμε 15 λεπτά με "μη διαθέσιμο"· μετά την επιτυχία επιστρέφει στο κανονικό διάστημα.
                if (!IsDisposed && _nativeMonitorTimer != null) _nativeMonitorTimer.Interval = ok ? 900000 : 60000;
            }
            OnGeocodeRequested(async cityName =>
            {
                var result = await WeatherService.GeocodeAsync(cityName);
                if (result.HasValue)
                {
                    Latitude = result.Value.Lat;
                    Longitude = result.Value.Lon;
                    LocationName = result.Value.Name;
                    SaveWidgetState();
                    if (_titleLabelRef != null) _titleLabelRef.Text = string.Format(LocalizationManager.T("WidgetText.WeatherTitleFormat"), LocationName);
                    Update();
                }
            });
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 900000 };
            _nativeMonitorTimer.Tick += (_, _) => { if (!AppActivity.IsFullscreenAppActive) Update(); };
            _nativeMonitorTimer.Start();
        }

        private void InitializeNativeSystemMonitor()
        {
            // Πρώην ξεχωριστό, χειροποίητο title bar χωρίς Χ/κλείδωμα/μενού — το μόνο widget
            // που δεν περνούσε από το κοινό CreateNativePanel, γι' αυτό δεν είχε ορατό κουμπί
            // κλεισίματος σε αντίθεση με τα υπόλοιπα (Clock/Network/Audio/Weather).
            var panel = CreateNativePanel("System Monitor", out var values);
            // Ζητήθηκε ρητά "κανε το system monitor ομορφο με δυνατοτητα γραφηματων και μπαρων
            // μεσα απο τις τρεις γραμμες του widget και κανε μεγαλυτερα τα γραμματα και με χρωματα
            // για εμφαση" — αντικαταστάθηκε το απλό πολυγραμμικό Label με custom control που
            // σχεδιάζει μπάρα-μετρητή δίπλα σε κάθε γραμμή (CPU/RAM/Δίκτυο/GPU), bold/έγχρωμο
            // κείμενο τιμών.
            values.Visible = false;
            bool gpuReady = false;
            var rows = new SystemMonitorRowsControl { Dock = DockStyle.Fill, GpuIndex = Math.Max(0, AppSettings.Load().SystemMonitorGpuIndex) };
            // ‹ › on the GPU tile switch between the graphics adapters (dedicated + integrated); the choice is remembered
            rows.GpuStep += step =>
            {
                if (rows.GpuCount < 2) return;
                rows.GpuIndex = ((rows.GpuIndex + step) % rows.GpuCount + rows.GpuCount) % rows.GpuCount;
                try { var a = AppSettings.Load(); a.SystemMonitorGpuIndex = rows.GpuIndex; a.Save(); } catch { }
                if (gpuReady) { var s = GpuMonitorService.Instance.GetSnapshot(rows.GpuIndex); rows.SetGpu(s.Available ? s.LoadPercent : null, s.Available ? (s.TemperatureC.HasValue ? $"{s.TemperatureC:0}°C" : "—") : LocalizationManager.T("WidgetText.GpuNA"), s.Available ? s.Name : null); }
            };
            panel.Controls.Add(rows);
            rows.BringToFront();

            // ΔΙΟΡΘΩΣΗ σοβαρού bug (ζητήθηκε ρητά): η ΠΡΩΤΗ πρόσβαση στο GpuMonitorService.Instance
            // ενεργοποιεί το LibreHardwareMonitorLib's Computer.Open() — πραγματική σάρωση hardware
            // (NVAPI/ADL/Intel), αργή διεργασία που μπορεί να πάρει πάνω από ένα δευτερόλεπτο. Πριν
            // αυτή τη διόρθωση γινόταν συγχρονισμένα μέσα στο πρώτο Update() στο UI thread —
            // πάγωνε στιγμιαία ΟΛΗ την εφαρμογή (και το γιατί "επηρέαζε τα Windows" γενικότερα: ένα
            // μπλοκαρισμένο UI thread σταματά να αντλεί το message pump). Γίνεται τώρα "ζέσταμα"
            // σε background thread πριν καν χρειαστεί· μέχρι να ολοκληρωθεί, η γραμμή GPU απλά
            // δείχνει "μη διαθέσιμο" για ένα-δύο tick του timer αντί να μπλοκάρει οτιδήποτε.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                GpuMonitorService.Instance.GetSnapshot();
                gpuReady = true;
            });

            void Update()
            {
                var m = SystemMonitorService.Instance.GetSnapshot();
                var net = AdvancedSystemMonitorService.Instance.GetSnapshot();
                double ramUsedPct = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                rows.SetCpu(m.CpuPercent);
                rows.SetRam(ramUsedPct, string.Format(LocalizationManager.T("WidgetText.RamFreeFormat"), m.AvailableMemoryMb.ToString("0"), m.TotalMemoryMb.ToString("0"), ramUsedPct.ToString("0")));
                rows.SetNetwork(net.NetworkDownKbps, net.NetworkUpKbps);
                if (gpuReady)
                {
                    var gpu = GpuMonitorService.Instance.GetSnapshot(rows.GpuIndex);
                    rows.GpuCount = gpu.Count;
                    rows.SetGpu(gpu.Available ? gpu.LoadPercent : null,
                        gpu.Available ? $"{(gpu.TemperatureC.HasValue ? $"{gpu.TemperatureC:0}°C" : "—")}" : LocalizationManager.T("WidgetText.GpuNA"),
                        gpu.Available ? gpu.Name : null);
                }
                else rows.SetGpu(null, LocalizationManager.T("WidgetText.GpuLoading"));
            }
            Update();
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _nativeMonitorTimer.Tick += (_, _) => { if (!AppActivity.IsFullscreenAppActive) Update(); };
            _nativeMonitorTimer.Start();
        }

        // Νέο widget (SeelenUI-inspired richer widget set, ζητήθηκε ρητά): μήνας-σε-πλέγμα χωρίς
        // εξάρτηση σε εξωτερική πηγή ραντεβού - η επίσημη Windows Appointments API απαιτεί
        // δήλωση "appointments" capability σε packaged (MSIX) εφαρμογή, που το Opsis δεν είναι·
        // ίδιο μοτίβο με πολλά desktop calendar widgets (π.χ. Rainmeter): δείχνει μήνα/ημέρες/
        // σήμερα, όχι πραγματικά ραντεβού.
        private void InitializeNativeCalendar()
        {
            var panel = CreateNativePanel(LocalizationManager.T("Widgets.Calendar"), out var values);
            values.Visible = false;

            var area = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var navRow = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent };
            var prevBtn = new GlassLabel { Text = "◀", Dock = DockStyle.Left, Width = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            var nextBtn = new GlassLabel { Text = "▶", Dock = DockStyle.Right, Width = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 10), Cursor = Cursors.Hand };
            var monthLabel = new GlassLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            navRow.Controls.Add(monthLabel); navRow.Controls.Add(prevBtn); navRow.Controls.Add(nextBtn);
            var grid = new CalendarGridControl { Dock = DockStyle.Fill };
            area.Controls.Add(grid);
            area.Controls.Add(navRow);
            grid.BringToFront();
            panel.Controls.Add(area);
            area.BringToFront();

            var shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            void Render()
            {
                monthLabel.Text = shown.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
                grid.SetMonth(shown);
            }
            prevBtn.Click += (_, _) => { shown = shown.AddMonths(-1); Render(); };
            nextBtn.Click += (_, _) => { shown = shown.AddMonths(1); Render(); };
            prevBtn.MouseEnter += (_, _) => prevBtn.ForeColor = UiTheme.AccentCyan;
            prevBtn.MouseLeave += (_, _) => prevBtn.ForeColor = UiTheme.TextSecondary;
            nextBtn.MouseEnter += (_, _) => nextBtn.ForeColor = UiTheme.AccentCyan;
            nextBtn.MouseLeave += (_, _) => nextBtn.ForeColor = UiTheme.TextSecondary;
            Render();
        }

        // Νέο widget: τρέχον media session (Spotify/Edge/κ.λπ.) μέσω της επίσημης
        // GlobalSystemMediaTransportControlsSessionManager WinRT API - δουλεύει από unpackaged
        // Win32 εφαρμογή χωρίς ειδικό manifest capability (μόνο ανάγνωση + play/pause/next/prev,
        // όχι album art - κρατήθηκε έξω επίτηδες: η μετατροπή thumbnail stream -> Image πρόσθετε
        // ρίσκο χωρίς να μπορεί να δοκιμαστεί εκτενώς σε αυτό το πέρασμα).
        private async System.Threading.Tasks.Task InitializeNativeMediaAsync()
        {
            var panel = CreateNativePanel(LocalizationManager.T("Widgets.Media"), out var values);
            values.Visible = false;

            var area = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var statusLabel = new GlassLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Text = LocalizationManager.T("WidgetText.MediaNone") };
            var titleLabel = new GlassLabel { Dock = DockStyle.Top, AutoEllipsis = true, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = UiTheme.TextPrimary };
            var artistLabel = new GlassLabel { Dock = DockStyle.Top, AutoEllipsis = true, Height = 20, TextAlign = ContentAlignment.MiddleCenter, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary };
            var controlsRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Anchor = AnchorStyles.None };
            var prevBtn = new GlassLabel { Text = "⏮", Width = 40, Height = 32, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 13), Cursor = Cursors.Hand };
            var playPauseBtn = new GlassLabel { Text = "▶", Width = 40, Height = 32, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 13), Cursor = Cursors.Hand };
            var nextBtn = new GlassLabel { Text = "⏭", Width = 40, Height = 32, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = new Font("Segoe UI", 13), Cursor = Cursors.Hand };
            controlsRow.Controls.Add(prevBtn); controlsRow.Controls.Add(playPauseBtn); controlsRow.Controls.Add(nextBtn);
            area.Controls.Add(statusLabel);
            area.Controls.Add(controlsRow);
            area.Controls.Add(artistLabel);
            area.Controls.Add(titleLabel);
            panel.Controls.Add(area);
            area.BringToFront();

            WinRTMediaControl.GlobalSystemMediaTransportControlsSession? session = null;

            async System.Threading.Tasks.Task RefreshAsync()
            {
                try
                {
                    var mgr = await WinRTMediaControl.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    session = mgr.GetCurrentSession();
                    if (IsDisposed) return;
                    if (session == null)
                    {
                        statusLabel.Visible = true; titleLabel.Visible = false; artistLabel.Visible = false; controlsRow.Visible = false;
                        return;
                    }
                    var props = await session.TryGetMediaPropertiesAsync();
                    var playback = session.GetPlaybackInfo();
                    if (IsDisposed) return;
                    statusLabel.Visible = false; titleLabel.Visible = true; artistLabel.Visible = true; controlsRow.Visible = true;
                    titleLabel.Text = props.Title ?? "";
                    artistLabel.Text = props.Artist ?? "";
                    playPauseBtn.Text = playback.PlaybackStatus == WinRTMediaControl.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "⏸" : "▶";
                }
                catch (Exception)
                {
                    if (IsDisposed) return;
                    statusLabel.Visible = true; titleLabel.Visible = false; artistLabel.Visible = false; controlsRow.Visible = false;
                    statusLabel.Text = LocalizationManager.T("WidgetText.MediaUnsupported");
                }
            }

            prevBtn.Click += async (_, _) => { if (session != null) await session.TrySkipPreviousAsync(); await RefreshAsync(); };
            nextBtn.Click += async (_, _) => { if (session != null) await session.TrySkipNextAsync(); await RefreshAsync(); };
            playPauseBtn.Click += async (_, _) => { if (session != null) await session.TryTogglePlayPauseAsync(); await RefreshAsync(); };
            foreach (var l in new[] { prevBtn, playPauseBtn, nextBtn })
            {
                l.MouseEnter += (_, _) => l.ForeColor = UiTheme.AccentCyan;
                l.MouseLeave += (_, _) => l.ForeColor = (l == playPauseBtn) ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            }

            await RefreshAsync();
            if (IsDisposed) return;
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _nativeMonitorTimer.Tick += async (_, _) => { if (!AppActivity.IsFullscreenAppActive) await RefreshAsync(); };
            _nativeMonitorTimer.Start();
        }

        // Νέο widget: πρόσφατες ειδοποιήσεις Windows μέσω UserNotificationListener - ζητάει μία
        // φορά άδεια από τον χρήστη (πραγματικό system consent prompt, OS-level) την πρώτη φορά.
        private async System.Threading.Tasks.Task InitializeNativeNotificationsAsync()
        {
            var panel = CreateNativePanel(LocalizationManager.T("Widgets.Notifications"), out var values);
            values.Visible = false;

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
            var statusLabel = new GlassLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Text = LocalizationManager.T("WidgetText.NotificationsLoading") };
            // Dock=Fill χρειάζεται εξωτερικό container + BringToFront (βλ. InitializeNativeDisk) - αλλιώς
            // η λίστα ξεκινά κάτω από το title bar και κρύβεται πίσω του.
            var area = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            area.Controls.Add(list);
            area.Controls.Add(statusLabel);
            panel.Controls.Add(area);
            area.BringToFront();
            statusLabel.BringToFront();
            list.HandleCreated += (_, _) => GlassBlur.ApplyScrollbarTheme(list, true);

            WinRTNotificationsMgmt.UserNotificationListener? listener;
            try { listener = WinRTNotificationsMgmt.UserNotificationListener.Current; }
            catch (Exception)
            {
                statusLabel.Text = LocalizationManager.T("WidgetText.NotificationsUnsupported");
                return;
            }

            bool accessGranted = false;

            async System.Threading.Tasks.Task RefreshAsync()
            {
                try
                {
                    if (!accessGranted)
                    {
                        var access = await listener.RequestAccessAsync();
                        if (IsDisposed) return;
                        accessGranted = access == WinRTNotificationsMgmt.UserNotificationListenerAccessStatus.Allowed;
                        if (!accessGranted)
                        {
                            statusLabel.Text = LocalizationManager.T("WidgetText.NotificationsAccessDenied");
                            statusLabel.Visible = true; list.Visible = false;
                            return;
                        }
                    }
                    var notifications = await listener.GetNotificationsAsync(WinRTNotifications.NotificationKinds.Toast);
                    if (IsDisposed) return;
                    if (notifications.Count == 0)
                    {
                        statusLabel.Text = LocalizationManager.T("WidgetText.NotificationsEmpty");
                        statusLabel.Visible = true; list.Visible = false;
                        return;
                    }
                    statusLabel.Visible = false; list.Visible = true;
                    DisposeChildren(list);
                    foreach (var n in notifications.OrderByDescending(x => x.CreationTime).Take(8))
                    {
                        string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                        string text = "";
                        try
                        {
                            var binding = n.Notification.Visual.GetBinding(WinRTNotifications.KnownNotificationBindings.ToastGeneric);
                            if (binding != null) text = string.Join(" — ", binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                        }
                        catch (Exception) { }

                        int rowWidth = Math.Max(200, list.ClientSize.Width - 4);
                        var row = new Panel { Width = rowWidth, Height = 54, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                        UiTheme.ApplyRoundedRegion(row, 8);
                        row.Controls.Add(new GlassLabel { Text = appName, Location = new Point(10, 6), AutoSize = true, Font = new Font(UiTheme.FontBody.FontFamily, 9f, FontStyle.Bold), ForeColor = UiTheme.TextPrimary });
                        row.Controls.Add(new GlassLabel { Text = text, Location = new Point(10, 24), Size = new Size(rowWidth - 48, 26), AutoEllipsis = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
                        var dismissBtn = new GlassLabel { Text = "✕", Location = new Point(rowWidth - 30, 6), Size = new Size(22, 22), TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, Cursor = Cursors.Hand, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                        uint nid = n.Id;
                        dismissBtn.Click += (_, _) => { try { listener.RemoveNotification(nid); } catch (Exception) { } _ = RefreshAsync(); };
                        row.Controls.Add(dismissBtn);
                        list.Controls.Add(row);
                    }
                }
                catch (Exception)
                {
                    if (IsDisposed) return;
                    statusLabel.Text = LocalizationManager.T("WidgetText.NotificationsUnsupported");
                    statusLabel.Visible = true; list.Visible = false;
                }
            }

            await RefreshAsync();
            if (IsDisposed) return;
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _nativeMonitorTimer.Tick += async (_, _) => { if (!AppActivity.IsFullscreenAppActive) await RefreshAsync(); };
            _nativeMonitorTimer.Start();
        }

        // Νέο widget: ζευγμένες συσκευές Bluetooth + κατάσταση σύνδεσης (μόνο ανάγνωση - ζευγάρωμα/
        // αποσύνδεση απαιτούν επιπλέον capabilities που δεν αξίζουν το ρίσκο σε αυτό το πέρασμα).
        private async System.Threading.Tasks.Task InitializeNativeBluetoothAsync()
        {
            var panel = CreateNativePanel(LocalizationManager.T("Widgets.Bluetooth"), out var values);
            values.Visible = false;

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
            var statusLabel = new GlassLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody, Text = LocalizationManager.T("WidgetText.BluetoothLoading") };
            var area = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            area.Controls.Add(list);
            area.Controls.Add(statusLabel);
            panel.Controls.Add(area);
            area.BringToFront();
            statusLabel.BringToFront();

            async System.Threading.Tasks.Task RefreshAsync()
            {
                try
                {
                    string selector = WinRTBluetooth.BluetoothDevice.GetDeviceSelector();
                    var devices = await WinRTDeviceEnum.DeviceInformation.FindAllAsync(selector);
                    if (IsDisposed) return;
                    if (devices.Count == 0)
                    {
                        statusLabel.Text = LocalizationManager.T("WidgetText.BluetoothNoDevices");
                        statusLabel.Visible = true; list.Visible = false;
                        return;
                    }
                    statusLabel.Visible = false; list.Visible = true;
                    DisposeChildren(list);
                    foreach (var d in devices)
                    {
                        bool connected = false;
                        try
                        {
                            using var bt = await WinRTBluetooth.BluetoothDevice.FromIdAsync(d.Id);
                            connected = bt?.ConnectionStatus == WinRTBluetooth.BluetoothConnectionStatus.Connected;
                        }
                        catch (Exception) { }
                        if (IsDisposed) return;

                        int rowWidth = Math.Max(200, list.ClientSize.Width - 4);
                        var row = new Panel { Width = rowWidth, Height = 40, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 6) };
                        UiTheme.ApplyRoundedRegion(row, 8);
                        row.Controls.Add(new GlassLabel { Text = connected ? "🔵" : "⚪", Location = new Point(10, 9), AutoSize = true, Font = new Font("Segoe UI", 10) });
                        row.Controls.Add(new GlassLabel { Text = d.Name, Location = new Point(36, 10), Size = new Size(rowWidth - 150, 20), AutoEllipsis = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextPrimary });
                        row.Controls.Add(new GlassLabel { Text = connected ? LocalizationManager.T("WidgetText.BluetoothConnected") : LocalizationManager.T("WidgetText.BluetoothPaired"), Location = new Point(rowWidth - 106, 10), Size = new Size(100, 20), TextAlign = ContentAlignment.MiddleRight, Font = new Font(UiTheme.FontBody.FontFamily, 8f), ForeColor = connected ? UiTheme.AccentCyan : UiTheme.TextMuted, Anchor = AnchorStyles.Top | AnchorStyles.Right });
                        list.Controls.Add(row);
                    }
                }
                catch (Exception)
                {
                    if (IsDisposed) return;
                    statusLabel.Text = LocalizationManager.T("WidgetText.BluetoothUnsupported");
                    statusLabel.Visible = true; list.Visible = false;
                }
            }

            await RefreshAsync();
            if (IsDisposed) return;
            _nativeMonitorTimer = new System.Windows.Forms.Timer { Interval = 8000 };
            _nativeMonitorTimer.Tick += async (_, _) => { if (!AppActivity.IsFullscreenAppActive) await RefreshAsync(); };
            _nativeMonitorTimer.Start();
        }

        // Ίδιο μοτίβο με το MainWindow.DisposeChildren (άλλη κλάση/αρχείο, όχι προσβάσιμο από εδώ) -
        // Controls.Clear() μόνο αφαιρεί, δεν κάνει Dispose τα παιδιά (γνωστό leak σε αυτό το codebase).
        private static void DisposeChildren(Control parent)
        {
            foreach (Control child in parent.Controls.Cast<Control>().ToList()) child.Dispose();
            parent.Controls.Clear();
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    }

    // Αναλογικό ρολόι — εναλλακτικό στυλ στο Clock widget (Digital/Analog, ζητήθηκε ρητά).
    // Ζητήθηκε ρητά "analog clock διαφορετικά θέματα": "Classic" (η αρχική εμφάνιση, αμετάβλητη),
    // "Neon" (λαμπερά χέρια/ενδείξεις με glow), "Minimal" (μόνο οι 4 κύριες ενδείξεις 12/3/6/9,
    // λεπτότερα χέρια, χωρίς δευτερολεπτοδείκτη).
    internal sealed class AnalogClockControl : Control
    {
        public string Theme { get; set; } = "Classic";

        public AnalogClockControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);

            int size = Math.Min(Width, Height) - 12;
            if (size < 20) return;
            var center = new PointF(Width / 2f, Height / 2f);
            float radius = size / 2f;
            bool minimal = Theme == "Minimal";
            bool neon = Theme == "Neon";
            // Ζητήθηκε ρητά "περισσότερα θέματα για τα analog ρολόγια": Mono (καθαρά
            // ασπρόμαυρο/χωρίς χρώμα accent) και Retro (πιο χοντρά χέρια, accent χρώμα στο face/hub).
            bool mono = Theme == "Mono";
            bool retro = Theme == "Retro";

            using (var facePen = new Pen(retro ? UiTheme.AccentBlue : UiTheme.Border, retro ? 3f : 2f))
                g.DrawEllipse(facePen, center.X - radius, center.Y - radius, radius * 2, radius * 2);

            using var tickPen = new Pen(retro ? UiTheme.AccentBlue : UiTheme.TextMuted, retro ? 3f : 2f);
            for (int i = 0; i < 12; i++)
            {
                bool isMajor = i % 3 == 0;
                if (minimal && !isMajor) continue; // Minimal: μόνο 12/3/6/9
                double angle = i * Math.PI / 6.0;
                float outer = radius - 3;
                float inner = radius - (isMajor ? 11 : 6);
                var p1 = new PointF(center.X + (float)Math.Sin(angle) * outer, center.Y - (float)Math.Cos(angle) * outer);
                var p2 = new PointF(center.X + (float)Math.Sin(angle) * inner, center.Y - (float)Math.Cos(angle) * inner);
                g.DrawLine(tickPen, p1, p2);
            }

            var now = DateTime.Now;
            Color handColor = mono ? UiTheme.TextPrimary : neon ? UiTheme.AccentCyan : retro ? UiTheme.AccentBlue : UiTheme.TextPrimary;
            Color secondHandColor = mono ? UiTheme.TextMuted : UiTheme.AccentCyan;
            Color hubColor = mono ? UiTheme.TextMuted : retro ? UiTheme.AccentBlue : UiTheme.AccentCyan;
            float widthScale = minimal ? 0.7f : retro ? 1.3f : 1f;
            if (neon)
            {
                // Glow: το ίδιο χέρι ξαναζωγραφίζεται πιο φαρδύ/διάφανο από πίσω — φτηνή προσομοίωση
                // shadowBlur (η Graphics/Pen του GDI+ δεν έχει native glow).
                using var glowPen1 = new Pen(Color.FromArgb(90, UiTheme.AccentCyan), 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                using var glowPen2 = new Pen(Color.FromArgb(70, UiTheme.AccentBlue), 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                DrawHandWithPen(g, center, radius * 0.5f, glowPen1, (now.Hour % 12 + now.Minute / 60.0) * 30.0);
                DrawHandWithPen(g, center, radius * 0.72f, glowPen2, now.Minute * 6.0);
            }
            DrawHand(g, center, radius * 0.5f, 5f * widthScale, (now.Hour % 12 + now.Minute / 60.0) * 30.0, handColor);
            DrawHand(g, center, radius * 0.72f, 3.5f * widthScale, now.Minute * 6.0, handColor);
            if (!minimal) DrawHand(g, center, radius * 0.82f, 1.5f, now.Second * 6.0, secondHandColor);

            using var hubBrush = new SolidBrush(hubColor);
            g.FillEllipse(hubBrush, center.X - 4, center.Y - 4, 8, 8);
        }

        private static void DrawHand(Graphics g, PointF center, float length, float width, double angleDegrees, Color color)
        {
            using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            DrawHandWithPen(g, center, length, pen, angleDegrees);
        }

        private static void DrawHandWithPen(Graphics g, PointF center, float length, Pen pen, double angleDegrees)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            var tip = new PointF(center.X + (float)Math.Sin(rad) * length, center.Y - (float)Math.Cos(rad) * length);
            g.DrawLine(pen, center, tip);
        }
    }

    // Ζωντανό mini sparkline (Download/Upload) κάτω από το κειμενικό στατιστικό — ζητήθηκε ρητά
    // "κάποιο visual γραφικό για ομορφιά" πέρα από τις γυμνές πληροφορίες κειμένου.
    internal sealed class NetworkSparklineControl : Control
    {
        private readonly Queue<double> _down = new();
        private readonly Queue<double> _up = new();
        private const int MaxPoints = 40;

        public NetworkSparklineControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Push(double downKbps, double upKbps)
        {
            _down.Enqueue(downKbps); while (_down.Count > MaxPoints) _down.Dequeue();
            _up.Enqueue(upKbps); while (_up.Count > MaxPoints) _up.Dequeue();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);
            if (_down.Count < 2) return;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            double scaleMax = Math.Max(64, Math.Max(_down.Max(), _up.Max()));
            DrawSeries(g, _down, scaleMax, UiTheme.AccentCyan, true);
            DrawSeries(g, _up, scaleMax, UiTheme.AccentBlue, false);
        }

        private void DrawSeries(Graphics g, Queue<double> series, double scaleMax, Color color, bool fill)
        {
            var values = series.ToArray();
            var pts = new PointF[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float x = (i + (MaxPoints - values.Length)) * Width / (float)Math.Max(1, MaxPoints - 1);   // δεξιά στοιχισμένο: το νέο δείγμα μπαίνει από δεξιά
                double v = scaleMax > 0 ? Math.Clamp(values[i] / scaleMax, 0, 1) : 0;
                pts[i] = new PointF(x, Height - 4 - (float)(v * (Height - 8)));
            }
            if (pts.Length < 2) return;

            if (fill)
            {
                var poly = new PointF[pts.Length + 2];
                Array.Copy(pts, poly, pts.Length);
                poly[pts.Length] = new PointF(pts[^1].X, Height);
                poly[pts.Length + 1] = new PointF(pts[0].X, Height);
                using var fillBrush = new SolidBrush(Color.FromArgb(40, color));
                g.FillPolygon(fillBrush, poly);
            }
            using var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }
    }

    // Εναλλακτικό στυλ απεικόνισης στο Network widget — ζητήθηκε ρητά "διαφορετικά στυλ
    // απεικόνισης των μπαρών" και "στις 3 μπάρες να υπάρχουν πληροφορίες του δικτύου": αντί για
    // γράφημα-ιστορικού (sparkline), δύο σύνολα segmented LED μπαρών (Λήψη/Αποστολή) που δείχνουν
    // την ΤΡΕΧΟΥΣΑ ένταση με πραγματικές αριθμητικές τιμές KB/s δίπλα τους.
    internal sealed class NetworkBarsControl : Control
    {
        private double _downKbps, _upKbps;
        private double _peakDown = 64, _peakUp = 64; // αυτο-κλιμακούμενο ανώτατο όριο αναφοράς

        public NetworkBarsControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Push(double downKbps, double upKbps)
        {
            _downKbps = downKbps;
            _upKbps = upKbps;
            _peakDown = Math.Max(_peakDown * 0.98, downKbps);
            _peakUp = Math.Max(_peakUp * 0.98, upKbps);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);
            if (Width < 20 || Height < 20) return;

            float rowHeight = Height / 2f;
            DrawMeterRow(g, 0, rowHeight, "↓", _downKbps, _peakDown, UiTheme.AccentCyan);
            DrawMeterRow(g, rowHeight, rowHeight, "↑", _upKbps, _peakUp, UiTheme.AccentBlue);
        }

        private void DrawMeterRow(Graphics g, float top, float rowHeight, string arrow, double valueKbps, double peak, Color color)
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            string label = $"{arrow} {valueKbps:0.0} KB/s";
            using var textBrush = new SolidBrush(UiTheme.TextPrimary);
            var labelSize = g.MeasureString(label, font);
            g.DrawString(label, font, textBrush, new PointF(0, top + (rowHeight - labelSize.Height) / 2f));

            const int segments = 12;
            float barsLeft = labelSize.Width + 8;
            float barsWidth = Math.Max(0, Width - barsLeft);
            if (barsWidth < 10) return;
            float segGap = 2f;
            float segWidth = (barsWidth - segGap * (segments - 1)) / segments;
            float barTop = top + rowHeight * 0.25f;
            float barHeight = rowHeight * 0.5f;

            double level = peak > 0 ? Math.Clamp(valueKbps / peak, 0, 1) : 0;
            int lit = (int)Math.Round(level * segments);

            for (int s = 0; s < segments; s++)
            {
                float x = barsLeft + s * (segWidth + segGap);
                Color segColor = s < lit ? SegmentColor(s, segments, color) : Color.FromArgb(40, UiTheme.Border);
                using var brush = new SolidBrush(segColor);
                g.FillRectangle(brush, x, barTop, segWidth, barHeight);
            }
        }

        private static Color SegmentColor(int segmentIndex, int totalSegments, Color baseColor)
        {
            double t = (segmentIndex + 1) / (double)totalSegments;
            if (t > 0.85) return Color.FromArgb(235, 76, 66);
            if (t > 0.6) return Color.FromArgb(240, 200, 60);
            return baseColor;
        }
    }

    // WMP-Legacy-style equalizer: κάθετες, τμηματοποιημένες ("LED") μπάρες με peak-hold καπάκι
    // που πέφτει αργά, τροφοδοτούμενες από πραγματικά FFT bands (AudioSpectrumService) — όχι
    // πλέον ASCII κείμενο βασισμένο σε ένα μοναδικό peak value.
    internal sealed class EqualizerControl : Control
    {
        private float[] _display = Array.Empty<float>();
        private float[] _peaks = Array.Empty<float>();

        // "WMP" (LED μπάρες, προεπιλογή) ή "Winamp" (gradient μπάρες + αντανάκλαση, όπως ζητήθηκε
        // ρητά "visualizations όπως το winamp ή το windows media player legacy").
        public string Style { get; set; } = "WMP";

        public EqualizerControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void PushBands(float[] bands)
        {
            if (_display.Length != bands.Length)
            {
                _display = new float[bands.Length];
                _peaks = new float[bands.Length];
            }
            for (int i = 0; i < bands.Length; i++)
            {
                // Γρήγορη άνοδος, πιο αργή πτώση — τυπική συμπεριφορά equalizer (attack/release).
                _display[i] = bands[i] > _display[i] ? bands[i] : _display[i] * 0.75f + bands[i] * 0.25f;
                if (_display[i] >= _peaks[i]) _peaks[i] = _display[i];
                else _peaks[i] = Math.Max(0, _peaks[i] - 0.02f);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Style == "Winamp") { PaintWinampStyle(e.Graphics); return; }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);
            if (_display.Length == 0) return;

            const int segments = 12;
            float barGap = 2f, segGap = 2f;
            float barWidth = (Width - barGap * (_display.Length - 1)) / _display.Length;
            float segHeight = (Height - segGap * (segments - 1)) / (float)segments;

            for (int b = 0; b < _display.Length; b++)
            {
                float x = b * (barWidth + barGap);
                int litSegments = (int)Math.Round(_display[b] * segments);
                int peakSegment = (int)Math.Round(_peaks[b] * segments);

                for (int s = 0; s < segments; s++)
                {
                    float y = Height - (s + 1) * (segHeight + segGap) + segGap;
                    Color segColor = s < litSegments
                        ? SegmentColor(s, segments)
                        : Color.FromArgb(40, UiTheme.Border);
                    using var brush = new SolidBrush(segColor);
                    g.FillRectangle(brush, x, y, barWidth, segHeight);
                }

                if (peakSegment > 0 && peakSegment <= segments)
                {
                    float py = Height - peakSegment * (segHeight + segGap) + segGap;
                    using var peakBrush = new SolidBrush(Color.White);
                    g.FillRectangle(peakBrush, x, py, barWidth, 2f);
                }
            }
        }

        private static Color SegmentColor(int segmentIndex, int totalSegments)
        {
            double t = (segmentIndex + 1) / (double)totalSegments;
            if (t > 0.85) return Color.FromArgb(235, 76, 66);
            if (t > 0.6) return Color.FromArgb(240, 200, 60);
            return Color.FromArgb(60, 210, 140);
        }

        // Κλασικό Winamp Mini-Visualizer look: συνεχείς (όχι segmented) gradient μπάρες πράσινο→
        // κόκκινο, με αχνή "αντανάκλαση" από κάτω αντί για δεύτερο σετ μπαρών LED.
        private void PaintWinampStyle(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (var bg = new SolidBrush(Color.Black)) g.FillRectangle(bg, ClientRectangle);
            if (_display.Length == 0) return;

            float mainH = Height * 0.8f;
            float mirrorH = Height - mainH;
            const float barGap = 2f;
            float barWidth = (Width - barGap * (_display.Length - 1)) / _display.Length;

            for (int b = 0; b < _display.Length; b++)
            {
                float x = b * (barWidth + barGap);
                float barHeight = _display[b] * mainH;

                if (barHeight > 0.5f)
                {
                    using var grad = new LinearGradientBrush(new RectangleF(x, mainH - barHeight, barWidth, barHeight + 0.01f),
                        Color.FromArgb(255, 230, 60, 60), Color.FromArgb(255, 30, 220, 90), LinearGradientMode.Vertical);
                    g.FillRectangle(grad, x, mainH - barHeight, barWidth, barHeight);
                }

                float mirrorBarH = Math.Min(mirrorH, barHeight * (mainH > 0 ? mirrorH / mainH : 0));
                if (mirrorBarH > 0.5f)
                {
                    using var mirrorGrad = new LinearGradientBrush(new RectangleF(x, mainH, barWidth, mirrorBarH + 0.01f),
                        Color.FromArgb(90, 30, 220, 90), Color.FromArgb(0, 30, 220, 90), LinearGradientMode.Vertical);
                    g.FillRectangle(mirrorGrad, x, mainH, barWidth, mirrorBarH);
                }

                float peakY = mainH - _peaks[b] * mainH;
                using var peakBrush = new SolidBrush(Color.White);
                g.FillRectangle(peakBrush, x, peakY, barWidth, 2f);
            }
        }
    }

    // Χειροποίητο, ζωντανό εικονίδιο καιρού (όχι στατικό emoji) — αλλάζει σχήμα ανάλογα με τη
    // συνθήκη καιρού (WMO code) ΚΑΙ κινείται (ζητήθηκε ρητά "κινούμενα εικονίδια ανάλογα με την
    // κατάσταση του καιρού"): ήλιος με περιστρεφόμενες ακτίνες, σύννεφο που μετατοπίζεται, βροχή/
    // χιόνι με σταγόνες/νιφάδες που πέφτουν, καταιγίδα με αναβοσβήνοντα κεραυνό.
    internal sealed class WeatherIconControl : Control
    {
        public WeatherCondition Condition { get; set; } = WeatherCondition.Clear;
        private int _phase;
        private readonly Random _rng = new();
        private readonly PointF[] _drops;

        public WeatherIconControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _drops = new PointF[10];
            for (int i = 0; i < _drops.Length; i++) _drops[i] = new PointF((float)_rng.NextDouble(), (float)_rng.NextDouble());
        }

        public void AdvancePhase() { _phase++; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);

            float cx = Width / 2f, cy = Height / 2f - 4;
            // Μεγαλύτερο κινούμενο εικονίδιο καιρού — ζητήθηκε ρητά (ήταν πολύ μικρό σε σχέση με
            // το widget). Μικρότερος διαιρέτης = μεγαλύτερο εικονίδιο για το ίδιο μέγεθος control.
            float scale = Math.Min(Width, Height) / 85f;
            if (scale <= 0) return;

            switch (Condition)
            {
                case WeatherCondition.Clear: DrawSun(g, cx, cy, scale, 1f); break;
                case WeatherCondition.PartlyCloudy: DrawSun(g, cx - 14 * scale, cy - 6 * scale, scale * 0.7f, 0.8f); DrawCloud(g, cx + 6 * scale, cy + 8 * scale, scale); break;
                case WeatherCondition.Cloudy: case WeatherCondition.Fog: DrawCloud(g, cx, cy, scale * 1.15f); break;
                case WeatherCondition.Drizzle: case WeatherCondition.Rain: DrawCloud(g, cx, cy - 10 * scale, scale); DrawRain(g, cx, cy, scale); break;
                case WeatherCondition.Snow: DrawCloud(g, cx, cy - 10 * scale, scale); DrawSnow(g, cx, cy, scale); break;
                case WeatherCondition.Thunderstorm: DrawCloud(g, cx, cy - 10 * scale, scale); DrawBolt(g, cx, cy, scale); break;
            }
        }

        private void DrawSun(Graphics g, float cx, float cy, float scale, float alpha)
        {
            using var rayPen = new Pen(Color.FromArgb((int)(200 * alpha), 250, 190, 70), 3f * scale);
            double rot = _phase * 1.2 * Math.PI / 180.0;
            float r1 = 22 * scale, r2 = 30 * scale;
            for (int i = 0; i < 8; i++)
            {
                double a = rot + i * Math.PI / 4;
                g.DrawLine(rayPen,
                    cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1,
                    cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2);
            }
            using var sunBrush = new SolidBrush(Color.FromArgb((int)(255 * alpha), 255, 205, 90));
            g.FillEllipse(sunBrush, cx - 18 * scale, cy - 18 * scale, 36 * scale, 36 * scale);
        }

        private void DrawCloud(Graphics g, float cx, float cy, float scale)
        {
            float drift = (float)Math.Sin(_phase * 0.02) * 6f * scale;
            cx += drift;
            using var cloudBrush = new SolidBrush(UiTheme.TextMuted);
            g.FillEllipse(cloudBrush, cx - 26 * scale, cy - 6 * scale, 30 * scale, 24 * scale);
            g.FillEllipse(cloudBrush, cx - 6 * scale, cy - 16 * scale, 36 * scale, 32 * scale);
            g.FillEllipse(cloudBrush, cx + 16 * scale, cy - 2 * scale, 26 * scale, 22 * scale);
            g.FillRectangle(cloudBrush, cx - 24 * scale, cy + 6 * scale, 62 * scale, 12 * scale);
        }

        private void DrawRain(Graphics g, float cx, float cy, float scale)
        {
            using var dropPen = new Pen(Color.FromArgb(200, 90, 160, 230), 2.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int i = 0; i < _drops.Length; i++)
            {
                float t = ((_phase * 0.03f + i * 0.37f) % 1f);
                float x = cx + (_drops[i].X - 0.5f) * 60 * scale;
                float y = cy + 10 * scale + t * 34 * scale;
                g.DrawLine(dropPen, x, y, x - 3 * scale, y + 8 * scale);
            }
        }

        private void DrawSnow(Graphics g, float cx, float cy, float scale)
        {
            using var flakeBrush = new SolidBrush(Color.White);
            for (int i = 0; i < _drops.Length; i++)
            {
                float t = ((_phase * 0.015f + i * 0.41f) % 1f);
                float x = cx + (_drops[i].X - 0.5f) * 60 * scale + (float)Math.Sin(_phase * 0.05 + i) * 4 * scale;
                float y = cy + 8 * scale + t * 36 * scale;
                g.FillEllipse(flakeBrush, x, y, 3.5f * scale, 3.5f * scale);
            }
        }

        private void DrawBolt(Graphics g, float cx, float cy, float scale)
        {
            bool flash = (_phase / 8) % 3 == 0;
            if (!flash) return;
            var pts = new[]
            {
                new PointF(cx - 2 * scale, cy + 6 * scale),
                new PointF(cx + 6 * scale, cy + 6 * scale),
                new PointF(cx - 2 * scale, cy + 24 * scale),
                new PointF(cx + 10 * scale, cy + 18 * scale),
                new PointF(cx + 2 * scale, cy + 18 * scale),
                new PointF(cx + 10 * scale, cy + 2 * scale),
            };
            using var boltBrush = new SolidBrush(Color.FromArgb(230, 255, 221, 87));
            g.FillPolygon(boltBrush, pts);
        }
    }

    // Κυκλικό ("donut") ring ανά δίσκο, με τις πληροφορίες στο κέντρο — αντικατέστησε τη
    // scrollable λίστα γραμμών (DiskUsageRow) κατόπιν ρητού αιτήματος. Ένας δίσκος τη φορά, με
    // ◀/▶ σελιδοποίηση από το InitializeNativeDisk αντί για scroll.
    internal sealed class DiskRingControl : Control
    {
        private string _letter = "";
        private string _label = "";
        private double _usedGb, _totalGb, _pct;

        public DiskRingControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetData(string letter, string label, double usedGb, double totalGb, double pct)
        {
            _letter = letter; _label = label; _usedGb = usedGb; _totalGb = totalGb; _pct = Math.Clamp(pct, 0, 1);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);

            float size = Math.Min(Width, Height) - 20;
            if (size < 30) return;
            float thickness = Math.Max(8f, size * 0.11f);
            var ringRect = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
            var inset = ringRect;
            inset.Inflate(-thickness / 2f, -thickness / 2f);

            // Πιο διακριτικό "άδειο" κομμάτι του δακτυλίου (ζητήθηκε ρητά "μη εμφάνιση λευκού
            // πλαισίου") — το πλήρες UiTheme.Border ήταν πολύ έντονο/φωτεινό σε ανοιχτόχρωμα θέματα.
            using (var trackPen = new Pen(Color.FromArgb(70, UiTheme.Border), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawEllipse(trackPen, inset);

            Color ringColor = _pct > 0.9 ? Color.FromArgb(235, 76, 66) : _pct > 0.75 ? Color.FromArgb(240, 200, 60) : UiTheme.AccentCyan;
            if (_pct > 0.001)
            {
                using var arcPen = new Pen(ringColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float sweep = (float)(_pct * 360.0);
                g.DrawArc(arcPen, inset, -90f, sweep);
            }

            using var letterFont = new Font("Segoe UI", size * 0.09f, FontStyle.Bold);
            using var pctFont = new Font("Segoe UI", size * 0.16f, FontStyle.Bold);
            using var subFont = new Font("Segoe UI", size * 0.065f);
            using var textBrush = new SolidBrush(UiTheme.TextPrimary);
            using var subBrush = new SolidBrush(UiTheme.TextSecondary);
            using var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            // ΔΙΟΡΘΩΣΗ πραγματικού bug ("τα γράμματα να χωράνε μέσα στον κύκλο"): οι γραμμές
            // κειμένου ζωγραφίζονταν με ΣΤΑΘΕΡΟ μέγεθος γραμματοσειράς ανεξάρτητα από το πόσο
            // πλατύ είναι πραγματικά το εσωτερικό άδειο κύκλο σε ΚΑΘΕ ύψος (η διαθέσιμη πλάτη
            // μειώνεται όσο απομακρυνόμαστε κάθετα από το κέντρο) — η γραμμή "χρησιμοποιημένα/
            // σύνολο GB" (η πιο μακριά συμβολοσειρά, ΚΑΙ η πιο απομακρυσμένη από το κέντρο) ξεπερνούσε
            // εύκολα το πλάτος αυτό, ειδικά σε δίσκους με 4ψήφιο μέγεθος (π.χ. "1024,0 / 2048,0 GB").
            // Λύση: υπολογίζουμε το πραγματικά διαθέσιμο πλάτος (χορδή του εσωτερικού κύκλου) σε
            // κάθε ύψος και σμικρύνουμε τη γραμματοσειρά όσο χρειάζεται ώστε το κείμενο να χωράει.
            float innerRadius = size / 2f - thickness;
            float AvailableWidth(float dy) => Math.Abs(dy) >= innerRadius ? 0f : 2f * (float)Math.Sqrt(Math.Max(0.0, (double)innerRadius * innerRadius - (double)dy * dy)) - 6f;
            // Επιστρέφει (font, owned) — "owned" true ΜΟΝΟ όταν φτιάχτηκε ΝΕΑ γραμματοσειρά εδώ,
            // ώστε να μην κάνουμε Dispose μια γραμματοσειρά που ανήκει ήδη σε εξωτερικό "using"
            // (θα προκαλούσε διπλό Dispose στο ίδιο αντικείμενο).
            (Font font, bool owned) FitFont(string text, Font baseFont, float maxWidth)
            {
                if (maxWidth <= 0) return (baseFont, false);
                var measured = g.MeasureString(text, baseFont);
                if (measured.Width <= maxWidth) return (baseFont, false);
                float newSize = Math.Max(6f, baseFont.Size * (maxWidth / measured.Width));
                return (new Font(baseFont.FontFamily, newSize, baseFont.Style), true);
            }

            float cx = ringRect.X + ringRect.Width / 2f, cy = ringRect.Y + ringRect.Height / 2f;
            float pctY = -size * 0.08f, letterY = size * 0.13f, gbY = size * 0.26f;
            string pctText = $"{_pct * 100:0}%", gbText = $"{_usedGb:0} / {_totalGb:0} GB";

            var (pctDraw, pctOwned) = FitFont(pctText, pctFont, AvailableWidth(pctY));
            g.DrawString(pctText, pctDraw, textBrush, new PointF(cx, cy + pctY), centerFormat);
            if (pctOwned) pctDraw.Dispose();

            var (letterDraw, letterOwned) = FitFont(_letter, letterFont, AvailableWidth(letterY));
            g.DrawString(_letter, letterDraw, subBrush, new PointF(cx, cy + letterY), centerFormat);
            if (letterOwned) letterDraw.Dispose();

            var (gbDraw, gbOwned) = FitFont(gbText, subFont, AvailableWidth(gbY));
            g.DrawString(gbText, gbDraw, subBrush, new PointF(cx, cy + gbY), centerFormat);
            if (gbOwned) gbDraw.Dispose();

            using var labelFont = new Font("Segoe UI", size * 0.07f);
            using var labelFormat = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(_label, labelFont, subBrush, new RectangleF(0, ringRect.Bottom + 6, Width, 18), labelFormat);
        }
    }

    // System Monitor — σύγχρονο "dashboard tiles" στυλ (SeelenUI / Windows 11 Widgets): ένα στρογγυλεμένο tile ανά μέτρηση
    // (CPU/RAM/Δίκτυο/GPU) με σταθερό χρώμα-ταυτότητα, μικρή ετικέτα, μεγάλη τιμή, υπο-κείμενο και sparkline-ιστορικό
    // (area με gradient) στο πίσω μέρος. Κόκκινο ΜΟΝΟ πάνω από 90% ως προειδοποίηση. Προσαρμόζεται στο μέγεθος:
    // σε μικρό ύψος ή στενό πλάτος κρύβονται οι δευτερεύουσες λεπτομέρειες αντί να κόβονται.
    internal sealed class SystemMonitorRowsControl : Control
    {
        private const int HistoryLength = 60;   // 60 δείγματα × 2 s = 2 λεπτά ιστορικού

        private sealed class Series
        {
            public readonly Queue<double> Values = new();
            public void Push(double v01) { Values.Enqueue(Math.Clamp(v01, 0, 1)); while (Values.Count > HistoryLength) Values.Dequeue(); }
        }

        private double _cpuPct, _ramPct, _netDown, _netUp, _netPeak = 64;
        private string _ramSub = "", _gpuSub = "";
        private double? _gpuPct;
        private readonly Series _cpuH = new(), _ramH = new(), _netH = new(), _gpuH = new();

        public SystemMonitorRowsControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetCpu(double pct) { _cpuPct = pct; _cpuH.Push(pct / 100.0); Invalidate(); }
        public void SetRam(double pct, string sub) { _ramPct = pct; _ramSub = sub; _ramH.Push(pct / 100.0); Invalidate(); }
        public void SetNetwork(double downKbps, double upKbps)
        {
            _netDown = downKbps; _netUp = upKbps;
            _netPeak = Math.Max(_netPeak * 0.98, Math.Max(downKbps, upKbps));
            _netH.Push(_netPeak > 0 ? Math.Max(downKbps, upKbps) / _netPeak : 0);
            Invalidate();
        }
        private string? _gpuName;
        private RectangleF _gpuPrevRect, _gpuNextRect;
        public int GpuIndex { get; set; }
        public int GpuCount { get; set; }
        public event Action<int>? GpuStep;
        public void SetGpu(double? pct, string sub, string? name = null) { _gpuPct = pct; _gpuSub = sub; _gpuName = name; _gpuH.Push((pct ?? 0) / 100.0); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && GpuCount > 1)
            {
                if (_gpuPrevRect.Contains(e.Location)) { GpuStep?.Invoke(-1); return; }
                if (_gpuNextRect.Contains(e.Location)) { GpuStep?.Invoke(1); return; }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = GpuCount > 1 && (_gpuPrevRect.Contains(e.Location) || _gpuNextRect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        private static Color Identity(double pct, Color c) => pct > 90 ? Color.FromArgb(235, 76, 66) : c;

        private static string Rate(double kbps) => kbps >= 1024 ? $"{kbps / 1024:0.0} MB/s" : $"{kbps:0} KB/s";

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);
            if (Width < 60 || Height < 60) return;

            const float pad = 6f, gap = 6f;
            // Πλατύ widget -> 2×2 πλέγμα, ψηλό/στενό -> 4 γραμμές.
            bool grid = Width >= Height * 1.1f;
            int cols = grid ? 2 : 1, rows = grid ? 2 : 4;
            float tw = (Width - pad * 2 - gap * (cols - 1)) / cols;
            float th = (Height - pad * 2 - gap * (rows - 1)) / rows;

            var tiles = new (string Label, string Value, string Sub, Series H, Color C, double Pct)[]
            {
                ("CPU", $"{_cpuPct:0}%", "", _cpuH, UiTheme.AccentCyan, _cpuPct),
                ("RAM", $"{_ramPct:0}%", _ramSub, _ramH, UiTheme.AccentBlue, _ramPct),
                (LocalizationManager.T("WidgetText.NetLabel"), Rate(Math.Max(_netDown, _netUp)), $"↓ {Rate(_netDown)}   ↑ {Rate(_netUp)}", _netH, Color.FromArgb(180, 120, 235), 0),
                (GpuCount > 1 ? $"GPU {GpuIndex % GpuCount + 1}/{GpuCount}" : "GPU", _gpuPct.HasValue ? $"{_gpuPct:0}%" : "—", string.IsNullOrEmpty(_gpuName) ? _gpuSub : _gpuSub + "  " + _gpuName, _gpuH, Color.FromArgb(240, 150, 60), _gpuPct ?? 0),
            };
            for (int i = 0; i < tiles.Length; i++)
            {
                int cx = grid ? i % 2 : 0, cy = grid ? i / 2 : i;
                var r = new RectangleF(pad + cx * (tw + gap), pad + cy * (th + gap), tw, th);
                DrawTile(g, r, tiles[i].Label, tiles[i].Value, tiles[i].Sub, tiles[i].H, Identity(tiles[i].Pct, tiles[i].C));
                if (i == 3 && GpuCount > 1 && r.Width >= 90 && r.Height >= 34)
                {
                    float s = Math.Min(20f, r.Height * 0.4f);
                    _gpuNextRect = new RectangleF(r.Right - s - 4, r.Y + 4, s, s);
                    _gpuPrevRect = new RectangleF(_gpuNextRect.X - s - 2, r.Y + 4, s, s);
                    using var af = new Font("Segoe UI Symbol", Math.Max(7f, s * 0.5f), FontStyle.Bold);
                    using var ab = new SolidBrush(UiTheme.TextSecondary);
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("◀", af, ab, _gpuPrevRect, sf);
                    g.DrawString("▶", af, ab, _gpuNextRect, sf);
                }
                else if (i == 3) { _gpuPrevRect = _gpuNextRect = RectangleF.Empty; }
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void DrawTile(Graphics g, RectangleF r, string label, string value, string sub, Series h, Color accent)
        {
            if (r.Width < 30 || r.Height < 24) return;
            float radius = Math.Min(12f, r.Height * 0.22f);
            using var shape = Rounded(r, radius);
            // "Πλήρης διαφάνεια": το παράθυρο είναι color-key (Black) - ό,τι ζωγραφίζεται ημιδιαφανές πάνω του γίνεται σκούρο
            // κουτί (όχι διαφάνεια). Εκεί ζωγραφίζουμε ΜΟΝΟ τις πληροφορίες (κείμενο + γραμμή ιστορικού), χωρίς fill/gradient.
            bool transparent = UiTheme.IsFullyTransparent;
            if (!transparent)
                using (var fill = new SolidBrush(Color.FromArgb(70, UiTheme.Surface))) g.FillPath(fill, shape);

            // Sparkline-ιστορικό (area + γραμμή) στο κάτω ~55% του tile, clipped στο στρογγυλεμένο σχήμα.
            var state = g.Save();
            g.SetClip(shape, CombineMode.Intersect);
            var vals = h.Values.ToArray();
            if (vals.Length >= 2)
            {
                float sx = r.X, sw = r.Width, sh = r.Height * 0.40f, sy = r.Bottom - 3;
                var pts = new PointF[vals.Length];
                for (int i = 0; i < vals.Length; i++)
                    pts[i] = new PointF(sx + sw * (i + (HistoryLength - vals.Length)) / (HistoryLength - 1f), sy - (float)vals[i] * sh);
                var area = new PointF[pts.Length + 2];
                Array.Copy(pts, area, pts.Length);
                area[pts.Length] = new PointF(pts[^1].X, r.Bottom); area[pts.Length + 1] = new PointF(pts[0].X, r.Bottom);
                if (!transparent)
                    using (var grad = new LinearGradientBrush(new RectangleF(sx, sy - sh, sw, sh + 4), Color.FromArgb(110, accent), Color.FromArgb(0, accent), LinearGradientMode.Vertical))
                        g.FillPolygon(grad, area);
                using var line = new Pen(transparent ? accent : Color.FromArgb(230, accent), transparent ? 2f : 1.6f) { LineJoin = LineJoin.Round };
                g.DrawLines(line, pts);
            }
            g.Restore(state);

            // Λεπτή μπάρα-ταυτότητα αριστερά.
            using (var bar = new SolidBrush(accent)) g.FillRectangle(bar, r.X + 7, r.Y + 8, 3, Math.Min(r.Height - 16, 26f));

            float textX = r.X + 16;
            bool roomy = r.Height >= 58;
            using var labelFont = new Font("Segoe UI Semibold", Math.Clamp(r.Height * 0.15f, 7f, 10f));
            using var valueFont = new Font("Segoe UI Semibold", Math.Clamp(r.Height * (roomy ? 0.25f : 0.34f), 10f, 20f));
            using var subFont = new Font("Segoe UI", Math.Clamp(r.Height * 0.12f, 6.5f, 8.5f));
            using var muted = new SolidBrush(UiTheme.TextMuted);
            using var primary = new SolidBrush(UiTheme.TextPrimary);
            using var secondary = new SolidBrush(UiTheme.TextSecondary);

            float valueY = r.Y + 5 + labelFont.GetHeight(g);
            if (transparent)
            {
                // Διακριτική σκιά για αναγνωσιμότητα πάνω σε οποιοδήποτε wallpaper (όχι ακριβώς μαύρο: δεν είναι το color-key).
                using var shadow = new SolidBrush(Color.FromArgb(255, 14, 14, 18));
                g.DrawString(label.ToUpperInvariant(), labelFont, shadow, textX + 1, r.Y + 6);
                g.DrawString(value, valueFont, shadow, textX + 1.5f, valueY);
            }
            g.DrawString(label.ToUpperInvariant(), labelFont, muted, textX, r.Y + 5);
            g.DrawString(value, valueFont, primary, textX, valueY - 1);
            if (roomy && sub.Length > 0 && r.Width >= 110)
            {
                var subRect = new RectangleF(textX, valueY + valueFont.GetHeight(g) - 2, r.Right - textX - 6, subFont.GetHeight(g) + 2);
                using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                if (transparent)
                {
                    using var shadow = new SolidBrush(Color.FromArgb(255, 14, 14, 18));
                    g.DrawString(sub, subFont, shadow, new RectangleF(subRect.X + 1, subRect.Y + 1, subRect.Width, subRect.Height), fmt);
                }
                g.DrawString(sub, subFont, secondary, subRect, fmt);
            }
        }
    }

    // Μηνιαίο πλέγμα ημερολογίου (χωρίς πραγματικά ραντεβού - βλ. σχόλιο στο InitializeNativeCalendar).
    internal sealed class CalendarGridControl : Control
    {
        private DateTime _month = DateTime.Today;
        public CalendarGridControl() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        public void SetMonth(DateTime month) { _month = month; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(UiTheme.SurfaceGlass)) g.FillRectangle(bg, ClientRectangle);
            if (Width < 60 || Height < 60) return;

            var culture = CultureInfo.CurrentCulture;
            var dayNames = culture.DateTimeFormat.AbbreviatedDayNames;
            int firstDow = (int)culture.DateTimeFormat.FirstDayOfWeek;
            var first = new DateTime(_month.Year, _month.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(_month.Year, _month.Month);
            int leadDays = ((int)first.DayOfWeek - firstDow + 7) % 7;

            float headerH = 18f;
            float cellW = Width / 7f;
            int rows = (int)Math.Ceiling((leadDays + daysInMonth) / 7.0);
            float cellH = Math.Max(1f, (Height - headerH) / Math.Max(rows, 1));

            using var headFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var headBrush = new SolidBrush(UiTheme.TextMuted);
            using var centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            for (int c = 0; c < 7; c++)
            {
                string name = dayNames[(firstDow + c) % 7];
                string letter = name.Length > 0 ? char.ToUpper(name[0], culture).ToString() : "";
                g.DrawString(letter, headFont, headBrush, new RectangleF(c * cellW, 0, cellW, headerH), centerFormat);
            }

            using var dayFont = new Font("Segoe UI", 8.5f);
            using var dayBrush = new SolidBrush(UiTheme.TextPrimary);
            using var todayBrush = new SolidBrush(UiTheme.AccentCyan);
            for (int day = 1; day <= daysInMonth; day++)
            {
                int cellIndex = leadDays + day - 1;
                int row = cellIndex / 7, col = cellIndex % 7;
                var rect = new RectangleF(col * cellW, headerH + row * cellH, cellW, cellH);
                bool isToday = _month.Year == DateTime.Today.Year && _month.Month == DateTime.Today.Month && day == DateTime.Today.Day;
                if (isToday)
                {
                    float d = Math.Min(cellW, cellH) * 0.72f;
                    g.FillEllipse(todayBrush, rect.X + (rect.Width - d) / 2, rect.Y + (rect.Height - d) / 2, d, d);
                }
                g.DrawString(day.ToString(culture), dayFont, isToday ? Brushes.White : dayBrush, rect, centerFormat);
            }
        }
    }

    public class WidgetState
    {
        public string WidgetId { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsLocked { get; set; }

        // Clock: "Digital" | "Analog".
        public string Variant { get; set; } = "Digital";
        public bool Clock24Hour { get; set; } = true;
        public bool ClockShowSeconds { get; set; } = true;
        public string AnalogTheme { get; set; } = "Classic";
        public string DigitalFontFamily { get; set; } = "Segoe UI";
        public int DigitalColorArgb { get; set; } = -1;

        // Audio Visualizer: "WMP" (LED μπάρες) | "Winamp" (gradient + αντανάκλαση).
        public string AudioStyle { get; set; } = "WMP";

        // Network: "Sparkline" (γράφημα) | "Bars" (μπάρες σήματος με πραγματικές τιμές).
        public string NetworkStyle { get; set; } = "Sparkline";

        // Διαφάνεια widget (0.25-1.0), όπως ήδη έχουν τα DeskContainers — ζητήθηκε ρητά.
        public double Opacity { get; set; } = 1.0;

        // Weather: αποθηκευμένη τοποθεσία χρήστη — Αθήνα παραμένει το default όταν είναι κενό.
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? LocationName { get; set; }
    }

    public sealed class WidgetSnapshot
    {
        public string WidgetId { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsLocked { get; set; }
    }

    public class WidgetHostEngine
    {
        private static WidgetHostEngine? _instance;
        public static WidgetHostEngine Instance => _instance ??= new WidgetHostEngine();

        private readonly List<WidgetWindow> _activeWidgets = new();
        public IReadOnlyCollection<WidgetWindow> GetActiveWidgets() => _activeWidgets;

        public IReadOnlyList<WidgetSnapshot> GetSnapshots() => _activeWidgets
            .Where(w => !w.IsDisposed)
            .Select(w => new WidgetSnapshot { WidgetId = w.WidgetId, X = w.Location.X, Y = w.Location.Y, Width = w.Width, Height = w.Height, IsLocked = w.IsLocked })
            .ToArray();

        public void RestoreSnapshots(IEnumerable<WidgetSnapshot> snapshots)
        {
            CloseAll();
            foreach (var snapshot in snapshots.Where(s => !string.IsNullOrWhiteSpace(s.WidgetId)))
            {
                var widget = SpawnWidget(snapshot.WidgetId, snapshot.X, snapshot.Y, Math.Max(160, snapshot.Width), Math.Max(100, snapshot.Height));
                widget.IsLocked = snapshot.IsLocked;
                widget.SaveWidgetState();
            }
        }

        public void CloseAll()
        {
            foreach (var widget in new List<WidgetWindow>(_activeWidgets))
                if (!widget.IsDisposed) widget.Close();
            _activeWidgets.Clear();
        }

        public WidgetWindow SpawnWidget(string widgetId, int x, int y, int width, int height)
        {
            var existing = _activeWidgets.Find(w => w.WidgetId == widgetId);
            if (existing != null && !existing.IsDisposed)
            {
                existing.BringToFront();
                return existing;
            }

            // Ένα ενεργό widget πρέπει να ξαναγυρίζει αυτόματα στην επόμενη εκκίνηση των Windows
            // (ζητήθηκε ρητά) — το --background flag (StartupManager.SetStartup) φροντίζει ήδη
            // ώστε αυτή η αυτόματη εκκίνηση να μην αναδύει το κύριο παράθυρο.
            if (!StartupManager.IsStartupEnabled()) StartupManager.SetStartup(true);

            var widgetWindow = new WidgetWindow(widgetId, x, y, width, height);
            _activeWidgets.Add(widgetWindow);
            widgetWindow.FormClosed += (s, e) => _activeWidgets.Remove(widgetWindow);
            widgetWindow.Show();
            return widgetWindow;
        }
    }
}
