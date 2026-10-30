using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Opsis.Tests;

// Quality check: every literal key passed to LocalizationManager.T("...") in the source exists in the English (and Greek) locale file, so the UI never shows a raw key.
public sealed class LocaleKeyUsageTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        // the repository root has both folders (a build output folder may carry a copy of "locales" only)
        while (d != null && !(Directory.Exists(Path.Combine(d.FullName, "locales")) && Directory.Exists(Path.Combine(d.FullName, "src")))) d = d.Parent;
        if (d == null) d = new DirectoryInfo(@"C:\w\Opsis");
        return d.FullName;
    }

    [Fact]
    public void EveryLiteralLocaleKeyUsedInSourceExists()
    {
        string root = Root();
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "locales", "en-US.json")))!;
        var rx = new Regex(@"LocalizationManager\.T\(""([A-Za-z0-9_.]+)""\)");
        var missing = new SortedSet<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
            foreach (Match m in rx.Matches(File.ReadAllText(f)))
                if (!en.ContainsKey(m.Groups[1].Value)) missing.Add(m.Groups[1].Value + "  (" + Path.GetFileName(f) + ")");
        Assert.True(missing.Count == 0, "Keys used in code but missing from en-US.json:\n" + string.Join("\n", missing.Take(40)));
    }
}
