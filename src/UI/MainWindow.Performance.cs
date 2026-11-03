using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    public sealed partial class MainWindow
    {
        // Performance: dynamic scaling switch and an optional tier for each part of Opsis (Auto = follows the detected tier and the dynamic scaling).
        private static void BuildPerformanceRows(Control panel)
        {
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Perf.Dynamic"), AppSettings.Load().PerfDynamic,
                v => { var a = AppSettings.Load(); a.PerfDynamic = v; a.Save(); PerformanceGovernor.RaiseChanged(); },
                LocalizationManager.T("Perf.DynamicDesc")));
            AddText(panel, LocalizationManager.T("Perf.PerFeature"));
            string[] labels =
            {
                LocalizationManager.T("Perf.Auto"), LocalizationManager.T("Perf.Low"), LocalizationManager.T("Perf.Balanced"), LocalizationManager.T("Perf.High")
            };
            foreach (var feature in new[] { PerfFeature.Wallpaper, PerfFeature.Widgets, PerfFeature.SystemMonitor, PerfFeature.DeskStrip, PerfFeature.Effects })
            {
                var f = feature;
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Perf.Feature." + f), AutoSize = false, Width = 230, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
                var combo = new Opsis.UI.FlatComboBox { Width = 180 };
                var current = PerformanceProfile.FeatureOverride(f);
                int idx = current == null ? 0 : (int)current.Value + 1;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    int i = Math.Max(0, combo.SelectedIndex);
                    PerformanceProfile.SetFeatureOverride(f, i == 0 ? null : (HardwareTier)(i - 1));
                };
                row.Controls.Add(combo);
                panel.Controls.Add(row);
            }
        }
    }
}
