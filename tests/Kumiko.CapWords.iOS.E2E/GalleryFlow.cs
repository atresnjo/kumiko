using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using UIKit;
using static CapWordsFlow;

internal static class GalleryFlow
{
    internal static void Run(string output)
    {
        using var program = AnimationGalleryApp.CreateProgram();
        program.Record(Path.Combine(output, "gallery-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        Exception? callbackError = null;
        view.UnhandledException += error => callbackError = error;
        view.Render();
        PumpUntil(() => !program.Model.CapWords.Words.IsDefaultOrEmpty && Has(view, "sticker"));
        Capture(view, output, "gallery-phone");
        var capWords = Find<UIButton>(view, "open-capwords");
        var center = capWords.ConvertPointToView(new CGPoint(capWords.Bounds.GetMidX(), capWords.Bounds.GetMidY()), view);
        Require(ReferenceEquals(view.HitTest(center, null), capWords), "Gallery artwork passes touches to its card button");
        Tap(view, "open-capwords");
        PumpUntil(() => Has(view, "capture"));
        Require(program.Model.Screen == GalleryScreen.CapWords, "CapWords gallery card opens the word collection");
        Navigation(program, view);
        Tap(view, "open-alma");
        PumpUntil(() => Has(view, "alma-playback"));
        Require(program.Model.Navigation.Path.Entries.Length == 2 && program.Model.Navigation.Current.Route == GalleryScreen.Alma,
            "Opening another study discards the abandoned forward visit");
        Tap(view, "alma-playback");
        PumpUntil(() => !program.Model.Alma.Playing);
        Capture(view, output, "alma-phone");
        var orbit = Find<UIView>(view, "nutrients-orbit");
        var count = All(orbit).Count(child => child.GetType().Name == "CarouselCard");
        Require(count == 8 && orbit.AccessibilityLabel == "Support energy", "Alma starts on the reference's second nutrient");
        for (var i = 0; i < 16; i++)
        {
            Tap(view, "alma-next");
            view.Render();
        }
        Require(program.Model.Alma.Index == 1 && All(orbit).Count(child => child.GetType().Name == "CarouselCard") == count,
            "Two complete carousel wraps retain a bounded native card set");
        Tap(view, "alma-previous");
        view.Render();
        Require(program.Model.Alma.Index == 0 && orbit.AccessibilityLabel == "Bone health", "Previous updates the model and accessible focus");
        Capture(view, output, "alma-bone");
        Tap(view, "alma-playback");
        view.Render();
        Tap(view, "alma-back");
        PumpUntil(() => Has(view, "open-alma"));
        var before = program.Model.Alma.Index;
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(2.7));
        Require(program.Model.Alma.Index == before, "Leaving Alma removes its advancing subscription");
        Tap(view, "open-alma");
        PumpUntil(() => Has(view, "alma-playback"));
        Require(program.Model.Alma.Index == before, "Returning preserves the selected nutrient");
        Tap(view, "alma-playback");
        view.Render();
        foreach (var size in new[] { (Width: 320, Height: 640, Name: "narrow"), (Width: 820, Height: 1020, Name: "tablet") })
        {
            view.Frame = new CGRect(0, 0, size.Width, size.Height);
            view.Render();
            Capture(view, output, "alma-" + size.Name);
            Tap(view, "alma-back");
            view.Render();
            Capture(view, output, "gallery-" + size.Name);
            Tap(view, "open-alma");
            view.Render();
        }
        Tap(view, "alma-back");
        view.Render();
        using var replayProgram = AnimationGalleryApp.CreateProgram();
        replayProgram.Replay(Path.Combine(output, "gallery-session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Capture(view, output, "gallery-final");
        Capture(replay, output, "gallery-replay");
        var (live, replayed) = (program.Model.Navigation, replayProgram.Model.Navigation);
        Require(live.Position == replayed.Position && live.Path.Revision == replayed.Path.Revision &&
            live.Path.Entries.SequenceEqual(replayed.Path.Entries), "Replay reproduces gallery visits");
        Require(File.ReadAllBytes(Path.Combine(output, "gallery-final.png")).AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "gallery-replay.png"))), "Combined gallery session replays to identical native pixels");
        Require(callbackError is null, "Gallery and carousel native callbacks remain guarded");
        File.WriteAllText(Path.Combine(output, "gallery-report.json"),
            """{"passed":true,"bothExamples":true,"cardHitTesting":true,"boundedWrap":true,"timerStopsOnExit":true,"retainedSelection":true,"phone":true,"narrow":true,"tablet":true,"pixelReplay":true,"modelHistory":true,"staleRequestsIgnored":true,"forwardVisit":true,"replayedVisits":true} """);
    }

    /// <summary>The study's back arrow moves Back to the gallery's visit; only current requests from this host move it.</summary>
    static void Navigation(Kumiko.Runtime.Program<GalleryModel, GalleryMessage> program, NativeView view)
    {
        var opened = program.Model.Navigation;
        Require(opened.Position == 1 && opened.Path.Entries[0].Route == GalleryScreen.Home, "Opening a study pushes a visit after the gallery");
        var stale = opened.BackRequest(AnimationGalleryApp.Host)!;
        Tap(view, "collection-back");
        PumpUntil(() => Has(view, "open-alma"));
        var home = program.Model.Navigation;
        Require(home.Current.Id == opened.Path.Entries[0].Id && home.CanGoForward,
            "The study's back arrow returns to the gallery's own visit and keeps the study forward");
        var steps = program.History.Count;
        program.Send(new GalleryMessage.Navigate(stale));
        program.Send(new GalleryMessage.Navigate(home.ForwardRequest("other")!));
        program.Send(new GalleryMessage.Navigate(new(AnimationGalleryApp.Host, home.Path.Revision, home.Current.Id, home.Current.Id)));
        PumpUntil(() => program.History.Count >= steps + 3);
        Require(ReferenceEquals(program.Model.Navigation, home), "Stale, foreign or nonadjacent history requests keep the gallery");
        program.Send(new GalleryMessage.Navigate(home.ForwardRequest(AnimationGalleryApp.Host)!));
        PumpUntil(() => Has(view, "capture"));
        Require(program.Model.Navigation.Current.Id == opened.Current.Id, "Forward returns to the retained study visit");
        Tap(view, "collection-back");
        PumpUntil(() => Has(view, "open-alma"));
    }

    static void PumpUntil(Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.Elapsed.TotalSeconds < 5)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
        Require(done(), "Gallery action reached its native view");
    }
}
