using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

/// <summary>
/// An inverted-arc carousel that follows its enclosing page scroll view. The container stays pinned to the
/// visible top while the page scrolls; its cards morph into an orb menu across the morph distance.
/// </summary>
internal sealed partial class NativeArcMorphView : UIView, IUIAccessibilityContainer
{
    readonly Action<Action> guard;
    readonly Action<string> select;
    readonly UIView container = new() { UserInteractionEnabled = false };
    readonly CAGradientLayer backdrop = new();
    readonly UILabel greeting = new() { AccessibilityIdentifier = "arc-greeting", Lines = 1 };
    readonly UILabel subtitle = new() { AccessibilityIdentifier = "arc-subtitle", Lines = 1 };
    readonly UIView dots = new() { UserInteractionEnabled = false };
    readonly List<ArcCard> cards = [];
    readonly List<UILabel> labels = [];
    readonly List<UIView> pips = [];
    NativeArcMorphContent? content;
    UIScrollView? scroll;
    ScrollFollower? follower;
    nfloat offset;
    bool ringShown;
    RoutineElement? routine;

    internal NativeArcMorphView(Action<Action> guard, Action<string> select)
    {
        this.guard = guard;
        this.select = select;
        ClipsToBounds = false;
        routine = new RoutineElement(this) { AccessibilityTraits = (ulong)UIAccessibilityTrait.Adjustable };
        this.SetAccessibilityElements(NSArray.FromNSObjects(routine, greeting, subtitle));
        AddSubview(container);
        container.Layer.AddSublayer(backdrop);
        container.AddSubview(greeting);
        container.AddSubview(subtitle);
        container.AddSubview(dots);
        greeting.AdjustsFontSizeToFitWidth = true;
        greeting.MinimumScaleFactor = .7f;
        subtitle.AdjustsFontSizeToFitWidth = true;
        subtitle.MinimumScaleFactor = .8f;
        pan = new UIPanGestureRecognizer(this, new ObjCRuntime.Selector("kumikoHandleArcPan:")) { MaximumNumberOfTouches = 1 };
        pan.ShouldBegin = recognizer =>
        {
            var allowed = false;
            guard(() =>
            {
                var velocity = ((UIPanGestureRecognizer)recognizer).VelocityInView(this);
                allowed = content is not null && Math.Abs(velocity.X) > Math.Abs(velocity.Y);
            });
            return allowed;
        };
        tap = new UITapGestureRecognizer(this, new ObjCRuntime.Selector("kumikoHandleArcTap:"));
        AddGestureRecognizer(pan);
        AddGestureRecognizer(tap);
    }

    internal void Configure(NativeArcMorphContent next)
    {
        var previous = content;
        var replaced = previous is null || !previous.Cards.Select(card => card.Id).SequenceEqual(next.Cards.Select(card => card.Id));
        content = next;
        if (replaced)
        {
            foreach (var card in cards)
                card.Dispose();
            foreach (var label in labels)
                label.Dispose();
            foreach (var pip in pips)
                pip.Dispose();
            cards.Clear();
            labels.Clear();
            pips.Clear();
            foreach (var item in next.Cards)
            {
                var card = new ArcCard();
                cards.Add(card);
                container.AddSubview(card);
                var label = new UILabel { AccessibilityIdentifier = "orb-label", TextAlignment = UITextAlignment.Center, Alpha = 0 };
                labels.Add(label);
                container.AddSubview(label);
                var pip = new UIView { UserInteractionEnabled = false };
                pip.Layer.CornerRadius = 3;
                pips.Add(pip);
                dots.AddSubview(pip);
            }
            StopSpring();
            position = target = next.SelectedIndex;
        }
        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].Configure(next.Cards[i], ArcMorphMetrics.Fit((float)Bounds.Width));
            NativePaint.Label(labels[i], next.Cards[i].Label, 15, UIFontWeight.Regular, UI.Color.Hex(0x8e8a84), adjustsFont: false);
        }
        ConfigureHeading(next);
        using var paper = NativePaint.Color(next.Background);
        using var clear = NativePaint.Color(next.Background with
        {
            A = 0
        });
        backdrop.Colors = [paper.CGColor, paper.CGColor, clear.CGColor];
        AccessibilityLabel = next.Cards[next.SelectedIndex].Category;
        AccessibilityValue = $"{next.SelectedIndex + 1} of {next.Cards.Length}";
        routine!.AccessibilityLabel = $"{AccessibilityLabel}, {next.Cards[next.SelectedIndex].Title}";
        routine.AccessibilityValue = AccessibilityValue;
        routine.AccessibilityHint = "Swipe up or down to change routine.";
        if (!replaced && !dragging && next.SelectedIndex != (int)Math.Round(target))
            SpringTo(next.SelectedIndex, 0);
        Attach();
        Render();
    }

    void ConfigureHeading(NativeArcMorphContent next)
    {
        using var titleFont = UIFont.SystemFontOfSize(32, UIFontWeight.Medium);
        using var subtitleFont = UIFont.SystemFontOfSize(16, UIFontWeight.Regular);
        using var ink = NativePaint.Color(UI.Color.Hex(0x1f1e1c));
        using var muted = NativePaint.Color(UI.Color.Hex(0x7c7873));
        var heading = new NSMutableAttributedString(next.Heading.Title + (next.Heading.Accent.Length > 0 ? " " : ""),
            new UIStringAttributes { Font = titleFont, ForegroundColor = ink, KerningAdjustment = -1.1f });
        using (var accent = new NSAttributedString(next.Heading.Accent,
            new UIStringAttributes { Font = titleFont, ForegroundColor = muted, KerningAdjustment = -1.1f }))
            heading.Append(accent);
        greeting.AttributedText = heading;
        heading.Dispose();
        NativePaint.Label(subtitle, next.Heading.Subtitle, 16, UIFontWeight.Regular, UI.Color.Hex(0x6f6b66), adjustsFont: false);
        greeting.AccessibilityLabel = $"{next.Heading.Title} {next.Heading.Accent}".Trim();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            Attach();
            if (content is null)
                return;
            var fit = ArcMorphMetrics.Fit((float)Bounds.Width);
            for (var i = 0; i < cards.Count; i++)
                cards[i].Configure(content.Cards[i], fit);
            Render();
        });
    }

    public override void MovedToSuperview()
    {
        base.MovedToSuperview();
        guard(Attach);
    }

    public override void MovedToWindow()
    {
        base.MovedToWindow();
        guard(() =>
        {
            if (Window is null)
                StopSpring();
        });
    }

    /// <summary>Follows the nearest enclosing page scroll view; the hero owns its delegate only while attached.</summary>
    void Attach()
    {
        UIScrollView? found = null;
        for (var view = Superview; view is not null; view = view.Superview)
            if (view is UIScrollView candidate)
            {
                found = candidate;
                break;
            }
        if (found == scroll)
            return;
        Detach();
        if (found is null || found.WeakDelegate is not null)
            return;
        scroll = found;
        follower = new ScrollFollower(this);
        found.Delegate = follower;
        offset = found.ContentOffset.Y;
    }

    void Detach()
    {
        if (scroll is not null && follower is not null && ReferenceEquals(scroll.WeakDelegate, follower))
            scroll.Delegate = null!;
        follower?.Dispose();
        follower = null;
        scroll = null;
        offset = 0;
    }

    void Scrolled(nfloat next)
    {
        if (next == offset)
            return;
        offset = next;
        Render();
    }

    /// <summary>Releases between the expanded and docked states settle at the nearer one, biased by fling direction.</summary>
    nfloat Settle(nfloat proposed, nfloat velocity)
    {
        var collapse = ArcMorphMetrics.Collapse((float)Bounds.Width);
        if (proposed <= 0 || proposed >= collapse)
            return proposed;
        if (Math.Abs(velocity) > .3)
            return velocity > 0 ? collapse : 0;
        return proposed < collapse / 2 ? 0 : collapse;
    }

    sealed class ScrollFollower(NativeArcMorphView owner) : UIScrollViewDelegate
    {
        public override void Scrolled(UIScrollView scrollView) => owner.guard(() => owner.Scrolled(scrollView.ContentOffset.Y));

        public override void WillEndDragging(UIScrollView scrollView, CGPoint velocity, ref CGPoint targetContentOffset)
        {
            var proposed = targetContentOffset;
            var settled = proposed;
            owner.guard(() => settled = proposed with { Y = owner.Settle(proposed.Y, velocity.Y) });
            targetContentOffset = settled;
        }
    }

    void Step(int direction)
    {
        if (content is null)
            return;
        var next = Math.Clamp((int)Math.Round(target) + direction, 0, cards.Count - 1);
        Commit(next, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopSpring();
            Detach();
            RemoveGestureRecognizer(pan);
            RemoveGestureRecognizer(tap);
            pan.Dispose();
            tap.Dispose();
            foreach (var card in cards)
                card.Dispose();
            foreach (var label in labels)
                label.Dispose();
            foreach (var pip in pips)
                pip.Dispose();
            cards.Clear();
            labels.Clear();
            pips.Clear();
            this.SetAccessibilityElements(null);
            routine?.Dispose();
            routine = null;
            backdrop.RemoveFromSuperLayer();
            backdrop.Dispose();
            greeting.Dispose();
            subtitle.Dispose();
            dots.Dispose();
            container.Dispose();
        }
        base.Dispose(disposing);
    }
}
