using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

/// <summary>A sphere lit from the upper left, with a small specular glint and a coloured contact shadow.</summary>
internal sealed class NativeOrbView(Action<Action> guard) : UIView
{
    readonly CAGradientLayer sphere = new() { LayerType = CAGradientLayerType.Radial };
    readonly CALayer glint = new();
    readonly CAShapeLayer mark = new() { FillColor = null, LineCap = CAShapeLayer.CapRound, LineJoin = CAShapeLayer.JoinRound };
    readonly CAShapeLayer outline = new() { FillColor = null };
    NativeOrbContent? content;

    internal void Configure(NativeOrbContent next)
    {
        content = next;
        if (sphere.SuperLayer is null)
        {
            Layer.AddSublayer(sphere);
            Layer.AddSublayer(glint);
            Layer.AddSublayer(mark);
            Layer.AddSublayer(outline);
            UserInteractionEnabled = false;
            ClipsToBounds = false;
        }
        var empty = next.Mark == NativeOrbMark.Empty;
        using var light = NativePaint.Color(NativeArcMorphView.Mix(next.Color, UI.Color.White, .62));
        using var body = NativePaint.Color(next.Color);
        using var rim = NativePaint.Color(NativeArcMorphView.Mix(next.Color, UI.Color.Hex(0x6f5f55), .14));
        sphere.Colors = [light.CGColor, body.CGColor, rim.CGColor];
        sphere.Locations = [0, .62, 1];
        sphere.StartPoint = new CGPoint(.36, .28);
        sphere.EndPoint = new CGPoint(1.04, 1.04);
        sphere.Hidden = empty;
        glint.Hidden = empty || next.Mark != NativeOrbMark.None;
        using var white = UIColor.White.ColorWithAlpha(.92f);
        glint.BackgroundColor = white.CGColor;
        mark.StrokeColor = UIColor.White.CGColor;
        using var shade = UIColor.White;
        mark.FillColor = next.Mark == NativeOrbMark.Dot ? shade.CGColor : null;
        mark.Hidden = next.Mark is NativeOrbMark.None or NativeOrbMark.Empty;
        using var dash = UIColor.FromRGB(0xc9, 0xc5, 0xbf);
        outline.StrokeColor = dash.CGColor;
        outline.LineWidth = 1.3f;
        outline.LineDashPattern = [NSNumber.FromDouble(3.2), NSNumber.FromDouble(3.2)];
        outline.Hidden = !empty;
        Layer.ShadowColor = body.CGColor;
        Layer.ShadowOpacity = empty ? 0 : .35f + .5f * next.Glow;
        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            if (content is null)
                return;
            var d = (double)Math.Min(Bounds.Width, Bounds.Height);
            var frame = new CGRect((Bounds.Width - d) / 2, (Bounds.Height - d) / 2, d, d);
            CATransaction.Begin();
            CATransaction.DisableActions = true;
            sphere.Frame = frame;
            sphere.CornerRadius = (nfloat)(d / 2);
            sphere.MasksToBounds = true;
            var g = d * .09;
            glint.Frame = new CGRect(frame.X + d * .68 - g / 2, frame.Y + d * .7 - g / 2, g, g);
            glint.CornerRadius = (nfloat)(g / 2);
            Layer.ShadowRadius = (nfloat)(d * (.2 + .14 * content.Glow));
            Layer.ShadowOffset = new CGSize(0, d * .2);
            using (var shadow = UIBezierPath.FromOval(frame.Inset((nfloat)(d * .12), (nfloat)(d * .12))))
                Layer.ShadowPath = shadow.CGPath;
            using var path = new CGPath();
            if (content.Mark == NativeOrbMark.Check)
            {
                path.MoveToPoint(new CGPoint(frame.X + d * .34, frame.Y + d * .51));
                path.AddLineToPoint(new CGPoint(frame.X + d * .45, frame.Y + d * .62));
                path.AddLineToPoint(new CGPoint(frame.X + d * .66, frame.Y + d * .39));
                mark.LineWidth = (nfloat)Math.Max(1.4, d * .045);
            }
            else if (content.Mark == NativeOrbMark.Dot)
                path.AddEllipseInRect(new CGRect(frame.GetMidX() - d * .09, frame.GetMidY() - d * .09, d * .18, d * .18));
            mark.Path = path;
            mark.Frame = Bounds;
            using var ring = UIBezierPath.FromOval(frame.Inset(.65f, .65f));
            outline.Path = ring.CGPath;
            outline.Frame = Bounds;
            CATransaction.Commit();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var layer in new CALayer[] { sphere, glint, mark, outline })
            {
                layer.RemoveFromSuperLayer();
                layer.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}

/// <summary>A one-point dashed rule centred vertically in its bounds.</summary>
internal sealed class NativeDashView(Action<Action> guard) : UIView
{
    readonly CAShapeLayer line = new() { LineWidth = 1 };

    internal void Configure(NativeDashContent next)
    {
        if (line.SuperLayer is null)
        {
            Layer.AddSublayer(line);
            UserInteractionEnabled = false;
        }
        using var color = NativePaint.Color(next.Color);
        line.StrokeColor = color.CGColor;
        line.LineDashPattern = [NSNumber.FromDouble(3), NSNumber.FromDouble(3)];
        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            using var path = new CGPath();
            path.MoveToPoint(0, Bounds.Height / 2);
            path.AddLineToPoint(Bounds.Width, Bounds.Height / 2);
            line.Path = path;
            line.Frame = Bounds;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            line.RemoveFromSuperLayer();
            line.Dispose();
        }
        base.Dispose(disposing);
    }
}
