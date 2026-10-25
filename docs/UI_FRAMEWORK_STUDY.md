# Study: should Opsis and its plugins move to WinUI 3 or WPF? (2026-10-25)

Status: **analysis only, no code changed.** Items marked (measure) are expectations that must be confirmed with a pilot, not facts.

## 1. What the app is made of today (verified in the repo)
| Layer | Technology | Size / notes |
|---|---|---|
| Main window (all settings pages) | WinForms, owner-drawn controls (`HoverButton`, `FlatComboBox`, `FlatCheckBox`, `UiTheme`) | `src/UI` ~6.9k lines, `MainWindow*.cs` ~5k |
| Wallpaper host | WinForms `Form` + WebView2, `SetParent` into WorkerW/Progman | `WallpaperWindow.cs` 1.4k lines, depends on Win32 window styles |
| Widgets, DeskStrip, zone overlays | WinForms layered windows (`TransparencyKey`, acrylic via `SetWindowCompositionAttribute`, click-through) | `src/Widgets` ~11.8k lines |
| Tray, services | WinForms `NotifyIcon`, plain .NET services | `src/Services` ~4.7k lines |
| DeskContainers engine | **WPF**, separate process `OpsisFences.exe`, file bridge | already running side by side with the WinForms app |
| Plugins | `AssemblyLoadContext` per plugin, contract `OpsisNewsCrawler.Plugin.Abstractions.dll`; a plugin page is returned by `PluginNavigationItem.CreatePage()` and hosted inside the main window (a WinForms control) | `PluginHost.cs` |

Total ≈ 23.5k lines of C#. The two things that make Opsis what it is — the wallpaper and the widgets — are **Win32 window tricks**, not forms UI.

## 2. WinUI 3 — not recommended
- **Wallpaper/widgets cannot follow.** They rely on re-parenting a window into WorkerW, `WS_EX_LAYERED`/`TransparencyKey` click-through windows and per-window acrylic. WinUI 3 windows are composition-based (XAML island inside a top-level window); per-pixel layered transparency and `SetParent` into the desktop are not supported scenarios (measure; I would not bet the core feature on it).
- **Two UI stacks plus a third.** Opsis would still need WinForms/Win32 for the wallpaper layer, so WinUI 3 adds a new runtime instead of replacing one.
- **Deployment cost.** Windows App SDK runtime/bootstrapper (or self-contained, much larger package), more installer work, heavier start-up and RAM (measure). The current installer is a plain framework-dependent .NET 10 publish.
- **Plugins.** The contract hands out WinForms controls; WinUI 3 cannot host them, so every plugin would need a rewrite and cannot load into the same process UI.
- Gain: modern look (Mica, Fluent controls). That look is available in WPF on .NET 10 too, without the above costs.

## 3. WPF — viable, but only for the main window, and only gradually
Why it fits better:
- Same process can mix both stacks (`ElementHost` / `WindowsFormsHost`), so migration can go page by page.
- Already used by OpsisFences, so the build, theming (`OpsisTheme.cs`) and installer path exist.
- .NET 10 ships a Fluent theme for WPF; DPI, accessibility, animation, data binding/MVVM, virtualised lists are far better than owner-drawn WinForms. Most of today's visual glitches (button flicker, manual repaint on theme change, the 400 ms re-colour walker in Fences) come from hand-painted controls.
- Layered/transparent WPF windows exist (`AllowsTransparency`), but they are software-rendered and heavier; for the wallpaper/widgets the WinForms+Win32 path should stay (measure before touching).

Costs / risks:
- ~5k lines of owner-drawn page code to rewrite; every page needs a live click-through test (project rule). Realistic effort: several weeks of focused work, not a quick task.
- WPF runtime loaded in the main process adds RAM and start-up time (measure; OpsisFences already pays it in its own process).
- Behaviour regressions in a product whose current priority is stability.

## 4. Plugins
The contract exposes WinForms. Do not break it:
1. Bump the contract to **v2** with an additional optional member, e.g. `object CreatePageV2()` that may return a WPF `UIElement` *or* a WinForms `Control`; the host wraps a `UIElement` in `ElementHost`. v1 plugins keep working unchanged (`IPluginProfileAware` shows the "optional interface" pattern already in use).
2. Keep loading in a separate `AssemblyLoadContext`; WPF plugins must not share the host's WPF resource dictionaries except through a small theme-colour API (palette strings, like `theme.txt` for Fences).
3. Version check already exists (`IsNewerVersion`); add a minimum-contract-version field so an old host refuses a v2-only plugin cleanly.

## 5. Recommendation
1. **Do not move to WinUI 3.**
2. **Keep WinForms/Win32 for the wallpaper host, widgets, DeskStrip, tray** (the core product; no benefit from changing).
3. **Pilot WPF for the main window** with ONE page (suggest the Wallpaper page: it has the most controls and the reported glitches) hosted via `ElementHost` inside the current `MainWindow`. Measure: RAM and start-up delta, theme switch speed, flicker, DPI. Decision gate after the pilot: continue page by page, or stop with no loss (the rest of the app is untouched).
4. Add the plugin contract v2 only when the first WPF page proves worthwhile.
5. Alternative worth considering before any rewrite: fix the specific owner-draw problems (double-buffering, fewer full repaints on theme events) — cheaper, and some of the flicker already traced to repaint storms rather than to WinForms itself.

## 6. If the user approves the pilot — steps for the implementing terminal
- Queue rule: finish active tasks in `PROJECT_STATE.md` / `_handoff/` first, then `docs/ENGINE_ROADMAP.md` items, then this.
- New project folder `src/UiWpf/` (or a separate assembly) with `UseWPF=true`; main csproj currently has `UseWPF=false` and the `fences\**` exclusion pattern — mirror it, do not mix XAML into the WinForms project blindly.
- Re-use `UiTheme` colours through a bridge (`ThemeManager.Changed/Repainted`) so light/dark switching works.
- Record RAM (private working set after 60 s idle), cold start to tray, page-open time, before vs after, in `PROJECT_STATE.md`.
- Test with isolated `OPSIS_APPDATA`; never use SendKeys toward unknown focus.
