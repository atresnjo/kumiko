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
    private static uint gCurrentGenerationCount = 0;

    private static bool IsCachedForOwnerSize(Node node, in CachedMeasurement cached, float ownerWidth, float ownerHeight) =>
        (!node.Style.ResolvesAgainstOwnerWidth() || Comparison.InexactEquals(cached.OwnerWidth, ownerWidth)) &&
        (!node.Style.ResolvesAgainstOwnerHeight() || Comparison.InexactEquals(cached.OwnerHeight, ownerHeight));

    public static bool CalculateLayoutInternal(
        Node node, float availableWidth, float availableHeight,
    Direction ownerDirection,
        SizingMode widthSizingMode, SizingMode heightSizingMode,
        float ownerWidth, float ownerHeight,
        bool performLayout, LayoutPassReason reason,
        ref LayoutData layoutMarkerData, uint depth, uint generationCount)
    {
        LayoutResults layout = node.Layout;
        depth++;

        bool needToVisitNode =
            (node.IsDirty() && layout.GenerationCount != generationCount) ||
                layout.ConfigVersion != node.Config.GetVersion() ||
            layout.LastOwnerDirection != ownerDirection;

        if (needToVisitNode)
        {
            layout.NextCachedMeasurementsIndex = 0;
            layout.CachedLayout.AvailableWidth = -1;
            layout.CachedLayout.AvailableHeight = -1;
            layout.CachedLayout.WidthSizingMode = SizingMode.MaxContent;
            layout.CachedLayout.HeightSizingMode = SizingMode.MaxContent;
            layout.CachedLayout.ComputedWidth = -1;
            layout.CachedLayout.ComputedHeight = -1;
            layout.CachedLayout.OwnerWidth = -1;
            layout.CachedLayout.OwnerHeight = -1;
        }

        CachedMeasurement? cachedResults = null;

        if (node.HasMeasureFunc())
        {
            float marginAxisRow = node.Style.ComputeMarginForAxis(FlexDirection.Row, ownerWidth);
            float marginAxisColumn = node.Style.ComputeMarginForAxis(FlexDirection.Column, ownerWidth);

            if (Cache.CanUseCachedMeasurement(
                        widthSizingMode, availableWidth, heightSizingMode, availableHeight,
                        layout.CachedLayout.WidthSizingMode, layout.CachedLayout.AvailableWidth,
                        layout.CachedLayout.HeightSizingMode, layout.CachedLayout.AvailableHeight,
                        layout.CachedLayout.ComputedWidth, layout.CachedLayout.ComputedHeight,
                        marginAxisRow, marginAxisColumn, node.Config) &&
                IsCachedForOwnerSize(node, layout.CachedLayout, ownerWidth, ownerHeight))
            {
                cachedResults = layout.CachedLayout;
            }
            else
            {
                for (int i = 0; i < layout.NextCachedMeasurementsIndex; i++)
                {
                    if (Cache.CanUseCachedMeasurement(
                                widthSizingMode, availableWidth, heightSizingMode, availableHeight,
                            layout.CachedMeasurements[i].WidthSizingMode,
                            layout.CachedMeasurements[i].AvailableWidth,
                            layout.CachedMeasurements[i].HeightSizingMode,
                            layout.CachedMeasurements[i].AvailableHeight,
                            layout.CachedMeasurements[i].ComputedWidth,
                            layout.CachedMeasurements[i].ComputedHeight,
                                marginAxisRow, marginAxisColumn, node.Config) &&
                IsCachedForOwnerSize(node, layout.CachedMeasurements[i], ownerWidth, ownerHeight))
                    {
                        cachedResults = layout.CachedMeasurements[i];
                        break;
                    }
                }
            }
        }
        else if (performLayout)
        {
            if (Comparison.InexactEquals(layout.CachedLayout.AvailableWidth, availableWidth) &&
                Comparison.InexactEquals(layout.CachedLayout.AvailableHeight, availableHeight) &&
                layout.CachedLayout.WidthSizingMode == widthSizingMode &&
                layout.CachedLayout.HeightSizingMode == heightSizingMode &&
                IsCachedForOwnerSize(node, layout.CachedLayout, ownerWidth, ownerHeight))
            {
                cachedResults = layout.CachedLayout;
            }
        }
        else
        {
            for (uint i = 0; i < layout.NextCachedMeasurementsIndex; i++)
            {
                if (Comparison.InexactEquals(layout.CachedMeasurements[i].AvailableWidth, availableWidth) &&
                    Comparison.InexactEquals(layout.CachedMeasurements[i].AvailableHeight, availableHeight) &&
                    layout.CachedMeasurements[i].WidthSizingMode == widthSizingMode &&
                    layout.CachedMeasurements[i].HeightSizingMode == heightSizingMode &&
                    IsCachedForOwnerSize(node, layout.CachedMeasurements[i], ownerWidth, ownerHeight))
                {
                    cachedResults = layout.CachedMeasurements[i];
                    break;
                }
            }
        }

        if (!needToVisitNode && cachedResults != null)
        {
            layout.SetMeasuredDimension(Dimension.Width, cachedResults.Value.ComputedWidth);
            layout.SetMeasuredDimension(Dimension.Height, cachedResults.Value.ComputedHeight);
            if (performLayout)
                layoutMarkerData.CachedLayouts += 1;
            else
                layoutMarkerData.CachedMeasures += 1;
        }
        else
        {
            CalculateLayoutImpl(node, availableWidth, availableHeight,
                ownerDirection, widthSizingMode, heightSizingMode,
                ownerWidth, ownerHeight, performLayout, reason,
                ref layoutMarkerData, depth, generationCount);

            layout.LastOwnerDirection = ownerDirection;
            layout.ConfigVersion = node.Config.GetVersion();

            if (cachedResults == null)
            {
                layoutMarkerData.MaxMeasureCache = Math.Max(
                    layoutMarkerData.MaxMeasureCache,
                    layout.NextCachedMeasurementsIndex + 1u);

                if (layout.NextCachedMeasurementsIndex == LayoutResults.MaxCachedMeasurements)
                    layout.NextCachedMeasurementsIndex = 0;

                var newCacheEntry = new CachedMeasurement
                {
                    OwnerWidth = ownerWidth,
                    OwnerHeight = ownerHeight,
                    AvailableWidth = availableWidth,
                    AvailableHeight = availableHeight,
                    WidthSizingMode = widthSizingMode,
                    HeightSizingMode = heightSizingMode,
                    ComputedWidth = layout.MeasuredDimension(Dimension.Width),
                    ComputedHeight = layout.MeasuredDimension(Dimension.Height),
                };

                if (performLayout)
                {
                    layout.CachedLayout = newCacheEntry;
                }
                else
                {
                    layout.CachedMeasurements[layout.NextCachedMeasurementsIndex] = newCacheEntry;
                    layout.NextCachedMeasurementsIndex++;
                }
            }
        }

        if (performLayout)
        {
            node.SetLayoutDimension(node.Layout.MeasuredDimension(Dimension.Width), Dimension.Width);
            node.SetLayoutDimension(node.Layout.MeasuredDimension(Dimension.Height), Dimension.Height);
            node.SetHasNewLayout(true);
            node.SetDirty(false);
            layout.LastLayoutOwnerWidth = ownerWidth;
            layout.LastLayoutOwnerHeight = ownerHeight;
        }

        layout.GenerationCount = generationCount;

        LayoutType layoutType;
        if (performLayout)
        {
            layoutType = cachedResults != null ? LayoutType.CachedLayout : LayoutType.Layout;
        }
        else
        {
            layoutType = cachedResults != null ? LayoutType.CachedMeasure : LayoutType.Measure;
        }
        if (Event.Enabled)
            Event.Publish(node, EventType.NodeLayout,
            new Event.NodeLayoutData { LayoutType = layoutType });

        return (needToVisitNode || cachedResults == null);
    }

    public static void CalculateLayout(
        Node node, float ownerWidth, float ownerHeight, Direction ownerDirection)
    {
        Event.Publish(node, EventType.LayoutPassStart);
        LayoutData markerData = new LayoutData();
        uint generationCount = ++gCurrentGenerationCount;
        node.ProcessDimensions();
        Direction direction = node.ResolveDirection(ownerDirection);
        float width = YogaConstants.Undefined;
        SizingMode widthSizingMode = SizingMode.MaxContent;
        var style = node.Style;

        if (node.HasDefiniteLength(Dimension.Width, ownerWidth))
        {
            width = node.GetResolvedDimension(direction, FlexDirection.Row.Dimension(), ownerWidth, ownerWidth)
                .Unwrap() + node.Style.ComputeMarginForAxis(FlexDirection.Row, ownerWidth);
            widthSizingMode = SizingMode.StretchFit;
        }
        else if (style.ResolvedMaxDimension(direction, Dimension.Width, ownerWidth, ownerWidth).IsDefined())
        {
            width = style.ResolvedMaxDimension(direction, Dimension.Width, ownerWidth, ownerWidth).Unwrap();
            widthSizingMode = SizingMode.FitContent;
        }
        else
        {
            width = ownerWidth;
            widthSizingMode = Comparison.IsUndefined(width) ? SizingMode.MaxContent : SizingMode.StretchFit;
        }

        float height = YogaConstants.Undefined;
        SizingMode heightSizingMode = SizingMode.MaxContent;
        if (node.HasDefiniteLength(Dimension.Height, ownerHeight))
        {
            height = node.GetResolvedDimension(direction, FlexDirection.Column.Dimension(), ownerHeight, ownerWidth)
                .Unwrap() + node.Style.ComputeMarginForAxis(FlexDirection.Column, ownerWidth);
            heightSizingMode = SizingMode.StretchFit;
        }
        else if (style.ResolvedMaxDimension(direction, Dimension.Height, ownerHeight, ownerWidth).IsDefined())
        {
            height = style.ResolvedMaxDimension(direction, Dimension.Height, ownerHeight, ownerWidth).Unwrap();
            heightSizingMode = SizingMode.FitContent;
        }
        else
        {
            height = ownerHeight;
            heightSizingMode = Comparison.IsUndefined(height) ? SizingMode.MaxContent : SizingMode.StretchFit;
        }

        if (CalculateLayoutInternal(node, width, height, ownerDirection,
                widthSizingMode, heightSizingMode, ownerWidth, ownerHeight,
                true, LayoutPassReason.Initial, ref markerData, 0, generationCount))
        {
            node.SetPosition(node.Layout.GetDirection(), ownerWidth, ownerHeight);
            // Kumiko: a boundary-enabled tree shares one unrounded grid, so rounding would only walk it.
            if (node.Config is not { RelayoutBoundaries: true } config || config.GetPointScaleFactor() != 0)
                PixelGrid.RoundLayoutResultsToPixelGrid(node, 0.0f, 0.0f);
        }
        node.LayoutPendingBoundaries(generationCount, ref markerData);

        if (Event.Enabled)
            Event.Publish(node, EventType.LayoutPassEnd,
            new Event.LayoutPassEndData { LayoutData = markerData });
    }
}
