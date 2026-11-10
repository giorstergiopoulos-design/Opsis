<#
  Opsis resource baseline logger (standalone; run it yourself, it is not part of the app).

  Samples every -IntervalSec seconds the Opsis processes (Opsis, OpsisContainers, Opsis*/plugin hosts and the
  WebView2 processes started by them) and appends one CSV row per process per sample:
    time, process, pid, cpu_percent (of all cores), private_bytes_mb, working_set_mb, handles, threads,
    gdi_objects, user_objects

  Usage (PowerShell, any folder):
    powershell -ExecutionPolicy Bypass -File resource-logger.ps1 -DurationMin 60 -IntervalSec 5
    powershell -ExecutionPolicy Bypass -File resource-logger.ps1 -DurationMin 0      # 0 = until Ctrl+C
  Output: opsis-baseline-<yyyyMMdd-HHmmss>.csv next to the script (or -OutFile).
  Tip: run it once with the app idle for 10 min, then again during your normal use; compare the CSVs.
  It only reads performance counters / process info. It changes nothing and sends nothing.
#>
param(
    [int]$IntervalSec = 5,
    [int]$DurationMin = 30,
    [string]$OutFile = ""
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class OpsisGuiCounts {
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hProcess, uint flags);
}
'@

if (-not $OutFile) { $OutFile = Join-Path $PSScriptRoot ("opsis-baseline-{0}.csv" -f (Get-Date -Format "yyyyMMdd-HHmmss")) }
$names = @('Opsis', 'OpsisContainers', 'OpsisNewsCrawler')
$cores = [Environment]::ProcessorCount
$prev = @{}   # pid -> @{cpu=TotalProcessorTime ms; t=ticks}
"time,process,pid,cpu_percent,private_bytes_mb,working_set_mb,handles,threads,gdi_objects,user_objects" | Out-File $OutFile -Encoding ascii

function Get-Targets {
    $roots = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.ProcessName })
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($r in $roots) { [void]$ids.Add($r.Id) }
    # WebView2 (msedgewebview2) children of the Opsis processes
    $cim = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -ErrorAction SilentlyContinue
    foreach ($c in $cim) { if ($ids.Contains([int]$c.ParentProcessId)) { [void]$ids.Add([int]$c.ProcessId) } }
    # second level (renderer/gpu/utility are children of the browser process)
    foreach ($c in $cim) { if ($ids.Contains([int]$c.ParentProcessId)) { [void]$ids.Add([int]$c.ProcessId) } }
    foreach ($id in $ids) { Get-Process -Id $id -ErrorAction SilentlyContinue }
}

$end = if ($DurationMin -gt 0) { (Get-Date).AddMinutes($DurationMin) } else { [DateTime]::MaxValue }
Write-Host "Logging to $OutFile  (Ctrl+C to stop)"
while ((Get-Date) -lt $end) {
    $now = Get-Date
    $stamp = $now.ToString("yyyy-MM-dd HH:mm:ss")
    foreach ($p in (Get-Targets)) {
        try {
            $cpuMs = $p.TotalProcessorTime.TotalMilliseconds
            $cpu = 0.0
            if ($prev.ContainsKey($p.Id)) {
                $dt = ($now.Ticks - $prev[$p.Id].t) / 10000.0
                if ($dt -gt 0) { $cpu = [Math]::Round(100.0 * ($cpuMs - $prev[$p.Id].cpu) / $dt / $cores, 2) }
            }
            $prev[$p.Id] = @{ cpu = $cpuMs; t = $now.Ticks }
            $gdi = [OpsisGuiCounts]::GetGuiResources($p.Handle, 0)
            $usr = [OpsisGuiCounts]::GetGuiResources($p.Handle, 1)
            "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}" -f $stamp, $p.ProcessName, $p.Id, $cpu,
                [Math]::Round($p.PrivateMemorySize64 / 1MB, 1), [Math]::Round($p.WorkingSet64 / 1MB, 1),
                $p.HandleCount, $p.Threads.Count, $gdi, $usr | Out-File $OutFile -Append -Encoding ascii
        } catch { }
    }
    Start-Sleep -Seconds $IntervalSec
}
Write-Host "Done: $OutFile"
