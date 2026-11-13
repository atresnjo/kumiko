using Kumiko.Rendering;

namespace Kumiko.UI;

// Experimental internal seam. Native controls consume intent and layout, never GPU instances.
internal readonly record struct NativeButtonContent(string Label, ButtonOptions Options);
internal readonly record struct NativeTextContent(string Text, TextStyle Style, bool Heading, bool RightToLeft);
internal readonly record struct NativeTextFieldContent(string Value, string Placeholder, string Name, bool Password);
internal readonly record struct NativeCheckboxContent(string Label, bool Checked);
/// <summary>A tinted icon. <paramref name="Svg"/> is the registered document, or null for a platform symbol name.</summary>
internal readonly record struct NativeIconContent(string Name, string? Svg, float Size, Color Color, string? AccessibleName);
/// <summary>
/// A styled container presented as a plain native view; its native children nest inside it. <paramref name="Image"/>
/// names a box with <see cref="Role.Image"/> semantics, which is announced as one image.
/// </summary>
internal readonly record struct NativeBoxContent(Color? Background, Color? Border, float BorderWidth, float Radius, bool Clip,
    string? Image = null);
/// <summary>A vertical scroll viewport; the platform scrolls its <paramref name="ContentHeight"/> of children.</summary>
internal readonly record struct NativeScrollContent(float ContentHeight);
internal readonly record struct NativeControlNode(ulong Id, string? Key, object Content,
    Rect Bounds, ulong Parent, bool Enabled, bool Inert, string? SharedLayout, ulong SharedOwner,
    Motion SharedMotion, Transition? EnterFrom, bool ReduceMotion);

internal interface IControlRenderer
{
    object TextMetricsKey
    {
        get;
    }
    Size MeasureButton(string label, ButtonOptions options);
    Size MeasureText(string text, TextStyle style, float width, object? content);
    float TextBaseline(string text, TextStyle style, object? content);
    void Present(IReadOnlyList<NativeControlNode> controls);
    /// <summary>The latest text the user entered in a native field since the last call, or null.</summary>
    string? TakeText(ulong id);
    /// <summary>The native field whose return key was pressed since the last call, or 0.</summary>
    ulong TakeSubmit();
}

public sealed partial class Context
{
    internal IControlRenderer? ControlRenderer
    {
        get; init;
    }

    /// <summary>
    /// Set by a native host's opt-in for one build pass: turns each plain text node (text, style with its color
    /// resolved, heading) into the host's exact text content, or null when the host cannot draw that style exactly.
    /// </summary>
    internal Func<string, TextStyle, bool, object?>? ExactNativeText;

    void AdaptNativeText()
    {
        if (ControlRenderer is null || ExactNativeText is not { } adapt)
            return;
        for (var i = 0; i < Count; i++)
        {
            ref var node = ref Tree[i];
            if (node.Kind != ContentKind.Text || node.NativeContent is not null || node.Spans is not null)
                continue;
            var style = node.TextStyle with
            {
                Color = node.TextStyle.Color ?? Theme.Text
            };
            node.NativeContent = adapt(node.Text ?? "", style, node.Role == Role.Heading) ??
                throw new NotSupportedException($"The native host cannot draw {style.Size} pt text exactly: {PathOf(i)}.");
        }
    }

    Interaction NativeButton(string label, ButtonOptions options, NodeKey key, string source, int line)
    {
        CheckThread();
        var measured = ControlRenderer!.MeasureButton(label, options);
        var index = Add(new Style
        {
            Width = options.Width ?? measured.Width,
            Height = measured.Height
        }, key: key, source: source, line: line);
        Tree[index].NativeButton = options;
        Tree[index].Role = Role.Button;
        Tree[index].Name = label;
        return Interact(index, !options.Disabled);
    }

    internal void CoverNativePage(ulong id)
    {
        var start = Array.FindIndex(Tree, 0, Count, node => node.Id == id);
        if (start < 0)
            return;
        for (var i = start; i < Count; i++)
            if (i == start || Within(Tree[i].Id, id))
            {
                Tree[i].NativeCovered = true;
                Tree[i].Interactive = false;
            }
    }

    void ValidateNativeControls()
    {
        if (ControlRenderer is null)
            return;
        var ids = new HashSet<ulong>();
        var shared = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Count; i++)
        {
            ref readonly var node = ref Tree[i];
            ref readonly var style = ref node.Style;
            if (node.LayoutHidden || style.Layout.Display == DisplayMode.Contents)
                continue;
            if (!ids.Add(node.Id))
                throw new InvalidOperationException($"Duplicate native control identity at {PathOf(i)}. Use unique keys.");
            if (node.SharedLayout is { } tag && !shared.Add(tag))
                throw new InvalidOperationException($"Duplicate native shared layout tag: {tag}.");
            // Plain boxes and vertical scroll areas become native views when they carry no other content.
            var box = node.NativeContent is null && node.NativeButton is null && node.Kind == ContentKind.None;
            if (node.Kind is not (ContentKind.None or ContentKind.Text or ContentKind.Icon) || node.Spans is not null ||
                !box && (style.Background is not null || style.Border is not null || style.Radius != 0 || style.Scroll) ||
                style.Shadow is not null || style.Glass is not null || style.Opacity != 1 || style.ScrollHorizontal ||
                !box && style.Clip && !style.Scroll || style.Selectable || node.IsOverlay && node.NativeContent is null ||
                node.ExitTo is not null || node.Rotation != 0 ||
                node.Scale is not (0 or 1) || node.Draggable is not null ||
                node.NativeButton is null && node.NativeContent is null && node.Kind is not (ContentKind.Text or ContentKind.Icon) &&
                (node.Interactive || node.Role is not (Role.None or Role.Group or Role.List or Role.ListItem or Role.Form or Role.ScrollArea) &&
                !(box && node.Role == Role.Image && node.Name is not null)))
                throw new NotSupportedException($"Unsupported content or styling in a native-controls view: {PathOf(i)}.");
        }
    }

    void PresentNativeControls()
    {
        if (ControlRenderer is null)
            return;
        var controls = new List<NativeControlNode>();
        var parents = new ulong[Count];
        var scrolled = new bool[Count];
        for (var i = 0; i < Count; i++)
        {
            ref readonly var node = ref Tree[i];
            var parent = node.Parent >= 0 ? parents[node.Parent] : 0;
            scrolled[i] = node.Parent >= 0 && (scrolled[node.Parent] || Tree[node.Parent].Style.Scroll || Tree[node.Parent].Style.Clip);
            parents[i] = parent;
            if (node.LayoutHidden || node.Style.Layout.Display == DisplayMode.Contents)
                continue;
            object? content = node.NativeContent;
            if (node.NativeButton is { } options)
                content = new NativeButtonContent(node.Name ?? "", options);
            else if (node.Kind == ContentKind.Text && content is null)
                content = new NativeTextContent(node.Text ?? "", node.TextStyle, node.Role == Role.Heading, node.RightToLeft);
            else if (node.Kind == ContentKind.Icon && content is null)
                content = new NativeIconContent(node.Text ?? "", SvgIcons.Document(node.Text ?? ""), node.TextStyle.Size,
                    node.TextStyle.Color ?? Theme.Text, node.Name);
            else if (content is null && node.Style.Scroll)
                content = new NativeScrollContent(ContentBottom(i) - node.Rect.Y);
            else if (content is null && (node.Style.Background is not null || node.Style.Border is not null || node.Style.Clip ||
                node.EnterFrom is not null || node.SharedLayout is not null || node.Role == Role.Image && node.Name is not null))
                content = new NativeBoxContent(node.Style.Background, node.Style.Border, node.Style.BorderWidth, node.Style.Radius, node.Style.Clip,
                    node.Role == Role.Image ? node.Name : null);
            parents[i] = node.NativeContent is not null || content is NativeScrollContent or NativeBoxContent ? node.Id : parent;
            if (content is null)
                continue;
            // The native scroll view clips its own content.
            if (!scrolled[i] && (node.Rect.X < node.ClipRect.X || node.Rect.Y < node.ClipRect.Y ||
                node.Rect.Right > node.ClipRect.Right || node.Rect.Bottom > node.ClipRect.Bottom))
                throw new NotSupportedException($"Native control clipping requires a native container adapter: {node.Rect} within {node.ClipRect}.");
            var inert = node.NativeCovered || modalRoot != 0 && !Within(node.Id, modalRoot) && !debuggerNodes.Contains(node.Id);
            controls.Add(new(node.Id, node.Key, content, node.Rect, parent, !node.Disabled, inert, node.SharedLayout,
                node.Parent >= 0 ? Tree[node.Parent].Id : 0, node.SharedMotion, node.EnterFrom, ReduceMotion));
        }
        ControlRenderer.Present(controls);
    }

    /// <summary>The lowest edge of a node's descendants, which are stored after it in build order.</summary>
    float ContentBottom(int index)
    {
        var bottom = Tree[index].Rect.Bottom;
        var last = index;
        while (Tree[last].LastChild >= 0)
            last = Tree[last].LastChild;
        for (var i = index + 1; i <= last; i++)
            if (!Tree[i].LayoutHidden && Tree[i].Style.Layout.Display != DisplayMode.Contents)
                bottom = Math.Max(bottom, Tree[i].Rect.Bottom + Tree[i].LayoutMargin.Bottom + Tree[index].LayoutPadding.Bottom);
        return bottom;
    }

    /// <summary>A model-controlled text field presented by the platform. Edits arrive through the renderer.</summary>
    bool NativeTextField(string value, Action<string> changed, TextFieldOptions options, NodeKey key, string source, int line)
    {
        var name = options.Name ?? options.Placeholder;
        var index = Add(new Style
        {
            Width = options.Width,
            Height = Math.Max(44, options.Height ?? 44)
        }, key: key, source: source, line: line);
        Tree[index].Role = Role.TextField;
        Tree[index].Name = name;
        Tree[index].Value = options.Password ? null : value;
        Tree[index].NativeContent = new NativeTextFieldContent(value, options.Placeholder, name, options.Password);
        var id = Tree[index].Id;
        Interact(index, !options.Disabled);
        if (ControlRenderer!.TakeText(id) is not { } text || text == value)
            return false;
        WithSource(id, () => changed(text));
        return true;
    }

    /// <summary>A checkbox presented by the platform; activation toggles it like a pointer press.</summary>
    bool NativeCheckbox(string label, ref bool value, NodeKey key, string source, int line)
    {
        var measured = ControlRenderer!.MeasureButton(label, default);
        var index = Add(new Style
        {
            Width = measured.Width + 32,
            Height = measured.Height
        }, key: key, source: source, line: line);
        Tree[index].Role = Role.Checkbox;
        Tree[index].Name = label;
        Tree[index].Checked = value;
        Tree[index].NativeContent = new NativeCheckboxContent(label, value);
        if (!Interact(index).Clicked)
            return false;
        value = !value;
        Changed = true;
        return true;
    }
}
