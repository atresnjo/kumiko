using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>Control density, after gpui-kit's xs/sm/md/lg scale. Medium is the default.</summary>
public enum ControlSize
{
    Medium,
    XSmall,
    Small,
    Large
}

/// <summary>Semantic color role of a button, badge, alert or progress bar.</summary>
public enum Tone
{
    Default,
    Primary,
    Secondary,
    Danger,
    Success,
    Warning,
    Info
}

/// <summary>How a tone is painted: filled, tinted, outlined, transparent until hovered, or as a link.</summary>
public enum Appearance
{
    Solid,
    Soft,
    Outline,
    Ghost,
    Link
}

public enum TabsVariant
{
    /// <summary>Raised active tab inside a muted track (shadcn Tabs).</summary>
    Segmented,
    /// <summary>Text tabs over a bottom rule; the active tab is underlined.</summary>
    Underline,
    /// <summary>Free-standing rounded tabs; the active tab is filled.</summary>
    Pill
}

public enum Orientation
{
    Vertical,
    Horizontal
}

/// <summary>
/// Button appearance. <paramref name="Colors"/> replaces the tone's colors for a one-off custom button.
/// </summary>
public readonly record struct ButtonProps(
    Tone Tone = Tone.Default,
    Appearance Appearance = Appearance.Solid,
    ControlSize Size = ControlSize.Medium,
    string? Icon = null,
    string? TrailingIcon = null,
    bool Disabled = false,
    bool Selected = false,
    float? Width = null,
    bool Pill = false,
    ToneColors? Colors = null
);

public readonly record struct BadgeProps(
    Tone Tone = Tone.Primary,
    Appearance Appearance = Appearance.Solid,
    ControlSize Size = ControlSize.Medium,
    string? Icon = null,
    bool Pill = false,
    ToneColors? Colors = null
);

public readonly record struct AlertProps(Tone Tone = Tone.Default, string? Icon = null, ControlSize Size = ControlSize.Medium);

public readonly record struct CheckboxProps(string? Description = null, bool Disabled = false, ControlSize Size = ControlSize.Medium);

public readonly record struct SwitchProps(string? Description = null, bool Disabled = false, ControlSize Size = ControlSize.Medium,
    bool LabelFirst = false);

public readonly record struct RadioGroupProps(Orientation Orientation = Orientation.Vertical, bool Disabled = false,
    ControlSize Size = ControlSize.Medium);

/// <summary>Slider range. <paramref name="Step"/> of zero moves by one hundredth of the range.</summary>
public readonly record struct SliderProps(float Min = 0, float Max = 100, float Step = 1, float Width = 240, bool Disabled = false,
    ToneColors? Colors = null);

public readonly record struct ProgressProps(Tone Tone = Tone.Default, ControlSize Size = ControlSize.Medium, float? Width = null);

public readonly record struct TabsProps(TabsVariant Variant = TabsVariant.Segmented, ControlSize Size = ControlSize.Medium,
    bool Fill = false)
{
    /// <summary>Renders content inside each tab. Do not create controls or retain the context.</summary>
    public Action<Context, Choice, TabItemState>? RenderItem
    {
        get; init;
    }
}

public readonly record struct ToggleGroupProps(ControlSize Size = ControlSize.Medium, bool Disabled = false, bool Outline = true);

/// <summary>Label, help text and error for a form field. The error replaces the description when set.</summary>
public readonly record struct FieldProps(string? Description = null, string? Error = null, bool Required = false,
    string Placeholder = "", float? Width = null, bool Password = false, bool Disabled = false, bool Multiline = false,
    float? Height = null);

public readonly record struct DialogProps(string? Description = null, float? Width = null, bool CloseOnBackdrop = true,
    bool ShowClose = true);

public readonly record struct AvatarProps(ControlSize Size = ControlSize.Medium, Rendering.Image? Image = null, Color? Color = null);

public readonly record struct SidebarItemProps(string? Icon = null, bool Selected = false, string? Badge = null, int Depth = 0,
    bool Disabled = false);
