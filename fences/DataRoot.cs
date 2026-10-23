using System;
using System.IO;

namespace Desktop_Fences
{
    // Opsis integration: DesktopFences originally kept all data next to the exe (portable). Opsis installs under
    // Program Files (read-only), so all data lives in %AppData%\Opsis\fences (overridable via OPSIS_FENCES_DATA).
    internal static class DataRoot
    {
        public static readonly string Dir = Resolve();

        private static string Resolve()
        {
            string? env = Environment.GetEnvironmentVariable("OPSIS_FENCES_DATA");
            string dir = !string.IsNullOrWhiteSpace(env)
                ? env
                : Path.Combine(Environment.GetEnvironmentVariable("OPSIS_APPDATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opsis"), "fences");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
