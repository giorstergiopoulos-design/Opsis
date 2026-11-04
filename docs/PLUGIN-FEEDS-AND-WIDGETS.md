# Plugin widgets and feeds (contract additions 2026-11, Opsis 2.4.3)

Optional additions to `OpsisNewsCrawler.Plugin.Abstractions` (assembly version stays **0.8.0.0**; a plugin that does not use them is unaffected).

| Type | Purpose |
|---|---|
| `IPluginWidgetProvider` | A plugin that implements it offers desktop widgets: `IReadOnlyList<PluginWidget> GetWidgets()` |
| `PluginWidget(Key, TitleEl, TitleEn, CreateContent, ReplacesBuiltIn)` | One widget. `ReplacesBuiltIn = "calendar"` makes it replace the built-in calendar widget. |
| `PluginFeed(Key, TitleEl, TitleEn, GetItems)` + `PluginFeedItem(Text, Url)` | A list of text items another app can show (News Crawler bars) |
| `PluginFeeds` | Static registry: `Register`, `Unregister`, `All`, `Find`, `Changed` |

## Who does what
- **Opsis (host)**: `PluginHost.FindWidgetReplacing(builtInId)` returns the widget of the first *started* plugin that replaces the built-in id; `WidgetEngine` hosts the plugin control in the normal widget panel (title, drag, resize). If the plugin throws while creating the control, the built-in widget is used and the error goes to `%AppData%\Opsis\plugins.log`.
- **News Crawler**: lists `PluginFeeds.All()` as bar content (`SlotKind.Feed`, spec `feed:<key>`).
- **Plugin**: registers its feeds in `Start()` and unregisters them in `Stop()`.

## Compatibility
The host shares its copy of the contract DLL with every plugin (the plugin references it with `Private=false`). A plugin built against the new types needs a host that ships the new DLL (22 KB, contains `PluginFeeds`); with an older host the plugin fails to load (`ReflectionTypeLoadException`: "Could not load type IPluginWidgetProvider"), is skipped, and the error is written to `plugins.log`.

## Trust
A plugin in a user-writable folder (`%AppData%\Opsis\plugins`) loads only after its exact content (SHA-256 over every `.dll` / `.deps.json`) was approved (`plugin-trust.json`). Nothing changed here.

## Testing
- `tests/Opsis.Tests/PluginWidgetContractTests.cs`: stub plugin, replacement rules, feeds, contract types and version. Set `OPSIS_REAL_PLUGIN_DIR` to the output folder of a real plugin to load it the way the host does.
- `tests/PluginStub`: a minimal plugin used by the `plugin-e2e` workflow with `Opsis.exe --test-plugin-widget <folder>` (needs `OPSIS_E2E=1` and an isolated `OPSIS_APPDATA`).
