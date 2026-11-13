using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using UIKit;
using static CapWordsFlow;

internal static class OpalFlow
{
    internal static void Run(string output)
    {
        using var program = AnimationGalleryApp.CreateProgram();
        program.Record(Path.Combine(output, "opal-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        Exception? error = null;
        view.UnhandledException += failure => error = failure;
        view.Render();
        SlackFlow.Wait(() => !program.Model.CapWords.Words.IsDefaultOrEmpty);
        var scroll = Find<UIScrollView>(view, "gallery-scroll");
        var card = Find<UIButton>(view, "open-opal");
        scroll.ScrollRectToVisible(card.ConvertRectToView(card.Bounds, scroll), false);
        Reach(view, "open-opal");
        Capture(view, output, "gallery-opal-phone");
        Tap(view, "open-opal");
        view.Render();
        Require(program.Model.Screen == GalleryScreen.Opal && Find<UILabel>(view, "opal-duration").Text == "5m",
            $"Fourth study opens with the reference's five-minute duration ({program.Model.Screen}, {program.Model.Opal.Index}, '{Find<UILabel>(view, "opal-duration").Text}')");
        Capture(view, output, "opal-phone");
        foreach (var key in new[] { "opal-minus", "opal-plus", "opal-value", "opal-block", "opal-start", "opal-back",
            "opal-home", "opal-blocks", "opal-screen-time", "opal-leaderboard", "opal-account" })
            Reach(view, key);
        Tap(view, "opal-minus");
        view.Render();
        Require(program.Model.Opal.Index == 1, "Decrement at the minimum stays at five minutes");
        Pause(.35);
        Tap(view, "opal-plus");
        view.Render();
        Pause(.35);
        Tap(view, "opal-plus");
        view.Render();
        Require(!program.Model.Opal.Expanded && program.Model.Opal.TapCount == 1, "Slow taps reset the rapid-tap sequence");
        Tap(view, "opal-plus");
        view.Render();
        Tap(view, "opal-minus");
        view.Render();
        Require(program.Model.Opal.Expanded, "Three native actions within 300ms expand the slider");
        var slider = Find<UIView>(view, "opal-slider");
        Reach(view, "opal-slider");
        Require(!Find<UIButton>(view, "opal-plus").Enabled && !Find<UIButton>(view, "opal-block").Enabled,
            "Expanded presentation disables its hidden compact controls");
        using var input = new OpalContact();
        OpalContact.Send(slider, input, UIGestureRecognizerState.Began, 12.1 * slider.Bounds.Width / 36);
        view.Render();
        Require(program.Model.Opal.Index == 13 && Find<UILabel>(view, "opal-duration").Text == "2h", "Native contact crosses the one-hour/two-hour boundary");
        OpalContact.Send(slider, input, UIGestureRecognizerState.Changed, slider.Bounds.Width + 100);
        OpalContact.Send(slider, input, UIGestureRecognizerState.Cancelled, slider.Bounds.Width + 100);
        view.Render();
        Require(program.Model.Opal.Index == 35 && Find<UILabel>(view, "opal-duration").Text == "24h", "Cancelled edge drag retains the last bounded duration");
        Require(slider.Subviews.Single().Layer.Transform.IsIdentity, "Detached hosts leave no rubber deformation or motion");
        Capture(view, output, "opal-maximum");
        OpalContact.Send(slider, input, UIGestureRecognizerState.Began, -100);
        OpalContact.Send(slider, input, UIGestureRecognizerState.Ended, -100);
        view.Render();
        Require(program.Model.Opal.Index == 1, "Left overdrag clamps to the first selectable step");
        foreach (var size in new[] { (Width: 320, Height: 640, Name: "narrow"), (Width: 820, Height: 1020, Name: "tablet") })
        {
            view.Frame = new CGRect(0, 0, size.Width, size.Height);
            view.Render();
            view.LayoutIfNeeded();
            slider = Find<UIView>(view, "opal-slider");
            Reach(view, "opal-value");
            Reach(view, "opal-slider");
            Reach(view, "opal-start");
            var value = Find<UIView>(view, "opal-value").ConvertRectToView(Find<UIView>(view, "opal-value").Bounds, view);
            var track = slider.ConvertRectToView(slider.Bounds, view);
            Require(value.GetMaxX() < track.X && track.GetMaxX() <= size.Width,
                $"Resized timer targets remain separate and inside the canvas ({size.Name}: {value}, {track})");
            Capture(view, output, "opal-expanded-" + size.Name);
            Tap(view, "opal-value");
            view.Render();
            Tap(view, "opal-back");
            view.Render();
            var galleryScroll = Find<UIScrollView>(view, "gallery-scroll");
            var galleryCard = Find<UIButton>(view, "open-opal");
            galleryScroll.ScrollRectToVisible(galleryCard.ConvertRectToView(galleryCard.Bounds, galleryScroll), false);
            Reach(view, "open-opal");
            Capture(view, output, "gallery-opal-" + size.Name);
            Tap(view, "open-opal");
            view.Render();
            Capture(view, output, "opal-" + size.Name);
            Tap(view, "opal-value");
            view.Render();
        }
        view.Frame = new CGRect(0, 0, 393, 759);
        view.Render();
        Capture(view, output, "opal-expanded-phone");
        Tap(view, "opal-value");
        view.Render();
        Tap(view, "opal-start");
        view.Render();
        Require(program.Model.Opal.Notice == "Start Timer", "Start Timer discloses the selected duration and disconnected session");
        Capture(view, output, "opal-notice");
        Tap(view, "sheet-close");
        view.Render();
        Tap(view, "opal-value");
        view.Render();
        Tap(view, "opal-back");
        view.Render();
        Require(new[] { "open-capwords", "open-alma", "open-slack", "open-opal" }.All(key => Has(view, key)), "All four studies remain available");
        Tap(view, "open-opal");
        view.Render();
        Require(program.Model.Opal.Index == 1 && program.Model.Opal.Expanded, "Returning retains duration and expanded state");
        using var replayProgram = AnimationGalleryApp.CreateProgram();
        replayProgram.Replay(Path.Combine(output, "opal-session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Capture(view, output, "opal-final");
        Capture(replay, output, "opal-replay");
        Require(File.ReadAllBytes(Path.Combine(output, "opal-final.png")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, "opal-replay.png"))),
            "Recorded Opal messages replay to identical native pixels");
        Require(error is null, "Opal native callbacks remain guarded");
        File.WriteAllText(Path.Combine(output, "opal-report.json"),
            """{"passed":true,"galleryHitTesting":true,"rapidTaps":true,"slowTapReset":true,"boundedDuration":true,"nativeContact":true,"cancellation":true,"retainedState":true,"phone":true,"narrow":true,"tablet":true,"reachableControls":true,"pixelReplay":true} """);
    }

    internal static void Reach(UIView view, string key)
    {
        var control = Find<UIView>(view, key);
        var point = control.ConvertPointToView(new CGPoint(control.Bounds.Width / 2, control.Bounds.Height / 2), view);
        var hit = view.HitTest(point, null);
        Require(hit is not null && (hit == control || hit.IsDescendantOfView(control)), $"Native hit testing reaches {key}");
        Require(control.Bounds.Width >= 44 && control.Bounds.Height >= 44, $"{key} retains a 44pt target");
    }

    static void Pause(double seconds) => NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(seconds));
}
