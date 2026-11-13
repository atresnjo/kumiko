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
    public static void YGNodeStyleSetPosition(Node node, YGEdge edge, float points)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.Points(points);
        if (node.Style.Position(internalEdge) != newValue)
        {
            node.Style.SetPosition(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetPositionPercent(Node node, YGEdge edge, float percent)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.Percent(percent);
        if (node.Style.Position(internalEdge) != newValue)
        {
            node.Style.SetPosition(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetPositionAuto(Node node, YGEdge edge)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.OfAuto();
        if (node.Style.Position(internalEdge) != newValue)
        {
            node.Style.SetPosition(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static YGValue YGNodeStyleGetPosition(Node node, YGEdge edge)
    {
        return (YGValue)node.Style.Position(edge.ToInternal());
    }

    // --- Margin ---

    public static void YGNodeStyleSetMargin(Node node, YGEdge edge, float points)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.Points(points);
        if (node.Style.Margin(internalEdge) != newValue)
        {
            node.Style.SetMargin(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetMarginPercent(Node node, YGEdge edge, float percent)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.Percent(percent);
        if (node.Style.Margin(internalEdge) != newValue)
        {
            node.Style.SetMargin(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetMarginAuto(Node node, YGEdge edge)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.OfAuto();
        if (node.Style.Margin(internalEdge) != newValue)
        {
            node.Style.SetMargin(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static YGValue YGNodeStyleGetMargin(Node node, YGEdge edge)
    {
        return (YGValue)node.Style.Margin(edge.ToInternal());
    }

    // --- Padding ---

    public static void YGNodeStyleSetPadding(Node node, YGEdge edge, float points)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.Points(points);
        if (node.Style.Padding(internalEdge) != newValue)
        {
            node.Style.SetPadding(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetPaddingPercent(Node node, YGEdge edge, float percent)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.Percent(percent);
        if (node.Style.Padding(internalEdge) != newValue)
        {
            node.Style.SetPadding(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static YGValue YGNodeStyleGetPadding(Node node, YGEdge edge)
    {
        return (YGValue)node.Style.Padding(edge.ToInternal());
    }

    // --- Border ---

    public static void YGNodeStyleSetBorder(Node node, YGEdge edge, float border)
    {
        var internalEdge = edge.ToInternal();
        var newValue = StyleLength.Points(border);
        if (node.Style.Border(internalEdge) != newValue)
        {
            node.Style.SetBorder(internalEdge, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static float YGNodeStyleGetBorder(Node node, YGEdge edge)
    {
        var border = node.Style.Border(edge.ToInternal());
        if (border.IsUndefined() || border.IsAuto())
        {
            return YogaConstants.Undefined;
        }

        return ((YGValue)border).Value;
    }

    // --- Gap ---

    public static void YGNodeStyleSetGap(Node node, YGGutter gutter, float gapLength)
    {
        var internalGutter = gutter.ToInternal();
        var newValue = StyleLength.Points(gapLength);
        if (node.Style.Gap(internalGutter) != newValue)
        {
            node.Style.SetGap(internalGutter, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetGapPercent(Node node, YGGutter gutter, float percent)
    {
        var internalGutter = gutter.ToInternal();
        var newValue = StyleLength.Percent(percent);
        if (node.Style.Gap(internalGutter) != newValue)
        {
            node.Style.SetGap(internalGutter, newValue);
            node.MarkDirtyAndPropagate();
        }
    }

    public static YGValue YGNodeStyleGetGap(Node node, YGGutter gutter)
    {
        return (YGValue)node.Style.Gap(gutter.ToInternal());
    }

    // --- AspectRatio ---

    public static void YGNodeStyleSetAspectRatio(Node node, float aspectRatio)
    {
        var newValue = new FloatOptional(aspectRatio);
        if (node.Style.AspectRatio != newValue)
        {
            node.Style.AspectRatio = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static float YGNodeStyleGetAspectRatio(Node node)
    {
        var op = node.Style.AspectRatio;
        return op.IsUndefined() ? YogaConstants.Undefined : op.Unwrap();
    }

    // --- BoxSizing ---

    public static void YGNodeStyleSetBoxSizing(Node node, YGBoxSizing boxSizing)
    {
        UpdateStyleEnum(node,
            s => s.BoxSizing,
            (s, v) => s.BoxSizing = v,
            boxSizing.ToInternal());
    }

    public static YGBoxSizing YGNodeStyleGetBoxSizing(Node node)
    {
        return node.Style.BoxSizing.ToYG();
    }

    // --- Width ---
}
