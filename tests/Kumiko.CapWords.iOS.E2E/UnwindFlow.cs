using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using ObjCRuntime;
using UIKit;
using static CapWordsFlow;

// Failure scenarios are listed in UNWIND-ACCEPTANCE.md.
internal static class UnwindFlow
{
    internal static void Run(string output)
    {
        using var program = AnimationGalleryApp.CreateProgram();
        program.Record(Path.Combine(output, "unwind-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        Exception? error = null;
        view.UnhandledException += failure => error = failure;
        view.Render();
        SlackFlow.Wait(() => !program.Model.CapWords.Words.IsDefaultOrEmpty);
        var gallery = Find<UIScrollView>(view, "gallery-scroll");
        var open = Find<UIButton>(view, "open-unwind");
        gallery.ScrollRectToVisible(open.ConvertRectToView(open.Bounds, gallery), false);
        OpalFlow.Reach(view, "open-unwind");
        Capture(view, output, "gallery-unwind-phone");
        Tap(view, "open-unwind");
        view.Render();
        Require(program.Model.Screen == GalleryScreen.Unwind && program.Model.Unwind.Index == 2,
            "The fifth study opens on Breathwork, the third of five routines");
        var hero = Find<UIView>(view, "unwind-hero");
        var scroll = Find<UIScrollView>(view, "unwind-scroll");
        var cards = Cards(hero);
        Require(cards.Length == 5 && hero.AccessibilityLabel == "Breathwork" && hero.AccessibilityValue == "3 of 5",
            "The hero owns five bounded routine cards with an accessible selection");
        Capture(view, output, "unwind-phone");
        Arc(hero, cards);
        Gestures(view, program, hero, cards);
        Morph(view, program, hero, scroll);
        Notices(view, program, scroll);
        foreach (var size in new[] { (Width: 320, Height: 640, Name: "narrow"), (Width: 820, Height: 1020, Name: "tablet") })
        {
            view.Frame = new CGRect(0, 0, size.Width, size.Height);
            view.Render();
            view.LayoutIfNeeded();
            hero = Find<UIView>(view, "unwind-hero");
            var selected = Cards(hero)[program.Model.Unwind.Index];
            var greeting = All(hero).OfType<UILabel>().Single(label => label.AccessibilityIdentifier == "arc-greeting");
            Require(selected.Bounds.Width <= 282 && selected.Bounds.Width <= size.Width - 100 &&
                selected.ConvertRectToView(selected.Bounds, hero).GetMaxY() < greeting.ConvertRectToView(greeting.Bounds, hero).Y,
                $"{size.Name} cards stay bounded above the greeting");
            Capture(view, output, "unwind-" + size.Name);
            Find<UIScrollView>(view, "unwind-scroll").ContentOffset = new CGPoint(0, NativeComponents.ArcMorphCollapseDistance(Math.Min(size.Width, 600)));
            Capture(view, output, "unwind-collapsed-" + size.Name);
            Find<UIScrollView>(view, "unwind-scroll").ContentOffset = CGPoint.Empty;
        }
        view.Frame = new CGRect(0, 0, 393, 759);
        view.Render();
        Tap(view, "unwind-back");
        view.Render();
        Require(program.Model.Screen == GalleryScreen.Home &&
            new[] { "open-capwords", "open-alma", "open-slack", "open-opal", "open-unwind" }.All(key => Has(view, key)),
            "Back returns to a gallery with all five studies");
        var kept = program.Model.Unwind.Index;
        Tap(view, "open-unwind");
        view.Render();
        Require(program.Model.Unwind.Index == kept && Find<UIView>(view, "unwind-hero").AccessibilityLabel == UnwindApp.Routines[kept].Category,
            "Returning retains the selected routine");
        using var replayProgram = AnimationGalleryApp.CreateProgram();
        replayProgram.Replay(Path.Combine(output, "unwind-session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Capture(view, output, "unwind-final");
        Capture(replay, output, "unwind-replay");
        Require(File.ReadAllBytes(Path.Combine(output, "unwind-final.png")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, "unwind-replay.png"))),
            "Recorded Unwind messages replay to identical native pixels");
        Tap(view, "unwind-back");
        view.Render();
        Require(error is null, "Unwind native callbacks remain guarded");
        File.WriteAllText(Path.Combine(output, "unwind-report.json"),
            """{"passed":true,"invertedArc":true,"verticalScrollPassThrough":true,"fingerTracking":true,"velocitySnap":true,"cancel":true,"edgeResistance":true,"scrollMorph":true,"pinnedMenu":true,"settleToNearest":true,"orbSelection":true,"contentHitTesting":true,"notices":true,"phone":true,"narrow":true,"tablet":true,"retainedState":true,"pixelReplay":true} """);
    }

    // Z-order follows distance from the selection, so cards are matched to routines by their accessible names.
    static UIView[] Cards(UIView hero) => All(hero).Where(child => child.GetType().Name == "ArcCard")
        .OrderBy(card => UnwindApp.Routines.IndexOf(UnwindApp.Routines.Single(routine => routine.Category == card.AccessibilityLabel))).ToArray();

    static double Rotation(UIView card) => Math.Atan2(card.Layer.Transform.M12, card.Layer.Transform.M11);

    static CGPoint Center(UIView card, UIView space) => card.Superview!.ConvertPointToView(card.Layer.Position, space);

    static void Arc(UIView hero, UIView[] cards)
    {
        var left = Center(cards[1], hero);
        var middle = Center(cards[2], hero);
        var right = Center(cards[3], hero);
        Require(left.Y < middle.Y - 8 && right.Y < middle.Y - 8 && Math.Abs(left.Y - right.Y) < 1,
            "Side cards rise symmetrically on an inverted arc");
        Require(Rotation(cards[1]) > .15 && Rotation(cards[3]) < -.15 && Math.Abs(Rotation(cards[2])) < .001,
            "Side cards tilt away from the centre while the selected card stays upright");
        Require(Rotation(cards[0]) > Rotation(cards[1]) && left.X < middle.X && middle.X < right.X,
            "Tilt grows with distance from the selected card");
    }

    static void Gestures(NativeView view, Kumiko.Runtime.Program<GalleryModel, GalleryMessage> program, UIView hero, UIView[] cards)
    {
        var pan = hero.GestureRecognizers!.OfType<UIPanGestureRecognizer>().Single();
        using var input = new DrivenPan();
        input.Speed = new CGPoint(0, 600);
        Require(pan.ShouldBegin!(input) == false, "Vertical drags on a card scroll the page instead of the carousel");
        input.Speed = new CGPoint(600, 40);
        Require(pan.ShouldBegin!(input), "Horizontal drags begin a carousel swipe");
        var start = Center(cards[2], hero).X;
        Send(hero, input, UIGestureRecognizerState.Began, 0);
        Send(hero, input, UIGestureRecognizerState.Changed, -120);
        Require(Math.Abs(Center(cards[2], hero).X - (start - 120)) < 2, "The selected card follows the finger one-to-one");
        Require(Math.Abs(Rotation(cards[2])) > .02, "A dragged card tilts as it travels along the arc");
        Capture(view, EntryPoint.Output, "unwind-drag");
        Send(hero, input, UIGestureRecognizerState.Ended, -200);
        view.Render();
        Require(program.Model.Unwind.Index == 3 && hero.AccessibilityLabel == "Outside", "Release past halfway commits the next routine");
        Send(hero, input, UIGestureRecognizerState.Began, 0);
        Send(hero, input, UIGestureRecognizerState.Changed, 260);
        Send(hero, input, UIGestureRecognizerState.Cancelled, 260);
        view.Render();
        Require(program.Model.Unwind.Index == 3, "A cancelled drag keeps the selected routine");
        Send(hero, input, UIGestureRecognizerState.Began, 0);
        Send(hero, input, UIGestureRecognizerState.Ended, 30, 1400);
        view.Render();
        Require(program.Model.Unwind.Index == 2, "A short fast flick uses release velocity");
        Send(hero, input, UIGestureRecognizerState.Began, 0);
        Send(hero, input, UIGestureRecognizerState.Ended, 2000);
        view.Render();
        Require(program.Model.Unwind.Index == 0, "A long drag cannot pass the first routine");
        Send(hero, input, UIGestureRecognizerState.Began, 0);
        Send(hero, input, UIGestureRecognizerState.Changed, 300);
        Require(Center(cards[0], hero).X - start < 150, "Dragging past the first routine meets rubber-band resistance");
        Send(hero, input, UIGestureRecognizerState.Cancelled, 300);
        using var tap = new DrivenTap();
        Touch(hero, tap, Center(cards[1], hero) with
        {
            Y = Center(cards[1], hero).Y + 40
        });
        view.Render();
        Require(program.Model.Unwind.Index == 1 && hero.AccessibilityLabel == "Sleep", "Tapping a side card selects it");
        Touch(hero, tap, Center(cards[2], hero));
        view.Render();
        Require(program.Model.Unwind.Index == 2, "Tapping the next card returns to Breathwork");
    }

    static void Morph(NativeView view, Kumiko.Runtime.Program<GalleryModel, GalleryMessage> program, UIView hero, UIScrollView scroll)
    {
        var cards = Cards(hero);
        var collapse = NativeComponents.ArcMorphCollapseDistance(393);
        var labels = All(hero).OfType<UILabel>().Where(label => label.Text is "Sleep" or "Breathe" or "Outside" && label.AccessibilityIdentifier == "orb-label").ToArray();
        Require(labels.Length == 3 && labels.All(label => label.Alpha < .01), "Orb labels stay hidden while the carousel is expanded");
        Require(scroll.Delegate is not null, "The hero follows the page scroll view");
        scroll.ContentOffset = new CGPoint(0, collapse * .45);
        var half = cards[2].Bounds.Width;
        Require(half < 270 && half > 70 && Math.Abs(Rotation(cards[1])) < Math.Abs(Rotation(cards[0])) + 1,
            $"Halfway scrolling shrinks the cards continuously ({half})");
        Capture(view, EntryPoint.Output, "unwind-morph-half");
        scroll.ContentOffset = new CGPoint(0, collapse);
        Require(Math.Abs(cards[2].Bounds.Width - 55) < .5 && Math.Abs(cards[1].Bounds.Width - 44) < .5 && Math.Abs(cards[3].Bounds.Width - 44) < .5,
            $"The collapsed header turns cards into a large selected orb between smaller neighbours ({string.Join(", ", cards.Select(card => card.Bounds.Width))})");
        Require(cards.All(card => Math.Abs(Rotation(card)) < .001) && labels.All(label => label.Alpha > .99),
            "Collapsed orbs straighten and reveal their labels");
        var pinned = Center(cards[2], scroll.Superview!);
        Capture(view, EntryPoint.Output, "unwind-collapsed");
        var week = Find<UIView>(view, "unwind-week");
        var point = week.ConvertPointToView(new CGPoint(week.Bounds.Width / 2, week.Bounds.Height / 2), view);
        var hit = view.HitTest(point, null);
        Require(point.Y < hero.ConvertPointToView(new CGPoint(0, hero.Bounds.Height), view).Y + collapse &&
            hit is not null && hit != hero && !hit.IsDescendantOfView(hero), "Content beside the pinned orbs keeps its touches");
        scroll.ContentOffset = new CGPoint(0, collapse + 260);
        Require(Math.Abs(Center(cards[2], scroll.Superview!).Y - pinned.Y) < .5, "The orb menu stays pinned while content keeps scrolling");
        using var tap = new DrivenTap();
        Touch(hero, tap, Center(cards[1], hero));
        view.Render();
        Require(program.Model.Unwind.Index == 1 && hero.AccessibilityLabel == "Sleep", "Tapping a side orb selects it");
        Capture(view, EntryPoint.Output, "unwind-collapsed-sleep");
        Touch(hero, tap, Center(cards[2], hero));
        view.Render();
        Require(program.Model.Unwind.Index == 2, "Tapping the right orb returns to Breathwork");
        var settle = scroll.Delegate!;
        foreach (var (target, velocity, expected) in new[] { (100.0, 0.0, 0.0), (190.0, 0.0, (double)collapse), (100.0, 1.2, (double)collapse), (200.0, -1.2, 0.0) })
        {
            var offset = new CGPoint(0, target);
            settle.WillEndDragging(scroll, new CGPoint(0, velocity), ref offset);
            Require(Math.Abs(offset.Y - expected) < .5, $"Release at {target} with velocity {velocity} settles at {expected}");
        }
        var beyond = new CGPoint(0, collapse + 90);
        settle.WillEndDragging(scroll, CGPoint.Empty, ref beyond);
        Require(Math.Abs(beyond.Y - (collapse + 90)) < .5, "Releases beyond the collapse distance scroll freely");
        scroll.ContentOffset = CGPoint.Empty;
        Require(Math.Abs(cards[2].Bounds.Width - 282) < .5 && labels.All(label => label.Alpha < .01), "Scrolling back restores the expanded carousel");
    }

    static void Notices(NativeView view, Kumiko.Runtime.Program<GalleryModel, GalleryMessage> program, UIScrollView scroll)
    {
        foreach (var key in new[] { "unwind-back", "unwind-bell", "unwind-avatar", "unwind-tab-today", "unwind-tab-saved", "unwind-tab-insights" })
            OpalFlow.Reach(view, key);
        foreach (var key in new[] { "unwind-bell", "unwind-avatar", "unwind-see-all", "unwind-write", "unwind-voice", "unwind-tab-saved", "unwind-tab-insights" })
        {
            var control = Find<UIButton>(view, key);
            scroll.ScrollRectToVisible(control.ConvertRectToView(control.Bounds, scroll).Inset(0, -120), false);
            Tap(view, key);
            view.Render();
            Require(program.Model.Unwind.Notice is not null && Has(view, "sheet-close"), $"{key} discloses demo scope in the native drawer");
            if (key == "unwind-write")
                Capture(view, EntryPoint.Output, "unwind-notice");
            Tap(view, "sheet-close");
            view.Render();
            Require(program.Model.Unwind.Notice is null, $"{key} drawer closes");
        }
        scroll.ContentOffset = CGPoint.Empty;
    }

    static void Send(UIView hero, DrivenPan pan, UIGestureRecognizerState state, double translation, double velocity = 0)
    {
        pan.Phase = state;
        pan.Offset = new CGPoint(translation, 0);
        pan.Speed = new CGPoint(velocity, 0);
        hero.PerformSelector(new Selector("kumikoHandleArcPan:"), pan);
    }

    static void Touch(UIView hero, DrivenTap tap, CGPoint point)
    {
        tap.Point = point;
        hero.PerformSelector(new Selector("kumikoHandleArcTap:"), tap);
    }

    sealed class DrivenPan : UIPanGestureRecognizer
    {
        internal UIGestureRecognizerState Phase;
        internal CGPoint Offset;
        internal CGPoint Speed;
        public override UIGestureRecognizerState State
        {
            get => Phase; set => Phase = value;
        }
        public override CGPoint TranslationInView(UIView? view) => Offset;
        public override CGPoint VelocityInView(UIView? view) => Speed;
    }

    sealed class DrivenTap : UITapGestureRecognizer
    {
        internal CGPoint Point;
        public override UIGestureRecognizerState State
        {
            get => UIGestureRecognizerState.Ended; set
            {
            }
        }
        public override CGPoint LocationInView(UIView? view) => Point;
    }
}
