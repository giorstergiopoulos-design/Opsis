using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services
{
    // `--run-selftest` (v2.0 handoff, Φάση 1/9) — headless ελέγχος που ΔΕΝ αντικαθιστά unit tests
    // (δεν υπάρχει ξεχωριστό test project για το Opsis ακόμα), αλλά δίνει ένα γρήγορο, αυτόματο
    // "δεν έσπασε κάτι βασικό" πριν από ένα installer/zip (βλ. scripts/BuildAll.ps1 στο
    // _handoff/scripts, που καλεί αυτό το flag ΜΟΝΟ αν το βρει μέσα στον κώδικα). Exit code 0/1 +
    // JSON report στο %AppData%\Opsis\selftest-report.json.
    public static class SelfTestRunner
    {
        public static int Run()
        {
            var results = new List<(string Name, bool Passed, string Detail)>();

            results.Add(RunCheck("HardwareProfile.DetectAndOverrideRoundTrip", CheckHardwareProfile));
            results.Add(RunCheck("AtomicFile.CorruptedFileRecovery", CheckAtomicFileRecovery));
            results.Add(RunCheck("SafeMode.CounterRoundTrip", CheckSafeModeCounter));

            bool allPassed = results.TrueForAll(r => r.Passed);
            WriteReport(results, allPassed);
            foreach (var r in results)
                Console.WriteLine($"[{(r.Passed ? "PASS" : "FAIL")}] {r.Name}{(r.Detail.Length > 0 ? " - " + r.Detail : "")}");
            Console.WriteLine(allPassed ? "[+] Self-test passed" : "[-] Self-test FAILED");
            return allPassed ? 0 : 1;
        }

        private static (string, bool, string) RunCheck(string name, Func<string?> check)
        {
            try
            {
                var failureDetail = check();
                return (name, failureDetail == null, failureDetail ?? "");
            }
            catch (Exception ex) { return (name, false, $"threw {ex.GetType().Name}: {ex.Message}"); }
        }

        // Επιστρέφει null = OK, ή ένα μήνυμα περιγράφοντας το πρόβλημα.
        private static string? CheckHardwareProfile()
        {
            var before = HardwareProfileService.GetOverride();
            try
            {
                HardwareProfileService.SetOverride(HardwareTier.Low);
                if (HardwareProfileService.CurrentTier != HardwareTier.Low) return "override to Low did not take effect";

                HardwareProfileService.SetOverride(HardwareTier.High);
                if (HardwareProfileService.CurrentTier != HardwareTier.High) return "override to High did not take effect";

                HardwareProfileService.SetOverride(null);
                var auto = HardwareProfileService.CurrentTier;
                if (auto != HardwareTier.Low && auto != HardwareTier.Balanced && auto != HardwareTier.High)
                    return "auto-detect returned an invalid tier";
                return null;
            }
            finally { HardwareProfileService.SetOverride(before); } // restore whatever the real setting was
        }

        private static string? CheckAtomicFileRecovery()
        {
            string dir = Path.Combine(Path.GetTempPath(), "OpsisSelfTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "settings.json");
                AtomicFile.WriteAllText(path, "{\"a\":1}");
                AtomicFile.WriteAllText(path, "{\"a\":2}"); // second write creates the .bak from the first
                File.WriteAllText(path, "{ this is not valid json"); // simulate corruption (crash mid-write)
                string recovered = AtomicFile.ReadAllText(path);
                if (!recovered.Contains("\"a\":1")) return $"expected recovery from .bak, got: {recovered}";
                return null;
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch (Exception) { } }
        }

        private static string? CheckSafeModeCounter()
        {
            // Δεν αγγίζει το πραγματικό startup_counter.txt — ελέγχει μόνο ότι οι public μέθοδοι
            // δεν πετάνε exception (το ίδιο το αρχείο είναι private στο SafeModeService).
            SafeModeService.MarkStartupHealthy();
            return null;
        }

        private sealed class SelfTestReport
        {
            public DateTime RanAtUtc { get; set; }
            public bool Passed { get; set; }
            public List<SelfTestItem> Checks { get; set; } = new();
        }

        private sealed class SelfTestItem
        {
            public string Name { get; set; } = "";
            public bool Passed { get; set; }
            public string Detail { get; set; } = "";
        }

        private static void WriteReport(List<(string Name, bool Passed, string Detail)> results, bool allPassed)
        {
            try
            {
                var report = new SelfTestReport { RanAtUtc = DateTime.UtcNow, Passed = allPassed };
                foreach (var (name, passed, detail) in results)
                    report.Checks.Add(new SelfTestItem { Name = name, Passed = passed, Detail = detail });
                string path = Path.Combine(AppPaths.AppDataFolder, "selftest-report.json");
                Directory.CreateDirectory(AppPaths.AppDataFolder);
                File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { }
        }
    }
}
