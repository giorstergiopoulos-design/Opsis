using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MotionDesk.Services
{
    // Η πραγματική εγκατάσταση μιας νεότερης έκδοσης — ίδια λογική με το self-update του GearWin,
    // portαρισμένη από το ήδη ζωντανά δοκιμασμένο UpdateInstaller.cs του OpsisNewsCrawler. Το setup
    // έρχεται ΜΟΝΟ από τα releases αυτού του repo, επαληθεύεται με SHA-256 (digest που δημοσιεύει
    // το ίδιο το GitHub, με fallback το .sha256 asset) και διαγράφεται αν δεν ταιριάζει· τρέχει
    // ΜΟΝΟ αφού το Opsis έχει κλείσει (ο installer αντικαθιστά τα τρέχοντα αρχεία), σιωπηλά, και το
    // Opsis ξανανοίγει μετά.
    public static class UpdateInstallerService
    {
        private const long MaxSetupBytes = 150L * 1024 * 1024;

        public static string UpdateDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Opsis", "update");

        // Κατεβάζει+επαληθεύει. Πετάει InvalidOperationException όταν δεν υπάρχει τίποτα να
        // επαληθευτεί απέναντι, InvalidDataException σε αναντιστοιχία hash (το αρχείο διαγράφεται).
        public static async Task<(string Path, string Sha256)> DownloadAsync(
            UpdateCheckService.UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (update.SetupUrl is null || !UpdateCheckService.IsTrustedDownloadUrl(update.SetupUrl))
                throw new InvalidOperationException("untrusted-url");

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Opsis-Update");

            var expected = update.Sha256;
            if (expected is null && update.ChecksumUrl is not null && UpdateCheckService.IsTrustedDownloadUrl(update.ChecksumUrl))
            {
                var text = await http.GetStringAsync(update.ChecksumUrl, ct);
                var first = text.Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
                if (first.Length == 64 && first.All(Uri.IsHexDigit)) expected = first;
            }
            if (expected is null) throw new InvalidOperationException("no-digest");

            Directory.CreateDirectory(UpdateDir);
            foreach (var old in Directory.GetFiles(UpdateDir))
            { try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            var path = Path.Combine(UpdateDir, "OpsisSetup.exe");

            using var response = await http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            if (total > MaxSetupBytes) throw new InvalidDataException("too-large");

            using var sha = SHA256.Create();
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, ct)) > 0)
                {
                    read += n;
                    if (read > MaxSetupBytes) throw new InvalidDataException("too-large");
                    await target.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    if (total > 0) progress?.Report(Math.Min(1.0, read / (double)total));
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }
            var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (actual != expected)
            {
                try { File.Delete(path); } catch (IOException) { }
                throw new InvalidDataException("hash-mismatch");
            }
            return (path, actual);
        }

        // Ξεκινάει έναν κρυφό PowerShell helper που περιμένει το Opsis να κλείσει, ξαναελέγχει το
        // hash, τρέχει το setup σιωπηλά (/CLOSEAPPLICATIONS κλείνει ό,τι κρατάει ακόμα το AppMutex,
        // βλ. installer.iss), και το ξανανοίγει αν πέτυχε. Ο καλών πρέπει να τερματίσει τη
        // διεργασία αμέσως μετά (ίδιο μοτίβο με το ήδη δουλεμένο OpsisNewsCrawler).
        public static void LaunchAfterExit(string setupPath, string sha256)
        {
            static string Q(string s) => "'" + s.Replace("'", "''") + "'";
            var exe = Environment.ProcessPath ?? "";
            var script = new StringBuilder()
                .AppendLine("$ErrorActionPreference = 'Stop'")
                .AppendLine($"try {{ Wait-Process -Id {Environment.ProcessId} -Timeout 120 -ErrorAction SilentlyContinue }} catch {{ }}")
                .AppendLine($"if ((Get-FileHash -LiteralPath {Q(setupPath)} -Algorithm SHA256).Hash.ToLowerInvariant() -ne {Q(sha256)}) {{ exit 2 }}")
                .AppendLine($"$p = Start-Process -FilePath {Q(setupPath)} -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS' -Wait -PassThru")
                .AppendLine($"if ($p.ExitCode -eq 0 -and (Test-Path -LiteralPath {Q(exe)})) {{ Start-Process -FilePath {Q(exe)} }}")
                .ToString();
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", script },
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
    }
}
