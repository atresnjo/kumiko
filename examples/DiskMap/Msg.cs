using System.Collections.Immutable;
using DiskMap.Core;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiskMap;

internal abstract partial record Msg
{
    public sealed record Navigate(HistoryRequest Request) : Msg;
    public sealed record Progress(long Generation, ScanProgress Value) : Msg;
    /// <summary>A scan of the folder at Names below the root finished.</summary>
    public sealed record Scanned(long Generation, ImmutableArray<string> Names, Item Item, TimeSpan Time) : Msg;
    public sealed record ScanFailed(long Generation, string Error) : Msg;
    public sealed record VolumeRead(Volume? Volume) : Msg;
    /// <summary>The view measured the treemap at a new size.</summary>
    public sealed record Resized(float Width, float Height) : Msg;
    /// <summary>Selects an item by names below the open folder; empty clears the selection.</summary>
    public sealed record Select(ImmutableArray<string> Names) : Msg;
    /// <summary>Opens a folder by names below the open folder; a file selects it instead.</summary>
    public sealed record Open(ImmutableArray<string> Names) : Msg;
    public sealed record OpenSelected : Msg;
    public sealed record Up : Msg;
    /// <summary>Opens the folder at a depth of the breadcrumb (0 is the root).</summary>
    public sealed record Crumb(int Depth) : Msg;
    /// <summary>Moves the selection through the open folder's items in list order.</summary>
    public sealed record Step(int Delta) : Msg;
    public sealed record SetMetric(Metric Metric) : Msg;
    public sealed record SetFilter(string Filter) : Msg;
    public sealed record FocusFilter : Msg;
    public sealed record ToggleMark : Msg;
    public sealed record ClearMarks : Msg;
    public sealed record OpenMenu(ImmutableArray<string> Names, Point At) : Msg;
    public sealed record CloseMenu : Msg;
    public sealed record Reveal(string Path) : Msg;
    public sealed record OpenItem(string Path) : Msg;
    /// <summary>Asks to move the marked items, or the selection when nothing is marked, to the Trash.</summary>
    public sealed record AskTrash : Msg;
    public sealed record ConfirmTrash : Msg;
    public sealed record CancelTrash : Msg;
    public sealed record Trashed(ImmutableArray<string> Paths, string? Error) : Msg;
    /// <summary>Scans the open folder again, keeping the map on screen until it finishes.</summary>
    public sealed record Rescan : Msg;
    public sealed record ChooseFolder : Msg;
    public sealed record Chosen(string? Path) : Msg;
    public sealed record SetHelp(bool Shown) : Msg;
    public sealed record Noted(string? Notice) : Msg;
}
