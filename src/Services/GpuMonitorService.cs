using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace MotionDesk.Services
{
    public sealed class GpuSnapshot
    {
        public string Name { get; set; } = "—";
        public double? LoadPercent { get; set; }
        public double? TemperatureC { get; set; }
        public double? CoreVoltage { get; set; }
        public double? MemoryUsedMb { get; set; }
        public bool Available { get; set; }
        public int Count { get; set; }
    }

    // Πραγματικά sensor data GPU (φόρτος/θερμοκρασία/τάση πυρήνα) μέσω LibreHardwareMonitorLib
    // (ανοιχτού κώδικα, MPL-2.0, ο διάδοχος του OpenHardwareMonitor) — ζητήθηκε ρητά "voltage της
    // GPU" στο System Monitor widget. Οι στάνταρ Win32 performance counters (αυτό που ήδη
    // χρησιμοποιεί το SystemMonitorService για CPU/RAM) ΔΕΝ εκθέτουν καθόλου τάση — μόνο μια
    // πραγματική βιβλιοθήκη hardware sensors μπορεί να τη διαβάσει. Ενεργοποιείται ΜΟΝΟ το GPU
    // (όχι CPU/motherboard) ώστε να ΜΗΝ χρειάζεται ο πυρηνικός driver (WinRing0) που απαιτεί
    // Administrator — τα GPU sensors διαβάζονται μέσω NVAPI/ADL/Intel vendor APIs, διαθέσιμα
    // συνήθως και χωρίς elevation. Αν κάποιος sensor δεν είναι διαθέσιμος σε αυτό το σύστημα,
    // γυρνάει null αντί να πετάξει exception — το widget δείχνει "—" σε αυτή την περίπτωση.
    public sealed class GpuMonitorService : IDisposable
    {
        // Lazy (thread-safe): το Instance προσπελαύνεται ΤΑΥΤΟΧΡΟΝΑ από το background warm-up Task και από
        // το UI thread — το απλό "??=" μπορούσε να δημιουργήσει ΔΥΟ Computer instances (διπλό, αργό
        // hardware scan και διπλά ανοιχτοί sensors).
        private static readonly Lazy<GpuMonitorService> _lazy = new(() => new GpuMonitorService());
        public static GpuMonitorService Instance => _lazy.Value;
        private readonly object _sync = new(); // το LibreHardwareMonitor ΔΕΝ είναι thread-safe

        private readonly Computer? _computer;
        private readonly UpdateVisitor _visitor = new();
        private readonly bool _opened;

        private GpuMonitorService()
        {
            try
            {
                _computer = new Computer { IsGpuEnabled = true };
                _computer.Open();
                _opened = true;
            }
            catch { _computer = null; _opened = false; }
        }

        /// <summary>Number of graphics adapters found (dedicated AND integrated).</summary>
        public int Count
        {
            get
            {
                try { lock (_sync) return _opened && _computer != null ? Adapters(_computer.Hardware.Select(h => (h.HardwareType, h.Name))).Count : 0; }
                catch { return 0; }
            }
        }

        /// <summary>Dedicated adapters (NVIDIA/AMD) first, then integrated (Intel), each group in the order the driver reports them. Pure, so it can be tested.</summary>
        public static System.Collections.Generic.List<(HardwareType Type, string Name)> Adapters(System.Collections.Generic.IEnumerable<(HardwareType Type, string Name)> all) =>
            all.Where(h => h.Type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
               .Select((h, i) => (h, i)).OrderBy(x => x.h.Type == HardwareType.GpuIntel ? 1 : 0).ThenBy(x => x.i).Select(x => x.h).ToList();

        public GpuSnapshot GetSnapshot() => GetSnapshot(0);

        /// <summary>Every graphics adapter (dedicated first, integrated last), e.g. for the wallpaper GPU element which lists them all.</summary>
        public System.Collections.Generic.List<GpuSnapshot> GetAllSnapshots()
        {
            var list = new System.Collections.Generic.List<GpuSnapshot>();
            int n = Math.Max(1, Count);
            for (int i = 0; i < n; i++) { var s = GetSnapshot(i); if (s.Available) list.Add(s); }
            return list;
        }

        /// <summary>Snapshot of the adapter at <paramref name="index"/> (wraps around, so a stored index stays valid when an adapter disappears).</summary>
        public GpuSnapshot GetSnapshot(int index)
        {
            var snap = new GpuSnapshot();
            if (!_opened || _computer == null) return snap;
            try
            {
                lock (_sync) _computer.Accept(_visitor);
                var gpus = _computer.Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    .Select((h, i) => (h, i)).OrderBy(x => x.h.HardwareType == HardwareType.GpuIntel ? 1 : 0).ThenBy(x => x.i).Select(x => x.h).ToList();
                if (gpus.Count == 0) return snap;
                snap.Count = gpus.Count;
                var gpu = gpus[((index % gpus.Count) + gpus.Count) % gpus.Count];

                snap.Name = gpu.Name;
                snap.Available = true;
                foreach (var sensor in gpu.Sensors)
                {
                    if (sensor.Value == null) continue;
                    switch (sensor.SensorType)
                    {
                        case SensorType.Load when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && snap.LoadPercent == null:
                            snap.LoadPercent = sensor.Value; break;
                        case SensorType.Temperature when snap.TemperatureC == null:
                            snap.TemperatureC = sensor.Value; break;
                        case SensorType.Voltage when snap.CoreVoltage == null:
                            snap.CoreVoltage = sensor.Value; break;
                        case SensorType.SmallData when sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) && snap.MemoryUsedMb == null:
                            snap.MemoryUsedMb = sensor.Value; break;
                    }
                }
            }
            catch { }
            return snap;
        }

        public void Dispose()
        {
            try { if (_opened) _computer?.Close(); } catch { }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer) => computer.Traverse(this);
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }
    }
}
