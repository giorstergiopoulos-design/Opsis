using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public sealed class AnalogClockLayoutTests
{
    [Theory]
    [InlineData(200, 200)]
    [InlineData(320, 180)]
    [InlineData(150, 300)]
    [InlineData(260, 260)]
    public void FaceIsCentredAndClearOfTheDateStripOnBothSides(int w, int h)
    {
        int size = AnalogClockControl.FaceSize(w, h, 22);
        Assert.InRange(size, 20, w);
        Assert.True((h - size) / 2.0 >= 22, "the free space above and below the face must be at least the date strip height");
        Assert.True((w - size) / 2.0 >= 6);
    }
}
