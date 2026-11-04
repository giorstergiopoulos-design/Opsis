using System.Windows.Forms;
using MotionDesk.Services;
using OpsisNewsCrawler.Plugin.Abstractions;
using Xunit;

namespace Opsis.Tests;

// The 2026-11 contract additions (IPluginWidgetProvider, PluginWidget, PluginFeed, PluginFeeds): a plugin can replace a built-in desktop widget (e.g. "calendar")
// and publish text feeds for the News Crawler bars. They are optional: the assembly version stays 0.8.0.0.
public sealed class PluginWidgetContractTests
{
    private sealed class StubPlugin : IOpsisNewsCrawlerPlugin, IPluginWidgetProvider
    {
        private readonly bool _throws;
        public StubPlugin(string id, string? replaces, bool throws = false) { Id = id; _replaces = replaces; _throws = throws; }
        private readonly string? _replaces;
        public string Id { get; }
        public string Name => "Stub " + Id;
        public string Version => "1.0.0";
        public bool Started;
        public void Start() { Started = true; PluginFeeds.Register(new PluginFeed("stub-" + Id, "Δοκιμή", "Test", () => new[] { new PluginFeedItem("one"), new PluginFeedItem("two", "https://example.org") })); }
        public void Stop() { Started = false; PluginFeeds.Unregister("stub-" + Id); }
        public void Pause() { }
        public void Resume() { }
        public void Refresh() { }
        public void SetMode(string mode) { }
        public void SetPosition(string position) { }
        public void SetTheme(bool dark, int background, int surface, int border, int text, int textMuted, int accent, double opacity) { }
        public void SetLanguage(string code) { }
        public PluginAboutInfo GetAboutInfo() => new(Name, Version, "", "", "test", "MIT", Array.Empty<PluginAboutSection>());
        public IReadOnlyList<PluginMenuItem> GetTrayMenuItems() => Array.Empty<PluginMenuItem>();
        public IReadOnlyList<PluginNavigationItem> GetNavigationItems() => Array.Empty<PluginNavigationItem>();
#pragma warning disable CS0067
        public event Action<string>? NavigationRequested;
#pragma warning restore CS0067
        public IReadOnlyList<PluginWidget> GetWidgets()
        {
            if (_throws) throw new InvalidOperationException("boom");
            return new[] { new PluginWidget("w-" + Id, "Ημερολόγιο", "Calendar", () => new Label { Text = "hello" }, _replaces) };
        }
    }

    [Fact]
    public void AStartedPluginWidgetReplacesTheBuiltInCalendar()
    {
        var host = new PluginHost();
        host.AddForTest(new StubPlugin("a", "calendar"), started: true);
        var w = host.FindWidgetReplacing("calendar");
        Assert.NotNull(w);
        Assert.Equal("w-a", w!.Key);
        using var content = w.CreateContent();
        Assert.IsType<Label>(content);
    }

    [Fact]
    public void MatchingIgnoresCaseAndOtherBuiltInsAreNotReplaced()
    {
        var host = new PluginHost();
        host.AddForTest(new StubPlugin("a", "Calendar"), started: true);
        Assert.NotNull(host.FindWidgetReplacing("CALENDAR"));
        Assert.Null(host.FindWidgetReplacing("clock"));
    }

    [Fact]
    public void AStoppedPluginOrOneThatDoesNotReplaceAnythingIsIgnored()
    {
        var host = new PluginHost();
        host.AddForTest(new StubPlugin("stopped", "calendar"), started: false);
        host.AddForTest(new StubPlugin("extra", null), started: true);
        Assert.Null(host.FindWidgetReplacing("calendar"));
    }

    [Fact]
    public void AFailingProviderIsSkippedAndTheNextOneIsUsed()
    {
        var host = new PluginHost();
        host.AddForTest(new StubPlugin("bad", "calendar", throws: true), started: true);
        host.AddForTest(new StubPlugin("good", "calendar"), started: true);
        Assert.Equal("w-good", host.FindWidgetReplacing("calendar")!.Key);
    }

    [Fact]
    public void FeedsRegisterFindReplaceAndUnregister()
    {
        string key = "t-" + Guid.NewGuid().ToString("N")[..6];
        int changed = 0; void OnChanged() => changed++;
        PluginFeeds.Changed += OnChanged;
        try
        {
            PluginFeeds.Register(new PluginFeed(key, "Α", "A", () => new[] { new PluginFeedItem("x") }));
            Assert.NotNull(PluginFeeds.Find(key.ToUpperInvariant()));
            PluginFeeds.Register(new PluginFeed(key, "Β", "B", () => Array.Empty<PluginFeedItem>()));      // same key replaces
            Assert.Single(PluginFeeds.All(), f => f.Key == key);
            Assert.Equal("B", PluginFeeds.Find(key)!.TitleEn);
            PluginFeeds.Unregister(key);
            Assert.Null(PluginFeeds.Find(key));
            Assert.True(changed >= 3);
        }
        finally { PluginFeeds.Changed -= OnChanged; PluginFeeds.Unregister(key); }
    }

    [Fact]
    public void TheContractAssemblyKeepsItsVersionAndHasTheNewTypes()
    {
        var asm = typeof(IOpsisNewsCrawlerPlugin).Assembly;
        Assert.Equal(new Version(0, 8, 0, 0), asm.GetName().Version);
        var names = asm.GetExportedTypes().Select(t => t.Name).ToHashSet();
        foreach (var n in new[] { "IOpsisNewsCrawlerPlugin", "PluginNavigationItem", "IPluginProfileAware", "IPluginWidgetProvider", "PluginWidget", "PluginFeed", "PluginFeedItem", "PluginFeeds" })
            Assert.Contains(n, names);
    }

    // Loads the REAL plugin the way the host does (own load context, shared contract) when it has been built: set OPSIS_REAL_PLUGIN_DIR to its output folder.
    [Fact]
    public void TheRealPluginLoadsAndOffersTheCalendarReplacement()
    {
        var dir = Environment.GetEnvironmentVariable("OPSIS_REAL_PLUGIN_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;          // not built on this machine: nothing to check
        var host = new PluginHost();
        host.LoadFolder(dir);
        Assert.NotEmpty(host.Plugins);
        host.StartAllForTest();
        try
        {
            var w = host.FindWidgetReplacing("calendar");
            Assert.NotNull(w);
            Assert.False(string.IsNullOrEmpty(w!.TitleEl));
        }
        finally { foreach (var p in host.Plugins) { try { p.Stop(); } catch (Exception) { } } }
    }
}
