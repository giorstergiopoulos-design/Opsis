using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void ReadAllText_RecoversFromCorruptedFile_UsingBackup()
    {
        string dir = Path.Combine(Path.GetTempPath(), "OpsisTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            AtomicFile.WriteAllText(path, "{\"a\":1}");
            AtomicFile.WriteAllText(path, "{\"a\":2}"); // second write creates the .bak from the first (valid) write
            File.WriteAllText(path, "{ not valid json");            // simulate a crash mid-write

            string recovered = AtomicFile.ReadAllText(path);

            Assert.Contains("\"a\":1", recovered);
            Assert.True(File.Exists(path + ".bak"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadAllText_ValidFile_ReturnsItsOwnContent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "OpsisTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            AtomicFile.WriteAllText(path, "{\"a\":42}");
            Assert.Equal("{\"a\":42}", AtomicFile.ReadAllText(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
