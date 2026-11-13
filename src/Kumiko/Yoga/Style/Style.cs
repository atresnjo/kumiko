using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Facebook.Yoga;

internal partial class Style
{
    public const float DefaultFlexGrow = 0.0f;
    public const float DefaultFlexShrink = 0.0f;
    public const float WebDefaultFlexShrink = 1.0f;

    private Direction _direction = Direction.Inherit;
    private FlexDirection _flexDirection = FlexDirection.Column;
    private Justify _justifyContent = Justify.FlexStart;
    private Justify _justifyItems = Justify.Stretch;
    private Justify _justifySelf = Justify.Auto;
    private Align _alignContent = Align.FlexStart;
    private Align _alignItems = Align.Stretch;
    private Align _alignSelf = Align.Auto;
    private PositionType _positionType = PositionType.Relative;
    private Wrap _flexWrap = Wrap.NoWrap;
    private Overflow _overflow = Overflow.Visible;
    private Display _display = Display.Flex;
    private BoxSizing _boxSizing = BoxSizing.BorderBox;

    // Kumiko: values are stored directly rather than as handles into a compact value pool;
    // layout reads them many times per node. Stores normalize exactly as the pool did.
    private FloatOptional _flex = FloatOptional.Undefined;
    private FloatOptional _flexGrow = FloatOptional.Undefined;
    private FloatOptional _flexShrink = FloatOptional.Undefined;
    private StyleSizeLength _flexBasis = StyleSizeLength.OfAuto();
    private readonly StyleLength[] _margin = UndefinedLengths(9);
    private readonly StyleLength[] _position = UndefinedLengths(9);
    private readonly StyleLength[] _padding = UndefinedLengths(9);
    private readonly StyleLength[] _border = UndefinedLengths(9);
    private readonly StyleLength[] _gap = UndefinedLengths(3);
    private readonly StyleSizeLength[] _dimensions = [StyleSizeLength.OfAuto(), StyleSizeLength.OfAuto()];
    private readonly StyleSizeLength[] _minDimensions = [StyleSizeLength.Undefined(), StyleSizeLength.Undefined()];
    private readonly StyleSizeLength[] _maxDimensions = [StyleSizeLength.Undefined(), StyleSizeLength.Undefined()];
    private FloatOptional _aspectRatio = FloatOptional.Undefined;

    // Grid properties
    private GridTrackList? _gridTemplateColumns;
    private GridTrackList? _gridTemplateRows;
    private GridTrackList? _gridAutoColumns;
    private GridTrackList? _gridAutoRows;
    private GridLine _gridColumnStart = new GridLine();
    private GridLine _gridColumnEnd = new GridLine();
    private GridLine _gridRowStart = new GridLine();
    private GridLine _gridRowEnd = new GridLine();

    private static StyleLength[] UndefinedLengths(int count)
    {
        var values = new StyleLength[count];
        Array.Fill(values, StyleLength.Undefined());
        return values;
    }

    private static StyleLength Normalize(StyleLength value) => value.IsUndefined() ? StyleLength.Undefined() : value;

    // Keyword sizes carry no value; a stretch fraction is stored as the plain keyword.
    private static StyleSizeLength Normalize(StyleSizeLength value) =>
        value.IsUndefined() ? StyleSizeLength.Undefined() :
        value.IsAuto() ? StyleSizeLength.OfAuto() :
        value.IsMaxContent() ? StyleSizeLength.OfMaxContent() :
        value.IsStretch() ? StyleSizeLength.OfStretch() :
        value.IsFitContent() ? StyleSizeLength.OfFitContent() : value;

    private static FloatOptional Normalize(FloatOptional value) => value.IsUndefined() ? FloatOptional.Undefined : value;

    public Direction Direction
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _direction;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _direction = value;
    }

    public FlexDirection FlexDirection
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _flexDirection;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _flexDirection = value;
    }

    public Justify JustifyContent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _justifyContent;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _justifyContent = value;
    }

    public Justify JustifyItems
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _justifyItems;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _justifyItems = value;
    }

    public Justify JustifySelf
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _justifySelf;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _justifySelf = value;
    }

    public Align AlignContent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _alignContent;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _alignContent = value;
    }

    public Align AlignItems
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _alignItems;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _alignItems = value;
    }

    public Align AlignSelf
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _alignSelf;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _alignSelf = value;
    }

    public PositionType PositionType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _positionType;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _positionType = value;
    }

    public Wrap FlexWrap
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _flexWrap;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _flexWrap = value;
    }

    public Overflow Overflow
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _overflow;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _overflow = value;
    }

    public Display Display
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _display;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _display = value;
    }

    public FloatOptional Flex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _flex;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Store(ref _flex, value);
    }

    public FloatOptional FlexGrow
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _flexGrow;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Store(ref _flexGrow, value);
    }

    public FloatOptional FlexShrink
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _flexShrink;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Store(ref _flexShrink, value);
    }

    public StyleSizeLength FlexBasis
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _flexBasis;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Store(ref _flexBasis, value);
    }

    public StyleLength Margin(Edge edge)
    {
        return _margin[YogaEnums.ToUnderlying(edge)];
    }

    public void SetMargin(Edge edge, StyleLength value)
    {
        Store(ref _margin[YogaEnums.ToUnderlying(edge)], value);
    }

    public StyleLength Position(Edge edge)
    {
        return _position[YogaEnums.ToUnderlying(edge)];
    }

    public void SetPosition(Edge edge, StyleLength value)
    {
        Store(ref _position[YogaEnums.ToUnderlying(edge)], value);
    }

    public StyleLength Padding(Edge edge)
    {
        return _padding[YogaEnums.ToUnderlying(edge)];
    }

    public void SetPadding(Edge edge, StyleLength value)
    {
        Store(ref _padding[YogaEnums.ToUnderlying(edge)], value);
    }

    public StyleLength Border(Edge edge)
    {
        return _border[YogaEnums.ToUnderlying(edge)];
    }

    public void SetBorder(Edge edge, StyleLength value)
    {
        Store(ref _border[YogaEnums.ToUnderlying(edge)], value);
    }

    public StyleLength Gap(Gutter gutter)
    {
        return _gap[YogaEnums.ToUnderlying(gutter)];
    }

    public void SetGap(Gutter gutter, StyleLength value)
    {
        Store(ref _gap[YogaEnums.ToUnderlying(gutter)], value);
    }

    public StyleSizeLength Dimension(Dimension axis)
    {
        return _dimensions[YogaEnums.ToUnderlying(axis)];
    }

    public void SetDimension(Dimension axis, StyleSizeLength value)
    {
        Store(ref _dimensions[YogaEnums.ToUnderlying(axis)], value);
    }

    public StyleSizeLength MinDimension(Dimension axis)
    {
        return _minDimensions[YogaEnums.ToUnderlying(axis)];
    }

    public void SetMinDimension(Dimension axis, StyleSizeLength value)
    {
        Store(ref _minDimensions[YogaEnums.ToUnderlying(axis)], value);
    }

    // Grid Container Properties
    public GridTrackList GridTemplateColumns
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridTemplateColumns ??= new GridTrackList();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridTemplateColumns = value;
    }

    public void ResizeGridTemplateColumns(int count)
    {
        GridTemplateColumns.Resize(count);
    }

    public void SetGridTemplateColumnAt(int index, GridTrackSize value)
    {
        GridTemplateColumns[index] = value;
    }

    public GridTrackList GridTemplateRows
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridTemplateRows ??= new GridTrackList();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridTemplateRows = value;
    }

    public void ResizeGridTemplateRows(int count)
    {
        GridTemplateRows.Resize(count);
    }

    public void SetGridTemplateRowAt(int index, GridTrackSize value)
    {
        GridTemplateRows[index] = value;
    }

    public GridTrackList GridAutoColumns
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridAutoColumns ??= new GridTrackList();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridAutoColumns = value;
    }

    public void ResizeGridAutoColumns(int count)
    {
        GridAutoColumns.Resize(count);
    }

    public void SetGridAutoColumnAt(int index, GridTrackSize value)
    {
        GridAutoColumns[index] = value;
    }

    public GridTrackList GridAutoRows
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridAutoRows ??= new GridTrackList();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridAutoRows = value;
    }

    public void ResizeGridAutoRows(int count)
    {
        GridAutoRows.Resize(count);
    }

    public void SetGridAutoRowAt(int index, GridTrackSize value)
    {
        GridAutoRows[index] = value;
    }

    // Grid Item Properties
    public GridLine GridColumnStart
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridColumnStart;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridColumnStart = value;
    }

    public GridLine GridColumnEnd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridColumnEnd;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridColumnEnd = value;
    }

    public GridLine GridRowStart
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridRowStart;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridRowStart = value;
    }

    public GridLine GridRowEnd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _gridRowEnd;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gridRowEnd = value;
    }

    public StyleSizeLength MaxDimension(Dimension axis)
    {
        return _maxDimensions[YogaEnums.ToUnderlying(axis)];
    }

    public void SetMaxDimension(Dimension axis, StyleSizeLength value)
    {
        Store(ref _maxDimensions[YogaEnums.ToUnderlying(axis)], value);
    }

    public FloatOptional AspectRatio
    {
        get => _aspectRatio;
        set
        {
            // degenerate aspect ratios act as auto.
            // see https://drafts.csswg.org/css-sizing-4/#valdef-aspect-ratio-ratio
            Store(ref _aspectRatio, value == 0.0f || float.IsInfinity(value.Unwrap()) ? FloatOptional.Undefined : value);
        }
    }

    public BoxSizing BoxSizing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _boxSizing;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _boxSizing = value;
    }

    public Style Clone()
    {
        var clone = new Style();
        clone._direction = _direction;
        clone._flexDirection = _flexDirection;
        clone._justifyContent = _justifyContent;
        clone._justifyItems = _justifyItems;
        clone._justifySelf = _justifySelf;
        clone._alignContent = _alignContent;
        clone._alignItems = _alignItems;
        clone._alignSelf = _alignSelf;
        clone._positionType = _positionType;
        clone._flexWrap = _flexWrap;
        clone._overflow = _overflow;
        clone._display = _display;
        clone._boxSizing = _boxSizing;

        clone._flex = _flex;
        clone._flexGrow = _flexGrow;
        clone._flexShrink = _flexShrink;
        clone._flexBasis = _flexBasis;
        clone._aspectRatio = _aspectRatio;

        Array.Copy(_margin, clone._margin, _margin.Length);
        Array.Copy(_position, clone._position, _position.Length);
        Array.Copy(_padding, clone._padding, _padding.Length);
        Array.Copy(_border, clone._border, _border.Length);
        Array.Copy(_gap, clone._gap, _gap.Length);
        Array.Copy(_dimensions, clone._dimensions, _dimensions.Length);
        Array.Copy(_minDimensions, clone._minDimensions, _minDimensions.Length);
        Array.Copy(_maxDimensions, clone._maxDimensions, _maxDimensions.Length);

        clone._gridTemplateColumns = _gridTemplateColumns is null ? null : new GridTrackList(_gridTemplateColumns);
        clone._gridTemplateRows = _gridTemplateRows is null ? null : new GridTrackList(_gridTemplateRows);
        clone._gridAutoColumns = _gridAutoColumns is null ? null : new GridTrackList(_gridAutoColumns);
        clone._gridAutoRows = _gridAutoRows is null ? null : new GridTrackList(_gridAutoRows);
        clone._gridColumnStart = _gridColumnStart;
        clone._gridColumnEnd = _gridColumnEnd;
        clone._gridRowStart = _gridRowStart;
        clone._gridRowEnd = _gridRowEnd;

        return clone;
    }

    private static bool GridTracksEqual(GridTrackList? a, GridTrackList? b) =>
        ReferenceEquals(a, b) || (a?.Count ?? 0) == (b?.Count ?? 0) &&
        (a is null || b is null || System.Linq.Enumerable.SequenceEqual(a, b));

    public override bool Equals(object? obj)
    {
        if (obj is not Style other)
            return false;

        return _direction == other._direction &&
               _flexDirection == other._flexDirection &&
               _justifyContent == other._justifyContent &&
               _justifyItems == other._justifyItems &&
               _justifySelf == other._justifySelf &&
               _alignContent == other._alignContent &&
               _alignItems == other._alignItems &&
               _alignSelf == other._alignSelf &&
               _positionType == other._positionType &&
               _flexWrap == other._flexWrap &&
               _overflow == other._overflow &&
               _display == other._display &&
               _boxSizing == other._boxSizing &&
               _flex == other._flex &&
               _flexGrow == other._flexGrow &&
               _flexShrink == other._flexShrink &&
               _flexBasis == other._flexBasis &&
               _margin.AsSpan().SequenceEqual(other._margin) &&
               _position.AsSpan().SequenceEqual(other._position) &&
               _padding.AsSpan().SequenceEqual(other._padding) &&
               _border.AsSpan().SequenceEqual(other._border) &&
               _gap.AsSpan().SequenceEqual(other._gap) &&
               _dimensions.AsSpan().SequenceEqual(other._dimensions) &&
               _minDimensions.AsSpan().SequenceEqual(other._minDimensions) &&
               _maxDimensions.AsSpan().SequenceEqual(other._maxDimensions) &&
               _aspectRatio == other._aspectRatio &&
               GridTracksEqual(_gridTemplateColumns, other._gridTemplateColumns) &&
               GridTracksEqual(_gridTemplateRows, other._gridTemplateRows) &&
               GridTracksEqual(_gridAutoColumns, other._gridAutoColumns) &&
               GridTracksEqual(_gridAutoRows, other._gridAutoRows) &&
               _gridColumnStart.Equals(other._gridColumnStart) &&
               _gridColumnEnd.Equals(other._gridColumnEnd) &&
               _gridRowStart.Equals(other._gridRowStart) &&
               _gridRowEnd.Equals(other._gridRowEnd);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_direction);
        hash.Add(_flexDirection);
        hash.Add(_justifyContent);
        hash.Add(_justifyItems);
        hash.Add(_justifySelf);
        hash.Add(_alignContent);
        hash.Add(_alignItems);
        hash.Add(_alignSelf);
        hash.Add(_positionType);
        hash.Add(_flexWrap);
        hash.Add(_overflow);
        hash.Add(_display);
        hash.Add(_boxSizing);
        return hash.ToHashCode();
    }

    private static void Store(ref StyleLength slot, StyleLength value) => slot = Normalize(value);

    private static void Store(ref StyleSizeLength slot, StyleSizeLength value) => slot = Normalize(value);

    private static void Store(ref FloatOptional slot, FloatOptional value) => slot = Normalize(value);
}
