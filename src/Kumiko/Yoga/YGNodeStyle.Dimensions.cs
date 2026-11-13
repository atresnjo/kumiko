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
    public static void YGNodeStyleSetWidth(Node node, float points)
    {
        SetDimension(node, Dimension.Width, StyleSizeLength.Points(points));
    }

    public static void YGNodeStyleSetWidthPercent(Node node, float percent)
    {
        SetDimension(node, Dimension.Width, StyleSizeLength.Percent(percent));
    }

    public static void YGNodeStyleSetWidthAuto(Node node)
    {
        SetDimension(node, Dimension.Width, StyleSizeLength.OfAuto());
    }

    public static void YGNodeStyleSetWidthMaxContent(Node node)
    {
        SetDimension(node, Dimension.Width, StyleSizeLength.OfMaxContent());
    }

    public static void YGNodeStyleSetWidthFitContent(Node node)
    {
        SetDimension(node, Dimension.Width, StyleSizeLength.OfFitContent());
    }

    public static void YGNodeStyleSetWidthStretch(Node node)
    {
        SetDimension(node, Dimension.Width, StyleSizeLength.OfStretch());
    }

    public static YGValue YGNodeStyleGetWidth(Node node)
    {
        return node.Style.Dimension(Dimension.Width).ToYGValue();
    }

    // --- Height ---

    public static void YGNodeStyleSetHeight(Node node, float points)
    {
        SetDimension(node, Dimension.Height, StyleSizeLength.Points(points));
    }

    public static void YGNodeStyleSetHeightPercent(Node node, float percent)
    {
        SetDimension(node, Dimension.Height, StyleSizeLength.Percent(percent));
    }

    public static void YGNodeStyleSetHeightAuto(Node node)
    {
        SetDimension(node, Dimension.Height, StyleSizeLength.OfAuto());
    }

    public static void YGNodeStyleSetHeightMaxContent(Node node)
    {
        SetDimension(node, Dimension.Height, StyleSizeLength.OfMaxContent());
    }

    public static void YGNodeStyleSetHeightFitContent(Node node)
    {
        SetDimension(node, Dimension.Height, StyleSizeLength.OfFitContent());
    }

    public static void YGNodeStyleSetHeightStretch(Node node)
    {
        SetDimension(node, Dimension.Height, StyleSizeLength.OfStretch());
    }

    public static YGValue YGNodeStyleGetHeight(Node node)
    {
        return node.Style.Dimension(Dimension.Height).ToYGValue();
    }

    // --- MinWidth ---

    public static void YGNodeStyleSetMinWidth(Node node, float minWidth)
    {
        SetMinDimension(node, Dimension.Width, StyleSizeLength.Points(minWidth));
    }

    public static void YGNodeStyleSetMinWidthPercent(Node node, float minWidth)
    {
        SetMinDimension(node, Dimension.Width, StyleSizeLength.Percent(minWidth));
    }

    public static void YGNodeStyleSetMinWidthMaxContent(Node node)
    {
        SetMinDimension(node, Dimension.Width, StyleSizeLength.OfMaxContent());
    }

    public static void YGNodeStyleSetMinWidthFitContent(Node node)
    {
        SetMinDimension(node, Dimension.Width, StyleSizeLength.OfFitContent());
    }

    public static void YGNodeStyleSetMinWidthStretch(Node node)
    {
        SetMinDimension(node, Dimension.Width, StyleSizeLength.OfStretch());
    }

    public static YGValue YGNodeStyleGetMinWidth(Node node)
    {
        return node.Style.MinDimension(Dimension.Width).ToYGValue();
    }

    // --- MinHeight ---

    public static void YGNodeStyleSetMinHeight(Node node, float minHeight)
    {
        SetMinDimension(node, Dimension.Height, StyleSizeLength.Points(minHeight));
    }

    public static void YGNodeStyleSetMinHeightPercent(Node node, float minHeight)
    {
        SetMinDimension(node, Dimension.Height, StyleSizeLength.Percent(minHeight));
    }

    public static void YGNodeStyleSetMinHeightMaxContent(Node node)
    {
        SetMinDimension(node, Dimension.Height, StyleSizeLength.OfMaxContent());
    }

    public static void YGNodeStyleSetMinHeightFitContent(Node node)
    {
        SetMinDimension(node, Dimension.Height, StyleSizeLength.OfFitContent());
    }

    public static void YGNodeStyleSetMinHeightStretch(Node node)
    {
        SetMinDimension(node, Dimension.Height, StyleSizeLength.OfStretch());
    }

    public static YGValue YGNodeStyleGetMinHeight(Node node)
    {
        return node.Style.MinDimension(Dimension.Height).ToYGValue();
    }

    // --- MaxWidth ---

    public static void YGNodeStyleSetMaxWidth(Node node, float maxWidth)
    {
        SetMaxDimension(node, Dimension.Width, StyleSizeLength.Points(maxWidth));
    }

    public static void YGNodeStyleSetMaxWidthPercent(Node node, float maxWidth)
    {
        SetMaxDimension(node, Dimension.Width, StyleSizeLength.Percent(maxWidth));
    }

    public static void YGNodeStyleSetMaxWidthMaxContent(Node node)
    {
        SetMaxDimension(node, Dimension.Width, StyleSizeLength.OfMaxContent());
    }

    public static void YGNodeStyleSetMaxWidthFitContent(Node node)
    {
        SetMaxDimension(node, Dimension.Width, StyleSizeLength.OfFitContent());
    }

    public static void YGNodeStyleSetMaxWidthStretch(Node node)
    {
        SetMaxDimension(node, Dimension.Width, StyleSizeLength.OfStretch());
    }

    public static YGValue YGNodeStyleGetMaxWidth(Node node)
    {
        return node.Style.MaxDimension(Dimension.Width).ToYGValue();
    }

    // --- MaxHeight ---

    public static void YGNodeStyleSetMaxHeight(Node node, float maxHeight)
    {
        SetMaxDimension(node, Dimension.Height, StyleSizeLength.Points(maxHeight));
    }

    public static void YGNodeStyleSetMaxHeightPercent(Node node, float maxHeight)
    {
        SetMaxDimension(node, Dimension.Height, StyleSizeLength.Percent(maxHeight));
    }

    public static void YGNodeStyleSetMaxHeightMaxContent(Node node)
    {
        SetMaxDimension(node, Dimension.Height, StyleSizeLength.OfMaxContent());
    }

    public static void YGNodeStyleSetMaxHeightFitContent(Node node)
    {
        SetMaxDimension(node, Dimension.Height, StyleSizeLength.OfFitContent());
    }

    public static void YGNodeStyleSetMaxHeightStretch(Node node)
    {
        SetMaxDimension(node, Dimension.Height, StyleSizeLength.OfStretch());
    }

    public static YGValue YGNodeStyleGetMaxHeight(Node node)
    {
        return node.Style.MaxDimension(Dimension.Height).ToYGValue();
    }

    // --- Grid Item Properties ---
}
