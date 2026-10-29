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

        /// <summary>
        /// Opens the downloaded setup so that NOBODY can change or delete it afterwards (FileShare.Read), and checks its SHA-256 from that very handle.
        /// The setup runs elevated (installer in Program Files), so a gap between "hash checked" and "file executed" would let any user-level process
        /// swap it (TOCTOU). The returned stream must stay open until the setup process has been created (the running image is then write-protected).
        /// Throws InvalidDataException when the hash does not match (the stream is closed first).
        /// </summary>
        internal static FileStream OpenVerified(string setupPath, string sha256)
        {
            var fs = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using var sha = SHA256.Create();
                var actual = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("hash-mismatch");
                fs.Position = 0;
                return fs;
            }
            catch { fs.Dispose(); throw; }
        }

        // Verifies the setup through a locked handle and starts it right away (silent; /CLOSEAPPLICATIONS lets the installer close Opsis through its
        // AppMutex, see installer.iss) - the lock is released only after the process exists. A small hidden PowerShell helper then waits for the setup
        // to finish and starts Opsis again when it succeeded; it plays no part in the integrity check. The caller terminates this process right after.
        public static void LaunchAfterExit(string setupPath, string sha256)
        {
            static string Q(string s) => "'" + s.Replace("'", "''") + "'";
            var exe = Environment.ProcessPath ?? "";
            using var locked = OpenVerified(setupPath, sha256);
            var setup = Process.Start(new ProcessStartInfo(setupPath)
            {
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true,                       // lets the installer's manifest ask for elevation (UAC)
                WorkingDirectory = Path.GetDirectoryName(setupPath)!
            });
            if (setup == null) return;
            var script = new StringBuilder()
                .AppendLine("$ErrorActionPreference = 'SilentlyContinue'")
                .AppendLine($"try {{ Wait-Process -Id {setup.Id} -Timeout 900 }} catch {{ }}")
                .AppendLine($"if (Test-Path -LiteralPath {Q(exe)}) {{ Start-Process -FilePath {Q(exe)} }}")
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
