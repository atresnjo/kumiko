using Facebook.Yoga;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;

namespace Kumiko.UI;

internal static partial class Layout
{
    static void ResolveBoxMetrics(Context c)
    {
        for (var i = 0; i < c.Count; i++)
        {
            ref var node = ref c.Tree[i];
            var layout = c.LayoutWorkspace[i].GetLayout();
            node.LayoutMargin = new(layout.Margin(PhysicalEdge.Top), layout.Margin(PhysicalEdge.Right),
                layout.Margin(PhysicalEdge.Bottom), layout.Margin(PhysicalEdge.Left));
            node.LayoutPadding = new(
                layout.Padding(PhysicalEdge.Top) + layout.Border(PhysicalEdge.Top),
                layout.Padding(PhysicalEdge.Right) + layout.Border(PhysicalEdge.Right),
                layout.Padding(PhysicalEdge.Bottom) + layout.Border(PhysicalEdge.Bottom),
                layout.Padding(PhysicalEdge.Left) + layout.Border(PhysicalEdge.Left));
        }
    }

    static bool ResolvePositionOverrides(Context c)
    {
        var changed = false;
        for (var i = 1; i < c.Count; i++)
        {
            ref readonly var node = ref c.Tree[i];
            if (node.LayoutHidden || node.Style.Layout.Display == DisplayMode.Contents)
                continue;
            ref readonly var s = ref node.Style;
            var owner = c.LayoutWorkspace[node.LayoutParent];
            var yoga = c.LayoutWorkspace[i];
            if (s.Absolute && s.Layout.Position is null && s.Layout.Insets is null)
            {
                changed |= SetPosition(YGEdge.Left, YGNodeLayoutGetPadding(owner, YGEdge.Left));
                changed |= SetPosition(YGEdge.Top, YGNodeLayoutGetPadding(owner, YGEdge.Top));
                if (s.Width.Kind == LengthKind.Fill)
                    changed |= SetPosition(YGEdge.Right, YGNodeLayoutGetPadding(owner, YGEdge.Right));
                if (s.Height.Kind == LengthKind.Fill)
                    changed |= SetPosition(YGEdge.Bottom, YGNodeLayoutGetPadding(owner, YGEdge.Bottom));
            }
            else if (!s.Absolute && s.Layout.Insets is null && s.Layout.Position != PositionMode.Static)
            {
                ref readonly var parent = ref c.Tree[node.LayoutParent].Style;
                var row = parent.Direction == Direction.Row;
                var cross = row ? s.Height : s.Width;
                var align = s.AlignSelf ?? parent.AlignItems;
                if ((parent.Wrap != FlexWrap.NoWrap || row && parent.AlignItems == Align.Baseline) &&
                    align == Align.Start && cross.Kind != LengthKind.Fill && !HasAutoMargin(s.Layout.Margin, row))
                    changed |= SetPosition(row ? YGEdge.Top : YGEdge.Left, row ? node.LayoutMargin.Top : node.LayoutMargin.Left);
            }

            bool SetPosition(YGEdge edge, float value)
            {
                var previous = YGNodeStyleGetPosition(yoga, edge);
                if (previous.Unit == Unit.Point && Math.Abs(previous.Value - value) < .001f)
                    return false;
                YGNodeStyleSetPosition(yoga, edge, value);
                return true;
            }
        }
        return changed;
    }

    static bool HasAutoMargin(LengthEdges? margin, bool row) => margin is { } e && (row
        ? e.Top?.Kind == LengthKind.Auto || e.Bottom?.Kind == LengthKind.Auto
        : e.Right?.Kind == LengthKind.Auto || e.Left?.Kind == LengthKind.Auto ||
          e.Start?.Kind == LengthKind.Auto || e.End?.Kind == LengthKind.Auto);
}
