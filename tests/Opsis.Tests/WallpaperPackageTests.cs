using System.IO.Compression;
using MotionDesk.Services;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public class WallpaperPackageTests
{
    private static string Temp() { string d = Path.Combine(Path.GetTempPath(), "opsis_wp_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    private static string MakeZip(string dir, params (string Name, string Body)[] files)
    {
        string zip = Path.Combine(dir, "pack.zip");
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, body) in files)
        {
            var e = z.CreateEntry(name);
            using var w = new StreamWriter(e.Open()); w.Write(body);
        }
        return zip;
    }

    [Fact]
    public void Imports_a_package_with_index_at_the_root()
    {
        string dir = Temp();
        try
        {
            string zip = MakeZip(dir, ("index.html", "<html></html>"), ("opsis-wallpaper.json", "{\"name\":\"X\"}"), ("img/a.txt", "a"));
            string index = WallpaperPackages.Import(zip, Path.Combine(dir, "out"));
            Assert.True(File.Exists(index));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(index)!, "img", "a.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Imports_a_package_packed_inside_one_folder()
    {
        string dir = Temp();
        try
        {
            string zip = MakeZip(dir, ("My Wall/index.html", "<html></html>"), ("My Wall/js/app.js", "1"));
            string index = WallpaperPackages.Import(zip, Path.Combine(dir, "out"));
            Assert.Equal("index.html", Path.GetFileName(index));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(index)!, "js", "app.js")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Refuses_packages_without_index_or_with_paths_that_escape_the_folder()
    {
        string dir = Temp();
        try
        {
            Assert.Throws<InvalidDataException>(() => WallpaperPackages.Import(MakeZip(dir, ("readme.txt", "x")), Path.Combine(dir, "out")));
            string evil = Path.Combine(dir, "evil.zip");
            using (var z = ZipFile.Open(evil, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("index.html").Open())) w.Write("<html></html>");
                using (var w = new StreamWriter(z.CreateEntry("../../escaped.txt").Open())) w.Write("x");
            }
            Assert.Throws<InvalidDataException>(() => WallpaperPackages.Import(evil, Path.Combine(dir, "out2")));
            Assert.False(File.Exists(Path.Combine(dir, "escaped.txt")));
            Assert.False(Directory.Exists(Path.Combine(dir, "out2", "evil")));      // a failed import leaves nothing behind
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Export_then_import_round_trips()
    {
        string dir = Temp();
        try
        {
            string src = Path.Combine(dir, "src"); Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "index.html"), "<html>hi</html>");
            string zip = Path.Combine(dir, "wall.zip");
            WallpaperPackages.Export(Path.Combine(src, "index.html"), zip);
            string index = WallpaperPackages.Import(zip, Path.Combine(dir, "out"));
            Assert.Equal("<html>hi</html>", File.ReadAllText(index));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Manifest_values_are_typed_and_fall_back_to_defaults()
    {
        string dir = Temp();
        try
        {
            string index = Path.Combine(dir, "index.html"); File.WriteAllText(index, "<html></html>");
            File.WriteAllText(Path.Combine(dir, WallpaperManifest.FileName), """
                { "name": "T", "properties": [
                  { "key": "a", "type": "slider", "min": 1, "max": 10, "default": 4 },
                  { "key": "b", "type": "checkbox", "default": true },
                  { "key": "c", "type": "color", "default": "#112233" },
                  { "key": "", "type": "text" } ] }
                """);
            var m = WallpaperManifest.LoadFor(index)!;
            Assert.Equal(3, m.Properties.Count);                                  // the property without a key is dropped
            var s = new WallpaperSettings();
            Assert.Equal("{\"a\":4,\"b\":true,\"c\":\"#112233\"}", WallpaperManifest.ValuesJson(s, index, m));
            WallpaperManifest.SetValue(s, index, "a", "7.5"); WallpaperManifest.SetValue(s, index, "b", "false");
            Assert.Equal("{\"a\":7.5,\"b\":false,\"c\":\"#112233\"}", WallpaperManifest.ValuesJson(s, index, m));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Elements_catalog_is_merged_with_what_the_user_saved()
    {
        var s = new WallpaperSettings();
        s.Elements.Add(new WallpaperElement { Id = "cpu", Enabled = true, X = 33, Y = 44, Scale = 120 });
        s.Elements.Add(new WallpaperElement { Id = "removed-long-ago", Enabled = true });
        s.EnsureElements();
        Assert.Equal(WallpaperElement.Catalog.Length, s.Elements.Count);
        Assert.DoesNotContain(s.Elements, e => e.Id == "removed-long-ago");
        var cpu = s.Elements.First(e => e.Id == "cpu");
        Assert.True(cpu.Enabled); Assert.Equal(33, cpu.X); Assert.Equal(120, cpu.Scale);
        Assert.All(s.Elements.Where(e => e.Id != "cpu"), e => Assert.False(e.Enabled));
    }

    [Fact]
    public void Media_sources_are_recognised()
    {
        Assert.Contains("youtube-nocookie.com/embed/dQw4w9WgXcQ", MediaSource.PageUri("https://youtu.be/dQw4w9WgXcQ"));
        Assert.Contains("embed/dQw4w9WgXcQ", MediaSource.PageUri("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=5"));
        Assert.Equal("https://example.com/clock", MediaSource.PageUri("example.com/clock"));
        Assert.Equal("", MediaSource.PageUri("not a url"));
        Assert.Equal("", MediaSource.PageUri(""));
        Assert.Null(MediaSource.LocalPagePath("https://example.com"));
        Assert.Equal("", MediaSource.ImageUri(@"C:\no\such\file.gif"));
    }
}
