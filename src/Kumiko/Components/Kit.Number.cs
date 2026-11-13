using System.Globalization;
using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>
    /// A labelled number field in the window culture. Typing reports each valid value and keeps partial input
    /// such as "1," until Enter or leaving the field, which clamps to the range. Up/Down step (Shift ×10).
    /// An empty optional field reports null. Returns true when it reported a change.
    /// </summary>
    public bool NumberInput(
        string label,
        double? value,
        Action<double?> changed,
        NumberInputProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var ctx = context;
        var culture = ctx.Culture;
        var min = props.Min ?? double.NegativeInfinity;
        var max = props.Max ?? double.PositiveInfinity;
        var step = props.Step > 0 && double.IsFinite(props.Step) ? props.Step : 1;
        var decimals = Math.Clamp(props.Decimals, 0, 15);
        string Format(double number) => number.ToString((props.Grouping ? "N" : "F") + decimals.ToString(CultureInfo.InvariantCulture), culture);
        double Clamp(double number) => Math.Round(Math.Clamp(number, min, max), decimals, MidpointRounding.AwayFromZero);
        var width = props.Width ?? 200;
        using var field = Box(key, source, line).Column().Width(width).Gap(6).Semantics(Role.Group, label);
        var state = ctx.NodeState<NumberState>(field.Id);
        Label(label, props.Required, key: "label");
        var reported = false;
        void Report(double? next)
        {
            if (next == value)
                return;
            changed(next);
            reported = true;
            ctx.Changed = true;
        }
        string? Validate(string text, out double? parsed)
        {
            parsed = null;
            if (text.Trim().Length == 0)
                return props.Required ? ctx.Strings.InvalidNumber : null;
            if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var number) || !double.IsFinite(number))
                return ctx.Strings.InvalidNumber;
            parsed = number;
            if (number >= min && number <= max)
                return null;
            if (double.IsFinite(min) && double.IsFinite(max))
                return ctx.Format(ctx.Strings.NumberRange, Format(min), Format(max));
            return double.IsFinite(min) ? ctx.Format(ctx.Strings.NumberMinimum, Format(min)) : ctx.Format(ctx.Strings.NumberMaximum, Format(max));
        }
        void Commit()
        {
            if (!state.Editing)
                return;
            state.Editing = false;
            state.Error = null;
            if (state.Draft.Trim().Length == 0 && !props.Required)
                Report(null);
            else if (double.TryParse(state.Draft, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var number) && double.IsFinite(number))
                Report(Clamp(number));
        }
        void Step(int direction)
        {
            var current = value ?? Math.Max(0, min);
            if (state.Editing && double.TryParse(state.Draft, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var typed) &&
                double.IsFinite(typed))
                current = typed;
            var next = Clamp(current + direction * step);
            state.Editing = false;
            state.Error = null;
            Report(next);
        }
        using (ctx.Div(key: "row").Row().Width(width).ItemsCenter().Gap(4))
        {
            using (var input = ctx.Div(key: "input-box").Flex())
                Editor(input.Id);
            if (Stepper("minus", ctx.Format(ctx.Strings.Decrease, label), props.Disabled || value <= min, "decrease"))
                Step(-1);
            if (Stepper("plus", ctx.Format(ctx.Strings.Increase, label), props.Disabled || value >= max, "increase"))
                Step(1);
        }
        void Editor(ulong box)
        {
            var focused = ctx.FocusWithin(box);
            if (state.Editing && !focused)
                Commit();
            if (focused && !props.Disabled)
            {
                var multiplier = (ctx.Input.Modifiers & Modifiers.Shift) != 0 ? 10 : 1;
                switch (ctx.Input.Key)
                {
                    case Key.Enter:
                        ctx.Input.Key = Key.None;
                        Commit();
                        break;
                    case Key.Up:
                        ctx.Input.Key = Key.None;
                        Step(multiplier);
                        break;
                    case Key.Down:
                        ctx.Input.Key = Key.None;
                        Step(-multiplier);
                        break;
                }
            }
            var shown = state.Editing ? state.Draft : value is { } v ? Format(v) : "";
            var id = ctx.NextNodeId("input");
            ctx.TextField(shown, text =>
            {
                state.Editing = true;
                state.Draft = text;
                state.Error = Validate(text, out var parsed);
                if (state.Error is null)
                    Report(parsed is { } number ? Math.Round(number, decimals, MidpointRounding.AwayFromZero) : null);
            }, new(props.Placeholder, MathF.Max(40, width - 2 * (t.Metrics.HeightMedium + 4)), Disabled: props.Disabled, Name: label,
                Error: state.Error), key: "input");
            ctx.AmendAccessibility(id, a => a with
            {
                Minimum = double.IsFinite(min) ? min : null,
                Maximum = double.IsFinite(max) ? max : null,
                Number = value,
                Step = step
            });
        }
        if (state.Error is { } error)
            ctx.Text(error, Text(t.FontSize - 1, t.Danger.Text) with
            {
                Wrap = true
            }, key: "error");
        else if (props.Description is { } description)
            ctx.Text(description, Text(t.FontSize - 1, t.MutedForeground) with
            {
                Wrap = true
            }, key: "description");
        return reported;
    }

    /// <summary>A step button outside the Tab order; the field's arrow keys are the keyboard path.</summary>
    bool Stepper(string icon, string name, bool disabled, NodeKey key)
    {
        var t = Theme;
        var size = t.Metrics.HeightMedium;
        using var button = context.Div(key: key)
            .Width(size)
            .Height(size)
            .Center()
            .Rounded(t.Radius)
            .Border(t.Input)
            .TabStop(false)
            .Semantics(Role.Button, name);
        var hit = button.Control(!disabled);
        if (hit.Hovered && !disabled)
            button.Background(t.Accent);
        context.Icon(icon, 14, t.Foreground, key: "icon");
        if (disabled)
            button.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        return hit.Clicked && !disabled;
    }

    sealed class NumberState
    {
        public bool Editing;
        public string Draft = "";
        public string? Error;
    }
}
