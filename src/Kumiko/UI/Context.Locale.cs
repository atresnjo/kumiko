using System.Globalization;

namespace Kumiko.UI;

public sealed partial class Context
{
    CultureInfo cultureValue = CultureInfo.CurrentCulture;
    /// <summary>Culture used by controls to format and parse dates, times and numbers.</summary>
    public CultureInfo Culture
    {
        get => ReadUI(cultureValue);
        set
        {
            CheckThread();
            ArgumentNullException.ThrowIfNull(value);
            cultureValue = value;
        }
    }

    UiStrings? stringsValue;
    /// <summary>
    /// Text built into Kumiko controls. Defaults to the built-in translation for <see cref="Culture"/>
    /// (English when none exists); an assigned value is used regardless of culture.
    /// </summary>
    public UiStrings Strings
    {
        get => ReadUI(stringsValue ?? UiStrings.For(cultureValue));
        set
        {
            CheckThread();
            ArgumentNullException.ThrowIfNull(value);
            stringsValue = value;
        }
    }

    FlowDirection flowValue;
    /// <summary>Window reading direction; Inherit follows <see cref="Culture"/>.</summary>
    public FlowDirection FlowDirection
    {
        get => ReadUI(flowValue);
        set
        {
            CheckThread();
            flowValue = value;
        }
    }

    internal bool RightToLeftWindow => flowValue == FlowDirection.RightToLeft ||
        flowValue == FlowDirection.Inherit && cultureValue.TextInfo.IsRightToLeft;

    /// <summary>Whether content built at the current position reads right to left.</summary>
    internal bool IsRightToLeft()
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            var flow = Tree[stack[i]].Style.Flow;
            if (flow != FlowDirection.Inherit)
                return flow == FlowDirection.RightToLeft;
        }
        return RightToLeftWindow;
    }

    /// <summary>Maps Left/Right to reading order: in right-to-left content Left means forward.</summary>
    internal Key Logical(Key key) => key is UI.Key.Left or UI.Key.Right && IsRightToLeft() ? key == UI.Key.Left ? UI.Key.Right : UI.Key.Left : key;

    /// <summary>
    /// Converts a window rectangle to the layout coordinates of a window-level layer, which mirror in
    /// right-to-left windows. Use it to place popups beside a <see cref="Div.PreviousRect"/>.
    /// </summary>
    public Rect ToLayout(Rect window) => RightToLeftWindow ? window with
    {
        X = Size.Width - window.Right
    } : window;

    /// <summary>Formats one of <see cref="Strings"/> with the window culture.</summary>
    internal string Format(string format, params object?[] values) => string.Format(Culture, format, values);
}
