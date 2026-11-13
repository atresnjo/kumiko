using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Facebook.Yoga;

internal partial class Node
{
    public void MarkDirtyAndPropagate()
    {
        // A boundary dirtied only from inside still owes its owner this notification.
        if (_isDirty && !_dirtyInsideBoundary)
            return;
        _dirtyInsideBoundary = false;
        SetDirty(true);
        SetLayoutComputedFlexBasis(FloatOptional.Undefined);
        _owner?.MarkDirtyFromChild();
    }

    // Kumiko relayout boundaries. An absolutely positioned flex box with point width and height
    // has a size and position independent of its content, and absolute boxes never contribute
    // to an ancestor's intrinsic size or baseline. Descendant changes therefore stop here and
    // the box is laid out again alone, with the constraints of its last layout, after the root
    // pass. Pixel-grid rounding needs ancestor offsets, so boundaries require an unrounded grid.
    private bool _dirtyInsideBoundary;
    private List<Node>? _pendingBoundaries;

    public bool IsRelayoutBoundary()
    {
        return _owner != null &&
            _config is { RelayoutBoundaries: true } config &&
            config.GetPointScaleFactor() == 0 &&
            _style.PositionType == PositionType.Absolute &&
            _style.Display == Display.Flex &&
            _style.Dimension(Dimension.Width).IsPoints() &&
            _style.Dimension(Dimension.Height).IsPoints() &&
            _layout.CachedLayout.AvailableWidth >= 0 &&
            !float.IsNaN(_layout.LastLayoutOwnerWidth);
    }

    private void MarkDirtyFromChild()
    {
        if (!IsRelayoutBoundary())
        {
            MarkDirtyAndPropagate();
            return;
        }
        if (_isDirty)
            return;
        SetDirty(true);
        _dirtyInsideBoundary = true;
        var root = this;
        while (root._owner is { } owner)
            root = owner;
        (root._pendingBoundaries ??= new()).Add(this);
    }

    // Lays out boundaries that the root pass did not reach, outermost requests first.
    internal void LayoutPendingBoundaries(uint generationCount, ref LayoutData markerData)
    {
        if (_pendingBoundaries is not { } pending)
            return;
        for (var i = 0; i < pending.Count; i++)
        {
            var boundary = pending[i];
            if (!boundary._dirtyInsideBoundary || !boundary.IsRelayoutBoundary())
                continue;
            uint depth = 0;
            var root = boundary;
            for (; root._owner is { } owner; root = owner)
                depth++;
            if (root != this)
                continue;
            var layout = boundary._layout;
            var cached = layout.CachedLayout;
            LayoutAlgorithm.CalculateLayoutInternal(boundary, cached.AvailableWidth, cached.AvailableHeight,
                layout.LastOwnerDirection, cached.WidthSizingMode, cached.HeightSizingMode,
                layout.LastLayoutOwnerWidth, layout.LastLayoutOwnerHeight, true, LayoutPassReason.Initial,
                ref markerData, depth, generationCount);
        }
        pending.Clear();
    }
}
