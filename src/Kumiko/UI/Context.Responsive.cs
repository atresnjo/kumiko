namespace Kumiko.UI;

/// <summary>Available space on one axis. UIKit traits take precedence on iOS.</summary>
public enum SizeClass
{
    Compact,
    Regular
}

/// <summary>Logical host width bands, independent of native size-class traits.</summary>
public enum Breakpoint
{
    /// <summary>Less than 600 logical points.</summary>
    Compact,
    /// <summary>600 to less than 900 logical points.</summary>
    Medium,
    /// <summary>900 to less than 1200 logical points.</summary>
    Expanded,
    /// <summary>At least 1200 logical points.</summary>
    Wide
}

public sealed partial class Context
{
    internal SizeClass? HorizontalSizeClassOverride;
    internal SizeClass? VerticalSizeClassOverride;
    Edges safeAreaInsets;

    /// <summary>Native horizontal size class, or Compact below 600 logical points and Regular otherwise.</summary>
    public SizeClass HorizontalSizeClass => ReadUI(HorizontalSizeClassOverride ?? Classify(sizeValue.Width));

    /// <summary>Native vertical size class, or Compact below 600 logical points and Regular otherwise.</summary>
    public SizeClass VerticalSizeClass => ReadUI(VerticalSizeClassOverride ?? Classify(sizeValue.Height));

    /// <summary>Width band based on this host's logical bounds, independent of device scale and UIKit traits.</summary>
    public Breakpoint WidthBreakpoint => ReadUI(sizeValue.Width switch
    {
        < 600 => Breakpoint.Compact,
        < 900 => Breakpoint.Medium,
        < 1200 => Breakpoint.Expanded,
        _ => Breakpoint.Wide
    });

    /// <summary>Physical edge insets inside this host's bounds, in logical points. Zero unless supplied by the host.</summary>
    /// <remarks>Does not include keyboard occlusion. An already safe-area-framed host normally reports zero.</remarks>
    public Edges SafeAreaInsets
    {
        get => ReadUI(safeAreaInsets);
        internal set => safeAreaInsets = value;
    }

    static SizeClass Classify(float length) => length < 600 ? SizeClass.Compact : SizeClass.Regular;
}
