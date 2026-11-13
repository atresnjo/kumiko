using System.Globalization;
using System.Text;

namespace Kumiko.UI;
/// <summary>Stable sibling identity. Use a domain ID rather than a list position.</summary>
public readonly record struct NodeKey(string? Value)
{
    public static implicit operator NodeKey(string value) => new(value);
    public static implicit operator NodeKey(ulong value) => new(value.ToString(CultureInfo.InvariantCulture));
    public static implicit operator NodeKey(int value) => new(value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Semantic roles shared by testing and accessibility providers.</summary>
public enum Role
{
    None,
    Group,
    Button,
    Checkbox,
    Switch,
    Slider,
    TextField,
    Text,
    Image,
    List,
    ListItem,
    Heading,
    Grid,
    Row,
    Cell,
    ColumnHeader,
    ComboBox,
    Option,
    Menu,
    MenuItem,
    Alert,
    Tree,
    TreeItem,
    Tooltip,
    /// <summary>A movable divider between panes; exposes its position as a range.</summary>
    Separator,
    Dialog,
    Form,
    ScrollArea
}

/// <summary>Named motion durations. None changes immediately; ReduceMotion overrides all presets.</summary>
public enum Motion
{
    None,
    Quick,
    Standard,
    Gentle,
    Slow
}

/// <summary>Independent animation channels owned by a node.</summary>
public enum MotionChannel
{
    Value,
    Width,
    Height,
    OffsetX,
    OffsetY,
    Opacity
}

/// <summary>An immutable, post-layout accessibility snapshot in logical window coordinates.</summary>
public sealed record AccessibleNode(
    ulong Id,
    ulong Parent,
    Role Role,
    string Name,
    string? Value,
    Rect Rect,
    bool Enabled,
    bool Focused,
    bool Checked,
    bool Offscreen,
    AccessibleProperties? Properties = null
)
{
    public AccessibilityActions Actions
    {
        get; init;
    }
    public bool Inert
    {
        get; init;
    }
    public bool Hidden
    {
        get; init;
    }
    public string? Key
    {
        get; init;
    }
    public bool Supports(AccessibilityAction action) => (Actions & (AccessibilityActions)(1 << (int)action)) != 0;
}
public sealed partial class Context
{
    readonly List<AccessibleNode> accessibility = new();
    readonly List<AccessibleNode> semanticTree = new();
    readonly Dictionary<ulong, AccessibleNode> previousAccessible = new();
    (bool Hidden, bool InModal, ulong Parent)[] semanticState = [];
    /// <summary>Semantic tree for platform providers and offscreen inspection. Read on the UI thread.</summary>
    public IReadOnlyList<AccessibleNode> Accessibility => ReadUI<IReadOnlyList<AccessibleNode>>(semanticTree);
    public long Revision => ReadUI(frame);

    internal float AnimateNode(
        ulong id,
        MotionChannel channel,
        float target,
        Motion motion
    ) => AnimateCore(Mix(id, 0xa11ce000UL + (ulong)channel), target, Seconds(motion));

    internal static float Seconds(Motion motion) => motion switch
    {
        Motion.None => 0,
        Motion.Quick => .1f,
        Motion.Gentle => .3f,
        Motion.Slow => .75f,
        _ => .16f
    };
    internal string PathOf(int index)
    {
        var parts = new Stack<string>();
        for (var i = index; i >= 0; i = Tree[i].Parent)
        {
            var n = Tree[i];
            parts.Push(n.Key is not null ? $"[{Quote(n.Key)}]" : $"{(n.Kind == ContentKind.None ? "Div" : n.Kind.ToString())}#{i - Math.Max(0, n.Parent)}");
            if (n.CompositionSegment is { Length: > 0 } segment)
                parts.Push(segment.TrimEnd('/'));
        }

        return "/" + string.Join('/', parts);
    }

    internal static string Quote(string? value)
    {
        if (value is null)
            return "null";
        var result = new StringBuilder("\"");
        foreach (var ch in value)
            result.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture),
                _ => ch.ToString()
            });
        return result.Append('"').ToString();
    }

    void UpdateSemantics()
    {
        previousAccessible.Clear();
        foreach (var node in accessibility)
            previousAccessible[node.Id] = node;
        accessibility.Clear();
        semanticTree.Clear();
        if (semanticState.Length < Count)
            Array.Resize(ref semanticState, Math.Max(Count, semanticState.Length * 2));
        for (var i = 0; i < Count; i++)
        {
            ref var n = ref Tree[i];
            var hidden = n.NativeCovered || n.LayoutHidden || n.Style.Opacity <= 0 || n.Parent >= 0 && semanticState[n.Parent].Hidden;
            var inModal = n.Id == modalRoot || debuggerNodes.Contains(n.Id) || n.Parent >= 0 && semanticState[n.Parent].InModal;
            if (n.Role == Role.None)
                n.Role = n.Kind switch
                {
                    ContentKind.Text => Role.Text,
                    ContentKind.Image or ContentKind.Icon => Role.Image,
                    _ => n.Style.Scroll ? Role.ScrollArea : Role.Group
                };
            if (n.Style.Scroll && n.Role == Role.Group)
                n.Role = Role.ScrollArea;
            var name = n.Name ?? (n.Role is Role.Text or Role.Heading ? n.Text ?? "" : ControlName(i));
            var inert = modalRoot != 0 && !inModal;
            var properties = n.Accessible;
            if (n.Style.Scroll && Scrolls.TryGetValue(n.Id, out var scroll))
                properties = (properties ?? new()) with
                {
                    Minimum = 0,
                    Maximum = scroll.Max,
                    Number = scroll.Offset,
                    Step = 40,
                    Horizontal = scroll.Horizontal
                };
            // Keep diagnostics indexed by layout node, sharing each immutable record
            // with the compact tree instead of allocating a second snapshot.
            var parent = n.Parent < 0 ? 0 : semanticState[n.Parent].Parent;
            var meaningful = n.Role != Role.Group || name.Length > 0 || n.Accessible is not null;
            if (n.Role == Role.Image && name.Length == 0)
                meaningful = false;
            semanticState[i] = (hidden, inModal, meaningful ? n.Id : parent);
            var bounds = n.Rect.Intersect(n.ClipRect);
            var enabled = !n.Disabled && !inert;
            var offscreen = hidden || bounds is not { Width: > 0, Height: > 0 };
            var actions = SupportedActions(i);
            // An unchanged node keeps last frame's record, so a scrolling frame allocates only for what moved.
            if (previousAccessible.TryGetValue(n.Id, out var previous) && previous.Parent == parent && previous.Role == n.Role &&
                previous.Name == name && previous.Value == n.Value && previous.Rect == bounds && previous.Enabled == enabled &&
                previous.Focused == (n.Id == focus) && previous.Checked == n.Checked && previous.Offscreen == offscreen &&
                Equals(previous.Properties, properties) && previous.Actions == actions && previous.Inert == inert &&
                previous.Hidden == hidden && previous.Key == n.Key)
                accessibility.Add(previous);
            else
                accessibility.Add(new(n.Id, parent, n.Role, name, n.Value, bounds, enabled, n.Id == focus, n.Checked, offscreen, properties)
                {
                    Actions = actions,
                    Inert = inert,
                    Hidden = hidden,
                    Key = n.Key
                });
            if (meaningful && !n.LayoutHidden)
                semanticTree.Add(accessibility[^1]);
        }
    }

    string ControlName(int index)
    {
        if (!Tree[index].Style.Control)
            return "";
        var labels = new List<string>();
        void Visit(int i)
        {
            if (Tree[i].LayoutHidden || Tree[i].Style.Control)
                return;
            if (Tree[i].Kind == ContentKind.Text && Tree[i].Text is { Length: > 0 } text)
                labels.Add(text);
            for (var ch = Tree[i].FirstChild; ch >= 0; ch = Tree[ch].Next)
                Visit(ch);
        }

        for (var ch = Tree[index].FirstChild; ch >= 0; ch = Tree[ch].Next)
            Visit(ch);
        return string.Join(' ', labels);
    }

    /// <summary>Invokes an enabled control through Kumiko input. Does not synthesize system input.</summary>
    public bool Activate(ulong id)
    {
        return PerformAccessibilityAction(id, new(AccessibilityAction.Invoke));
    }
}
