using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Facebook.Yoga;

internal partial class Style
{
    public FloatOptional ResolvedMinDimension(
        Direction direction,
        Dimension axis,
        float referenceLength,
        float ownerWidth)
    {
        FloatOptional value = MinDimension(axis).Resolve(referenceLength);
        if (this.BoxSizing == BoxSizing.BorderBox)
        {
            return value;
        }

        FloatOptional dimensionPaddingAndBorder = new FloatOptional(
            ComputePaddingAndBorderForDimension(direction, axis, ownerWidth));

        return value + (dimensionPaddingAndBorder.IsDefined() ? dimensionPaddingAndBorder : FloatOptional.Zero);
    }

    public FloatOptional ResolvedMaxDimension(
        Direction direction,
        Dimension axis,
        float referenceLength,
        float ownerWidth)
    {
        FloatOptional value = MaxDimension(axis).Resolve(referenceLength);
        if (this.BoxSizing == BoxSizing.BorderBox)
        {
            return value;
        }

        FloatOptional dimensionPaddingAndBorder = new FloatOptional(
            ComputePaddingAndBorderForDimension(direction, axis, ownerWidth));

        return value + (dimensionPaddingAndBorder.IsDefined() ? dimensionPaddingAndBorder : FloatOptional.Zero);
    }

    public bool HorizontalInsetsDefined()
    {
        return _position[YogaEnums.ToUnderlying(Edge.Left)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.Right)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.All)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.Horizontal)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.Start)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.End)].IsDefined();
    }

    public bool VerticalInsetsDefined()
    {
        return _position[YogaEnums.ToUnderlying(Edge.Top)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.Bottom)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.All)].IsDefined() ||
               _position[YogaEnums.ToUnderlying(Edge.Vertical)].IsDefined();
    }

    public bool IsFlexStartPositionDefined(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.FlexStartEdge(), direction).IsDefined();
    }

    public bool IsFlexStartPositionAuto(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.FlexStartEdge(), direction).IsAuto();
    }

    public bool IsInlineStartPositionDefined(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.InlineStartEdge(direction), direction).IsDefined();
    }

    public bool IsInlineStartPositionAuto(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.InlineStartEdge(direction), direction).IsAuto();
    }

    public bool IsFlexEndPositionDefined(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.FlexEndEdge(), direction).IsDefined();
    }

    public bool IsFlexEndPositionAuto(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.FlexEndEdge(), direction).IsAuto();
    }

    public bool IsInlineEndPositionDefined(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.InlineEndEdge(direction), direction).IsDefined();
    }

    public bool IsInlineEndPositionAuto(FlexDirection axis, Direction direction)
    {
        return ComputePosition(axis.InlineEndEdge(direction), direction).IsAuto();
    }

    public float ComputeFlexStartPosition(FlexDirection axis, Direction direction, float axisSize)
    {
        return ComputePosition(axis.FlexStartEdge(), direction)
            .Resolve(axisSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeInlineStartPosition(FlexDirection axis, Direction direction, float axisSize)
    {
        return ComputePosition(axis.InlineStartEdge(direction), direction)
            .Resolve(axisSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeFlexEndPosition(FlexDirection axis, Direction direction, float axisSize)
    {
        return ComputePosition(axis.FlexEndEdge(), direction)
            .Resolve(axisSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeInlineEndPosition(FlexDirection axis, Direction direction, float axisSize)
    {
        return ComputePosition(axis.InlineEndEdge(direction), direction)
            .Resolve(axisSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeFlexStartMargin(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeMargin(axis.FlexStartEdge(), direction)
            .Resolve(widthSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeInlineStartMargin(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeMargin(axis.InlineStartEdge(direction), direction)
            .Resolve(widthSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeFlexEndMargin(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeMargin(axis.FlexEndEdge(), direction)
            .Resolve(widthSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeInlineEndMargin(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeMargin(axis.InlineEndEdge(direction), direction)
            .Resolve(widthSize)
            .UnwrapOrDefault(0.0f);
    }

    public float ComputeFlexStartBorder(FlexDirection axis, Direction direction)
    {
        return Comparison.MaxOrDefined(
            ComputeBorder(axis.FlexStartEdge(), direction).Resolve(0.0f).UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeInlineStartBorder(FlexDirection axis, Direction direction)
    {
        return Comparison.MaxOrDefined(
            ComputeBorder(axis.InlineStartEdge(direction), direction)
                .Resolve(0.0f)
                .UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeFlexEndBorder(FlexDirection axis, Direction direction)
    {
        return Comparison.MaxOrDefined(
            ComputeBorder(axis.FlexEndEdge(), direction).Resolve(0.0f).UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeInlineEndBorder(FlexDirection axis, Direction direction)
    {
        return Comparison.MaxOrDefined(
            ComputeBorder(axis.InlineEndEdge(direction), direction)
                .Resolve(0.0f)
                .UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeFlexStartPadding(FlexDirection axis, Direction direction, float widthSize)
    {
        return Comparison.MaxOrDefined(
            ComputePadding(axis.FlexStartEdge(), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeInlineStartPadding(FlexDirection axis, Direction direction, float widthSize)
    {
        return Comparison.MaxOrDefined(
            ComputePadding(axis.InlineStartEdge(direction), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeFlexEndPadding(FlexDirection axis, Direction direction, float widthSize)
    {
        return Comparison.MaxOrDefined(
            ComputePadding(axis.FlexEndEdge(), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeInlineEndPadding(FlexDirection axis, Direction direction, float widthSize)
    {
        return Comparison.MaxOrDefined(
            ComputePadding(axis.InlineEndEdge(direction), direction)
                .Resolve(widthSize)
                .UnwrapOrDefault(0.0f),
            0.0f);
    }

    public float ComputeInlineStartPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeInlineStartPadding(axis, direction, widthSize) +
               ComputeInlineStartBorder(axis, direction);
    }

    public float ComputeFlexStartPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeFlexStartPadding(axis, direction, widthSize) +
               ComputeFlexStartBorder(axis, direction);
    }

    public float ComputeInlineEndPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeInlineEndPadding(axis, direction, widthSize) +
               ComputeInlineEndBorder(axis, direction);
    }

    public float ComputeFlexEndPaddingAndBorder(FlexDirection axis, Direction direction, float widthSize)
    {
        return ComputeFlexEndPadding(axis, direction, widthSize) +
               ComputeFlexEndBorder(axis, direction);
    }

    public float ComputePaddingAndBorderForDimension(Direction direction, Dimension dimension, float widthSize)
    {
        FlexDirection flexDirectionForDimension = dimension == Facebook.Yoga.Dimension.Width
            ? FlexDirection.Row
            : FlexDirection.Column;

        return ComputeFlexStartPaddingAndBorder(flexDirectionForDimension, direction, widthSize) +
               ComputeFlexEndPaddingAndBorder(flexDirectionForDimension, direction, widthSize);
    }

    public float ComputeBorderForAxis(FlexDirection axis)
    {
        return ComputeInlineStartBorder(axis, Direction.LTR) +
               ComputeInlineEndBorder(axis, Direction.LTR);
    }

    public float ComputeMarginForAxis(FlexDirection axis, float widthSize)
    {
        // The total margin for a given axis does not depend on the direction
        // so hardcoding LTR here to avoid piping direction to this function
        return ComputeInlineStartMargin(axis, Direction.LTR, widthSize) +
               ComputeInlineEndMargin(axis, Direction.LTR, widthSize);
    }

    public float ComputeGapForAxis(FlexDirection axis, float ownerSize)
    {
        var gap = axis.IsRow() ? ComputeColumnGap() : ComputeRowGap();
        return Comparison.MaxOrDefined(gap.Resolve(ownerSize).UnwrapOrDefault(0.0f), 0.0f);
    }

    public float ComputeGapForDimension(Dimension dimension, float ownerSize)
    {
        var gap = dimension == Facebook.Yoga.Dimension.Width ? ComputeColumnGap() : ComputeRowGap();
        return Comparison.MaxOrDefined(gap.Resolve(ownerSize).UnwrapOrDefault(0.0f), 0.0f);
    }

    public bool FlexStartMarginIsAuto(FlexDirection axis, Direction direction)
    {
        return ComputeMargin(axis.FlexStartEdge(), direction).IsAuto();
    }

    public bool FlexEndMarginIsAuto(FlexDirection axis, Direction direction)
    {
        return ComputeMargin(axis.FlexEndEdge(), direction).IsAuto();
    }

    public bool InlineStartMarginIsAuto(FlexDirection axis, Direction direction)
    {
        return ComputeMargin(axis.InlineStartEdge(direction), direction).IsAuto();
    }

    public bool InlineEndMarginIsAuto(FlexDirection axis, Direction direction)
    {
        return ComputeMargin(axis.InlineEndEdge(direction), direction).IsAuto();
    }

    private StyleLength ComputeColumnGap()
    {
        if (_gap[YogaEnums.ToUnderlying(Gutter.Column)].IsDefined())
        {
            return _gap[YogaEnums.ToUnderlying(Gutter.Column)];
        }
        else
        {
            return _gap[YogaEnums.ToUnderlying(Gutter.All)];
        }
    }

    private StyleLength ComputeRowGap()
    {
        if (_gap[YogaEnums.ToUnderlying(Gutter.Row)].IsDefined())
        {
            return _gap[YogaEnums.ToUnderlying(Gutter.Row)];
        }
        else
        {
            return _gap[YogaEnums.ToUnderlying(Gutter.All)];
        }
    }

    private static StyleLength ComputeLeftEdge(StyleLength[] edges, Direction layoutDirection)
    {
        if (layoutDirection == Direction.LTR &&
            edges[YogaEnums.ToUnderlying(Edge.Start)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Start)];
        }
        else if (layoutDirection == Direction.RTL &&
                 edges[YogaEnums.ToUnderlying(Edge.End)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.End)];
        }
        else if (edges[YogaEnums.ToUnderlying(Edge.Left)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Left)];
        }
        else if (edges[YogaEnums.ToUnderlying(Edge.Horizontal)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Horizontal)];
        }
        else
        {
            return edges[YogaEnums.ToUnderlying(Edge.All)];
        }
    }

    private static StyleLength ComputeTopEdge(StyleLength[] edges)
    {
        if (edges[YogaEnums.ToUnderlying(Edge.Top)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Top)];
        }
        else if (edges[YogaEnums.ToUnderlying(Edge.Vertical)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Vertical)];
        }
        else
        {
            return edges[YogaEnums.ToUnderlying(Edge.All)];
        }
    }

    private static StyleLength ComputeRightEdge(StyleLength[] edges, Direction layoutDirection)
    {
        if (layoutDirection == Direction.LTR &&
            edges[YogaEnums.ToUnderlying(Edge.End)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.End)];
        }
        else if (layoutDirection == Direction.RTL &&
                 edges[YogaEnums.ToUnderlying(Edge.Start)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Start)];
        }
        else if (edges[YogaEnums.ToUnderlying(Edge.Right)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Right)];
        }
        else if (edges[YogaEnums.ToUnderlying(Edge.Horizontal)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Horizontal)];
        }
        else
        {
            return edges[YogaEnums.ToUnderlying(Edge.All)];
        }
    }

    private static StyleLength ComputeBottomEdge(StyleLength[] edges)
    {
        if (edges[YogaEnums.ToUnderlying(Edge.Bottom)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Bottom)];
        }
        else if (edges[YogaEnums.ToUnderlying(Edge.Vertical)].IsDefined())
        {
            return edges[YogaEnums.ToUnderlying(Edge.Vertical)];
        }
        else
        {
            return edges[YogaEnums.ToUnderlying(Edge.All)];
        }
    }

    private StyleLength ComputePosition(PhysicalEdge edge, Direction direction)
    {
        switch (edge)
        {
            case PhysicalEdge.Left:
                return ComputeLeftEdge(_position, direction);
            case PhysicalEdge.Top:
                return ComputeTopEdge(_position);
            case PhysicalEdge.Right:
                return ComputeRightEdge(_position, direction);
            case PhysicalEdge.Bottom:
                return ComputeBottomEdge(_position);
            default:
                throw new InvalidOperationException("Invalid physical edge");
        }
    }

    private StyleLength ComputeMargin(PhysicalEdge edge, Direction direction)
    {
        switch (edge)
        {
            case PhysicalEdge.Left:
                return ComputeLeftEdge(_margin, direction);
            case PhysicalEdge.Top:
                return ComputeTopEdge(_margin);
            case PhysicalEdge.Right:
                return ComputeRightEdge(_margin, direction);
            case PhysicalEdge.Bottom:
                return ComputeBottomEdge(_margin);
            default:
                throw new InvalidOperationException("Invalid physical edge");
        }
    }

    private StyleLength ComputePadding(PhysicalEdge edge, Direction direction)
    {
        switch (edge)
        {
            case PhysicalEdge.Left:
                return ComputeLeftEdge(_padding, direction);
            case PhysicalEdge.Top:
                return ComputeTopEdge(_padding);
            case PhysicalEdge.Right:
                return ComputeRightEdge(_padding, direction);
            case PhysicalEdge.Bottom:
                return ComputeBottomEdge(_padding);
            default:
                throw new InvalidOperationException("Invalid physical edge");
        }
    }

    private StyleLength ComputeBorder(PhysicalEdge edge, Direction direction)
    {
        switch (edge)
        {
            case PhysicalEdge.Left:
                return ComputeLeftEdge(_border, direction);
            case PhysicalEdge.Top:
                return ComputeTopEdge(_border);
            case PhysicalEdge.Right:
                return ComputeRightEdge(_border, direction);
            case PhysicalEdge.Bottom:
                return ComputeBottomEdge(_border);
            default:
                throw new InvalidOperationException("Invalid physical edge");
        }
    }
}
