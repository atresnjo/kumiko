using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed class NativeGradientView(Action<Action> guard) : UIView
{
    readonly CAGradientLayer gradient = new();

    internal void Configure(NativeGradientContent content)
    {
        ClipsToBounds = true;
        using var start = NativePaint.Color(content.Start);
        using var end = NativePaint.Color(content.End);
        gradient.Colors = [start.CGColor, end.CGColor];
        gradient.StartPoint = new CGPoint(0, 0);
        gradient.EndPoint = new CGPoint(content.Diagonal ? 1 : 0, 1);
        if (gradient.SuperLayer is null)
            Layer.InsertSublayer(gradient, 0);
        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() => gradient.Frame = Bounds);
    }

    // The gradient is decoration: its own area passes touches to views beneath, while children stay interactive.
    public override UIView? HitTest(CGPoint point, UIEvent? uievent)
    {
        var hit = base.HitTest(point, uievent);
        return hit == this ? null : hit;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            gradient.RemoveFromSuperLayer();
            gradient.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class NativeFlipCounterView(Action<Action> guard) : UIView
{
    readonly UILabel number = new();
    readonly UILabel suffix = new();
    int? previous;

    internal void Configure(NativeCounterContent content)
    {
        if (number.Superview is null)
        {
            AddSubview(number);
            AddSubview(suffix);
        }
        NativePaint.Label(number, content.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), content.Style.Size,
            UIFontWeight.Bold, content.Style.Color ?? NativePalette.FocusDark.Text);
        NativePaint.Label(suffix, content.Suffix, content.Style.Size, UIFontWeight.Bold,
            content.Style.Color ?? NativePalette.FocusDark.Text);
        IsAccessibilityElement = true;
        AccessibilityLabel = $"{content.Value} {content.Suffix}";
        if (content.ReduceMotion || UIAccessibility.IsReduceMotionEnabled || Window is not { Hidden: false })
        {
            number.Layer.RemoveAnimation("kumiko-counter-flip");
            number.Layer.RemoveAnimation("kumiko-counter-opacity");
        }
        if (previous is { } old && old != content.Value)
        {
            number.Layer.RemoveAnimation("kumiko-counter-flip");
            if (!content.ReduceMotion && !UIAccessibility.IsReduceMotionEnabled && Window is { Hidden: false })
            {
                var direction = content.Value < old ? -1 : 1;
                var transform = CATransform3D.MakeScale(.6f, .6f, 1).Rotate((nfloat)(direction * Math.PI / 4), 1, 0, 0);
                transform.M42 = direction * 7;
                using var from = NSValue.FromCATransform3D(transform);
                using var to = NSValue.FromCATransform3D(CATransform3D.Identity);
                using var spring = new CASpringAnimation { KeyPath = "transform" };
                spring.Mass = 1;
                spring.Stiffness = 1000;
                spring.Damping = 90;
                spring.From = from;
                spring.To = to;
                spring.Duration = .8;
                number.Layer.AddAnimation(spring, "kumiko-counter-flip");
                using var fade = new CASpringAnimation { KeyPath = "opacity", Mass = 1, Stiffness = 1000, Damping = 90, Duration = .8 };
                using var translucent = NSNumber.FromFloat(.5f);
                using var opaque = NSNumber.FromFloat(1);
                fade.From = translucent;
                fade.To = opaque;
                number.Layer.AddAnimation(fade, "kumiko-counter-opacity");
            }
        }
        previous = content.Value;
        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            var left = number.SizeThatFits(Bounds.Size);
            var right = suffix.SizeThatFits(Bounds.Size);
            var x = (Bounds.Width - left.Width - right.Width - 4) / 2;
            number.Frame = new CGRect(x, (Bounds.Height - left.Height) / 2, left.Width, left.Height);
            suffix.Frame = new CGRect(x + left.Width + 4, (Bounds.Height - right.Height) / 2, right.Width, right.Height);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            number.Dispose();
            suffix.Dispose();
        }
        base.Dispose(disposing);
    }
}
