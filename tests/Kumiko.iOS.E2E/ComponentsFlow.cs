using System.Collections.Immutable;
using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static class ComponentsFlow
{
    internal static void Run(string output)
    {
        using var program = ComponentsApp.Create();
        var typed = (Program<ComponentsModel, ComponentsMessage>)program;
        program.Record(Path.Combine(output, "components-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 390, 780) };
        Exception? error = null;
        view.UnhandledException += exception => error = exception;
        view.Render();
        var table = Find<UITableView>(view, "items");
        Require(table.NumberOfRowsInSection(0) == 3, "Initial native list rows");
        Require(Find<UILabel>(view, "heading").Text == "My list", "Native heading");
        Capture(view, output, "phone");
        Tap(view, "add");
        PumpUntil(() => typed.Model.Items.Length == 4);
        PumpUntil(() => table.NumberOfRowsInSection(0) == 4);
        Require(ReferenceEquals(table, Find<UITableView>(view, "items")), "Table identity survives add");
        UIView panel;
        // UIKit temporaries otherwise remain in the executable's outer autorelease pool.
        using (var pool = new NSAutoreleasePool())
        {
            Select(table, 1);
            PumpUntil(() => typed.Model.SelectedId == "item-2");
            PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "panel"));
            Require(Find<UILabel>(view, "item-title").Text == "Take a walk", "Row domain ID opens matching details");
            Require(!table.UserInteractionEnabled && table.AccessibilityElementsHidden, "Modal blocks background interaction and accessibility");
            var count = typed.Model.Items.Length;
            Tap(view, "add");
            Select(table, 0);
            Pump();
            Require(typed.Model.Items.Length == count && typed.Model.SelectedId == "item-2", "Background native actions rejected");
            Capture(view, output, "drawer");
            view.Frame = new CGRect(0, 0, 320, 640);
            view.Render();
            panel = Find<UIView>(view, "panel");
            Require(panel.Frame.Right <= view.Bounds.Width && panel.Frame.Bottom <= view.Bounds.Height,
                "Drawer constrained after narrow resize");
            Require(Find<UIButton>(view, "close").Frame.Height >= 44, "Drawer close touch target");
            Capture(view, output, "narrow");
            Tap(view, "close");
            PumpUntil(() => typed.Model.SelectedId is null && table.UserInteractionEnabled);
        }
        PumpUntil(() => panel.Handle == IntPtr.Zero);
        Require(panel.Handle == IntPtr.Zero, "Removed drawer native lifetime ends after UIKit releases it");
        Select(table, 0);
        PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "details"));
        Tap(view, "details-backdrop");
        PumpUntil(() => typed.Model.SelectedId is null && table.UserInteractionEnabled);
        Require(!table.AccessibilityElementsHidden, "Background accessibility restored");
        while (!typed.Model.Items.IsEmpty)
        {
            Select(table, 0);
            PumpUntil(() => typed.Model.SelectedId is not null);
            PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "remove"));
            var before = typed.Model.Items.Length;
            Tap(view, "remove");
            PumpUntil(() => typed.Model.Items.Length == before - 1 && typed.Model.SelectedId is null);
            PumpUntil(() => table.NumberOfRowsInSection(0) == before - 1);
        }
        Require(Find<UILabel>(view, "empty").Text!.Contains("empty", StringComparison.Ordinal), "Empty list explains recovery");
        Capture(view, output, "empty");
        Tap(view, "add");
        PumpUntil(() => table.NumberOfRowsInSection(0) == 1);
        Require(typed.Model.Items[0].Id == "item-5", "Empty list recovers without reusing deleted identity");
        Capture(view, output, "live");
        using var replayProgram = ComponentsApp.Create();
        replayProgram.Replay(Path.Combine(output, "components-session.jsonl"));
        using var replay = new NativeView(replayProgram) { Frame = view.Frame };
        replay.Render();
        var replayModel = ((Program<ComponentsModel, ComponentsMessage>)replayProgram).Model;
        Require(replayModel.Items.SequenceEqual(typed.Model.Items) && replayModel.SelectedId == typed.Model.SelectedId,
            "Component session replay reproduces model");
        Capture(replay, output, "replay");
        Require(File.ReadAllBytes(Path.Combine(output, "components-live.png")).AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "components-replay.png"))), "Replay reproduces the native image");
        ScrollAndIdentity(view, typed, table);
        view.Frame = new CGRect(0, 0, 820, 1100);
        view.Render();
        Capture(view, output, "tablet");
        Select(table, 0);
        PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == "panel"));
        if (!OperatingSystem.IsIOSVersionAtLeast(17))
            throw new PlatformNotSupportedException("The offscreen appearance check requires trait overrides.");
        view.TraitOverrides.UserInterfaceStyle = UIUserInterfaceStyle.Dark;
        view.UpdateTraitsIfNeeded();
        view.Render();
        Require(view.TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark,
            "Dark traits apply to the detached host");
        Capture(view, output, "dark");
        Require(error is null, "Component callbacks remain guarded");
        view.Dispose();
        Pump();
        Require(error is null, "Queued component work ignores disposal");
        File.WriteAllText(Path.Combine(output, "components-report.json"),
            """
            {"passed":true,"renderer":"UIKit","offscreen":true,"nativeLabels":true,
             "nativeTable":true,"stableIdentity":true,"scrollRetention":true,"domainSelection":true,
             "modalIsolation":true,"closeAndBackdrop":true,"resize":true,"emptyRecovery":true,
             "disabledAndRemovedRows":true,"duplicateIdsRejected":true,"replay":true,
             "phone":true,"tablet":true,"dark":true}
            """);
    }

    static void ScrollAndIdentity(NativeView view, Program<ComponentsModel, ComponentsMessage> program, UITableView table)
    {
        for (var i = 0; i < 40; i++)
            program.Send(new ComponentsMessage.Add());
        view.Render();
        table.LayoutIfNeeded();
        table.SetContentOffset(new CGPoint(0, 320), false);
        view.Render();
        Require(table.ContentOffset.Y >= 300, "Unchanged render preserves native scroll");
        var offset = table.ContentOffset.Y;
        program.Send(new ComponentsMessage.Add());
        view.Render();
        Require(Math.Abs(table.ContentOffset.Y - offset) < 1, "Appending preserves native scroll");
        // Reorder the same records: selecting the first cell must use its new domain ID.
        program.Send(new ComponentsMessage.Close());
        var items = program.Model.Items.Reverse().ToImmutableArray();
        using var reordered = ProgramWith(items);
        using var other = new NativeView(reordered) { Frame = view.Frame };
        other.Render();
        var reorderedTable = Find<UITableView>(other, "items");
        Select(reorderedTable, 0);
        PumpUntil(() => reordered.Model.SelectedId == items[0].Id);
        Require(reordered.Model.SelectedId != program.Model.Items[0].Id, "Selection follows domain identity after reorder");
        using var disabled = ProgramWith([new("disabled", "Unavailable", Enabled: false)]);
        using var disabledView = new NativeView(disabled) { Frame = view.Frame };
        disabledView.Render();
        var disabledTable = Find<UITableView>(disabledView, "items");
        Select(disabledTable, 0);
        Pump();
        Require(disabled.Model.SelectedId is null, "Disabled row cannot select");
        var staleSource = disabledTable.Source!;
        disabledView.Dispose();
        using var index = NSIndexPath.FromRowSection(0, 0);
        staleSource.RowSelected(disabledTable, index);
        Pump();
        Require(disabled.Model.SelectedId is null, "Detached list delegate cannot select");
        using var duplicate = ProgramWith([new("same", "One"), new("same", "Two")]);
        using var duplicateView = new NativeView(duplicate) { Frame = view.Frame };
        var rejected = false;
        try
        {
            duplicateView.Render();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Require(rejected, "Duplicate domain IDs rejected before presentation");
    }

    static Program<ComponentsModel, ComponentsMessage> ProgramWith(ImmutableArray<NativeListItem> items) =>
        ComponentsApp.CreateProgram(new ComponentsModel(items));

    static IEnumerable<UIView> All(UIView root) => root.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    static T Find<T>(UIView view, string key) where T : UIView => All(view).OfType<T>()
        .Single(child => child.AccessibilityIdentifier == key);
    static void Tap(UIView view, string key) => Find<UIButton>(view, key)
        .SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    static void Select(UITableView table, int row)
    {
        using var index = NSIndexPath.FromRowSection(row, 0);
        table.Source!.RowSelected(table, index);
    }
    static void Capture(UIView view, string output, string state)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(output, $"components-{state}.png"), png.ToArray());
    }
    static void Pump() => NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.05));
    static void PumpUntil(Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            Pump();
        Require(done(), "Native event loop delivered component update");
    }
    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
