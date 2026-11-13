// Copyright (c) Meta Platforms, Inc. and affiliates.
// Licensed under the MIT license; see third_party/licenses/Yoga.Net-MIT.txt.

namespace Facebook.Yoga;

internal static partial class LayoutAlgorithm
{
    // CSS Flexbox §4.5 min-content probing. Leaf content is owner-independent;
    // container contributions depend on owner width through percentage spacing.
    private static float ComputeMinContentMainSize(Node node, FlexDirection requestedAxis,
        Direction ownerDirection, float ownerWidth, float ownerHeight, uint generationCount)
    {
        bool wantRow = requestedAxis.IsRow();
        var declared = wantRow ? node.GetMinContentWidth() : node.GetMinContentHeight();
        if (declared.IsDefined())
            return declared.Unwrap();

        var layout = node.Layout;
        ref var memo = ref (wantRow ? ref layout.MinContentWidth : ref layout.MinContentHeight);
        if (node.HasMeasureFunc())
        {
            if (memo.Generation != generationCount)
            {
                float width = wantRow ? 0 : float.NaN;
                float height = wantRow ? float.NaN : 0;
                var widthMode = wantRow ? MeasureMode.AtMost : MeasureMode.Undefined;
                var heightMode = wantRow ? MeasureMode.Undefined : MeasureMode.AtMost;
                var size = node.HasMinContentMeasureFunc()
                    ? node.MeasureMinContent(width, widthMode, height, heightMode)
                    : node.Measure(width, widthMode, height, heightMode);
                memo.Size = wantRow ? size.Width : size.Height;
                memo.Generation = generationCount;
            }
            var direction = node.ResolveDirection(ownerDirection);
            return memo.Size + BoundAxis.PaddingAndBorderForAxis(node, requestedAxis, direction, ownerWidth);
        }
        if (node.GetChildCount() == 0)
            return 0;
        if (memo.Generation == generationCount && Comparison.InexactEquals(memo.OwnerWidth, ownerWidth))
            return memo.Size;

        var resolvedDirection = node.ResolveDirection(ownerDirection);
        var mainAxis = node.Style.FlexDirection.ResolveDirection(resolvedDirection);
        var crossAxis = mainAxis.ResolveCrossDirection(resolvedDirection);
        float mainTotal = 0;
        float crossMax = 0;
        foreach (var child in node.GetChildren())
        {
            if (child.Style.Display == Display.None || child.Style.PositionType == PositionType.Absolute)
                continue;
            float main = ComputeMinContentMainSize(child, mainAxis, resolvedDirection, ownerWidth, ownerHeight, generationCount);
            float cross = ComputeMinContentMainSize(child, crossAxis, resolvedDirection, ownerWidth, ownerHeight, generationCount);
            mainTotal += main + child.Style.ComputeMarginForAxis(mainAxis, ownerWidth);
            crossMax = Math.Max(crossMax, cross + child.Style.ComputeMarginForAxis(crossAxis, ownerWidth));
        }
        mainTotal += BoundAxis.PaddingAndBorderForAxis(node, mainAxis, resolvedDirection, ownerWidth);
        crossMax += BoundAxis.PaddingAndBorderForAxis(node, crossAxis, resolvedDirection, ownerWidth);
        memo.Size = wantRow == mainAxis.IsRow() ? mainTotal : crossMax;
        memo.OwnerWidth = ownerWidth;
        memo.Generation = generationCount;
        return memo.Size;
    }

    private static FloatOptional ComputeAutoMinMainSize(Node child, FlexDirection mainAxis,
        Direction direction, float ownerMainAxisSize, float ownerWidth, float ownerHeight, uint generationCount)
    {
        if (child.Style.Display == Display.None || child.Style.MinDimension(mainAxis.Dimension()).IsDefined())
            return FloatOptional.Undefined;
        if (child.Style.Overflow != Overflow.Visible)
            return FloatOptional.Zero;

        var mainDim = mainAxis.Dimension();
        var crossDim = mainAxis.IsRow() ? Dimension.Height : Dimension.Width;
        var specifiedMain = child.GetResolvedDimension(direction, mainDim, ownerMainAxisSize, ownerWidth);
        var transferred = FloatOptional.Undefined;
        var ratio = child.Style.AspectRatio;
        if (ratio.IsDefined())
        {
            var cross = child.GetResolvedDimension(direction, crossDim,
                mainAxis.IsRow() ? ownerHeight : ownerWidth, ownerWidth);
            if (cross.IsDefined())
                transferred = new(mainAxis.IsRow() ? cross.Unwrap() * ratio.Unwrap() : cross.Unwrap() / ratio.Unwrap());
        }
        var floor = new FloatOptional(ComputeMinContentMainSize(child, mainAxis, direction, ownerWidth, ownerHeight, generationCount));
        var suggestion = specifiedMain.IsDefined() ? specifiedMain : transferred;
        if (suggestion.IsDefined() && (floor.IsUndefined() || suggestion < floor))
            floor = suggestion;
        var max = child.Style.ResolvedMaxDimension(direction, mainDim, ownerMainAxisSize, ownerWidth);
        if (max.IsDefined() && floor > max)
            floor = max;
        return floor.IsUndefined() || floor.Unwrap() < 0 ? FloatOptional.Zero : floor;
    }

    private static float BoundAxisWithAutoMin(Node child, FlexDirection axis, Direction direction,
        float value, float axisSize, float widthSize)
    {
        float bounded = BoundAxis.ComputeBoundAxis(child, axis, direction, value, axisSize, widthSize);
        var floor = child.Layout.ComputedAutoMinMainSize;
        return floor.IsDefined() && bounded < floor.Unwrap() ? floor.Unwrap() : bounded;
    }
}
