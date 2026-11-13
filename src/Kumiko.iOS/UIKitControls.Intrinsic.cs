using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    readonly UISwitch measureSwitch = new();
    readonly UIStepper measureStepper = new();

    internal Size MeasureIntrinsic(bool stepper)
    {
        var size = (stepper ? (UIControl)measureStepper : measureSwitch).SizeThatFits(CGSize.Empty);
        return new(Math.Max(44, (float)size.Width), Math.Max(44, (float)size.Height));
    }

    Entry Intrinsic(UIControl control, ulong id, Func<object?> read)
    {
        var view = new IntrinsicView(control, Guard);
        return new(view, Owner: new IntrinsicOwner(control, new ValueOwner(this, control, id, read)));
    }

    sealed class IntrinsicOwner(UIControl control, ValueOwner events) : IDisposable
    {
        internal UIControl Control => control;

        public void Dispose()
        {
            events.Dispose();
            control.RemoveFromSuperview();
            control.Dispose();
        }
    }

    sealed class IntrinsicView : UIView
    {
        readonly UIControl control;
        readonly Action<Action> guard;

        internal IntrinsicView(UIControl control, Action<Action> guard)
        {
            this.control = control;
            this.guard = guard;
            IsAccessibilityElement = false;
            AddSubview(control);
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            guard(() =>
            {
                control.SizeToFit();
                control.Center = new CGPoint(Bounds.GetMidX(), Bounds.GetMidY());
            });
        }
    }
}
