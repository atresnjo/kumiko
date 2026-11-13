using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>
/// App-styled text input: the field draws no border or background of its own, so it sits inside an app surface.
/// Text uses the style's size, weight and color, scaled by Dynamic Type. Dark selects the dark keyboard.
/// </summary>
public sealed record NativeInputStyle(TextStyle Text, Color Placeholder, Color Tint, bool Dark = false);

public static partial class NativeComponents
{
    /// <summary>A borderless single-line UITextField with app typography. Edits send model messages; Return ends editing.</summary>
    public static void NativeTextField<TMessage>(this Ui<TMessage> ui, string label, string value,
        Func<string, TMessage> onChange, NativeInputStyle style, string placeholder = "", float height = 44,
        bool disabled = false, NodeKey key = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(placeholder);
        ValidateHeight(height);
        using var control = Input(ui, new NativeFieldContent(label, value, placeholder, style), label, Role.TextField, height, disabled, key);
        Change(ui, control, onChange, disabled);
    }

    /// <summary>A borderless multiline UITextView with app typography and a placeholder shown while empty.</summary>
    public static void NativeTextEditor<TMessage>(this Ui<TMessage> ui, string label, string value,
        Func<string, TMessage> onChange, NativeInputStyle style, string placeholder = "", float height = 88,
        bool disabled = false, NodeKey key = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(placeholder);
        ValidateHeight(height);
        using var control = Input(ui, new NativeEditorContent(label, value, style, placeholder), label, Role.TextField, height, disabled, key);
        Change(ui, control, onChange, disabled);
    }

    /// <summary>A model-controlled UISwitch whose on state uses <paramref name="tint"/>.</summary>
    public static void NativeSwitch<TMessage>(this Ui<TMessage> ui, string label, bool value,
        Func<bool, TMessage> onChange, Color tint, bool disabled = false, NodeKey key = default)
    {
        using var control = Input(ui, new NativeSwitchContent(label, value, tint), label, Role.Switch, 44, disabled, key);
        var size = RequireRenderer(ui.Context).MeasureIntrinsic(false);
        control.Width(size.Width).Height(size.Height);
        Change(ui, control, onChange, disabled);
    }

    /// <summary>
    /// Sets UIKit's light or dark traits for this view's native controls (bars, switches, keyboards) this frame, for
    /// apps whose theme lives in the model. A frame without it restores the host's interface style.
    /// </summary>
    public static void NativeInterfaceStyle(this Context context, bool dark) =>
        RequireRenderer(context).PendingInterfaceStyle = dark ? UIKit.UIUserInterfaceStyle.Dark : UIKit.UIUserInterfaceStyle.Light;

    /// <summary>
    /// Draws every plain <see cref="Context.Text"/> node built in this frame exactly, as <c>NativeLabel</c>
    /// does: size, weight and color from its style (the theme text color when unset), scaled by Dynamic Type, at
    /// least 11 points. Headings keep their header trait. It covers the whole frame wherever it is called; a frame
    /// without it keeps the system text roles. Use it for views that share their parts with desktop.
    /// </summary>
    public static void NativeExactText(this Context context)
    {
        RequireRenderer(context);
        context.ExactNativeText = static (text, style, heading) =>
            float.IsFinite(style.Size) && style.Size >= 11 ? new NativeLabelContent(text, style, Heading: heading) : null;
    }

    static void ValidateHeight(float height)
    {
        if (!float.IsFinite(height) || height < 32)
            throw new ArgumentOutOfRangeException(nameof(height), "Native inputs are at least 32 points high.");
    }
}
