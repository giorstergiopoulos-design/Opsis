using MotionDesk.Services;
using MotionDesk.Widgets;
using OpsisNewsCrawler.Plugin.Abstractions;
using Xunit;

namespace Opsis.Tests;

public sealed class PluginRaceAndFamilyTests
{
    private sealed class MenuPlugin : IOpsisNewsCrawlerPlugin
    {
        private readonly PluginMenuItem[] _items;
        public MenuPlugin(string id, params PluginMenuItem[] items) { Id = id; _items = items; }
        public string Id { get; }
        public string Name => "Plugin " + Id;
        public string Version => "1.0.0";
        public void Start() { }
        public void Stop() { }
        public void Pause() { }
        public void Resume() { }
        public void Refresh() { }
        public void SetMode(string mode) { }
        public void SetPosition(string position) { }
        public void SetTheme(bool dark, int background, int surface, int border, int text, int textMuted, int accent, double opacity) { }
        public void SetLanguage(string code) { }
        public PluginAboutInfo GetAboutInfo() => new(Name, Version, "", "", "t", "MIT", Array.Empty<PluginAboutSection>());
        public IReadOnlyList<PluginMenuItem> GetTrayMenuItems() => _items;
        public IReadOnlyList<PluginNavigationItem> GetNavigationItems() => Array.Empty<PluginNavigationItem>();
#pragma warning disable CS0067
        public event Action<string>? NavigationRequested;
#pragma warning restore CS0067
    }

    private static PluginMenuItem Item(string key, bool sep = false) => new(key, "Τίτλος " + key, "Title " + key, () => { }, sep);

    [Fact]
    public void WelcomeAndFamilyEntriesOfPluginsAreFilteredSoOpsisShowsThemOnce()
    {
        var host = new PluginHost();
        var items = new[] { Item("crawler.welcome"), Item("sep", true), Item("open-settings"), Item("crawler.suite"), Item("welcome"), Item("suite"), Item("family"), Item("x", true) };
        host.AddForTest(new MenuPlugin("crawler", items), true);
        host.AddForTest(new MenuPlugin("appearance", Item("appearance.welcome"), Item("appearance.suite"), Item("apply")), true);
        var groups = host.GetTrayMenuGroups();
        Assert.Equal(2, groups.Count);
        var keys = groups.SelectMany(g => g.Items).Select(i => i.Key).ToList();
        Assert.Equal(new[] { "open-settings", "apply" }, keys);
        Assert.DoesNotContain(groups[0].Items, i => i.IsSeparator && (groups[0].Items[0] == i || groups[0].Items[^1] == i));       // no dangling separators
        Assert.DoesNotContain(host.GetTrayMenuItems(), i => i.Key.Contains("welcome") || i.Key.Contains("suite"));
    }

    [Fact]
    public void AGroupWithOnlyHostOwnedEntriesDisappears()
    {
        var host = new PluginHost();
        host.AddForTest(new MenuPlugin("only", Item("only.welcome"), Item("only.suite")), true);
        Assert.Empty(host.GetTrayMenuGroups());
    }

    [Fact]
    public void ReadingThePluginStateWhileOtherThreadsChangeItNeverThrows()
    {
        var host = new PluginHost();
        string prefix = "race-" + Guid.NewGuid().ToString("N")[..6] + "-";
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var stop = new CancellationTokenSource();
        int n = 0;
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < 400 && !stop.IsCancellationRequested; i++)
            {
                try
                {
                    string id = prefix + w + "-" + i;
                    host.AddForTest(new MenuPlugin(id, Item("a." + id), Item("open")), started: i % 2 == 0);
                    PluginFeeds.Register(new PluginFeed(id, "Α", "A", () => new[] { new PluginFeedItem("x") }));
                    PluginFeeds.Unregister(id);
                    Interlocked.Increment(ref n);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
        })).ToArray();
        var readers = Enumerable.Range(0, 4).Select(r => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    _ = host.GetTrayMenuGroups(); _ = host.GetTrayMenuItems(); _ = host.GetNavigationItems(); _ = host.GetAboutInfos(); _ = host.Plugins; _ = host.PendingApprovals;
                    _ = PluginFeeds.All(); _ = host.FindWidgetReplacing("calendar");
                }
                catch (Exception ex) { errors.Add(ex); }
            }
        })).ToArray();
        Task.WaitAll(writers, TimeSpan.FromSeconds(60));
        stop.Cancel();
        Task.WaitAll(readers, TimeSpan.FromSeconds(10));
        Assert.Empty(errors);
        Assert.True(n > 0);
    }

    [Fact]
    public void RetryRunsAPluginCallAgainOnCollectionModifiedButPassesOtherErrorsOn()
    {
        int calls = 0;
        Assert.Equal(7, PluginHost.Retry(() => { if (++calls < 3) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute."); return 7; }));
        Assert.Equal(3, calls);
        Assert.Throws<InvalidOperationException>(() => PluginHost.Retry<int>(() => throw new InvalidOperationException("something else")));
        calls = 0;
        Assert.Throws<InvalidOperationException>(() => PluginHost.Retry<int>(() => { calls++; throw new InvalidOperationException("Collection was modified"); }));
        Assert.Equal(3, calls);       // gives up after 3 attempts
    }

    [Fact]
    public void ARaceErrorIsTransientSoNoDialogIsShown()
    {
        Assert.True(MotionDesk.Program.IsTransient(new InvalidOperationException("Collection was modified; enumeration operation may not execute.")));
        Assert.True(MotionDesk.Program.IsTransient(new AggregateException(new InvalidOperationException("Collection was modified"))));
        Assert.True(MotionDesk.Program.IsTransient(new ObjectDisposedException("x")));
        Assert.False(MotionDesk.Program.IsTransient(new InvalidOperationException("other problem")));
        Assert.False(MotionDesk.Program.IsTransient(new NullReferenceException()));
    }

    [Fact]
    public void FamilyListIsParsedInTheUsersLanguageWithEnglishFallback()
    {
        const string json = """
        { "owner": "me", "apps": [
          { "id": "a", "name": "A", "repo": "A-repo", "tagline": { "en": "English", "el": "Ελληνικά" }, "paths": ["%ProgramFiles%\\A\\a.exe"] },
          { "id": "b", "name": "B", "repo": "B-repo", "tagline": { "en": "Only English" }, "paths": [] },
          { "name": "no id" }, { "id": "c" } ] }
        """;
        var el = FamilyApp.Parse(json, "el");
        Assert.Equal(2, el.Count);
        Assert.Equal("Ελληνικά", el[0].Tagline);
        Assert.Equal("Only English", el[1].Tagline);
        Assert.Equal("https://github.com/me/A-repo/releases/latest", el[0].ReleasesUrl);
        Assert.Null(el[1].InstalledPath);
        Assert.Empty(FamilyApp.Parse("not json", "en"));
    }
}
