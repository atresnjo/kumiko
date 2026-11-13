using System.Globalization;
using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>A checkbox with label and optional description. Returns true when it reported a change.</summary>
    public bool Checkbox(
        string label,
        bool value,
        Action<bool> changed,
        CheckboxProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var size = props.Size switch
        {
            ControlSize.XSmall or ControlSize.Small => 14f,
            ControlSize.Large => 20f,
            _ => 16f
        };
        using var row = Box(key, source, line)
            .Row()
            .Items(props.Description is null ? Align.Center : Align.Start)
            .Gap(8)
            .Semantics(Role.Checkbox, label, isChecked: value);
        var hit = row.Control(!props.Disabled);
        var primary = t.Primary;
        using (var square = context.Div(key: "box")
            .Width(size)
            .Height(size)
            .Center()
            .Rounded(MathF.Min(t.Radius, 4))
            .Background(value ? primary.Background : t.Background)
            .Border(value ? primary.Background : hit.Hovered && !props.Disabled ? t.Ring : t.Input))
        {
            Ring(square, hit, t);
            if (value)
                context.Icon("checkmark", size - 4, primary.Foreground, key: "check");
        }
        ChoiceLabel(label, props.Description, t);
        if (props.Disabled)
            row.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        if (!hit.Clicked || props.Disabled)
            return false;
        changed(!value);
        return true;
    }

    /// <summary>An on/off switch with an animated thumb. Returns true when it reported a change.</summary>
    public bool Switch(
        string label,
        bool value,
        Action<bool> changed,
        SwitchProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var (width, height) = props.Size switch
        {
            ControlSize.XSmall or ControlSize.Small => (28f, 16f),
            ControlSize.Large => (44f, 24f),
            _ => (36f, 20f)
        };
        const float inset = 2;
        using var row = Box(key, source, line)
            .Row()
            .Items(props.Description is null ? Align.Center : Align.Start)
            .Gap(10)
            .Semantics(Role.Switch, label, isChecked: value);
        var hit = row.Control(!props.Disabled);
        if (props.LabelFirst)
        {
            using (context.Div(key: "text").Flex())
                ChoiceLabel(label, props.Description, t);
        }
        using (var track = context.Div(key: "track")
            .Width(width)
            .Height(height)
            .Padding(inset)
            .Rounded(t.RadiusFull)
            .Background(value ? t.Primary.Background : t.Switch))
        {
            Ring(track, hit, t);
            var knob = height - inset * 2;
            using var thumb = context.Div(key: "thumb")
                .Width(knob)
                .Height(knob)
                .Rounded(t.RadiusFull)
                .Background(value ? t.Primary.Foreground : t.SwitchThumb)
                .Offset(value ? width - height : 0, 0, Motion.Quick);
            if (t.Shadows)
                thumb.Shadow(2, 1, Color.Black.Alpha(.15f));
        }
        if (!props.LabelFirst)
            ChoiceLabel(label, props.Description, t);
        if (props.Disabled)
            row.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        if (!hit.Clicked || props.Disabled)
            return false;
        changed(!value);
        return true;
    }

    /// <summary>
    /// A single-choice group. Arrow keys move selection and focus; only the selected item is a Tab stop.
    /// </summary>
    public bool RadioGroup(
        string name,
        IReadOnlyList<Choice> choices,
        string? selected,
        Action<string> changed,
        RadioGroupProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var size = props.Size is ControlSize.XSmall or ControlSize.Small ? 14f : props.Size == ControlSize.Large ? 20f : 16f;
        using var group = Box(key, source, line).Gap(props.Orientation == Orientation.Horizontal ? 16 : 10)
            .Semantics(Role.Group, name);
        if (props.Orientation == Orientation.Horizontal)
            group.Row().ItemsCenter();
        var ids = new ulong[choices.Count];
        int focused = -1;
        int clicked = -1;
        int anchor = Math.Max(0, IndexOf(choices, selected));
        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var on = choice.Id == selected;
            var enabled = !props.Disabled && !choice.Disabled;
            using var item = context.Div(key: choice.Id)
                .Row()
                .ItemsCenter()
                .Gap(8)
                .TabStop(i == anchor)
                .Semantics(Role.Option, choice.Label, isChecked: on)
                .Accessibility(new(Selected: on));
            ids[i] = item.Id;
            var hit = item.Control(enabled);
            if (hit.Focused)
                focused = i;
            using (var circle = context.Div(key: "circle")
                .Width(size)
                .Height(size)
                .Center()
                .Rounded(t.RadiusFull)
                .Background(on ? t.Primary.Background : t.Background)
                .Border(on ? t.Primary.Background : hit.Hovered && enabled ? t.Ring : t.Input))
            {
                Ring(circle, hit, t);
                if (on)
                {
                    using (context.Div(key: "dot").Width(size * .4f).Height(size * .4f).Rounded(t.RadiusFull)
                        .Background(t.Primary.Foreground))
                    {
                    }
                }
            }
            context.Text(choice.Label, Text(t.FontSize, t.Foreground), key: "label");
            if (!enabled)
                item.Opacity(t.Metrics.DisabledOpacity);
            else
                Hand(hit);
            if (hit.Clicked && enabled && !on)
                clicked = i;
        }
        if (clicked >= 0)
        {
            changed(choices[clicked].Id);
            return true;
        }
        var step = focused < 0 ? 0 : context.Logical(context.Input.Key) switch
        {
            Key.Up or Key.Left => -1,
            Key.Down or Key.Right => 1,
            _ => 0
        };
        var target = Step(choices, focused, step, props.Disabled);
        if (target < 0)
            return false;
        context.Input.Key = Key.None;
        context.FocusControl(ids[target]);
        changed(choices[target].Id);
        return true;
    }

    /// <summary>A horizontal slider; drag, click or use arrows, Page Up/Down, Home and End.</summary>
    public bool Slider(
        string name,
        float value,
        Action<float> changed,
        SliderProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        if (props.Max <= props.Min)
            props = props with
            {
                Min = 0,
                Max = 100
            };
        var range = props.Max - props.Min;
        var step = props.Step > 0 ? props.Step : range / 100;
        var width = props.Width > 0 ? props.Width : 240;
        const float height = 20, thumb = 16, bar = 6;
        float Snap(float v) => Math.Clamp(props.Min + MathF.Round((v - props.Min) / step) * step, props.Min, props.Max);
        var current = Snap(value);
        using var box = Box(key, source, line)
            .Width(width)
            .Height(height)
            .Semantics(Role.Slider, name, current.ToString(CultureInfo.InvariantCulture));
        box.Accessibility(new(Minimum: props.Min, Maximum: props.Max, Number: current, Step: step), request =>
        {
            if (request.Action is not (AccessibilityAction.SetValue or AccessibilityAction.SetRange))
                return false;
            changed(Snap((float)request.Number));
            return true;
        });
        var hit = box.Control(!props.Disabled);
        var next = current;
        if ((hit.Pressed || hit.Clicked && context.Input.Key == Key.None) && hit.Rect is { } r)
        {
            var along = context.IsRightToLeft() ? r.Right - context.Input.Pointer.X - thumb / 2 : context.Input.Pointer.X - r.X - thumb / 2;
            next = Snap(props.Min + Clamp01(along / Math.Max(1, r.Width - thumb)) * range);
        }
        if (hit.Focused)
        {
            var keyed = context.Logical(context.Input.Key) switch
            {
                Key.Left or Key.Down => Snap(current - step),
                Key.Right or Key.Up => Snap(current + step),
                Key.PageDown => Snap(current - step * 10),
                Key.PageUp => Snap(current + step * 10),
                Key.Home => props.Min,
                Key.End => props.Max,
                _ => float.NaN
            };
            if (!float.IsNaN(keyed))
            {
                context.Input.Key = Key.None;
                next = keyed;
            }
        }
        var colors = props.Colors;
        var fill = colors?.Background ?? t.SliderBar;
        var fraction = Clamp01((current - props.Min) / range);
        var travel = width - thumb;
        using (context.Div(key: "track").Absolute().Offset(thumb / 2, (height - bar) / 2).Width(travel).Height(bar)
            .Rounded(t.RadiusFull).Background(fill.Alpha(.2f)))
        {
        }
        using (context.Div(key: "range").Absolute().Offset(thumb / 2, (height - bar) / 2).Width(travel * fraction).Height(bar)
            .Rounded(t.RadiusFull).Background(fill))
        {
        }
        if ((hit.Hovered || hit.Focused || hit.Pressed) && !props.Disabled)
        {
            using (context.Div(key: "halo").Absolute().Offset(travel * fraction - 4, (height - thumb) / 2 - 4)
                .Width(thumb + 8).Height(thumb + 8).Rounded(t.RadiusFull).Background((hit.Focused ? t.Ring : fill).Alpha(.25f)))
            {
            }
        }
        using (var knob = context.Div(key: "thumb").Absolute().Offset(travel * fraction, (height - thumb) / 2).Width(thumb)
            .Height(thumb).Rounded(t.RadiusFull).Background(colors?.Foreground ?? t.SliderThumb).Border(fill, 1.5f))
        {
            if (t.Shadows)
                knob.Shadow(2, 1, Color.Black.Alpha(.15f));
        }
        if (props.Disabled)
            box.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        if (next == current || props.Disabled)
            return false;
        changed(next);
        return true;
    }

    /// <summary>A labelled text field with help text or a validation error beneath it.</summary>
    public bool Field(
        string label,
        string value,
        Action<string> changed,
        FieldProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var width = props.Width ?? 280;
        using var field = Box(key, source, line).Column().Width(width).Gap(6);
        Label(label, props.Required, key: "label");
        var edited = context.TextField(value, changed, new(props.Placeholder, width, props.Password, props.Multiline, props.Disabled,
            label, Error: props.Error, Height: props.Height), key: "input");
        if (props.Error is { } error)
            context.Text(error, Text(t.FontSize - 1, t.Danger.Text) with
            {
                Wrap = true
            }, key: "error");
        else if (props.Description is { } description)
            context.Text(description, Text(t.FontSize - 1, t.MutedForeground) with
            {
                Wrap = true
            }, key: "description");
        return edited;
    }

    /// <summary>A form label; required labels end with a destructive asterisk.</summary>
    public void Label(string text, bool required = false, NodeKey key = default, [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0)
    {
        var t = Theme;
        var style = Text(t.FontSize, t.Foreground, Weight.Medium);
        if (!required)
        {
            context.Text(text, style, key, source, line);
            return;
        }
        var full = text + " *";
        context.RichText(full, [new(text.Length + 1, full.Length, t.Danger.Text)], style, key, source, line);
    }

    void ChoiceLabel(string label, string? description, KitTheme t)
    {
        using var text = context.Div(key: "label").Column().Gap(2);
        context.Text(label, Text(t.FontSize, t.Foreground, Weight.Medium), key: "text");
        if (description is not null)
            context.Text(description, Text(t.FontSize - 1, t.MutedForeground), key: "description");
    }

    static int IndexOf(IReadOnlyList<Choice> choices, string? id)
    {
        for (int i = 0; i < choices.Count; i++)
            if (choices[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>The next enabled index from <paramref name="from"/>, wrapping; -1 when nothing moves.</summary>
    static int Step(IReadOnlyList<Choice> choices, int from, int direction, bool disabled)
    {
        if (direction == 0 || from < 0 || disabled)
            return -1;
        for (int n = 1; n < choices.Count; n++)
        {
            var i = ((from + direction * n) % choices.Count + choices.Count) % choices.Count;
            if (!choices[i].Disabled)
                return i;
        }
        return -1;
    }
}
