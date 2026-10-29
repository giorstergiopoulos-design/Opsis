using System;
using System.Collections.Generic;

namespace MotionDesk.Widgets
{
    // Data shapes of the legacy built-in DeskContainers (removed in 2.2.0). They are kept only so ContainersMigration can read the old
    // %AppData%\Opsis\deskcontainers\container*_config.json files once and carry the containers over to the new engine.
    public sealed class ContainerItem
    {
        public string Path { get; set; } = "";
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public int OpenCount { get; set; }
    }

    public sealed class ContainerTabState
    {
        public string Name { get; set; } = "Tab";
        public List<ContainerItem> Items { get; set; } = new();
    }

    public class DeskContainerState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? Title { get; set; }
        public string[]? Paths { get; set; }
        public List<ContainerItem>? Items { get; set; }
        public bool IsLocked { get; set; }
        public bool IsRolledUp { get; set; }
        public bool ExcludeFromQuickHide { get; set; }
        public bool PlaceNewIconsHereByDefault { get; set; }
        public int AccentColorArgb { get; set; }
        public double WindowOpacity { get; set; } = 1.0;
        public string SortMode { get; set; } = "None";
        public string AutoCollectExtensions { get; set; } = "";
        public string TitleFontFamily { get; set; } = "Segoe UI";
        public float TitleFontSize { get; set; } = 12f;
        public string? PortalFolder { get; set; }
        public int IconPx { get; set; } = 32;
        public bool ShowLabels { get; set; } = true;
        public List<ContainerTabState>? Tabs { get; set; }
        public int ActiveTab { get; set; }
        public bool IsNote { get; set; }
        public string? NoteText { get; set; }
    }
}
