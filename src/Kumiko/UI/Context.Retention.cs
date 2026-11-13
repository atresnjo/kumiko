namespace Kumiko.UI;

/// <summary>A keyed owner of transient managed UI state. Dispose in reverse scope order.</summary>
public sealed class RetainedScope : IDisposable
{
    readonly Context context;
    readonly Div layout;
    readonly ulong id;
    bool disposed;
    internal RetainedScope(Context context, Div layout, bool visible)
    {
        this.context = context;
        this.layout = layout;
        id = layout.Id;
        Visible = visible;
    }
    /// <summary>Build children only when visible. Hidden owners retain state without input or layout.</summary>
    public bool Visible
    {
        get;
    }
    public void Dispose()
    {
        if (disposed)
            return;
        context.EndRetention(id);
        layout.Dispose();
        disposed = true;
    }
}

public sealed partial class Context
{
    /// <summary>
    /// Consumes native Back intent, Command+[ on macOS or Alt+Left on Linux and Windows. While the focused editor has an
    /// uncommitted input-method composition, the input method owns the keyboard and Back is not taken.
    /// </summary>
    public bool BackShortcut()
    {
        if (Input.Composition is not null || Input.CommitComposition || Edits.TryGetValue(focus, out var edit) && edit.Composition.Length > 0)
            return false;
        if (Input.Navigation == NavigationDirection.Back)
        {
            Input.Navigation = NavigationDirection.None;
            return true;
        }
        return OperatingSystem.IsMacOS() ? Shortcut(UI.Key.BracketLeft) : Shortcut(UI.Key.Left, Modifiers.Alt);
    }

    sealed class RetentionOwner
    {
        internal readonly HashSet<ulong> Ids = new();
        internal ulong Parent, Focus;
        internal long Pass;
        internal bool Visible;
    }
    readonly Dictionary<ulong, RetentionOwner> retentionOwners = new();
    readonly List<ulong> retentionStack = new();
    readonly Dictionary<ulong, List<ulong>> retentionChildren = new();
    readonly List<ulong> retiredOwners = new();
    readonly HashSet<ulong> releasedNodes = new();
    long retentionPass, retentionChildrenPass = -1;
    ulong returningFocus;

    /// <summary>
    /// Owns managed edit, scroll and node state across hidden frames. This is not native-control retention.
    /// Declare covered owners with visible:false and skip their children. Omitting an owner retires its state.
    /// </summary>
    public RetainedScope RetainedScope(NodeKey key, bool visible = true)
    {
        CheckThread();
        if (string.IsNullOrEmpty(key.Value))
            throw new ArgumentException("A retained owner requires a stable key.", nameof(key));
#pragma warning disable KUM001 // RetainedScope owns this layout.
        var layout = Div(key).Display(visible ? DisplayMode.Contents : DisplayMode.None);
#pragma warning restore KUM001
        BeginRetention(layout.Id, visible);
        return new(this, layout, visible);
    }

    void BeginRetention(ulong id, bool visible)
    {
        if (!retentionOwners.TryGetValue(id, out var owner))
            retentionOwners.Add(id, owner = new());
        if (owner.Pass == retentionPass)
            throw new InvalidOperationException("Duplicate retained scope identity.");
        owner.Parent = retentionStack.Count > 0 ? retentionStack[^1] : 0;
        owner.Pass = retentionPass;
        if (!visible)
        {
            Cover(owner);
            KeepCoveredOwners(id);
        }
        else
        {
            owner.Ids.Clear();
            // Restore once, then forget: a later covering records its own focus or none.
            if (!owner.Visible && owner.Focus != 0)
                returningFocus = owner.Focus;
            owner.Focus = 0;
        }
        owner.Visible = visible;
        retentionStack.Add(id);
    }

    /// <summary>Records focus when an owner becomes covered; later covered frames keep that record.</summary>
    void Cover(RetentionOwner owner)
    {
        if (owner.Visible)
            owner.Focus = owner.Ids.Contains(focus) ? focus : 0;
        owner.Visible = false;
    }

    void KeepCoveredOwners(ulong parent)
    {
        if (retentionChildrenPass != retentionPass)
        {
            // Owner identities include their parent path, so one map per pass describes nesting.
            foreach (var children in retentionChildren.Values)
                children.Clear();
            foreach (var pair in retentionOwners)
                if (pair.Value.Parent != 0)
                {
                    if (!retentionChildren.TryGetValue(pair.Value.Parent, out var children))
                        retentionChildren.Add(pair.Value.Parent, children = new());
                    children.Add(pair.Key);
                }
            retentionChildrenPass = retentionPass;
        }
        if (!retentionChildren.TryGetValue(parent, out var nested))
            return;
        foreach (var id in nested)
        {
            var owner = retentionOwners[id];
            owner.Pass = retentionPass;
            Cover(owner);
            KeepCoveredOwners(id);
        }
    }

    internal void EndRetention(ulong id)
    {
        CheckThread();
        if (retentionStack.Count == 0 || retentionStack[^1] != id)
            throw new InvalidOperationException("Retained scopes must end in reverse order.");
        retentionStack.RemoveAt(retentionStack.Count - 1);
    }

    void OwnRetainedNode(ulong id)
    {
        if (retentionStack.Count > 0)
            retentionOwners[retentionStack[^1]].Ids.Add(id);
    }

    /// <summary>
    /// Releases the state of owners omitted this frame. State of nodes that a live owner merely did not
    /// build keeps the ordinary periodic pruning, as outside retained scopes.
    /// </summary>
    void FinishRetention()
    {
        retiredOwners.Clear();
        foreach (var pair in retentionOwners)
            if (pair.Value.Pass != retentionPass)
                retiredOwners.Add(pair.Key);
        if (retiredOwners.Count > 0)
        {
            releasedNodes.Clear();
            foreach (var id in retiredOwners)
            {
                releasedNodes.UnionWith(retentionOwners[id].Ids);
                retentionOwners.Remove(id);
                retentionChildren.Remove(id);
            }
            foreach (var owner in retentionOwners.Values)
                releasedNodes.ExceptWith(owner.Ids);
            for (var i = 0; i < Count; i++)
                releasedNodes.Remove(Tree[i].Id);
            foreach (var id in releasedNodes)
            {
                Edits.Remove(id);
                Scrolls.Remove(id);
                nodeStates.Remove(id);
                animations.Remove(id);
                if (focus == id)
                    focus = 0;
            }
            retentionChildrenPass = -1;
        }
        if (returningFocus != 0 && (focus == 0 || !FocusIsLive()))
            focus = returningFocus;
        returningFocus = 0;
    }

    bool FocusIsLive()
    {
        for (var i = 0; i < Count; i++)
            if (Tree[i].Id == focus)
                return Tree[i].Interactive && !Tree[i].LayoutHidden;
        return false;
    }
}
