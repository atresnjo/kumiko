// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Facebook.Yoga;

internal static partial class LayoutAlgorithm
{
    public static void ConstrainMaxSizeForMode(
        Node node,
        Direction direction,
        FlexDirection axis,
        float ownerAxisSize,
        float ownerWidth,
        ref SizingMode mode,
        ref float size)
    {
        var maxSize = node.Style.ResolvedMaxDimension(
                direction,
                axis.Dimension(),
                ownerAxisSize,
                ownerWidth) +
            new FloatOptional(node.Style.ComputeMarginForAxis(axis, ownerWidth));

        switch (mode)
        {
            case SizingMode.StretchFit:
            case SizingMode.FitContent:
                size = (!maxSize.IsDefined() || size < maxSize.Unwrap())
                    ? size
                    : maxSize.Unwrap();
                break;
            case SizingMode.MaxContent:
                if (maxSize.IsDefined())
                {
                    mode = SizingMode.FitContent;
                    size = maxSize.Unwrap();
                }
                break;
        }
    }

    private static void ComputeFlexBasisForChild(
        Node node,
        Node child,
        float width,
        SizingMode widthMode,
        float height,
        float ownerWidth,
        float ownerHeight,
        SizingMode heightMode,
        Direction direction,
        ref LayoutData layoutMarkerData,
        uint depth,
        uint generationCount)
    {
        var mainAxis = node.Style.FlexDirection.ResolveDirection(direction);
        bool isMainAxisRow = mainAxis.IsRow();
        float mainAxisSize = isMainAxisRow ? width : height;
        float mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;

        float childWidth = YogaConstants.Undefined;
        float childHeight = YogaConstants.Undefined;
        SizingMode childWidthSizingMode;
        SizingMode childHeightSizingMode;

        var resolvedFlexBasis = child.ResolveFlexBasis(
            direction, mainAxis, mainAxisOwnerSize, ownerWidth);
        bool isRowStyleDimDefined =
            child.HasDefiniteLength(Dimension.Width, ownerWidth);
        bool isColumnStyleDimDefined =
            child.HasDefiniteLength(Dimension.Height, ownerHeight);

        bool fixFlexBasisFitContent =
            node.Config.IsExperimentalFeatureEnabled(
                ExperimentalFeature.FixFlexBasisFitContent);

        bool useResolvedFlexBasis =
            resolvedFlexBasis.IsDefined() && Comparison.IsDefined(mainAxisSize);

        if (useResolvedFlexBasis)
        {
            if (child.Layout.ComputedFlexBasis.IsUndefined() ||
                (child.Config.IsExperimentalFeatureEnabled(
                     ExperimentalFeature.WebFlexBasis) &&
                 child.Layout.ComputedFlexBasisGeneration != generationCount))
            {
                var paddingAndBorder = new FloatOptional(
                    BoundAxis.PaddingAndBorderForAxis(child, mainAxis, direction, ownerWidth));
                child.SetLayoutComputedFlexBasis(
                    FloatOptional.MaxOrDefined(resolvedFlexBasis, paddingAndBorder));
            }
        }
        else if (isMainAxisRow && isRowStyleDimDefined)
        {
            var paddingAndBorder = new FloatOptional(
                BoundAxis.PaddingAndBorderForAxis(
                    child, FlexDirection.Row, direction, ownerWidth));
            child.SetLayoutComputedFlexBasis(
                FloatOptional.MaxOrDefined(
                    child.GetResolvedDimension(
                        direction, Dimension.Width, ownerWidth, ownerWidth),
                    paddingAndBorder));
        }
        else if (!isMainAxisRow && isColumnStyleDimDefined)
        {
            var paddingAndBorder = new FloatOptional(
                BoundAxis.PaddingAndBorderForAxis(
                    child, FlexDirection.Column, direction, ownerWidth));
            child.SetLayoutComputedFlexBasis(
                FloatOptional.MaxOrDefined(
                    child.GetResolvedDimension(
                        direction, Dimension.Height, ownerHeight, ownerWidth),
                    paddingAndBorder));
        }
        else
        {
            childWidthSizingMode = SizingMode.MaxContent;
            childHeightSizingMode = SizingMode.MaxContent;

            float marginRow =
                child.Style.ComputeMarginForAxis(FlexDirection.Row, ownerWidth);
            float marginColumn =
                child.Style.ComputeMarginForAxis(FlexDirection.Column, ownerWidth);

            if (isRowStyleDimDefined)
            {
                childWidth = child
                    .GetResolvedDimension(
                        direction, Dimension.Width, ownerWidth, ownerWidth)
                    .Unwrap() + marginRow;
                childWidthSizingMode = SizingMode.StretchFit;
            }
            if (isColumnStyleDimDefined)
            {
                childHeight = child
                        .GetResolvedDimension(
                            direction, Dimension.Height, ownerHeight, ownerWidth)
                    .Unwrap() + marginColumn;
                childHeightSizingMode = SizingMode.StretchFit;
            }

            if ((!isMainAxisRow && node.Style.Overflow == Overflow.Scroll) ||
                node.Style.Overflow != Overflow.Scroll)
            {
                if (Comparison.IsUndefined(childWidth) && Comparison.IsDefined(width))
                {
                    childWidth = width;
                    childWidthSizingMode = SizingMode.FitContent;
                }
            }

            bool parentDoesNotScroll = node.Style.Overflow != Overflow.Scroll;
            bool applyHeightFitContent = isMainAxisRow || parentDoesNotScroll;
            if (fixFlexBasisFitContent)
            {
                bool independent = !isMainAxisRow && parentDoesNotScroll &&
                    Comparison.IsUndefined(childHeight) && Comparison.IsDefined(height) &&
                    IsColumnStretchEdge(node, child) && IsInColumnStretchScrollSubtree(node) &&
                    CanSkipHeightFitContent(child);
                if (independent)
                {
                    if (child.IsDirty() && child.Layout.HadOverflow())
                        child.SetLayoutHadOverflow(false);
                    applyHeightFitContent = false;
                }
            }
            if (applyHeightFitContent && Comparison.IsUndefined(childHeight) &&
                Comparison.IsDefined(height))
            {
                childHeight = height;
                childHeightSizingMode = SizingMode.FitContent;
            }

            var childStyle = child.Style;
            if (childStyle.AspectRatio.IsDefined())
            {
                if (!isMainAxisRow && childWidthSizingMode == SizingMode.StretchFit)
                {
                    childHeight = marginColumn +
                        (childWidth - marginRow) / childStyle.AspectRatio.Unwrap();
                    childHeightSizingMode = SizingMode.StretchFit;
                }
                else if (isMainAxisRow && childHeightSizingMode == SizingMode.StretchFit)
                {
                    childWidth = marginRow +
                        (childHeight - marginColumn) * childStyle.AspectRatio.Unwrap();
                    childWidthSizingMode = SizingMode.StretchFit;
                }
            }

            bool hasExactWidth =
                Comparison.IsDefined(width) && widthMode == SizingMode.StretchFit;
            bool childWidthStretch =
                AlignHelper.ResolveChildAlignment(node, child) == Align.Stretch &&
                childWidthSizingMode != SizingMode.StretchFit;
            if (!isMainAxisRow && !isRowStyleDimDefined && hasExactWidth &&
                childWidthStretch)
            {
                childWidth = width;
                childWidthSizingMode = SizingMode.StretchFit;
                if (childStyle.AspectRatio.IsDefined())
                {
                    childHeight =
                        (childWidth - marginRow) / childStyle.AspectRatio.Unwrap();
                    childHeightSizingMode = SizingMode.StretchFit;
                }
            }

            bool hasExactHeight =
                Comparison.IsDefined(height) && heightMode == SizingMode.StretchFit;
            bool childHeightStretch =
                AlignHelper.ResolveChildAlignment(node, child) == Align.Stretch &&
                childHeightSizingMode != SizingMode.StretchFit;
            if (isMainAxisRow && !isColumnStyleDimDefined && hasExactHeight &&
                childHeightStretch)
            {
                childHeight = height;
                childHeightSizingMode = SizingMode.StretchFit;
                if (childStyle.AspectRatio.IsDefined())
                {
                    childWidth =
                        (childHeight - marginColumn) * childStyle.AspectRatio.Unwrap();
                    childWidthSizingMode = SizingMode.StretchFit;
                }
            }

            ConstrainMaxSizeForMode(
                child, direction, FlexDirection.Row, ownerWidth, ownerWidth,
                ref childWidthSizingMode, ref childWidth);
            ConstrainMaxSizeForMode(
                child, direction, FlexDirection.Column, ownerHeight, ownerWidth,
                ref childHeightSizingMode, ref childHeight);

            CalculateLayoutInternal(
                child, childWidth, childHeight, direction,
                childWidthSizingMode, childHeightSizingMode,
                ownerWidth, ownerHeight, false,
                LayoutPassReason.MeasureChild,
                ref layoutMarkerData, depth, generationCount);

            child.SetLayoutComputedFlexBasis(new FloatOptional(
                Comparison.MaxOrDefined(
                    child.Layout.MeasuredDimension(mainAxis.Dimension()),
                    BoundAxis.PaddingAndBorderForAxis(child, mainAxis, direction, ownerWidth))));
        }
        child.SetLayoutComputedFlexBasisGeneration(generationCount);
    }

    private static void MeasureNodeWithMeasureFunc(
        Node node, Direction direction,
        float availableWidth, float availableHeight,
        SizingMode widthSizingMode, SizingMode heightSizingMode,
        float ownerWidth, float ownerHeight,
        ref LayoutData layoutMarkerData, LayoutPassReason reason)
    {
        Debug.AssertFatal.AssertWithNode(
            node, node.HasMeasureFunc(),
            "Expected node to have custom measure function");

        if (widthSizingMode == SizingMode.MaxContent)
            availableWidth = YogaConstants.Undefined;
        if (heightSizingMode == SizingMode.MaxContent)
            availableHeight = YogaConstants.Undefined;

        var layout = node.Layout;
        float paddingAndBorderAxisRow = layout.Padding(PhysicalEdge.Left) +
            layout.Padding(PhysicalEdge.Right) + layout.Border(PhysicalEdge.Left) +
            layout.Border(PhysicalEdge.Right);
        float paddingAndBorderAxisColumn = layout.Padding(PhysicalEdge.Top) +
            layout.Padding(PhysicalEdge.Bottom) + layout.Border(PhysicalEdge.Top) +
            layout.Border(PhysicalEdge.Bottom);

        float innerWidth = Comparison.IsUndefined(availableWidth)
            ? availableWidth
            : Comparison.MaxOrDefined(0.0f, availableWidth - paddingAndBorderAxisRow);
        float innerHeight = Comparison.IsUndefined(availableHeight)
            ? availableHeight
            : Comparison.MaxOrDefined(0.0f, availableHeight - paddingAndBorderAxisColumn);

        if (widthSizingMode == SizingMode.StretchFit &&
            heightSizingMode == SizingMode.StretchFit)
        {
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                    availableWidth, ownerWidth, ownerWidth), Dimension.Width);
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                    availableHeight, ownerHeight, ownerWidth), Dimension.Height);
        }
        else
        {
            Event.Publish(node, EventType.MeasureCallbackStart);

            var measuredSize = node.Measure(
                innerWidth, widthSizingMode.ToMeasureMode(),
                innerHeight, heightSizingMode.ToMeasureMode());

            if (Event.Enabled)
                Event.Publish(node, EventType.MeasureCallbackEnd,
                new Event.MeasureCallbackEndData
                {
                    Width = innerWidth,
                    WidthMeasureMode = widthSizingMode.ToMeasureMode(),
                    Height = innerHeight,
                    HeightMeasureMode = heightSizingMode.ToMeasureMode(),
                    MeasuredWidth = measuredSize.Width,
                    MeasuredHeight = measuredSize.Height,
                    Reason = reason,
                });

            layoutMarkerData.MeasureCallbacks += 1;
            if (layoutMarkerData.MeasureCallbackReasonsCount is { } reasons)
                reasons[(int)reason] += 1;

            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                    (widthSizingMode == SizingMode.MaxContent ||
                     widthSizingMode == SizingMode.FitContent)
                        ? measuredSize.Width + paddingAndBorderAxisRow
                        : availableWidth,
                    ownerWidth, ownerWidth), Dimension.Width);

            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                    (heightSizingMode == SizingMode.MaxContent ||
                     heightSizingMode == SizingMode.FitContent)
                        ? measuredSize.Height + paddingAndBorderAxisColumn
                        : availableHeight,
                    ownerHeight, ownerWidth), Dimension.Height);
        }
    }

    private static void MeasureNodeWithoutChildren(
        Node node, Direction direction,
        float availableWidth, float availableHeight,
        SizingMode widthSizingMode, SizingMode heightSizingMode,
        float ownerWidth, float ownerHeight)
    {
        var layout = node.Layout;
        float width = availableWidth;
        if (widthSizingMode == SizingMode.MaxContent ||
            widthSizingMode == SizingMode.FitContent)
        {
            width = layout.Padding(PhysicalEdge.Left) +
                layout.Padding(PhysicalEdge.Right) +
                layout.Border(PhysicalEdge.Left) + layout.Border(PhysicalEdge.Right);
        }
        node.SetLayoutMeasuredDimension(
            BoundAxis.ComputeBoundAxis(
                node, FlexDirection.Row, direction, width, ownerWidth, ownerWidth),
            Dimension.Width);

        float height = availableHeight;
        if (heightSizingMode == SizingMode.MaxContent ||
            heightSizingMode == SizingMode.FitContent)
        {
            height = layout.Padding(PhysicalEdge.Top) +
                layout.Padding(PhysicalEdge.Bottom) +
                layout.Border(PhysicalEdge.Top) + layout.Border(PhysicalEdge.Bottom);
        }
        node.SetLayoutMeasuredDimension(
            BoundAxis.ComputeBoundAxis(
                node, FlexDirection.Column, direction, height, ownerHeight, ownerWidth),
            Dimension.Height);
    }

    internal static bool IsFixedSize(float dim, SizingMode sizingMode)
    {
        return sizingMode == SizingMode.StretchFit ||
            (Comparison.IsDefined(dim) && sizingMode == SizingMode.FitContent && dim <= 0.0);
    }

    private static bool MeasureNodeWithFixedSize(
        Node node, Direction direction,
        float availableWidth, float availableHeight,
        SizingMode widthSizingMode, SizingMode heightSizingMode,
        float ownerWidth, float ownerHeight)
    {
        if (IsFixedSize(availableWidth, widthSizingMode) &&
            IsFixedSize(availableHeight, heightSizingMode))
        {
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                    Comparison.IsUndefined(availableWidth) ||
                            (widthSizingMode == SizingMode.FitContent && availableWidth < 0.0f)
                        ? 0.0f : availableWidth,
                    ownerWidth, ownerWidth), Dimension.Width);

            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                    Comparison.IsUndefined(availableHeight) ||
                            (heightSizingMode == SizingMode.FitContent && availableHeight < 0.0f)
                        ? 0.0f : availableHeight,
                    ownerHeight, ownerWidth), Dimension.Height);
            return true;
        }
        return false;
    }

    public static void ZeroOutLayoutRecursively(Node node)
    {
        node.Layout = new LayoutResults();
        node.SetLayoutDimension(0, Dimension.Width);
        node.SetLayoutDimension(0, Dimension.Height);
        node.SetHasNewLayout(true);
        node.CloneChildrenIfNeeded();
        foreach (var child in node.GetChildren())
            ZeroOutLayoutRecursively(child);
    }

    public static void CleanupContentsNodesRecursively(Node node, bool didPerformLayout)
    {
        if (node.HasContentsChildren())
        {
            node.CloneContentsChildrenIfNeeded();
            foreach (var child in node.GetChildren())
            {
                if (child.Style.Display == Display.Contents)
                {
                    child.Layout = new LayoutResults();
                    child.SetLayoutDimension(0, Dimension.Width);
                    child.SetLayoutDimension(0, Dimension.Height);
                    if (didPerformLayout)
                        child.SetHasNewLayout(true);
                    child.SetDirty(false);
                    child.CloneChildrenIfNeeded();
                    CleanupContentsNodesRecursively(child, didPerformLayout);
                }
            }
        }
    }

    public static float CalculateAvailableInnerDimension(
        Node node, Direction direction, Dimension dimension,
        float availableDim, float paddingAndBorder,
        float ownerDim, float ownerWidth)
    {
        float availableInnerDim = availableDim - paddingAndBorder;
        if (Comparison.IsDefined(availableInnerDim))
        {
            var minDimensionOptional =
                node.Style.ResolvedMinDimension(direction, dimension, ownerDim, ownerWidth);
            float minInnerDim = minDimensionOptional.IsUndefined()
                ? 0.0f : minDimensionOptional.Unwrap() - paddingAndBorder;

            var maxDimensionOptional =
                node.Style.ResolvedMaxDimension(direction, dimension, ownerDim, ownerWidth);
            float maxInnerDim = maxDimensionOptional.IsUndefined()
                ? float.MaxValue : maxDimensionOptional.Unwrap() - paddingAndBorder;
            availableInnerDim = Comparison.MaxOrDefined(
                Comparison.MinOrDefined(availableInnerDim, maxInnerDim), minInnerDim);
        }
        return availableInnerDim;
    }

    private static float ComputeFlexBasisForChildren(
        Node node, float availableInnerWidth, float availableInnerHeight,
        float ownerWidth, float ownerHeight,
        SizingMode widthSizingMode, SizingMode heightSizingMode,
        Direction direction, FlexDirection mainAxis, bool performLayout,
        ref LayoutData layoutMarkerData, uint depth, uint generationCount)
    {
        float totalOuterFlexBasis = 0.0f;
        Node? singleFlexChild = null;
        var children = node.GetLayoutChildren();
        SizingMode sizingModeMainDim =
            mainAxis.IsRow() ? widthSizingMode : heightSizingMode;
        if (sizingModeMainDim == SizingMode.StretchFit)
        {
            foreach (var child in children)
            {
                if (child.IsNodeFlexible())
                {
                    if (singleFlexChild != null ||
                        Comparison.InexactEquals(child.ResolveFlexGrow(), 0.0f) ||
                        Comparison.InexactEquals(child.ResolveFlexShrink(), 0.0f))
                    {
                        singleFlexChild = null;
                        break;
                    }
                    else
                    {
                        singleFlexChild = child;
                    }
                }
            }
        }

        foreach (var child in children)
        {
            child.ProcessDimensions();
            if (child.Style.Display == Display.None)
            {
                if (performLayout)
                {
                    ZeroOutLayoutRecursively(child);
                    child.SetHasNewLayout(true);
                    child.SetDirty(false);
                }
                continue;
            }
            if (performLayout)
            {
                Direction childDirection = child.ResolveDirection(direction);
                child.SetPosition(childDirection, availableInnerWidth, availableInnerHeight);
            }
            if (child.Style.PositionType == PositionType.Absolute)
                continue;
            if (child == singleFlexChild)
            {
                child.SetLayoutComputedFlexBasisGeneration(generationCount);
                child.SetLayoutComputedFlexBasis(new FloatOptional(0));
            }
            else
            {
                ComputeFlexBasisForChild(
                    node, child, availableInnerWidth, widthSizingMode,
                    availableInnerHeight, ownerWidth, ownerHeight,
                    heightSizingMode, direction,
                    ref layoutMarkerData, depth, generationCount);
            }
            totalOuterFlexBasis +=
                (child.Layout.ComputedFlexBasis.Unwrap() +
                 child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth));
        }
        return totalOuterFlexBasis;
    }
}
