namespace Kumiko.UI;

/// <summary>Payload kinds with a meaning to Kumiko.</summary>
public static class DragKinds
{
    /// <summary>Files dragged in from the operating system; <see cref="DropEvent.Files"/> holds their absolute paths.</summary>
    public const string Files = "kumiko/files";
}

/// <summary>An in-window drag payload: a kind that targets accept, such as "task", and a value, such as its ID.</summary>
public sealed record DragPayload(string Kind, string Value);

/// <summary>What was dropped: an in-window <see cref="Item"/>, or files from the system.</summary>
public sealed record DropEvent(DragPayload? Item, IReadOnlyList<string> Files, Point Position);

/// <summary>
/// A drop target's state for this frame. <see cref="IsOver"/> is true while an accepted payload hovers over it;
/// <see cref="Dropped"/> is set on the frame it is released here.
/// </summary>
public readonly record struct DropState(bool IsOver, DropEvent? Dropped);

public sealed partial class Context
{
    const float DragThreshold = 6;
    Point pressOrigin;
    DragPayload? dragPayload;
    string? dragLabel;
    bool dragStarted, dragEnded, dropDelivered;
    /// <summary>A drag started since the last press, so its release never clicks, even after Escape cancels it.</summary>
    bool dragged;
    readonly List<(ulong Id, string[] Accepts)> dropTargets = new(), nextDropTargets = new();
    ulong dropWinner;
    Point dragGrab;
    Size dragSize;

    /// <summary>The in-window payload being dragged, if any; null from the frame it is released or cancelled.</summary>
    public DragPayload? Dragging => dragStarted && !dragEnded ? dragPayload : null;

    /// <summary>
    /// The held element's upright box in window coordinates: its size where it was grabbed, kept at the same offset
    /// from the pointer. Use it to find where a dragged item would land in a list; it stays set during the frame the
    /// item is dropped. Null when nothing is dragged.
    /// </summary>
    public Rect? DraggedRect => dragStarted && dragPayload is not null
        ? new Rect(Input.Pointer.X - dragGrab.X, Input.Pointer.Y - dragGrab.Y, dragSize.Width, dragSize.Height)
        : null;

    bool Dragged => dragStarted && dragPayload is not null || Input.DragFiles is { Length: > 0 } || Input.DroppedFiles is { Length: > 0 };

    /// <summary>Starts, cancels or ends drags before the view is built.</summary>
    void TrackDrag()
    {
        dragEnded = false;
        dropDelivered = false;
        dropTargets.Clear();
        dropTargets.AddRange(nextDropTargets);
        nextDropTargets.Clear();
        if (Input.Pressed)
        {
            pressOrigin = Input.Pointer;
            dragPayload = null;
            dragStarted = false;
            dragged = false;
        }
        if (dragStarted && Input.Key == UI.Key.Escape)
        {
            Input.Key = UI.Key.None;
            dragPayload = null;
            dragStarted = false;
        }
        if (dragStarted && (Input.Released || !Input.Down))
            dragEnded = true;
        dropWinner = Winner();
    }

    /// <summary>The innermost target from the last frame that accepts the current payload and contains the pointer.</summary>
    ulong Winner()
    {
        if (!Dragged)
            return 0;
        var kind = dragStarted && dragPayload is not null ? dragPayload.Kind : DragKinds.Files;
        for (var i = dropTargets.Count - 1; i >= 0; i--)
            if (dropTargets[i].Accepts.Contains(kind) && rects.TryGetValue(dropTargets[i].Id, out var rect) && rect.Contains(Input.Pointer))
                return dropTargets[i].Id;
        return 0;
    }

    /// <summary>Clears a finished drag after the frame that delivered it.</summary>
    void FinishDrag()
    {
        if (dragEnded)
        {
            dragPayload = null;
            dragStarted = false;
        }
        if (dragStarted)
            Cursor = Cursor.Hand;
    }

    internal void DragSource(int index, DragPayload payload, string? label)
    {
        ref var n = ref Tree[index];
        if (Input.Pressed && hot == n.Id && Input.Button == PointerButton.Primary)
        {
            dragPayload = payload;
            dragLabel = label;
        }
        if (!dragStarted && dragPayload is not null && active == n.Id && Input.Down)
        {
            var dx = Input.Pointer.X - pressOrigin.X;
            var dy = Input.Pointer.Y - pressOrigin.Y;
            if (dx * dx + dy * dy >= DragThreshold * DragThreshold)
            {
                dragStarted = true;
                dragged = true;
                var from = rects.TryGetValue(n.Id, out var rect) ? rect : new(pressOrigin.X, pressOrigin.Y, 0, 0);
                dragGrab = new(pressOrigin.X - from.X, pressOrigin.Y - from.Y);
                dragSize = new(from.Width, from.Height);
            }
        }
        if (dragStarted && !dragEnded && active == n.Id && dragLabel is { Length: > 0 } text)
            Preview(text);
    }

    /// <summary>A small label following the pointer while dragging; it never takes input.</summary>
    void Preview(string text)
    {
        using var layer = Overlay("kumiko-drag-preview", false);
        var at = ToLayout(new Rect(Input.Pointer.X, Input.Pointer.Y, 0, 0));
        using var chip = Div(key: "chip").Absolute().Offset(at.X + 12, at.Y + 8).Px(Theme.Space.S2).Py(Theme.Space.S1)
            .Rounded(Theme.Radius.Sm).Background(Theme.Accent).Opacity(.9f);
        Text(text, Theme.Typography.Label with
        {
            Color = Theme.AccentText
        }, key: "text");
    }

    internal DropState DropTarget(int index, string[] accepts)
    {
        var id = Tree[index].Id;
        nextDropTargets.Add((id, accepts));
        if (dropWinner != id)
            return default;
        if (dropDelivered)
            return new(false, null);
        DropEvent? dropped = null;
        if (dragStarted && dragEnded && dragPayload is { } item)
            dropped = new(item, [], Input.Pointer);
        else if (Input.DroppedFiles is { Length: > 0 } files)
            dropped = new(null, files, Input.Pointer);
        if (dropped is not null)
        {
            // Rebuild once so the application's response appears in this frame; deliver only once.
            dropDelivered = true;
            Changed = true;
        }
        return new(dropped is null, dropped);
    }
}

public readonly partial struct Div
{
    /// <summary>
    /// Lets this element be dragged after the pointer moves 6 px while pressed. Call <see cref="Control"/> too;
    /// a drag never also clicks. <paramref name="label"/> follows the pointer as a preview. Offer a keyboard or
    /// menu alternative for anything users can drag (WCAG 2.5.7).
    /// </summary>
    public Div DragSource(string kind, string value, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        context.DragSource(index, new(kind, value), label);
        return this;
    }

    /// <summary>
    /// Lets this element be dragged after the pointer moves 6 px while pressed, lifting the element itself: its image
    /// follows the pointer with <paramref name="style"/>'s tilt, sway, scale and shadow, a placeholder keeps its place,
    /// and after a drop it glides into wherever the element is built next, or back home when Escape cancels. Keep the
    /// element's key stable so it is found after the drop. Call <see cref="Control"/> too; a drag never also clicks.
    /// Offer a keyboard or menu alternative for anything users can drag (WCAG 2.5.7).
    /// </summary>
    public Div DragSource(string kind, string value, DragStyle style)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        var payload = new DragPayload(kind, value);
        ref var node = ref context.At(index);
        node.Draggable = payload;
        node.DragLook = style;
        context.DragSource(index, payload, null);
        return this;
    }

    /// <summary>
    /// Registers this element as a drop target for the given payload kinds (<see cref="DragKinds.Files"/> for system
    /// files). The innermost accepting target under the pointer receives the drop. Call after styling.
    /// </summary>
    public DropState DropTarget(params string[] accepts) => context.DropTarget(index, accepts);
}
