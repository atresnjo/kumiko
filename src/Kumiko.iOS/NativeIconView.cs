using CoreAnimation;
using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

/// <summary>
/// Context.Icon in a native view: registered SVG icons as tinted shape layers (crisp at any scale, same geometry as
/// desktop), unprefixed names as SF Symbols. A null accessible name makes the icon decorative.
/// </summary>
internal sealed class NativeIconView : UIView
{
    readonly CALayer shapes = new();
    UIImageView? symbol;
    NativeIconContent? content;
    SvgIconGeometry? geometry;

    internal NativeIconView()
    {
        UserInteractionEnabled = false;
        Layer.AddSublayer(shapes);
    }

    internal void Configure(NativeIconContent next)
    {
        var previous = content;
        content = next;
        IsAccessibilityElement = next.AccessibleName is not null;
        AccessibilityLabel = next.AccessibleName;
        AccessibilityTraits = UIAccessibilityTrait.Image;
        if (previous is { } same && same.Name == next.Name && same.Svg == next.Svg && same.Color == next.Color)
            return;
        if (next.Svg is null)
            ConfigureSymbol(next);
        else
            ConfigureShapes(next);
        SetNeedsLayout();
    }

    void ConfigureSymbol(NativeIconContent next)
    {
        foreach (var layer in shapes.Sublayers ?? [])
            layer.RemoveFromSuperLayer();
        geometry = null;
        using var configuration = UIImageSymbolConfiguration.Create(next.Size * .82f);
        var image = UIImage.GetSystemImage(next.Name, configuration)
            ?? throw new NotSupportedException($"Unknown SF Symbol '{next.Name}'.");
        symbol ??= new UIImageView { ContentMode = UIViewContentMode.Center };
        if (symbol.Superview is null)
            AddSubview(symbol);
        symbol.Image = image;
        symbol.TintColor = NativePaint.Color(next.Color);
    }

    void ConfigureShapes(NativeIconContent next)
    {
        symbol?.RemoveFromSuperview();
        geometry = SvgIconGeometry.Parse(next.Name, next.Svg!);
        using var tint = NativePaint.Color(next.Color);
        var layers = shapes.Sublayers ?? [];
        for (var i = 0; i < Math.Max(layers.Length, geometry.Shapes.Count); i++)
        {
            if (i >= geometry.Shapes.Count)
            {
                layers[i].RemoveFromSuperLayer();
                continue;
            }
            var shape = geometry.Shapes[i];
            var layer = i < layers.Length ? (CAShapeLayer)layers[i] : new CAShapeLayer();
            layer.Path = shape.Path;
            layer.FillColor = shape.Fill ? tint.CGColor : null;
            layer.StrokeColor = shape.Stroke ? tint.CGColor : null;
            layer.LineWidth = shape.StrokeWidth;
            layer.LineCap = shape.LineCap switch
            {
                "round" => CAShapeLayer.CapRound,
                "square" => CAShapeLayer.CapSquare,
                _ => CAShapeLayer.CapButt
            };
            layer.LineJoin = shape.LineJoin switch
            {
                "round" => CAShapeLayer.JoinRound,
                "bevel" => CAShapeLayer.JoinBevel,
                _ => CAShapeLayer.JoinMiter
            };
            layer.FillRule = shape.EvenOdd ? CAShapeLayer.FillRuleEvenOdd : CAShapeLayer.FillRuleNonZero;
            layer.Opacity = shape.Opacity;
            if (i >= layers.Length)
                shapes.AddSublayer(layer);
        }
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        if (symbol?.Superview is not null)
            symbol.Frame = Bounds;
        if (geometry is not { } icon)
            return;
        // Fit the viewBox, keeping its aspect ratio, like the desktop rasterizer.
        var box = icon.ViewBox;
        var scale = (nfloat)Math.Min((double)(Bounds.Width / box.Width), (double)(Bounds.Height / box.Height));
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        shapes.Frame = Bounds;
        shapes.SublayerTransform = CATransform3D.MakeTranslation(
                (Bounds.Width - box.Width * scale) / 2, (Bounds.Height - box.Height * scale) / 2, 0)
            .Scale(scale, scale, 1)
            .Translate(-box.X, -box.Y, 0);
        CATransaction.Commit();
    }
}
