using System.Text.Json.Nodes;
using MotionDesk.Services;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public sealed class FencesMigrationTests
{
    [Fact]
    public void Convert_MapsItemsTabsPortalAndNote_AndScalesPixelsToDips()
    {
        var states = new List<DeskContainerState>
        {
            new() { Title = "Games", X = 300, Y = 150, Width = 600, Height = 300, IsLocked = true,
                Items = new List<ContainerItem> { new() { Path = @"C:\Games\a.lnk" }, new() { Path = "::spacer::abcd" } } },
            new() { Title = "Tabs", Width = 400, Height = 200, Tabs = new List<ContainerTabState>
                { new() { Name = "", Items = new() { new ContainerItem { Path = @"C:\x.txt" } } }, new() { Name = "Work", Items = new() } } },
            new() { Title = "Docs", Width = 400, Height = 200, PortalFolder = @"C:\Docs" },
            new() { Title = "Note", Width = 300, Height = 200, IsNote = true, NoteText = "hello" },
        };

        var arr = JsonNode.Parse(FencesMigration.Convert(states, 1.5))!.AsArray();

        Assert.Equal(4, arr.Count);
        Assert.Equal("Data", (string)arr[0]!["ItemsType"]!);
        Assert.Equal(200.0, (double)arr[0]!["X"]!);                    // 300 px / 1.5
        Assert.Equal("true", (string)arr[0]!["IsLocked"]!);
        Assert.Equal(2, arr[0]!["Items"]!.AsArray().Count);                // the item and the blank spacer
        Assert.StartsWith("INTERNAL_BLANK_", (string)arr[0]!["Items"]![1]!["Filename"]!);
        Assert.Equal("Medium", (string)arr[0]!["IconSize"]!);
        Assert.Equal("Medium", (string)arr[0]!["TitleTextSize"]!);
        Assert.True((bool)arr[0]!["Items"]![0]!["IsLink"]!);
        Assert.Equal("true", (string)arr[1]!["TabsEnabled"]!);
        Assert.Equal("Work", (string)arr[1]!["Tabs"]![1]!["TabName"]!);
        Assert.Equal("Tabs", (string)arr[1]!["Tabs"]![0]!["TabName"]!); // unnamed first tab takes the container title
        Assert.Equal("Portal", (string)arr[2]!["ItemsType"]!);
        Assert.Equal(@"C:\Docs", (string)arr[2]!["Path"]!);
        Assert.Equal("Note", (string)arr[3]!["ItemsType"]!);
        Assert.Equal("hello", (string)arr[3]!["NoteContent"]!);
    }
}
