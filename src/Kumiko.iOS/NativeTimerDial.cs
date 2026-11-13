using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed class NativeTimerDial : UIView
{
    readonly Action<Action> guard;
    readonly UILabel value = new() { TextAlignment = UITextAlignment.Center };
    readonly UILabel status = new() { TextAlignment = UITextAlignment.Center };
    readonly UILabel detail = new() { TextAlignment = UITextAlignment.Center };
    NativeDialContent? content;

    internal NativeTimerDial(Action<Action> guard)
    {
        this.guard = guard;
        AddSubview(status);
        AddSubview(value);
        AddSubview(detail);
        IsAccessibilityElement = true;
        Opaque = false;
    }

    internal void Configure(NativeDialContent next)
    {
        content = next;
        NativePaint.Label(value, next.Value, 56, UIFontWeight.Light, next.Palette.Muted);
        NativePaint.Label(status, next.Status.ToUpperInvariant(), 11, UIFontWeight.Semibold, next.Palette.Muted);
        NativePaint.Label(detail, next.Detail, 13, UIFontWeight.Regular, next.Palette.Muted);
        AccessibilityTraits = UIAccessibilityTrait.Image;
        AccessibilityLabel = next.Status;
        AccessibilityValue = $"{next.Value}, {next.Detail}";
        SetNeedsDisplay();
        SetNeedsLayout();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            var width = Bounds.Width;
            var center = Bounds.Height / 2;
            status.Frame = new CGRect(14, center - 53, width - 28, 20);
            value.Frame = new CGRect(8, center - 32, width - 16, 74);
            detail.Frame = new CGRect(14, center + 39, width - 28, 24);
            value.AdjustsFontSizeToFitWidth = true;
            value.MinimumScaleFactor = .6f;
            status.AdjustsFontSizeToFitWidth = true;
        });
    }

    public override void Draw(CGRect rect) => guard(() =>
    {
        if (content is not { } c)
            return;
        using var context = UIGraphics.GetCurrentContext();
        if (context is null)
            return;
        var center = new CGPoint(Bounds.GetMidX(), Bounds.GetMidY());
        var radius = (nfloat)Math.Min(Bounds.Width, Bounds.Height) / 2 - 7;
        NativePaint.Stroke(context, c.Palette.Border.Alpha(.45f));
        context.SetLineWidth(12);
        context.StrokeEllipseInRect(new CGRect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        context.SetLineCap(CGLineCap.Round);
        for (var tick = 0; tick < 60; tick++)
        {
            var angle = tick * Math.PI / 30 - Math.PI / 2;
            var outer = radius - 15;
            var inner = outer - (tick % 5 == 0 ? 6 : 3);
            NativePaint.Stroke(context, c.Palette.Muted.Alpha(tick % 5 == 0 ? .33f : .13f));
            context.SetLineWidth(tick % 5 == 0 ? 1.8f : 1.2f);
            context.MoveTo((nfloat)(center.X + Math.Cos(angle) * inner), (nfloat)(center.Y + Math.Sin(angle) * inner));
            context.AddLineToPoint((nfloat)(center.X + Math.Cos(angle) * outer), (nfloat)(center.Y + Math.Sin(angle) * outer));
            context.StrokePath();
        }
        if (c.Progress > 0)
        {
            NativePaint.Stroke(context, c.Palette.Accent);
            context.SetLineWidth(4);
            context.AddArc(center.X, center.Y, radius, -(nfloat)Math.PI / 2,
                (nfloat)(c.Progress * Math.PI * 2 - Math.PI / 2), false);
            context.StrokePath();
        }
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            value.Dispose();
            status.Dispose();
            detail.Dispose();
        }
        base.Dispose(disposing);
    }
}
