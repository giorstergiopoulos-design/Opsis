using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace MotionDesk.Services
{
    public sealed class PowerPlanInfo
    {
        public Guid Guid { get; init; }
        public string Name { get; init; } = "";
        public bool IsActive { get; init; }
    }

    // Backing service for the "Quick Settings" widget (ήχος/φωτεινότητα/σχέδιο ενέργειας σε ένα
    // πάνελ) — ζητήθηκε ρητά SeelenUI-inspired, το δικό τους "quick-settings" popup. Κάθε κομμάτι
    // χρησιμοποιεί ήδη τεκμηριωμένα APIs, όχι κάτι εύθραυστο/χωρίς επίσημη υποστήριξη.
    public static class SystemControlService
    {
        // ---- Ήχος (NAudio, ήδη εξάρτηση του project — IAudioEndpointVolume στο προεπιλεγμένο
        // audio endpoint, το ίδιο που αλλάζει και ο μίκτης έντασης των Windows) ----
        private static MMDevice? GetDefaultRenderDevice()
        {
            try
            {
                using var en = new MMDeviceEnumerator();
                return en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (Exception) { return null; }
        }

        public static float GetVolume()
        {
            using var dev = GetDefaultRenderDevice();
            try { return dev?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f; }
            catch (Exception) { return 0f; }
        }

        public static void SetVolume(float scalar01)
        {
            using var dev = GetDefaultRenderDevice();
            try { if (dev != null) dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar01, 0f, 1f); }
            catch (Exception) { }
        }

        public static bool GetMute()
        {
            using var dev = GetDefaultRenderDevice();
            try { return dev?.AudioEndpointVolume.Mute ?? false; }
            catch (Exception) { return false; }
        }

        public static void SetMute(bool mute)
        {
            using var dev = GetDefaultRenderDevice();
            try { if (dev != null) dev.AudioEndpointVolume.Mute = mute; }
            catch (Exception) { }
        }

        // ---- Φωτεινότητα οθόνης (WMI, root\WMI\WmiMonitorBrightness(Methods)) — λειτουργεί μόνο
        // για οθόνες που εκθέτουν αυτή την κλάση (σχεδόν πάντα laptop panels· σπάνια εξωτερικές
        // οθόνες μέσω DDC/CI). Αν δεν υπάρχει καμία instance, η κάρτα απλώς κρύβει το slider —
        // ίδιο μοτίβο "αν δεν υποστηρίζεται, κρύψου" με τα Dependencies της εφαρμογής.
        public static bool IsBrightnessSupported()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
                return searcher.Get().Count > 0;
            }
            catch (Exception) { return false; }
        }

        public static int GetBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementObject mo in searcher.Get())
                    return Convert.ToInt32(mo["CurrentBrightness"]);
            }
            catch (Exception) { }
            return 50;
        }

        public static void SetBrightness(int percent0To100)
        {
            try
            {
                int v = Math.Clamp(percent0To100, 0, 100);
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject mo in searcher.Get())
                    mo.InvokeMethod("WmiSetBrightness", new object[] { 0 /* timeout */, (byte)v });
            }
            catch (Exception) { }
        }

        // ---- Σχέδιο ενέργειας (powercfg — ήδη ορισμένα schemes, καμία ανάγκη για elevation
        // για να ενεργοποιηθεί ένα από αυτά, μόνο για να δημιουργηθεί/διαγραφεί ένα νέο) ----
        public static List<PowerPlanInfo> GetPowerPlans()
        {
            var plans = new List<PowerPlanInfo>();
            try
            {
                // ΔΙΟΡΘΩΣΗ "κείμενο σαν αλαμπουρνέζικα" στα ελληνικά ονόματα σχεδίων ενέργειας
                // ("Εξισορρόπηση" κ.λπ.): η κονσόλα των Windows (άρα και η έξοδος του powercfg)
                // γράφει στη σελίδα κωδικών OEM του ΣΥΣΤΗΜΑΤΟΣ (π.χ. 737 για ελληνικά Windows), ΟΧΙ
                // στην προεπιλεγμένη κωδικοποίηση που χρησιμοποιεί το .NET όταν δεν οριστεί ρητά
                // StandardOutputEncoding. ΠΡΩΤΗ προσπάθεια διόρθωσης χρησιμοποιούσε
                // `CultureInfo.CurrentCulture.TextInfo.OEMCodePage` — live-verified ΛΑΘΟΣ (ακόμα
                // αλαμπουρνέζικα): το CurrentCulture ενός WinForms process δεν ταυτίζεται αξιόπιστα
                // με το πραγματικό system locale/OEM codepage. Η πραγματική, σωστή πηγή είναι η ίδια
                // η Win32 GetOEMCP() — επιβεβαιωμένο με χειροκίνητη δοκιμή: τα raw bytes της εξόδου
                // του powercfg αποκωδικοποιούνται σωστά ΜΟΝΟ με codepage 737, που αυτό ακριβώς
                // επιστρέφει η GetOEMCP() σε ελληνικά Windows, ανεξάρτητα από το .NET CurrentCulture.
                var oemEncoding = System.Text.Encoding.GetEncoding(GetOEMCP());
                var psi = new ProcessStartInfo("powercfg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = oemEncoding };
                psi.ArgumentList.Add("/list");
                using var proc = Process.Start(psi);
                if (proc == null) return plans;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);
                // Παράδειγμα γραμμής: "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *"
                foreach (var line in output.Split('\n'))
                {
                    int gi = line.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);
                    if (gi < 0) continue;
                    var rest = line[(gi + 5)..].Trim();
                    var guidPart = rest.Split(' ', '\t')[0];
                    if (!Guid.TryParse(guidPart, out var guid)) continue;
                    int nameStart = rest.IndexOf('(');
                    int nameEnd = rest.IndexOf(')');
                    string name = (nameStart >= 0 && nameEnd > nameStart) ? rest[(nameStart + 1)..nameEnd] : guidPart;
                    bool active = rest.TrimEnd().EndsWith("*");
                    plans.Add(new PowerPlanInfo { Guid = guid, Name = name, IsActive = active });
                }
            }
            catch (Exception) { }
            return plans;
        }

        public static void SetActivePowerPlan(Guid guid)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("/setactive");
                psi.ArgumentList.Add(guid.ToString());
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            catch (Exception) { }
        }

        [DllImport("kernel32.dll")]
        private static extern int GetOEMCP();
    }
}
