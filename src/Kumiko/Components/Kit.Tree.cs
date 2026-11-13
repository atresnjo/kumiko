using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>
    /// A virtualized tree. The model owns <paramref name="expanded"/> and <paramref name="selected"/>; selection
    /// follows keyboard focus. Up/Down/Home/End move, Right expands or enters a branch, Left collapses or goes to
    /// the parent, Enter or a double click activates. Only visible rows are built. Returns true when it reported.
    /// </summary>
    public bool Tree(
        string name,
        IReadOnlyList<TreeNode> roots,
        IReadOnlySet<string> expanded,
        string? selected,
        Action<string, bool> expandedChanged,
        Action<string> selectedChanged,
        TreeProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var rows = new List<TreeRow>();
        Flatten(roots, expanded, 0, -1, rows);
        var rowHeight = t.Metrics.HeightMedium;
        var height = props.Height ?? 320;
        using var tree = Box(key, source, line).FillWidth().Height(height).Semantics(Role.Tree, name);
        var state = context.NodeState<TreeState>(tree.Id);
        var current = rows.FindIndex(r => r.Node.Id == selected);
        var reported = false;
        using (var viewport = context.Div(key: "viewport").SizeFull().Scroll().Clip())
        {
            if (state.Reveal && current >= 0)
                RevealRow(viewport.Id, current * rowHeight, rowHeight, viewport.PreviousRect?.Height ?? height);
            state.Reveal = false;
            var offset = context.Scrolls.TryGetValue(viewport.Id, out var scroll) ? scroll.Offset : 0;
            var first = Math.Max(0, (int)(offset / rowHeight) - 2);
            var last = Math.Min(rows.Count, (int)Math.Ceiling((offset + height) / rowHeight) + 2);
            using var content = context.Div(key: "content").FillWidth().Height(rows.Count * rowHeight);
            for (var i = first; i < last; i++)
            {
                var action = TreeItem(rows[i], i, rowHeight, expanded, selected, current, state, expandedChanged, selectedChanged, props.RenderItem);
                if (action == TreeAction.None)
                    continue;
                reported = true;
                var node = rows[i].Node;
                switch (action)
                {
                    case TreeAction.Toggle:
                        expandedChanged(node.Id, !expanded.Contains(node.Id));
                        break;
                    case TreeAction.Activate:
                        selectedChanged(node.Id);
                        if (node.HasChildren)
                            expandedChanged(node.Id, !expanded.Contains(node.Id));
                        else
                            props.Activated?.Invoke(node.Id);
                        break;
                    case TreeAction.Select:
                        selectedChanged(node.Id);
                        break;
                    case TreeAction.Key:
                        reported = TreeKey(rows, i, expanded, expandedChanged, selectedChanged, props, state);
                        break;
                }
            }
        }
        return reported;
    }

    TreeAction TreeItem(TreeRow row, int index, float height, IReadOnlySet<string> expanded, string? selected, int current, TreeState state,
        Action<string, bool> expandedChanged, Action<string> selectedChanged, Action<Context, TreeNode, TreeItemState>? renderItem)
    {
        var t = Theme;
        var node = row.Node;
        var open = node.HasChildren && expanded.Contains(node.Id);
        var isSelected = node.Id == selected;
        var indent = 8 + row.Depth * 16;
        using var item = context.Div(key: node.Id)
            .Absolute()
            .Offset(0, index * height)
            .FillWidth()
            .Height(height)
            .Row()
            .ItemsCenter()
            .Gap(4)
            .Padding(new Edges(0, 8, 0, indent))
            .Rounded(t.Radius)
            .TabStop(isSelected || current < 0 && index == 0)
            .Semantics(Role.TreeItem, node.Label)
            .Accessibility(new(Selected: isSelected, Expanded: node.HasChildren ? open : null)
            {
                Level = row.Depth + 1
            }, request =>
            {
                switch (request.Action)
                {
                    case AccessibilityAction.Expand when node.HasChildren && !open:
                    case AccessibilityAction.Collapse when open:
                        expandedChanged(node.Id, !open);
                        return true;
                    case AccessibilityAction.Select:
                    case AccessibilityAction.Invoke:
                        selectedChanged(node.Id);
                        return true;
                    default:
                        return false;
                }
            });
        if (state.Focus == node.Id)
        {
            context.FocusNextControl();
            state.Focus = null;
        }
        var hit = item.Control(!node.Disabled);
        if (isSelected)
            item.Background(t.Accent);
        else if (hit.Hovered && !node.Disabled)
            item.Background(t.Muted);
        Ring(item, hit, t);
        if (node.Disabled)
            item.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        if (node.HasChildren)
            context.Icon(open ? "chevron.down" : "chevron.right", 14, t.MutedForeground, key: "chevron");
        else
        {
            using (context.Div(key: "leaf").Width(14).Height(14))
            {
            }
        }
        if (renderItem is not null)
            context.RenderContent(renderItem, node, new(isSelected, hit.Hovered && !node.Disabled, !node.Disabled && (hit.Hovered || hit.Focused),
                hit.Focused, node.Disabled, isSelected ? t.AccentForeground : t.Foreground, open, row.Depth, node.HasChildren));
        else
        {
            if (node.Icon is { } icon)
                context.Icon(icon, 16, t.MutedForeground, key: "icon");
            using (context.Div(key: "label").Flex().Clip())
                context.Text(node.Label, Text(t.FontSize, isSelected ? t.AccentForeground : t.Foreground), key: "text");
        }
        if (hit.Focused && context.Input.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End)
            return TreeAction.Key;
        if (!hit.Clicked)
            return TreeAction.None;
        if (hit.DoubleClicked || context.Input.Key is Key.Enter)
            return TreeAction.Activate;
        var onChevron = node.HasChildren && hit.Rect is { } rect && context.Input.Pointer.X < rect.X + indent + 18 && context.Input.Key == Key.None;
        return onChevron ? TreeAction.Toggle : TreeAction.Select;
    }

    bool TreeKey(List<TreeRow> rows, int index, IReadOnlySet<string> expanded, Action<string, bool> expandedChanged,
        Action<string> selectedChanged, TreeProps props, TreeState state)
    {
        var row = rows[index];
        var open = row.Node.HasChildren && expanded.Contains(row.Node.Id);
        var key = context.Logical(context.Input.Key);
        context.Input.Key = Key.None;
        context.Changed = true;
        int target = key switch
        {
            Key.Up => NextEnabled(rows, index, -1),
            Key.Down => NextEnabled(rows, index, 1),
            Key.Home => NextEnabled(rows, -1, 1),
            Key.End => NextEnabled(rows, rows.Count, -1),
            Key.Right when open => index + 1 < rows.Count && rows[index + 1].Parent == index ? index + 1 : -1,
            Key.Left when !open => row.Parent,
            _ => -1
        };
        if (key == Key.Right && row.Node.HasChildren && !open || key == Key.Left && open)
        {
            expandedChanged(row.Node.Id, key == Key.Right);
            return true;
        }
        if (target < 0 || target >= rows.Count || target == index)
            return false;
        state.Focus = rows[target].Node.Id;
        state.Reveal = true;
        selectedChanged(rows[target].Node.Id);
        return true;
    }

    static int NextEnabled(List<TreeRow> rows, int from, int direction)
    {
        for (var i = from + direction; i >= 0 && i < rows.Count; i += direction)
            if (!rows[i].Node.Disabled)
                return i;
        return -1;
    }

    void RevealRow(ulong scroller, float y, float rowHeight, float viewport)
    {
        if (!context.Scrolls.TryGetValue(scroller, out var scroll))
            context.Scrolls[scroller] = scroll = new();
        if (y < scroll.Offset)
            scroll.Offset = y;
        else if (y + rowHeight > scroll.Offset + viewport)
            scroll.Offset = y + rowHeight - viewport;
        scroll.Follow = false;
    }

    static void Flatten(IReadOnlyList<TreeNode> nodes, IReadOnlySet<string> expanded, int depth, int parent, List<TreeRow> rows)
    {
        foreach (var node in nodes)
        {
            var index = rows.Count;
            rows.Add(new(node, depth, parent));
            if (node.HasChildren && expanded.Contains(node.Id))
                Flatten(node.Children, expanded, depth + 1, index, rows);
        }
    }

    readonly record struct TreeRow(TreeNode Node, int Depth, int Parent);

    enum TreeAction
    {
        None,
        Select,
        Toggle,
        Activate,
        Key
    }

    sealed class TreeState
    {
        public string? Focus;
        public bool Reveal;
    }
}
