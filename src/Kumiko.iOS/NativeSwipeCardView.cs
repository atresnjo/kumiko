using CoreAnimation;
using CoreGraphics;
using Foundation;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed class NativeSwipeCardView : UIView
{
    internal string Id { get; set; } = "";
    internal NativeSwipeFeedbackView Feedback
    {
        get;
    }
    readonly Action<Action> guard;
    NativePalette? configuredPalette;
    bool configuredPreview;

    internal NativeSwipeCardView(Action<Action> guard)
    {
        this.guard = guard;
        Feedback = new(guard);
        AddSubview(Feedback);
        Layer.CornerRadius = 24;
        Layer.CornerCurve = CACornerCurve.Continuous;
        ClipsToBounds = true;
        Layer.BorderWidth = 1;
    }

    internal void ConfigurePalette(NativePalette palette, bool preview)
    {
        if (configuredPalette != palette || configuredPreview != preview)
        {
            using var background = NativePaint.Color(palette.Surface);
            using var border = NativePaint.Color(palette.Border);
            BackgroundColor = background;
            Layer.BorderColor = border.CGColor;
            Feedback.Configure(palette, preview);
            configuredPalette = palette;
            configuredPreview = preview;
        }
        if (Feedback.Frame != Bounds)
            Feedback.Frame = Bounds;
        if (Subviews.LastOrDefault() != Feedback)
            BringSubviewToFront(Feedback);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() => Feedback.Frame = Bounds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Feedback.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class NativeSwipeFeedbackView : UIView
{
    readonly Action<Action> guard;
    readonly UILabel label = new();
    readonly UIImageView icon = new();
    readonly UIImage? read = UIImage.GetSystemImage("checkmark");
    readonly UIImage? unread = UIImage.GetSystemImage("eye.slash");
    NativePalette palette = NativePalette.FocusDark;
    double progress;
    bool right;
    bool preview;

    internal NativeSwipeFeedbackView(Action<Action> guard)
    {
        this.guard = guard;
        UserInteractionEnabled = false;
        Opaque = false;
        BackgroundColor = UIColor.Clear;
        Alpha = 0;
        AddSubview(label);
        AddSubview(icon);
    }

    internal void Configure(NativePalette next, bool small)
    {
        palette = next;
        preview = small;
    }

    internal void Update(double value, bool isRight)
    {
        // An invisible untouched card has no feedback work to reconcile.
        if (progress == 0 && value == 0 && Alpha == 0)
            return;
        Layer.RemoveAnimation("kumiko-deck-feedback");
        progress = value;
        right = isRight;
        Alpha = (nfloat)Math.Min(1, value * 2);
        NativePaint.Label(label, right ? "Mark as\nRead" : "Keep\nUnread", preview ? 16 : 24, UIFontWeight.Bold, Color.White);
        label.Lines = 2;
        icon.Image = right ? read : unread;
        using var tint = progress >= 1 ? NativePaint.Color(right ? palette.Accent : palette.Warm) : UIColor.White;
        icon.TintColor = tint;
        var x = right ? 20 : Bounds.Width - 130;
        label.Frame = new CGRect(x, 88, 120, 68);
        icon.Frame = new CGRect(x + 18, 38, 24, 24);
        SetNeedsDisplay();
    }

    internal void Hide(bool animate)
    {
        var opacity = Layer.PresentationLayer?.Opacity ?? Layer.Opacity;
        Layer.RemoveAnimation("kumiko-deck-feedback");
        Alpha = 0;
        if (!animate || opacity <= 0)
            return;
        using var fade = CABasicAnimation.FromKeyPath("opacity");
        using var from = NSNumber.FromFloat(opacity);
        using var to = NSNumber.FromFloat(0);
        fade.From = from;
        fade.To = to;
        fade.Duration = .15;
        Layer.AddAnimation(fade, "kumiko-deck-feedback");
    }

    public override void Draw(CGRect rect) => guard(() =>
    {
        if (progress <= 0)
            return;
        using var context = UIGraphics.GetCurrentContext();
        if (context is null)
            return;
        var color = right ? palette.Accent : palette.Warm;
        using var space = CGColorSpace.CreateDeviceRGB();
        using var gradient = new CGGradient(space,
            new nfloat[] { color.R, color.G, color.B, .8f, color.R * .2f, color.G * .3f, color.B * .5f, .8f }, new nfloat[] { 0, 1 });
        context.DrawLinearGradient(gradient, default, new CGPoint(Bounds.Width, Bounds.Height), CGGradientDrawingOptions.DrawsAfterEndLocation);
        var center = new CGPoint(right ? 50 : Bounds.Width - 100, 50);
        if (progress >= 1)
        {
            context.SetFillColor(UIColor.White.CGColor);
            context.FillEllipseInRect(new CGRect(center.X - 28, center.Y - 28, 56, 56));
        }
        context.SetStrokeColor(UIColor.White.CGColor);
        context.SetLineWidth(3);
        context.SetLineCap(CGLineCap.Round);
        context.AddArc(center.X, center.Y, 28, (nfloat)(-Math.PI / 2), (nfloat)(-Math.PI / 2 + Math.PI * 2 * Math.Min(1, progress)), false);
        context.StrokePath();
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            label.Dispose();
            icon.Dispose();
            read?.Dispose();
            unread?.Dispose();
        }
        base.Dispose(disposing);
    }
}
