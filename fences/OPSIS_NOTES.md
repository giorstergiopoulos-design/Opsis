# fences/ — DesktopFences inside Opsis

This folder is the open-source **Desktop Fences +** (https://github.com/limbo666/DesktopFences, WPF, MIT — see `LICENSE` here and
`../THIRD_PARTY_NOTICES.md`) built as the companion app `OpsisFences.exe`. Opsis starts and stops it and uses it as its **DeskContainers**.

- Built by `MotionDeskStudio.csproj` (target `BuildFences`) into `<output>\fences\`; the main project does not compile these files.
- Data: `%AppData%\Opsis\fences` (`DataRoot.cs`; `OPSIS_FENCES_DATA` overrides, `OPSIS_APPDATA` is honoured for tests).
- Opsis-specific changes are marked `Opsis` in comments; new files: `DataRoot.cs`, `WshShim.cs`, `OpsisTheme.cs`, `OpsisMenuStyles.cs`.
- Env set by Opsis: `OPSIS_PARENT_PID` (exit with Opsis), `OPSIS_THEME` (dark palette for the settings windows).
- Full status, rules and test notes: `../PROJECT_STATE.md` (top section).
