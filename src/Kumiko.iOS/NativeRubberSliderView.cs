using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeRubberSliderView : UIView
{
    readonly Action<Action> guard;
    readonly Action<string> select;
    readonly UILongPressGestureRecognizer contact;
    readonly UIView track = new();
    readonly UIView rubber = new();
    readonly UIView stretch = new() { AccessibilityIdentifier = "kumiko-rubber-stretch" };
    readonly UIVisualEffectView material;
    readonly CAShapeLayer ticks = new();
    readonly CALayer fill = new();
    NativeRubberSliderContent? content;
    CGSize arrangedSize;
    int selected;
    bool active;
    bool stopped;

    internal NativeRubberSliderView(Action<Action> guard, Action<string> select)
    {
        this.guard = guard;
        this.select = select;
        using var effect = UIBlurEffect.FromStyle(UIBlurEffectStyle.Light);
        material = new(effect)
        {
            UserInteractionEnabled = false,
            Alpha = .25f
        };
        AddSubview(rubber);
        rubber.AddSubview(stretch);
        stretch.AddSubview(track);
        track.AddSubview(material);
        track.Layer.AddSublayer(ticks);
        track.Layer.AddSublayer(fill);
        track.UserInteractionEnabled = false;
        rubber.UserInteractionEnabled = false;
        stretch.UserInteractionEnabled = false;
        track.ClipsToBounds = true;
        using var background = UIColor.FromRGB(23, 23, 23);
        track.BackgroundColor = background;
        track.Layer.CornerRadius = 16;
        track.Layer.CornerCurve = CACornerCurve.Continuous;
        track.Layer.BorderWidth = .5f;
        using var border = UIColor.FromRGB(64, 64, 64);
        track.Layer.BorderColor = border.CGColor;
        ticks.StrokeColor = border.CGColor;
        ticks.LineWidth = 2;
        ticks.LineCap = CAShapeLayer.CapRound;
        ticks.FillColor = UIColor.Clear.CGColor;
        fill.BackgroundColor = UIColor.White.CGColor;
        contact = new UILongPressGestureRecognizer(this, new ObjCRuntime.Selector("kumikoHandleSliderContact:"))
        {
            MinimumPressDuration = 0,
            AllowableMovement = nfloat.MaxValue,
            NumberOfTouchesRequired = 1
        };
        AddGestureRecognizer(contact);
        IsAccessibilityElement = true;
        AccessibilityTraits = UIAccessibilityTrait.Adjustable;
    }

    bool CanAnimate => content is { ReduceMotion: false } && !UIAccessibility.IsReduceMotionEnabled && Window is { Hidden: false };

    internal void Configure(NativeRubberSliderContent next)
    {
        var previous = content;
        content = next;
        if (previous?.Items.Length != next.Items.Length)
            arrangedSize = default;
        var selectionChanged = previous is null || selected != next.Index;
        selected = next.Index;
        contact.Enabled = !next.Disabled;
        AccessibilityLabel = next.Label;
        AccessibilityValue = next.Items[selected].Label;
        AccessibilityTraits = UIAccessibilityTrait.Adjustable | (next.Disabled ? UIAccessibilityTrait.NotEnabled : 0);
        if (selectionChanged)
            PlaceFill(previous is not null && CanAnimate);
        if (!CanAnimate || next.Disabled)
        {
            if (next.Disabled)
                active = false;
            Rest(false);
        }
        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            if (arrangedSize == Bounds.Size)
                return;
            arrangedSize = Bounds.Size;
            active = false;
            CATransaction.Begin();
            CATransaction.DisableActions = true;
            rubber.Layer.Transform = CATransform3D.Identity;
            rubber.Frame = new CGRect(0, (Bounds.Height - 40) / 2, Bounds.Width, 40);
            stretch.Layer.Transform = CATransform3D.Identity;
            stretch.Frame = rubber.Bounds;
            track.Frame = stretch.Bounds;
            material.Frame = track.Bounds;
            ticks.Frame = track.Bounds;
            using var path = new UIBezierPath();
            if (content is { } c)
                for (var i = 1; i < c.Items.Length; i++)
                {
                    var x = i * track.Bounds.Width / c.Items.Length;
                    var half = i % 2 == 0 ? 10 : 5;
                    path.MoveTo(new CGPoint(x, 20 - half));
                    path.AddLineTo(new CGPoint(x, 20 + half));
                }
            ticks.Path = path.CGPath;
            CATransaction.Commit();
            Rest(false);
            PlaceFill(false);
        });
    }

    void PlaceFill(bool animate)
    {
        if (content is null || track.Bounds.Width <= 0)
            return;
        var width = selected == content.Items.Length - 1 ? track.Bounds.Width : selected * track.Bounds.Width / content.Items.Length;
        var shown = fill.PresentationLayer?.Bounds.Width ?? fill.Bounds.Width;
        fill.RemoveAllAnimations();
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        fill.AnchorPoint = new CGPoint(0, .5);
        fill.Position = new CGPoint(0, 20);
        fill.Bounds = new CGRect(0, 0, width, 40);
        CATransaction.Commit();
        if (animate)
            Spring(fill, "bounds.size.width", shown, width, 1600, 140, .8);
    }

    void Choose(int index)
    {
        if (stopped || content is not { Disabled: false } c)
            return;
        index = Math.Clamp(index, c.MinimumIndex, c.Items.Length - 1);
        if (selected == index)
            return;
        selected = index;
        AccessibilityValue = c.Items[index].Label;
        PlaceFill(CanAnimate);
        if (Window is { Hidden: false })
        {
            using var feedback = OperatingSystem.IsIOSVersionAtLeast(17, 5) || OperatingSystem.IsMacCatalystVersionAtLeast(17, 5)
                ? UIImpactFeedbackGenerator.GetFeedbackGenerator(UIImpactFeedbackStyle.Light, this)
                : new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Light);
            feedback.ImpactOccurred();
        }
        select(c.Items[index].Id);
    }

    public override void AccessibilityIncrement() => guard(() => Choose(selected + 1));
    public override void AccessibilityDecrement() => guard(() => Choose(selected - 1));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stopped = true;
            RemoveGestureRecognizer(contact);
            contact.Dispose();
            fill.RemoveFromSuperLayer();
            fill.Dispose();
            ticks.RemoveFromSuperLayer();
            ticks.Dispose();
            material.Dispose();
            track.Dispose();
            stretch.Dispose();
            rubber.Dispose();
        }
        base.Dispose(disposing);
    }
}
