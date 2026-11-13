using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeRubberSliderView
{
    [Export("kumikoHandleSliderContact:")]
    void HandleContact(UILongPressGestureRecognizer gesture) => guard(() =>
    {
        if (stopped || content is not { Disabled: false })
            return;
        switch (gesture.State)
        {
            case UIGestureRecognizerState.Began:
                active = true;
                Press(true, CanAnimate);
                Follow(gesture.LocationInView(this).X);
                break;
            case UIGestureRecognizerState.Changed when active:
                Follow(gesture.LocationInView(this).X);
                break;
            case UIGestureRecognizerState.Ended when active:
                Follow(gesture.LocationInView(this).X);
                active = false;
                Rest(CanAnimate);
                break;
            case UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed when active:
                active = false;
                Rest(CanAnimate);
                break;
        }
    });

    void Follow(double x)
    {
        var width = Bounds.Width;
        if (width <= 0 || !double.IsFinite(x))
            return;
        Choose((int)Math.Clamp(Math.Ceiling(x / (width / content!.Items.Length)), content.MinimumIndex, content.Items.Length - 1));
        var outside = Math.Max(0, Math.Max(-x, x - width));
        var scaleX = 1 + Math.Clamp(outside / 625, 0, 1) * 1.5;
        var scaleY = 1 - Math.Clamp(outside / (width * .1), 0, 1) * .1;
        var anchor = x > width / 2 ? 0 : 1;
        var previous = stretch.Layer.PresentationLayer?.Transform ?? stretch.Layer.Transform;
        if (gestureStarting)
            previous.M41 += (nfloat)((anchor - stretch.Layer.AnchorPoint.X) * width * (previous.M11 - 1));
        stretch.Layer.RemoveAllAnimations();
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        stretch.Layer.AnchorPoint = new CGPoint(anchor, .5);
        stretch.Layer.Position = new CGPoint(anchor * width, 20);
        var transform = CATransform3D.MakeScale((nfloat)scaleX, (nfloat)scaleY, 1);
        stretch.Layer.Transform = CanAnimate ? transform : CATransform3D.Identity;
        CATransaction.Commit();
        // A fresh contact interrupts the release at its shown pose before the press grows again.
        if (CanAnimate && gestureStarting)
            TransformSpring(stretch.Layer, previous, transform, 900, 60, .6);
        gestureStarting = false;
    }

    bool gestureStarting = true;

    void Rest(bool animate)
    {
        Press(false, animate);
        var from = stretch.Layer.PresentationLayer?.Transform ?? stretch.Layer.Transform;
        stretch.Layer.RemoveAllAnimations();
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        stretch.Layer.Transform = CATransform3D.Identity;
        CATransaction.Commit();
        if (animate)
        {
            TransformSpring(stretch.Layer, from, CATransform3D.Identity, 900, 60, .8);
        }
        gestureStarting = true;
    }

    void Press(bool pressed, bool animate)
    {
        var from = rubber.Layer.PresentationLayer?.Transform ?? rubber.Layer.Transform;
        rubber.Layer.RemoveAllAnimations();
        var target = pressed ? CATransform3D.MakeScale(1.03f, 1.03f, 1) : CATransform3D.Identity;
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        rubber.Layer.Transform = animate ? target : CATransform3D.Identity;
        CATransaction.Commit();
        if (animate)
            TransformSpring(rubber.Layer, from, target, pressed ? 900 : 100, pressed ? 60 : 6, pressed ? .6 : 1);
    }

    static void TransformSpring(CALayer layer, CATransform3D from, CATransform3D to, double stiffness, double damping, double duration)
    {
        if (from.Equals(to))
            return;
        using var start = NSValue.FromCATransform3D(from);
        using var end = NSValue.FromCATransform3D(to);
        using var spring = new CASpringAnimation
        {
            KeyPath = "transform",
            Mass = 1,
            Stiffness = (nfloat)stiffness,
            Damping = (nfloat)damping,
            Duration = duration,
            From = start,
            To = end
        };
        layer.AddAnimation(spring, "kumiko-rubber-transform");
    }

    static void Spring(CALayer layer, string path, double from, double to, double stiffness, double damping, double duration)
    {
        if (Math.Abs(from - to) < .001)
            return;
        using var start = NSNumber.FromDouble(from);
        using var end = NSNumber.FromDouble(to);
        using var spring = new CASpringAnimation
        {
            KeyPath = path,
            Mass = 1,
            Stiffness = (nfloat)stiffness,
            Damping = (nfloat)damping,
            Duration = duration,
            From = start,
            To = end
        };
        layer.AddAnimation(spring, "kumiko-rubber-fill");
    }
}
