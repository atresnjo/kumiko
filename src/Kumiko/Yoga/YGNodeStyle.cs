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
    // Helper: update a style property (simple value)
    private static void UpdateStyle<TValue>(
            Node node,
        Func<Style, TValue> getter,
        Action<Style, TValue> setter,
        TValue value)
            where TValue : IEquatable<TValue>
    {
        var style = node.Style;
        if (!getter(style).Equals(value))
        {
            setter(style, value);
            node.MarkDirtyAndPropagate();
        }
    }

    // Helper: update a style property (indexed value)
    private static void UpdateStyleIndexed<TIdx, TValue>(
        Node node,
    Func<Style, TIdx, TValue> getter,
    Action<Style, TIdx, TValue> setter,
    TIdx idx,
    TValue value)
    {
        var style = node.Style;
        if (!EqualityComparer<TValue>.Default.Equals(getter(style, idx), value))
        {
            setter(style, idx, value);
            node.MarkDirtyAndPropagate();
        }
    }

    // Helper: update an enum-style property
    private static void UpdateStyleEnum<TEnum>(
        Node node,
        Func<Style, TEnum> getter,
        Action<Style, TEnum> setter,
        TEnum value)
        where TEnum : struct
    {
        var style = node.Style;
        if (!getter(style).Equals(value))
        {
            setter(style, value);
            node.MarkDirtyAndPropagate();
        }
    }

    // --- CopyStyle ---

    public static void YGNodeCopyStyle(Node dstNode, Node srcNode)
    {
        if (!dstNode.Style.Equals(srcNode.Style))
        {
            dstNode.SetStyle(srcNode.Style);
            dstNode.MarkDirtyAndPropagate();
        }
    }

    // --- Direction ---

    public static void YGNodeStyleSetDirection(Node node, YGDirection value)
    {
        UpdateStyleEnum(node,
            s => s.Direction,
            (s, v) => s.Direction = v,
            value.ToInternal());
    }

    public static YGDirection YGNodeStyleGetDirection(Node node)
    {
        return node.Style.Direction.ToYG();
    }

    // --- FlexDirection ---

    public static void YGNodeStyleSetFlexDirection(Node node, YGFlexDirection value)
    {
        UpdateStyleEnum(node,
            s => s.FlexDirection,
            (s, v) => s.FlexDirection = v,
            value.ToInternal());
    }

    public static YGFlexDirection YGNodeStyleGetFlexDirection(Node node)
    {
        return node.Style.FlexDirection.ToYG();
    }

    // --- JustifyContent ---

    public static void YGNodeStyleSetJustifyContent(Node node, YGJustify value)
    {
        UpdateStyleEnum(node,
            s => s.JustifyContent,
            (s, v) => s.JustifyContent = v,
            value.ToInternal());
    }

    public static YGJustify YGNodeStyleGetJustifyContent(Node node)
    {
        return node.Style.JustifyContent.ToYG();
    }

    // --- JustifyItems ---

    public static void YGNodeStyleSetJustifyItems(Node node, YGJustify value)
    {
        UpdateStyleEnum(node,
            s => s.JustifyItems,
            (s, v) => s.JustifyItems = v,
            value.ToInternal());
    }

    public static YGJustify YGNodeStyleGetJustifyItems(Node node)
    {
        return node.Style.JustifyItems.ToYG();
    }

    // --- JustifySelf ---

    public static void YGNodeStyleSetJustifySelf(Node node, YGJustify value)
    {
        UpdateStyleEnum(node,
            s => s.JustifySelf,
            (s, v) => s.JustifySelf = v,
            value.ToInternal());
    }

    public static YGJustify YGNodeStyleGetJustifySelf(Node node)
    {
        return node.Style.JustifySelf.ToYG();
    }

    // --- AlignContent ---

    public static void YGNodeStyleSetAlignContent(Node node, YGAlign value)
    {
        UpdateStyleEnum(node,
            s => s.AlignContent,
            (s, v) => s.AlignContent = v,
            value.ToInternal());
    }

    public static YGAlign YGNodeStyleGetAlignContent(Node node)
    {
        return node.Style.AlignContent.ToYG();
    }

    // --- AlignItems ---

    public static void YGNodeStyleSetAlignItems(Node node, YGAlign value)
    {
        UpdateStyleEnum(node,
            s => s.AlignItems,
            (s, v) => s.AlignItems = v,
            value.ToInternal());
    }

    public static YGAlign YGNodeStyleGetAlignItems(Node node)
    {
        return node.Style.AlignItems.ToYG();
    }

    // --- AlignSelf ---

    public static void YGNodeStyleSetAlignSelf(Node node, YGAlign value)
    {
        UpdateStyleEnum(node,
            s => s.AlignSelf,
            (s, v) => s.AlignSelf = v,
            value.ToInternal());
    }

    public static YGAlign YGNodeStyleGetAlignSelf(Node node)
    {
        return node.Style.AlignSelf.ToYG();
    }

    // --- PositionType ---

    public static void YGNodeStyleSetPositionType(Node node, YGPositionType value)
    {
        UpdateStyleEnum(node,
            s => s.PositionType,
            (s, v) => s.PositionType = v,
            value.ToInternal());
    }

    public static YGPositionType YGNodeStyleGetPositionType(Node node)
    {
        return node.Style.PositionType.ToYG();
    }

    // --- FlexWrap ---

    public static void YGNodeStyleSetFlexWrap(Node node, YGWrap value)
    {
        UpdateStyleEnum(node,
            s => s.FlexWrap,
            (s, v) => s.FlexWrap = v,
            value.ToInternal());
    }

    public static YGWrap YGNodeStyleGetFlexWrap(Node node)
    {
        return node.Style.FlexWrap.ToYG();
    }

    // --- Overflow ---

    public static void YGNodeStyleSetOverflow(Node node, YGOverflow value)
    {
        UpdateStyleEnum(node,
            s => s.Overflow,
            (s, v) => s.Overflow = v,
            value.ToInternal());
    }

    public static YGOverflow YGNodeStyleGetOverflow(Node node)
    {
        return node.Style.Overflow.ToYG();
    }

    // --- Display ---

    public static void YGNodeStyleSetDisplay(Node node, YGDisplay value)
    {
        bool wasContents = node.Style.Display == Display.Contents;
        UpdateStyleEnum(node,
            s => s.Display,
            (s, v) => s.Display = v,
            value.ToInternal());
        if (node.GetOwner() is { } owner && wasContents != (value == YGDisplay.Contents))
            owner.RefreshContentsChildrenCount();
    }

    public static YGDisplay YGNodeStyleGetDisplay(Node node)
    {
        return node.Style.Display.ToYG();
    }

    // --- Flex ---

    public static void YGNodeStyleSetFlex(Node node, float flex)
    {
        var newValue = new FloatOptional(flex);
        if (node.Style.Flex != newValue)
        {
            node.Style.Flex = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static float YGNodeStyleGetFlex(Node node)
    {
        return node.Style.Flex.IsUndefined()
            ? YogaConstants.Undefined
            : node.Style.Flex.Unwrap();
    }

    // --- FlexGrow ---

    public static void YGNodeStyleSetFlexGrow(Node node, float flexGrow)
    {
        var newValue = new FloatOptional(flexGrow);
        if (node.Style.FlexGrow != newValue)
        {
            node.Style.FlexGrow = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static float YGNodeStyleGetFlexGrow(Node node)
    {
        return node.Style.FlexGrow.IsUndefined()
            ? Style.DefaultFlexGrow
            : node.Style.FlexGrow.Unwrap();
    }

    // --- FlexShrink ---

    public static void YGNodeStyleSetFlexShrink(Node node, float flexShrink)
    {
        var newValue = new FloatOptional(flexShrink);
        if (node.Style.FlexShrink != newValue)
        {
            node.Style.FlexShrink = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static float YGNodeStyleGetFlexShrink(Node node)
    {
        return node.Style.FlexShrink.IsUndefined()
            ? (node.GetConfig()!.UseWebDefaults()
                ? Style.WebDefaultFlexShrink
                : Style.DefaultFlexShrink)
            : node.Style.FlexShrink.Unwrap();
    }

    // --- FlexBasis ---

    public static void YGNodeStyleSetFlexBasis(Node node, float flexBasis)
    {
        var newValue = StyleSizeLength.Points(flexBasis);
        if (node.Style.FlexBasis != newValue)
        {
            node.Style.FlexBasis = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetFlexBasisPercent(Node node, float flexBasisPercent)
    {
        var newValue = StyleSizeLength.Percent(flexBasisPercent);
        if (node.Style.FlexBasis != newValue)
        {
            node.Style.FlexBasis = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetFlexBasisAuto(Node node)
    {
        var newValue = StyleSizeLength.OfAuto();
        if (node.Style.FlexBasis != newValue)
        {
            node.Style.FlexBasis = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetFlexBasisMaxContent(Node node)
    {
        var newValue = StyleSizeLength.OfMaxContent();
        if (node.Style.FlexBasis != newValue)
        {
            node.Style.FlexBasis = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetFlexBasisFitContent(Node node)
    {
        var newValue = StyleSizeLength.OfFitContent();
        if (node.Style.FlexBasis != newValue)
        {
            node.Style.FlexBasis = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static void YGNodeStyleSetFlexBasisStretch(Node node)
    {
        var newValue = StyleSizeLength.OfStretch();
        if (node.Style.FlexBasis != newValue)
        {
            node.Style.FlexBasis = newValue;
            node.MarkDirtyAndPropagate();
        }
    }

    public static YGValue YGNodeStyleGetFlexBasis(Node node)
    {
        return node.Style.FlexBasis.ToYGValue();
    }

    // --- Position ---

    // Helper: convert YGGridTrackType to StyleSizeLength (for minmax)
    private static StyleSizeLength StyleSizeLengthFromTypeAndValue(YGGridTrackType type, float value)
    {
        return type switch
        {
            YGGridTrackType.Points => StyleSizeLength.Points(value),
            YGGridTrackType.Percent => StyleSizeLength.Percent(value),
            YGGridTrackType.Fr => StyleSizeLength.Stretch(value),
            YGGridTrackType.Auto => StyleSizeLength.OfAuto(),
            YGGridTrackType.Minmax => StyleSizeLength.OfAuto(),
            _ => throw new InvalidOperationException("Unknown YGGridTrackType"),
        };
    }

    private static void SetDimension(Node node, Dimension dim, StyleSizeLength value)
    {
        if (node.Style.Dimension(dim) != value)
        {
            node.Style.SetDimension(dim, value);
            node.MarkDirtyAndPropagate();
            node.ProcessDimensions();
        }
    }

    private static void SetMinDimension(Node node, Dimension dim, StyleSizeLength value)
    {
        if (node.Style.MinDimension(dim) != value)
        {
            node.Style.SetMinDimension(dim, value);
            node.MarkDirtyAndPropagate();
            node.ProcessDimensions();
        }
    }

    private static void SetMaxDimension(Node node, Dimension dim, StyleSizeLength value)
    {
        if (node.Style.MaxDimension(dim) != value)
        {
            node.Style.SetMaxDimension(dim, value);
            node.MarkDirtyAndPropagate();
            node.ProcessDimensions();
        }
    }
}
