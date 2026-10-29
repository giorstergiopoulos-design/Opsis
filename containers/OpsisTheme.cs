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

        private static bool _handlerRegistered;

        public static void Init() => ApplySpec(Environment.GetEnvironmentVariable("OPSIS_THEME"));

        // Called again (through the bridge) when the Opsis theme changes while the engine runs; windows opened afterwards use the new palette.
        public static void Reload(string spec)
        {
            try { if (Application.Current != null) Application.Current.Dispatcher.Invoke(() => ApplySpec(spec)); } catch (Exception) { }
        }

        private static void ApplySpec(string? spec)
        {
            var pal = (spec ?? "").Split(';');
            bool dark = pal.Length >= 9 && pal[0] == "1";
            if (dark)
            {
                try
                {
                    _bg = Hex(pal[1]); _surface = Hex(pal[2]); _hover = Hex(pal[3]); _border = Hex(pal[4]);
                    _text = Hex(pal[5]); _text2 = Hex(pal[6]); _muted = Hex(pal[7]); _accent = Hex(pal[8]);
                }
                catch (FormatException) { return; }
            }
            // Menus always follow the Opsis style (dark surface); built-in Opsis colours when the Opsis theme is light.
            try { OpsisMenuStyles.Apply(dark ? "#" + pal[2] : "#1E2129", dark ? "#" + pal[3] : "#2C303C", dark ? "#" + pal[4] : "#3A3F4E", dark ? "#" + pal[5] : "#EBEEF5", dark ? "#" + pal[7] : "#8E94A6", dark ? "#" + pal[8] : "#00C8FF"); } catch (Exception) { }
            _buttonStyle = _comboStyle = _comboItemStyle = _checkStyle = _radioStyle = _sliderStyle = _textStyle = null;
            _enabled = dark;                       // only the dark palette is mapped; a light Opsis theme keeps the original light windows
            if (dark && !_handlerRegistered)
            {
                _handlerRegistered = true;
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
            }
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString("#" + s.TrimStart('#'));

        private static bool IsFenceOrOverlay(Window w) =>
            w is NonActivatingWindow || w is SmartToast || w is DrawFenceOverlay || w is FenceFocusFormManager
            || w.GetType().Name.Contains("WaitWindow") || w.Title == "" && w.WindowStyle == WindowStyle.None && w.AllowsTransparency && w.Width < 400 && w.Height < 120;

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window w || IsFenceOrOverlay(w) || !ReferenceEquals(e.OriginalSource, w)) return;
            if (!_enabled && !Loc.Greek) return;
            void Pass() { try { if (_enabled) Recolor(w); if (Loc.Greek) TranslateTree(w); } catch (Exception ex) { Log(ex); } }
            Pass();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };   // content built later (tabs, lists) gets the same treatment
            timer.Tick += (_, _) => { if (w.IsVisible) Pass(); else timer.Stop(); };
            timer.Start();
            w.Closed += (_, _) => timer.Stop();
        }

        private static void Log(Exception ex)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(DataRoot.Dir, "opsis_theme.log"), DateTime.Now + " " + ex + Environment.NewLine); } catch { }
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
            if (l < 0.5 && Sat(s.Color) > 0.5) { var ab = new SolidColorBrush(Color.FromArgb(s.Color.A, _accent.R, _accent.G, _accent.B)); ab.Freeze(); return ab; }   // coloured section headings follow the Opsis accent
            if (l >= 0.5) return b;
            var n = l < 0.25 ? _text : _text2;
            var nb = new SolidColorBrush(Color.FromArgb(s.Color.A, n.R, n.G, n.B)); nb.Freeze(); return nb;
        }

        private static Brush? MapBorder(Brush? b)
        {
            if (b is not SolidColorBrush s || s.Color.A < 16 || Lum(s.Color) < 0.55) return b;
            var nb = new SolidColorBrush(Color.FromArgb(s.Color.A, _border.R, _border.G, _border.B)); nb.Freeze(); return nb;
        }

        // Opsis look for the borderless settings windows: rounded outer frame with a thin Opsis border (like the Opsis dialogs) and the
        // Opsis font, instead of the upstream square white card.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> _chromed = new System.Runtime.CompilerServices.ConditionalWeakTable<Window, object>();

        private static void ApplyChrome(Window w)
        {
            if (_chromed.TryGetValue(w, out _)) return;
            _chromed.Add(w, new object());
            w.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            if (w.AllowsTransparency && w.Content is Border b)
            {
                w.Background = Brushes.Transparent;
                b.CornerRadius = new CornerRadius(12);
                b.BorderThickness = new Thickness(1);
                b.BorderBrush = new SolidColorBrush(_border);
                void Clip() { if (b.ActualWidth > 0 && b.ActualHeight > 0) b.Clip = new RectangleGeometry(new Rect(0, 0, b.ActualWidth, b.ActualHeight), 12, 12); }
                b.SizeChanged += (_, _) => Clip();
                Clip();
            }
        }

        private static void Set(DependencyObject d, DependencyProperty prop, Brush? current, Brush? wanted)
        {
            if (!ReferenceEquals(current, wanted)) d.SetValue(prop, wanted);
        }

        // Strings that the engine builds without going through Loc.T (labels created by helpers) are translated here, by exact dictionary match only.
        private static void TranslateTree(DependencyObject root)
        {
            if (!(root is System.Windows.Media.Visual)) return;
            if (root is Window win) { var t = Loc.Translate(win.Title); if (t != win.Title) win.Title = t; }
            if (root is TextBlock tb) { if (tb.Inlines.Count <= 1) { var t = Loc.Translate(tb.Text); if (t != tb.Text) tb.Text = t; } }
            else if (root is ContentControl cc && cc.Content is string s) { var t = Loc.Translate(s); if (t != s) cc.Content = t; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child;
                try { child = VisualTreeHelper.GetChild(root, i); } catch (ArgumentOutOfRangeException) { break; }
                TranslateTree(child);
            }
        }

        private static void Recolor(DependencyObject root)
        {
            if (!(root is System.Windows.Media.Visual) && !(root is System.Windows.Media.Media3D.Visual3D)) return;
            switch (root)
            {
                // Only write a property when the colour really changes: an unconditional assignment would create a LOCAL value that beats the
                // triggers inside our control templates (checked fill, hover ring...).
                case Window w: Set(w, Window.BackgroundProperty, w.Background, MapBack(w.Background)); ApplyChrome(w); break;
                case Border b: Set(b, Border.BackgroundProperty, b.Background, MapBack(b.Background)); Set(b, Border.BorderBrushProperty, b.BorderBrush, MapBorder(b.BorderBrush)); break;
                case Panel p: Set(p, Panel.BackgroundProperty, p.Background, MapBack(p.Background)); break;
                case TextBlock t: Set(t, TextBlock.ForegroundProperty, t.Foreground, MapFore(t.Foreground)); Set(t, TextBlock.BackgroundProperty, t.Background, MapBack(t.Background)); break;
                case Control c:
                    Set(c, Control.BackgroundProperty, c.Background, MapBack(c.Background)); Set(c, Control.ForegroundProperty, c.Foreground, MapFore(c.Foreground)); Set(c, Control.BorderBrushProperty, c.BorderBrush, MapBorder(c.BorderBrush));
                    break;
                case Shape sh: Set(sh, Shape.StrokeProperty, sh.Stroke, MapBorder(sh.Stroke)); break;
            }
            if (root is ComboBox combo && combo.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue && !ReferenceEquals(combo.Style, ComboStyle))
            {
                combo.Background = new SolidColorBrush(_hover);        // the stock gradient background is not a solid brush, so the colour map skips it
                combo.BorderBrush = new SolidColorBrush(_border);
                combo.Foreground = new SolidColorBrush(_text);
                combo.Style = ComboStyle;
                combo.ItemContainerStyle = ComboItemStyle;
            }
            if (root is Button sb && sb.Background is SolidColorBrush sbb && Sat(sbb.Color) > 0.45 && sbb.Color.A > 40)
            {
                var bc = sbb.Color;
                bool danger = bc.G < bc.R * 0.45 && bc.B < bc.R * 0.6;                 // red stays red (Clear All Data...)
                if (!danger)
                {
                    sb.Background = new SolidColorBrush(_hover);                    // the rainbow of green/blue/orange buttons becomes the Opsis button look
                    sb.BorderBrush = new SolidColorBrush(_accent);
                    sb.Foreground = new SolidColorBrush(_text);
                }
            }
            if (root is CheckBox cbx && cbx.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue) cbx.Style = CheckStyle;
            else if (root is RadioButton rdo && rdo.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue) rdo.Style = RadioStyle;
            else if (root is Slider sld && sld.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue && sld.Orientation == Orientation.Horizontal) sld.Style = SliderStyle;
            else if (root is TextBox txt && txt.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue && !(txt.TemplatedParent is ComboBox)) txt.Style = TextStyle;
            if (root is Button button && button.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue && !ReferenceEquals(button.Style, ButtonStyle))
                button.Style = ButtonStyle;
            // applying a style re-creates the template, so the child count is read only now and re-checked on every step
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child;
                try { child = VisualTreeHelper.GetChild(root, i); } catch (ArgumentOutOfRangeException) { break; }
                Recolor(child);
            }
        }

        private static Style? _comboStyle, _comboItemStyle;
        private static Style ComboStyle => _comboStyle ??= (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>
  <Setter Property='Foreground' Value='#TEXT'/>
  <Setter Property='Background' Value='#HOVER'/>
  <Setter Property='BorderBrush' Value='#BORDER'/>
  <Setter Property='Padding' Value='4'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ComboBox'>
        <Grid>
          <ToggleButton ClickMode='Press' Focusable='False' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
            <ToggleButton.Template>
              <ControlTemplate TargetType='ToggleButton'>
                <Border Background='{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}' BorderBrush='{Binding BorderBrush, RelativeSource={RelativeSource AncestorType=ComboBox}}' BorderThickness='1' CornerRadius='6'>
                  <Path HorizontalAlignment='Right' Margin='0,0,9,0' VerticalAlignment='Center' Data='M 0 0 L 4 4 L 8 0' Stroke='#MUTED' StrokeThickness='1.5'/>
                </Border>
              </ControlTemplate>
            </ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='9,2,26,2' VerticalAlignment='Center' HorizontalAlignment='Left'/>
          <Popup IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
            <Border MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='260' Background='#SURFACE' BorderBrush='#BORDER' BorderThickness='1' CornerRadius='6' Margin='0,2,0,0' Padding='3'>
              <ScrollViewer><StackPanel IsItemsHost='True'/></ScrollViewer>
            </Border>
          </Popup>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>".Replace("#TEXT", ToHex(_text)).Replace("#HOVER", ToHex(_hover)).Replace("#BORDER", ToHex(_border)).Replace("#MUTED", ToHex(_muted)).Replace("#SURFACE", ToHex(_surface)));

        private static Style ComboItemStyle => _comboItemStyle ??= (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBoxItem'>
  <Setter Property='Foreground' Value='#TEXT'/>
  <Setter Property='Padding' Value='8,4'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ComboBoxItem'>
        <Border x:Name='bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}'>
          <ContentPresenter/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='#HOVER'/></Trigger>
          <Trigger Property='IsSelected' Value='True'><Setter TargetName='bd' Property='Background' Value='#HOVER'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>".Replace("#TEXT", ToHex(_text)).Replace("#HOVER", ToHex(_hover)));

        // ---- Opsis-style CheckBox / RadioButton / Slider / TextBox (the upstream controls are stock WPF) ----
        private static Style Build(string xaml) => (Style)XamlReader.Parse(xaml
            .Replace("#TEXT", ToHex(_text)).Replace("#MUTED", ToHex(_muted)).Replace("#ACCENT", ToHex(_accent))
            .Replace("#BORDER", ToHex(_border)).Replace("#HOVER", ToHex(_hover)).Replace("#SURFACE", ToHex(_surface)));

        private const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

        private static Style? _checkStyle, _radioStyle, _sliderStyle, _textStyle;

        private static Style CheckStyle => _checkStyle ??= Build(@"<Style " + Ns + @" TargetType='CheckBox'>
  <Setter Property='Foreground' Value='#TEXT'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <StackPanel Orientation='Horizontal' Background='Transparent'>
      <Border x:Name='bx' Width='18' Height='18' CornerRadius='5' BorderThickness='1.5' BorderBrush='#MUTED' Background='Transparent' VerticalAlignment='Center'>
        <Path x:Name='ck' Data='M 3.5 9 L 7 12.5 L 14 4.5' Stroke='#0B0B0C' StrokeThickness='2' Visibility='Collapsed' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
      </Border>
      <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </StackPanel>
    <ControlTemplate.Triggers>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='bx' Property='Background' Value='#ACCENT'/><Setter TargetName='bx' Property='BorderBrush' Value='#ACCENT'/><Setter TargetName='ck' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bx' Property='BorderBrush' Value='#ACCENT'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");

        private static Style RadioStyle => _radioStyle ??= Build(@"<Style " + Ns + @" TargetType='RadioButton'>
  <Setter Property='Foreground' Value='#TEXT'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RadioButton'>
    <StackPanel Orientation='Horizontal' Background='Transparent'>
      <Grid Width='18' Height='18' VerticalAlignment='Center'>
        <Ellipse x:Name='ring' Stroke='#MUTED' StrokeThickness='1.5' Fill='Transparent'/>
        <Ellipse x:Name='dot' Width='8' Height='8' Fill='#ACCENT' Visibility='Collapsed'/>
      </Grid>
      <ContentPresenter Margin='7,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </StackPanel>
    <ControlTemplate.Triggers>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='dot' Property='Visibility' Value='Visible'/><Setter TargetName='ring' Property='Stroke' Value='#ACCENT'/></Trigger>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ring' Property='Stroke' Value='#ACCENT'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");

        private static Style SliderStyle => _sliderStyle ??= Build(@"<Style " + Ns + @" TargetType='Slider'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
    <Grid Height='20' Background='Transparent'>
      <Border Height='4' CornerRadius='2' Background='#BORDER' VerticalAlignment='Center'/>
      <Track x:Name='PART_Track'>
        <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' Background='#ACCENT' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb Width='16' Height='16' Focusable='False'><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='#TEXT' Stroke='#ACCENT' StrokeThickness='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
      </Track>
    </Grid>
  </ControlTemplate></Setter.Value></Setter>
</Style>");

        private static Style TextStyle => _textStyle ??= Build(@"<Style " + Ns + @" TargetType='TextBox'>
  <Setter Property='Foreground' Value='#TEXT'/><Setter Property='Background' Value='#HOVER'/><Setter Property='BorderBrush' Value='#BORDER'/>
  <Setter Property='CaretBrush' Value='#TEXT'/><Setter Property='SelectionBrush' Value='#ACCENT'/><Setter Property='Padding' Value='6,4'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
    <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
      <ScrollViewer x:Name='PART_ContentHost'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#ACCENT'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");

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
