using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static class CapWordsFlow
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var program = CapWordsApp.CreateProgram();
        program.Record(Path.Combine(output, "session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        Exception? callbackError = null;
        view.UnhandledException += error => callbackError = error;
        view.Render();
        PumpUntil(() => Has(view, "capture"));
        Capture(view, output, "phone-list");
        var gridArt = All(view).OfType<UIImageView>().Where(image => image.AccessibilityIdentifier == "artwork").ToArray();
        Require(gridArt.Length == 2 && gridArt.All(image => image.Image is not null), "Both licensed word illustrations decode");
        foreach (var word in program.Model.Words)
        {
            var button = All(view).OfType<UIButton>().Single(button => button.AccessibilityLabel == word.Name);
            Require(button.Frame.Width >= 44 && button.Frame.Height >= 44, "Image has a native accessible hit target");
            if (word.Id.StartsWith("computer", StringComparison.Ordinal))
            {
                button.Highlighted = true;
                Capture(view, output, "pressed");
                button.Highlighted = false;
                Capture(view, output, "released");
                Require(!File.ReadAllBytes(Path.Combine(output, "pressed.png")).AsSpan().SequenceEqual(
                    File.ReadAllBytes(Path.Combine(output, "released.png"))), "Native press feedback changes visible content");
                Require(File.ReadAllBytes(Path.Combine(output, "phone-list.png")).AsSpan().SequenceEqual(
                    File.ReadAllBytes(Path.Combine(output, "released.png"))), "Releasing restores the list pixels");
            }
            button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
            PumpUntil(() => program.Model.SelectedId == word.Id && Has(view, "name"));
            Require(Find<UILabel>(view, "name").Text == word.Name, "Native action opens the matching detail");
            Require(Find<UILabel>(view, "phonetic").Text == word.Phonetic, "Detail phonetic matches the selected word");
            Require(All(view).Count(child => child.AccessibilityIdentifier == "artwork") == 1, "Grid is removed from detail");
            Require(Find<UIView>(view, "hero").Layer.AnimationKeys?.Length is null or 0, "Detached hosts apply final geometry without motion");
            Capture(view, output, word.Id.StartsWith("computer", StringComparison.Ordinal) ? "mouse-detail" : "ice-cream-detail");
            Tap(view, "back");
            PumpUntil(() => program.Model.SelectedId is null && Has(view, "capture"));
        }
        Capture(view, output, "back");
        Require(File.ReadAllBytes(Path.Combine(output, "phone-list.png")).AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "back.png"))), "Back reproduces the initial list pixels");
        foreach (var size in new[] { (Width: 320, Height: 640, Name: "narrow"), (Width: 820, Height: 1020, Name: "tablet") })
        {
            view.Frame = new CGRect(0, 0, size.Width, size.Height);
            view.Render();
            Capture(view, output, size.Name + "-list");
            var button = All(view).OfType<UIButton>().First(child => child.AccessibilityIdentifier == "open-word");
            button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
            PumpUntil(() => Has(view, "name"));
            Require(Find<UIView>(view, "hero").Frame.Width <= 300, "Tablet artwork remains bounded");
            Capture(view, output, size.Name + "-detail");
            Tap(view, "back");
            PumpUntil(() => Has(view, "capture"));
        }
        using var replayProgram = CapWordsApp.CreateProgram();
        replayProgram.Replay(Path.Combine(output, "session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Capture(view, output, "live");
        Capture(replay, output, "replay");
        Require(program.Model.SelectedId == replayProgram.Model.SelectedId &&
            program.Model.Words.Select(word => word.Id).SequenceEqual(replayProgram.Model.Words.Select(word => word.Id)), "Session restores the final model");
        Require(File.ReadAllBytes(Path.Combine(output, "live.png")).AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "replay.png"))), "Session reproduces native pixels");
        Require(callbackError is null, "No exceptions cross native callbacks");
        File.WriteAllText(Path.Combine(output, "report.json"),
            """{"passed":true,"detachedUIKit":true,"bothWords":true,"backPixelParity":true,"phone":true,"narrow":true,"tablet":true,"modelReplay":true,"pixelReplay":true} """);
    }

    internal static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    internal static bool Has(UIView view, string key) => All(view).Any(child => child.AccessibilityIdentifier == key);
    internal static T Find<T>(UIView view, string key) where T : UIView => All(view).OfType<T>()
        .Single(child => child.AccessibilityIdentifier == key);
    internal static void Tap(UIView view, string key) => Find<UIButton>(view, key).SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);

    internal static void Capture(UIView view, string output, string state, bool presentation = false)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context =>
            (presentation ? view.Layer.PresentationLayer ?? view.Layer : view.Layer).RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(output, state + ".png"), png.ToArray());
        var tree = All(view).Select(child => $"{child.GetType().Name} {child.AccessibilityIdentifier} {child.ConvertRectToView(child.Bounds, view)}");
        File.WriteAllLines(Path.Combine(output, state + ".txt"), tree);
    }

    static void PumpUntil(Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
        Require(done(), "UIKit update delivered");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
