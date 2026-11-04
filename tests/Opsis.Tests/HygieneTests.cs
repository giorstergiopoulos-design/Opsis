using System.Diagnostics;
using MotionDesk.Services;
using MotionDesk.UI;
using Xunit;

namespace Opsis.Tests;

// Release hygiene: no personal data or local build paths inside the binaries or the tracked text files, the same publisher information everywhere, the standard MIT licence,
// Inno Setup 7, and an updater that does not run hidden scripts.
public sealed class HygieneTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !(File.Exists(Path.Combine(d.FullName, "installer.iss")) && Directory.Exists(Path.Combine(d.FullName, "src")))) d = d.Parent;
        return (d ?? new DirectoryInfo(@"C:\w\Opsis")).FullName;
    }

    private static readonly string B = new string((char)92, 1);
    private static string[] Forbidden() => new[] { "C:" + B + "Users" + B, "C:" + B + "w" + B, "gstrj", "LOGISTIRIO", "giorstergiopoulos@gmail", "/Users/" };

    private static List<string> Hits(string file)
    {
        var bytes = File.ReadAllBytes(file);
        var ascii = System.Text.Encoding.Latin1.GetString(bytes);
        var utf16 = System.Text.Encoding.Unicode.GetString(bytes);
        var hits = new List<string>();
        foreach (var p in Forbidden())
            if (ascii.Contains(p, StringComparison.OrdinalIgnoreCase) || utf16.Contains(p, StringComparison.OrdinalIgnoreCase)) hits.Add(Path.GetFileName(file) + " contains '" + p + "'");
        return hits;
    }

    [Fact]
    public void TheAppAndTheLibrariesItShipsCarryNoPersonalDataOrBuildPaths()
    {
        var files = new List<string> { typeof(MainWindow).Assembly.Location };
        foreach (var f in Directory.GetFiles(Path.Combine(Root(), "libs"), "*.dll")) files.Add(f);
        var hits = files.Where(File.Exists).SelectMany(Hits).ToList();
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void TrackedTextFilesCarryNoPersonalPaths()
    {
        var root = Root();
        var files = new List<string> { Path.Combine(root, "README.md"), Path.Combine(root, "CLAUDE.md"), Path.Combine(root, "PROJECT_STATE.md"), Path.Combine(root, "installer.iss") };
        foreach (var dir in new[] { "docs", "tools", "installer" })
            if (Directory.Exists(Path.Combine(root, dir)))
                files.AddRange(Directory.GetFiles(Path.Combine(root, dir), "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".md") || f.EndsWith(".ps1") || f.EndsWith(".txt") || f.EndsWith(".iss")));
        // the security scan workflow and these tests list the patterns on purpose; the "C:\w\" fallback of other tests is a test-only path
        var hits = files.Where(File.Exists).SelectMany(Hits).Where(h => !h.StartsWith("containers-")).ToList();
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void VersionInfoNamesTheSamePublisherEverywhere()
    {
        var vi = FileVersionInfo.GetVersionInfo(typeof(MainWindow).Assembly.Location);
        Assert.Equal("giorstergiopoulos-design", vi.CompanyName);
        Assert.Contains("Opsis", vi.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(vi.FileDescription) && string.IsNullOrWhiteSpace(vi.Comments));
        Assert.Contains("giorstergiopoulos-design", vi.LegalCopyright);
        Assert.StartsWith(MainVersion(), vi.FileVersion);
        var script = File.ReadAllText(Path.Combine(Root(), "installer.iss"));
        Assert.Contains("#define MyAppPublisher \"giorstergiopoulos-design\"", script);
        Assert.Contains("AppPublisherURL=", script);
        Assert.Contains("VersionInfoCompany=", script);
        Assert.Contains("#define MyAppVersion \"" + MainVersion() + "\"", script);
    }

    private static string MainVersion()
    {
        var csproj = File.ReadAllText(Path.Combine(Root(), "MotionDeskStudio.csproj"));
        var m = System.Text.RegularExpressions.Regex.Match(csproj, "<Version>([0-9.]+)</Version>");
        return m.Groups[1].Value;
    }

    [Fact]
    public void TheLicenceIsTheStandardMitText()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "LICENSE")).Replace("\r\n", "\n");
        Assert.StartsWith("MIT License\n\nCopyright (c) 2026 giorstergiopoulos-design\n\nPermission is hereby granted, free of charge", text);
        Assert.EndsWith("SOFTWARE.\n", text);
        Assert.DoesNotContain("Third-Party", text);
        Assert.True(text.Length < 1200, "the LICENSE file must hold only the licence (notices go to THIRD_PARTY_NOTICES.md)");
    }

    [Fact]
    public void TheInstallerNeedsInnoSetup7AndTheUpdaterRunsNoHiddenScript()
    {
        var script = File.ReadAllText(Path.Combine(Root(), "installer.iss"));
        Assert.Contains("EncodeVer(7,0,0)", script);
        Assert.Contains("RESTARTOPSIS", script);
        var updater = File.ReadAllText(Path.Combine(Root(), "src", "Services", "UpdateInstallerService.cs"));
        foreach (var bad in new[] { "powershell", "ExecutionPolicy", "WindowStyle", "-Command" })
            Assert.DoesNotContain(bad, updater, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RESTARTOPSIS=1", UpdateInstallerService.SetupArguments);
        // no other source file starts a hidden PowerShell either
        foreach (var f in Directory.GetFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            var t = File.ReadAllText(f);
            Assert.False(t.Contains("ExecutionPolicy", StringComparison.OrdinalIgnoreCase) && t.Contains("Bypass", StringComparison.OrdinalIgnoreCase), f + " starts PowerShell with a policy bypass");
        }
    }
}
