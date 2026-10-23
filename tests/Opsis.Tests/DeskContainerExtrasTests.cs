using System.Drawing;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

// Chameleon (ιδέα από το DesktopFences, MIT): κυρίαρχο χρώμα εικόνας.
public sealed class DeskContainerExtrasTests
{
    private static string Png(Color fill, Color? second = null, double secondShare = 0)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opsis-chameleon-{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(fill);
            if (second != null) { using var b = new SolidBrush(second.Value); g.FillRectangle(b, 0, 0, 64, (int)(64 * secondShare)); }
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public void SaturatedImage_ReturnsThatHue()
    {
        var path = Png(Color.FromArgb(200, 40, 40));
        try
        {
            var c = DeskContainerExtras.DominantColor(path);
            Assert.NotNull(c);
            Assert.True(c!.Value.R > c.Value.G + 100 && c.Value.R > c.Value.B + 100, $"expected red-ish, got {c}");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GrayImage_HasNoDominantColor()
    {
        var path = Png(Color.FromArgb(128, 128, 128));
        try { Assert.Null(DeskContainerExtras.DominantColor(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LargerAreaWins()
    {
        var path = Png(Color.FromArgb(40, 80, 200), Color.FromArgb(200, 40, 40), 0.25);
        try
        {
            var c = DeskContainerExtras.DominantColor(path);
            Assert.NotNull(c);
            Assert.True(c!.Value.B > c.Value.R + 80, $"expected blue-ish, got {c}");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingFile_ReturnsNull() =>
        Assert.Null(DeskContainerExtras.DominantColor(Path.Combine(Path.GetTempPath(), "does-not-exist-opsis.png")));
}
