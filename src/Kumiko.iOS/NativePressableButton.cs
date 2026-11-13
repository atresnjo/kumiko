using CoreAnimation;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed class NativePressableButton : UIButton
{
    readonly Action<Action> guard;
    float pressedScale = 1;
    bool reduceMotion;
    UIAction? down;
    UIAction? cancel;
    bool pressedOnDown;
    internal Action? PressIn
    {
        get; set;
    }
    bool pressOnTouchDown;
    internal bool PressOnTouchDown
    {
        get => pressOnTouchDown;
        set
        {
            pressOnTouchDown = value;
            if (value && down is null)
                InstallPressIn();
        }
    }

    internal NativePressableButton(Action<Action> guard) : base(UIButtonType.Custom)
    {
        this.guard = guard;
        IsAccessibilityElement = true;
        AccessibilityTraits = UIAccessibilityTrait.Button;
    }

    void InstallPressIn()
    {
        down = UIAction.Create(_ => guard(() =>
        {
            pressedOnDown = false;
            if (Enabled && PressOnTouchDown)
            {
                pressedOnDown = true;
                PressIn?.Invoke();
            }
        }));
        cancel = UIAction.Create(_ => guard(() => pressedOnDown = false));
        AddAction(down, UIControlEvent.TouchDown);
        AddAction(cancel, UIControlEvent.TouchCancel | UIControlEvent.TouchUpOutside);
    }

    internal bool ConsumeRelease()
    {
        var consumed = pressedOnDown;
        pressedOnDown = false;
        return consumed;
    }

    public override bool Highlighted
    {
        get => base.Highlighted;
        set
        {
            base.Highlighted = value;
            guard(UpdateFeedback);
        }
    }

    internal void Configure(string label, bool enabled, float scale, bool reduced)
    {
        reduceMotion = reduced;
        if (reduced || UIAccessibility.IsReduceMotionEnabled)
            Layer.RemoveAnimation("kumiko-press-scale");
        pressedScale = scale;
        AccessibilityLabel = label;
        Enabled = enabled;
        UpdateFeedback();
    }

    void UpdateFeedback()
    {
        Alpha = !Enabled ? .4f : Highlighted && pressedScale == 1 ? .65f : 1;
        if (pressedScale == 1)
            return;
        var scale = Enabled && Highlighted ? pressedScale : 1;
        var target = CATransform3D.MakeScale(scale, scale, 1);
        if (Layer.Transform.Equals(target))
            return;
        var from = Layer.PresentationLayer?.Transform ?? Layer.Transform;
        Layer.RemoveAnimation("kumiko-press-scale");
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        Layer.Transform = target;
        CATransaction.Commit();
        if (reduceMotion || Window is not { Hidden: false } || UIAccessibility.IsReduceMotionEnabled)
            return;
        using var animation = CABasicAnimation.FromKeyPath("transform");
        using var start = NSValue.FromCATransform3D(from);
        using var end = NSValue.FromCATransform3D(target);
        animation.From = start;
        animation.To = end;
        animation.Duration = .1;
        Layer.AddAnimation(animation, "kumiko-press-scale");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PressIn = null;
            if (down is not null)
            {
                RemoveAction(down, UIControlEvent.TouchDown);
                down.Dispose();
            }
            if (cancel is not null)
            {
                RemoveAction(cancel, UIControlEvent.TouchCancel | UIControlEvent.TouchUpOutside);
                cancel.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
