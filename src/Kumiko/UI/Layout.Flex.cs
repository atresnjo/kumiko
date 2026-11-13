using Facebook.Yoga;
using static Facebook.Yoga.YGNodeStyleAPI;
using YogaNode = Facebook.Yoga.Node;

namespace Kumiko.UI;

internal static partial class Layout
{
    static void ApplyStyle(Context c, int index, YogaNode yoga)
    {
        ref readonly var node = ref c.Tree[index];
        var s = node.Style;
        var parent = node.LayoutParent >= 0 ? c.Tree[node.LayoutParent].Style : new Style();
        var row = parent.Direction == Direction.Row;
        var main = row ? s.Width : s.Height;
        var cross = row ? s.Height : s.Width;
        YGNodeStyleSetFlexDirection(yoga, s.Direction == Direction.Row
            ? s.Reverse ? YGFlexDirection.RowReverse : YGFlexDirection.Row
            : s.Reverse ? YGFlexDirection.ColumnReverse : YGFlexDirection.Column);
        YGNodeStyleSetFlexWrap(yoga, s.Wrap switch
        {
            FlexWrap.Wrap => YGWrap.Wrap,
            FlexWrap.WrapReverse => YGWrap.WrapReverse,
            _ => YGWrap.NoWrap
        });
        YGNodeStyleSetAlignItems(yoga, Alignment(s.AlignItems, s.Direction));
        YGNodeStyleSetAlignSelf(yoga, cross.Kind == LengthKind.Fill ? YGAlign.Stretch :
            s.AlignSelf is { } align ? Alignment(align, parent.Direction) : YGAlign.Auto);
        YGNodeStyleSetAlignContent(yoga, s.AlignContent switch
        {
            ContentAlign.Center => YGAlign.Center,
            ContentAlign.End => YGAlign.FlexEnd,
            ContentAlign.Stretch => YGAlign.Stretch,
            ContentAlign.SpaceBetween => YGAlign.SpaceBetween,
            ContentAlign.SpaceAround => YGAlign.SpaceAround,
            ContentAlign.SpaceEvenly => YGAlign.SpaceEvenly,
            _ => YGAlign.FlexStart
        });
        YGNodeStyleSetJustifyContent(yoga, s.Justify switch
        {
            Justify.Center => YGJustify.Center,
            Justify.End => YGJustify.FlexEnd,
            Justify.SpaceBetween => YGJustify.SpaceBetween,
            Justify.SpaceAround => YGJustify.SpaceAround,
            Justify.SpaceEvenly => YGJustify.SpaceEvenly,
            _ => YGJustify.FlexStart
        });
        YGNodeStyleSetGap(yoga, YGGutter.Row, s.RowGap ?? s.Gap);
        YGNodeStyleSetGap(yoga, YGGutter.Column, s.ColumnGap ?? s.Gap);
        SetEdges(yoga, s.Padding, s.Margin);
        YGNodeStyleSetMinWidth(yoga, s.MinWidth > 0 ? s.MinWidth : float.NaN);
        YGNodeStyleSetMinHeight(yoga, s.MinHeight > 0 ? s.MinHeight : float.NaN);
        YGNodeStyleSetMaxWidth(yoga, float.IsFinite(s.MaxWidth) ? Math.Max(s.MinWidth, s.MaxWidth) : float.NaN);
        YGNodeStyleSetMaxHeight(yoga, float.IsFinite(s.MaxHeight) ? Math.Max(s.MinHeight, s.MaxHeight) : float.NaN);
        YGNodeStyleSetAspectRatio(yoga, s.AspectRatio > 0 ? s.AspectRatio : float.NaN);
        YGNodeStyleSetFlexGrow(yoga, s.Absolute ? 0 : s.Grow > 0 ? s.Grow : main.Kind == LengthKind.Fill ? 1 : 0);
        YGNodeStyleSetFlexShrink(yoga, s.Shrink ?? (row && node.Flexible ? 1 : 0));
        var basis = s.Basis.Kind == LengthKind.Auto && main.Kind == LengthKind.Fill && !s.Absolute ? Length.Px(0) : s.Basis;
        switch (basis.Kind)
        {
            case LengthKind.Pixels:
                YGNodeStyleSetFlexBasis(yoga, basis.Value);
                break;
            case LengthKind.Percent:
                YGNodeStyleSetFlexBasisPercent(yoga, basis.Value);
                break;
            default:
                YGNodeStyleSetFlexBasisAuto(yoga);
                break;
        }
        SetDimension(yoga, s.Width, true);
        SetDimension(yoga, s.Height, false);
        YGNodeStyleSetOverflow(yoga, s.Scroll ? YGOverflow.Scroll : YGOverflow.Visible);
        // An unconstrained scroller has no intrinsic size along its scrolling axis.
        if (s.ScrollHorizontal && s.Scroll && s.Width.Kind == LengthKind.Auto)
            YGNodeStyleSetWidth(yoga, 0);
        else if (s.Scroll && !s.ScrollHorizontal && s.Height.Kind == LengthKind.Auto && !float.IsFinite(s.MaxHeight))
            YGNodeStyleSetHeight(yoga, 0);
        YGNodeStyleSetPositionType(yoga, s.Absolute ? YGPositionType.Absolute : YGPositionType.Relative);
        YGNodeStyleSetPosition(yoga, YGEdge.Right,
            s.Absolute && s.Width.Kind == LengthKind.Fill ? parent.Padding.Right : float.NaN);
        YGNodeStyleSetPosition(yoga, YGEdge.Bottom,
            s.Absolute && s.Height.Kind == LengthKind.Fill ? parent.Padding.Bottom : float.NaN);
        // Kumiko absolute nodes always start at the parent's content origin, independent
        // of its alignment. Authored offsets are applied during export, outside flex sizing.
        var multiline = parent.Wrap != FlexWrap.NoWrap || row && parent.AlignItems == Align.Baseline;
        var crossStart = (s.AlignSelf ?? parent.AlignItems) == Align.Start;
        // Yoga.Net 3.2.3 reads the relative position instead of the leading margin
        // for flex-start in its multiline alignment pass. Supply that margin there.
        YGNodeStyleSetPosition(yoga, YGEdge.Left, s.Absolute ? parent.Padding.Left :
            multiline && !row && crossStart ? s.Margin.Left : float.NaN);
        YGNodeStyleSetPosition(yoga, YGEdge.Top, s.Absolute ? parent.Padding.Top :
            multiline && row && crossStart ? s.Margin.Top : float.NaN);
        ApplyOptions(c, index, yoga);
    }

    static YGAlign Alignment(Align value, Direction direction) => value switch
    {
        Align.Center => YGAlign.Center,
        Align.End => YGAlign.FlexEnd,
        Align.Stretch => YGAlign.Stretch,
        Align.Baseline when direction == Direction.Row => YGAlign.Baseline,
        _ => YGAlign.FlexStart
    };

    static void SetDimension(YogaNode yoga, Length length, bool width)
    {
        if (length.Kind == LengthKind.Pixels)
        {
            if (width)
                YGNodeStyleSetWidth(yoga, length.Value);
            else
                YGNodeStyleSetHeight(yoga, length.Value);
        }
        else if (length.Kind == LengthKind.Percent)
        {
            var percent = length.Value;
            if (width)
                YGNodeStyleSetWidthPercent(yoga, percent);
            else
                YGNodeStyleSetHeightPercent(yoga, percent);
        }
        else if (width)
            YGNodeStyleSetWidthAuto(yoga);
        else
            YGNodeStyleSetHeightAuto(yoga);
    }

    static void SetEdges(YogaNode yoga, Edges padding, Edges margin)
    {
        YGNodeStyleSetPadding(yoga, YGEdge.Left, padding.Left);
        YGNodeStyleSetPadding(yoga, YGEdge.Top, padding.Top);
        YGNodeStyleSetPadding(yoga, YGEdge.Right, padding.Right);
        YGNodeStyleSetPadding(yoga, YGEdge.Bottom, padding.Bottom);
        YGNodeStyleSetMargin(yoga, YGEdge.Left, margin.Left);
        YGNodeStyleSetMargin(yoga, YGEdge.Top, margin.Top);
        YGNodeStyleSetMargin(yoga, YGEdge.Right, margin.Right);
        YGNodeStyleSetMargin(yoga, YGEdge.Bottom, margin.Bottom);
    }
}
