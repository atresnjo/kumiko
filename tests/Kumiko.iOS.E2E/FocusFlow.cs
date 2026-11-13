using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static partial class FocusFlow
{
    internal static void Run(string output)
    {
        using var program = FocusApp.CreateProgram();
        program.Record(Path.Combine(output, "focus-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            view.TraitOverrides.UserInterfaceStyle = UIUserInterfaceStyle.Dark;
            view.UpdateTraitsIfNeeded();
        }
        Exception? callbackError = null;
        view.UnhandledException += error => callbackError = error;
        view.Render();
        Require(Find<UIView>(view, "timer-dial").AccessibilityValue!.Contains("45:00", StringComparison.Ordinal), "Initial focus duration");
        var bar = Find<UITabBar>(view, "bottom-bar");
        var tabs = bar.Items!.ToArray();
        Require(tabs.Length == 5 && bar.Frame.Height >= 44 && bar.Frame.Width / tabs.Length >= 44, "System tab bar touch targets");
        Require(bar.SelectedItem?.AccessibilityIdentifier == "now", "Initial native selected tab");
        CheckSessionLayout(view, output, "phone");
        Capture(view, output, "phone");
        var replaced = program.Model.Navigation;
        var owner = (UITabBarDelegate)bar.Delegate!;
        Tap(view, "blocking");
        PumpUntil(() => program.Model.Tab == "blocking");
        PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "block-list"));
        var list = Find<UITableView>(view, "block-list");
        Require(list.NumberOfRowsInSection(0) == 9, "Native multi-selection list");
        CheckTabVisits(view, program, replaced, (bar, owner, tabs));
        bar = Find<UITabBar>(view, "bottom-bar");
        Require(bar.SelectedItem?.AccessibilityIdentifier == "blocking", "Native selection follows model");
        Tap(view, "settings");
        PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "demo-note"));
        Require(Find<UILabel>(view, "demo-note").Text!.Contains("simulated", StringComparison.Ordinal), "Example discloses simulated integration");
        program.SaveModel(Path.Combine(output, "focus-settings-model.json"));
        Tap(view, "now");
        PumpUntil(() => program.Model.Tab == "now" && All(view).Any(child => child.AccessibilityIdentifier == "duration"));
        bar = Find<UITabBar>(view, "bottom-bar");
        tabs = bar.Items!.ToArray();
        Require(bar.SelectedItem?.AccessibilityIdentifier == "now", "Model-driven navigation updates native selection");
        Tap(view, "duration");
        PumpUntil(() => program.Model.Drawer == "duration" && All(view).Any(child => child.AccessibilityIdentifier == "sheet"));
        Require(Find<UIView>(view, "sheet").Layer.CornerRadius == 28, "Rounded native bottom drawer");
        Tap(view, "activity");
        Pump();
        Require(program.Model.Tab == "now", "Bottom drawer isolates background tabs");
        var covered = program.Model.Navigation;
        var queued = program.History.Count;
        program.Send(new FocusMessage.SelectTab("activity"));
        program.Send(new FocusMessage.Navigate(new(FocusApp.Host, covered.Path.Revision, covered.Current.Id, covered.Current.Id)));
        PumpUntil(() => program.History.Count >= queued + 2);
        Require(ReferenceEquals(program.Model.Navigation, covered), "Open drawer rejects queued tab selections and history requests");
        Capture(view, output, "duration");
        CheckDurationReflow(view, output);
        Find<UIButton>(view, "duration-choice", "25 min").SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => program.Model.Minutes == 25 && program.Model.Drawer is null);
        PumpUntil(() => Find<UIView>(view, "timer-dial").AccessibilityValue!.Contains("25:00", StringComparison.Ordinal));
        Tap(view, "duration");
        PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "sheet"));
        Find<UIButton>(view, "duration-choice", "45 min").SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        PumpUntil(() => program.Model.Minutes == 45 && program.Model.Drawer is null);
        Tap(view, "start");
        PumpUntil(() => program.Model.Running);
        PumpUntil(() => program.Model.Remaining < 2700);
        Capture(view, output, "running");
        Tap(view, "pause");
        PumpUntil(() => !program.Model.Running);
        var remaining = program.Model.Remaining;
        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(1.2));
        Require(program.Model.Remaining == remaining, "Pause removes the session timer subscription");
        Tap(view, "blocking-summary");
        PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "drawer-block-list"));
        var drawerList = Find<UITableView>(view, "drawer-block-list");
        using (var row = NSIndexPath.FromRowSection(0, 0))
            drawerList.Source!.RowSelected(drawerList, row);
        PumpUntil(() => !program.Model.Blocked.Contains("app-mail"));
        Require(program.Model.Drawer == "blocking", "Changing drawer content does not dismiss it");
        Capture(view, output, "blocking");
        Tap(view, "sheet-close");
        PumpUntil(() => program.Model.Drawer is null);
        PumpUntil(() => Find<UIButton>(view, "blocking-summary").AccessibilityLabel!.StartsWith("block 1 app", StringComparison.Ordinal));
        Tap(view, "connection-action");
        PumpUntil(() => program.Model.Drawer == "connection" && All(view).Any(child => child.AccessibilityIdentifier == "connection-copy"));
        Tap(view, "focus-drawer-backdrop");
        PumpUntil(() => program.Model.Drawer is null);
        Require(ReferenceEquals(bar, Find<UITabBar>(view, "bottom-bar")) && bar.Items!.Zip(tabs).All(pair => ReferenceEquals(pair.First, pair.Second)),
            "Stable tab bar and items across drawer, duration and timer updates within one visit");
        Capture(view, output, "live");
        using var replayProgram = FocusApp.CreateProgram();
        replayProgram.Replay(Path.Combine(output, "focus-session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        Capture(replay, output, "replay");
        Require(replayProgram.Model.Minutes == program.Model.Minutes && replayProgram.Model.Remaining == program.Model.Remaining &&
            replayProgram.Model.Blocked.SetEquals(program.Model.Blocked), "Focus model replays");
        Require(SameVisit(replayProgram.Model.Navigation, program.Model.Navigation), "Replay reproduces the tab visit and history revision");
        CheckRestore(output, view.Frame);
        Require(File.ReadAllBytes(Path.Combine(output, "focus-live.png")).AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "focus-replay.png"))), "Focus native pixels replay");
        view.Frame = new CGRect(0, 0, 320, 640);
        view.Render();
        Require(Find<UIView>(view, "bottom-bar").Frame.Width <= 320, "Navigation adapts to narrow bounds");
        CheckSessionLayout(view, output, "narrow");
        Capture(view, output, "narrow");
        view.Frame = new CGRect(0, 0, 820, 1020);
        view.Render();
        Require(Find<UIView>(view, "bottom-bar").Frame.Width <= 600, "Tablet navigation width is bounded");
        CheckSessionLayout(view, output, "tablet");
        Capture(view, output, "tablet");
        Require(callbackError is null, "Native drawing and action callbacks are guarded");
        File.WriteAllText(Path.Combine(output, "focus-report.json"),
            """
            {"passed":true,"offscreen":true,"nativeComponents":true,"reference":"image0",
             "stableTabs":true,"tabVisits":true,"noTabBackEntry":true,"staleRequestsIgnored":true,
             "retainedTabBar":true,"modalBlocksNavigation":true,"restoredTab":true,"replayedVisit":true,"systemTabBar":true,"nativeSelection":true,"durationSelection":true,"timerSubscription":true,"pause":true,
             "multiSelection":true,"roundedBottomDrawer":true,"modalIsolation":true,
             "closeAndBackdrop":true,"phone":true,"narrow":true,"tablet":true,
             "flexActions":true,"durationReflow":true,"retainedDurationButtons":true,
             "modelReplay":true,"pixelReplay":true,"simulatedIntegrationsDisclosed":true}
            """);
    }

    static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    static T Find<T>(UIView view, string key, string? label = null) where T : UIView => All(view).OfType<T>()
        .Single(child => child.AccessibilityIdentifier == key && (label is null || child.AccessibilityLabel == label));
    static void Tap(UIView view, string key)
    {
        var bar = Find<UITabBar>(view, "bottom-bar");
        var item = bar.Items!.SingleOrDefault(item => item.AccessibilityIdentifier == key);
        if (item is not null)
            bar.Delegate!.ItemSelected(bar, item);
        else
            Find<UIButton>(view, key).SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    }
    static void Capture(UIView view, string output, string state)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(output, $"focus-{state}.png"), png.ToArray());
    }
    static void Pump() => NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.03));
    static void PumpUntil(Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            Pump();
        Require(done(), "Native focus update delivered");
    }
    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
