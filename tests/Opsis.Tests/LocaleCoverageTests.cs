using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Opsis.Tests;

// Every UI string must exist in EVERY language file, with the same {0}-style placeholders, and none may be empty.
public sealed class LocaleCoverageTests
{
    private const int KnownMissing = 23;

    private static string LocalesDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "locales"))) d = d.Parent;
        return Path.Combine(d!.FullName, "locales");
    }

    private static Dictionary<string, Dictionary<string, string>> Load() =>
        Directory.GetFiles(LocalesDir(), "*.json").ToDictionary(
            f => Path.GetFileNameWithoutExtension(f),
            f => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!);

    [Fact]
    public void All14LanguagesHaveTheSameKeys()
    {
        var all = Load();
        Assert.Equal(14, all.Count);
        var reference = all["en-US"].Keys.ToHashSet();
        var problems = new List<string>();
        foreach (var (lang, map) in all)
        {
            // en-US and el-GR are complete. The other 12 languages still lack 23 long texts (VersionNotes history + the DeskContainers guide, shown in
            // English): a RATCHET - the count may only go down until they are translated (open item for 2.2.1).
            int missing = reference.Except(map.Keys).Count();
            int allowed = lang is "en-US" or "el-GR" ? 0 : KnownMissing;
            if (missing > allowed) problems.Add(lang + " has " + missing + " missing keys (allowed " + allowed + ")");
            foreach (var k in map.Keys.Except(reference)) problems.Add(lang + " extra " + k);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)));
    }

    [Fact]
    public void PlaceholdersMatchAndNoValueIsEmpty()
    {
        var all = Load();
        var en = all["en-US"];
        var problems = new List<string>();
        foreach (var (lang, map) in all)
            foreach (var (k, v) in map)
            {
                if (string.IsNullOrWhiteSpace(v)) { problems.Add(lang + " empty " + k); continue; }
                if (!en.TryGetValue(k, out var e)) continue;
                var a = Regex.Matches(e, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x);
                var b = Regex.Matches(v, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x);
                if (!a.SequenceEqual(b)) problems.Add(lang + " placeholders differ " + k);
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)));
    }
}
