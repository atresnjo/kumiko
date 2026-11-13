using CoreGraphics;
using Foundation;
using Kumiko;
using Kumiko.Runtime;
using UIKit;

internal static class HotReloadFlow
{
    internal static void Run(string output)
    {
        Require(System.Reflection.Metadata.MetadataUpdater.IsSupported, "Runtime supports metadata updates");
        var builds = 0;
        using var program = new Program<NativeButtonModel, NativeButtonMessage>(new(), NativeButtonApp.Update,
            model =>
            {
                Require(NSThread.IsMain, "View builds on the main thread");
                builds++;
                return NativeButtonApp.Root(model);
            }, NativeButtonAppKumikoJson.Default.Model, NativeButtonAppKumikoJson.Default.Message);
        using var view = new Kumiko.iOS.NativeView(program) { Frame = new CGRect(0, 0, 390, 300) };
        using var otherProgram = NativeButtonApp.Create();
        using var other = new Kumiko.iOS.NativeView(otherProgram) { Frame = view.Frame };
        Exception? callbackError = null;
        view.UnhandledException += error => callbackError = error;
        other.UnhandledException += error => callbackError = error;
        view.Render();
        other.Render();
        var button = Find(view);
        for (var count = 1; count <= 2; count++)
        {
            button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
            PumpUntil(() => Title(button) == $"Tapped {count} times");
        }
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.1));
        var idle = builds;
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.15));
        Require(builds == idle, "Idle native views do not rebuild");
        Capture(view, Path.Combine(output, "hot-before.png"));
        Mark(output, "ready");

        PumpUntil(() => Title(button) == "Reloaded 2 times" && Title(Find(other)) == "Reloaded 0 times");
        Require(ReferenceEquals(button, Find(view)), "View edit retains native identity");
        Require(program.Model.Count == 2 && program.History.Count == 3, "View edit preserves model and history");
        Capture(view, Path.Combine(output, "hot-after.png"));
        Mark(output, "view-updated");

        PumpUntil(() => File.Exists(Path.Combine(output, "send-action")));
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => Title(button) == "Reloaded 12 times");
        Require(program.Model.Count == 12 && program.History.Count == 4, "Update edit affects the next message");
        Mark(output, "update-applied");

        PumpUntil(() => Title(button) == "Again 12 times");
        Require(ReferenceEquals(button, Find(view)), "Repeated edits retain native identity");
        Capture(view, Path.Combine(output, "hot-final.png"));
        program.SaveModel(Path.Combine(output, "hot-state.json"));
        Task.Run(() => HotReload.UpdateApplication(null)).GetAwaiter().GetResult();
        view.Dispose();
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.1));
        var disposedBuilds = builds;
        Task.Run(() => HotReload.UpdateApplication(null)).GetAwaiter().GetResult();
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.1));
        Require(builds == disposedBuilds && callbackError is null, "Disposed host ignores queued and later updates");
        File.WriteAllText(Path.Combine(output, "hot-report.json"),
            $$"""
            {"passed":true,"sourceEdits":3,"count":12,"history":4,"pid":{{Environment.ProcessId}},
             "mainThread":true,"stableIdentity":true,"multipleHosts":true,"idle":true,
             "disposed":true,"offscreen":true,"transport":"dotnet watch"}
            """);
    }

    static UIButton Find(UIView view) => view.Subviews.OfType<UIButton>()
        .Single(button => button.AccessibilityIdentifier == "counter");

    static string? Title(UIButton button)
    {
        button.LayoutIfNeeded();
        return button.TitleLabel?.Text;
    }

    static void Mark(string output, string name) => File.WriteAllText(Path.Combine(output, name), "ready");

    static void PumpUntil(Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(90))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
        Require(done(), "Native event loop delivered the hot update");
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

    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
