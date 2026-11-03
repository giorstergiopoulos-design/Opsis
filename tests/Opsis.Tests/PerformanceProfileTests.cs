using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class PerformanceProfileTests
{
    [Fact]
    public void TheTableGetsRicherFromLowToHigh()
    {
        var low = PerformanceProfile.For(HardwareTier.Low);
        var mid = PerformanceProfile.For(HardwareTier.Balanced);
        var high = PerformanceProfile.For(HardwareTier.High);
        Assert.True(low.WallpaperFps < mid.WallpaperFps && mid.WallpaperFps < high.WallpaperFps);
        Assert.True(low.RenderScale < mid.RenderScale && mid.RenderScale <= high.RenderScale);
        Assert.True(low.WaveLayers < mid.WaveLayers && mid.WaveLayers < high.WaveLayers);
        Assert.True(low.RefreshScale > mid.RefreshScale && mid.RefreshScale >= high.RefreshScale);   // weaker tier = slower refresh
        Assert.True(low.MaxWebViews <= mid.MaxWebViews && mid.MaxWebViews <= high.MaxWebViews);
        Assert.False(low.BlurEffects); Assert.False(low.Animations); Assert.False(low.Allow3D);       // Low = plain and static
        Assert.True(high.BlurEffects && high.Animations && high.Allow3D);
    }

    [Theory]                        // simulated machines -> tier (the classification the profile system starts from)
    [InlineData(4000, 2, false, HardwareTier.Low)]       // old netbook-class laptop
    [InlineData(8000, 4, false, HardwareTier.Low)]       // 300-400 EUR laptop with integrated graphics
    [InlineData(16000, 8, false, HardwareTier.Balanced)]
    [InlineData(16000, 8, true, HardwareTier.High)]
    [InlineData(32000, 16, true, HardwareTier.High)]
    public void SimulatedMachinesGetTheExpectedTier(long ram, int cores, bool dgpu, HardwareTier expected) =>
        Assert.Equal(expected, HardwareProfileService.Classify(ram, cores, dgpu));

    [Fact]
    public void LowerNeverGoesBelowLow()
    {
        Assert.Equal(HardwareTier.Balanced, PerformanceGovernor.Lower(HardwareTier.High, 1));
        Assert.Equal(HardwareTier.Low, PerformanceGovernor.Lower(HardwareTier.High, 2));
        Assert.Equal(HardwareTier.Low, PerformanceGovernor.Lower(HardwareTier.Low, 5));
        Assert.Equal(HardwareTier.High, PerformanceGovernor.Lower(HardwareTier.High, 0));
    }

    private static readonly LoadSample Busy = new(95, null, 50, false, false);
    private static readonly LoadSample Calm = new(20, null, 40, false, false);
    private static readonly LoadSample Middle = new(70, null, 50, false, false);     // neither pressure nor calm

    private static int Run(int drop, HardwareTier tier, LoadSample s, double seconds, double stepSeconds = 2)
    {
        DateTime? pressure = null, calm = null; var t0 = new DateTime(2026, 1, 1);
        for (double t = 0; t <= seconds; t += stepSeconds) drop = PerformanceGovernor.Step(drop, tier, s, t0.AddSeconds(t), ref pressure, ref calm);
        return drop;
    }

    [Fact]
    public void ShortSpikesDoNotDegrade() => Assert.Equal(0, Run(0, HardwareTier.High, Busy, 8));

    [Fact]
    public void TenSecondsOfPressureLowersOneStep() => Assert.Equal(1, Run(0, HardwareTier.High, Busy, 11));

    [Fact]
    public void LongPressureKeepsLoweringButStopsAtLow()
    {
        Assert.Equal(2, Run(0, HardwareTier.High, Busy, 120));
        Assert.Equal(0, Run(0, HardwareTier.Low, Busy, 120));            // already Low: nothing to lower
        Assert.Equal(1, Run(0, HardwareTier.Balanced, Busy, 120));
    }

    [Fact]
    public void RestoresOnlyAfterALongCalmAndOneStepAtATime()
    {
        Assert.Equal(2, Run(2, HardwareTier.High, Calm, 40));            // 40 s of calm is not enough (needs 45 s)
        Assert.Equal(1, Run(2, HardwareTier.High, Calm, 50));
        Assert.Equal(0, Run(2, HardwareTier.High, Calm, 100));
    }

    [Fact]
    public void MiddleLoadHoldsTheCurrentLevelAndResetsTheTimers()
    {
        Assert.Equal(1, Run(1, HardwareTier.High, Middle, 300));
        // pressure that is interrupted by a middle sample must start over: 8 s busy, 2 s middle, 8 s busy -> no step
        DateTime? p = null, c = null; var t0 = new DateTime(2026, 1, 1); int drop = 0;
        for (int t = 0; t <= 8; t += 2) drop = PerformanceGovernor.Step(drop, HardwareTier.High, Busy, t0.AddSeconds(t), ref p, ref c);
        drop = PerformanceGovernor.Step(drop, HardwareTier.High, Middle, t0.AddSeconds(10), ref p, ref c);
        for (int t = 12; t <= 20; t += 2) drop = PerformanceGovernor.Step(drop, HardwareTier.High, Busy, t0.AddSeconds(t), ref p, ref c);
        Assert.Equal(0, drop);
    }

    [Fact]
    public void LowBatteryCountsAsPressureAndBlocksRestoring()
    {
        Assert.True(PerformanceGovernor.IsPressure(new LoadSample(10, null, 10, true, true)));
        Assert.False(PerformanceGovernor.IsCalm(new LoadSample(10, null, 10, true, true)));
    }
}
