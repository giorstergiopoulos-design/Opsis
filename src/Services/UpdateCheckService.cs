using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    // Ελέγχει τα GitHub releases του project για νεότερη έκδοση. Η ίδια λογική με το self-update
    // του GearWin (ζητήθηκε ρητά, portαρισμένο μέσω του ήδη δουλεμένου/ζωντανά δοκιμασμένου port
    // στο OpsisNewsCrawler — UpdateChecker.cs/UpdateInstaller.cs εκεί): μόνο το δημόσιο releases API,
    // μόνο asset αυτού του ίδιου repo (https, github.com, ακριβές path/filename), επαλήθευση SHA-256
    // πριν τρέξει οτιδήποτε. Ποτέ δεν κατεβάζει/τρέχει τίποτα από μόνο του χωρίς κλικ του χρήστη.
    public static class UpdateCheckService
    {
        public const string Repository = "giorstergiopoulos-design/Opsis";
        private const string SetupName = "OpsisSetup.exe";
        private static readonly string DownloadPrefix = $"https://github.com/{Repository}/releases/download/";

        // Ζητήθηκε ρητά (v2.0 handoff, update mechanism βάσει GearWin): έλεγχος σε ΚΑΘΕ εκκίνηση και
        // μετά κάθε ~12 ώρες — αντικαθιστά το προηγούμενο "μία φορά τον μήνα".
        public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(12);
        private static readonly string LatestReleaseApi = $"https://api.github.com/repos/{Repository}/releases/latest";
        private static readonly HttpClient Http = CreateClient();

        public sealed record UpdateInfo(Version Version, string PageUrl, string? SetupUrl, string? ChecksumUrl, string? Sha256, string Tag)
        {
            public bool CanInstallAutomatically => SetupUrl is not null && (Sha256 is not null || ChecksumUrl is not null);
        }

        public sealed record CheckResult(bool Succeeded, UpdateInfo? Update);

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Opsis-UpdateCheck");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

        public static async Task<CheckResult> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                var json = await Http.GetStringAsync(LatestReleaseApi, ct);
                return new CheckResult(true, Parse(json, CurrentVersion));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                return new CheckResult(false, null); // offline, rate-limited, no releases yet…
            }
            catch (Exception) { return new CheckResult(true, null); } // malformed/unexpected answer: treat as "no update", not "failed" (don't retry-loop forever on a permanent parse issue)
        }

        public static UpdateInfo? Parse(string json, Version current)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var latest) || latest <= current) return null;

            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : $"https://github.com/{Repository}/releases";
            if (!page.StartsWith($"https://github.com/{Repository}/", StringComparison.Ordinal)) return null;

            string? setup = null, sum = null, digest = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString();
                    var url = a.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
                    if (!IsTrustedDownloadUrl(url)) continue;
                    if (name == SetupName)
                    {
                        setup = url;
                        digest = ParseDigest(a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String ? dg.GetString() : null);
                    }
                    else if (name == SetupName + ".sha256") sum = url;
                }
            }
            return new UpdateInfo(latest, page, setup, sum, digest, tag);
        }

        // true ΜΟΝΟ για https://github.com/<αυτό το repo>/releases/download/... — τίποτα άλλο.
        public static bool IsTrustedDownloadUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com"
            && string.IsNullOrEmpty(u.UserInfo) && url!.StartsWith(DownloadPrefix, StringComparison.Ordinal);

        // "sha256:ABC..." -> "abc..." (64 hex digits), οτιδήποτε άλλο null.
        public static string? ParseDigest(string? digest)
        {
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
            var hex = digest[7..].Trim().ToLowerInvariant();
            return Regex.IsMatch(hex, "^[0-9a-f]{64}$") ? hex : null;
        }

        public static bool TryParseVersion(string text, out Version version)
        {
            text = text.Trim().TrimStart('v', 'V');
            var dash = text.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) text = text[..dash];
            return Version.TryParse(text, out version!);
        }

        // Έλεγχος "due" χρησιμοποιημένος από το tray: επιστρέφει update όταν υπάρχει ΚΑΙ το όριο
        // (ή η πρώτη φορά) το επιτρέπει.
        public static async Task<UpdateInfo?> CheckIfDueAsync()
        {
            var settings = AppSettings.Load();
            if (!settings.UpdateCheckEnabled) return null;
            if (DateTime.UtcNow - settings.LastUpdateCheckUtc < AutoCheckInterval) return null;
            var result = await CheckAsync();
            if (result.Succeeded)
            {
                var s = AppSettings.Load();
                s.LastUpdateCheckUtc = DateTime.UtcNow;
                s.Save();
            }
            return result.Update;
        }
    }
}
