using Facebook.Yoga;
using static Facebook.Yoga.YGNodeAPI;
using static Facebook.Yoga.YGNodeStyleAPI;
using YogaNode = Facebook.Yoga.Node;

namespace Kumiko.UI;

internal static partial class Layout
{
    internal static void PrepareTree(Context c)
    {
        for (var i = 0; i < c.Count; i++)
        {
            ref var node = ref c.Tree[i];
            var parent = node.Parent;
            node.RightToLeft = node.Style.Flow switch
            {
                FlowDirection.LeftToRight => false,
                FlowDirection.RightToLeft => true,
                _ => parent >= 0 ? c.Tree[parent].RightToLeft : c.RightToLeftWindow
            };
            node.LayoutHidden = node.Style.Layout.Display == DisplayMode.None || parent >= 0 && c.Tree[parent].LayoutHidden;
            if (node.Style.Layout.Position is { } position)
                node.Style.Absolute = position == PositionMode.Absolute;
            while (parent >= 0 && c.Tree[parent].Style.Layout.Display == DisplayMode.Contents)
                parent = c.Tree[parent].Parent;
            node.LayoutParent = parent;
        }
    }

    static void ApplyOptions(Context c, int index, YogaNode yoga)
    {
        ref readonly var node = ref c.Tree[index];
        var s = node.Style;
        var rtl = node.RightToLeft;
        var outerRtl = node.Parent >= 0 && c.Tree[node.Parent].RightToLeft;
        var options = s.Layout;
        YGNodeStyleSetDisplay(yoga, options.Display switch
        {
            DisplayMode.None => YGDisplay.None,
            DisplayMode.Contents => YGDisplay.Contents,
            _ => YGDisplay.Flex
        });
        YGNodeStyleSetBoxSizing(yoga, options.BoxSizing == BoxSizing.ContentBox ? YGBoxSizing.ContentBox : YGBoxSizing.BorderBox);
        YGNodeStyleSetOverflow(yoga, s.Scroll ? YGOverflow.Scroll : s.Clip ? YGOverflow.Hidden : YGOverflow.Visible);
        // A scroll viewport contributes no intrinsic minimum on its scrolling axis,
        // including when an ancestor recursively probes its min-content size.
        yoga.SetMinContentWidth(s.Scroll && s.ScrollHorizontal ? FloatOptional.Zero : FloatOptional.Undefined);
        yoga.SetMinContentHeight(s.Scroll && !s.ScrollHorizontal ? FloatOptional.Zero : FloatOptional.Undefined);
        YGNodeSetIsReferenceBaseline(yoga, options.ReferenceBaseline);
        YGNodeSetAlwaysFormsContainingBlock(yoga, options.ContainingBlock);
        // Changing a containing-block flag has no dirty propagation in the port.
        yoga.MarkDirtyAndPropagate();
        YGNodeStyleSetFlex(yoga, options.FlexValue ?? float.NaN);
        if (options.FlexValue is not null)
        {
            YGNodeStyleSetFlexGrow(yoga, s.Grow > 0 ? s.Grow : float.NaN);
            YGNodeStyleSetFlexShrink(yoga, s.Shrink ?? float.NaN);
            if (s.Basis.Kind == LengthKind.Auto)
                YGNodeStyleSetFlexBasisAuto(yoga);
        }
        SetGap(yoga, YGGutter.All, options.Gap ?? Length.Px(s.Gap));
        SetGap(yoga, YGGutter.Row, options.RowGap ?? (s.RowGap is { } row ? Length.Px(row) : options.Gap ?? Length.Px(s.Gap)));
        SetGap(yoga, YGGutter.Column, options.ColumnGap ?? (s.ColumnGap is { } column ? Length.Px(column) : options.Gap ?? Length.Px(s.Gap)));
        ApplyBound(yoga, options.MinWidth, true, true);
        ApplyBound(yoga, options.MinHeight, false, true);
        ApplyBound(yoga, options.MaxWidth, true, false);
        ApplyBound(yoga, options.MaxHeight, false, false);
        ApplyEdges(yoga, options.Margin, EdgeProperty.Margin, outerRtl, outerRtl != rtl);
        ApplyEdges(yoga, options.Padding, EdgeProperty.Padding, rtl);
        YGNodeStyleSetBorder(yoga, YGEdge.Top, s.FlowBorder.Top);
        YGNodeStyleSetBorder(yoga, YGEdge.Right, s.FlowBorder.Right);
        YGNodeStyleSetBorder(yoga, YGEdge.Bottom, s.FlowBorder.Bottom);
        YGNodeStyleSetBorder(yoga, YGEdge.Left, s.FlowBorder.Left);
        ApplyEdges(yoga, options.Border, EdgeProperty.Border, rtl);
        if (options.Position == PositionMode.Static)
            YGNodeStyleSetPositionType(yoga, YGPositionType.Static);
        YGNodeStyleSetPosition(yoga, YGEdge.Start, float.NaN);
        YGNodeStyleSetPosition(yoga, YGEdge.End, float.NaN);
        if (options.Insets is not null || options.Position is not null)
        {
            YGNodeStyleSetPosition(yoga, YGEdge.Left, float.NaN);
            YGNodeStyleSetPosition(yoga, YGEdge.Top, float.NaN);
            YGNodeStyleSetPosition(yoga, YGEdge.Right, float.NaN);
            YGNodeStyleSetPosition(yoga, YGEdge.Bottom, float.NaN);
            ApplyEdges(yoga, options.Insets, EdgeProperty.Position, outerRtl, outerRtl != rtl);
        }
    }

    enum EdgeProperty
    {
        Margin, Padding, Position, Border
    }

    static void ApplyEdges(YogaNode yoga, LengthEdges? edges, EdgeProperty property, bool mirror, bool reverseLogical = false)
    {
        // Yoga runs in LTR before Kumiko reflects subtrees. Content edges reflect with
        // the node; outer margins/positions reflect with its parent. Logical edges follow
        // the node's effective reading direction even at an explicit direction boundary.
        if (reverseLogical && edges is { } logical)
            edges = logical with
            {
                Start = logical.End,
                End = logical.Start
            };
        if (mirror && edges is { } physical)
            edges = physical with
            {
                Left = physical.Right,
                Right = physical.Left
            };
        // Logical overrides must be reset as well as physical values when a pooled node changes style.
        SetEdge(yoga, YGEdge.Start, edges?.Start, property);
        SetEdge(yoga, YGEdge.End, edges?.End, property);
        if (edges is { } value)
        {
            if (value.Top is { } top)
                SetEdge(yoga, YGEdge.Top, top, property);
            if (value.Right is { } right)
                SetEdge(yoga, YGEdge.Right, right, property);
            if (value.Bottom is { } bottom)
                SetEdge(yoga, YGEdge.Bottom, bottom, property);
            if (value.Left is { } left)
                SetEdge(yoga, YGEdge.Left, left, property);
        }
    }

    static void SetEdge(YogaNode yoga, YGEdge edge, Length? length, EdgeProperty property)
    {
        var value = length?.Value ?? float.NaN;
        switch (property)
        {
            case EdgeProperty.Margin when length?.Kind == LengthKind.Auto:
                YGNodeStyleSetMarginAuto(yoga, edge);
                break;
            case EdgeProperty.Margin when length?.Kind == LengthKind.Percent:
                YGNodeStyleSetMarginPercent(yoga, edge, value);
                break;
            case EdgeProperty.Margin:
                YGNodeStyleSetMargin(yoga, edge, value);
                break;
            case EdgeProperty.Padding when length?.Kind == LengthKind.Percent:
                YGNodeStyleSetPaddingPercent(yoga, edge, value);
                break;
            case EdgeProperty.Padding:
                YGNodeStyleSetPadding(yoga, edge, value);
                break;
            case EdgeProperty.Position when length?.Kind == LengthKind.Auto:
                YGNodeStyleSetPositionAuto(yoga, edge);
                break;
            case EdgeProperty.Position when length?.Kind == LengthKind.Percent:
                YGNodeStyleSetPositionPercent(yoga, edge, value);
                break;
            case EdgeProperty.Position:
                YGNodeStyleSetPosition(yoga, edge, value);
                break;
            case EdgeProperty.Border:
                YGNodeStyleSetBorder(yoga, edge, value);
                break;
        }
    }

    static void SetGap(YogaNode yoga, YGGutter gutter, Length value)
    {
        if (value.Kind == LengthKind.Percent)
            YGNodeStyleSetGapPercent(yoga, gutter, value.Value);
        else
            YGNodeStyleSetGap(yoga, gutter, value.Value);
    }

    static void ApplyBound(YogaNode yoga, Length? value, bool width, bool minimum)
    {
        if (value is not { } length)
            return;
        var points = length.Kind == LengthKind.Auto ? float.NaN : length.Value;
        var percent = length.Kind == LengthKind.Percent;
        if (width && minimum && percent)
            YGNodeStyleSetMinWidthPercent(yoga, points);
        else if (width && minimum)
            YGNodeStyleSetMinWidth(yoga, points);
        else if (!width && minimum && percent)
            YGNodeStyleSetMinHeightPercent(yoga, points);
        else if (!width && minimum)
            YGNodeStyleSetMinHeight(yoga, points);
        else if (width && percent)
            YGNodeStyleSetMaxWidthPercent(yoga, points);
        else if (width)
            YGNodeStyleSetMaxWidth(yoga, points);
        else if (percent)
            YGNodeStyleSetMaxHeightPercent(yoga, points);
        else
            YGNodeStyleSetMaxHeight(yoga, points);
    }
}
