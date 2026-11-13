using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Kumiko.UI;

namespace Kumiko.Platform;

// COM order follows UIAutomationCore.h, IScrollProvider and IScrollItemProvider.
[GeneratedComInterface, Guid("b38b8077-1fc3-42a5-8cae-d40c2215055a")]
internal partial interface IUiaScroll
{
    [PreserveSig] int Scroll(int horizontal, int vertical);
    [PreserveSig] int SetScrollPercent(double horizontal, double vertical);
    [PreserveSig] int GetHorizontalScrollPercent(out double value);
    [PreserveSig] int GetVerticalScrollPercent(out double value);
    [PreserveSig] int GetHorizontalViewSize(out double value);
    [PreserveSig] int GetVerticalViewSize(out double value);
    [PreserveSig] int GetHorizontallyScrollable(out int value);
    [PreserveSig] int GetVerticallyScrollable(out int value);
}
[GeneratedComInterface, Guid("2360c714-4bf1-4b26-ba65-9b21316127eb")]
internal partial interface IUiaScrollItem
{
    [PreserveSig] int ScrollIntoView();
}

internal sealed partial class UiaElement : IUiaScroll, IUiaScrollItem
{
    bool Horizontal => Node?.Properties?.Horizontal == true;
    double ScrollMaximum => Node?.Properties?.Maximum ?? 0;
    double ViewLength => Horizontal ? Node?.Rect.Width ?? 0 : Node?.Rect.Height ?? 0;
    public int ScrollIntoView() => Action(new(AccessibilityAction.Reveal));
    public int Scroll(int horizontal, int vertical)
    {
        if (horizontal is < 0 or > 4 || vertical is < 0 or > 4)
            return Invalid;
        if ((Horizontal ? vertical : horizontal) != 2)
            return NotSupported;
        var amount = Horizontal ? horizontal : vertical;
        var delta = amount switch
        {
            0 => -ViewLength,
            1 => -40,
            3 => ViewLength,
            4 => 40,
            _ => 0
        };
        return Action(new(AccessibilityAction.Scroll, Number: delta));
    }
    public int SetScrollPercent(double horizontal, double vertical)
    {
        if (!double.IsFinite(horizontal) || !double.IsFinite(vertical))
            return Invalid;
        if ((Horizontal ? vertical : horizontal) != -1)
            return NotSupported;
        var percent = Horizontal ? horizontal : vertical;
        if (percent == -1)
            return Alive ? 0 : Unavailable;
        return percent is < 0 or > 100 ? Invalid : Action(new(AccessibilityAction.SetRange, Number: ScrollMaximum * percent / 100));
    }
    int Percent(bool horizontal, out double value)
    {
        value = Horizontal == horizontal && ScrollMaximum > 0 ? 100 * (Node?.Properties?.Number ?? 0) / ScrollMaximum : -1;
        return Alive ? 0 : Unavailable;
    }
    int ViewSize(bool horizontal, out double value)
    {
        value = Horizontal == horizontal && ViewLength + ScrollMaximum > 0 ? 100 * ViewLength / (ViewLength + ScrollMaximum) : 100;
        return Alive ? 0 : Unavailable;
    }
    int Scrollable(bool horizontal, out int value)
    {
        value = Horizontal == horizontal && ScrollMaximum > 0 ? 1 : 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetHorizontalScrollPercent(out double value) => Percent(true, out value);
    public int GetVerticalScrollPercent(out double value) => Percent(false, out value);
    public int GetHorizontalViewSize(out double value) => ViewSize(true, out value);
    public int GetVerticalViewSize(out double value) => ViewSize(false, out value);
    public int GetHorizontallyScrollable(out int value) => Scrollable(true, out value);
    public int GetVerticallyScrollable(out int value) => Scrollable(false, out value);
}
