using System.Collections.Immutable;
using Kumiko.iOS;

internal sealed record CatalogModel(bool Notifications = true, float Volume = .4f, double Quantity = 2,
    string Segment = "day", string Choice = "work", string Search = "", string Notes = "A native multiline editor.\nTry adding a note.",
    DateTimeOffset Date = default, int Page = 0, bool Busy = false, bool Enabled = true, bool Visible = true,
    bool Reversed = false, bool Details = false, int Edits = 0, string LastCommand = "Ready",
    string? Presented = null, string Name = "Sample", string Draft = "", bool Favorite = false,
    int Dismissals = 0, string LastPresentation = "None");

internal sealed partial class CatalogApp
{
    internal static readonly ImmutableArray<NativeChoice> Segments = [new("day", "Day"), new("week", "Week"), new("month", "Month")];
    internal static readonly ImmutableArray<NativeChoice> Choices = [new("work", "Work"), new("personal", "Personal"), new("other", "Other")];
    internal static readonly ImmutableArray<NativeCommand> Menu = [new("reset", "Reset values", "arrow.counterclockwise", Destructive: true),
        new("reverse", "Reverse choices", "arrow.up.arrow.down"), new("unavailable", "Unavailable", Enabled: false)];
    internal static readonly ImmutableArray<NativeCommand> Navigation = [new("details", "About", "info.circle")];
    internal static readonly ImmutableArray<NativeCommand> Toolbar = [new("enable", "Lock inputs", "lock"),
        new("busy", "Activity", "arrow.triangle.2.circlepath"), new("visible", "Show or hide", "eye")];
    internal static readonly ImmutableArray<NativeAlertAction> DiscardActions = [new("keep", "Keep editing", NativeAlertActionStyle.Cancel),
        new("discard", "Discard", NativeAlertActionStyle.Destructive)];
    internal static readonly ImmutableArray<NativeAlertAction> DeleteActions = [new("delete", "Delete item", NativeAlertActionStyle.Destructive),
        new("archive", "Archive instead"), new("cancel", "Cancel", NativeAlertActionStyle.Cancel)];
    internal static readonly ImmutableArray<NativeAlertAction> ResetActions = [new("cancel", "Cancel", NativeAlertActionStyle.Cancel),
        new("reset", "Reset", NativeAlertActionStyle.Destructive)];
    internal static readonly ImmutableArray<NativeCommand> ContextMenu = [new("favorite", "Favorite", "star"),
        new("duplicate", "Duplicate", "plus.square.on.square"), new("share", "Share", "square.and.arrow.up", Enabled: false),
        new("remove", "Remove", "trash", Destructive: true)];

    internal static ImmutableArray<NativeAlertAction> RenameActions(string draft) => [new("cancel", "Cancel", NativeAlertActionStyle.Cancel),
        new("rename", "Rename", Enabled: draft.Trim().Length > 0)];
}
