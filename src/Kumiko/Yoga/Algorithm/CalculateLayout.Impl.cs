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
    private static void CalculateLayoutImpl(
        Node node, float availableWidth, float availableHeight,
        Direction ownerDirection,
        SizingMode widthSizingMode, SizingMode heightSizingMode,
        float ownerWidth, float ownerHeight,
        bool performLayout, LayoutPassReason reason,
        ref LayoutData layoutMarkerData, uint depth, uint generationCount)
    {
        Debug.AssertFatal.AssertWithNode(node,
            Comparison.IsUndefined(availableWidth) ? widthSizingMode == SizingMode.MaxContent : true,
            "availableWidth is indefinite so widthSizingMode must be MaxContent");
        Debug.AssertFatal.AssertWithNode(node,
            Comparison.IsUndefined(availableHeight) ? heightSizingMode == SizingMode.MaxContent : true,
            "availableHeight is indefinite so heightSizingMode must be MaxContent");

        if (performLayout)
            layoutMarkerData.Layouts += 1;
        else
            layoutMarkerData.Measures += 1;

        Direction direction = node.ResolveDirection(ownerDirection);
        node.SetLayoutDirection(direction);
        bool fixFlexBasisFitContent = node.Config.IsExperimentalFeatureEnabled(ExperimentalFeature.FixFlexBasisFitContent);
        if (fixFlexBasisFitContent && performLayout)
            node.SetLayoutHadOverflow(false);

        FlexDirection flexRowDirection = FlexDirection.Row.ResolveDirection(direction);
        FlexDirection flexColumnDirection = FlexDirection.Column.ResolveDirection(direction);

        PhysicalEdge startEdge = direction == Direction.LTR ? PhysicalEdge.Left : PhysicalEdge.Right;
        PhysicalEdge endEdge = direction == Direction.LTR ? PhysicalEdge.Right : PhysicalEdge.Left;

        float marginRowLeading = node.Style.ComputeInlineStartMargin(flexRowDirection, direction, ownerWidth);
        node.SetLayoutMargin(marginRowLeading, startEdge);
        float marginRowTrailing = node.Style.ComputeInlineEndMargin(flexRowDirection, direction, ownerWidth);
        node.SetLayoutMargin(marginRowTrailing, endEdge);
        float marginColumnLeading = node.Style.ComputeInlineStartMargin(flexColumnDirection, direction, ownerWidth);
        node.SetLayoutMargin(marginColumnLeading, PhysicalEdge.Top);
        float marginColumnTrailing = node.Style.ComputeInlineEndMargin(flexColumnDirection, direction, ownerWidth);
        node.SetLayoutMargin(marginColumnTrailing, PhysicalEdge.Bottom);

        float marginAxisRow = marginRowLeading + marginRowTrailing;
        float marginAxisColumn = marginColumnLeading + marginColumnTrailing;

        node.SetLayoutBorder(node.Style.ComputeInlineStartBorder(flexRowDirection, direction), startEdge);
        node.SetLayoutBorder(node.Style.ComputeInlineEndBorder(flexRowDirection, direction), endEdge);
        node.SetLayoutBorder(node.Style.ComputeInlineStartBorder(flexColumnDirection, direction), PhysicalEdge.Top);
        node.SetLayoutBorder(node.Style.ComputeInlineEndBorder(flexColumnDirection, direction), PhysicalEdge.Bottom);

        node.SetLayoutPadding(node.Style.ComputeInlineStartPadding(flexRowDirection, direction, ownerWidth), startEdge);
        node.SetLayoutPadding(node.Style.ComputeInlineEndPadding(flexRowDirection, direction, ownerWidth), endEdge);
        node.SetLayoutPadding(node.Style.ComputeInlineStartPadding(flexColumnDirection, direction, ownerWidth), PhysicalEdge.Top);
        node.SetLayoutPadding(node.Style.ComputeInlineEndPadding(flexColumnDirection, direction, ownerWidth), PhysicalEdge.Bottom);

        if (node.HasMeasureFunc())
        {
            MeasureNodeWithMeasureFunc(node, direction,
                availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
                widthSizingMode, heightSizingMode, ownerWidth, ownerHeight,
                ref layoutMarkerData, reason);
            CleanupContentsNodesRecursively(node, performLayout);
            return;
        }

        var childCount = node.GetLayoutChildCount();
        if (childCount == 0)
        {
            MeasureNodeWithoutChildren(node, direction,
                availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
                widthSizingMode, heightSizingMode, ownerWidth, ownerHeight);
            CleanupContentsNodesRecursively(node, performLayout);
            return;
        }

        if (!performLayout && MeasureNodeWithFixedSize(node, direction,
                availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
                widthSizingMode, heightSizingMode, ownerWidth, ownerHeight))
        {
            CleanupContentsNodesRecursively(node, performLayout);
            return;
        }

        node.CloneChildrenIfNeeded();
        if (!fixFlexBasisFitContent || !performLayout)
            node.SetLayoutHadOverflow(false);
        CleanupContentsNodesRecursively(node, performLayout);

        // STEP 1
        FlexDirection mainAxis = node.Style.FlexDirection.ResolveDirection(direction);
        FlexDirection crossAxis = mainAxis.ResolveCrossDirection(direction);
        bool isMainAxisRow = mainAxis.IsRow();
        bool isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;

        float mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;
        float crossAxisOwnerSize = isMainAxisRow ? ownerHeight : ownerWidth;

        float paddingAndBorderAxisMain = BoundAxis.PaddingAndBorderForAxis(node, mainAxis, direction, ownerWidth);
        float paddingAndBorderAxisCross = BoundAxis.PaddingAndBorderForAxis(node, crossAxis, direction, ownerWidth);
        float leadingPaddingAndBorderCross =
            node.Style.ComputeFlexStartPaddingAndBorder(crossAxis, direction, ownerWidth);

        SizingMode sizingModeMainDim = isMainAxisRow ? widthSizingMode : heightSizingMode;
        SizingMode sizingModeCrossDim = isMainAxisRow ? heightSizingMode : widthSizingMode;

        float paddingAndBorderAxisRow = isMainAxisRow ? paddingAndBorderAxisMain : paddingAndBorderAxisCross;
        float paddingAndBorderAxisColumn = isMainAxisRow ? paddingAndBorderAxisCross : paddingAndBorderAxisMain;

        // STEP 2
        float availableInnerWidth = CalculateAvailableInnerDimension(
            node, direction, Dimension.Width, availableWidth - marginAxisRow,
            paddingAndBorderAxisRow, ownerWidth, ownerWidth);
        float availableInnerHeight = CalculateAvailableInnerDimension(
            node, direction, Dimension.Height, availableHeight - marginAxisColumn,
            paddingAndBorderAxisColumn, ownerHeight, ownerWidth);

        float availableInnerMainDim = isMainAxisRow ? availableInnerWidth : availableInnerHeight;
        float availableInnerCrossDim = isMainAxisRow ? availableInnerHeight : availableInnerWidth;

        // STEP 3
        float totalMainDim = 0;
        totalMainDim += ComputeFlexBasisForChildren(
            node, availableInnerWidth, availableInnerHeight,
            availableInnerWidth, availableInnerHeight,
            widthSizingMode, heightSizingMode, direction, mainAxis,
            performLayout, ref layoutMarkerData, depth, generationCount);

        if (childCount > 1)
        {
            totalMainDim +=
                node.Style.ComputeGapForAxis(mainAxis, availableInnerMainDim) *
                (float)(childCount - 1);
        }

        bool mainAxisOverflows =
            (sizingModeMainDim != SizingMode.MaxContent) && totalMainDim > availableInnerMainDim;

        if (isNodeFlexWrap && mainAxisOverflows && sizingModeMainDim == SizingMode.FitContent)
            sizingModeMainDim = SizingMode.StretchFit;

        // STEP 4: COLLECT FLEX ITEMS INTO FLEX LINES
        var scratch = FlexLayoutScratch.Rent();
        var layoutChildren = scratch.Children;
        foreach (var child in node.GetLayoutChildren())
            layoutChildren.Add(child);
        int startOfLineIndex = 0;
        int lineCount = 0;
        float totalLineCrossDim = 0;
        float crossAxisGap = node.Style.ComputeGapForAxis(crossAxis, availableInnerCrossDim);
        float maxLineMainDim = 0;

        while (startOfLineIndex < layoutChildren.Count)
        {
            var flexLine = scratch.Line;
            startOfLineIndex = YogaAlgorithm.CalculateFlexLine(
                flexLine, node, ownerDirection, ownerWidth, mainAxisOwnerSize,
                availableInnerWidth, availableInnerMainDim, layoutChildren, startOfLineIndex, lineCount);

            bool canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;

            // STEP 5
            bool sizeBasedOnContent = false;
            if (sizingModeMainDim != SizingMode.StretchFit)
            {
                var style = node.Style;
                float minInnerWidth = style.ResolvedMinDimension(
                    direction, Dimension.Width, ownerWidth, ownerWidth).Unwrap() - paddingAndBorderAxisRow;
                float maxInnerWidth = style.ResolvedMaxDimension(
                    direction, Dimension.Width, ownerWidth, ownerWidth).Unwrap() - paddingAndBorderAxisRow;
                float minInnerHeight = style.ResolvedMinDimension(
                    direction, Dimension.Height, ownerHeight, ownerWidth).Unwrap() - paddingAndBorderAxisColumn;
                float maxInnerHeight = style.ResolvedMaxDimension(
                    direction, Dimension.Height, ownerHeight, ownerWidth).Unwrap() - paddingAndBorderAxisColumn;

                float minInnerMainDim = isMainAxisRow ? minInnerWidth : minInnerHeight;
                float maxInnerMainDim = isMainAxisRow ? maxInnerWidth : maxInnerHeight;

                if (Comparison.IsDefined(minInnerMainDim) && flexLine.SizeConsumed < minInnerMainDim)
                {
                    availableInnerMainDim = minInnerMainDim;
                }
                else if (Comparison.IsDefined(maxInnerMainDim) && flexLine.SizeConsumed > maxInnerMainDim)
                {
                    availableInnerMainDim = maxInnerMainDim;
                }
                else
                {
                    bool useLegacyStretchBehaviour = node.HasErrata(Errata.StretchFlexBasis);
                    if (!useLegacyStretchBehaviour &&
                        ((Comparison.IsDefined(flexLine.Layout.TotalFlexGrowFactors) &&
                          flexLine.Layout.TotalFlexGrowFactors == 0) ||
                         (Comparison.IsDefined(node.ResolveFlexGrow()) &&
                          node.ResolveFlexGrow() == 0)))
                    {
                        availableInnerMainDim = flexLine.SizeConsumed;
                    }
                    sizeBasedOnContent = !useLegacyStretchBehaviour;
                }
            }

            if (!sizeBasedOnContent && Comparison.IsDefined(availableInnerMainDim))
                flexLine.Layout.RemainingFreeSpace = availableInnerMainDim - flexLine.SizeConsumed;
            else if (flexLine.SizeConsumed < 0)
                flexLine.Layout.RemainingFreeSpace = -flexLine.SizeConsumed;

            if (!canSkipFlex)
            {
                ResolveFlexibleLength(
                    node, flexLine, mainAxis, crossAxis, direction,
                    ownerWidth, mainAxisOwnerSize,
                    availableInnerMainDim, availableInnerCrossDim,
                    availableInnerWidth, availableInnerHeight,
                    mainAxisOverflows, sizingModeCrossDim, performLayout,
                    ref layoutMarkerData, depth, generationCount);
            }

            node.SetLayoutHadOverflow(
                node.Layout.HadOverflow() || (flexLine.Layout.RemainingFreeSpace < 0));

            // STEP 6
            JustifyMainAxis(node, flexLine, mainAxis, crossAxis, direction,
                sizingModeMainDim, sizingModeCrossDim, mainAxisOwnerSize, ownerWidth,
                availableInnerMainDim, availableInnerCrossDim, availableInnerWidth, performLayout);

            float containerCrossAxis = availableInnerCrossDim;
            if (sizingModeCrossDim == SizingMode.MaxContent || sizingModeCrossDim == SizingMode.FitContent)
            {
                containerCrossAxis = BoundAxis.ComputeBoundAxis(
                    node, crossAxis, direction,
                    flexLine.Layout.CrossDim + paddingAndBorderAxisCross,
                    crossAxisOwnerSize, ownerWidth) - paddingAndBorderAxisCross;
            }

            if (!isNodeFlexWrap && sizingModeCrossDim == SizingMode.StretchFit)
                flexLine.Layout.CrossDim = availableInnerCrossDim;

            if (!isNodeFlexWrap)
            {
                flexLine.Layout.CrossDim = BoundAxis.ComputeBoundAxis(
                    node, crossAxis, direction,
                    flexLine.Layout.CrossDim + paddingAndBorderAxisCross,
                    crossAxisOwnerSize, ownerWidth) - paddingAndBorderAxisCross;
            }

            // STEP 7: CROSS-AXIS ALIGNMENT
            if (performLayout)
            {
                for (int i = 0; i < flexLine.ItemsInFlow.Count; i++)
                {
                    var child = flexLine.ItemsInFlow[i];
                    float leadingCrossDim = leadingPaddingAndBorderCross;
                    Align alignItem = AlignHelper.ResolveChildAlignment(node, child);

                    if (alignItem == Align.Stretch &&
                        !child.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                        !child.Style.FlexEndMarginIsAuto(crossAxis, direction))
                    {
                        if (!child.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim))
                        {
                            float childMainSize = child.Layout.MeasuredDimension(mainAxis.Dimension());
                            var childStyle = child.Style;
                            float childCrossSize = childStyle.AspectRatio.IsDefined()
                                ? child.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth) +
                                    (isMainAxisRow
                                         ? childMainSize / childStyle.AspectRatio.Unwrap()
                                         : childMainSize * childStyle.AspectRatio.Unwrap())
                                : flexLine.Layout.CrossDim;

                            childMainSize += child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth);

                            SizingMode childMainSizingMode = SizingMode.StretchFit;
                            SizingMode childCrossSizingMode = SizingMode.StretchFit;
                            ConstrainMaxSizeForMode(child, direction, mainAxis,
                                availableInnerMainDim, availableInnerWidth,
                                ref childMainSizingMode, ref childMainSize);
                            ConstrainMaxSizeForMode(child, direction, crossAxis,
                                availableInnerCrossDim, availableInnerWidth,
                                ref childCrossSizingMode, ref childCrossSize);

                            float childWidthS = isMainAxisRow ? childMainSize : childCrossSize;
                            float childHeightS = !isMainAxisRow ? childMainSize : childCrossSize;

                            var alignContent = node.Style.AlignContent;
                            var crossAxisDoesNotGrow = alignContent != Align.Stretch && isNodeFlexWrap;
                            SizingMode childWidthSizingMode =
                                Comparison.IsUndefined(childWidthS) || (!isMainAxisRow && crossAxisDoesNotGrow)
                                ? SizingMode.MaxContent : SizingMode.StretchFit;
                            SizingMode childHeightSizingMode =
                                Comparison.IsUndefined(childHeightS) || (isMainAxisRow && crossAxisDoesNotGrow)
                                ? SizingMode.MaxContent : SizingMode.StretchFit;

                            CalculateLayoutInternal(child, childWidthS, childHeightS, direction,
                                childWidthSizingMode, childHeightSizingMode,
                                availableInnerWidth, availableInnerHeight,
                                true, LayoutPassReason.Stretch,
                                ref layoutMarkerData, depth, generationCount);
                        }
                    }
                    else
                    {
                        float remainingCrossDim = containerCrossAxis -
                            child.DimensionWithMargin(crossAxis, availableInnerWidth);

                        if (child.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                            child.Style.FlexEndMarginIsAuto(crossAxis, direction))
                        {
                            leadingCrossDim += Comparison.MaxOrDefined(0.0f, remainingCrossDim / 2);
                        }
                        else if (child.Style.FlexEndMarginIsAuto(crossAxis, direction))
                        {
                            // No-Op
                        }
                        else if (child.Style.FlexStartMarginIsAuto(crossAxis, direction))
                        {
                            leadingCrossDim += Comparison.MaxOrDefined(0.0f, remainingCrossDim);
                        }
                        else if (alignItem == Align.FlexStart)
                        {
                            // No-Op
                        }
                        else if (alignItem == Align.Center)
                        {
                            leadingCrossDim += remainingCrossDim / 2;
                        }
                        else
                        {
                            leadingCrossDim += remainingCrossDim;
                        }
                    }
                    child.SetLayoutPosition(
                        child.Layout.Position(crossAxis.FlexStartEdge()) +
                            totalLineCrossDim + leadingCrossDim,
                        crossAxis.FlexStartEdge());
                }
            }

            float appliedCrossGap = lineCount != 0 ? crossAxisGap : 0.0f;
            totalLineCrossDim += flexLine.Layout.CrossDim + appliedCrossGap;
            maxLineMainDim = Comparison.MaxOrDefined(maxLineMainDim, flexLine.Layout.MainDim);
            lineCount++;
        }

        // STEP 8: MULTI-LINE CONTENT ALIGNMENT
        if (performLayout && (isNodeFlexWrap || Baseline.IsBaselineLayout(node)))
            AlignMultilineContent(node, layoutChildren, lineCount, totalLineCrossDim, crossAxisGap,
                leadingPaddingAndBorderCross, paddingAndBorderAxisCross, sizingModeCrossDim, availableInnerCrossDim,
                crossAxisOwnerSize, ownerWidth, direction, mainAxis, crossAxis, isMainAxisRow,
                availableInnerWidth, availableInnerHeight, ref layoutMarkerData, depth, generationCount);

        FlexLayoutScratch.Return(scratch);

        // STEP 9: COMPUTING FINAL DIMENSIONS
        node.SetLayoutMeasuredDimension(
            BoundAxis.ComputeBoundAxis(node, FlexDirection.Row, direction,
                availableWidth - marginAxisRow, ownerWidth, ownerWidth), Dimension.Width);
        node.SetLayoutMeasuredDimension(
            BoundAxis.ComputeBoundAxis(node, FlexDirection.Column, direction,
                availableHeight - marginAxisColumn, ownerHeight, ownerWidth), Dimension.Height);

        if (sizingModeMainDim == SizingMode.MaxContent ||
            (node.Style.Overflow != Overflow.Scroll && sizingModeMainDim == SizingMode.FitContent))
        {
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, mainAxis, direction,
                    maxLineMainDim, mainAxisOwnerSize, ownerWidth), mainAxis.Dimension());
        }
        else if (sizingModeMainDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll)
        {
            node.SetLayoutMeasuredDimension(
                Comparison.MaxOrDefined(
                    Comparison.MinOrDefined(
                        availableInnerMainDim + paddingAndBorderAxisMain,
                        BoundAxis.BoundAxisWithinMinAndMax(node, direction, mainAxis,
                            new FloatOptional(maxLineMainDim), mainAxisOwnerSize, ownerWidth).Unwrap()),
                    paddingAndBorderAxisMain), mainAxis.Dimension());
        }

        if (sizingModeCrossDim == SizingMode.MaxContent ||
            (node.Style.Overflow != Overflow.Scroll && sizingModeCrossDim == SizingMode.FitContent))
        {
            node.SetLayoutMeasuredDimension(
                BoundAxis.ComputeBoundAxis(node, crossAxis, direction,
                    totalLineCrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth),
                crossAxis.Dimension());
        }
        else if (sizingModeCrossDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll)
        {
            node.SetLayoutMeasuredDimension(
                Comparison.MaxOrDefined(
                    Comparison.MinOrDefined(
                        availableInnerCrossDim + paddingAndBorderAxisCross,
                        BoundAxis.BoundAxisWithinMinAndMax(node, direction, crossAxis,
                            new FloatOptional(totalLineCrossDim + paddingAndBorderAxisCross),
                            crossAxisOwnerSize, ownerWidth).Unwrap()),
                    paddingAndBorderAxisCross), crossAxis.Dimension());
        }

        // wrap-reverse
        if (performLayout && node.Style.FlexWrap == Wrap.WrapReverse)
        {
            foreach (var child in node.GetLayoutChildren())
            {
                if (child.Style.PositionType != PositionType.Absolute)
                {
                    child.SetLayoutPosition(
                        node.Layout.MeasuredDimension(crossAxis.Dimension()) -
                            child.Layout.Position(crossAxis.FlexStartEdge()) -
                            child.Layout.MeasuredDimension(crossAxis.Dimension()),
                        crossAxis.FlexStartEdge());
                }
            }
        }

        if (performLayout)
        {
            // STEP 10: SETTING TRAILING POSITIONS FOR CHILDREN
            bool needsMainTrailingPos = TrailingPosition.NeedsTrailingPosition(mainAxis);
            bool needsCrossTrailingPos = TrailingPosition.NeedsTrailingPosition(crossAxis);
            if (needsMainTrailingPos || needsCrossTrailingPos)
            {
                foreach (var child in node.GetLayoutChildren())
                {
                    if (child.Style.Display == Display.None || child.Style.PositionType == PositionType.Absolute)
                        continue;
                    if (needsMainTrailingPos)
                        TrailingPosition.SetChildTrailingPosition(node, child, mainAxis);
                    if (needsCrossTrailingPos)
                        TrailingPosition.SetChildTrailingPosition(node, child, crossAxis);
                }
            }

            // STEP 11: SIZING AND POSITIONING ABSOLUTE CHILDREN
            if (node.Style.PositionType != PositionType.Static ||
                node.AlwaysFormsContainingBlock || depth == 1)
            {
                AbsoluteLayout.LayoutAbsoluteDescendants(
                    node, node,
                    isMainAxisRow ? sizingModeMainDim : sizingModeCrossDim,
                    direction, ref layoutMarkerData, depth, generationCount,
                    0.0f, 0.0f, availableInnerWidth, availableInnerHeight);
            }
        }
    }
}
