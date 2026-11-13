// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGNodeStyle.h, yoga/YGNodeStyle.cpp

using System;
using System.Collections.Generic;

namespace Facebook.Yoga;

/// <summary>
/// Public C-style API for Yoga node style properties.
/// Delegates to the internal Node/Style classes.
/// </summary>
internal static partial class YGNodeStyleAPI
{
    // GridColumnStart
    public static void YGNodeStyleSetGridColumnStart(Node node, int gridColumnStart)
    {
        var newValue = GridLine.FromInteger(gridColumnStart);
        if (node.Style.GridColumnStart != newValue)
        {
            node.Style.GridColumnStart = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridColumnStartAuto(Node node)
    {
        var newValue = GridLine.Auto();
        if (node.Style.GridColumnStart != newValue)
        {
            node.Style.GridColumnStart = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridColumnStartSpan(Node node, int span)
    {
        var newValue = GridLine.Span(span);
        if (node.Style.GridColumnStart != newValue)
        {
            node.Style.GridColumnStart = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static int YGNodeStyleGetGridColumnStart(Node node)
    {
        var gridLine = node.Style.GridColumnStart;
        return gridLine.IsInteger() ? gridLine.Integer : 0;
    }

    // GridColumnEnd
    public static void YGNodeStyleSetGridColumnEnd(Node node, int gridColumnEnd)
    {
        var newValue = GridLine.FromInteger(gridColumnEnd);
        if (node.Style.GridColumnEnd != newValue)
        {
            node.Style.GridColumnEnd = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridColumnEndAuto(Node node)
    {
        var newValue = GridLine.Auto();
        if (node.Style.GridColumnEnd != newValue)
        {
            node.Style.GridColumnEnd = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridColumnEndSpan(Node node, int span)
    {
        var newValue = GridLine.Span(span);
        if (node.Style.GridColumnEnd != newValue)
        {
            node.Style.GridColumnEnd = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static int YGNodeStyleGetGridColumnEnd(Node node)
    {
        var gridLine = node.Style.GridColumnEnd;
        return gridLine.IsInteger() ? gridLine.Integer : 0;
    }

    // GridRowStart
    public static void YGNodeStyleSetGridRowStart(Node node, int gridRowStart)
    {
        var newValue = GridLine.FromInteger(gridRowStart);
        if (node.Style.GridRowStart != newValue)
        {
            node.Style.GridRowStart = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridRowStartAuto(Node node)
    {
        var newValue = GridLine.Auto();
        if (node.Style.GridRowStart != newValue)
        {
            node.Style.GridRowStart = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridRowStartSpan(Node node, int span)
    {
        var newValue = GridLine.Span(span);
        if (node.Style.GridRowStart != newValue)
        {
            node.Style.GridRowStart = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static int YGNodeStyleGetGridRowStart(Node node)
    {
        var gridLine = node.Style.GridRowStart;
        return gridLine.IsInteger() ? gridLine.Integer : 0;
    }

    // GridRowEnd
    public static void YGNodeStyleSetGridRowEnd(Node node, int gridRowEnd)
    {
        var newValue = GridLine.FromInteger(gridRowEnd);
        if (node.Style.GridRowEnd != newValue)
        {
            node.Style.GridRowEnd = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridRowEndAuto(Node node)
    {
        var newValue = GridLine.Auto();
        if (node.Style.GridRowEnd != newValue)
        {
            node.Style.GridRowEnd = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGridRowEndSpan(Node node, int span)
    {
        var newValue = GridLine.Span(span);
        if (node.Style.GridRowEnd != newValue)
        {
            node.Style.GridRowEnd = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static int YGNodeStyleGetGridRowEnd(Node node)
    {
        var gridLine = node.Style.GridRowEnd;
        return gridLine.IsInteger() ? gridLine.Integer : 0;
    }

    // --- Grid Container Properties ---

    // Helper: convert YGGridTrackType to GridTrackSize
    private static GridTrackSize GridTrackSizeFromTypeAndValue(YGGridTrackType type, float value)
    {
        return type switch
        {
            YGGridTrackType.Points => GridTrackSize.Length(value),
            YGGridTrackType.Percent => GridTrackSize.Percent(value),
            YGGridTrackType.Fr => GridTrackSize.Fr(value),
            YGGridTrackType.Auto => GridTrackSize.Auto(),
            YGGridTrackType.Minmax => GridTrackSize.Auto(),
            _ => throw new InvalidOperationException("Unknown YGGridTrackType"),
        };
    }

    // GridTemplateColumns
    public static void YGNodeStyleSetGridTemplateColumnsCount(Node node, int count)
    {
        node.Style.ResizeGridTemplateColumns(count);
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridTemplateColumn(Node node, int index, YGGridTrackType type, float value)
    {
        node.Style.SetGridTemplateColumnAt(index, GridTrackSizeFromTypeAndValue(type, value));
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridTemplateColumnMinMax(
        Node node,
        int index,
        YGGridTrackType minType,
        float minValue,
        YGGridTrackType maxType,
        float maxValue)
    {
        node.Style.SetGridTemplateColumnAt(
            index,
            GridTrackSize.MinMax(
                StyleSizeLengthFromTypeAndValue(minType, minValue),
                StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
        node.MarkDirtyAndPropagate();
    }

    // GridTemplateRows
    public static void YGNodeStyleSetGridTemplateRowsCount(Node node, int count)
    {
        node.Style.ResizeGridTemplateRows(count);
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridTemplateRow(Node node, int index, YGGridTrackType type, float value)
    {
        node.Style.SetGridTemplateRowAt(index, GridTrackSizeFromTypeAndValue(type, value));
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridTemplateRowMinMax(
        Node node,
        int index,
        YGGridTrackType minType,
        float minValue,
        YGGridTrackType maxType,
        float maxValue)
    {
        node.Style.SetGridTemplateRowAt(
            index,
            GridTrackSize.MinMax(
                StyleSizeLengthFromTypeAndValue(minType, minValue),
                StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
        node.MarkDirtyAndPropagate();
    }

    // GridAutoColumns
    public static void YGNodeStyleSetGridAutoColumnsCount(Node node, int count)
    {
        node.Style.ResizeGridAutoColumns(count);
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridAutoColumn(Node node, int index, YGGridTrackType type, float value)
    {
        node.Style.SetGridAutoColumnAt(index, GridTrackSizeFromTypeAndValue(type, value));
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridAutoColumnMinMax(
        Node node,
        int index,
        YGGridTrackType minType,
        float minValue,
        YGGridTrackType maxType,
        float maxValue)
    {
        node.Style.SetGridAutoColumnAt(
            index,
            GridTrackSize.MinMax(
                StyleSizeLengthFromTypeAndValue(minType, minValue),
                StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
        node.MarkDirtyAndPropagate();
    }

    // GridAutoRows
    public static void YGNodeStyleSetGridAutoRowsCount(Node node, int count)
    {
        node.Style.ResizeGridAutoRows(count);
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridAutoRow(Node node, int index, YGGridTrackType type, float value)
    {
        node.Style.SetGridAutoRowAt(index, GridTrackSizeFromTypeAndValue(type, value));
        node.MarkDirtyAndPropagate();
    }

    public static void YGNodeStyleSetGridAutoRowMinMax(
        Node node,
        int index,
        YGGridTrackType minType,
        float minValue,
        YGGridTrackType maxType,
        float maxValue)
    {
        node.Style.SetGridAutoRowAt(
            index,
            GridTrackSize.MinMax(
                StyleSizeLengthFromTypeAndValue(minType, minValue),
                StyleSizeLengthFromTypeAndValue(maxType, maxValue)));
        node.MarkDirtyAndPropagate();
    }

    // --- Internal Helpers for Dimension/MinDimension/MaxDimension ---
}
