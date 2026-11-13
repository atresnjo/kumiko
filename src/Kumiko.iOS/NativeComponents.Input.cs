using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

public static partial class NativeComponents
{
    /// <summary>A model-controlled UISwitch. Supply a visible label beside it when needed.</summary>
    public static void NativeSwitch<TMessage>(this Ui<TMessage> ui, string label, bool value,
        Func<bool, TMessage> onChange, bool disabled = false, NodeKey key = default)
    {
        using var control = Input(ui, new NativeSwitchContent(label, value), label, Role.Switch, 44, disabled, key);
        var size = RequireRenderer(ui.Context).MeasureIntrinsic(false);
        control.Width(size.Width).Height(size.Height);
        Change(ui, control, onChange, disabled);
    }

    /// <summary>A continuous UISlider with a finite, inclusive range.</summary>
    public static void NativeSlider<TMessage>(this Ui<TMessage> ui, string label, float value,
        Func<float, TMessage> onChange, float minimum = 0, float maximum = 1,
        bool disabled = false, NodeKey key = default)
    {
        ValidateRange(value, minimum, maximum);
        using var control = Input(ui, new NativeSliderContent(label, value, minimum, maximum), label,
            Role.Slider, 44, disabled, key);
        Change(ui, control, onChange, disabled, next => float.IsFinite(next) && next >= minimum && next <= maximum);
    }

    /// <summary>A UIStepper with native increment/decrement buttons and an inclusive range.</summary>
    public static void NativeStepper<TMessage>(this Ui<TMessage> ui, string label, double value,
        Func<double, TMessage> onChange, double minimum = 0, double maximum = 100, double step = 1,
        bool disabled = false, NodeKey key = default)
    {
        ValidateRange(value, minimum, maximum);
        if (!double.IsFinite(step) || step <= 0 || step > maximum - minimum)
            throw new ArgumentOutOfRangeException(nameof(step));
        using var control = Input(ui, new NativeStepperContent(label, value, minimum, maximum, step), label,
            Role.Slider, 44, disabled, key);
        var size = RequireRenderer(ui.Context).MeasureIntrinsic(true);
        control.Width(size.Width).Height(size.Height);
        Change(ui, control, onChange, disabled, next => double.IsFinite(next) && next >= minimum && next <= maximum);
    }

    /// <summary>A UISegmentedControl. Selection sends the stable choice ID.</summary>
    public static void NativeSegmentedControl<TMessage>(this Ui<TMessage> ui, string label,
        ImmutableArray<NativeChoice> items, string selectedId, Func<string, TMessage> onChange,
        bool disabled = false, NodeKey key = default) =>
        Choice(ui, label, items, selectedId, onChange, false, disabled, key);

    /// <summary>A single-column UIPickerView. Selection sends the stable choice ID.</summary>
    public static void NativePicker<TMessage>(this Ui<TMessage> ui, string label,
        ImmutableArray<NativeChoice> items, string selectedId, Func<string, TMessage> onChange,
        bool disabled = false, NodeKey key = default) =>
        Choice(ui, label, items, selectedId, onChange, true, disabled, key);

    static void Choice<TMessage>(Ui<TMessage> ui, string label, ImmutableArray<NativeChoice> items,
        string selectedId, Func<string, TMessage> onChange, bool wheel, bool disabled, NodeKey key)
    {
        if (items.IsDefaultOrEmpty || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label)) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length ||
            !items.Any(item => item.Id == selectedId))
            throw new ArgumentException("Supply unique nonempty choice IDs and a valid selected ID.", nameof(items));
        using var control = Input(ui, new NativeChoiceContent(label, items, selectedId, wheel), label,
            Role.ComboBox, wheel ? 162 : 44, disabled, key);
        Change(ui, control, onChange, disabled, id => items.Any(item => item.Id == id));
    }

    /// <summary>A wheel-style UIDatePicker. Values are instants; UIKit displays them in the local time zone.</summary>
    public static void NativeDatePicker<TMessage>(this Ui<TMessage> ui, string label, DateTimeOffset value,
        Func<DateTimeOffset, TMessage> onChange, NativeDatePickerMode mode = NativeDatePickerMode.Date,
        DateTimeOffset? minimum = null, DateTimeOffset? maximum = null, bool disabled = false, NodeKey key = default)
    {
        if (!Enum.IsDefined(mode) || minimum > maximum || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
        using var control = Input(ui, new NativeDateContent(label, value, mode, minimum, maximum), label,
            Role.ComboBox, 216, disabled, key);
        Change(ui, control, onChange, disabled, next => !(next < minimum || next > maximum));
    }

    /// <summary>A UISearchBar. Text changes send model messages; no search or network request runs implicitly.</summary>
    public static void NativeSearchField<TMessage>(this Ui<TMessage> ui, string label, string value,
        Func<string, TMessage> onChange, string placeholder = "Search", bool disabled = false, NodeKey key = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var control = Input(ui, new NativeSearchContent(label, value, placeholder), label,
            Role.TextField, 56, disabled, key);
        Change(ui, control, onChange, disabled);
    }

    /// <summary>A scrolling UITextView with model-controlled multiline text and native editing.</summary>
    public static void NativeTextEditor<TMessage>(this Ui<TMessage> ui, string label, string value,
        Func<string, TMessage> onChange, float height = 132, bool disabled = false, NodeKey key = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!float.IsFinite(height) || height < 44)
            throw new ArgumentOutOfRangeException(nameof(height));
        using var control = Input(ui, new NativeEditorContent(label, value), label, Role.TextField, height, disabled, key);
        Change(ui, control, onChange, disabled);
    }

    static Div Input<TMessage>(Ui<TMessage> ui, object content, string label, Role role,
        float height, bool disabled, NodeKey key)
    {
        var renderer = RequireRenderer(ui.Context);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var control = ui.Context.Div(key).FillWidth().Height(height).Semantics(role, label).Native(content);
        renderer.PrepareValue(control.Id, content, disabled);
        return control;
    }

    static void Change<TValue, TMessage>(Ui<TMessage> ui, Div control, Func<TValue, TMessage> onChange,
        bool disabled, Func<TValue, bool>? valid = null)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        control.Control(!disabled);
        var renderer = RequireRenderer(ui.Context);
        if (renderer.TakeValue<TValue>(control.Id) is { } edit && !disabled && (valid is null || valid(edit.Value)))
            ui.Context.WithSource(control.Id, () => ui.Dispatch(onChange(edit.Value)));
    }

    static void ValidateRange(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || !double.IsFinite(minimum) || !double.IsFinite(maximum) ||
            minimum >= maximum || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), "Supply a finite value inside an increasing finite range.");
    }
}
