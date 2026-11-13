using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>The window corner or edge center where toasts stack. Left and right are physical sides.</summary>
public enum ToastPlacement
{
    BottomRight,
    BottomLeft,
    BottomCenter,
    TopRight,
    TopLeft,
    TopCenter
}

/// <summary>
/// Toast stack layout: <paramref name="Width"/> (default 356), the <paramref name="Inset"/> from the window edges
/// (20) and the <paramref name="Gap"/> between spread toasts (10). <paramref name="Visible"/> toasts (3) show while
/// the stack is collapsed; older ones wait behind them. <paramref name="Expanded"/> keeps the stack spread out
/// instead of only while hovered or focused.
/// </summary>
public readonly record struct ToasterProps(
    ToastPlacement Placement = ToastPlacement.BottomRight,
    float? Width = null,
    float? Inset = null,
    float? Gap = null,
    int? Visible = null,
    bool Expanded = false
);

/// <summary>
/// How one toast looks and behaves. <paramref name="Busy"/> shows a spinner in place of the icon and
/// <paramref name="Progress"/>, from 0 to 1, an eased bar along the bottom. <paramref name="Action"/> labels a button;
/// <paramref name="Clickable"/> makes the whole toast a button. A dismissible toast has a close button, closes with
/// Escape and can be swiped away. With a <paramref name="Duration"/> it expires once that much time has passed while
/// the stack is neither hovered nor focused.
/// </summary>
public readonly record struct ToastProps(
    string? Description = null,
    Tone Tone = Tone.Default,
    string? Icon = null,
    bool Busy = false,
    float? Progress = null,
    string? Action = null,
    bool Clickable = false,
    bool Dismissible = true,
    TimeSpan? Duration = null
);

/// <summary>A toast to show. <paramref name="Id"/> is its stable identity while it stays in the list.</summary>
public sealed record Toast(string Id, string Title, ToastProps Props = default);

public enum ToastEventKind
{
    Clicked,
    Action,
    Dismissed,
    Expired
}

/// <summary>What happened to a toast this frame. The application removes dismissed and expired toasts.</summary>
public readonly record struct ToastEvent(string Id, ToastEventKind Kind);

public readonly partial struct Kit
{
    const float ToastPeek = 14, ToastSwipe = 72;

    /// <summary>
    /// Shows <paramref name="toasts"/>, oldest first, as a stack at a window corner and reports what the user did to
    /// them. The newest toast rises in at the front and pushes the others back into a deck; hovering or focusing
    /// the deck spreads it into a list. A toast removed from the list slides out, and a swiped one flies off the side
    /// it was thrown to. The stack never takes focus or blocks the page around it, and it pauses expiry timers while
    /// the user is looking at it.
    /// </summary>
    public IReadOnlyList<ToastEvent> Toaster(
        IReadOnlyList<Toast> toasts,
        ToasterProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        ArgumentNullException.ThrowIfNull(toasts);
        var events = new List<ToastEvent>();
        if (toasts.Count == 0)
            return events;
        var window = context.Size;
        var inset = MathF.Max(0, props.Inset ?? 20);
        var gap = MathF.Max(0, props.Gap ?? 10);
        var width = MathF.Min(props.Width ?? 356, MathF.Max(0, window.Width - 2 * inset));
        var bottom = props.Placement is ToastPlacement.BottomRight or ToastPlacement.BottomLeft or ToastPlacement.BottomCenter;
        var x = props.Placement switch
        {
            ToastPlacement.BottomCenter or ToastPlacement.TopCenter => (window.Width - width) / 2,
            // Offsets are in layout coordinates, which mirror in right-to-left windows; the placement is physical.
            ToastPlacement.BottomRight or ToastPlacement.TopRight => context.RightToLeftWindow ? inset : window.Width - inset - width,
            _ => context.RightToLeftWindow ? window.Width - inset - width : inset
        };
        using var layer = context.Overlay(key.Value is null ? "toaster" : key, false);
        var stack = context.NodeState<ToasterState>(layer.Id);
        var input = context.Input;
        var expanded = props.Expanded || stack.Region is { } region && region.Contains(input.Pointer) || stack.Focused;
        var visible = Math.Max(1, props.Visible ?? 3);
        var front = Natural(toasts[^1]);
        var t = Theme;
        Rect? covered = null;
        var focused = false;
        // Lay out from the front, then build oldest first so the front toast paints on top.
        var places = new float[toasts.Count];
        var along = 0f;
        for (var depth = 0; depth < toasts.Count; depth++)
        {
            places[depth] = expanded ? along : depth * ToastPeek;
            along += Natural(toasts[toasts.Count - 1 - depth]) + gap;
        }
        for (var k = 0; k < toasts.Count; k++)
        {
            var toast = toasts[k];
            var depth = toasts.Count - 1 - k;
            var place = places[depth];
            var shown = expanded ? Natural(toast) : front;
            var scale = expanded ? 1 : 1 - .05f * Math.Min(depth, visible);
            var lift = shown * (1 - scale) / 2;
            var y = bottom ? window.Height - inset - shown - place - lift : inset + place + lift;
            var (rect, hasFocus) = ToastCard(stack, toast, depth, x, y, width, shown, scale, depth >= visible, expanded, bottom, t, events, source, line);
            focused |= hasFocus;
            if (rect is { } r && depth < visible)
                covered = covered is { } c ? Union(c, r) : r;
        }
        stack.Region = covered;
        stack.Focused = focused;
        foreach (var gone in stack.Heights.Keys.Where(id => !toasts.Any(toast => toast.Id == id)).ToArray())
            stack.Heights.Remove(gone);
        return events;

        float Natural(Toast toast) => stack.Heights.TryGetValue(toast.Id, out var height) ? height : 64;
    }

    (Rect? Rect, bool Focused) ToastCard(ToasterState stack, Toast toast, int depth, float x, float y, float width, float height, float scale, bool hidden,
        bool expanded, bool bottom, KitTheme t, List<ToastEvent> events, string source, int line)
    {
        var p = toast.Props;
        var input = context.Input;
        var exit = new Transition(Opacity: 0, Y: bottom ? .4f : -.4f, Scale: .94f, Relative: true, Motion: Motion.Standard);
        using var card = Box(toast.Id, source, line).Absolute().Width(width).Rounded(t.RadiusLarge * 1.25f).Background(t.Popover).Border(t.Border)
            .Clip().TabStop(false).Semantics(Role.Alert, toast.Title, p.Description)
            .Enter(new(Opacity: 0, Y: bottom ? 1 : -1, Scale: .96f, Relative: true, Motion: Motion.Gentle));
        var state = context.NodeState<ToastState>(card.Id);
        if (t.Shadows)
            card.Shadow(24, 8, Color.Black.Alpha(t.IsDark ? .45f : .1f));
        // Cards tucked behind the front one take input only once the stack spreads; hovering their edge spreads it.
        var interactive = !hidden && (expanded || depth == 0) && (p.Clickable || p.Dismissible);
        var hit = card.Control(interactive);
        if (p.Clickable && interactive)
            Hand(hit);
        // Swiping: the card follows the pointer sideways and, thrown far enough, flies off that side.
        if (p.Dismissible && hit.Pressed)
        {
            if (!state.Holding)
                (state.Holding, state.PressX, state.Moved) = (true, input.Pointer.X, false);
            state.Drag = input.Pointer.X - state.PressX;
            state.Moved |= MathF.Abs(state.Drag) > 4;
        }
        else if (state.Holding)
        {
            state.Holding = false;
            if (MathF.Abs(state.Drag) > ToastSwipe)
                events.Add(new(toast.Id, ToastEventKind.Dismissed));
            else
                state.Drag = 0;
        }
        if (hit.Clicked && !state.Moved && p.Clickable)
            events.Add(new(toast.Id, ToastEventKind.Clicked));
        if (!hit.Pressed)
            state.Moved = false;
        if (state.Drag != 0)
            exit = new(Opacity: 0, X: MathF.Sign(state.Drag), Relative: true, Motion: Motion.Standard);
        card.Exit(exit);
        if (state.Natural > 0)
        {
            var offsetX = card.Animate(x + state.Drag, Motion.Quick, MotionChannel.OffsetX);
            card.Offset(state.Holding ? x + state.Drag : offsetX, card.Animate(y, Motion.Standard, MotionChannel.OffsetY));
            card.Scale(card.Animate(scale, Motion.Standard));
            card.Height(card.Animate(height, Motion.Standard, MotionChannel.Height));
        }
        else
        {
            // Until its height is measured the card takes its natural size where it would rest; the enter transition
            // starts fully transparent, so the estimate is never seen, and eased channels begin from the next frame.
            card.Offset(x, y).Scale(scale);
            context.FrameIn(TimeSpan.FromMilliseconds(1));
        }
        var fade = card.Animate(hidden ? 0 : 1, Motion.Quick, MotionChannel.Opacity);
        card.Opacity(fade * (1 - Clamp01(MathF.Abs(state.Drag) / (ToastSwipe * 2.5f))));
        var focused = context.FocusWithin(card.Id);
        if (focused && p.Dismissible && context.Shortcut(Key.Escape, Modifiers.None))
            events.Add(new(toast.Id, ToastEventKind.Dismissed));
        Expire(toast, state, expanded || state.Holding || focused, events);
        if (hit.Focused && context.FocusVisible)
            card.Border(t.Ring, t.Metrics.FocusWidth);
        using (var content = context.Div(key: "content").FillWidth().Column())
        {
            // Cards waiting behind the front one show only their edge, so their content fades out.
            content.Opacity(content.Animate(expanded || depth == 0 ? 1 : 0, Motion.Quick));
            if (content.PreviousRect is { } natural)
                stack.Heights[toast.Id] = state.Natural = natural.Height;
            ToastBody(toast, width, expanded && !hidden, t, events);
        }
        return (card.PreviousRect, focused);
    }

    void ToastBody(Toast toast, float width, bool expanded, KitTheme t, List<ToastEvent> events)
    {
        var p = toast.Props;
        var colors = Colors(p.Tone, t);
        var accent = p.Tone == Tone.Default ? t.PopoverForeground : colors.Text;
        using (context.Div(key: "row").Row().FillWidth().Items(Align.Start).Gap(12).Padding(new Edges(14, 14, p.Progress is null ? 14 : 10, 16)))
        {
            var icon = p.Icon ?? p.Tone switch
            {
                Tone.Success => "checkmark.circle.fill",
                Tone.Warning => "exclamationmark.triangle",
                Tone.Danger => "xmark.octagon",
                Tone.Info => "info.circle",
                _ => null
            };
            if (p.Busy)
                ToastSpinner(accent, t);
            else if (icon is not null)
            {
                using (context.Div(key: "icon").Width(18).Height(20).Center())
                    context.Icon(icon, 16, accent, key: "glyph");
            }
            using (context.Div(key: "text").Flex().Column().Gap(3))
            {
                context.Text(toast.Title, Text(t.FontSize, t.PopoverForeground, Weight.Medium), key: "title");
                if (p.Description is { } description)
                    context.Text(description, Text(t.FontSize - 1, t.MutedForeground) with
                    {
                        Wrap = true
                    }, key: "description");
            }
            if (p.Action is { } action && Button(action, new(Size: ControlSize.Small), "action").Clicked)
                events.Add(new(toast.Id, ToastEventKind.Action));
            if (p.Dismissible && expanded &&
                IconButton("xmark", "Dismiss " + toast.Title, new(Appearance: Appearance.Ghost, Size: ControlSize.XSmall), "dismiss").Clicked)
                events.Add(new(toast.Id, ToastEventKind.Dismissed));
        }
        if (p.Progress is not { } progress)
            return;
        var bar = p.Tone == Tone.Default ? t.PopoverForeground : colors.Background;
        using var track = context.Div(key: "progress").FillWidth().Height(3).Background(t.Border).Semantics(Role.Image, "Progress",
            $"{Clamp01(progress) * 100:0}%");
        using var fill = context.Div(key: "fill").Absolute().Height(3).Background(bar);
        fill.Width(fill.Animate(width * Clamp01(progress), Motion.Gentle, MotionChannel.Width));
    }

    /// <summary>A faint ring with a quarter arc turning once every 0.8 s; it requests frames only while shown.</summary>
    void ToastSpinner(Color color, KitTheme t)
    {
        var turn = context.ReduceMotion ? 0 : (float)(context.Time % .8 / .8 * 360);
        using var box = context.Div(key: "spinner").Width(18).Height(20).Center();
        using var ring = context.Div(key: "ring").Width(16).Height(16).Rounded(t.RadiusFull).Border(Color.Mix(t.Popover, color, .2f), 2)
            .Rotate(turn).Semantics(Role.Image, "Working");
        using (context.Div(key: "quarter").Absolute().Offset(8, 0).Width(8).Height(8).Clip())
        using (context.Div(key: "arc").Absolute().Offset(-8, 0).Width(16).Height(16).Rounded(t.RadiusFull).Border(color, 2))
        {
        }
        if (!context.ReduceMotion)
            context.FrameIn(TimeSpan.FromSeconds(1 / 60d));
    }

    /// <summary>Counts a toast's duration down while nobody is looking at the stack, and reports expiry once.</summary>
    void Expire(Toast toast, ToastState state, bool paused, List<ToastEvent> events)
    {
        if (toast.Props.Duration is not { } duration || state.Expired)
            return;
        var now = context.Time;
        if (state.Remaining is not { } remaining)
            remaining = duration.TotalSeconds;
        else if (!paused)
            remaining -= now - state.Seen;
        (state.Remaining, state.Seen) = (remaining, now);
        if (remaining <= 0)
        {
            state.Expired = true;
            events.Add(new(toast.Id, ToastEventKind.Expired));
        }
        else if (!paused)
        {
            context.FrameIn(TimeSpan.FromSeconds(remaining));
        }
    }

    static Rect Union(Rect a, Rect b)
    {
        var left = MathF.Min(a.X, b.X);
        var top = MathF.Min(a.Y, b.Y);
        return new(left, top, MathF.Max(a.Right, b.Right) - left, MathF.Max(a.Bottom, b.Bottom) - top);
    }

    sealed class ToasterState
    {
        public Rect? Region;
        public bool Focused;
        /// <summary>Each toast's measured content height, read before its card is built.</summary>
        public readonly Dictionary<string, float> Heights = new(StringComparer.Ordinal);
    }

    sealed class ToastState
    {
        public float Natural, PressX, Drag;
        public bool Holding, Moved, Expired;
        public double? Remaining;
        public double Seen;
    }
}
