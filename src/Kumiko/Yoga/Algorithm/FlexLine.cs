using System;
using System.Collections.Generic;

namespace Facebook.Yoga;


internal struct FlexLineRunningLayout
{
    public float TotalFlexGrowFactors;
    public float TotalFlexShrinkScaledFactors;
    public float RemainingFreeSpace;
    public float MainDim;
    public float CrossDim;
    public float Gap;
    public float MaxBaseline;

    public FlexLineRunningLayout(float totalFlexGrowFactors, float totalFlexShrinkScaledFactors)
    {
        TotalFlexGrowFactors = totalFlexGrowFactors;
        TotalFlexShrinkScaledFactors = totalFlexShrinkScaledFactors;
        RemainingFreeSpace = 0.0f;
        MainDim = 0.0f;
        CrossDim = 0.0f;
        Gap = 0.0f;
        MaxBaseline = 0.0f;
    }
}

// Kumiko: flex lines and their child lists are reused across layout passes.
internal sealed class FlexLine
{
    public readonly List<Node> ItemsInFlow = new();
    public float SizeConsumed;
    public int NumberOfAutoMargins;
    public FlexLineRunningLayout Layout;
}

// Per-call scratch for CalculateLayoutImpl. Rented as a stack so measure callbacks may
// re-enter layout for another tree on the same thread.
internal sealed class FlexLayoutScratch
{
    [ThreadStatic]
    private static Stack<FlexLayoutScratch>? s_pool;

    public readonly List<Node> Children = new();
    public readonly FlexLine Line = new();

    public static FlexLayoutScratch Rent()
    {
        var pool = s_pool;
        return pool is { Count: > 0 } ? pool.Pop() : new FlexLayoutScratch();
    }

    public static void Return(FlexLayoutScratch scratch)
    {
        scratch.Children.Clear();
        scratch.Line.ItemsInFlow.Clear();
        (s_pool ??= new()).Push(scratch);
    }
}

internal static partial class YogaAlgorithm
{
    // Fills line from children[startIndex..] and returns the first index of the next line.
    public static int CalculateFlexLine(
        FlexLine line,
        Node node,
        Direction ownerDirection,
        float ownerWidth,
        float mainAxisOwnerSize,
        float availableInnerWidth,
        float availableInnerMainDim,
        List<Node> children,
        int startIndex,
        int lineCount)
    {
        var itemsInFlow = line.ItemsInFlow;
        itemsInFlow.Clear();

        float sizeConsumed = 0.0f;
        float totalFlexGrowFactors = 0.0f;
        float totalFlexShrinkScaledFactors = 0.0f;
        int numberOfAutoMargins = 0;
        Node? firstElementInLine = null;

        float sizeConsumedIncludingMinConstraint = 0;
        Direction direction = node.ResolveDirection(ownerDirection);
        FlexDirection mainAxis = node.Style.FlexDirection.ResolveDirection(direction);
        bool isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;
        float gap = node.Style.ComputeGapForAxis(mainAxis, availableInnerMainDim);

        int index = startIndex;
        for (; index < children.Count; index++)
        {
            var child = children[index];
            if (child.Style.Display == Display.None ||
                child.Style.PositionType == PositionType.Absolute)
            {
                continue;
            }

            if (firstElementInLine == null)
            {
                firstElementInLine = child;
            }

            if (child.Style.FlexStartMarginIsAuto(mainAxis, ownerDirection))
            {
                numberOfAutoMargins++;
            }
            if (child.Style.FlexEndMarginIsAuto(mainAxis, ownerDirection))
            {
                numberOfAutoMargins++;
            }

            child.SetLineIndex((nuint)lineCount);
            float childMarginMainAxis = child.Style.ComputeMarginForAxis(mainAxis, availableInnerWidth);
            float childLeadingGapMainAxis = child == firstElementInLine ? 0.0f : gap;

            float flexBasisWithMinAndMaxConstraints = BoundAxis.BoundAxisWithinMinAndMax(
                child,
                direction,
                mainAxis,
                child.Layout.ComputedFlexBasis,
                mainAxisOwnerSize,
                ownerWidth).Unwrap();

            float requiredMainDim = sizeConsumedIncludingMinConstraint + flexBasisWithMinAndMaxConstraints +
                childMarginMainAxis + childLeadingGapMainAxis;
            if (requiredMainDim > availableInnerMainDim &&
                !Comparison.InexactEquals(requiredMainDim, availableInnerMainDim) &&
                isNodeFlexWrap && itemsInFlow.Count > 0)
            {
                break;
            }

            sizeConsumedIncludingMinConstraint += flexBasisWithMinAndMaxConstraints +
                childMarginMainAxis + childLeadingGapMainAxis;
            sizeConsumed += flexBasisWithMinAndMaxConstraints + childMarginMainAxis +
                childLeadingGapMainAxis;

            if (child.IsNodeFlexible())
            {
                totalFlexGrowFactors += child.ResolveFlexGrow();
                totalFlexShrinkScaledFactors += -child.ResolveFlexShrink() *
                    child.Layout.ComputedFlexBasis.Unwrap();
            }

            itemsInFlow.Add(child);
        }

        if (totalFlexGrowFactors > 0 && totalFlexGrowFactors < 1)
        {
            totalFlexGrowFactors = 1;
        }

        if (totalFlexShrinkScaledFactors > 0 && totalFlexShrinkScaledFactors < 1)
        {
            totalFlexShrinkScaledFactors = 1;
        }

        line.SizeConsumed = sizeConsumed;
        line.NumberOfAutoMargins = numberOfAutoMargins;
        line.Layout = new FlexLineRunningLayout(totalFlexGrowFactors, totalFlexShrinkScaledFactors);
        return index;
    }
}

