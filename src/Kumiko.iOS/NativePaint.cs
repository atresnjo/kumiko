using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal static class NativePaint
{
    internal static UIColor Color(Color color) => UIColor.FromRGBA(color.R, color.G, color.B, color.A);

    internal static UIFont Font(float size, UIFontWeight weight = UIFontWeight.Regular)
    {
        using var font = UIFont.SystemFontOfSize(size, weight);
        return UIFontMetrics.DefaultMetrics.GetScaledFont(font, UITraitCollection.CurrentTraitCollection);
    }

    internal static UIFontWeight Weight(Weight weight) => weight switch
    {
        UI.Weight.Bold => UIFontWeight.Bold,
        UI.Weight.Semibold => UIFontWeight.Semibold,
        UI.Weight.Medium => UIFontWeight.Medium,
        _ => UIFontWeight.Regular
    };

    internal static void Fill(CGContext context, Color color) => context.SetFillColor(color.R, color.G, color.B, color.A);
    internal static void Stroke(CGContext context, Color color) => context.SetStrokeColor(color.R, color.G, color.B, color.A);

    internal static void Glow(CGContext context, CGRect bounds, Color color, nfloat centerY, nfloat radius)
    {
        using var space = CGColorSpace.CreateDeviceRGB();
        using var gradient = new CGGradient(space,
            new nfloat[] { color.R, color.G, color.B, color.A, color.R, color.G, color.B, 0 }, new nfloat[] { 0, 1 });
        var center = new CGPoint(bounds.GetMidX(), centerY);
        context.DrawRadialGradient(gradient, center, 0, center, radius, CGGradientDrawingOptions.DrawsAfterEndLocation);
    }

    internal static void Label(UILabel label, string text, float size, UIFontWeight weight, Color color, bool adjustsFont = true)
    {
        using var font = Font(size, weight);
        using var foreground = Color(color);
        label.AdjustsFontForContentSizeCategory = adjustsFont;
        label.Text = text;
        label.Font = font;
        label.TextColor = foreground;
        label.UserInteractionEnabled = false;
    }
}

internal sealed class NativeSurfaceView(Action<Action> guard) : UIView
{
    NativeSurfaceOptions options;

    internal void Configure(NativeSurfaceOptions next)
    {
        options = next;
        using var background = NativePaint.Color(next.Background);
        // The layer fill honours the corner radius even when an elevated surface leaves its bounds unclipped.
        BackgroundColor = UIColor.Clear;
        Layer.BackgroundColor = background.CGColor;
        Layer.CornerRadius = next.Radius;
        Layer.CornerCurve = CoreAnimation.CACornerCurve.Continuous;
        // Elevated surfaces cannot clip: the layer's shadow is drawn outside its bounds.
        ClipsToBounds = next.Elevation <= 0;
        Layer.ShadowColor = UIColor.Black.CGColor;
        Layer.ShadowOpacity = next.Elevation > 0 ? .1f : 0;
        Layer.ShadowRadius = next.Elevation;
        Layer.ShadowOffset = new CGSize(0, next.Elevation / 2);
        SetNeedsLayout();
        SetNeedsDisplay();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            using var outline = UIBezierPath.FromRoundedRect(Bounds, options.Radius);
            Layer.ShadowPath = options.Elevation > 0 ? outline.CGPath : null;
        });
    }

    public override void Draw(CGRect rect) => guard(() =>
    {
        using var context = UIGraphics.GetCurrentContext();
        if (context is null)
            return;
        using (var clip = UIBezierPath.FromRoundedRect(Bounds, options.Radius))
            clip.AddClip();
        if (options.Glow is { } glow)
            NativePaint.Glow(context, Bounds, glow, 0, (nfloat)Math.Max(Bounds.Width, Bounds.Height) * .8f);
        if (options.Border is { } border)
        {
            NativePaint.Stroke(context, border);
            context.SetLineWidth(.75f);
            using var path = UIBezierPath.FromRoundedRect(Bounds.Inset(.4f, .4f), options.Radius);
            path.Stroke();
        }
    });
}
