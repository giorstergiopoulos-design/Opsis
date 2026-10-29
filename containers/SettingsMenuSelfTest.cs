using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Desktop_Fences
{
    // `OpsisContainers.exe --selftest-settings <report.txt>` (use with an isolated OPSIS_APPDATA): builds the Settings menu, and for every checkable
    // entry flips it, simulates the click, and checks that options.json really changed (the "footprint"), then restores the original file.
    // It also checks that no entry shows a raw locale key. Commands (backup, restore...) are listed but never invoked.
    internal static class SettingsMenuSelfTest
    {
        private static string ReadRetry(string path) { for (int i = 0; i < 8; i++) { try { return File.Exists(path) ? File.ReadAllText(path) : ""; } catch (IOException) { System.Threading.Thread.Sleep(40); } } return ""; }
        private static void WriteRetry(string path, string text) { for (int i = 0; i < 8; i++) { try { File.WriteAllText(path, text); return; } catch (IOException) { System.Threading.Thread.Sleep(40); } } }

        public static int Run(string reportPath)
        {
            var sb = new StringBuilder();
            int ok = 0, fail = 0, skipped = 0;
            string optionsPath = ProfileManager.GetProfileFilePath("options.json");
            try { SettingsManager.SaveSettings(); } catch { }
            string original = ReadRetry(optionsPath);
            var menu = new ContextMenu();
            var root = SettingsMenu.Build(menu);
            void Visit(MenuItem item, string path)
            {
                string header = item.Header as string ?? "";
                string here = path + "/" + header;
                if (header.Length == 0 || header.StartsWith("DCM.") || header.StartsWith("Tray.")) { fail++; sb.AppendLine("FAIL  " + here + "  (raw or empty text)"); }
                if (item.HasItems) { foreach (var sub in item.Items) if (sub is MenuItem mi) Visit(mi, here); return; }
                if (!item.IsCheckable) { skipped++; sb.AppendLine("SKIP  " + here + "  (command, not invoked)"); return; }
                bool choice = (item.Tag as string) == "choice";
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));       // re-sync every check mark with the current settings
                if (choice && item.IsChecked) { skipped++; sb.AppendLine("SKIP  " + here + "  (already the selected choice)"); return; }
                try
                {
                    string before = ReadRetry(optionsPath);
                    item.IsChecked = choice ? true : !item.IsChecked;
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    string after = ReadRetry(optionsPath);
                    if (after != before) { ok++; sb.AppendLine("OK    " + here); }
                    else { fail++; sb.AppendLine("FAIL  " + here + "  (options.json did not change)"); }
                }
                catch (Exception ex) { fail++; sb.AppendLine("FAIL  " + here + "  (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                finally
                {
                    try { WriteRetry(optionsPath, original); SettingsManager.LoadSettings(); menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent)); } catch { }
                }
            }
            foreach (var top in root.Items) if (top is MenuItem m) Visit(m, "");
            sb.Insert(0, $"SETTINGS MENU SELF-TEST: ok={ok} fail={fail} skipped={skipped}{Environment.NewLine}");
            try { File.WriteAllText(reportPath, sb.ToString()); } catch { }
            return fail == 0 ? 0 : 1;
        }
    }
}
