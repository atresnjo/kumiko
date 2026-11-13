using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>A stable choice in a segmented control or wheel picker.</summary>
public sealed record NativeChoice(string Id, string Label);

/// <summary>A stable menu or bar action. Leading applies only to navigation bars.</summary>
public sealed record NativeCommand(string Id, string Label, string? Symbol = null,
    bool Enabled = true, bool Destructive = false, bool Leading = false);

/// <summary>The calendar/time fields displayed by a native date picker.</summary>
public enum NativeDatePickerMode
{
    Date,
    Time,
    DateAndTime
}

internal sealed record NativeSwitchContent(string Label, bool Value, Color? Tint = null);
internal sealed record NativeSliderContent(string Label, float Value, float Minimum, float Maximum);
internal sealed record NativeStepperContent(string Label, double Value, double Minimum, double Maximum, double Step);
internal sealed record NativeChoiceContent(string Label, ImmutableArray<NativeChoice> Items, string SelectedId, bool Wheel);
internal sealed record NativeDateContent(string Label, DateTimeOffset Value, NativeDatePickerMode Mode,
    DateTimeOffset? Minimum, DateTimeOffset? Maximum);
internal sealed record NativeSearchContent(string Label, string Value, string Placeholder);
internal sealed record NativeEditorContent(string Label, string Value, NativeInputStyle? Style = null, string Placeholder = "");
internal sealed record NativeFieldContent(string Label, string Value, string Placeholder, NativeInputStyle Style);
internal sealed record NativeProgressContent(string Label, float Value);
internal sealed record NativeActivityContent(string Label, bool Running);
internal sealed record NativePageContent(string Label, int Count, int Page);
internal sealed record NativeImageContent(string? Label, string? Symbol, ImmutableArray<byte> Data, Color? Tint = null);
internal sealed record NativeSeparatorContent;
internal sealed record NativeCommandsContent(string Label, ImmutableArray<NativeCommand> Items, NativeCommandKind Kind);
internal enum NativeCommandKind
{
    Menu, Navigation, Toolbar
}
