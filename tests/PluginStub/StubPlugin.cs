using System.Windows.Forms;
using OpsisNewsCrawler.Plugin.Abstractions;

namespace OpsisStubPlugin;

/// <summary>Test plugin: one widget that replaces the built-in "calendar", and one text feed.</summary>
public sealed class StubPlugin : IOpsisNewsCrawlerPlugin, IPluginWidgetProvider
{
    public string Id => "opsis-stub";
    public string Name => "Opsis stub plugin";
    public string Version => "1.0.0";
    public void Start() => PluginFeeds.Register(new PluginFeed("opsis-stub", "Δοκιμή", "Test feed", () => new[] { new PluginFeedItem("Stub feed item") }));
    public void Stop() => PluginFeeds.Unregister("opsis-stub");
    public void Pause() { }
    public void Resume() { }
    public void Refresh() { }
    public void SetMode(string mode) { }
    public void SetPosition(string position) { }
    public void SetTheme(bool dark, int background, int surface, int border, int text, int textMuted, int accent, double opacity) { }
    public void SetLanguage(string code) { }
    public PluginAboutInfo GetAboutInfo() => new(Name, Version, "Πρόσθετο δοκιμής", "Test plugin", "Opsis", "MIT", Array.Empty<PluginAboutSection>());
    public IReadOnlyList<PluginMenuItem> GetTrayMenuItems() => Array.Empty<PluginMenuItem>();
    public IReadOnlyList<PluginNavigationItem> GetNavigationItems() => Array.Empty<PluginNavigationItem>();
#pragma warning disable CS0067
    public event Action<string>? NavigationRequested;
#pragma warning restore CS0067
    public IReadOnlyList<PluginWidget> GetWidgets() => new[]
    {
        new PluginWidget("stub-calendar", "Ημερολόγιο (δοκιμή)", "Calendar (stub)", () => new Label
        {
            Text = "STUB CALENDAR WIDGET", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            ForeColor = System.Drawing.Color.White, BackColor = System.Drawing.Color.Transparent, Font = new System.Drawing.Font("Segoe UI", 14f, System.Drawing.FontStyle.Bold),
        }, "calendar"),
    };
}
