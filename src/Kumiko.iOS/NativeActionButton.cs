using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed class NativeActionButton : UIButton
{
    readonly Action<Action> guard;
    readonly UILabel caption = new() { TextAlignment = UITextAlignment.Center };
    readonly UIImageView icon = new() { ContentMode = UIViewContentMode.ScaleAspectFit };
    readonly UIImageView chevron = new() { ContentMode = UIViewContentMode.ScaleAspectFit };
    NativeActionContent? content;

    internal NativeActionButton(Action<Action> guard)
    {
        this.guard = guard;
        AddSubview(caption);
        AddSubview(icon);
        AddSubview(chevron);
        caption.AccessibilityElementsHidden = true;
        icon.UserInteractionEnabled = false;
        chevron.UserInteractionEnabled = false;
        IsAccessibilityElement = true;
        Opaque = false;
    }

    public override bool Highlighted
    {
        get => base.Highlighted;
        set
        {
            base.Highlighted = value;
            Alpha = content?.Options.Disabled == true ? .4f : value ? .72f : 1;
        }
    }

    internal void Configure(NativeActionContent next)
    {
        content = next;
        var p = next.Palette;
        var foreground = next.Options.Style == NativeActionStyle.Mint ? p.Background :
            next.Options.Accent ? p.Accent : p.Text;
        NativePaint.Label(caption, next.Label, 18,
            next.Options.Style == NativeActionStyle.Glow ? UIFontWeight.Semibold : UIFontWeight.Regular, foreground);
        using var tint = NativePaint.Color(next.Options.Symbol == "circle.fill" ? p.Warm : foreground);
        icon.TintColor = tint;
        using var image = next.Options.Symbol is { } symbol ? UIImage.GetSystemImage(symbol) : null;
        icon.Image = image;
        icon.Transform = next.Options.Style == NativeActionStyle.Mint
            ? CGAffineTransform.MakeRotation(.25f) : CGAffineTransform.MakeIdentity();
        using var chevronImage = UIImage.GetSystemImage("chevron.down");
        chevron.Image = chevronImage;
        using var muted = NativePaint.Color(p.Muted);
        chevron.TintColor = muted;
        chevron.Hidden = !next.Options.Chevron;
        AccessibilityLabel = next.Label;
        AccessibilityTraits = UIAccessibilityTrait.Button;
        Enabled = !next.Options.Disabled;
        Alpha = Enabled ? 1 : .4f;
        SetNeedsLayout();
        SetNeedsDisplay();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            if (content is not { } c)
                return;
            var width = Bounds.Width;
            var height = Bounds.Height;
            var iconOnly = c.Options.Width is <= 48 && c.Options.Symbol is not null;
            caption.Hidden = iconOnly;
            if (iconOnly)
                icon.Frame = new CGRect((width - 20) / 2, (height - 20) / 2, 20, 20);
            else
            {
                var textWidth = caption.SizeThatFits(new CGSize(width, height)).Width;
                var iconWidth = c.Options.Symbol is null ? 0 : c.Options.Symbol == "circle.fill" ? 8 : 16;
                textWidth = (nfloat)Math.Min(textWidth, Math.Max(0, width - 24 - (iconWidth > 0 ? iconWidth + 8 : 0) - (c.Options.Chevron ? 20 : 0)));
                caption.AdjustsFontSizeToFitWidth = true;
                caption.MinimumScaleFactor = .65f;
                var total = textWidth + (iconWidth > 0 ? iconWidth + 8 : 0) + (c.Options.Chevron ? 20 : 0);
                var left = (width - total) / 2;
                icon.Frame = new CGRect(left, (height - iconWidth) / 2, iconWidth, iconWidth);
                caption.Frame = new CGRect(left + (iconWidth > 0 ? iconWidth + 8 : 0), 0, textWidth, height);
                chevron.Frame = new CGRect(left + total - 12, (height - 11) / 2, 11, 11);
            }
        });
    }

    public override void Draw(CGRect rect) => guard(() =>
    {
        if (content is not { } c)
            return;
        using var context = UIGraphics.GetCurrentContext();
        if (context is null)
            return;
        var visual = c.Options.Compact ? Bounds.Inset(c.Options.Width is <= 48 ? 6 : 0, 6) : Bounds;
        var radius = c.Options.Style == NativeActionStyle.Mint ? 9 : c.Options.Style == NativeActionStyle.Glow ? visual.Height / 2 :
            c.Options.Compact ? 11 : c.Options.Width is <= 48 ? 14 : 18;
        var fill = c.Options.Style switch
        {
            NativeActionStyle.Mint => c.Palette.Accent,
            NativeActionStyle.Glow => c.Palette.Background,
            _ => c.Options.Style == NativeActionStyle.Plain ? Color.Transparent : c.Palette.Raised
        };
        using var path = UIBezierPath.FromRoundedRect(visual.Inset(.4f, .4f), radius);
        NativePaint.Fill(context, fill);
        path.Fill();
        if (c.Options.Style == NativeActionStyle.Glow)
        {
            context.SaveState();
            path.AddClip();
            context.TranslateCTM(0, Bounds.Height + 12);
            context.ScaleCTM(1, .38f);
            NativePaint.Glow(context, Bounds, c.Palette.Accent.Alpha(.9f), 0, Bounds.Width * .62f);
            context.RestoreState();
        }
        if (c.Options.Style != NativeActionStyle.Mint && c.Options.Style != NativeActionStyle.Plain)
        {
            NativePaint.Stroke(context, c.Palette.Border);
            context.SetLineWidth(.75f);
            path.Stroke();
        }
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            caption.Dispose();
            icon.Dispose();
            chevron.Dispose();
        }
        base.Dispose(disposing);
    }
}
