using System.Text.RegularExpressions;

namespace MotionDesk.Widgets
{
    /// <summary>Turns what the user typed for the "Page" / "Image" wallpaper modes into a URI the wallpaper page can load ("" = nothing usable).</summary>
    public static class MediaSource
    {
        private static readonly Regex YouTubeId = new(
            @"(?:youtu\.be/|youtube(?:-nocookie)?\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/|live/|v/))([A-Za-z0-9_-]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ImageUri(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            path = path.Trim().Trim('"');
            return File.Exists(path) ? new Uri(path).AbsoluteUri : "";
        }

        public static string PageUri(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            source = source.Trim().Trim('"');

            var yt = YouTubeId.Match(source);
            if (yt.Success)
            {
                string id = yt.Groups[1].Value;
                return $"https://www.youtube-nocookie.com/embed/{id}?autoplay=1&mute=1&loop=1&playlist={id}&controls=0&modestbranding=1&rel=0&playsinline=1&iv_load_policy=3&disablekb=1&cc_load_policy=0";
            }
            if (File.Exists(source)) return new Uri(source).AbsoluteUri;
            if (Directory.Exists(source))
            {
                string index = Path.Combine(source, "index.html");
                return File.Exists(index) ? new Uri(index).AbsoluteUri : "";
            }
            if (Uri.TryCreate(source, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)) return u.AbsoluteUri;
            // "example.com/clock" -> https://example.com/clock
            if (!source.Contains(' ') && source.Contains('.') && Uri.TryCreate("https://" + source, UriKind.Absolute, out var guess)) return guess.AbsoluteUri;
            return "";
        }
    }
}
