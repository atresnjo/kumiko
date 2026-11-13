namespace Kumiko.UI;

public sealed partial class Context
{
    /// <summary>Seconds a wheel notch takes to glide to its offset.</summary>
    const float WheelGlide = .16f;
    readonly List<(int Index, long Request, float Margin, Motion Motion)> reveals = new();
    readonly Dictionary<ulong, long> revealed = new();

    /// <summary>
    /// Eases <paramref name="s"/> from its current offset to <paramref name="target"/>. Wheel glides ease out so
    /// the page responds at once; <paramref name="symmetric"/> glides (reveals) also ease in, as page jumps should.
    /// </summary>
    internal void Glide(ScrollState s, float target, float duration, bool symmetric)
    {
        target = Math.Clamp(target, 0, s.Max);
        if (ReduceMotion || duration <= 0 || target == s.Offset)
        {
            s.Offset = target;
            s.Target = float.NaN;
            s.Follow = s.Offset >= s.Max - 1;
            return;
        }
        // Repeated requests for an active destination keep its progress through the easing curve.
        if (s.Symmetric == symmetric && MathF.Abs(s.Target - target) < .01f)
            return;
        s.From = s.Offset;
        s.Target = target;
        s.Elapsed = 0;
        s.Duration = duration;
        s.Symmetric = symmetric;
    }

    /// <summary>Moves every gliding scroller one frame along its curve before the view is built.</summary>
    void AdvanceScrolls()
    {
        foreach (var s in Scrolls.Values)
            Advance(s);
        foreach (var s in CrossScrolls.Values)
            Advance(s);
    }

    void Advance(ScrollState s)
    {
        if (float.IsNaN(s.Target))
            return;
        s.Elapsed += DeltaTime;
        var t = Math.Min(1, s.Elapsed / s.Duration);
        var eased = s.Symmetric
            ? t < .5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2
            : 1 - MathF.Pow(1 - t, 3);
        s.Offset = Math.Clamp(s.From + (s.Target - s.From) * eased, 0, s.Max);
        if (t >= 1)
            s.Target = float.NaN;
        else
            Animating = true;
        s.Follow = s.Offset >= s.Max - 1;
    }

    internal void Reveal(int index, long request, float margin, Motion motion) => reveals.Add((index, request, margin, motion));

    /// <summary>
    /// Starts the glides requested during this build, using final layout: an element added or moved in the same
    /// frame still lands exactly. Longer distances take a little longer, up to a quarter second more.
    /// </summary>
    void ApplyReveals()
    {
        foreach (var (index, request, margin, motion) in reveals)
        {
            var id = Tree[index].Id;
            if (revealed.TryGetValue(id, out var last) && last == request)
                continue;
            revealed[id] = request;
            for (var a = Tree[index].Parent; a >= 0; a = Tree[a].Parent)
            {
                ref readonly var style = ref Tree[a].Style;
                if (!style.Scroll || style.ScrollHorizontal || !Scrolls.TryGetValue(Tree[a].Id, out var state))
                    continue;
                var top = Tree[a].Rect.Y + style.Padding.Top;
                var target = Math.Clamp(state.Offset + Tree[index].Rect.Y - margin - top, 0, state.Max);
                var duration = motion switch
                {
                    Motion.None => 0,
                    Motion.Quick => .2f,
                    Motion.Standard => .32f,
                    Motion.Slow => .7f,
                    _ => .45f
                } + Math.Min(.25f, Math.Abs(target - state.Offset) / 4000);
                Glide(state, target, motion == Motion.None ? 0 : duration, true);
                Animating = true;
                break;
            }
        }
        reveals.Clear();
    }
}

public readonly partial struct Div
{
    /// <summary>
    /// Smoothly scrolls the nearest vertical scroll ancestor until this element's top sits <paramref name="margin"/>
    /// below the viewport's content edge (clamped to the scroll range). It starts once per distinct
    /// <paramref name="request"/>: keep calling it with the same value from the model, and change the value to
    /// scroll again. Wheel or trackpad input interrupts it; ReduceMotion jumps.
    /// </summary>
    public Div Reveal(long request, float margin = 0, Motion animate = Motion.Gentle)
    {
        context.Reveal(index, request, margin, animate);
        return this;
    }
}
