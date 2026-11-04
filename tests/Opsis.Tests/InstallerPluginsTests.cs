using Xunit;

namespace Opsis.Tests;

// Plugins installed next to Opsis by their own installers (e.g. <Opsis>\plugins\Chronikologio) must survive an Opsis upgrade and must not be removed by its uninstaller rules.
public sealed class InstallerPluginsTests
{
    private static string Script()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "installer.iss"))) d = d.Parent;
        return File.ReadAllText(Path.Combine((d ?? new DirectoryInfo(@"C:\w\Opsis")).FullName, "installer.iss"));
    }

    private static string Section(string text, string name)
    {
        int i = text.IndexOf("[" + name + "]", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        int next = text.IndexOf("\n[", i + 1, StringComparison.Ordinal);
        return next < 0 ? text[i..] : text[i..next];
    }

    [Fact]
    public void NoInstallDeleteOrUninstallDeleteRuleTouchesThePluginsFolder()
    {
        var s = Script();
        foreach (var name in new[] { "InstallDelete", "UninstallDelete" })
            Assert.DoesNotContain("plugins", Section(s, name), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheInstallerNeverRemovesTheAppFolderWholesale()
    {
        var s = Script();
        Assert.DoesNotContain("DelTree(ExpandConstant('{app}", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"{app}\*", Section(s, "InstallDelete") + Section(s, "UninstallDelete"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeleteAfterInstall", Section(s, "Files"), StringComparison.OrdinalIgnoreCase);
    }
}
