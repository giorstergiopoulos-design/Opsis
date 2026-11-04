# Writing a plugin for Opsis

A plugin is a .NET class library whose file name ends in `Plugin.dll` and that implements `IOpsisNewsCrawlerPlugin` from `OpsisNewsCrawler.Plugin.Abstractions` (assembly version 0.8.0.0; reference it with `Private=false`: the host shares its own copy). Optional additions: `IPluginWidgetProvider` / `PluginFeeds` (see `PLUGIN-FEEDS-AND-WIDGETS.md`), `IPluginProfileAware`.

## Where it goes
`%AppData%\Opsis\plugins\<name>\` (user-writable: Opsis asks the user to approve the exact content, SHA-256, once; until then the plugin waits in the tray menu entry "Plugins waiting for approval") or `<Opsis install folder>\plugins\<name>\` (Program Files: trusted).

## What Opsis owns (do NOT offer these from a plugin hosted by Opsis)
The **Welcome guide** and the **Opsis family** window are Opsis' own tray entries (one of each, whatever plugins are installed). When Opsis hosts a plugin it removes from `GetTrayMenuItems()` every entry whose `Key` is `welcome`, `suite` or `family`, or ends in `.welcome`, `.suite` or `.family`, so an older plugin that still offers them does not duplicate them. A standalone build of an app can keep its own copies.

## Threads
`Start()`, `Stop()`, `PluginFeeds.Register/Unregister` and the menu getters may be called from different threads at the same time. Return **copies** (new lists) from `GetTrayMenuItems()`, `GetNavigationItems()` and `GetWidgets()`, never your live collections; Opsis retries a call that fails with "Collection was modified" up to three times and never shows that error to the user, but a plugin that fails for another reason is skipped and logged in `%AppData%\Opsis\plugins.log`.

## Look
`%AppData%\Opsis\plugin-ui.json` (`{"style":"Opsis|Appearance|Crawler","version":n}`) tells plugins which default look the user chose.

## Testing
`tests/PluginStub` is a minimal plugin; `Opsis.exe --test-plugin-widget <folder>` (with `OPSIS_E2E=1` and an isolated `OPSIS_APPDATA`) loads the plugins like the app does, approves them, opens the calendar widget and saves a screenshot. See the `plugin-e2e` workflow.
