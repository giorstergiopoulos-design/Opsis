using System;

namespace OpsisContainers
{
    // Performance tier of the host (Opsis): on the Low tier the decorative animations of the containers (launch effects) are skipped. The tier arrives at start-up
    // in OPSIS_PERF_TIER and later through the bridge command "perf:<tier>" (dynamic scaling).
    internal static class OpsisPerf
    {
        public static volatile bool Reduced = string.Equals(Environment.GetEnvironmentVariable("OPSIS_PERF_TIER"), "Low", StringComparison.OrdinalIgnoreCase);
        public static void Set(string tier) => Reduced = string.Equals(tier, "Low", StringComparison.OrdinalIgnoreCase);
    }
}
