using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    const float HandleThickness = 6;

    /// <summary>
    /// A draggable divider that reports the size of the pane before it (after it with Reverse). Arrow keys
    /// step it, Home/End jump to the limits, and assistive technology can set it as a range. Place it between
    /// the panes; the model owns <paramref name="size"/>. Returns true when it reported a change.
    /// </summary>
    public bool ResizeHandle(
        string name,
        float size,
        Action<float> changed,
        ResizeHandleProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var vertical = props.Orientation == Orientation.Vertical;
        var min = props.Min ?? 0;
        var max = MathF.Max(min, props.Max ?? float.PositiveInfinity);
        var step = props.Step > 0 ? props.Step : 10;
        // A vertical handle in right-to-left content resizes the pane on its right.
        var sign = (props.Reverse ? -1 : 1) * (vertical && context.IsRightToLeft() ? -1 : 1);
        float Limit(float value) => Math.Clamp(value, min, max);
        using var handle = Box(key, source, line)
            .Center()
            .Semantics(Role.Separator, name, size.ToString("0", context.Culture))
            .Accessibility(new(Minimum: min, Maximum: float.IsFinite(max) ? max : null, Number: size, Step: step), request =>
            {
                if (request.Action != AccessibilityAction.SetRange)
                    return false;
                changed(Limit((float)request.Number));
                return true;
            });
        if (vertical)
            handle.Width(HandleThickness).FillHeight();
        else
            handle.Height(HandleThickness).FillWidth();
        var hit = handle.Control();
        var state = context.NodeState<HandleState>(handle.Id);
        var next = size;
        if (hit.Pressed)
        {
            // Measure from the press so a handle grabbed off-center does not jump.
            var pointer = vertical ? context.Input.Pointer.X : context.Input.Pointer.Y;
            if (context.Input.Pressed)
            {
                state.Origin = pointer;
                state.Pointer = pointer;
                state.Start = size;
            }
            // Controlled sizes may round-trip through fractions or rounding. Report a drag only
            // when the pointer moves, rather than feeding normalization error back every frame.
            if (pointer != state.Pointer)
            {
                state.Pointer = pointer;
                next = Limit(state.Start + sign * (pointer - state.Origin));
            }
        }
        if (hit.Focused)
        {
            var keyed = (context.Input.Key, vertical) switch
            {
                (Key.Left, true) or (Key.Up, false) => Limit(size - sign * step),
                (Key.Right, true) or (Key.Down, false) => Limit(size + sign * step),
                (Key.Home, _) => min,
                (Key.End, _) when float.IsFinite(max) => max,
                _ => float.NaN
            };
            if (!float.IsNaN(keyed))
            {
                context.Input.Key = Key.None;
                next = keyed;
            }
        }
        var active = hit.Hovered || hit.Pressed || hit.Focused;
        if (active)
            context.Cursor = vertical ? Cursor.ResizeHorizontal : Cursor.ResizeVertical;
        using (var rule = context.Div(key: "rule").Background(active ? t.Ring : t.Border))
        {
            if (vertical)
                rule.Width(active ? 2 : 1).FillHeight();
            else
                rule.Height(active ? 2 : 1).FillWidth();
        }
        if (next == size)
            return false;
        changed(next);
        return true;
    }

    sealed class HandleState
    {
        public float Origin;
        public float Pointer;
        public float Start;
    }
}
