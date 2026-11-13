namespace Kumiko.UI;

public sealed partial class Context
{
    readonly Dictionary<int, int[]> overlayStacks = new();
    ulong modalRoot;
    ulong inputModalRoot;
    readonly HashSet<ulong> focusPath = new();
    readonly Dictionary<ulong, Func<Context, bool>> keyPreviews = new();

    bool EscapeAllowed => !NavigationCompositionActive && (inputModalRoot == 0 ||
        stack.Any(index => Tree[index].Id == inputModalRoot));

    /// <summary>Registers scoped Tab handling for a composite control for the next frame.</summary>
    public void PreviewTab(ulong scope, Func<Context, bool> handler) => keyPreviews[scope] = handler;

    /// <summary>Begins a window-level overlay, outside ancestor clipping and layout.</summary>
    public Div Overlay(NodeKey key) => Overlay(key, true);

    /// <summary>
    /// Begins a window-level layer. A modal layer confines Tab and scrolling; a non-modal layer, such as
    /// a tooltip, leaves input with the page.
    /// </summary>
    internal Div Overlay(NodeKey key, bool modal)
    {
        CheckThread();
        if (contentRenderDepth > 0)
            throw new InvalidOperationException("A Kit content renderer must stay inside its component; overlays are not content.");
        var saved = stack.ToArray();
        stack.Clear();
        stack.Add(0);
        var overlay = Div(key).Absolute().Offset(0, 0).SizeFull();
        Tree[stack[^1]].IsOverlay = true;
        Tree[stack[^1]].Opener = saved.Length > 0 ? saved[^1] : 0;
        overlayStacks[stack[^1]] = saved;
        if (modal)
            modalRoot = overlay.Id;
        return overlay;
    }

    /// <summary>Moves focus to an existing enabled control.</summary>
    public void FocusControl(ulong id)
    {
        CheckThread();
        if (id == 0 || hits.Any(h => h.Id == id && h.Interactive))
            FocusNode(id);
    }

    public bool FocusWithin(ulong id)
    {
        if (focusPath.Contains(id))
            return true;
        var current = focus;
        while (current != 0)
        {
            if (current == id)
                return true;
            current = Owner(current);
        }
        return false;
    }

    /// <summary>
    /// The node that owns <paramref name="id"/> for focus: its parent, or for an overlay the scope that opened
    /// it, so a popover opened from a dialog keeps focus within that dialog.
    /// </summary>
    ulong Owner(ulong id)
    {
        var index = Array.FindIndex(Tree, 0, Count, n => n.Id == id);
        if (index < 0)
            return 0;
        var owner = Tree[index].IsOverlay ? Tree[index].Opener : Tree[index].Parent;
        return owner >= 0 && owner != index ? Tree[owner].Id : 0;
    }

    void RememberFocusPath()
    {
        focusPath.Clear();
        var current = focus;
        while (current != 0)
        {
            focusPath.Add(current);
            current = Owner(current);
        }
    }

    bool InModal(Hit hit)
    {
        if (debuggerNodes.Contains(hit.Id))
            return true;
        var current = hit.Id;
        while (current != 0)
        {
            if (current == modalRoot)
                return true;
            var node = Array.FindIndex(Tree, 0, Count, n => n.Id == current);
            current = node >= 0 && Tree[node].Parent >= 0 ? Tree[Tree[node].Parent].Id : 0;
        }
        return false;
    }

    ulong OverlayLayer(int index)
    {
        for (int i = index; i >= 0; i = Tree[i].Parent)
            if (Tree[i].IsOverlay)
                return Tree[i].Id;
        return 0;
    }
}
