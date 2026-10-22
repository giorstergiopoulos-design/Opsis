using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public class ThemePackageTextTests
{
    private static readonly KeyValuePair<string, string>[] NoCursors = Array.Empty<KeyValuePair<string, string>>();

    [Fact]
    public void A_dark_theme_with_an_accent_records_both_so_applying_it_does_not_reset_the_colours()
    {
        string text = ThemePackageEngine.BuildThemeText("Night", @"C:\w\bg.png", NoCursors, null, dark: true, colorizationArgb: unchecked((int)0xC40078D4));
        Assert.Contains("[VisualStyles]", text);
        Assert.Contains("SystemMode=Dark", text);
        Assert.Contains("AppMode=Dark", text);
        Assert.Contains("AutoColorization=0", text);
        Assert.Contains("ColorizationColor=0xC40078D4", text);
    }

    [Fact]
    public void A_light_theme_records_light()
    {
        string text = ThemePackageEngine.BuildThemeText("Day", null, NoCursors, null, dark: false, colorizationArgb: null);
        Assert.Contains("SystemMode=Light", text);
        Assert.Contains("AppMode=Light", text);
        Assert.DoesNotContain("ColorizationColor", text);
        Assert.DoesNotContain("AutoColorization", text);
    }

    [Fact]
    public void Unknown_state_writes_neither_mode_nor_colour_instead_of_guessing()
    {
        string text = ThemePackageEngine.BuildThemeText("X", null, NoCursors, null, dark: null, colorizationArgb: null);
        Assert.DoesNotContain("SystemMode", text);
        Assert.DoesNotContain("AppMode", text);
    }

    [Fact]
    public void Wallpaper_cursors_and_sounds_are_still_written()
    {
        var cursors = new[] { new KeyValuePair<string, string>("Arrow", @"C:\Cursors\a.cur"), new KeyValuePair<string, string>("Hand", @"C:\Cursors\h.cur") };
        string text = ThemePackageEngine.BuildThemeText("Full", @"C:\w\bg.png", cursors, "Windows Default", true, null);
        Assert.Contains(@"Wallpaper=C:\w\bg.png", text);
        Assert.Contains(@"[Control Panel\Cursors]", text);
        Assert.Contains(@"Arrow=C:\Cursors\a.cur", text);
        Assert.Contains("SchemeName=Windows Default", text);
        Assert.Contains(@"Path=%SystemRoot%\resources\Themes\aero\aero.msstyles", text);
    }

    [Fact]
    public void A_display_name_cannot_inject_extra_ini_lines()
    {
        string text = ThemePackageEngine.BuildThemeText("Evil\r\n[VisualStyles]\r\nPath=C:\\evil.msstyles", null, NoCursors, null, null, null);
        Assert.Equal(1, text.Split('\n').Count(l => l.Trim() == "[VisualStyles]"));
        Assert.Equal(0, text.Split('\n').Count(l => l.StartsWith("Path=C:\\evil")));
    }
}
