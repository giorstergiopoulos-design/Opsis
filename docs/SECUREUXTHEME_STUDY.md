# Study: SecureUxTheme 4.0.0 — what Opsis and Opsis Appearance can learn from it (2026-10-25)

Source studied: the zip `SecureUxTheme-4.0.0.zip` (≈1000 lines of C++: `hooks.cpp`, `chores.cpp`, `avrf.cpp`, `ThemeUiProxy/dll.cpp`, `AVRF.md`). License **LGPL-2.1**: using it as an unmodified, separately installed component is fine; copying or porting its code into Opsis (MIT) is not planned. Only ideas are taken.

## 1. What it does
- Windows only loads `.msstyles` visual styles signed by Microsoft. SecureUxTheme removes that check **in memory**, with no file changes and no driver.
- How: it is an *Application Verifier provider DLL* (`SecureUxTheme.dll`, copied to System32). The Application Verifier mechanism (IFEO `VerifierDlls`, `GlobalFlag`) makes Windows load it into chosen processes very early (winlogon, explorer, LogonUI, SystemSettings…). There it patches the import tables of `themeui.dll`, `themeservice.dll`, `uxinit.dll`, `uxtheme.dll`, hooking `CryptImportKey` and `ResolveDelayLoadedAPI` so the signature verification succeeds.
- `winlogon` chores: registers `ThemeUiProxy.dll` for two theme COM classes, and deletes the `DefaultColors` registry values so LogonUI stops resetting colours when the PC locks.
- Its own FAQ documents the risk: a bad patch or a bad theme can cause a **login loop**.

## 2. What it means for Opsis
Opsis' own rule (README, hand-off docs): **no DLL injection into explorer.exe / dwm.exe / winlogon.exe, no system file changes.** SecureUxTheme is exactly that kind of component (system-wide in-process patching + a DLL in System32 + IFEO registry keys + admin rights). **Decision: do not integrate, bundle, install or reproduce it.** Opsis already covers the safe 90 %: accent, light/dark, Mica/Acrylic/corners/title bars through documented DWM APIs, taskbar look through `SetWindowCompositionAttribute`, signed `.theme` files.

## 3. Improvements that are safe and useful (ordered)
| # | Idea | Where | Status |
|---|---|---|---|
| 1 | **Pre-flight check of `.theme` files**: read `[VisualStyles] Path=`, check the Authenticode signature of the `.msstyles`, warn (default answer = No) before applying an unsigned/third-party style when no patcher is installed. Detect an installed patcher (System32\SecureUxTheme.dll or IFEO `VerifierDlls` of winlogon) and skip the warning then. | Opsis Appearance: `ThemeSafety.cs`, called by the "Windows themes (.theme)" Apply button | **DONE (this pass), 8 unit tests** — not tried with a real unsigned theme |
| 2 | Same check in Opsis' own theme package engine (`ThemePackageEngine`, "Tech Grid" and saved `.theme` files) before it launches a `.theme`. Copy the logic of `ThemeSafety` (or share it), do not duplicate bugs. | Opsis `src/` (Personalization page) | TODO |
| 3 | **Re-apply the look after lock/unlock** (the LogonUI colour-reset problem): on `SessionSwitch` unlock, re-read the current accent/theme colours and, if they differ from what Opsis Appearance last set, re-apply them (no registry deletion, no injection). | Opsis Appearance `AppearancePlugin` (engine `Apply`), reuse `SystemEvents.SessionSwitch` like `AppActivity` | TODO — needs a live lock test |
| 4 | **Remember the active `.theme` and visual style before changing them** in the existing BackupVault, so "Restore my original settings" can switch back to the previous theme file even if an applied theme misbehaves. | Appearance `Safety.cs` / `BackupVault` | TODO |
| 5 | List the `.msstyles` that are installed and mark them (Microsoft / third party / unsigned) in the Windows-themes list, so the user knows before clicking. | Appearance page list, uses `ThemeSafety.Signature` | TODO (small) |
| 6 | A short help text in the Windows-themes section: "custom visual styles need a signature patcher that Opsis does not install; use only themes from sources you trust". Greek + English locale keys. | Appearance `ApplyLanguage` | TODO (tiny) |

## 4. Explicitly NOT planned
- Bundling or installing SecureUxTheme / ThemeUiProxy, writing IFEO keys, copying DLLs into System32, hooking `uxtheme`/`themeui`.
- Writing `HKLM` colour values or deleting `DefaultColors` (that is what the tool does inside winlogon; outside it this would be a system setting change).

## 5. Instructions for the implementing terminal
- Queue rule: finish active tasks listed in `PROJECT_STATE.md` / `_handoff/` first, then `docs/ENGINE_ROADMAP.md`, then `docs/UI_FRAMEWORK_STUDY.md`, then this list (items 2–6).
- Plugin repo: `C:\Users\gstrj\Documents\OpsisAppearance` (GitHub `giorstergiopoulos-design/OpsisAppearance`; set local `git config user.name/user.email` if a fresh clone). Build `dotnet build OpsisAppearance.sln -c Release`, tests `dotnet test tests/OpsisAppearance.Tests` (136 pass). The page can be rendered without a screen: `src\OpsisAppearance.App\bin\Release\net10.0-windows\OpsisAppearance.exe --snapshot out.png [--el]`.
- All controls of the page are in `OpsisControls.cs` (palette comes from the host `SetTheme`); do not add plain WinForms controls.
- The user's own Opsis keeps the installed plugin DLL (`%AppData%\Opsis\plugins\Opsis Appearance\OpsisAppearancePlugin.dll`) locked: do not overwrite it; give the user the new build instead.
