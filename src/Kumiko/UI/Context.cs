using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kumiko.Rendering;

namespace Kumiko.UI;
/// <summary>The payload painted by a node; layout containers have no payload.</summary>
public enum ContentKind
{
    None,
    Text,
    Image,
    Icon
}

/// <summary>
/// A post-build layout node. Identity is local to its keyed parent; geometry uses logical pixels.
/// </summary>
public struct Node
{
    public ulong Id;
    public string? Key, Source;
    internal ulong IdentityParent;
    internal string? CompositionPath, CompositionSegment;
    public int SourceLine;
    public Role Role;
    public string? Name, Value;
    public bool Disabled, Checked, MissingRowKey;
    public AccessibleProperties? Accessible;
    internal Func<AccessibilityRequest, bool>? AccessibilityHandler;
    internal bool HasInteraction;
    internal Interaction CachedInteraction;
    public int Parent, FirstChild, LastChild, Next, ChildCount;
    public Style Style;
    public ContentKind Kind;
    public string? Text;
    public TextStyle TextStyle;
    public TextSpan[]? Spans;
    public Image? Image;
    public bool Interactive, Flexible;
    public Size Measured;
    internal int LayoutParent;
    internal bool LayoutHidden;
    internal bool NativeCovered;
    internal Edges LayoutPadding, LayoutMargin;
    public Rect Rect, ClipRect;
    internal Rect? TextCaret;
    internal bool IsOverlay;
    internal Transition? EnterFrom, ExitTo;
    internal float Rotation, Scale;
    internal DragPayload? Draggable;
    internal DragStyle DragLook;
    internal string? SharedLayout;
    internal Motion SharedMotion;
    /// <summary>For an overlay, the scope that was open when it began; focus inside belongs to that scope too.</summary>
    internal int Opener;
    /// <summary>Effective reading direction after layout.</summary>
    internal bool RightToLeft;
    internal ButtonOptions? NativeButton;
    internal object? NativeContent;
}

internal readonly record struct Hit(ulong Id, ulong Parent, Rect Rect, bool Interactive, bool Scroll, bool TabStop = true);
internal sealed class ScrollState
{
    public float Offset, Max;
    /// <summary>A virtual list header's height from the latest layout; its rows start there.</summary>
    public float Header;
    /// <summary>The scroller's bounds and direction from the latest paint, where its bar is drawn.</summary>
    public Rect Track;
    public bool RightToLeft;
    public bool Follow = true, Horizontal;
    /// <summary>An eased scroll in flight (see Scrolling.cs); NaN when the offset is at rest.</summary>
    public float Target = float.NaN, From, Elapsed, Duration;
    public bool Symmetric;
}

/// <summary>
/// Builds an immediate-mode tree on its owning UI thread. Use using scopes, stable keys in loops, and
/// App.Dispatch for worker results.
/// </summary>
public sealed partial class Context : IDisposable
{
    internal readonly Layout.Workspace LayoutWorkspace = new();
    internal Node[] Tree = new Node[256];
    internal int Count;
    readonly List<int> stack = new(32);
    readonly Dictionary<ulong, Rect> rects = new();
    readonly List<Hit> hits = new(128), nextHits = new(128);
    readonly HashSet<ulong> hotPath = new();
    readonly HashSet<ulong> liveIds = new();
    readonly Dictionary<ulong, (float Value, float Start, long Frame)> animations = new();
    internal readonly Dictionary<ulong, ScrollState> Scrolls = new();
    readonly List<(int Index, ScrollState State)> listHeaders = new();
    internal readonly Dictionary<ulong, EditState> Edits = new();
    List<Instance> instances = new(512);
    ulong hot, active, focus;
    NodeKey pendingKey;
    long frame;
    bool disposed, focusNext;
    readonly int ownerThread = Environment.CurrentManagedThreadId;
    Theme theme = Theme.Light;
    /// <summary>Creates an offscreen UI context owned by the calling thread.</summary>
    public Context()
    {
        Runtime.KumikoExecution.CheckUI();
        textSystem = new();
    }

    /// <summary>Text measurement and rendering resources owned by this context.</summary>
    internal ref Node At(int index)
    {
        CheckThread();
        return ref Tree[index];
    }

    internal Rect? PreviousRect(int index) => rects.TryGetValue(Tree[index].Id, out var rect) ? rect : null;
    /// <summary>The painted bounds of a node in the last completed frame.</summary>
    internal Rect? RectOf(ulong id) => rects.TryGetValue(id, out var rect) ? rect : null;
    /// <summary>Compatibility API. Prefer the element's key parameter so identity cannot leak.</summary>
    [Obsolete("Pass key: to the element instead of calling Context.Key.")]
    public void Key(string value) => pendingKey = value;
    /// <summary>Compatibility API. Prefer the element's key parameter.</summary>
    [Obsolete("Pass key: to the element instead of calling Context.Key.")]
    public void Key(ulong value) => pendingKey = value;
    internal void CheckThread()
    {
        Runtime.KumikoExecution.CheckUI();
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("UI calls must run on the owning thread. Use App.Dispatch.");
    }

    internal static ulong Hash(string value)
    {
        ulong h = 14695981039346656037;
        foreach (var ch in value)
        {
            h ^= ch;
            h *= 1099511628211;
        }

        return h == 0 ? 1 : h;
    }

    static ulong Mix(ulong a, ulong b)
    {
        var x = a ^ (b + 0x9e3779b97f4a7c15 + (a << 6) + (a >> 2));
        return x == 0 ? 1 : x;
    }

    internal ulong NextNodeId(NodeKey key)
    {
        var parent = stack.Count > 0 ? stack[^1] : -1;
        var ordinal = parent >= 0 ? Tree[parent].ChildCount : Count;
        if (composition is { } owner)
            ordinal = owner.Ordinals.GetValueOrDefault(parent);
        var value = key.Value ?? pendingKey.Value;
        return Mix(IdentityParent(), value is not null ? Hash(value) : (ulong)ordinal + 1);
    }

    internal int Add(
        Style style,
        ContentKind kind = ContentKind.None,
        string? text = null,
        TextStyle textStyle = default,
        Image? image = null,
        NodeKey key = default,
        string? source = null,
        int line = 0
    )
    {
        CheckThread();
        if (key.Value is null)
            key = pendingKey;
        var id = NextNodeId(key);
        pendingKey = default;
        if (Count == Tree.Length)
            Array.Resize(ref Tree, Tree.Length * 2);
        int parent = stack.Count > 0 ? stack[^1] : -1;
        int index = Count++;
        OwnRetainedNode(id);
        Tree[index] = new()
        {
            Id = id,
            IdentityParent = IdentityParent(),
            CompositionPath = composition?.Path,
            CompositionSegment = CompositionSegment(parent),
            Key = key.Value,
            Source = source,
            SourceLine = line,
            Parent = parent,
            FirstChild = -1,
            LastChild = -1,
            Next = -1,
            Style = style,
            LayoutHidden = style.Layout.Display == DisplayMode.None || parent >= 0 && Tree[parent].LayoutHidden,
            Kind = kind,
            Text = text,
            TextStyle = textStyle,
            Image = image
        };
        if (composition is { } owner)
            owner.Ordinals[parent] = owner.Ordinals.GetValueOrDefault(parent) + 1;
        if (parent >= 0)
        {
            ref var p = ref Tree[parent];
            if (p.LastChild >= 0)
                Tree[p.LastChild].Next = index;
            else
                p.FirstChild = index;
            p.LastChild = index;
            p.ChildCount++;
        }

        return index;
    }

    internal void Push(int index) => stack.Add(index);
    internal void End(int index)
    {
        if (stack.Count == 0 || stack[^1] != index)
            throw new InvalidOperationException($"Div scope {PathOf(index)} must end in reverse order; open scope: {(stack.Count > 0 ? PathOf(stack[^1]) : "none")}.");
        stack.RemoveAt(stack.Count - 1);
        if (overlayStacks.Remove(index, out var saved))
        {
            stack.Clear();
            stack.AddRange(saved);
        }
    }

    /// <summary>Begins a layout scope. Always dispose with using; give list items stable keys.</summary>
    public Div Div(NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        int index = Add(new Style(), key: key, source: source, line: line);
        Push(index);
        return new(this, index);
    }

    internal Interaction Interact(int index, bool enabled = true)
    {
        CheckThread();
        if (contentRenderDepth > 0)
            throw new InvalidOperationException("A Kit content renderer cannot create controls; the component owns interaction and focus.");
        ref var n = ref Tree[index];
        if (n.HasInteraction)
            return n.CachedInteraction;
        enabled &= !n.NativeCovered && !n.LayoutHidden && n.Style.Layout.Display != DisplayMode.Contents;
        n.HasInteraction = true;
        n.Interactive = enabled;
        n.Disabled = !enabled;
        n.Style.Control = true;
        if (n.Role == Role.None)
            n.Role = Role.Button;
        var id = n.Id;
        var hovered = hotPath.Contains(id);
        if (focusNext && enabled)
        {
            focus = id;
            focusNext = false;
        }

        var invoked = enabled && accessibleActivation == id;
        if (invoked)
            accessibleActivation = 0;
        var clicked = enabled && (invoked || (Input.Released && Input.Button == PointerButton.Primary && active == id && hot == id && !dragged) || (focus == id && Input.Key is UI.Key.Enter or UI.Key.Space));
        if (clicked)
            Changed = true;
        return n.CachedInteraction = new(
            id,
            rects.TryGetValue(id, out var r) ? r : null,
            hovered,
            enabled && active == id && Input.Down,
            clicked,
            focus == id,
            clicked && Input.ClickCount == 2
        );
    }

    internal void FocusNode(ulong id) => focus = id;
    /// <summary>
    /// Where to open a context menu for <paramref name="control"/>: the pointer on a secondary press, or below the
    /// control's start edge when it is focused and Shift+F10 is pressed; otherwise null.
    /// </summary>
    public Point? MenuRequested(Interaction control)
    {
        CheckThread();
        if (control.Pressed && Input.Pressed && Input.Button == PointerButton.Secondary)
            return Input.Pointer;
        if (control.Focused && control.Rect is { } rect && Shortcut(UI.Key.F10, Modifiers.Shift))
            return new(RightToLeftWindow ? rect.Right : rect.X, rect.Bottom);
        return null;
    }

    /// <summary>Clears keyboard focus.</summary>
    public void Blur() => focus = 0;
    /// <summary>Moves focus to the next enabled control built in this pass.</summary>
    public void FocusNextControl() => focusNext = true;
    /// <summary>
    /// Compatibility animation by explicit ID. Prefer Div.Animate and named Motion presets for node-owned
    /// state.
    /// </summary>
    [Obsolete("Use Div.Animate or an animated layout property with a Motion preset.")]
    public float Animate(ulong id, float target, float duration = 0.16f) => AnimateCore(id, target, duration);

    float AnimateCore(ulong id, float target, float duration)
    {
        if (ReduceMotion || !animations.TryGetValue(id, out var a))
        {
            animations[id] = (target, target, frame);
            return target;
        }

        float start = a.Frame == frame ? a.Start : a.Value;
        var value = start + (target - start) * (1 - MathF.Exp(-6 * DeltaTime / Math.Max(0.001f, duration)));
        if (Math.Abs(value - target) < 0.001f)
            value = target;
        else
            Animating = true;
        animations[id] = (value, start, frame);
        return value;
    }

    /// <summary>Requests a future frame without polling while the application is idle.</summary>
    public void FrameIn(TimeSpan duration)
    {
        if (NextFrame is null || duration < NextFrame)
            NextFrame = duration;
    }

    /// <summary>
    /// Consumes a matching keyboard shortcut; Primary maps to Command on macOS and Control elsewhere.
    /// Escape belongs to the active modal layer (declare its Overlay first) and never dismisses during IME composition.
    /// </summary>
    public bool Shortcut(Key key, Modifiers modifiers = Modifiers.Primary)
    {
        static Modifiers Normalize(Modifiers value)
        {
            if ((value & Modifiers.Primary) != 0)
                value = (value & ~Modifiers.Primary) | (OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control);
            return value;
        }

        if (Input.Key != key || Normalize(Input.Modifiers) != Normalize(modifiers))
            return false;
        if (key == UI.Key.Escape && !EscapeAllowed)
            return false;
        Input.Key = UI.Key.None;
        Input.Character = '\0';
        return true;
    }

    /// <summary>
    /// Consumes a keystroke that produced <paramref name="character"/> with the active keyboard layout, such as
    /// 'j', 'G' or '?'. Shift and Option are part of the character; <paramref name="modifiers"/> lists the
    /// Command/Control keys that must be held (Primary maps as in <see cref="Shortcut(Key, Modifiers)"/>).
    /// Keystrokes typed into a focused text field never match.
    /// </summary>
    public bool Shortcut(char character, Modifiers modifiers = Modifiers.None)
    {
        const Modifiers chord = Modifiers.Meta | Modifiers.Control;
        static Modifiers Normalize(Modifiers value)
        {
            if ((value & Modifiers.Primary) != 0)
                value = (value & ~Modifiers.Primary) | (OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control);
            return value & chord;
        }

        if (character == '\0' || Input.Character != character || Normalize(Input.Modifiers) != Normalize(modifiers))
            return false;
        Input.Key = UI.Key.None;
        Input.Character = '\0';
        return true;
    }

    internal void Frame(Action<Context> view, float dt = 1 / 60f)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        DeltaTime = Math.Clamp(dt, 0, 1);
        Time += dt;
        frame++;
        Animating = false;
        NextFrame = null;
        TrackFocusSource();
        BeginAccessibilityFrame();
        ResolveInput();
        AdvanceScrolls();
        PrepareFormSubmit();
        TrackDrag();
        RememberFocusPath();
        inputModalRoot = modalRoot;
        keyPreviews.Clear();
        modalRoot = 0;
        TextSystem.Family = Theme.Font;
        TextSystem.MonoFamily = Theme.MonoFont;
        TextSystem.BeginFrame();
        double build = 0, layout = 0;
        int passes = 0;
        for (; passes < 4;)
        {
            var start = Stopwatch.GetTimestamp();
            DesktopMenuItems = null;
            DesktopMenuProjection = null;
            Count = 0;
            retentionPass++;
            retentionStack.Clear();
            composition = null;
            modalRoot = 0;
            navigationStacks.Clear();
            ExactNativeText = null;
            stack.Clear();
            reveals.Clear();
            listHeaders.Clear();
            pendingKey = default;
            Changed = false;
            WantsMoves = false;
            Cursor = Cursor.Arrow;
            var root = Add(new Style { Width = Length.Fill, Height = Length.Fill });
            Push(root);
            view(this);
            if (stack.Count != 1 || stack[0] != root)
                throw new InvalidOperationException($"A Div scope was left open: {PathOf(stack[^1])}.");
            End(root);
            AdaptNativeText();
            Layout.PrepareTree(this);
            ValidateNativeControls();
            build += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            Layout.Calculate(this);
            Layout.Mirror(this, 0, false, RightToLeftWindow);
            layout += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            passes++;
            if (SettleListHeaders() && passes < 4)
                Changed = true;
            if (!Changed)
                break;
            Input.Clear();
        }

        FinishRetention();
        ApplyReveals();
        ApplySharedLayouts();
        var paintStart = Stopwatch.GetTimestamp();
        // Last frame's paint stays readable so a node that stops being built can exit from its final image.
        (instances, previousInstances) = (previousInstances, instances);
        instances.Clear();
        painted.Clear();
        nextHits.Clear();
        rects.Clear();
        Paint.Visit(this, 0, new(0, 0, Size.Width, Size.Height), 1, 0, instances, nextHits, rects);
        ApplyTransitions();
        hits.Clear();
        hits.AddRange(nextHits);
        var paintMilliseconds = Stopwatch.GetElapsedTime(paintStart).TotalMilliseconds;
        if (focus != 0 && !hits.Any(h => h.Id == focus && h.Interactive))
            focus = 0;
        UpdateSemantics();
        PresentNativeControls();
        if (DevMode)
            InspectLayout();
        else
            warnings.Clear();
        Stats = new(build, layout, paintMilliseconds, passes, Count, instances.Count);
        FinishDrag();
        var retained = Instance.Retained(instances.Capacity, instances.Count);
        if (retained < instances.Capacity)
            instances.Capacity = retained;
        DesktopMenuItems = DesktopMenuProjection?.Invoke();
        navigationStacks.Clear();
        Input.Clear();
        accessibilitySource = null;
        accessibleActivation = 0;
        if (accessibleInvocations.Count > 0)
            FrameIn(TimeSpan.Zero);
        if (!Input.Down)
            active = 0;
        if (frame % 120 == 0)
            Prune();
    }

    void Prune()
    {
        foreach (var entry in animations)
            if (entry.Value.Frame < frame - 120)
                animations.Remove(entry.Key);
        if (Edits.Count > 0 || Scrolls.Count > 0 || CrossScrolls.Count > 0 || nodeStates.Count > 0)
        {
            liveIds.Clear();
            for (int i = 0; i < Count; i++)
                liveIds.Add(Tree[i].Id);
            foreach (var owner in retentionOwners.Values)
                liveIds.UnionWith(owner.Ids);
            foreach (var entry in Edits)
                if (!liveIds.Contains(entry.Key))
                    Edits.Remove(entry.Key);
            foreach (var entry in Scrolls)
                if (!liveIds.Contains(entry.Key))
                    Scrolls.Remove(entry.Key);
            foreach (var entry in CrossScrolls)
                if (!liveIds.Contains(entry.Key))
                    CrossScrolls.Remove(entry.Key);
            PruneNodeStates();
        }

        TextSystem.TrimCache();
    }

    /// <summary>Releases the context and its text/rendering resources.</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        navigationStacks.Clear();
        LayoutWorkspace.Clear();
        TextSystem.Dispose();
    }
}
