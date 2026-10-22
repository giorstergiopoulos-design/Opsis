using System.Text.Json;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public class AudioEqProfileTests
{
    private static string[] Lines(string text) => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToArray();

    [Fact]
    public void Export_matches_the_waveframe_golden_output_exactly()
    {
        // Same input as test/audiolib.test.js in the Waveframe repo: bass boost preset + a -3 dB top band.
        var p = new AudioEqProfile { Name = "Bass boost", Bands = new double[] { 8, 7, 5, 2, 0, 0, 0, 0, 0, -3 } };
        var lines = Lines(p.ToEqualizerApo());
        Assert.Equal(new[]
        {
            "Preamp: -8.0 dB",
            "Filter 1: ON LSC Fc 32 Hz Gain 8.0 dB Q 0.70",
            "Filter 2: ON PK Fc 64 Hz Gain 7.0 dB Q 1.41",
            "Filter 3: ON PK Fc 125 Hz Gain 5.0 dB Q 1.41",
            "Filter 4: ON PK Fc 250 Hz Gain 2.0 dB Q 1.41",
            "Filter 5: ON HSC Fc 16000 Hz Gain -3.0 dB Q 0.70",
        }, lines);
    }

    [Fact]
    public void Flat_profile_exports_preamp_zero_and_no_filters()
    {
        var lines = Lines(new AudioEqProfile().ToEqualizerApo());
        Assert.Equal(new[] { "Preamp: 0.0 dB" }, lines);
        Assert.True(new AudioEqProfile().IsFlat);
    }

    [Fact]
    public void Bass_boost_counts_into_the_headroom_and_adds_a_shelf()
    {
        var p = new AudioEqProfile { Bands = new double[] { 4, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, BassBoostDb = 6 };
        var text = p.ToEqualizerApo();
        Assert.Contains("Preamp: -10.0 dB", text);
        Assert.Contains("ON LSC Fc 100 Hz Gain 6.0 dB", text);
    }

    [Fact]
    public void Gains_are_clamped_to_plus_minus_12()
    {
        var p = new AudioEqProfile { Bands = new double[] { 30, -30, 0, 0, 0, 0, 0, 0, 0, 0 } };
        var text = p.ToEqualizerApo();
        Assert.Contains("Gain 12.0 dB", text);
        Assert.Contains("Gain -12.0 dB", text);
        Assert.Contains("Preamp: -12.0 dB", text);
    }

    [Fact]
    public void Export_then_import_roundtrips_every_builtin_preset()
    {
        foreach (var b in AudioEqProfile.BuiltIn)
        {
            var original = new AudioEqProfile { Name = b.En, Bands = (double[])b.Bands.Clone() };
            var back = AudioEqProfile.FromEqualizerApo(original.ToEqualizerApo(), b.En);
            Assert.Equal(original.Bands, back.Bands);
            Assert.Empty(back.ExtraLines);
        }
    }

    [Fact]
    public void Builtin_presets_are_the_sixteen_waveframe_presets()
    {
        Assert.Equal(16, AudioEqProfile.BuiltIn.Count);
        Assert.All(AudioEqProfile.BuiltIn, b => Assert.Equal(10, b.Bands.Length));
        Assert.Equal(new double[] { 8, 7, 5, 2, 0, 0, 0, 0, 0, 0 }, AudioEqProfile.BuiltIn.First(b => b.Key == "bassBoost").Bands);
    }

    [Fact]
    public void Import_keeps_only_safe_eq_lines_and_never_includes_or_plugins()
    {
        const string evil = "Include: C:\\Windows\\evil.txt\nConvolution: C:\\x.wav\nVSTPlugin: Library C:\\evil.dll\nCopy: L=R\n" +
                            "Preamp: -3 dB\nFilter 1: ON PK Fc 1000 Hz Gain 4 dB Q 1.41\nFilter 2: OFF PK Fc 2000 Hz Gain 9 dB Q 1\n" +
                            "Filter 3: ON PK Fc 3000 Hz Gain 2.5 dB Q 2\nFilter 4: ON PK Fc 999999 Hz Gain 2 dB Q 1\n";
        var p = AudioEqProfile.FromEqualizerApo(evil, "x");
        Assert.Equal(4, p.Bands[5]);                              // 1 kHz becomes a band
        Assert.Single(p.ExtraLines);                              // 3 kHz is not one of the ten: kept as an extra filter
        Assert.Contains("Fc 3000 Hz", p.ExtraLines[0]);
        var roundTrip = p.ToEqualizerApo();
        Assert.DoesNotContain("Include", roundTrip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Convolution", roundTrip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VSTPlugin", roundTrip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("999999", roundTrip);
        Assert.DoesNotContain("OFF", roundTrip);
    }

    [Fact]
    public void Import_reads_a_waveframe_user_preset_json()
    {
        const string json = "{\"name\":\"My night\",\"bands\":[3,2,1,0,1,2,1,1,2,2],\"bassBoost\":2.5,\"volumeBoost\":0,\"surround\":0,\"compressor\":false}";
        var p = AudioEqProfile.Import(json, "fallback");
        Assert.NotNull(p);
        Assert.Equal("My night", p!.Name);
        Assert.Equal(new double[] { 3, 2, 1, 0, 1, 2, 1, 1, 2, 2 }, p.Bands);
        Assert.Equal(2.5, p.BassBoostDb);
    }

    [Fact]
    public void Import_reads_a_list_of_presets_and_takes_the_first()
    {
        var p = AudioEqProfile.Import("[{\"name\":\"A\",\"bands\":[1,1,1,1,1,1,1,1,1,1]},{\"name\":\"B\",\"bands\":[0,0,0,0,0,0,0,0,0,0]}]", "x");
        Assert.Equal("A", p!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("{ not json")]
    [InlineData("{\"foo\":1}")]
    [InlineData("# just a comment\nCopy: L=R")]
    public void Import_returns_null_for_garbage(string content) => Assert.Null(AudioEqProfile.Import(content, "x"));

    [Fact]
    public void Profiles_survive_appsettings_json_serialization()
    {
        var p = new AudioEqProfile { Name = "Mine", Bands = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, BassBoostDb = 3, ExtraLines = { "Filter: ON PK Fc 3000 Hz Gain 2.5 dB Q 2.00" } };
        var back = JsonSerializer.Deserialize<AudioEqProfile>(JsonSerializer.Serialize(p))!;
        Assert.Equal(p.Bands, back.Bands);
        Assert.Equal(p.ExtraLines, back.ExtraLines);
        Assert.Equal("Mine", back.Name);
    }
}

[Collection("ApoConfig")]
public class EqualizerApoServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "opsis_apo_test_" + Guid.NewGuid().ToString("N")[..8]);
    public EqualizerApoServiceTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.txt"), "# my own config\nPreamp: -1 dB\n");
        EqualizerApoService.ConfigDirOverride = _dir;
    }
    public void Dispose() { EqualizerApoService.ConfigDirOverride = null; try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void ApplyProfile_writes_our_include_file_and_leaves_the_users_config_intact()
    {
        var p = new AudioEqProfile { Bands = new double[] { 6, 4, 0, 0, 0, 0, 0, 0, 0, 0 } };
        var (ok, _) = EqualizerApoService.ApplyProfile(p);
        Assert.True(ok);
        var cfg = File.ReadAllText(Path.Combine(_dir, "config.txt"));
        Assert.StartsWith("# my own config\nPreamp: -1 dB\n", cfg);          // untouched
        Assert.Equal(1, cfg.Split('\n').Count(l => l.Trim() == "Include: Opsis.txt"));
        var inc = File.ReadAllText(Path.Combine(_dir, "Opsis.txt"));
        Assert.Contains("Preamp: -6.0 dB", inc);
        Assert.Contains("Filter 1: ON LSC Fc 32 Hz Gain 6.0 dB", inc);
    }

    [Fact]
    public void Applying_twice_does_not_duplicate_the_include_line()
    {
        EqualizerApoService.ApplyProfile(new AudioEqProfile { Bands = new double[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 } });
        EqualizerApoService.ApplyProfile(new AudioEqProfile { Bands = new double[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0 } });
        var cfg = File.ReadAllText(Path.Combine(_dir, "config.txt"));
        Assert.Equal(1, cfg.Split('\n').Count(l => l.Trim() == "Include: Opsis.txt"));
        Assert.Contains("Gain 2.0 dB", File.ReadAllText(Path.Combine(_dir, "Opsis.txt")));
    }

    [Fact]
    public void The_legacy_motiondesk_include_is_migrated()
    {
        File.WriteAllText(Path.Combine(_dir, "config.txt"), "Include: MotionDesk.txt\nPreamp: -1 dB\n");
        File.WriteAllText(Path.Combine(_dir, "MotionDesk.txt"), "old");
        EqualizerApoService.ApplyProfile(new AudioEqProfile());
        var cfg = File.ReadAllText(Path.Combine(_dir, "config.txt"));
        Assert.DoesNotContain("MotionDesk.txt", cfg);
        Assert.False(File.Exists(Path.Combine(_dir, "MotionDesk.txt")));
        Assert.Contains("Include: Opsis.txt", cfg);
    }

    [Fact]
    public void ApplyProfile_reports_failure_when_equalizer_apo_is_not_installed()
    {
        EqualizerApoService.ConfigDirOverride = Path.Combine(_dir, "does-not-exist");
        var (ok, _) = EqualizerApoService.ApplyProfile(new AudioEqProfile());
        Assert.False(ok);
    }
}
