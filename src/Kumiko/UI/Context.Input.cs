namespace Kumiko.UI;

public sealed partial class Context
{
    void ResolveInput()
    {
        SelectInput();
        hot = 0;
        hotPath.Clear();
        for (int i = hits.Count - 1; i >= 0; i--)
        {
            var h = hits[i];
            if (!h.Rect.Contains(Input.Pointer))
                continue;
            hotPath.Add(h.Id);
            if (hot == 0 && h.Interactive)
                hot = h.Id;
        }

        TrackScrollbar();
        if (Input.Pressed)
        {
            active = hot;
            focus = hot;
        }

        if (Input.ScrollY != 0 || Input.ScrollX != 0)
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                var h = hits[i];
                if (DebuggerBlocksScroll(h.Id) || modalRoot != 0 && !InModal(h) || !h.Scroll || !h.Rect.Contains(Input.Pointer) || !Scrolls.TryGetValue(h.Id, out var s))
                    continue;
                var shift = (Input.Modifiers & Modifiers.Shift) != 0;
                var across = Input.ScrollX != 0 ? Input.ScrollX : shift ? Input.ScrollY : 0;
                // A virtual list scrolls both ways; each event moves along its dominant axis so a vertical swipe
                // never drifts sideways.
                if (CrossScrolls.TryGetValue(h.Id, out var cross) && MathF.Abs(across) > MathF.Abs(shift ? 0 : Input.ScrollY))
                {
                    if (ScrollBy(cross, across))
                        break;
                    continue;
                }
                float delta = s.Horizontal ? across : shift ? 0 : Input.ScrollY;
                if (ScrollBy(s, delta))
                    break;
            }

        // Control/Option/Command+Tab belong to application shortcuts such as tab switchers.
        var focusTab = Input.Key == UI.Key.Tab && (Input.Modifiers & ~Modifiers.Shift) == 0;
        if (focusTab)
        {
            foreach (var preview in keyPreviews.Reverse())
                if (FocusWithin(preview.Key) && preview.Value(this))
                {
                    Input.Key = UI.Key.None;
                    break;
                }
        }
        if (focusTab && Input.Key == UI.Key.Tab)
        {
            var controls = hits.Where(h => h.Interactive && h.TabStop && (modalRoot == 0 || InModal(h))).ToArray();
            if (controls.Length > 0)
            {
                var index = Array.FindIndex(controls, h => h.Id == focus);
                index = (index + ((Input.Modifiers & Modifiers.Shift) != 0 ? -1 : 1) + controls.Length) % controls.Length;
                focus = controls[index].Id;
            }

            Input.Key = UI.Key.None;
        }
    }

    /// <summary>Moves a scroller by an input delta; wheel notches glide. False when it cannot move that way.</summary>
    bool ScrollBy(ScrollState s, float delta)
    {
        if (!Input.ScrollPrecise)
        {
            // Wheel notches glide; a notch during a glide extends it from where it was heading.
            var from = float.IsNaN(s.Target) ? s.Offset : s.Target;
            var target = Math.Clamp(from + delta, 0, s.Max);
            if (target == from)
                return false;
            Glide(s, target, WheelGlide, false);
            return true;
        }
        var offset = Math.Clamp(s.Offset + delta, 0, s.Max);
        if (offset == s.Offset)
            return false;
        s.Target = float.NaN;
        s.Offset = offset;
        s.Follow = s.Offset >= s.Max - 1;
        return true;
    }
}
