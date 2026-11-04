using MotionDesk.UI;
using Xunit;

namespace Opsis.Tests;

public sealed class PageSearchTests
{
    [Theory]
    [InlineData("Start with Windows", "windows", true)]
    [InlineData("Start with Windows", "START WIN", true)]
    [InlineData("Εκκίνηση με τα Windows", "εκκινηση", true)]       // accents are ignored
    [InlineData("Größe des Rasters", "grosse", false)]            // ß is not folded, only combining marks
    [InlineData("Größe des Rasters", "groe", false)]
    [InlineData("Größe des Rasters", "gro", true)]
    [InlineData("Download && install", "download install", true)]  // the & mnemonic marker is ignored
    [InlineData("Grid size", "", true)]
    [InlineData("Grid size", "wallpaper", false)]
    public void MatchesWordsIgnoringCaseAndAccents(string text, string query, bool expected) =>
        Assert.Equal(expected, PageSearch.Matches(text, query));

    [Fact]
    public void EveryWordMustOccur() => Assert.False(PageSearch.Matches("Grid size", "grid colour"));
}

public sealed class PlaylistSuffixTests
{
    [Fact]
    public void ShowsActiveOverTotalOnlyWhenSomeVideosAreOff()
    {
        Assert.Equal("3 video(s)", MotionDesk.UI.MainWindow.ActivePlaylistSuffix(3, 3, "3 video(s)"));
        Assert.Equal("1 video(s)  [1/3]", MotionDesk.UI.MainWindow.ActivePlaylistSuffix(1, 3, "1 video(s)"));
        Assert.Equal("0 video(s)  [0/2]", MotionDesk.UI.MainWindow.ActivePlaylistSuffix(0, 2, "0 video(s)"));
    }
}

public sealed class StatusMessageTests
{
    [Fact]
    public void LabelsAreCleanedOfMnemonicsAndColons()
    {
        Assert.Equal("Grid size", MotionDesk.UI.MainWindow.CleanLabel("&Grid size:"));
        Assert.Equal("Start with Windows", MotionDesk.UI.MainWindow.CleanLabel("  Start with Windows "));
    }

    [Fact]
    public void ApplySettingRunsTheChangeAndNeverThrowsIntoTheUi()
    {
        bool ran = false;
        MotionDesk.UI.MainWindow.ApplySetting("x", () => ran = true);
        Assert.True(ran);
        MotionDesk.UI.MainWindow.ApplySetting("y", () => throw new InvalidOperationException("disk full"));     // reported in the status bar, not thrown
    }
}

public sealed class CommonStringsAdoptionTests
{
    [Fact]
    public void OpsisUsesTheSharedTranslationsInEveryLanguage()
    {
        string[] keys = { "ui.reset", "ui.restartNeeded", "ui.searchPlaceholder", "ui.noMatch", "ui.saved", "ui.failed", "ui.auto", "ui.low", "ui.balanced", "ui.high", "ui.gotIt", "ui.open", "ui.download", "ui.familyTitle", "ui.welcomeTitle", "ui.pending.plugins" };
        var before = global::Opsis.UI.OpsisStrings.Language;
        try
        {
            foreach (var lang in global::Opsis.UI.OpsisStrings.SupportedLanguages)
            {
                global::Opsis.UI.OpsisStrings.Language = lang;
                foreach (var k in keys) Assert.NotEqual(k, global::Opsis.UI.OpsisStrings.T(k));       // never the raw key
            }
        }
        finally { global::Opsis.UI.OpsisStrings.Language = before; }
    }
}
