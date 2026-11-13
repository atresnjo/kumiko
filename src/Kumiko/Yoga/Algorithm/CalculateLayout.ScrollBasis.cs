// Copyright (c) Meta Platforms, Inc. and affiliates.
// Licensed under the MIT license; see third_party/licenses/Yoga.Net-MIT.txt.

namespace Facebook.Yoga;

internal static partial class LayoutAlgorithm
{
    [ThreadStatic]
    private static Node?[]? s_heightProofStack;

    private static bool HasAutoHorizontalMargin(Style style) =>
        style.FlexStartMarginIsAuto(FlexDirection.Row, Direction.LTR) ||
        style.FlexEndMarginIsAuto(FlexDirection.Row, Direction.LTR) ||
        style.FlexStartMarginIsAuto(FlexDirection.Row, Direction.RTL) ||
        style.FlexEndMarginIsAuto(FlexDirection.Row, Direction.RTL);

    private static bool IsColumnStretchEdge(Node owner, Node child)
    {
        var style = child.Style;
        var width = child.GetProcessedDimension(Dimension.Width);
        return owner.Style.Display == Display.Flex && owner.Style.FlexDirection.IsColumn() &&
            owner.Style.FlexWrap == Wrap.NoWrap && style.PositionType != PositionType.Absolute &&
            !style.AspectRatio.IsDefined() && (width.IsAuto() || width.IsUndefined()) &&
            !HasAutoHorizontalMargin(style) && AlignHelper.ResolveChildAlignment(owner, child) == Align.Stretch;
    }

    private static bool IsInColumnStretchScrollSubtree(Node node)
    {
        for (Node? current = node; current is not null;)
        {
            var owner = current.GetOwner();
            while (owner?.Style.Display == Display.Contents)
                owner = owner.GetOwner();
            if (owner is null || !IsColumnStretchEdge(owner, current))
                return false;
            if (owner.Style.Overflow == Overflow.Scroll)
                return true;
            current = owner;
        }
        return false;
    }

    private static bool IsNonZeroLength(StyleLength length) =>
        length.IsAuto() || length.Value().IsDefined() && length.Value().Unwrap() != 0;

    private static bool HasNonZeroVerticalSpacing(Style style)
    {
        ReadOnlySpan<Edge> edges = [Edge.Top, Edge.Bottom, Edge.Vertical, Edge.All];
        foreach (var edge in edges)
            if (IsNonZeroLength(style.Margin(edge)) || IsNonZeroLength(style.Padding(edge)) || IsNonZeroLength(style.Border(edge)))
                return true;
        return false;
    }

    private static bool HasPercentageLength(Style style)
    {
        for (var i = 0; i < 9; i++)
        {
            var edge = (Edge)i;
            if (style.Margin(edge).IsPercent() || style.Position(edge).IsPercent() ||
                style.Padding(edge).IsPercent() || style.Border(edge).IsPercent())
                return true;
        }
        for (var i = 0; i < 2; i++)
        {
            var dimension = (Dimension)i;
            if (style.Dimension(dimension).IsPercent() || style.MinDimension(dimension).IsPercent() || style.MaxDimension(dimension).IsPercent())
                return true;
        }
        return style.FlexBasis.IsPercent() || style.Gap(Gutter.Column).IsPercent() ||
            style.Gap(Gutter.Row).IsPercent() || style.Gap(Gutter.All).IsPercent();
    }

    private static bool HasNonZeroFlex(Node node)
    {
        var style = node.Style;
        bool grow = style.FlexGrow.IsDefined() ? style.FlexGrow.Unwrap() != 0 : style.Flex.IsDefined() && style.Flex.Unwrap() > 0;
        bool shrink = style.FlexShrink.IsDefined() ? style.FlexShrink.Unwrap() != 0 :
            node.Config.UseWebDefaults() || style.Flex.IsDefined() && style.Flex.Unwrap() < 0;
        return grow || shrink;
    }

    private static bool IsHeightFitContentIndependent(Node node)
    {
        var style = node.Style;
        var height = style.Dimension(Dimension.Height);
        var basis = style.FlexBasis;
        bool relativePercent = style.Position(Edge.Top).IsPercent() || style.Position(Edge.Bottom).IsPercent() ||
            style.Position(Edge.Vertical).IsPercent() || style.Position(Edge.All).IsPercent();
        return !node.HasMeasureFunc() && !node.HasMinContentMeasureFunc() && !node.HasBaselineFunc() && !node.IsReferenceBaseline() &&
            (height.IsAuto() || height.IsUndefined()) && style.MinDimension(Dimension.Height).IsUndefined() &&
            style.MaxDimension(Dimension.Height).IsUndefined() && (basis.IsAuto() || basis.IsUndefined()) &&
            !HasNonZeroFlex(node) && style.BoxSizing == BoxSizing.BorderBox && !style.AspectRatio.IsDefined() &&
            style.PositionType != PositionType.Absolute && style.Overflow != Overflow.Scroll && style.Display == Display.Flex &&
            style.FlexDirection.IsColumn() && style.AlignItems == Align.Stretch && style.AlignSelf is Align.Auto or Align.Stretch &&
            style.JustifyContent == Justify.FlexStart && style.FlexWrap == Wrap.NoWrap &&
            !style.Gap(Gutter.All).IsDefined() && !style.Gap(Gutter.Row).IsDefined() && !relativePercent &&
            !HasNonZeroVerticalSpacing(style) && !HasPercentageLength(style);
    }

    private static bool CanSkipHeightFitContent(Node root)
    {
        // No callbacks or layout reentry during this bounded proof. Release every
        // reference even on a failed proof so the scratch cannot retain old trees.
        var stack = s_heightProofStack ??= new Node?[64];
        int count = 1;
        stack[0] = root;
        try
        {
            while (count > 0)
            {
                var node = stack[--count]!;
                if (!IsHeightFitContentIndependent(node))
                    return false;
                foreach (var child in node.GetLayoutChildren())
                {
                    if (count == stack.Length)
                        return false;
                    stack[count++] = child;
                }
            }
            return true;
        }
        finally
        {
            Array.Clear(stack);
        }
    }
}
