using System.Drawing;

namespace MotionDesk.UI
{
    // Colour tokens of the shared Opsis.UI controls: a live adapter on Opsis' own theme (UiTheme/ThemeManager), nothing is copied.
    internal sealed class OpsisUiTokens : Opsis.UI.IOpsisTheme
    {
        public bool IsDark => ThemeManager.Current.IsDark;
        public Color Background => UiTheme.Background;
        public Color Surface => UiTheme.Surface;
        public Color Border => UiTheme.Border;
        public Color Text => UiTheme.TextPrimary;
        public Color TextMuted => UiTheme.TextSecondary;       // the former controls used TextSecondary for the chevron / disabled text
        public Color Accent => UiTheme.AccentCyan;
        public Color SurfaceHover => UiTheme.SurfaceHover;

        private static bool _installed;

        /// <summary>Installs the tokens once, before the first control paints, and forwards theme changes to the library.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Opsis.UI.OpsisTheme.Use(new OpsisUiTokens());
            ThemeManager.Changed += () => Opsis.UI.OpsisTheme.NotifyChanged();
            ThemeManager.Repainted += () => Opsis.UI.OpsisTheme.NotifyChanged();
        }
    }
}
