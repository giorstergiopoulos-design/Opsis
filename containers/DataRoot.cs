using System;
using System.IO;

namespace OpsisContainers
{
    // Opsis integration: the original project kept all data next to the exe (portable). Opsis installs under
    // Program Files (read-only), so all data lives in %AppData%\Opsis\containers (overridable via OPSIS_CONTAINERS_DATA).
    internal static class DataRoot
    {
        public static readonly string Dir = Resolve();

        private static string Resolve()
        {
            string? env = Environment.GetEnvironmentVariable("OPSIS_CONTAINERS_DATA");
            string dir;
            if (!string.IsNullOrWhiteSpace(env)) dir = env;
            else
            {
                string root = Environment.GetEnvironmentVariable("OPSIS_APPDATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opsis");
                dir = Path.Combine(root, "containers");
                // one-time move of the folder used by 2.2.0 pre-releases ("containers") to the new name
                string old = Path.Combine(root, "containers");
                try { if (!Directory.Exists(dir) && Directory.Exists(old)) Directory.Move(old, dir); } catch (Exception) { }
            }
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
