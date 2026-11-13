namespace Facebook.Yoga;

internal partial class Style
{
    // Margins and padding on both axes resolve against owner width.
    public bool ResolvesAgainstOwnerWidth() =>
        AnyPercent(_margin) || AnyPercent(_padding) ||
        _dimensions[0].IsPercent() || _minDimensions[0].IsPercent() || _maxDimensions[0].IsPercent();

    public bool ResolvesAgainstOwnerHeight() =>
        _dimensions[1].IsPercent() || _minDimensions[1].IsPercent() || _maxDimensions[1].IsPercent();

    private static bool AnyPercent(StyleLength[] values)
    {
        foreach (var value in values)
            if (value.IsPercent())
                return true;
        return false;
    }
}
