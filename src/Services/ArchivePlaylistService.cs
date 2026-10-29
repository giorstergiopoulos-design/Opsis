using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MotionDesk.Widgets;

namespace MotionDesk.Services
{
    // Ζητήθηκε ρητά "δυνατότητα φόρτωσης video μεμονωμένων ή playlists με αρχεία zip/7zip" —
    // εξάγει ένα .zip/.7z σε φάκελο κάτω από το AppData και επιστρέφει τα βίντεο που βρέθηκαν
    // μέσα (αναδρομικά), ώστε να προστεθούν στο Wallpaper Library σαν να τα είχε προσθέσει ο
    // χρήστης ένα-ένα ή μέσω "Προσθήκη φακέλου".
    public static class ArchivePlaylistService
    {
        public static readonly string[] SupportedArchiveExtensions = { ".zip", ".7z" };

        private static string ExtractRootDir => Path.Combine(AppPaths.AppDataFolder, "extracted_playlists");

        public static bool IsArchive(string path) =>
            SupportedArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        // .7z δεν έχει built-in υποστήριξη στο .NET (σε αντίθεση με .zip, System.IO.Compression) —
        // αντί να προστεθεί μια νέα, βαριά εξάρτηση (π.χ. SevenZipSharp) μόνο για αυτό, γίνεται
        // shell-out στο ήδη εγκατεστημένο 7-Zip του χρήστη, ίδιο μοτίβο με το FFmpeg
        // (WmvConversionService.FindFfmpeg) — ένα προαιρετικό εξωτερικό εργαλείο, όχι bundled.
        public static string? FindSevenZip()
        {
            string[] candidates =
            {
                "7z",
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
            };
            foreach (var candidate in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo(candidate, "-h")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    using var p = Process.Start(psi);
                    if (p != null && p.WaitForExit(2000) && p.ExitCode is 0 or 1) return candidate;
                }
                catch { }
            }
            return null;
        }

        public static bool IsSevenZipAvailable => FindSevenZip() != null;

        public static void OpenSevenZipDownloadPage()
        {
            try { Process.Start(new ProcessStartInfo("https://www.7-zip.org/download.html") { UseShellExecute = true }); }
            catch { }
        }

        // Εξάγει το αρχείο (zip ή 7z) και επιστρέφει όλα τα αρχεία βίντεο που βρέθηκαν μέσα,
        // αναδρομικά. Γυρνάει άδεια λίστα (όχι exception) αν η εξαγωγή αποτύχει ή δεν βρέθηκαν
        // βίντεο, ώστε ο caller να συνεχίσει κανονικά με τα υπόλοιπα επιλεγμένα αρχεία.
        public static List<string> ExtractVideos(string archivePath)
        {
            var result = new List<string>();
            try
            {
                string destDir = Path.Combine(ExtractRootDir, Path.GetFileNameWithoutExtension(archivePath) + "_" + QuickHash(archivePath));
                Directory.CreateDirectory(destDir);

                bool extracted = string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase)
                    ? ExtractZip(archivePath, destDir)
                    : ExtractSevenZip(archivePath, destDir);

                if (!extracted) return result;

                foreach (var file in Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories))
                {
                    if (WallpaperSettings.SupportedVideoExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        result.Add(file);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException) { }
            return result;
        }

        private static bool ExtractZip(string archivePath, string destDir)
        {
            try { SafeZip.Extract(archivePath, destDir); return true; }
            catch (Exception ex) { RotatingLogger.Write("zip extraction refused: " + ex.Message); return false; }
        }

        private static bool ExtractSevenZip(string archivePath, string destDir)
        {
            string? sevenZip = FindSevenZip();
            if (sevenZip == null) return false;
            try
            {
                // ArgumentList (όχι interpolated Arguments string) — μια διαδρομή που περιέχει " θα έσπαγε
                // το parsing της γραμμής εντολών (ή, χειρότερα, θα μπορούσε να εισάγει επιπλέον ορίσματα).
                var psi = new ProcessStartInfo(sevenZip) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("x");
                psi.ArgumentList.Add(archivePath);
                psi.ArgumentList.Add("-o" + destDir);
                psi.ArgumentList.Add("-y");
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                // Το 7z γράφει συνεχώς λίστα αρχείων στο stdout — αν το pipe δεν διαβάζεται γεμίζει και
                // το 7z μπλοκάρει για πάντα (το UI thread περιμένει εδώ). Αδειάζουμε και τα δύο ασύγχρονα.
                _ = proc.StandardOutput.ReadToEndAsync();
                _ = proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();
                return proc.ExitCode == 0;
            }
            catch { return false; }
        }

        private static string QuickHash(string path)
        {
            try
            {
                var info = new FileInfo(path);
                string raw = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                using var sha = System.Security.Cryptography.SHA1.Create();
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
                return Convert.ToHexString(bytes)[..10];
            }
            catch { return Guid.NewGuid().ToString("N")[..10]; }
        }
    }
}
