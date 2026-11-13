using System.Collections.Immutable;

namespace Kumiko.Components;

/// <summary>
/// Calendar limits. <paramref name="IsDisabled"/> marks individual dates unavailable; <paramref name="Today"/>
/// replaces the system date, for example in tests or another time zone.
/// </summary>
public readonly record struct CalendarProps(DateOnly? Min = null, DateOnly? Max = null, Func<DateOnly, bool>? IsDisabled = null,
    DateOnly? Today = null);

/// <summary>
/// Date picker trigger. <paramref name="Placeholder"/> defaults to <see cref="UI.UiStrings.PickDate"/>;
/// <paramref name="Format"/> is a .NET date format, the culture's short date by default.
/// </summary>
public readonly record struct DatePickerProps(CalendarProps Calendar = default, string? Placeholder = null, string? Format = null,
    float? Width = null, bool Disabled = false, bool Clearable = false);

/// <summary>Time choices from <paramref name="Min"/> to <paramref name="Max"/> every <paramref name="StepMinutes"/> (30 by default).</summary>
public readonly record struct TimePickerProps(int StepMinutes = 30, TimeOnly? Min = null, TimeOnly? Max = null, string? Placeholder = null,
    float? Width = null, bool Disabled = false);

/// <summary>
/// Number range and display. <paramref name="Step"/> of zero steps by one; <paramref name="Decimals"/> fixes the
/// displayed precision; <paramref name="Grouping"/> adds the culture's thousands separator.
/// </summary>
public readonly record struct NumberInputProps(double? Min = null, double? Max = null, double Step = 1, int Decimals = 0,
    float? Width = null, bool Disabled = false, bool Required = false, string? Description = null, string Placeholder = "",
    bool Grouping = false);

/// <summary>Tooltip placement; it opens above its trigger by default after a 500 ms hover.</summary>
public readonly record struct TooltipProps(Side? Side = null, TimeSpan? Delay = null, float? MaxWidth = null);

/// <summary>A tree item. Leaves have no children; IDs must be unique within the tree.</summary>
public sealed record TreeNode(string Id, string Label, ImmutableArray<TreeNode> Children = default, string? Icon = null,
    bool Disabled = false)
{
    internal bool HasChildren => !Children.IsDefaultOrEmpty;
}

/// <summary>Tree viewport height (320 by default) and the action for Enter or double-click on an item.</summary>
public readonly record struct TreeProps(float? Height = null, Action<string>? Activated = null)
{
    /// <summary>Renders content after the tree-owned disclosure indicator and indentation. Do not create controls.</summary>
    public Action<UI.Context, TreeNode, TreeItemState>? RenderItem
    {
        get; init;
    }
}

/// <summary>
/// A divider that resizes the pane before it. A vertical handle sits between side-by-side panes. Set
/// <paramref name="Reverse"/> when the resized pane follows the handle. <paramref name="Step"/> defaults to 10.
/// </summary>
public readonly record struct ResizeHandleProps(Orientation Orientation = Orientation.Vertical, float? Min = null, float? Max = null,
    float Step = 0, bool Reverse = false);
