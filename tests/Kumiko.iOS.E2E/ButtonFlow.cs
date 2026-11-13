using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static class ButtonFlow
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var program = NativeButtonApp.Create();
        program.Record(Path.Combine(output, "session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 390, 300) };
        Exception? callbackError = null;
        view.UnhandledException += error =>
        {
            callbackError = error;
            Console.Error.WriteLine(error);
        };
        view.Render();
        var button = Find(view, "counter");
        var initialTitle = Title(button) ?? throw new InvalidOperationException("Missing native title");
        var countAt = initialTitle.IndexOf('0');
        Require(countAt >= 0, "Initial native title displays the zero count");
        string ExpectedTitle(int count) => initialTitle[..countAt] +
            count.ToString(System.Globalization.CultureInfo.InvariantCulture) + initialTitle[(countAt + 1)..];
        Require(button.ButtonType == UIButtonType.System, "A real system UIButton");
        Require(button.Frame.Height >= 44, "Native touch target");
        Capture(view, Path.Combine(output, "before.png"));
        for (var count = 1; count <= 3; count++)
        {
            button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
            PumpUntil(() => Title(button) == ExpectedTitle(count));
            Require(ReferenceEquals(button, Find(view, "counter")), "Stable native identity");
        }
        Require(callbackError is null, "No exception escaped a native callback");
        view.Render();
        Require(Title(button) == ExpectedTitle(3), "Exactly one message per native action");
        view.Frame = new CGRect(0, 0, 320, 240);
        view.Render();
        Require(button.Frame.Right <= 320 && button.Frame.Bottom <= 240, "Resize relayout");
        Require(ReferenceEquals(button, Find(view, "counter")), "Identity survives resize");
        Capture(view, Path.Combine(output, "after.png"));
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => Title(button) == ExpectedTitle(5));
        var typed = (Program<NativeButtonModel, NativeButtonMessage>)program;
        typed.Send(new NativeButtonMessage.SetEnabled(false));
        PumpUntil(() => !button.Enabled);
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        view.Render();
        Require(typed.Model.Count == 5, "Disabled action rejected");
        typed.Send(new NativeButtonMessage.SetVisible(false));
        view.Render();
        Require(view.Subviews.Length == 0, "Removed native control detached");
        Require(button.Handle == IntPtr.Zero, "Removed native control disposed");
        typed.Send(new NativeButtonMessage.SetVisible(true));
        typed.Send(new NativeButtonMessage.SetEnabled(true));
        view.Render();
        Require(!ReferenceEquals(button, Find(view, "counter")), "Recreated control has a new lifetime");
        Task.Run(() => typed.Send(new NativeButtonMessage.Increment())).GetAwaiter().GetResult();
        PumpUntil(() => Title(Find(view, "counter")) == ExpectedTitle(6));
        var secondHostRejected = false;
        try
        {
            using var second = new NativeView(program);
        }
        catch (InvalidOperationException)
        {
            secondHostRejected = true;
        }
        Require(secondHostRejected, "One native host per program");
        using var replayProgram = NativeButtonApp.Create();
        replayProgram.Replay(Path.Combine(output, "session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Require(Title(Find(replay, "counter")) == ExpectedTitle(6), "Kumiko replay");
        Capture(replay, Path.Combine(output, "replay.png"));
        using var unsupportedProgram = UnsupportedApp.Create();
        using var unsupported = new NativeView(unsupportedProgram) { Frame = view.Frame };
        var unsupportedRejected = false;
        try
        {
            unsupported.Render();
        }
        catch (NotSupportedException)
        {
            unsupportedRejected = true;
        }
        Require(unsupportedRejected, "Unsupported primitives fail explicitly");
        view.Dispose();
        typed.Send(new NativeButtonMessage.Increment());
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.05));
        Require(callbackError is null, "Disposed view ignores queued work");
        File.WriteAllText(Path.Combine(output, "report.json"),
            """
            {"passed":true,"renderer":"UIKit","control":"UIButton","actions":6,
             "stableIdentity":true,"resize":true,"disabled":true,"removal":true,
             "workerWake":true,"singleHost":true,"replay":true,"offscreen":true,
             "unsupportedRejected":true,"burstActions":true}
            """);
    }

    static UIButton Find(UIView view, string key) => view.Subviews.OfType<UIButton>()
        .Single(button => button.AccessibilityIdentifier == key);

    static string? Title(UIButton button)
    {
        button.LayoutIfNeeded();
        return button.TitleLabel?.Text;
    }

    static void Capture(UIView view, string path)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(path, png.ToArray());
    }

    static void PumpUntil(Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(5))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
        Require(done(), "Native event loop delivered the update");
    }

    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
