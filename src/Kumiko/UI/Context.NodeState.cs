namespace Kumiko.UI;

public sealed partial class Context
{
    readonly Dictionary<ulong, object> nodeStates = new();
    bool focusVisible;
    bool pointerMoved;
    Point lastPointer;

    /// <summary>
    /// Transient interaction state owned by a built node, such as a calendar's visible month. It is
    /// released when the node stops being built, like text editing state.
    /// </summary>
    internal T NodeState<T>(ulong id) where T : class, new()
    {
        CheckThread();
        if (nodeStates.TryGetValue(id, out var existing) && existing is T state)
            return state;
        var created = new T();
        nodeStates[id] = created;
        return created;
    }

    /// <summary>
    /// True when focus last moved by keyboard, as CSS :focus-visible. Draw focus rings and focus-only affordances,
    /// such as tooltips, only then; a pointer press focuses without showing them.
    /// </summary>
    public bool FocusVisible => ReadUI(focusVisible && focus != 0);

    void TrackFocusSource()
    {
        pointerMoved = Input.Pointer != lastPointer || Input.ScrollY != 0 || Input.ScrollX != 0;
        lastPointer = Input.Pointer;
        if (Input.Pressed)
            focusVisible = false;
        else if (Input.Key != UI.Key.None)
            focusVisible = true;
    }

    /// <summary>
    /// Whether a menu row paints as highlighted. Moving the pointer onto the row focuses it, so pointer and
    /// keyboard share one highlight; a resting pointer never takes focus back from the keyboard.
    /// </summary>
    internal bool MenuHighlight(Interaction item)
    {
        CheckThread();
        if (item.Hovered && pointerMoved && focus != item.Id)
        {
            focus = item.Id;
            focusVisible = false;
            Changed = true;
        }
        return focus == item.Id && (item.Hovered || focusVisible);
    }

    /// <summary>Updates the accessibility properties of a node built earlier in this pass.</summary>
    internal void AmendAccessibility(ulong id, Func<AccessibleProperties, AccessibleProperties> amend)
    {
        CheckThread();
        for (var i = Count - 1; i >= 0; i--)
        {
            if (Tree[i].Id != id)
                continue;
            Tree[i].Accessible = amend(Tree[i].Accessible ?? new());
            return;
        }
    }

    void PruneNodeStates()
    {
        foreach (var entry in nodeStates)
            if (!liveIds.Contains(entry.Key))
                nodeStates.Remove(entry.Key);
    }
}
