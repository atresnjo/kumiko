using Kumiko.Rendering;

namespace Kumiko.UI;

public sealed partial class Context
{
    /// <summary>Where a lifted image is drawn: its upright box, its turn in radians, its scale and how far it is lifted.</summary>
    readonly record struct Pose(Rect Box, float Angle, float Scale, float Height);

    /// <summary>The image of an element being dragged, captured when it lifted.</summary>
    sealed class Held(DragPayload payload, DragStyle style, Instance[] image, Rect source, Rect clip, float radius)
    {
        public readonly DragPayload Payload = payload;
        public readonly DragStyle Style = style;
        public readonly Instance[] Image = image;
        public readonly Rect Source = source, Clip = clip;
        public readonly float Radius = radius;
        public float Elapsed, Velocity;
        public Point Last;
        public Pose Pose;
    }

    /// <summary>
    /// A dropped or cancelled element gliding from its last held pose into the place it is built. Until the element is
    /// built again, for a few frames at most, its lifted image waits at that pose.
    /// </summary>
    sealed class Landing(Held held)
    {
        public readonly Held Held = held;
        public readonly DragPayload Payload = held.Payload;
        public readonly DragStyle Style = held.Style;
        public readonly Pose From = held.Pose;
        public float Elapsed;
        public int Missing;
    }

    Held? held;
    readonly List<Landing> landings = new();

    /// <summary>
    /// Runs after paint and enter transitions: lifts the dragged element's image, keeps a placeholder in its place,
    /// draws the image under the pointer with its tilt and shadow, and glides dropped elements into place.
    /// </summary>
    void ApplyDrag()
    {
        var holding = dragStarted && !dragEnded && dragPayload is not null;
        if (held is not null && (!holding || held.Payload != dragPayload))
        {
            landings.Add(new(held));
            held = null;
        }
        if (holding && held is null)
            held = Lift(dragPayload!);
        for (var i = landings.Count - 1; i >= 0; i--)
            if (!Land(landings[i]))
                landings.RemoveAt(i);
        if (held is null)
            return;
        var pose = Hold(held);
        var source = FindDraggable(held.Payload);
        if (source is { } range && held.Style.Placeholder != DragPlaceholder.Keep)
        {
            Fade(instances, range.Start, range.End, 0);
            if (held.Style.Placeholder == DragPlaceholder.Ghost)
            {
                var ghost = Instance.Quad(range.Rect, held.Style.PlaceholderColor ?? Theme.Text.Alpha(.06f), range.Clip, range.Radius);
                ghost.BorderColor = Theme.Text.Alpha(.08f).Vector;
                ghost.BorderWidth = 1;
                instances.Insert(range.Start, ghost);
            }
        }
        var start = instances.Count;
        instances.AddRange(held.Image);
        Place(instances, start, instances.Count, held.Source, held.Clip, pose, held.Style, held.Radius);
    }

    /// <summary>Captures the dragged element's image from this frame's paint, or the last frame's if it is gone.</summary>
    Held? Lift(DragPayload payload)
    {
        foreach (var (ranges, list) in new[] { (painted, instances), (previousPainted, previousInstances) })
            foreach (var range in ranges)
                if (range.Draggable == payload && range.End > range.Start)
                {
                    var image = new Instance[range.End - range.Start];
                    list.CopyTo(range.Start, image, 0, image.Length);
                    return new(payload, range.Look, image, range.Rect, range.Clip, range.Radius)
                    {
                        Last = Input.Pointer,
                        Pose = new(range.Rect, 0, 1, 0)
                    };
                }
        return null;
    }

    PaintedRange? FindDraggable(DragPayload payload)
    {
        foreach (var range in painted)
            if (range.Draggable == payload)
                return range;
        return null;
    }

    /// <summary>The held pose: under the pointer, lifting in, tilted and leaning with the sideways speed.</summary>
    Pose Hold(Held h)
    {
        var style = h.Style;
        h.Elapsed += DeltaTime;
        var lift = ReduceMotion ? 1 : Ease(Math.Clamp(h.Elapsed / Math.Max(.001f, Seconds(style.Lift)), 0, 1));
        var pointer = Input.Pointer;
        if (DeltaTime > 0 && !ReduceMotion)
        {
            var speed = (pointer.X - h.Last.X) / DeltaTime;
            h.Velocity += (speed - h.Velocity) * (1 - MathF.Exp(-DeltaTime * 12));
        }
        else if (ReduceMotion)
        {
            h.Velocity = 0;
        }
        h.Last = pointer;
        var limit = MathF.Abs(style.MaxAngle);
        var degrees = style.Angle * lift + h.Velocity / 100 * style.Sway;
        degrees = limit > 0 ? Math.Clamp(degrees, -limit, limit) : 0;
        var scale = 1 + ((style.Scale > 0 ? style.Scale : 1) - 1) * lift;
        var box = DraggedRect ?? h.Pose.Box;
        h.Pose = new(box, degrees * MathF.PI / 180, scale, lift);
        if (lift < 1 || MathF.Abs(h.Velocity) > 1)
            Animating = true;
        return h.Pose;
    }

    /// <summary>Glides a dropped element from its held pose into its new place; false once it has landed.</summary>
    bool Land(Landing landing)
    {
        if (FindDraggable(landing.Payload) is not { } range)
        {
            if (++landing.Missing > 3)
                return false;
            var waiting = instances.Count;
            instances.AddRange(landing.Held.Image);
            Place(instances, waiting, instances.Count, landing.Held.Source, landing.Held.Clip, landing.From, landing.Style, landing.Held.Radius);
            Animating = true;
            return true;
        }
        landing.Elapsed += DeltaTime;
        var duration = Seconds(landing.Style.Settle);
        var progress = ReduceMotion || duration <= 0 ? 1 : Math.Clamp(landing.Elapsed / duration, 0, 1);
        if (progress >= 1)
            return false;
        var t = Ease(progress);
        var from = landing.From;
        var to = range.Rect;
        var box = new Rect(from.Box.X + (to.X - from.Box.X) * t, from.Box.Y + (to.Y - from.Box.Y) * t, from.Box.Width + (to.Width - from.Box.Width) * t,
            from.Box.Height + (to.Height - from.Box.Height) * t);
        var pose = new Pose(box, from.Angle * (1 - t), 1 + (from.Scale - 1) * (1 - t), from.Height * (1 - t));
        var start = instances.Count;
        for (var i = range.Start; i < range.End; i++)
            instances.Add(instances[i]);
        Fade(instances, range.Start, range.End, 0);
        Place(instances, start, instances.Count, range.Rect, range.Clip, pose, landing.Style, range.Radius);
        Animating = true;
        return true;
    }

    static void Fade(List<Instance> list, int start, int end, float opacity)
    {
        for (var i = start; i < end; i++)
        {
            var item = list[i];
            item.Color.W *= opacity;
            item.BorderColor.W *= opacity;
            list[i] = item;
        }
    }

    /// <summary>
    /// Moves an element's image from <paramref name="source"/> to <paramref name="pose"/>: scaled about the box's center,
    /// turned about it, above a shadow. Clips the element inherited from its ancestors give way to the window, so the
    /// image is never cut off by the list it left; its own clips move with it.
    /// </summary>
    void Place(List<Instance> list, int start, int end, Rect source, Rect inherited, Pose pose, DragStyle style, float radius)
    {
        var scale = pose.Scale;
        var cx = pose.Box.X + pose.Box.Width / 2;
        var cy = pose.Box.Y + pose.Box.Height / 2;
        var dx = pose.Box.X - source.X;
        var dy = pose.Box.Y - source.Y;
        float X(float x) => cx + (x + dx - cx) * scale;
        float Y(float y) => cy + (y + dy - cy) * scale;
        var opacity = 1 + ((style.Opacity > 0 ? Math.Min(style.Opacity, 1) : 1) - 1) * pose.Height;
        var window = new System.Numerics.Vector4(-1e5f, -1e5f, 1e5f, 1e5f);
        for (var i = start; i < end; i++)
        {
            var item = list[i];
            item.Rect = new(X(item.Rect.X), Y(item.Rect.Y), item.Rect.Z * scale, item.Rect.W * scale);
            var own = item.Clip.X > inherited.X + .01f || item.Clip.Y > inherited.Y + .01f || item.Clip.Z < inherited.Right - .01f ||
                item.Clip.W < inherited.Bottom - .01f;
            item.Clip = own ? new(X(item.Clip.X), Y(item.Clip.Y), X(item.Clip.Z), Y(item.Clip.W)) : window;
            if (!own)
                item.ClipRound = 0;
            item.Radius *= scale;
            item.BorderWidth *= scale;
            item.Color.W *= opacity;
            item.BorderColor.W *= opacity;
            if (item.Turn.X != 0)
                item.Turn = new(item.Turn.X, X(item.Turn.Y), Y(item.Turn.Z), 0);
            list[i] = item;
        }
        if (pose.Angle != 0)
            Turn(list, start, end, pose.Angle, cx, cy);
        if (style.Shadow <= 0 || pose.Height <= 0)
            return;
        var lifted = pose.Height * Math.Min(style.Shadow, 1);
        var w = pose.Box.Width * scale;
        var h = pose.Box.Height * scale;
        var shadow = Instance.Quad(new(cx - w / 2, cy - h / 2 + 10 * lifted, w, h), Color.Black.Alpha((Theme.Dark ? .45f : .16f) * lifted),
            new(-1e5f, -1e5f, 2e5f, 2e5f), radius * scale);
        shadow.Kind = 2;
        shadow.BorderWidth = 4 + 14 * lifted;
        if (pose.Angle != 0)
            shadow.Turn = new(pose.Angle, cx, cy, 0);
        list.Insert(start, shadow);
    }
}
