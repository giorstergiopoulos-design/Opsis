using System.Windows;
using System.Windows.Markup;

namespace Desktop_Fences
{
    // Opsis integration: replaces the classic Windows look of every ContextMenu / MenuItem / Separator (heart menu, fence and icon
    // menus, tab menus...) with the Opsis style: rounded dark surface, accent hover, thin separators. Colours come from OpsisTheme.
    internal static class OpsisMenuStyles
    {
        public static void Apply(string surface, string hover, string border, string text, string muted, string accent)
        {
            if (Application.Current == null) return;
            string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Foreground' Value='#TEXT'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='#SURFACE' BorderBrush='#BORDER' BorderThickness='1' CornerRadius='10' Padding='5' Margin='6'>
            <Border.Effect><DropShadowEffect Color='Black' BlurRadius='14' ShadowDepth='2' Opacity='0.45'/></Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='10,4' Background='#BORDER'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='#TEXT'/>
    <Setter Property='Padding' Value='8,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Grid>
            <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='22'/>
                  <ColumnDefinition Width='*' MinWidth='110'/>
                  <ColumnDefinition Width='Auto'/>
                  <ColumnDefinition Width='18'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter x:Name='ic' Grid.Column='0' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Center' Width='16' Height='16'/>
                <Path x:Name='ck' Grid.Column='0' Visibility='Collapsed' VerticalAlignment='Center' HorizontalAlignment='Center' Data='M 0 5 L 4 9 L 11 0' Stroke='#ACCENT' StrokeThickness='2'/>
                <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center' Margin='4,0,12,0'/>
                <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='#MUTED' VerticalAlignment='Center' Margin='12,0,0,0'/>
                <Path x:Name='ar' Grid.Column='3' Visibility='Collapsed' VerticalAlignment='Center' HorizontalAlignment='Right' Data='M 0 0 L 4 4 L 0 8' Stroke='#MUTED' StrokeThickness='1.5'/>
              </Grid>
            </Border>
            <Popup x:Name='pp' Placement='Right' HorizontalOffset='-2' VerticalOffset='-6' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
              <Border Background='#SURFACE' BorderBrush='#BORDER' BorderThickness='1' CornerRadius='10' Padding='5' Margin='6'>
                <Border.Effect><DropShadowEffect Color='Black' BlurRadius='14' ShadowDepth='2' Opacity='0.45'/></Border.Effect>
                <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='Icon' Value='{x:Null}'><Setter TargetName='ic' Property='Visibility' Value='Collapsed'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='ck' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='HasItems' Value='True'><Setter TargetName='ar' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='#HOVER'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
            xaml = xaml.Replace("#SURFACE", surface).Replace("#HOVER", hover).Replace("#BORDER", border)
                       .Replace("#TEXT", text).Replace("#MUTED", muted).Replace("#ACCENT", accent);
            var dict = (ResourceDictionary)XamlReader.Parse(xaml);
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }
    }
}
