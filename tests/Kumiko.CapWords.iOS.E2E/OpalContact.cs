using CoreGraphics;
using ObjCRuntime;
using UIKit;

internal sealed class OpalContact : UILongPressGestureRecognizer
{
    UIGestureRecognizerState phase;
    CGPoint position;

    public override UIGestureRecognizerState State
    {
        get => phase;
        set => phase = value;
    }

    public override CGPoint LocationInView(UIView? view) => position;

    internal static void Send(UIView slider, OpalContact contact, UIGestureRecognizerState state, double x)
    {
        contact.phase = state;
        contact.position = new CGPoint(x, slider.Bounds.Height / 2);
        slider.PerformSelector(new Selector("kumikoHandleSliderContact:"), contact);
    }
}
