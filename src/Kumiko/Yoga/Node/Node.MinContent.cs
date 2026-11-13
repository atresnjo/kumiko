namespace Facebook.Yoga;

internal delegate YGSize YGMinContentMeasureFunc(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode);

internal partial class Node
{
    private YGMinContentMeasureFunc? _minContentMeasureFunc;
    private FloatOptional _minContentWidth = FloatOptional.Undefined;
    private FloatOptional _minContentHeight = FloatOptional.Undefined;

    public bool HasMinContentMeasureFunc() => _minContentMeasureFunc is not null;
    public FloatOptional GetMinContentWidth() => _minContentWidth;
    public FloatOptional GetMinContentHeight() => _minContentHeight;

    public void SetMinContentMeasureFunc(YGMinContentMeasureFunc? value)
    {
        if (_minContentMeasureFunc == value)
            return;
        _minContentMeasureFunc = value;
        MarkDirtyAndPropagate();
    }

    public void SetMinContentWidth(FloatOptional value)
    {
        if (_minContentWidth == value)
            return;
        _minContentWidth = value;
        MarkDirtyAndPropagate();
    }

    public void SetMinContentHeight(FloatOptional value)
    {
        if (_minContentHeight == value)
            return;
        _minContentHeight = value;
        MarkDirtyAndPropagate();
    }

    public YGSize MeasureMinContent(float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var size = _minContentMeasureFunc!(this, width, widthMode, height, heightMode);
        size.Width = Comparison.MaxOrDefined(0, size.Width);
        size.Height = Comparison.MaxOrDefined(0, size.Height);
        return size;
    }

    private void CopyMinContentFrom(Node other)
    {
        _minContentMeasureFunc = other._minContentMeasureFunc;
        _minContentWidth = other._minContentWidth;
        _minContentHeight = other._minContentHeight;
    }

    private void ResetMinContent()
    {
        _minContentMeasureFunc = null;
        _minContentWidth = FloatOptional.Undefined;
        _minContentHeight = FloatOptional.Undefined;
    }
}
