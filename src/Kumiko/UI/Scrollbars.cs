namespace Kumiko.UI;

/// <summary>
/// Scroll thumbs: Paint draws them, and the pointer drags them. Pressing a bar grabs the thumb where it was
/// pressed, or centers it under the pointer when the press lands on the track. The press never reaches the
/// content beneath the bar.
/// </summary>
public sealed partial class Context
{
    /// <summary>Pointer reach from a scroller's bar edge; wider than the painted thumb so it is easy to hit.</summary>
    const float ScrollbarReach = 12;
    ulong scrollbarDrag;
    ulong scrollbarHover;
    float scrollbarGrab;
    /// <summary>The dragged bar is a virtual list's horizontal bar, kept in <see cref="CrossScrolls"/>.</summary>
    bool scrollbarCross;

    /// <summary>Whether the pointer is over or dragging this scroller's bar, which then paints wider.</summary>
    internal bool ScrollbarActive(ulong id) => id != 0 && (scrollbarDrag == id || scrollbarHover == id);

    internal static Rect ScrollThumb(Rect track, ScrollState s, bool rightToLeft, float thickness)
    {
        var extent = s.Horizontal ? track.Width : track.Height;
        var length = ThumbLength(extent, s);
        var along = (extent - length) * s.Offset / s.Max;
        var inset = 2 + thickness;
        // Right-to-left scrollers keep their bar on the left and start horizontal travel at the right.
        return s.Horizontal
            ? new Rect(rightToLeft ? track.Right - length - along : track.X + along, track.Bottom - inset, length, thickness)
            : new Rect(rightToLeft ? track.X + 2 : track.Right - inset, track.Y + along, thickness, length);
    }

    static float ThumbLength(float extent, ScrollState s) => Math.Max(20, extent * extent / (extent + s.Max));

    /// <summary>The pointer's distance along the bar from where the thumb's travel starts.</summary>
    static float Along(ScrollState s, Point pointer) => s.Horizontal
        ? s.RightToLeft ? s.Track.Right - pointer.X : pointer.X - s.Track.X
        : pointer.Y - s.Track.Y;

    bool OnBar(ScrollState s, Point pointer)
    {
        var r = s.Track;
        if (s.Max <= 0 || !r.Contains(pointer))
            return false;
        if (s.Horizontal)
            return pointer.Y >= r.Bottom - ScrollbarReach;
        return s.RightToLeft ? pointer.X <= r.X + ScrollbarReach : pointer.X >= r.Right - ScrollbarReach;
    }

    /// <summary>Runs after hover resolution and before presses activate controls.</summary>
    void TrackScrollbar()
    {
        if (scrollbarDrag != 0)
        {
            if ((scrollbarCross ? CrossScrolls : Scrolls).TryGetValue(scrollbarDrag, out var dragged))
                DragThumb(dragged);
            if (!Input.Down || Input.Released)
                scrollbarDrag = 0;
            Input.Pressed = false;
            hot = 0;
            return;
        }
        scrollbarHover = 0;
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            var h = hits[i];
            if (!h.Scroll || !h.Rect.Contains(Input.Pointer) || DebuggerBlocksScroll(h.Id) || modalRoot != 0 && !InModal(h))
                continue;
            var cross = CrossScrolls.TryGetValue(h.Id, out var across) && OnBar(across, Input.Pointer);
            if (!cross && (!Scrolls.TryGetValue(h.Id, out across) || !OnBar(across, Input.Pointer)))
                continue;
            var s = across!;
            // A control layered over the bar from outside the scroller, such as a resize handle, keeps its press.
            if (hot != 0 && !Within(hot, h.Id))
                return;
            scrollbarHover = h.Id;
            hot = 0;
            if (!Input.Pressed || Input.Button != PointerButton.Primary)
                return;
            var extent = s.Horizontal ? s.Track.Width : s.Track.Height;
            var length = ThumbLength(extent, s);
            var start = (extent - length) * s.Offset / s.Max;
            var along = Along(s, Input.Pointer);
            scrollbarGrab = along >= start && along <= start + length ? along - start : length / 2;
            scrollbarDrag = h.Id;
            scrollbarCross = cross;
            Input.Pressed = false;
            DragThumb(s);
            return;
        }
    }

    void DragThumb(ScrollState s)
    {
        var extent = s.Horizontal ? s.Track.Width : s.Track.Height;
        var travel = extent - ThumbLength(extent, s);
        if (travel <= 0 || s.Max <= 0)
            return;
        s.Target = float.NaN;
        s.Offset = Math.Clamp((Along(s, Input.Pointer) - scrollbarGrab) / travel * s.Max, 0, s.Max);
        s.Follow = s.Offset >= s.Max - 1;
    }
}
