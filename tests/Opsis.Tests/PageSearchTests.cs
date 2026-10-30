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
