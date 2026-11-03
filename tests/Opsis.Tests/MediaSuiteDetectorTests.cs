using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class MediaSuiteDetectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "opsis_ms_" + Guid.NewGuid().ToString("N")[..8]);
    public MediaSuiteDetectorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void NothingInstalledIsFalse() => Assert.False(MediaSuiteDetector.Decide(new[] { Path.Combine(_dir, "none") }, new[] { "Some App", "Another" }));

    [Fact]
    public void RegistryNameOfWaveframeOrMediaSuiteCounts()
    {
        Assert.True(MediaSuiteDetector.Decide(Array.Empty<string>(), new[] { "Waveframe 1.9.1" }));
        Assert.True(MediaSuiteDetector.Decide(Array.Empty<string>(), new[] { "opsis media suite" }));
        Assert.False(MediaSuiteDetector.Decide(Array.Empty<string>(), new[] { "Opsis - Desktop Customization" }));
    }

    [Fact]
    public void FolderCountsOnlyWithAnExeInside()
    {
        var empty = Path.Combine(_dir, "Opsis Media Suite"); Directory.CreateDirectory(empty);
        Assert.False(MediaSuiteDetector.Decide(new[] { empty }, Array.Empty<string>()));
        File.WriteAllText(Path.Combine(empty, "Waveframe.exe"), "x");
        Assert.True(MediaSuiteDetector.Decide(new[] { empty }, Array.Empty<string>()));
    }
}

public sealed class PluginUiStyleTests
{
    [Theory]
    [InlineData("Appearance", "Appearance")]
    [InlineData("crawler", "Crawler")]
    [InlineData("opsis", "Opsis")]
    [InlineData("", "Opsis")]
    [InlineData(null, "Opsis")]
    [InlineData("nonsense", "Opsis")]
    public void NormalizeFallsBackToOpsis(string? input, string expected) => Assert.Equal(expected, MotionDesk.Services.PluginUiStyle.Normalize(input));
}
