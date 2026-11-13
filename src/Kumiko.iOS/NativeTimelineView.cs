using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed class NativeTimelineView(Action<Action> guard) : UIView
{
    NativeTimelineContent? content;

    internal void Configure(NativeTimelineContent next)
    {
        content = next;
        Opaque = false;
        IsAccessibilityElement = true;
        AccessibilityLabel = "Day activity timeline";
        SetNeedsDisplay();
    }

    public override void Draw(CGRect rect) => guard(() =>
    {
        if (content is not { } c)
            return;
        using var context = UIGraphics.GetCurrentContext();
        if (context is null)
            return;
        var track = new CGRect(0, 4, Bounds.Width, 12);
        using var path = UIBezierPath.FromRoundedRect(track, 6);
        NativePaint.Fill(context, c.Palette.Raised);
        path.Fill();
        context.SaveState();
        path.AddClip();
        foreach (var segment in c.Segments)
        {
            NativePaint.Fill(context, segment.Color);
            context.FillRect(new CGRect(segment.Start * track.Width, track.Y,
                (segment.End - segment.Start) * track.Width, track.Height));
        }
        context.RestoreState();
        var marker = (nfloat)Math.Clamp(c.Marker * Bounds.Width, 1, Math.Max(1, Bounds.Width - 15));
        NativePaint.Stroke(context, c.Palette.Accent);
        context.SetLineWidth(1.5f);
        context.MoveTo(marker, 1);
        context.AddLineToPoint(marker, 19);
        context.StrokePath();
        using var flag = UIBezierPath.FromRoundedRect(new CGRect(marker + 1, 5, 12, 10), 2);
        flag.Stroke();
    });
}
