using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using UIKit;
using static CapWordsFlow;

internal static class SlackFlow
{
    internal static void Run(string output)
    {
        using var program = AnimationGalleryApp.CreateProgram();
        program.Record(Path.Combine(output, "slack-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        Exception? callbackError = null;
        view.UnhandledException += error => callbackError = error;
        view.Render();
        Wait(() => !program.Model.CapWords.Words.IsDefaultOrEmpty);
        var galleryScroll = Find<UIScrollView>(view, "gallery-scroll");
        var button = Find<UIButton>(view, "open-slack");
        galleryScroll.ScrollRectToVisible(button.ConvertRectToView(button.Bounds, galleryScroll), false);
        var point = button.ConvertPointToView(new CGPoint(button.Bounds.Width / 2, button.Bounds.Height / 2), view);
        Require(ReferenceEquals(view.HitTest(point, null), button), "Slack gallery card is reachable by native scrolling and hit testing");
        Capture(view, output, "gallery-slack-phone");
        Tap(view, "open-slack");
        view.Render();
        Require(program.Model.Screen == GalleryScreen.Slack && program.Model.Slack.Index == 4, "Slack opens its five-card catch-up stack");
        var deck = Find<UIView>(view, "slack-deck");
        var cards = deck.Subviews.Where(child => child.GetType().Name == "NativeSwipeCardView").ToArray();
        Require(cards.Length == 5 && cards.Count(card => !card.AccessibilityElementsHidden) == 1,
            "The deck owns five bounded cards with one accessible active channel");
        Require(All(view).OfType<UILabel>().Any(label => label.Text == "#channel-5"), "The reference's final channel starts on top");
        Require(cards.All(card => card.Layer.AnimationKeys?.Length is null or 0), "Detached decks settle without compositor motion");
        Capture(view, output, "slack-phone");
        Tap(view, "slack-read");
        view.Render();
        Require(program.Model.Slack.Index == 3 && program.Model.Slack.Dispositions[4] == "read", "Mark as Read consumes one matching channel");
        Tap(view, "slack-unread");
        view.Render();
        Require(program.Model.Slack.Index == 3, "A second footer action during dismissal cannot consume another card");
        Wait(() => !program.Model.Slack.Busy);
        Tap(view, "slack-undo");
        view.Render();
        Require(program.Model.Slack.Index == 4 && program.Model.Slack.Dispositions[4] == "", "Undo restores the actual dismissed card and its status");
        Wait(() => !program.Model.Slack.Busy);
        foreach (var size in new[] { (Width: 320, Height: 640, Name: "narrow"), (Width: 820, Height: 1020, Name: "tablet") })
        {
            view.Frame = new CGRect(0, 0, size.Width, size.Height);
            view.Render();
            Capture(view, output, "slack-" + size.Name);
            Require(Find<UIView>(view, "slack-deck").Bounds.Width <= 560, "Slack's tablet card width is bounded");
        }
        view.Frame = new CGRect(0, 0, 393, 759);
        view.Render();
        for (var i = 0; i < 5; i++)
        {
            Tap(view, i % 2 == 0 ? "slack-unread" : "slack-read");
            view.Render();
            Wait(() => !program.Model.Slack.Busy);
        }
        Require(program.Model.Slack.Index == -1 && Has(view, "slack-done-title") && Has(view, "slack-done"), "The final card reveals the completion state and Done action");
        Capture(view, output, "slack-complete");
        Require(Find<UIButton>(view, "slack-done").Bounds.Width >= 280, "Completion keeps a full-width native Done target");
        Tap(view, "slack-done");
        view.Render();
        Require(program.Model.Screen == GalleryScreen.Home, "Done returns to the example gallery");
        Tap(view, "open-slack");
        view.Render();
        Require(program.Model.Slack.Index == -1 && Has(view, "slack-done-title"), "Reopening a completed study preserves its completion state");
        Tap(view, "slack-undo");
        view.Render();
        Require(program.Model.Slack.Index == 0 && !Has(view, "slack-done-title"), "Undo also recovers from completion");
        Wait(() => !program.Model.Slack.Busy);
        Capture(view, output, "slack-undo-complete");
        Tap(view, "slack-back");
        view.Render();
        Require(Has(view, "open-capwords") && Has(view, "open-alma") && Has(view, "open-slack"), "All three studies remain available in one gallery");
        Tap(view, "open-slack");
        view.Render();
        Require(program.Model.Slack.Index == 0, "Slack retains its progress across gallery switching");
        using var replayProgram = AnimationGalleryApp.CreateProgram();
        replayProgram.Replay(Path.Combine(output, "slack-session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Capture(view, output, "slack-final");
        Capture(replay, output, "slack-replay");
        Require(program.Model.Slack.Index == replayProgram.Model.Slack.Index && program.Model.Slack.Dispositions.SequenceEqual(replayProgram.Model.Slack.Dispositions),
            "Recorded Slack progress and dispositions replay through the model");
        Require(File.ReadAllBytes(Path.Combine(output, "slack-final.png")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, "slack-replay.png"))),
            "Recorded Slack state reproduces native pixels");
        Require(callbackError is null, "Slack native callbacks remain guarded");
        File.WriteAllText(Path.Combine(output, "slack-report.json"),
            """{"passed":true,"galleryHitTesting":true,"boundedCards":5,"read":true,"duplicateGuard":true,"undo":true,"completion":true,"retainedState":true,"phone":true,"narrow":true,"tablet":true,"pixelReplay":true} """);
    }

    internal static void Wait(Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.Elapsed.TotalSeconds < 5)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
        Require(done(), "Slack action reached its native view");
    }
}
