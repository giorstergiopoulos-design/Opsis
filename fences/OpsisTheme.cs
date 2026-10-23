using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Desktop_Fences
{
    // Opsis integration: the upstream settings windows hard-code a light theme. When Opsis starts the engine it passes its
    // current palette in OPSIS_THEME ("dark;bg;surface;hover;border;text;text2;muted;accent", hex RRGGBB). Every settings /
    // dialog window (never the fences themselves) is re-coloured to that palette after it loads and while it is open.
    internal static class OpsisTheme
    {
        private static bool _enabled;
        private static Color _bg, _surface, _hover, _border, _text, _text2, _muted, _accent;
        private static Style? _buttonStyle;

        public static void Init()
        {
            string? spec = Environment.GetEnvironmentVariable("OPSIS_THEME");
            if (string.IsNullOrWhiteSpace(spec)) return;
            var p = spec.Split(';');
            if (p.Length < 9 || p[0] != "1") return;           // only the dark palette is mapped; a light Opsis theme keeps the original light windows
            try
            {
                _bg = Hex(p[1]); _surface = Hex(p[2]); _hover = Hex(p[3]); _border = Hex(p[4]);
                _text = Hex(p[5]); _text2 = Hex(p[6]); _muted = Hex(p[7]); _accent = Hex(p[8]);
            }
            catch (FormatException) { return; }
            _enabled = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString("#" + s.TrimStart('#'));

        private static bool IsFenceOrOverlay(Window w) =>
            w is NonActivatingWindow || w is SmartToast || w is DrawFenceOverlay || w is FenceFocusFormManager || w is NotificationFormManager
            || w.GetType().Name.Contains("WaitWindow") || w.Title == "" && w.WindowStyle == WindowStyle.None && w.AllowsTransparency && w.Width < 400 && w.Height < 120;

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!_enabled || sender is not Window w || IsFenceOrOverlay(w) || !ReferenceEquals(e.OriginalSource, w)) return;
            Recolor(w);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };   // content built later (tabs, lists) gets the same treatment
            timer.Tick += (_, _) => { if (w.IsVisible) Recolor(w); else timer.Stop(); };
            timer.Start();
            w.Closed += (_, _) => timer.Stop();
        }

        private static double Lum(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        private static double Sat(Color c) { int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx == 0 ? 0 : (mx - mn) / (double)mx; }

        private static Brush? MapBack(Brush? b)
        {
            if (b is not SolidColorBrush s || s.Color.A < 16) return b;
            var c = s.Color; double l = Lum(c);
            Color? n = null;
            if (l >= 0.97) n = _surface;
            else if (l >= 0.80) n = _bg;
            else if (l >= 0.55 && Sat(c) < 0.45) n = _hover;
            else if (l >= 0.25 && l < 0.55 && Sat(c) < 0.18) n = _hover;      // neutral grey headers
            if (n == null) return b;
            var nb = new SolidColorBrush(Color.FromArgb(c.A, n.Value.R, n.Value.G, n.Value.B)); nb.Freeze(); return nb;
        }

        private static Brush? MapFore(Brush? b)
        {
            if (b is not SolidColorBrush s || s.Color.A < 16) return b;
            double l = Lum(s.Color);
            if (l >= 0.5 || Sat(s.Color) > 0.5) return b;
            var n = l < 0.25 ? _text : _text2;
            var nb = new SolidColorBrush(Color.FromArgb(s.Color.A, n.R, n.G, n.B)); nb.Freeze(); return nb;
        }

        private static Brush? MapBorder(Brush? b)
        {
            if (b is not SolidColorBrush s || s.Color.A < 16 || Lum(s.Color) < 0.55) return b;
            var nb = new SolidColorBrush(Color.FromArgb(s.Color.A, _border.R, _border.G, _border.B)); nb.Freeze(); return nb;
        }

        private static void Recolor(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            switch (root)
            {
                case Window w: w.Background = MapBack(w.Background); break;
                case Border b: b.Background = MapBack(b.Background); b.BorderBrush = MapBorder(b.BorderBrush); break;
                case Panel p: p.Background = MapBack(p.Background); break;
                case TextBlock t: t.Foreground = MapFore(t.Foreground); t.Background = MapBack(t.Background); break;
                case Control c:
                    c.Background = MapBack(c.Background); c.Foreground = MapFore(c.Foreground); c.BorderBrush = MapBorder(c.BorderBrush);
                    break;
                case Shape sh: sh.Stroke = MapBorder(sh.Stroke); break;
            }
            if (root is Button button && button.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue && !ReferenceEquals(button.Style, ButtonStyle))
                button.Style = ButtonStyle;
            for (int i = 0; i < n; i++) Recolor(VisualTreeHelper.GetChild(root, i));
        }

        private static Style ButtonStyle => _buttonStyle ??= BuildButtonStyle();

        private static Style BuildButtonStyle()
        {
            string xaml = @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Setter Property='Background' Value='#HOVER'/>
  <Setter Property='Foreground' Value='#TEXT'/>
  <Setter Property='BorderBrush' Value='#BORDER'/>
  <Setter Property='Padding' Value='14,6'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.85'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.7'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            xaml = xaml.Replace("#HOVER", ToHex(_hover)).Replace("#TEXT", ToHex(_text)).Replace("#BORDER", ToHex(_border));
            return (Style)XamlReader.Parse(xaml);
        }
    }
}
