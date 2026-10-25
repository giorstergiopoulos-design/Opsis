# Wallpaper engine — stability & performance roadmap (written 2026-10-25)

Source of the ideas: a study of an open-source wallpaper engine (Sucrose, GPL-3). **Only ideas were taken. Do not copy or translate code from it** (Opsis is MIT; GPL code cannot be mixed in). Everything below is described in terms of Opsis' own code.

## QUEUE RULE (read first)
1. If `PROJECT_STATE.md` ("In Progress", "Next Actions") or `_handoff/` lists active tasks, **finish those first**. The items here go **after** them, in the order below.
2. Keep version **2.2.0**, no GitHub release unless the user asks, commit + push to `main` is accepted.
3. Every item: small commit, `dotnet build MotionDeskStudio.csproj -c Release -o <temp folder>`, `dotnet test tests/Opsis.Tests`, then a live check with an isolated data folder (`OPSIS_APPDATA=<temp>`, `settings.json` containing `{"StartWallpaperWithWindows":true}` starts the wallpaper). The user's own Opsis may be elevated and unkillable: always build to a separate `-o` folder and never touch keyboard/mouse while the user works (no SendKeys).
4. Each new setting needs: a property in `AppSettings`, a checkbox/row in the Wallpaper page (`MainWindow.cs`, near `Settings.AutoPerformanceMode`), keys in `locales/en-US.json` and `el-GR.json` (other languages fall back to English), and a line in `CHANGELOG.md` 2.2.0 (neutral wording, never name the studied project).

## DONE in this pass (all in 2.2.0, pushed)
| Area | What | Files |
|---|---|---|
| Pause conditions | `AppActivity` now also reports locked session (`SystemEvents.SessionSwitch`), remote/RDP session (`GetSystemMetrics(SM_REMOTESESSION)`), Battery Saver (`GetSystemPowerStatus.SystemStatusFlag`). Settings `PauseOnLock`, `PauseOnRemoteSession`, `PauseOnBatterySaver` (default on) with checkboxes on the Wallpaper page. `IsFullscreenAppActive` (name kept) is now "any pause reason" so all widgets stop too. | `src/Services/AppActivity.cs`, `AppSettings.cs`, `MainWindow.cs` |
| Fullscreen detection | 1 s polling, 2 consecutive samples to enter, immediate exit; also `SHQueryUserNotificationState` (D3D exclusive / presentation mode). | `AppActivity.cs` |
| Per-monitor | `AppActivity.IsPausedFor(Screen)`: a fullscreen app on monitor A pauses only the wallpaper on A. | `AppActivity.cs`, `WallpaperWindow.cs` (`WallpaperHostEngine`) |
| Heavy pause | When every wallpaper window is paused for 15 s, the whole WebView2 process tree is suspended (`NtSuspendProcess`) = 0 % CPU/GPU; thawed before any call into the WebView, on Disable, and at process exit. Setting `WallpaperHeavyPause` (default on). Verified live: 6 processes frozen, all threads suspended, thawed after the fullscreen app closed, no ProcessFailed. | `src/Widgets/ProcessFreezer.cs`, `WallpaperWindow.cs` (`ApplyHeavyPause`) |
| Crash loops | `ProcessFailed` handler ignores "unresponsive" while frozen/just thawed and stops reloading after 4 failures in 120 s. | `src/Widgets/WebView2Support.cs` |
| New wallpaper modes | `Page` (https site / YouTube link → nocookie embed with injected Referer / local .html) and `Image` (GIF/PNG/JPG, Cover|Contain), loaded in an iframe/img inside `wallpaper/index.html`; dropped while paused. Verified live via DevTools screenshots (YouTube video plays, image shown). Not done: GIF pause-frame, per-monitor source, Application wallpapers (running an exe behind icons), a wallpaper store/sharing. | `MediaSource.cs`, `MainWindow.WallpaperMedia.cs`, `index.html`, `WebView2Support.cs` |
| Already existing (kept) | `WallpaperInterop.IsWorkerWAlive()` supervisor every 2 s + `ExplorerRestartWatcher` (this is the "Progman check" the studied engine has). | `WallpaperWindow.cs` |

NOT VERIFIED live: lock screen pause, RDP pause, Battery Saver pause, two-monitor per-screen behaviour, the wallpaper page checkboxes (build only). Do these first when you have a chance: lock with Win+L for 20 s and look at `wallpaper-attach.log` ("heavy pause: froze/thawed"); toggle Battery Saver in Settings; with two monitors play a borderless fullscreen window on monitor 2 and confirm monitor 1 keeps animating.

## TODO 1 — Tiered pause (small, low risk)
Today: JS pause → (hidden only) `TrySuspend` → after 15 s process freeze. Make the stages explicit and user-visible:
- `PauseLevel { None, Light, Suspended, Frozen }` computed in `WallpaperHostEngine`; log each transition to `AttachLog`.
- Light: `motionDeskSetPaused(true)` (already). Suspended: `CoreWebView2.TrySuspendAsync()` also when visible (it returns false if visible: then skip). Frozen: `ProcessFreezer`.
- Add the delay (currently a constant 15 s) to `AppSettings` (`HeavyPauseDelaySeconds`, 5–120) and a small combo next to the heavy-pause checkbox.
- Add EcoQoS / low priority for the WebView2 tree while paused (`SetPriorityClass(IDLE_PRIORITY_CLASS)` on the same pid list in `ProcessFreezer.Tree`, restore on thaw) — only if the CPU is not already 0 % after freezing (measure first, drop the idea if useless).
- Unit-testable part: move the pure decision (`reasons → level`) into a static method and test it in `tests/Opsis.Tests`.

## TODO 2 — More pause triggers (small)
- Optional CPU/GPU/RAM thresholds ("pause wallpaper above X % system load for N s"): reuse `SystemMonitorService` / `GpuMonitorService`, never a new sampler. Hysteresis (resume 10 points below), default OFF.
- Screensaver running (`SPI_GETSCREENSAVERRUNNING`), "Focus" pause when any app is maximised on that monitor (default OFF; it is more aggressive than fullscreen).
- Per-monitor policy table in settings is NOT needed; keep global toggles.

## TODO 3 — Make the multi-monitor freeze smarter (medium)
`ApplyHeavyPause` freezes only when all windows are paused, because the WebView2 environment (and browser process) is shared with the web widgets (`WidgetEngine.cs` ~446). To freeze a single monitor, give wallpaper windows their own `CoreWebView2Environment` (different user-data folder, e.g. `%LocalAppData%\Opsis\WebView2-wallpaper-<n>`) — costs RAM (one browser process per monitor), so make it opt-in and measure before shipping.

## TODO 4 — Renderer in its own process (large, do last, needs the user's OK first)
Goal: a WebView2/GPU crash or hang can never take down tray, widgets and DeskContainers. Pattern already exists: `OpsisFences.exe` + file bridge (`src/Services/FencesBridge.cs`, `fences/OpsisBridge.cs`).
- New project `wallpaper/OpsisWallpaper.csproj` (WinForms, net10.0-windows) that owns `WallpaperWindow` + `WebView2Support` + `WallpaperInterop` and is built next to `Opsis.exe` exactly like `fences` (see `BuildFences` / `CopyFencesToPublish` targets in `MotionDeskStudio.csproj`, and `installer.iss` taskkill lines).
- Commands over a named pipe (not files): enable, disable, set-mode, set-performance, pause(level), capture-snapshot, shutdown. Opsis keeps `WallpaperHostEngine` as a thin proxy with the same public API so `MainWindow` does not change.
- Watchdog in Opsis: if the child exits unexpectedly, restart it with the failure-budget idea (max 3 per 5 min), then show a one-time notice.
- Everything shared with the child (settings, `wallpaper.json`, `AttachLog`) already goes through files, so only `WallpaperBridge` host objects and the schedule/automation callbacks need care.
- Keep the in-process path behind a setting until the new one has been used for a while.

## TODO 5 — Optional second video engine (decision needed from the user)
WebView2 `<video>` cannot play some formats (WMV etc.; today `WmvConversionService` converts with FFmpeg). An optional libmpv-based renderer would remove conversions and give hardware decoding. Check licences first (libmpv builds are LGPL or GPL depending on the build; GPL builds cannot be bundled with MIT code without consequences) and bundle size. Only start after the user confirms.

## TODO 6 — Small extras
- "Keep PC awake" option (`SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED)` refreshed every 30 s while the wallpaper is visible), default off.
- Wallpaper rotation timer that persists across restarts (the schedule rules in `WallpaperSettings.Rules` already exist: extend them, do not add a second scheduler).
- Unit tests for `AppActivity` decision logic: extract `static bool Decide(...)` first (inputs: fullscreen hits, device names, flags) so no Windows APIs are needed.
