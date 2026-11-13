namespace Kumiko.UI;

public sealed partial class Context
{
    /// <summary>One shared element: the parent it was last built under and the rect it was drawn at.</summary>
    sealed class SharedLayoutState
    {
        public ulong Owner;
        public Rect Shown, From;
        public float Elapsed, Duration;
        public long Frame;
    }

    readonly Dictionary<string, SharedLayoutState> sharedLayouts = new(StringComparer.Ordinal);

    /// <summary>
    /// Where a node with the same <see cref="Node.SharedLayout"/> id was drawn last frame under another parent, it
    /// is drawn between that rect and its new layout rect, easing out, until the motion completes.
    /// </summary>
    void ApplySharedLayouts()
    {
        // UIKit owns compositor animations; its nodes keep final layout and hit-test bounds.
        if (ControlRenderer is not null)
            return;
        for (var i = 0; i < Count; i++)
        {
            if (Tree[i].SharedLayout is not { } id)
                continue;
            var owner = Tree[i].Parent >= 0 ? Tree[Tree[i].Parent].Id : 0;
            var target = Tree[i].Rect;
            if (!sharedLayouts.TryGetValue(id, out var state) || state.Frame < frame - 1)
            {
                sharedLayouts[id] = new()
                {
                    Owner = owner,
                    Shown = target,
                    Frame = frame
                };
                continue;
            }
            if (state.Frame == frame)
                continue;
            if (state.Owner != owner)
            {
                state.Owner = owner;
                state.From = state.Shown;
                state.Elapsed = 0;
                state.Duration = ReduceMotion ? 0 : Seconds(Tree[i].SharedMotion);
            }
            else
            {
                state.Elapsed += DeltaTime;
            }
            var progress = state.Duration <= 0 ? 1 : Math.Clamp(state.Elapsed / state.Duration, 0, 1);
            var shown = progress >= 1 ? target : Blend(state.From, target, 1 - MathF.Pow(1 - progress, 4));
            state.Shown = shown;
            state.Frame = frame;
            if (progress < 1)
                Animating = true;
            Tree[i].Rect = shown;
            Translate(Tree[i].FirstChild, shown.X - target.X, shown.Y - target.Y);
        }
        if (frame % 120 == 0)
            foreach (var entry in sharedLayouts)
                if (entry.Value.Frame < frame - 1)
                    sharedLayouts.Remove(entry.Key);
    }

    static Rect Blend(Rect from, Rect to, float amount) => new(
        from.X + (to.X - from.X) * amount,
        from.Y + (to.Y - from.Y) * amount,
        from.Width + (to.Width - from.Width) * amount,
        from.Height + (to.Height - from.Height) * amount);

    void Translate(int child, float dx, float dy)
    {
        for (; child >= 0; child = Tree[child].Next)
        {
            var r = Tree[child].Rect;
            Tree[child].Rect = r with
            {
                X = r.X + dx,
                Y = r.Y + dy
            };
            Translate(Tree[child].FirstChild, dx, dy);
        }
    }
}
