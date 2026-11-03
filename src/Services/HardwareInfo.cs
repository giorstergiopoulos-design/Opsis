using System;
using System.Linq;
using System.Management;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    /// <summary>What the machine is, in one cheap snapshot (no LibreHardwareMonitor): used for the tier decision log and the diagnostics.</summary>
    public sealed record HardwareInfo(long RamMb, int LogicalCores, int PhysicalCores, string[] Adapters, int Monitors, long TotalPixels, bool OnBattery, string PowerPlan)
    {
        public static HardwareInfo Detect()
        {
            int physical = Environment.ProcessorCount;
            try
            {
                using var s = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
                int sum = 0; foreach (ManagementObject mo in s.Get()) sum += Convert.ToInt32(mo["NumberOfCores"]);
                if (sum > 0) physical = sum;
            }
            catch (Exception) { }
            string[] adapters = Array.Empty<string>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                adapters = s.Get().Cast<ManagementObject>().Select(m => m["Name"] as string ?? "").Where(n => n.Length > 0).ToArray();
            }
            catch (Exception) { }
            long pixels = 0; int monitors = 1;
            try { var all = Screen.AllScreens; monitors = all.Length; pixels = all.Sum(x => (long)x.Bounds.Width * x.Bounds.Height); } catch (Exception) { }
            bool battery = false;
            try { battery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline; } catch (Exception) { }
            return new HardwareInfo(HardwareProfileService.TotalRamMb(), Environment.ProcessorCount, physical, adapters, monitors, pixels, battery, "");
        }

        public override string ToString() =>
            $"RAM {RamMb} MB, cores {PhysicalCores}/{LogicalCores}, GPUs [{string.Join("; ", Adapters)}], {Monitors} monitor(s) {TotalPixels / 1_000_000.0:0.0} Mpx, battery={OnBattery}";
    }
}
