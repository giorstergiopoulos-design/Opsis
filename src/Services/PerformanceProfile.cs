using System;
using System.Collections.Generic;

namespace MotionDesk.Services
{
    /// <summary>The parts of Opsis that scale with the machine. Each can be forced to its own tier in the settings.</summary>
    public enum PerfFeature { Wallpaper, Widgets, DeskStrip, DeskContainers, Clock, SystemMonitor, Effects }

    /// <summary>
    /// ONE table of what each hardware tier allows (the single place to tune), and the rules that pick the tier a feature really uses:
    /// the detected/forced base tier, lowered while the machine is under pressure (<see cref="PerformanceGovernor"/>), unless that feature has its own override.
    /// </summary>
    public sealed record PerformanceProfile(
        HardwareTier Tier,
        int WallpaperFps,          // frame cap of the canvas wallpapers
        double RenderScale,        // 1 = full resolution, 0.5 = quarter of the pixels (wallpaper canvas / mirrors)
        int WaveLayers,
        int GlowBlur,
        bool Shading,
        int MaxWebViews,           // WebView2 wallpaper windows alive at the same time (one per monitor)
        double RefreshScale,       // multiplier on the refresh interval of widgets / system monitor (2 = half as often)
        bool BlurEffects,          // Mica / acrylic / blur-behind
        bool Animations,           // dock magnification, container animations, decorative pulses
        bool HardwareDecode,       // let WebView2 / Media Foundation use the GPU for video
        bool Allow3D)              // the future 3D wallpaper (Low = static fallback)
    {
        public static PerformanceProfile For(HardwareTier tier) => tier switch
        {
            HardwareTier.Low => new(HardwareTier.Low, 15, 0.5, 2, 0, false, 1, 2.5, false, false, true, false),
            HardwareTier.High => new(HardwareTier.High, 60, 1.0, 4, 30, true, 4, 1.0, true, true, true, true),
            _ => new(HardwareTier.Balanced, 30, 1.0, 3, 16, true, 2, 1.0, true, true, true, false),
        };

        /// <summary>The profile one feature uses right now: its own override if the user set one, otherwise the effective tier (base tier lowered by the governor).</summary>
        public static PerformanceProfile ForFeature(PerfFeature feature)
        {
            var own = FeatureOverride(feature);
            return For(own ?? PerformanceGovernor.EffectiveTier);
        }

        /// <summary>Interval of a timer after the profile of its feature is applied: slower on weaker tiers, never below the base.</summary>
        public static int Interval(PerfFeature feature, int baseMs) => (int)Math.Round(baseMs * ForFeature(feature).RefreshScale);

        // 2.4.6.1: cache 2 s — το FeatureOverride καλείται από timers (WidgetEngine κ.ά.) και πριν διάβαζε/parse-άρε
        // ολόκληρο το settings.json σε κάθε κλήση.
        private static Dictionary<string, string>? _overrideCache;
        private static long _overrideCacheTick;
        private static readonly object _overrideLock = new();

        public static HardwareTier? FeatureOverride(PerfFeature feature)
        {
            try
            {
                Dictionary<string, string>? map;
                lock (_overrideLock)
                {
                    if (_overrideCacheTick == 0 || Environment.TickCount64 - _overrideCacheTick > 2000)
                    {
                        _overrideCache = AppSettings.Load().PerfFeatureTier;
                        _overrideCacheTick = Math.Max(1, Environment.TickCount64);
                    }
                    map = _overrideCache;
                }
                if (map != null && map.TryGetValue(feature.ToString(), out var raw) && Enum.TryParse<HardwareTier>(raw, out var t)) return t;
            }
            catch (Exception) { }
            return null;
        }

        public static void SetFeatureOverride(PerfFeature feature, HardwareTier? tier)
        {
            var a = AppSettings.Load();
            a.PerfFeatureTier ??= new Dictionary<string, string>();
            if (tier == null) a.PerfFeatureTier.Remove(feature.ToString()); else a.PerfFeatureTier[feature.ToString()] = tier.Value.ToString();
            a.Save();
            lock (_overrideLock) { _overrideCacheTick = 0; }
            PerformanceGovernor.RaiseChanged();
        }
    }

    /// <summary>One sample of how busy the machine is (percent, 0-100). Null = not measured.</summary>
    public readonly record struct LoadSample(double? Cpu, double? Gpu, double? Ram, bool OnBattery, bool LowBattery);

    /// <summary>
    /// Dynamic scaling: when the machine stays under pressure for 10 s the effective tier drops one step (never below Low); it climbs back one step
    /// after the load has stayed calm for 45 s. The two thresholds and the different delays are the hysteresis that keeps it from flickering.
    /// Disabled while the user forces a tier (Settings -> Performance) or switched dynamic scaling off.
    /// </summary>
    public static class PerformanceGovernor
    {
        public static readonly TimeSpan DegradeAfter = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan RestoreAfter = TimeSpan.FromSeconds(45);

        public static event Action<HardwareTier>? EffectiveChanged;

        private static readonly object Sync = new();
        private static int _drop;                    // how many steps below the base tier
        private static DateTime? _pressureSince, _calmSince;
        private static System.Threading.Timer? _timer;

        public static bool IsPressure(LoadSample s) =>
            (s.Cpu ?? 0) > 88 || (s.Gpu ?? 0) > 92 || (s.Ram ?? 0) > 92 || s.LowBattery;

        public static bool IsCalm(LoadSample s) =>
            (s.Cpu ?? 0) < 60 && (s.Gpu ?? 0) < 75 && (s.Ram ?? 0) < 85 && !s.LowBattery;

        /// <summary>The tier after lowering <paramref name="baseTier"/> by <paramref name="drop"/> steps.</summary>
        public static HardwareTier Lower(HardwareTier baseTier, int drop) => (HardwareTier)Math.Max(0, (int)baseTier - Math.Max(0, drop));

        public static HardwareTier EffectiveTier
        {
            get { lock (Sync) return Lower(HardwareProfileService.CurrentTier, _drop); }
        }

        /// <summary>Pure state step (testable): returns the new drop after one sample taken at <paramref name="now"/>.</summary>
        internal static int Step(int drop, HardwareTier baseTier, LoadSample s, DateTime now, ref DateTime? pressureSince, ref DateTime? calmSince)
        {
            int maxDrop = (int)baseTier;               // Low cannot go lower
            if (IsPressure(s))
            {
                calmSince = null; pressureSince ??= now;
                if (drop < maxDrop && now - pressureSince >= DegradeAfter) { drop++; pressureSince = now; }
            }
            else if (IsCalm(s))
            {
                pressureSince = null; calmSince ??= now;
                if (drop > 0 && now - calmSince >= RestoreAfter) { drop--; calmSince = now; }
            }
            else { pressureSince = null; calmSince = null; }      // in between: hold the current level
            return drop;
        }

        public static bool Enabled => HardwareProfileService.GetOverride() == null && AppSettings.Load().PerfDynamic;

        /// <summary>Feeds one sample (called by the sampling timer, or by tests through <see cref="Step"/>).</summary>
        public static void Feed(LoadSample s, DateTime now)
        {
            HardwareTier before, after;
            lock (Sync)
            {
                before = Lower(HardwareProfileService.CurrentTier, _drop);
                if (!Enabled) { _drop = 0; _pressureSince = _calmSince = null; }
                else _drop = Step(_drop, HardwareProfileService.CurrentTier, s, now, ref _pressureSince, ref _calmSince);
                after = Lower(HardwareProfileService.CurrentTier, _drop);
            }
            if (before != after) EffectiveChanged?.Invoke(after);
        }

        public static void RaiseChanged() => EffectiveChanged?.Invoke(EffectiveTier);

        /// <summary>Starts the sampler (every 2 s, cheap: CPU and RAM counters Opsis already reads; no GPU query unless a monitor widget uses it).</summary>
        public static void Start()
        {
            if (_timer != null) return;
            HardwareProfileService.ProfileChanged += _ => { lock (Sync) { _drop = 0; _pressureSince = _calmSince = null; } RaiseChanged(); };
            _timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    var m = SystemMonitorService.Instance.GetSnapshot();
                    double ram = m.TotalMemoryMb > 0 ? (m.TotalMemoryMb - m.AvailableMemoryMb) / m.TotalMemoryMb * 100.0 : 0;
                    var p = System.Windows.Forms.SystemInformation.PowerStatus;
                    bool onBattery = p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
                    bool low = onBattery && p.BatteryLifePercent > 0 && p.BatteryLifePercent < 0.2f;
                    Feed(new LoadSample(m.CpuPercent, null, ram, onBattery, low), DateTime.UtcNow);
                }
                catch (Exception) { }
            }, null, 2000, 2000);
        }
    }
}
