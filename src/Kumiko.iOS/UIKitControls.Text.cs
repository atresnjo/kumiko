using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    readonly UILabel measureLabel = new();

    public object TextMetricsKey => (UITraitCollection.CurrentTraitCollection.PreferredContentSizeCategory,
        UITraitCollection.CurrentTraitCollection.LegibilityWeight, UIAccessibility.IsBoldTextEnabled);

    public Size MeasureText(string text, TextStyle style, float width, object? content)
    {
        if (content is NativeLabelContent label)
            ConfigureLabel(measureLabel, label, adjustsFont: false);
        else
            Configure(measureLabel, new(text, style, false, false), adjustsFont: false);
        var size = measureLabel.SizeThatFits(new CGSize(float.IsFinite(width) ? Math.Max(0, width) : float.MaxValue, float.MaxValue));
        return new((float)Math.Ceiling(size.Width), (float)Math.Ceiling(size.Height));
    }

    public float TextBaseline(string text, TextStyle style, object? content)
    {
        if (content is NativeLabelContent label)
            ConfigureLabel(measureLabel, label, adjustsFont: false);
        else
            Configure(measureLabel, new(text, style, false, false), adjustsFont: false);
        return (float)measureLabel.Font.Ascender;
    }

    static void Configure(UILabel label, NativeTextContent text, bool adjustsFont = true)
    {
        label.AdjustsFontForContentSizeCategory = adjustsFont;
        var style = text.Style;
        var role = style.Size >= 28 ? UIFontTextStyle.LargeTitle : style.Size >= 20 ? UIFontTextStyle.Title2 :
            style.Size <= 12 ? UIFontTextStyle.Caption1 : UIFontTextStyle.Body;
        using var preferred = UIFont.GetPreferredFontForTextStyle(role, UITraitCollection.CurrentTraitCollection);
        if (style.Weight == Weight.Regular)
            label.Font = preferred;
        else
        {
            using var font = UIFont.SystemFontOfSize(preferred.PointSize,
                style.Weight == Weight.Medium ? UIFontWeight.Medium : style.Weight == Weight.Semibold ? UIFontWeight.Semibold : UIFontWeight.Bold);
            label.Font = font;
        }
        label.Text = text.Text;
        label.Lines = style.Wrap ? 0 : 1;
        label.LineBreakMode = LineBreakMode(style);
        label.TextColor = style.Size <= 12 ? UIColor.SecondaryLabel : UIColor.Label;
        label.TextAlignment = text.RightToLeft ? UITextAlignment.Right : UITextAlignment.Left;
        label.AccessibilityTraits = text.Heading ? UIAccessibilityTrait.Header : UIAccessibilityTrait.StaticText;
    }

    static UILineBreakMode LineBreakMode(TextStyle style)
    {
        if (style.Wrap && style.Overflow == TextOverflow.Ellipsis)
            throw new ArgumentException("Ellipsis requires unwrapped text (Wrap=false).", nameof(style));
        return style.Wrap ? UILineBreakMode.WordWrap : style.Overflow == TextOverflow.Ellipsis
            ? UILineBreakMode.TailTruncation : UILineBreakMode.Clip;
    }
}
