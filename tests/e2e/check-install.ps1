# Checks the state of an Opsis installation on a clean Windows machine (used by .github/workflows/installer-e2e.yml, also runnable by hand).
#   -Mode installed   : everything the installer must have created
#   -Mode removed     : nothing may be left after the uninstall
param(
    [Parameter(Mandatory = $true)][ValidateSet('installed', 'removed')][string]$Mode,
    [string]$ExpectedVersion = '',
    [switch]$SkipAutostart          # the PREVIOUS release is checked without the autostart expectation of the new build
)
$ErrorActionPreference = 'Continue'
$failures = New-Object System.Collections.Generic.List[string]
function Check([string]$what, [bool]$ok) { if ($ok) { Write-Host "OK    $what" } else { Write-Host "FAIL  $what"; $failures.Add($what) } }

$appName = 'Opsis - Desktop Customization'      # the installer's folder / shortcut / autostart name (MyAppName in installer.iss)
$appDir = Join-Path ${env:ProgramFiles} $appName
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A9E8F765-1234-4567-89AB-CDEF01234567}_is1'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startMenu = @("$env:ProgramData\Microsoft\Windows\Start Menu\Programs", "$env:APPDATA\Microsoft\Windows\Start Menu\Programs")
$lnk = $startMenu | ForEach-Object { Get-ChildItem $_ -Recurse -Filter 'Opsis*.lnk' -ErrorAction SilentlyContinue } | Where-Object { $_ }
$perUser = Join-Path $env:LOCALAPPDATA "Programs\$appName"
function Get-OpsisRunValues { (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).PSObject.Properties | Where-Object { $_.Value -is [string] -and $_.Value -like '*Opsis.exe*' } }

if ($Mode -eq 'installed') {
    Check 'Program Files\Opsis\Opsis.exe exists' (Test-Path (Join-Path $appDir 'Opsis.exe'))
    Check 'DeskContainers engine (containers\OpsisContainers.exe) exists' (Test-Path (Join-Path $appDir 'containers\OpsisContainers.exe'))
    Check 'machine-wide uninstall entry exists' (Test-Path $uninstallKey)
    if ($ExpectedVersion -and (Test-Path $uninstallKey)) {
        $v = (Get-ItemProperty $uninstallKey).DisplayVersion
        Check "registered version is $ExpectedVersion (found $v)" ($v -eq $ExpectedVersion)
        $fv = (Get-Item (Join-Path $appDir 'Opsis.exe')).VersionInfo.FileVersion
        Check "Opsis.exe file version starts with $ExpectedVersion (found $fv)" ($fv -like "$ExpectedVersion*")
    }
    Check 'Start Menu shortcut exists (exactly one)' (@($lnk).Count -eq 1)
    if (-not $SkipAutostart) { Check 'autostart entry exists and points into Program Files' (@(Get-OpsisRunValues | Where-Object { $_.Value -like '*Program Files*' }).Count -ge 1) }
    Check 'no per-user copy left (LocalAppData\Programs\Opsis)' (-not (Test-Path $perUser))
    Check 'no OpsisContainers.exe still running from an old path' (-not (Get-Process OpsisContainers -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -notlike "$appDir*" }))
}
else {
    Check 'Program Files\Opsis is gone' (-not (Test-Path $appDir))
    Check 'uninstall entry is gone' (-not (Test-Path $uninstallKey))
    Check 'Start Menu shortcut is gone' (@($lnk).Count -eq 0)
    Check 'autostart entry is gone' (@(Get-OpsisRunValues).Count -eq 0)
    Check 'no Opsis process is running' (-not (Get-Process Opsis, OpsisContainers -ErrorAction SilentlyContinue))
    # user data under %AppData%\Opsis is kept on purpose (the uninstaller asks); it is not counted as a leftover
}
if ($failures.Count -gt 0) { Write-Host "`n$($failures.Count) check(s) failed"; exit 1 }
Write-Host "`nall checks passed"
