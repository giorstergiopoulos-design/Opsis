<#
  Προσομοίωση "φτηνού laptop" (300-400 EUR: 8GB RAM, 2 πυρήνες/4 νήματα Celeron-Pentium-Ryzen3-i3 κλάσης)
  πάνω σε ισχυρότερο μηχάνημα, μέσω Windows Job Object που περιορίζει ΟΛΗ τη διεργασία Opsis ΚΑΙ τα
  παιδιά της (WebView2):
    - affinity σε N λογικούς πυρήνες (προεπιλογή 2)
    - CPU hard cap = N * CpuSpeedFactor (προεπιλογή 0.5 => περίπου μισή ταχύτητα ανά πυρήνα)
    - όριο μνήμης συνόλου job (προεπιλογή 3072 MB, κατά προσέγγιση ό,τι μένει σε 8GB μετά τα Windows)
  + μεταβλητές OPSIS_SIM_RAM_MB / OPSIS_SIM_DGPU ώστε η ανίχνευση προφίλ να δει "8GB, χωρίς κάρτα γραφικών"
  + απομονωμένος φάκελος δεδομένων (OPSIS_APPDATA) - δεν αγγίζει τις πραγματικές ρυθμίσεις.
  Μετράει: χρόνο εκκίνησης, CPU % της προσομοιωμένης δυναμικότητας, μέγιστη RAM (Opsis + WebView2),
  καθυστέρηση απόκρισης UI (WM_NULL ping). Δεν είναι ακριβής μίμηση hardware (cache/θερμική/δίσκος
  διαφέρουν) αλλά πιάνει τα προβλήματα που φαίνονται όταν οι πόροι μειωθούν δραστικά.
#>
param(
  [string]$Exe = "$PSScriptRoot\..\bin\Release\net10.0-windows10.0.19041.0\win-x64\Opsis.exe",
  [int]$DurationSec = 90,
  [int]$LogicalCores = 2,
  [double]$CpuSpeedFactor = 0.5,
  [int]$MemLimitMB = 3072,
  [string]$WallpaperMode = "Weather",
  [switch]$Baseline,            # χωρίς περιορισμούς, για σύγκριση
  [string]$Report = "$PSScriptRoot\..\LowEndSim-report.md"
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Job {
  [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong a,b,c,d,e,f; }
  [StructLayout(LayoutKind.Sequential)] struct BASIC { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinWs, MaxWs; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
  [StructLayout(LayoutKind.Sequential)] struct EXT { public BASIC Basic; public IO_COUNTERS Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
  [StructLayout(LayoutKind.Sequential)] struct CPURATE { public uint ControlFlags; public uint CpuRate; }
  [DllImport("kernel32.dll")] static extern IntPtr CreateJobObject(IntPtr a, string n);
  [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr j, int c, IntPtr p, int l);
  [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
  public static IntPtr Create(ulong affinityMask, ulong jobMemBytes, uint cpuRate1e4) {
    IntPtr j = CreateJobObject(IntPtr.Zero, null);
    EXT e = new EXT();
    e.Basic.LimitFlags = 0x10 | 0x200;            // AFFINITY | JOB_MEMORY
    e.Basic.Affinity = new UIntPtr(affinityMask);
    e.JobMemoryLimit = new UIntPtr(jobMemBytes);
    int sz = Marshal.SizeOf(typeof(EXT)); IntPtr p = Marshal.AllocHGlobal(sz);
    Marshal.StructureToPtr(e, p, false);
    if (!SetInformationJobObject(j, 9, p, sz)) throw new Exception("ext limits failed " + Marshal.GetLastWin32Error());
    CPURATE c = new CPURATE(); c.ControlFlags = 0x1 | 0x4; c.CpuRate = cpuRate1e4;   // ENABLE | HARD_CAP
    int sz2 = Marshal.SizeOf(typeof(CPURATE)); IntPtr p2 = Marshal.AllocHGlobal(sz2);
    Marshal.StructureToPtr(c, p2, false);
    if (!SetInformationJobObject(j, 15, p2, sz2)) throw new Exception("cpu rate failed " + Marshal.GetLastWin32Error());
    return j;
  }
  public static bool Assign(IntPtr j, IntPtr proc) { return AssignProcessToJobObject(j, proc); }
}
public static class Ping {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  public static int CountWindows(uint pid) { int n = 0; EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) n++; return true; }, IntPtr.Zero); return n; }
  // Μέγιστη καθυστέρηση (ms) απάντησης σε WM_NULL από τα ορατά παράθυρα της διεργασίας· -1 = κρέμασμα (>2s)
  public static double MaxLatencyMs(uint pid) {
    double worst = 0;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      IntPtr r; var sw = System.Diagnostics.Stopwatch.StartNew();
      IntPtr ok = SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 2 /*ABORTIFHUNG*/, 2000, out r);
      sw.Stop(); double ms = ok == IntPtr.Zero ? 2000 : sw.Elapsed.TotalMilliseconds; if (ms > worst) worst = ms; return true; }, IntPtr.Zero);
    return worst;
  }
}
"@

$exe = (Resolve-Path $Exe).Path
$data = Join-Path $env:TEMP ("opsis-sim-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $data | Out-Null
# Realistic ρύθμιση: ζωντανό wallpaper + System Monitor widget ενεργά από την εκκίνηση.
'{"StartWallpaperWithWindows": true, "ShowSystemMonitor": true, "PluginsEnabled": false, "AutoPerformanceMode": true}' | Set-Content "$data\settings.json" -Encoding UTF8

('{"Mode":"' + $WallpaperMode + '"}') | Set-Content "$data\wallpaper.json" -Encoding UTF8
$total = [Environment]::ProcessorCount
$env:OPSIS_APPDATA = $data
if (-not $Baseline) { $env:OPSIS_SIM_RAM_MB = "8192"; $env:OPSIS_SIM_DGPU = "0" }

$sw = [Diagnostics.Stopwatch]::StartNew()
$mask = [uint64]((1 -shl $LogicalCores) - 1)
if ($Baseline) { $proc = Start-Process $exe -PassThru }
else {
  # Το affinity πρέπει να ισχύει ΠΡΙΝ ξεκινήσει το .NET runtime (διαβάζει ProcessorCount στην εκκίνηση),
  # γι' αυτό η εκκίνηση γίνεται μέσω "start /affinity" και το job ανατίθεται αμέσως μετά.
  Start-Process cmd.exe -ArgumentList ('/c start "" /affinity ' + $mask.ToString("X") + ' "' + $exe + '"') -WindowStyle Hidden
  $proc = $null
  for ($k = 0; $k -lt 100 -and -not $proc; $k++) { Start-Sleep -Milliseconds 50; $proc = Get-Process Opsis -ErrorAction SilentlyContinue | Select-Object -First 1 }
  if (-not $proc) { throw "Opsis did not start" }
}
if (-not $Baseline) {
  $rate = [uint32][math]::Round(10000.0 * $LogicalCores * $CpuSpeedFactor / $total)
  $job = [Job]::Create($mask, [uint64]$MemLimitMB * 1MB, $rate)
  if (-not [Job]::Assign($job, $proc.Handle)) { throw "AssignProcessToJobObject failed" }
}
$capCores = if ($Baseline) { $total } else { $LogicalCores * $CpuSpeedFactor }

$startup = $null
while ($sw.Elapsed.TotalSeconds -lt 60 -and -not $startup) { if ([Ping]::CountWindows($proc.Id) -gt 0) { $startup = $sw.Elapsed.TotalSeconds }; Start-Sleep -Milliseconds 100 }

function Tree($rootPid) {
  $all = Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId
  $ids = New-Object System.Collections.Generic.HashSet[int]; [void]$ids.Add($rootPid)
  do { $n = $ids.Count; foreach ($p in $all) { if ($ids.Contains([int]$p.ParentProcessId)) { [void]$ids.Add([int]$p.ProcessId) } } } while ($ids.Count -gt $n)
  $ids
}

$samples = @(); $lat = @(); $prevCpu = $null; $prevT = [Diagnostics.Stopwatch]::StartNew(); $peakWs = 0
for ($i = 0; $i -lt $DurationSec; $i++) {
  Start-Sleep -Seconds 1
  $ids = Tree $proc.Id
  $cpu = 0.0; $ws = 0
  foreach ($id in $ids) { $p = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($p) { $cpu += $p.TotalProcessorTime.TotalSeconds; $ws += $p.WorkingSet64 } }
  if ($prevCpu -ne $null) { $dt = $prevT.Elapsed.TotalSeconds; $samples += (($cpu - $prevCpu) / $dt) / $capCores * 100.0 }
  $prevCpu = $cpu; $prevT.Restart()
  if ($ws -gt $peakWs) { $peakWs = $ws }
  $lat += [Ping]::MaxLatencyMs($proc.Id)
}
$tier = (Get-Content "$data\logs\opsis.log" -ErrorAction SilentlyContinue | Select-String "Hardware:" | Select-Object -Last 1)
$alive = -not $proc.HasExited
$kids = Tree $proc.Id
foreach ($id in $kids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
Remove-Item Env:OPSIS_APPDATA, Env:OPSIS_SIM_RAM_MB, Env:OPSIS_SIM_DGPU -ErrorAction SilentlyContinue

function Pct($a, $q) { $s = $a | Sort-Object; if (-not $s) { return 0 }; $s[[math]::Min($s.Count - 1, [int][math]::Floor($q * $s.Count))] }
$label = if ($Baseline) { "Baseline ($total logical cores, no limits)" } else { "Low-end sim ($LogicalCores logical cores @ x$CpuSpeedFactor speed, 8GB RAM profile, job mem $MemLimitMB MB)" }
$line = @"

### $label
- Run: $(Get-Date -Format s), $DurationSec s, survived: $alive
- Detected: $tier
- Time to first visible window: $([math]::Round($startup, 1)) s
- CPU use of simulated capacity: median $([math]::Round((Pct $samples 0.5),1)) %, p95 $([math]::Round((Pct $samples 0.95),1)) %, max $([math]::Round(($samples | Measure-Object -Maximum).Maximum,1)) %
- Peak RAM (Opsis + WebView2): $([math]::Round($peakWs / 1MB)) MB
- UI response (WM_NULL ping): median $([math]::Round((Pct $lat 0.5),1)) ms, p95 $([math]::Round((Pct $lat 0.95),1)) ms, worst $([math]::Round(($lat | Measure-Object -Maximum).Maximum,1)) ms (2000 = hang)
"@
Add-Content $Report $line -Encoding UTF8
$line
