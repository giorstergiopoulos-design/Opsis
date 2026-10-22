<#
.SYNOPSIS
  Opsis build pipeline: restore/build/publish -> optional self-test -> optional Inno Setup -> zip.
.DESCRIPTION
  Corrected replacement for OpsisLocalDevManager.ps1. Differences:
   - checks $LASTEXITCODE (native exit codes are not caught by try/catch)
   - does NOT rewrite source files and does NOT run git
   - does NOT hide build output
   - self-test only runs if the app implements --run-selftest
  Run from the repo root:   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\BuildAll.ps1
#>
[CmdletBinding()]
param(
    [string]$Csproj       = "MotionDeskStudio.csproj",
    [string]$Configuration = "Release",
    [string]$Runtime      = "win-x64",
    [switch]$SkipSelfTest,
    [switch]$SkipInstaller,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
Set-Location (Resolve-Path (Join-Path $PSScriptRoot ".."))   # repo root = parent of scripts\
$root = Get-Location

function Step($msg) { Write-Host "[*] $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host "[-] $msg" -ForegroundColor Red; exit 1 }

if (-not (Test-Path $Csproj)) { Fail "$Csproj not found in $root. Run from the Opsis repo (scripts\ must be inside it)." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail "dotnet SDK not found. Install the .NET 10 SDK." }

Step "dotnet build"
& dotnet build $Csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { Fail "Build failed (exit $LASTEXITCODE)." }

Step "dotnet publish"
# ΔΙΟΡΘΩΣΗ (PHASE0_REPORT.md): το πραγματικό TFM αυτού του repo είναι
# net10.0-windows10.0.19041.0, όχι το γενικό net10.0 που υπέθετε η πρώτη εκδοχή.
$publishDir = Join-Path $root "bin\$Configuration\net10.0-windows10.0.19041.0\$Runtime\publish"
& dotnet publish $Csproj -c $Configuration -r $Runtime --self-contained false
if ($LASTEXITCODE -ne 0) { Fail "Publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path $publishDir)) { Fail "Publish dir not found: $publishDir (check TargetFramework / output path)." }

if (-not $SkipSelfTest) {
    # ΔΙΟΡΘΩΣΗ: το exe μετονομάστηκε σε Opsis.exe στο rename (το .csproj παραμένει σκόπιμα
    # MotionDeskStudio.csproj - βλ. root CLAUDE.md).
    $exe = Join-Path $publishDir "Opsis.exe"
    # Only run if the app advertises the flag, so a missing implementation never blocks the build.
    $hasFlag = $false
    if (Test-Path "src") { $hasFlag = [bool](Get-ChildItem -Path "src" -Recurse -Filter *.cs | Select-String -Pattern "--run-selftest" -SimpleMatch -Quiet) }
    if (Test-Path "Program.cs") { $hasFlag = $hasFlag -or [bool](Select-String -Path "Program.cs" -Pattern "--run-selftest" -SimpleMatch -Quiet) }
    if ($hasFlag -and (Test-Path $exe)) {
        Step "self-test (--run-selftest)"
        $p = Start-Process $exe -ArgumentList "--run-selftest" -PassThru -Wait -NoNewWindow
        if ($p.ExitCode -ne 0) { Fail "Self-test failed (exit $($p.ExitCode)). Installer/zip blocked." }
        Write-Host "[+] Self-test passed" -ForegroundColor Green
    } else {
        Write-Host "[!] --run-selftest not implemented yet; skipping (see docs/04_IMPLEMENTATION_PLAN.md)." -ForegroundColor Yellow
    }
}

if (-not $SkipInstaller) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($iscc -and (Test-Path "installer.iss")) {
        Step "Inno Setup ($iscc)"
        & $iscc "installer.iss"
        if ($LASTEXITCODE -ne 0) { Fail "ISCC failed (exit $LASTEXITCODE)." }
    } else {
        Write-Host "[!] ISCC.exe or installer.iss not found; skipping installer." -ForegroundColor Yellow
    }
}

if (-not $SkipZip) {
    Step "zip"
    $zip = Join-Path $root "MotionDeskStudio_COMPLETE_ALL_FEATURES.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $items = @((Join-Path $publishDir "*"))
    $setup = Join-Path $root "Output\OpsisSetup.exe"
    if (Test-Path $setup) { $items += $setup }
    Compress-Archive -Path $items -DestinationPath $zip
    Write-Host "[+] $zip" -ForegroundColor Green
}

Write-Host "[+] Done." -ForegroundColor Green
exit 0
