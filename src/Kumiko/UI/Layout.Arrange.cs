using Facebook.Yoga;
using static Facebook.Yoga.YGNodeLayoutAPI;

namespace Kumiko.UI;

internal static partial class Layout
{
    static void Export(Context c, int index, Point origin)
    {
        ref var node = ref c.Tree[index];
        var yoga = c.LayoutWorkspace[index];
        ref readonly var s = ref node.Style;
        if (node.LayoutHidden)
        {
            node.Measured = default;
            node.Rect = default;
            for (var child = node.FirstChild; child >= 0; child = c.Tree[child].Next)
                Export(c, child, default);
            return;
        }
        if (s.Layout.Display == DisplayMode.Contents)
        {
            var owner = c.LayoutWorkspace[node.LayoutParent];
            node.Rect = new(origin.X, origin.Y, YGNodeLayoutGetWidth(owner), YGNodeLayoutGetHeight(owner));
            node.Measured = default;
            for (var child = node.FirstChild; child >= 0; child = c.Tree[child].Next)
                Export(c, child, origin);
            return;
        }
        var layout = yoga.GetLayout();
        node.Measured = new(layout.Dimension(Dimension.Width), layout.Dimension(Dimension.Height));
        var alignment = FillAlignment(c, index);
        node.Rect = new(origin.X + layout.Position(PhysicalEdge.Left) + s.Offset.X + alignment.X,
            origin.Y + layout.Position(PhysicalEdge.Top) + s.Offset.Y + alignment.Y, node.Measured.Width, node.Measured.Height);
        var scroll = s.Scroll ? ScrollOffset(c, index, s, node.Rect.Inset(node.LayoutPadding), ContentExtent(c, index)) : default;
        var childOrigin = new Point(node.Rect.X - scroll.X, node.Rect.Y - scroll.Y);
        for (var child = node.FirstChild; child >= 0; child = c.Tree[child].Next)
            Export(c, child, childOrigin);
    }

    // The scrollable content box: in-flow layout children, including those lifted out of display:contents nodes.
    static Size ContentExtent(Context c, int index)
    {
        ref readonly var node = ref c.Tree[index];
        var yoga = c.LayoutWorkspace[index];
        float width = 0;
        float height = 0;
        foreach (var childYoga in yoga.GetChildren())
        {
            var child = ((Workspace.Entry)childYoga.GetContext()!).Index;
            ref readonly var childNode = ref c.Tree[child];
            if (childNode.Style.Absolute || childNode.LayoutHidden)
                continue;
            var childLayout = childYoga.GetLayout();
            var childAlignment = FillAlignment(c, child);
            width = Math.Max(width, childLayout.Position(PhysicalEdge.Left) + childAlignment.X - node.LayoutPadding.Left +
                childLayout.Dimension(Dimension.Width) + childNode.LayoutMargin.Right);
            height = Math.Max(height, childLayout.Position(PhysicalEdge.Top) + childAlignment.Y - node.LayoutPadding.Top +
                childLayout.Dimension(Dimension.Height) + childNode.LayoutMargin.Bottom);
        }
        return new(width, height);
    }

    static Point FillAlignment(Context c, int index)
    {
        ref readonly var node = ref c.Tree[index];
        if (node.LayoutParent < 0 || node.Style.Absolute)
            return default;
        ref readonly var parent = ref c.Tree[node.LayoutParent].Style;
        var row = parent.Direction == Direction.Row;
        if (parent.Wrap != FlexWrap.NoWrap || HasAutoMargin(node.Style.Layout.Margin, row))
            return default;
        ref readonly var s = ref node.Style;
        if ((row ? s.Height : s.Width).Kind != LengthKind.Fill)
            return default;
        var align = s.AlignSelf ?? parent.AlignItems;
        if (align is not (Align.Center or Align.End))
            return default;
        var owner = c.LayoutWorkspace[node.LayoutParent];
        var yoga = c.LayoutWorkspace[index];
        // Yoga stretch starts at the leading edge after a max constraint clamps it.
        // Kumiko Fill retains the authored alignment within that remaining space.
        var free = row
            ? YGNodeLayoutGetHeight(owner) - c.Tree[node.LayoutParent].LayoutPadding.Vertical - node.LayoutMargin.Vertical - YGNodeLayoutGetHeight(yoga)
            : YGNodeLayoutGetWidth(owner) - c.Tree[node.LayoutParent].LayoutPadding.Horizontal - node.LayoutMargin.Horizontal - YGNodeLayoutGetWidth(yoga);
        var offset = align == Align.Center ? free / 2 : free;
        return row ? new(0, offset) : new(offset, 0);
    }

    static Point ScrollOffset(Context c, int index, in Style s, Rect inner, Size content)
    {
        var id = c.Tree[index].Id;
        if (!c.Scrolls.TryGetValue(id, out var state))
            c.Scrolls[id] = state = new();
        state.Horizontal = s.ScrollHorizontal;
        state.Max = Math.Max(0, s.ScrollHorizontal ? content.Width - inner.Width : content.Height - inner.Height);
        if (s.FollowEnd && state.Follow)
            state.Offset = state.Max;
        state.Offset = Math.Clamp(state.Offset, 0, state.Max);
        return s.ScrollHorizontal ? new(state.Offset, 0) : new(0, state.Offset);
    }
}
