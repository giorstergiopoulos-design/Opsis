# containers/ — OpsisContainers inside Opsis

This folder is the open-source **Opsis DeskContainers** (https://github.com/limbo666/OpsisContainers, WPF, MIT — see `LICENSE` here and
`../THIRD_PARTY_NOTICES.md`) built as the companion app `OpsisContainers.exe`. Opsis starts and stops it and uses it as its **DeskContainers**.

- Built by `MotionDeskStudio.csproj` (target `BuildContainers`) into `<output>\containers\`; the main project does not compile these files.
- Data: `%AppData%\Opsis\containers` (`DataRoot.cs`; `OPSIS_CONTAINERS_DATA` overrides, `OPSIS_APPDATA` is honoured for tests).
- Opsis-specific changes are marked `Opsis` in comments; new files: `DataRoot.cs`, `WshShim.cs`, `OpsisTheme.cs`, `OpsisMenuStyles.cs`.
- Env set by Opsis: `OPSIS_PARENT_PID` (exit with Opsis), `OPSIS_THEME` (dark palette for the settings windows).
- Full status, rules and test notes: `../PROJECT_STATE.md` (top section).
