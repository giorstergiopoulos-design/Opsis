<#
  build-all.ps1 - pull + build + installer for MotionDeskStudio, GearWin, waveframe.
  Usage:   powershell -ExecutionPolicy Bypass -File build-all.ps1 [-Only MotionDesk,GearWin,Waveframe] [-SkipPull] [-NoInstaller]
  Edit the paths/branches in $Projects below. A project stops at its first error; the next one still runs.
  Does not replace the manual click-through test before a release (see CLAUDE.md).
#>
param(
    [string[]]$Only,
    [switch]$SkipPull,
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'
$Iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"

# --- CONFIGURE HERE ---
$Projects = @(
    @{ Name='MotionDesk'; Path=(Join-Path $env:USERPROFILE 'Documents\MotionDeskStudio'); Branch='claude/pensive-carson-sls1n6' },
    @{ Name='GearWin';    Path=(Join-Path $env:USERPROFILE 'Documents\GearWin');          Branch='claude/full-audit' },
    @{ Name='Waveframe';  Path=(Join-Path $env:USERPROFILE 'Documents\waveframe');        Branch='claude/full-audit' }
)

function Run($exe, $argList) {
    & $exe @argList
    if ($LASTEXITCODE -ne 0) { throw "$exe $($argList -join ' ') -> exit code $LASTEXITCODE" }
}

function Update-Repo($p) {
    if ($SkipPull) { return }
    Push-Location $p.Path
    try {
        if (git status --porcelain --untracked-files=no) { throw "Local changes in $($p.Path) - commit/stash first (pull skipped)." }
        Run git @('fetch','origin',$p.Branch)
        Run git @('checkout',$p.Branch)
        Run git @('pull','--ff-only','origin',$p.Branch)
    } finally { Pop-Location }
}

function Build-MotionDesk($p) {
    Push-Location $p.Path
    try {
        # A running MotionDesk.exe locks bin\Release\...\MotionDesk.exe and fails the build
        $running = Get-Process -Name 'MotionDesk' -ErrorAction SilentlyContinue
        if ($running) { Write-Host 'Closing running MotionDesk.exe before build...' -ForegroundColor Yellow; $running | Stop-Process -Force; Start-Sleep -Seconds 2 }
        Run dotnet @('build','MotionDeskStudio.csproj','-c','Release')
        # NO -o: installer.iss reads bin\Release\net8.0-windows10.0.19041.0\win-x64\publish
        Run dotnet @('publish','MotionDeskStudio.csproj','-c','Release','-r','win-x64','--self-contained','false')
        if (-not $NoInstaller) { Run $Iscc @('installer.iss'); "-> $($p.Path)\Output\MotionDeskStudioSetup.exe" }
    } finally { Pop-Location }
}

function Build-GearWin($p) {
    Push-Location $p.Path
    try {
        Run dotnet @('test','wpf\OptimizerWpf.Tests','-c','Release')
        # self-contained (as documented in OptimizerWpf.iss) - no .NET install needed on the target PC; OptimizerWpf.iss expects wpf\OptimizerWpf\publish\win-x64
        Run dotnet @('publish','wpf\OptimizerWpf','-c','Release','-r','win-x64','--self-contained','true','-o','wpf\OptimizerWpf\publish\win-x64')
        if (-not $NoInstaller) { Run $Iscc @('installer\OptimizerWpf.iss'); "-> $($p.Path)\installer\Output\" }
    } finally { Pop-Location }
}

function Build-Waveframe($p) {
    Push-Location $p.Path
    try {
        Run npm @('ci')
        Run npm @('run','smoke-test')
        if (-not $NoInstaller) { Run npm @('run','dist'); "-> $($p.Path)\dist\" }
    } finally { Pop-Location }
}

$results = @()
foreach ($p in $Projects) {
    if ($Only -and ($Only -notcontains $p.Name)) { continue }
    Write-Host "`n=== $($p.Name) ===" -ForegroundColor Cyan
    try {
        Update-Repo $p
        & "Build-$($p.Name)" $p
        $results += "OK     $($p.Name)"
    } catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        $results += "FAILED $($p.Name): $($_.Exception.Message)"
    }
}
Write-Host "`n=== Summary ===" ; $results | ForEach-Object { Write-Host $_ }
if ($results -match '^FAILED') { exit 1 }
