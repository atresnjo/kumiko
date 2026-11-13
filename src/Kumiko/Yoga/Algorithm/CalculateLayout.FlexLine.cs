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
    private static float DistributeFreeSpaceSecondPass(
        FlexLine flexLine, Node node,
        FlexDirection mainAxis, FlexDirection crossAxis, Direction direction,
        float ownerWidth, float mainAxisOwnerSize,
        float availableInnerMainDim, float availableInnerCrossDim,
        float availableInnerWidth, float availableInnerHeight,
        bool mainAxisOverflows, SizingMode sizingModeCrossDim,
        bool performLayout, ref LayoutData layoutMarkerData,
        uint depth, uint generationCount)
    {
        float childFlexBasis = 0;
        float flexShrinkScaledFactor = 0;
        float flexGrowFactor = 0;
        float deltaFreeSpace = 0;
        bool isMainAxisRow = mainAxis.IsRow();
        bool isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;

        foreach (var currentLineChild in flexLine.ItemsInFlow)
        {
            childFlexBasis = BoundAxis.BoundAxisWithinMinAndMax(
                currentLineChild, direction, mainAxis,
                     currentLineChild.Layout.ComputedFlexBasis,
                mainAxisOwnerSize, ownerWidth).Unwrap();
            float updatedMainSize = childFlexBasis;

            if (Comparison.IsDefined(flexLine.Layout.RemainingFreeSpace) &&
                flexLine.Layout.RemainingFreeSpace < 0)
            {
                flexShrinkScaledFactor =
                    -currentLineChild.ResolveFlexShrink() * childFlexBasis;
                if (flexShrinkScaledFactor != 0)
                {
                    float childSize = YogaConstants.Undefined;
                    if (Comparison.IsDefined(flexLine.Layout.TotalFlexShrinkScaledFactors) &&
                        (flexLine.Layout.TotalFlexShrinkScaledFactors > 0 ||
                         Math.Abs(flexLine.Layout.TotalFlexShrinkScaledFactors) < 1e-6f))
                    {
                        childSize = childFlexBasis + flexShrinkScaledFactor;
                    }
                    else
                    {
                        childSize = childFlexBasis +
                            (flexLine.Layout.RemainingFreeSpace /
                             flexLine.Layout.TotalFlexShrinkScaledFactors) *
                                flexShrinkScaledFactor;
                    }
                    updatedMainSize = BoundAxisWithAutoMin(
                        currentLineChild, mainAxis, direction,
                        childSize, availableInnerMainDim, availableInnerWidth);
                }
            }
            else if (Comparison.IsDefined(flexLine.Layout.RemainingFreeSpace) &&
                flexLine.Layout.RemainingFreeSpace > 0)
            {
                flexGrowFactor = currentLineChild.ResolveFlexGrow();
                if (!float.IsNaN(flexGrowFactor) && flexGrowFactor != 0)
                {
                    updatedMainSize = BoundAxisWithAutoMin(
                        currentLineChild, mainAxis, direction,
                        childFlexBasis + flexLine.Layout.RemainingFreeSpace /
                                flexLine.Layout.TotalFlexGrowFactors * flexGrowFactor,
                        availableInnerMainDim, availableInnerWidth);
                }
            }

            deltaFreeSpace += updatedMainSize - childFlexBasis;

            float marginMain = currentLineChild.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth);
            float marginCross = currentLineChild.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth);

            float childCrossSize = YogaConstants.Undefined;
            float childMainSize = updatedMainSize + marginMain;
            SizingMode childCrossSizingMode;
            SizingMode childMainSizingMode = SizingMode.StretchFit;

            var childStyle = currentLineChild.Style;
            if (childStyle.AspectRatio.IsDefined())
            {
                childCrossSize = isMainAxisRow
                    ? (childMainSize - marginMain) / childStyle.AspectRatio.Unwrap()
                    : (childMainSize - marginMain) * childStyle.AspectRatio.Unwrap();
                childCrossSizingMode = SizingMode.StretchFit;
                childCrossSize += marginCross;
            }
            else if (!float.IsNaN(availableInnerCrossDim) &&
                !currentLineChild.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim) &&
                sizingModeCrossDim == SizingMode.StretchFit &&
                !(isNodeFlexWrap && mainAxisOverflows) &&
                AlignHelper.ResolveChildAlignment(node, currentLineChild) == Align.Stretch &&
                !currentLineChild.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                !currentLineChild.Style.FlexEndMarginIsAuto(crossAxis, direction))
            {
                childCrossSize = availableInnerCrossDim;
                childCrossSizingMode = SizingMode.StretchFit;
            }
            else if (!currentLineChild.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim))
            {
                childCrossSize = availableInnerCrossDim;
                childCrossSizingMode = Comparison.IsUndefined(childCrossSize)
                    ? SizingMode.MaxContent : SizingMode.FitContent;
            }
            else
            {
                childCrossSize = currentLineChild
                    .GetResolvedDimension(direction, crossAxis.Dimension(),
                        availableInnerCrossDim, availableInnerWidth)
                    .Unwrap() + marginCross;
                bool isLoosePercentageMeasurement =
                    currentLineChild.ProcessedDimension(crossAxis.Dimension()).IsPercent() &&
                    sizingModeCrossDim != SizingMode.StretchFit;
                childCrossSizingMode =
                    Comparison.IsUndefined(childCrossSize) || isLoosePercentageMeasurement
                    ? SizingMode.MaxContent : SizingMode.StretchFit;
            }

            ConstrainMaxSizeForMode(currentLineChild, direction, mainAxis,
                availableInnerMainDim, availableInnerWidth,
                ref childMainSizingMode, ref childMainSize);
            ConstrainMaxSizeForMode(currentLineChild, direction, crossAxis,
                availableInnerCrossDim, availableInnerWidth,
                ref childCrossSizingMode, ref childCrossSize);

            bool requiresStretchLayout =
                !currentLineChild.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim) &&
                AlignHelper.ResolveChildAlignment(node, currentLineChild) == Align.Stretch &&
                !currentLineChild.Style.FlexStartMarginIsAuto(crossAxis, direction) &&
                !currentLineChild.Style.FlexEndMarginIsAuto(crossAxis, direction);

            float childWidth = isMainAxisRow ? childMainSize : childCrossSize;
            float childHeight = !isMainAxisRow ? childMainSize : childCrossSize;
            SizingMode childWidthSizingMode =
                isMainAxisRow ? childMainSizingMode : childCrossSizingMode;
            SizingMode childHeightSizingMode =
                !isMainAxisRow ? childMainSizingMode : childCrossSizingMode;

            bool isLayoutPass = performLayout && !requiresStretchLayout;
            CalculateLayoutInternal(
                currentLineChild, childWidth, childHeight,
                node.Layout.GetDirection(),
                childWidthSizingMode, childHeightSizingMode,
                availableInnerWidth, availableInnerHeight,
                isLayoutPass,
                isLayoutPass ? LayoutPassReason.FlexLayout : LayoutPassReason.FlexMeasure,
                ref layoutMarkerData, depth, generationCount);
            node.SetLayoutHadOverflow(
                node.Layout.HadOverflow() || currentLineChild.Layout.HadOverflow());
        }
        return deltaFreeSpace;
    }

    private static void DistributeFreeSpaceFirstPass(
        FlexLine flexLine, Direction direction, FlexDirection mainAxis,
        float ownerWidth, float mainAxisOwnerSize,
        float availableInnerMainDim, float availableInnerWidth)
    {
        float flexShrinkScaledFactor = 0;
        float flexGrowFactor = 0;
        float baseMainSize = 0;
        float boundMainSize = 0;
        float deltaFreeSpace = 0;
        float originalGrowFactors = flexLine.Layout.TotalFlexGrowFactors;
        float originalShrinkFactors = flexLine.Layout.TotalFlexShrinkScaledFactors;

        foreach (var currentLineChild in flexLine.ItemsInFlow)
        {
            float childFlexBasis = BoundAxis.BoundAxisWithinMinAndMax(
                currentLineChild, direction, mainAxis,
                           currentLineChild.Layout.ComputedFlexBasis,
                mainAxisOwnerSize, ownerWidth).Unwrap();

            if (flexLine.Layout.RemainingFreeSpace < 0)
            {
                flexShrinkScaledFactor =
                    -currentLineChild.ResolveFlexShrink() * childFlexBasis;
                if (Comparison.IsDefined(flexShrinkScaledFactor) && flexShrinkScaledFactor != 0)
                {
                    baseMainSize = childFlexBasis +
                        flexLine.Layout.RemainingFreeSpace /
                            originalShrinkFactors * flexShrinkScaledFactor;
                    boundMainSize = BoundAxisWithAutoMin(
                        currentLineChild, mainAxis, direction,
                        baseMainSize, availableInnerMainDim, availableInnerWidth);
                    if (Comparison.IsDefined(baseMainSize) && Comparison.IsDefined(boundMainSize) &&
                        baseMainSize != boundMainSize)
                    {
                        deltaFreeSpace += boundMainSize - childFlexBasis;
                        flexLine.Layout.TotalFlexShrinkScaledFactors -=
                            (-currentLineChild.ResolveFlexShrink() *
                             currentLineChild.Layout.ComputedFlexBasis.Unwrap());
                    }
                }
            }
            else if (Comparison.IsDefined(flexLine.Layout.RemainingFreeSpace) &&
                flexLine.Layout.RemainingFreeSpace > 0)
            {
                flexGrowFactor = currentLineChild.ResolveFlexGrow();
                if (Comparison.IsDefined(flexGrowFactor) && flexGrowFactor != 0)
                {
                    baseMainSize = childFlexBasis +
                        flexLine.Layout.RemainingFreeSpace /
                            originalGrowFactors * flexGrowFactor;
                    boundMainSize = BoundAxis.ComputeBoundAxis(
                        currentLineChild, mainAxis, direction,
                        baseMainSize, availableInnerMainDim, availableInnerWidth);
                    if (Comparison.IsDefined(baseMainSize) && Comparison.IsDefined(boundMainSize) &&
                        baseMainSize != boundMainSize)
                    {
                        deltaFreeSpace += boundMainSize - childFlexBasis;
                        flexLine.Layout.TotalFlexGrowFactors -= flexGrowFactor;
                    }
                }
            }
        }
        flexLine.Layout.RemainingFreeSpace -= deltaFreeSpace;
    }

    private static void ResolveFlexibleLength(
        Node node, FlexLine flexLine,
        FlexDirection mainAxis, FlexDirection crossAxis, Direction direction,
        float ownerWidth, float mainAxisOwnerSize,
        float availableInnerMainDim, float availableInnerCrossDim,
        float availableInnerWidth, float availableInnerHeight,
        bool mainAxisOverflows, SizingMode sizingModeCrossDim,
        bool performLayout, ref LayoutData layoutMarkerData,
        uint depth, uint generationCount)
    {
        float originalFreeSpace = flexLine.Layout.RemainingFreeSpace;
        foreach (var child in flexLine.ItemsInFlow)
            child.Layout.ComputedAutoMinMainSize = ComputeAutoMinMainSize(child, mainAxis, direction,
                mainAxisOwnerSize, availableInnerWidth, availableInnerHeight, generationCount);
        DistributeFreeSpaceFirstPass(
            flexLine, direction, mainAxis, ownerWidth,
            mainAxisOwnerSize, availableInnerMainDim, availableInnerWidth);

        float distributedFreeSpace = DistributeFreeSpaceSecondPass(
            flexLine, node, mainAxis, crossAxis, direction,
            ownerWidth, mainAxisOwnerSize,
            availableInnerMainDim, availableInnerCrossDim,
            availableInnerWidth, availableInnerHeight,
            mainAxisOverflows, sizingModeCrossDim, performLayout,
            ref layoutMarkerData, depth, generationCount);

        flexLine.Layout.RemainingFreeSpace = originalFreeSpace - distributedFreeSpace;
    }

    private static void JustifyMainAxis(
        Node node, FlexLine flexLine,
        FlexDirection mainAxis, FlexDirection crossAxis, Direction direction,
        SizingMode sizingModeMainDim, SizingMode sizingModeCrossDim,
        float mainAxisOwnerSize, float ownerWidth,
        float availableInnerMainDim, float availableInnerCrossDim,
        float availableInnerWidth, bool performLayout)
    {
        var style = node.Style;
        float leadingPaddingAndBorderMain =
            node.Style.ComputeFlexStartPaddingAndBorder(mainAxis, direction, ownerWidth);
        float trailingPaddingAndBorderMain =
            node.Style.ComputeFlexEndPaddingAndBorder(mainAxis, direction, ownerWidth);
        float gap = node.Style.ComputeGapForAxis(mainAxis, availableInnerMainDim);

        if (sizingModeMainDim == SizingMode.FitContent &&
            flexLine.Layout.RemainingFreeSpace > 0)
        {
            if (style.MinDimension(mainAxis.Dimension()).IsDefined() &&
                style.ResolvedMinDimension(
                    direction, mainAxis.Dimension(), mainAxisOwnerSize, ownerWidth).IsDefined())
            {
                float minAvailableMainDim =
                    style.ResolvedMinDimension(
                            direction, mainAxis.Dimension(), mainAxisOwnerSize, ownerWidth)
                        .Unwrap() - leadingPaddingAndBorderMain - trailingPaddingAndBorderMain;
                float occupiedSpaceByChildNodes =
                    availableInnerMainDim - flexLine.Layout.RemainingFreeSpace;
                flexLine.Layout.RemainingFreeSpace = Comparison.MaxOrDefined(
                    0.0f, minAvailableMainDim - occupiedSpaceByChildNodes);
            }
            else
            {
                flexLine.Layout.RemainingFreeSpace = 0;
            }
        }

        float leadingMainDim = 0;
        float betweenMainDim = gap;
        Justify justifyContent = flexLine.Layout.RemainingFreeSpace >= 0
            ? node.Style.JustifyContent
            : AlignHelper.FallbackAlignment(node.Style.JustifyContent);

        if (flexLine.NumberOfAutoMargins == 0)
        {
            switch (justifyContent)
            {
                case Justify.Start:
                case Justify.End:
                case Justify.Auto:
                case Justify.Stretch:
                case Justify.FlexStart:
                    break;
                case Justify.Center:
                    leadingMainDim = flexLine.Layout.RemainingFreeSpace / 2;
                    break;
                case Justify.FlexEnd:
                    leadingMainDim = flexLine.Layout.RemainingFreeSpace;
                    break;
                case Justify.SpaceBetween:
                    if (flexLine.ItemsInFlow.Count > 1)
                        betweenMainDim += flexLine.Layout.RemainingFreeSpace /
                            (float)(flexLine.ItemsInFlow.Count - 1);
                    break;
                case Justify.SpaceEvenly:
                    leadingMainDim = flexLine.Layout.RemainingFreeSpace /
                        (float)(flexLine.ItemsInFlow.Count + 1);
                    betweenMainDim += leadingMainDim;
                    break;
                case Justify.SpaceAround:
                    leadingMainDim = 0.5f * flexLine.Layout.RemainingFreeSpace /
                        (float)(flexLine.ItemsInFlow.Count);
                    betweenMainDim += leadingMainDim * 2;
                    break;
            }
        }

        flexLine.Layout.MainDim = leadingPaddingAndBorderMain + leadingMainDim;
        flexLine.Layout.CrossDim = 0;

        float maxAscentForCurrentLine = 0;
        float maxDescentForCurrentLine = 0;
        bool isNodeBaselineLayout = Baseline.IsBaselineLayout(node);
        for (int i = 0; i < flexLine.ItemsInFlow.Count; i++)
        {
            var child = flexLine.ItemsInFlow[i];
            var childLayout = child.Layout;
            if (child.Style.FlexStartMarginIsAuto(mainAxis, direction) &&
                flexLine.Layout.RemainingFreeSpace > 0.0f)
            {
                flexLine.Layout.MainDim += flexLine.Layout.RemainingFreeSpace /
                    (float)(flexLine.NumberOfAutoMargins);
            }

            if (performLayout)
            {
                child.SetLayoutPosition(
                    childLayout.Position(mainAxis.FlexStartEdge()) + flexLine.Layout.MainDim,
                    mainAxis.FlexStartEdge());
            }

            if (i != flexLine.ItemsInFlow.Count - 1)
                flexLine.Layout.MainDim += betweenMainDim;

            if (child.Style.FlexEndMarginIsAuto(mainAxis, direction) &&
                flexLine.Layout.RemainingFreeSpace > 0.0f)
            {
                flexLine.Layout.MainDim += flexLine.Layout.RemainingFreeSpace /
                    (float)(flexLine.NumberOfAutoMargins);
            }
            bool canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;
            if (canSkipFlex)
            {
                flexLine.Layout.MainDim +=
                    child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth) +
                    BoundAxis.BoundAxisWithinMinAndMax(child, direction, mainAxis,
                        childLayout.ComputedFlexBasis, mainAxisOwnerSize, ownerWidth).Unwrap();
                flexLine.Layout.CrossDim = availableInnerCrossDim;
            }
            else
            {
                flexLine.Layout.MainDim +=
                    child.DimensionWithMargin(mainAxis, availableInnerWidth);

                if (isNodeBaselineLayout)
                {
                    float ascent = Baseline.CalculateBaseline(child) +
                        child.Style.ComputeFlexStartMargin(
                            FlexDirection.Column, direction, availableInnerWidth);
                    float descent =
                        child.Layout.MeasuredDimension(Dimension.Height) +
                        child.Style.ComputeMarginForAxis(FlexDirection.Column, availableInnerWidth) -
                        ascent;
                    maxAscentForCurrentLine = Comparison.MaxOrDefined(maxAscentForCurrentLine, ascent);
                    maxDescentForCurrentLine = Comparison.MaxOrDefined(maxDescentForCurrentLine, descent);
                }
                else
                {
                    flexLine.Layout.CrossDim = Comparison.MaxOrDefined(
                        flexLine.Layout.CrossDim,
                        child.DimensionWithMargin(crossAxis, availableInnerWidth));
                }
            }
        }
        flexLine.Layout.MainDim += trailingPaddingAndBorderMain;
        if (isNodeBaselineLayout)
            flexLine.Layout.CrossDim = maxAscentForCurrentLine + maxDescentForCurrentLine;
    }

    // Kumiko: extracted unchanged from CalculateLayoutImpl (STEP 8) to keep source files short.
    private static void AlignMultilineContent(
        Node node, List<Node> layoutChildren, int lineCount, float totalLineCrossDim, float crossAxisGap,
        float leadingPaddingAndBorderCross, float paddingAndBorderAxisCross, SizingMode sizingModeCrossDim,
        float availableInnerCrossDim, float crossAxisOwnerSize, float ownerWidth, Direction direction,
        FlexDirection mainAxis, FlexDirection crossAxis, bool isMainAxisRow,
        float availableInnerWidth, float availableInnerHeight,
        ref LayoutData layoutMarkerData, uint depth, uint generationCount)
    {
        float leadPerLine = 0;
        float currentLead = leadingPaddingAndBorderCross;
        float extraSpacePerLine = 0;

        float unclampedCrossDim = sizingModeCrossDim == SizingMode.StretchFit
            ? availableInnerCrossDim + paddingAndBorderAxisCross
            : node.HasDefiniteLength(crossAxis.Dimension(), crossAxisOwnerSize)
            ? node.GetResolvedDimension(direction, crossAxis.Dimension(), crossAxisOwnerSize, ownerWidth).Unwrap()
            : totalLineCrossDim + paddingAndBorderAxisCross;

        float innerCrossDim = BoundAxis.ComputeBoundAxis(
            node, crossAxis, direction, unclampedCrossDim,
            crossAxisOwnerSize, ownerWidth) - paddingAndBorderAxisCross;

        float remainingAlignContentDim = innerCrossDim - totalLineCrossDim;
        Align alignContent = remainingAlignContentDim >= 0
            ? node.Style.AlignContent
            : AlignHelper.FallbackAlignment(node.Style.AlignContent);

        switch (alignContent)
        {
            case Align.Start:
            case Align.End:
                break;
            case Align.FlexEnd:
                currentLead += remainingAlignContentDim;
                break;
            case Align.Center:
                currentLead += remainingAlignContentDim / 2;
                break;
            case Align.Stretch:
                extraSpacePerLine = remainingAlignContentDim / (float)(lineCount);
                break;
            case Align.SpaceAround:
                currentLead += remainingAlignContentDim / (2 * (float)(lineCount));
                leadPerLine = remainingAlignContentDim / (float)(lineCount);
                break;
            case Align.SpaceEvenly:
                currentLead += remainingAlignContentDim / (float)(lineCount + 1);
                leadPerLine = remainingAlignContentDim / (float)(lineCount + 1);
                break;
            case Align.SpaceBetween:
                if (lineCount > 1)
                    leadPerLine = remainingAlignContentDim / (float)(lineCount - 1);
                break;
            case Align.Auto:
            case Align.FlexStart:
            case Align.Baseline:
                break;
        }

        int endIndex = 0;
        for (int i = 0; i < lineCount; i++)
        {
            int startIndex = endIndex;
            float lineHeight = 0;
            float maxAscentForCurrentLine = 0;
            float maxDescentForCurrentLine = 0;
            for (int j = startIndex; j < layoutChildren.Count; j++)
            {
                var child = layoutChildren[j];
                if (child.Style.Display == Display.None)
                    continue;
                if (child.Style.PositionType != PositionType.Absolute)
                {
                    if (child.GetLineIndex() != (nuint)i)
                        break;
                    if (child.IsLayoutDimensionDefined(crossAxis))
                    {
                        lineHeight = Comparison.MaxOrDefined(lineHeight,
                            child.Layout.MeasuredDimension(crossAxis.Dimension()) +
                                child.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth));
                    }
                    if (AlignHelper.ResolveChildAlignment(node, child) == Align.Baseline)
                    {
                        float ascent = Baseline.CalculateBaseline(child) +
                            child.Style.ComputeFlexStartMargin(FlexDirection.Column, direction, availableInnerWidth);
                        float descent = child.Layout.MeasuredDimension(Dimension.Height) +
                            child.Style.ComputeMarginForAxis(FlexDirection.Column, availableInnerWidth) - ascent;
                        maxAscentForCurrentLine = Comparison.MaxOrDefined(maxAscentForCurrentLine, ascent);
                        maxDescentForCurrentLine = Comparison.MaxOrDefined(maxDescentForCurrentLine, descent);
                        lineHeight = Comparison.MaxOrDefined(lineHeight, maxAscentForCurrentLine + maxDescentForCurrentLine);
                    }
                }
                endIndex = j + 1;
            }
            currentLead += i != 0 ? crossAxisGap : 0;
            lineHeight += extraSpacePerLine;

            for (int j = startIndex; j < endIndex; j++)
            {
                var child = layoutChildren[j];
                if (child.Style.Display == Display.None)
                    continue;
                if (child.Style.PositionType != PositionType.Absolute)
                {
                    switch (AlignHelper.ResolveChildAlignment(node, child))
                    {
                        case Align.Start:
                        case Align.End:
                            break;
                        case Align.FlexStart:
                            child.SetLayoutPosition(
                                currentLead + child.Style.ComputeFlexStartPosition(crossAxis, direction, availableInnerWidth),
                                crossAxis.FlexStartEdge());
                            break;
                        case Align.FlexEnd:
                            child.SetLayoutPosition(
                                currentLead + lineHeight -
                                    child.Style.ComputeFlexEndMargin(crossAxis, direction, availableInnerWidth) -
                                    child.Layout.MeasuredDimension(crossAxis.Dimension()),
                                crossAxis.FlexStartEdge());
                            break;
                        case Align.Center:
                            {
                                float childHeightCross = child.Layout.MeasuredDimension(crossAxis.Dimension());
                                child.SetLayoutPosition(currentLead + (lineHeight - childHeightCross) / 2, crossAxis.FlexStartEdge());
                                break;
                            }
                        case Align.Stretch:
                            {
                                child.SetLayoutPosition(
                                    currentLead + child.Style.ComputeFlexStartMargin(crossAxis, direction, availableInnerWidth),
                                    crossAxis.FlexStartEdge());
                                if (!child.HasDefiniteLength(crossAxis.Dimension(), availableInnerCrossDim))
                                {
                                    float childWidthML = isMainAxisRow
                                        ? (child.Layout.MeasuredDimension(Dimension.Width) +
                                           child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth))
                                        : leadPerLine + lineHeight;
                                    float childHeightML = !isMainAxisRow
                                        ? (child.Layout.MeasuredDimension(Dimension.Height) +
                                           child.Style.ComputeMarginForAxis(crossAxis, availableInnerWidth))
                                        : leadPerLine + lineHeight;
                                    if (!(Comparison.InexactEquals(childWidthML, child.Layout.MeasuredDimension(Dimension.Width)) &&
                                          Comparison.InexactEquals(childHeightML, child.Layout.MeasuredDimension(Dimension.Height))))
                                    {
                                        CalculateLayoutInternal(child, childWidthML, childHeightML, direction,
                                            SizingMode.StretchFit, SizingMode.StretchFit,
                                            availableInnerWidth, availableInnerHeight,
                                            true, LayoutPassReason.MultilineStretch,
                                            ref layoutMarkerData, depth, generationCount);
                                    }
                                }
                                break;
                            }
                        case Align.Baseline:
                            child.SetLayoutPosition(
                                currentLead + maxAscentForCurrentLine - Baseline.CalculateBaseline(child) +
                                    child.Style.ComputeFlexStartPosition(FlexDirection.Column, direction, availableInnerCrossDim),
                                PhysicalEdge.Top);
                            break;
                        case Align.Auto:
                        case Align.SpaceBetween:
                        case Align.SpaceAround:
                        case Align.SpaceEvenly:
                            break;
                    }
                }
            }
            currentLead = currentLead + leadPerLine + lineHeight;
        }
    }
}
