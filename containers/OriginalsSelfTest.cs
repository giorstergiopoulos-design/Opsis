using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OpsisContainers
{
    // `OpsisContainers.exe --selftest-originals <report.txt>` with OPSIS_APPDATA=<temp> AND OPSIS_DESKTOP_DIR=<temp desktop>: checks that a desktop icon that moved into a
    // container (hidden) comes back on every way a container can go away, also after the current directory changed (a file dialog does that). Never touches the real desktop.
    internal static class OriginalsSelfTest
    {
        public static int Run(string reportPath)
        {
            var sb = new StringBuilder(); int fail = 0;
            void Check(string what, bool ok) { if (!ok) fail++; sb.AppendLine((ok ? "OK    " : "FAIL  ") + what); }
            string desktop = Environment.GetEnvironmentVariable("OPSIS_DESKTOP_DIR") ?? "";
            if (string.IsNullOrEmpty(desktop) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPSIS_APPDATA")))
            { File.WriteAllText(reportPath, "needs OPSIS_APPDATA and OPSIS_DESKTOP_DIR\n"); return 2; }
            try
            {
                Directory.CreateDirectory(desktop);
                SettingsManager.HideDesktopOriginalsOnDrop = true;
                string profile = ProfileManager.CurrentProfileDir; Directory.CreateDirectory(Path.Combine(profile, "Shortcuts"));
                string orig = Path.Combine(desktop, "a.txt"); File.WriteAllText(orig, "x");
                string rel = Path.Combine("Shortcuts", "a.lnk"); File.WriteAllText(Path.Combine(profile, rel), "lnk");
                bool Hidden() => (File.GetAttributes(orig) & FileAttributes.Hidden) != 0;
                JObject Container(string id, string file) => new JObject { ["Id"] = id, ["Items"] = new JArray(new JObject { ["Filename"] = file }) };

                Directory.SetCurrentDirectory(profile);
                DesktopOriginals.OnDropped(rel, orig);
                Check("dropping hides the desktop original", Hidden());

                Directory.SetCurrentDirectory(desktop);        // a file dialog changed the current directory
                DesktopOriginals.Reconcile(new[] { Container("c1", rel) });
                Check("saving with the item still in a container keeps it hidden (even if the current directory changed)", Hidden());

                DesktopOriginals.RestoreFor(Container("c1", rel));
                Check("deleting the container (RestoreFor) shows the original", !Hidden());

                Directory.SetCurrentDirectory(profile);
                DesktopOriginals.OnDropped(rel, orig);
                Directory.SetCurrentDirectory(desktop);
                DesktopOriginals.Reconcile(new List<JObject>());
                Check("saving after the container is gone (Reconcile) shows the original", !Hidden());

                Directory.SetCurrentDirectory(profile);
                DesktopOriginals.OnDropped(rel, orig);
                DesktopOriginals.ShowAll();
                Check("engine exit / factory reset (ShowAll) shows the original", !Hidden());

                DesktopOriginals.OnDropped(rel, orig);
                File.Delete(orig); File.WriteAllText(orig, "y");   // original recreated: unhide must not throw
                DesktopOriginals.RestoreFor(Container("c1", rel));
                Check("restoring an original that was recreated does not fail", true);
            }
            catch (Exception ex) { fail++; sb.AppendLine("FAIL  exception " + ex); }
            sb.Insert(0, "ORIGINALS SELF-TEST: fail=" + fail + Environment.NewLine);
            File.WriteAllText(reportPath, sb.ToString());
            return fail == 0 ? 0 : 1;
        }
    }
}
