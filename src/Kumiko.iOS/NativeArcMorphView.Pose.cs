using CoreAnimation;
using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeArcMorphView
{
    // Measured from the reference at 393pt: side cards sit 320pt out, rise 30pt and tilt about 15.5°.
    const double Tilt = .27;
    const double Rise = 30;
    const double Gap = 38;

    internal readonly record struct ArcPose(CGPoint Center, nfloat Width, nfloat Height, nfloat Radius, nfloat Rotation,
        nfloat Alpha, nfloat Tint, nfloat ContentAlpha, nfloat Morph, nfloat Fit);

    nfloat Progress => (nfloat)Math.Clamp(offset / ArcMorphMetrics.MorphDistance((float)Bounds.Width), 0, 1);

    double Spacing(double morph)
    {
        var width = Bounds.Width;
        var fit = ArcMorphMetrics.Fit((float)width);
        return Lerp(282 * fit + Gap * fit, OrbSpacing, morph);
    }

    double OrbSpacing => Math.Clamp(Bounds.Width * .27, 96, 130);

    ArcPose PoseAt(int index, double at, double morph)
    {
        var width = (double)Bounds.Width;
        var fit = ArcMorphMetrics.Fit((float)width);
        var cardWidth = 282 * fit;
        var cardHeight = 234 * fit;
        var d = index - at;
        var distance = Math.Abs(d);
        var rise = Rise * fit * (distance <= 1 ? d * d : 2 * distance - 1);
        var expanded = new CGPoint(width / 2 + Spacing(0) * d, ArcMorphMetrics.CardTop + cardHeight / 2 - rise);
        var docked = new CGPoint(width / 2 + OrbSpacing * d, ArcMorphMetrics.OrbCenter);
        var diameter = Lerp(55, 44, Math.Min(1, distance));
        var w = Lerp(cardWidth, diameter, morph);
        var h = Lerp(cardHeight, diameter, morph);
        var radius = Lerp(30 * fit, Math.Min(w, h) / 2, Math.Pow(morph, 1.5));
        var focus = Math.Clamp(1 - distance, 0, 1);
        var faded = Math.Clamp((distance - 1.25) / .35, 0, 1) * morph;
        return new(new CGPoint(Lerp(expanded.X, docked.X, morph), Lerp(expanded.Y, docked.Y, morph)), (nfloat)w, (nfloat)h,
            (nfloat)Math.Min(radius, Math.Min(w, h) / 2), (nfloat)(-Tilt * Math.Clamp(d, -2.5, 2.5) * (1 - morph)),
            (nfloat)(distance > 2.6 && morph < .01 ? 0 : 1 - faded), (nfloat)Lerp(1, focus, Math.Clamp(morph / .45, 0, 1)),
            (nfloat)(1 - Smooth(0, .22, morph)), (nfloat)morph, (nfloat)fit);
    }

    void Render()
    {
        if (content is null || Bounds.Width <= 0 || cards.Count == 0)
            return;
        var width = Bounds.Width;
        var morph = (double)Progress;
        var pin = (nfloat)Math.Max(0, offset);
        var at = dragging ? dragPosition : position;
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        container.Frame = new CGRect(0, pin, width, Bounds.Height);
        const double reach = 1000;
        backdrop.Frame = new CGRect(0, -reach, width, reach + ArcMorphMetrics.Docked);
        backdrop.Locations = [0, (reach + ArcMorphMetrics.Docked - 8) / (reach + ArcMorphMetrics.Docked), 1];
        var top = ArcMorphMetrics.GreetingTop((float)width) - pin;
        var fade = (nfloat)(1 - Math.Clamp(offset / (ArcMorphMetrics.MorphDistance((float)width) * .45), 0, 1));
        greeting.Frame = new CGRect(22, top, width - 44, 40);
        subtitle.Frame = new CGRect(22, top + 42, width - 44, 22);
        greeting.Alpha = fade;
        subtitle.Alpha = fade;
        RenderDots(at, morph);
        var selected = (int)Math.Round(target);
        for (var i = 0; i < cards.Count; i++)
        {
            var pose = PoseAt(i, at, morph);
            cards[i].Apply(pose);
            var focus = Math.Clamp(1 - Math.Abs(i - at), 0, 1);
            var label = labels[i];
            label.Frame = new CGRect(pose.Center.X - 60, ArcMorphMetrics.LabelCenter - 11, 120, 22);
            label.Alpha = (nfloat)(Smooth(.75, 1, morph) * pose.Alpha);
            var ink = Mix(UI.Color.Hex(0x8e8a84), UI.Color.Hex(0x2b2926), focus);
            using var color = NativePaint.Color(ink);
            label.TextColor = color;
            var emphasis = i == selected;
            if (label.Tag != (emphasis ? 1 : 0))
            {
                label.Tag = emphasis ? 1 : 0;
                label.Font = UIFont.SystemFontOfSize(15, emphasis ? UIFontWeight.Medium : UIFontWeight.Regular);
            }
        }
        foreach (var index in Enumerable.Range(0, cards.Count).OrderByDescending(i => Math.Abs(i - at)))
            container.BringSubviewToFront(cards[index]);
        CATransaction.Commit();
        var docked = morph >= .999 && !dragging && Math.Abs(position - target) < .002;
        if (docked != ringShown)
        {
            ringShown = docked;
            for (var i = 0; i < cards.Count; i++)
                cards[i].ShowRing(docked && i == selected, CanAnimate);
        }
        else if (ringShown)
            for (var i = 0; i < cards.Count; i++)
                cards[i].ShowRing(i == selected, CanAnimate);
        if (routine is not null)
            routine.AccessibilityFrameInContainerSpace = new CGRect(0, pin, width,
                Lerp(ArcMorphMetrics.DotsCenter((float)width), ArcMorphMetrics.Docked, morph));
    }

    void RenderDots(double at, double morph)
    {
        var width = Bounds.Width;
        var widths = Enumerable.Range(0, pips.Count).Select(i => 6 + 16 * Math.Clamp(1 - Math.Abs(i - at), 0, 1)).ToArray();
        var total = widths.Sum() + 4 * (pips.Count - 1);
        dots.Frame = new CGRect((width - total) / 2, ArcMorphMetrics.DotsCenter((float)width) - 3, total, 6);
        dots.Alpha = (nfloat)(1 - Math.Clamp(morph / .15, 0, 1));
        var x = 0d;
        for (var i = 0; i < pips.Count; i++)
        {
            pips[i].Frame = new CGRect(x, 0, widths[i], 6);
            using var fill = NativePaint.Color(Mix(UI.Color.Hex(0xcbc7c1), UI.Color.Hex(0x2b2926), (widths[i] - 6) / 16));
            pips[i].BackgroundColor = fill;
            x += widths[i] + 4;
        }
    }

    bool CanAnimate => content is { ReduceMotion: false } && Window is { Hidden: false } && !UIAccessibility.IsReduceMotionEnabled;

    internal static double Lerp(double from, double to, double amount) => from + (to - from) * amount;

    internal static double Smooth(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal static UI.Color Mix(UI.Color from, UI.Color to, double amount)
    {
        var t = (float)Math.Clamp(amount, 0, 1);
        return new UI.Color(from.R + (to.R - from.R) * t, from.G + (to.G - from.G) * t, from.B + (to.B - from.B) * t,
            from.A + (to.A - from.A) * t);
    }

    /// <summary>VoiceOver's adjustable routine picker; the greeting labels remain separate elements.</summary>
    sealed class RoutineElement(NativeArcMorphView owner) : UIAccessibilityElement(owner)
    {
        public override void AccessibilityIncrement() => owner.guard(() => owner.Step(1));
        public override void AccessibilityDecrement() => owner.guard(() => owner.Step(-1));
    }
}
