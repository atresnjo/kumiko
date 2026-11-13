using Kumiko.Rendering;

namespace Kumiko.UI;

public sealed partial class Context
{
    /// <summary>A node with an enter or exit transition and the paint instances it produced this frame.</summary>
    readonly record struct PaintedRange(ulong Id, int Start, int End, Rect Rect, Rect Clip, Transition? EnterFrom, Transition? ExitTo,
        float Rotation, float Scale, float Radius, DragPayload? Draggable, DragStyle Look);

    /// <summary>One transition applied to a span of a departing image, relative to the image's first instance.</summary>
    readonly record struct Layer(int Start, int End, Rect Rect, Rect Clip, Transition To);

    /// <summary>The final image of nodes that stopped being built, animating to their exit transitions.</summary>
    sealed class Departure(Instance[] image, Layer[] layers)
    {
        public readonly Instance[] Image = image;
        public readonly Layer[] Layers = layers;
        public float Elapsed;
    }

    sealed class Arrival
    {
        public float Elapsed;
        public long Frame;
    }

    List<Instance> previousInstances = new(512);
    List<PaintedRange> painted = new(), previousPainted = new();
    readonly HashSet<ulong> paintedIds = new();
    readonly Dictionary<ulong, Arrival> arrivals = new();
    readonly List<Departure> departures = new();

    internal void Painted(int index, int start, int end, Rect clip)
    {
        ref readonly var n = ref Tree[index];
        painted.Add(new(n.Id, start, end, n.Rect, clip, n.EnterFrom, n.ExitTo, n.Rotation, n.Scale, n.Style.Radius, n.Draggable, n.DragLook));
    }

    /// <summary>
    /// Runs after paint: starts departures for nodes that disappeared, eases arriving nodes in from their enter
    /// transitions, and draws departing images above the page. Painted ranges are in post-order, so a nested
    /// node's transition applies before its ancestor's.
    /// </summary>
    void ApplyTransitions()
    {
        paintedIds.Clear();
        foreach (var range in painted)
            paintedIds.Add(range.Id);
        if (ReduceMotion)
            departures.Clear();
        else
            Depart();
        foreach (var range in painted)
        {
            if (range.Scale is > 0 and not 1)
                Transform(instances, range.Start, range.End, range.Rect, range.Clip, new(Opacity: 1, Scale: range.Scale), 1);
            if (range.Rotation != 0)
                Turn(instances, range.Start, range.End, range.Rotation * MathF.PI / 180, range.Rect.X + range.Rect.Width / 2,
                    range.Rect.Y + range.Rect.Height / 2);
            if (range.EnterFrom is not { } from)
                continue;
            if (!arrivals.TryGetValue(range.Id, out var arrival) || arrival.Frame < frame - 1)
                arrivals[range.Id] = arrival = new();
            else if (arrival.Frame != frame)
                arrival.Elapsed += DeltaTime;
            arrival.Frame = frame;
            var progress = Completion(arrival.Elapsed, from);
            if (progress >= 1)
                continue;
            Animating = true;
            Transform(instances, range.Start, range.End, range.Rect, range.Clip, from, 1 - Ease(progress));
        }
        ApplyDrag();
        for (var i = departures.Count - 1; i >= 0; i--)
        {
            var departure = departures[i];
            departure.Elapsed += DeltaTime;
            var start = instances.Count;
            var done = true;
            instances.AddRange(departure.Image);
            foreach (var layer in departure.Layers)
            {
                var progress = Completion(departure.Elapsed, layer.To);
                done &= progress >= 1;
                Transform(instances, start + layer.Start, start + layer.End, layer.Rect, layer.Clip, layer.To, Ease(progress));
            }
            if (done)
            {
                instances.RemoveRange(start, departure.Image.Length);
                departures.RemoveAt(i);
            }
            else
            {
                Animating = true;
            }
        }
        if (frame % 120 == 0)
            foreach (var entry in arrivals)
                if (entry.Value.Frame < frame - 1)
                    arrivals.Remove(entry.Key);
        (painted, previousPainted) = (previousPainted, painted);
    }

    /// <summary>
    /// Captures last frame's image of each outermost node with an exit transition that was not painted this frame,
    /// with the exits of its nested nodes as further layers. Ranges are in post-order, so an enclosing node comes
    /// after the nodes it contains.
    /// </summary>
    void Depart()
    {
        for (var i = 0; i < previousPainted.Count; i++)
        {
            var root = previousPainted[i];
            if (!Departing(root) || root.End <= root.Start)
                continue;
            var carried = false;
            for (var j = i + 1; j < previousPainted.Count && !carried; j++)
                carried = Departing(previousPainted[j]) && previousPainted[j].Start <= root.Start && previousPainted[j].End >= root.End;
            if (carried)
                continue;
            var layers = new List<Layer>();
            for (var j = 0; j <= i; j++)
            {
                var inner = previousPainted[j];
                if (Departing(inner) && inner.Start >= root.Start && inner.End <= root.End)
                    layers.Add(new(inner.Start - root.Start, inner.End - root.Start, inner.Rect, inner.Clip, inner.ExitTo!.Value));
            }
            var image = new Instance[root.End - root.Start];
            previousInstances.CopyTo(root.Start, image, 0, image.Length);
            departures.Add(new(image, [.. layers]));
        }
    }

    bool Departing(PaintedRange range) => range.ExitTo is not null && !paintedIds.Contains(range.Id);

    float Completion(float elapsed, Transition transition)
    {
        var duration = Seconds(transition.Motion);
        return ReduceMotion || duration <= 0 ? 1 : Math.Clamp((elapsed - transition.Delay) / duration, 0, 1);
    }

    /// <summary>Cubic ease-out: quick to respond, gentle to settle.</summary>
    static float Ease(float progress) => 1 - MathF.Pow(1 - progress, 3);

    /// <summary>
    /// Moves a span of instances <paramref name="amount"/> of the way from their laid-out look toward
    /// <paramref name="look"/>: opacity scales colors, offset and scale move rects and their clips, and the result
    /// stays inside the clip the node was painted under.
    /// </summary>
    static void Transform(List<Instance> list, int start, int end, Rect rect, Rect outer, Transition look, float amount)
    {
        if (amount <= 0)
            return;
        var opacity = 1 + (Math.Clamp(look.Opacity, 0, 1) - 1) * amount;
        var scale = 1 + ((look.Scale > 0 ? look.Scale : 1) - 1) * amount;
        var dx = (look.Relative ? look.X * rect.Width : look.X) * amount;
        var dy = (look.Relative ? look.Y * rect.Height : look.Y) * amount;
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        float X(float x) => cx + (x - cx) * scale + dx;
        float Y(float y) => cy + (y - cy) * scale + dy;
        for (var i = start; i < end; i++)
        {
            var item = list[i];
            item.Color.W *= opacity;
            item.BorderColor.W *= opacity;
            item.Rect = new(X(item.Rect.X), Y(item.Rect.Y), item.Rect.Z * scale, item.Rect.W * scale);
            item.Clip = new(MathF.Max(X(item.Clip.X), outer.X), MathF.Max(Y(item.Clip.Y), outer.Y), MathF.Min(X(item.Clip.Z), outer.Right),
                MathF.Min(Y(item.Clip.W), outer.Bottom));
            item.Radius *= scale;
            item.BorderWidth *= scale;
            if (item.Turn.X != 0)
                item.Turn = new(item.Turn.X, X(item.Turn.Y), Y(item.Turn.Z), 0);
            list[i] = item;
        }
    }

    /// <summary>
    /// Turns a span of instances by <paramref name="angle"/> radians about (<paramref name="px"/>, <paramref name="py"/>),
    /// after any turn they already have: two turns make one turn about a new pivot, or a shift when they cancel.
    /// </summary>
    internal static void Turn(List<Instance> list, int start, int end, float angle, float px, float py)
    {
        for (var i = start; i < end; i++)
        {
            var item = list[i];
            if (item.Kind == 4)
                continue;
            item.Turn = Compose(item.Turn, angle, px, py, ref item);
            list[i] = item;
        }
    }

    static System.Numerics.Vector4 Compose(System.Numerics.Vector4 first, float angle, float px, float py, ref Instance item)
    {
        if (first.X == 0)
            return new(angle, px, py, 0);
        float total = first.X + angle, s2 = MathF.Sin(angle), c2 = MathF.Cos(angle);
        // The pivot of the first turn, carried by the second.
        float ox = first.Y - px, oy = first.Z - py;
        float kx = px + ox * c2 - oy * s2, ky = py + ox * s2 + oy * c2;
        float s = MathF.Sin(total), c = MathF.Cos(total), det = 2 - 2 * c;
        if (det < 1e-6f)
        {
            float dx = kx - first.Y, dy = ky - first.Z;
            item.Rect = new(item.Rect.X + dx, item.Rect.Y + dy, item.Rect.Z, item.Rect.W);
            item.Clip = new(item.Clip.X + dx, item.Clip.Y + dy, item.Clip.Z + dx, item.Clip.W + dy);
            return default;
        }
        // Solve (I - R(total)) c = k - R(total) * first pivot for the combined pivot c.
        float rx = kx - (first.Y * c - first.Z * s), ry = ky - (first.Y * s + first.Z * c);
        return new(total, ((1 - c) * rx - s * ry) / det, (s * rx + (1 - c) * ry) / det, 0);
    }
}
