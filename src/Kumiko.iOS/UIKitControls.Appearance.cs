using CoreGraphics;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    internal Size MeasureAction(NativeActionContent content)
    {
        using var label = new UILabel();
        NativePaint.Label(label, content.Label, 18, UIFontWeight.Regular, content.Palette.Text);
        var measured = label.SizeThatFits(new CGSize(float.MaxValue, float.MaxValue));
        return new((float)Math.Ceiling(measured.Width) + 32 + (content.Options.Symbol is null ? 0 : 24) +
            (content.Options.Chevron ? 20 : 0), Math.Max(44, content.Options.Height));
    }

    static void ConfigureLabel(UILabel label, NativeLabelContent content, bool adjustsFont = true)
    {
        var style = content.Style;
        var weight = style.Weight switch
        {
            Weight.Bold => UIFontWeight.Bold,
            Weight.Semibold => UIFontWeight.Semibold,
            Weight.Medium => UIFontWeight.Medium,
            _ => UIFontWeight.Regular
        };
        NativePaint.Label(label, content.Text, style.Size, weight, style.Color ?? NativePalette.FocusDark.Text, adjustsFont);
        if (content.Design != NativeFontDesign.Default)
        {
            var design = content.Design switch
            {
                NativeFontDesign.Serif => UIFontDescriptorSystemDesign.Serif,
                NativeFontDesign.Rounded => UIFontDescriptorSystemDesign.Rounded,
                _ => UIFontDescriptorSystemDesign.Monospaced
            };
            using var baseFont = UIFont.SystemFontOfSize(style.Size, weight);
            using var descriptor = baseFont.FontDescriptor.CreateWithDesign(design.GetConstant()!);
            using var designed = UIFont.FromDescriptor(descriptor ?? baseFont.FontDescriptor, style.Size);
            using var scaled = UIFontMetrics.DefaultMetrics.GetScaledFont(designed, UITraitCollection.CurrentTraitCollection);
            label.Font = scaled;
        }
        label.Lines = style.Wrap ? 0 : 1;
        label.LineBreakMode = LineBreakMode(style);
        label.AccessibilityTraits = content.Heading ? UIAccessibilityTrait.Header : UIAccessibilityTrait.StaticText;
    }

    Entry? CreateAppearance(NativeControlNode node) => node.Content switch
    {
        NativeSurfaceContent => new(new NativeSurfaceView(guard)),
        NativePresentationContent => new(new NativePresentationView(guard)),
        NativeRubberSliderContent => new(new NativeRubberSliderView(guard, id => QueueValue(node.Id, id))),
        NativeCarouselContent => new(new NativeCircularCarouselView(guard, id => Select(node.Id, id))),
        NativeSwipeDeckContent => new(new NativeSwipeDeckView(guard, id => Select(node.Id, id))),
        NativeArcMorphContent => new(new NativeArcMorphView(guard, id => Select(node.Id, id))),
        NativeOrbContent => new(new NativeOrbView(guard)),
        NativeDashContent => new(new NativeDashView(guard)),
        NativeSwipeCardContent => new(new NativeSwipeCardView(guard)),
        NativeGradientContent => new(new NativeGradientView(guard)),
        NativeCounterContent => new(new NativeFlipCounterView(guard)),
        NativeLabelContent => new(new UILabel()),
        NativeIconContent => new(new NativeIconView()),
        NativeDialContent => new(new NativeTimerDial(guard)),
        NativeTimelineContent => new(new NativeTimelineView(guard)),
        NativeSheetContent sheet => CreateSheet(node, sheet),
        NativeTabBarContent => CreateTabBar(node),
        _ => null
    };

    static void UpdateAppearance(Entry entry, NativeControlNode node)
    {
        switch (node.Content)
        {
            case NativePresentationContent presentation:
                ((NativePresentationView)entry.View).Configure(presentation);
                break;
            case NativeRubberSliderContent slider:
                ((NativeRubberSliderView)entry.View).Configure(slider);
                break;
            case NativeSwipeDeckContent deck:
                ((NativeSwipeDeckView)entry.View).Configure(deck);
                break;
            case NativeSwipeCardContent card:
                ((NativeSwipeCardView)entry.View).Id = card.Id;
                break;
            case NativeGradientContent gradient:
                ((NativeGradientView)entry.View).Configure(gradient);
                break;
            case NativeCounterContent counter:
                ((NativeFlipCounterView)entry.View).Configure(counter);
                break;
            case NativeCarouselContent carousel:
                ((NativeCircularCarouselView)entry.View).Configure(carousel);
                break;
            case NativeArcMorphContent arc:
                ((NativeArcMorphView)entry.View).Configure(arc);
                break;
            case NativeOrbContent orb:
                ((NativeOrbView)entry.View).Configure(orb);
                break;
            case NativeDashContent dash:
                ((NativeDashView)entry.View).Configure(dash);
                break;
            case NativeSurfaceContent surface:
                ((NativeSurfaceView)entry.View).Configure(surface.Options);
                break;
            case NativeLabelContent label:
                ConfigureLabel((UILabel)entry.View, label);
                break;
            case NativeIconContent icon:
                ((NativeIconView)entry.View).Configure(icon);
                break;
            case NativeActionContent action:
                ((NativeActionButton)entry.View).Configure(action);
                break;
            case NativeDialContent dial:
                ((NativeTimerDial)entry.View).Configure(dial);
                break;
            case NativeTimelineContent timeline:
                ((NativeTimelineView)entry.View).Configure(timeline);
                break;
            case NativeSheetContent sheet:
                ((SheetOwner)entry.Owner!).Update(sheet);
                break;
            case NativeTabBarContent tabs:
                ((TabBarOwner)entry.Owner!).Update((UITabBar)entry.View, tabs);
                break;
        }
    }
}
