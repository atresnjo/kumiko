using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using Todo;
using UIKit;

/// <summary>Todo's lists → detail stack: guarded Back, stale requests, native pops, replay and restore.</summary>
internal static class TodoFlow
{
    static readonly List<string> checks = [];

    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var program = TodoApp.CreateProgram();
        program.Record(Path.Combine(output, "todo-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        var errors = new List<Exception>();
        view.UnhandledException += errors.Add;
#pragma warning disable CA1422 // Private hidden window, never activated or shown.
        using var window = new UIWindow(new CGRect(0, 0, 393, 759));
#pragma warning restore CA1422
        using var host = new UIViewController();
        window.Hidden = true;
        window.RootViewController = host;
        host.LoadViewIfNeeded();
        window.AddSubview(host.View!);
        host.View!.Frame = window.Bounds;
        host.View.AddSubview(view);
        Settle(view);
        var nav = Owner(view);
        Check(nav.ViewControllers!.Length == 1 && program.Model.Page is TodoPage.Lists, "lists overview is the native root page");
        Capture(view, output, "lists");
        Select(view, 0);
        Settle(view);
        Check(program.Model.Page is TodoPage.Detail { Id: "work" } && nav.ViewControllers!.Length == 2, "selecting a list pushes its native detail page");
        Check(Has(nav.TopViewController!.View!, "draft"), "detail page renders the reusable TodoList component");
        Capture(view, output, "detail");
        var stale = program.Model.Path.RequestTo(program.Model.Path.Entries[0].Id, TodoApp.Stack);
        Back(nav);
        Settle(view);
        Check(program.Model.Path.Entries.Length == 1 && nav.ViewControllers!.Length == 1, "Back from a clean detail page pops without confirmation");
        Select(view, 1);
        Settle(view);
        var path = program.Model.Path;
        program.Send(new TodoMessage.Back(stale));
        program.Send(new TodoMessage.Back(path.RequestTo(path.Entries[0].Id, "other")));
        program.Send(new TodoMessage.Navigate(new("other", 0, 0, 0)));
        Settle(view);
        Check(ReferenceEquals(program.Model.Path, path) && program.Model.Pending is null && nav.ViewControllers!.Length == 2,
            "stale and foreign Back or history requests leave the newer page");
        Guard(program, view, output);
        Rename(program, view);
        nav = Owner(view);
        Select(view, 0);
        Settle(view);
        nav.PopViewController(false);
        nav.Delegate!.DidShowViewController(nav, nav.TopViewController!, false);
        Settle(view);
        Check(program.Model.Path.Entries.Length == 1 && nav.ViewControllers!.Length == 1, "completed native pop of a clean page updates the model once");
        Select(view, 0);
        Settle(view);
        program.SaveModel(Path.Combine(output, "todo-model.json"));
        Capture(view, output, "final");
        Replay(output, view, host);
        Restore(program, view, output);
        view.Dispose();
        Check(window.Hidden, "Todo verification never shows the private window");
        Check(errors.Count == 0, "native callbacks complete without escaped errors");
        File.WriteAllText(Path.Combine(output, "todo-report.json"),
            "{\"passed\":true,\"offscreen\":true,\"syntheticTouch\":false,\"checks\":[" +
            string.Join(",", checks.Select(check => "\"" + check + "\"")) + "]}");
        Console.WriteLine($"PASS {checks.Count} Todo navigation checks");
    }

    /// <summary>A new-task draft guards Back: Keep editing, edit-withdrawal and Discard.</summary>
    static void Guard(Program<TodoModel, TodoMessage> program, NativeView view, string output)
    {
        var nav = Owner(view);
        var field = Find<UITextField>(nav.TopViewController!.View!, "draft");
        field.Text = "Fix the gate";
        field.SendActionForControlEvents(UIControlEvent.EditingChanged);
        Back(nav);
        Settle(view);
        Check(Draft(program, "home") == "Fix the gate" && program.Model.Pending is not null && nav.ViewControllers!.Length == 2,
            "Back with a new-task draft asks for confirmation and keeps the native page");
        var edge = nav.InteractivePopGestureRecognizer!;
        Check(!edge.Delegate!.ShouldBegin(edge), "native swipe on a guarded page does not begin");
        Settle(view);
        Capture(view, output, "guard");
        Tap(nav, "keep");
        Settle(view);
        Check(program.Model.Pending is null && Draft(program, "home") == "Fix the gate" && nav.ViewControllers!.Length == 2,
            "Keep editing retains the draft and the page");
        Back(nav);
        Settle(view);
        field.Text = "Fix the gate today";
        field.SendActionForControlEvents(UIControlEvent.EditingChanged);
        Settle(view);
        Check(program.Model.Pending is null && Draft(program, "home") == "Fix the gate today", "editing the draft withdraws the confirmation");
        Back(nav);
        Settle(view);
        var pending = program.Model.Pending!.Request;
        program.Send(new TodoMessage.ConfirmBack(pending with
        {
            Revision = pending.Revision - 1
        }));
        Settle(view);
        Check(program.Model.Pending is not null && nav.ViewControllers!.Length == 2, "a confirmation for another revision is ignored");
        Tap(nav, "discard");
        Settle(view);
        Check(program.Model.Path.Entries.Length == 1 && Draft(program, "home") == "" && nav.ViewControllers!.Length == 1,
            "Discard and leave clears the draft and reconciles the native root");
        Capture(view, output, "discarded");
    }

    /// <summary>An in-place rename is also unsaved; discarding it keeps the original title.</summary>
    static void Rename(Program<TodoModel, TodoMessage> program, NativeView view)
    {
        Select(view, 0);
        Settle(view);
        var nav = Owner(view);
        All(nav.TopViewController!.View!).OfType<UIButton>().First(button => button.AccessibilityIdentifier == "edit")
            .SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        Settle(view);
        Back(nav);
        Settle(view);
        Check(program.Model.Pending is not null, "Back with an item rename in progress asks for confirmation");
        Tap(nav, "discard");
        Settle(view);
        var work = program.Model.Lists.Single(list => list.Id == "work").List;
        Check(work.Items.All(item => item.Draft is null) && work.Items[0].Title == "Write report" && program.Model.Path.Entries.Length == 1,
            "discarding a rename keeps the saved title");
    }

    /// <summary>Replays the session into a second view in the same hidden host, then restores the original view.</summary>
    static void Replay(string output, NativeView original, UIViewController host)
    {
        using var replay = TodoApp.CreateProgram();
        replay.Replay(Path.Combine(output, "todo-session.jsonl"));
        using var view = new NativeView(replay) { Frame = original.Frame };
        original.RemoveFromSuperview();
        host.View!.AddSubview(view);
        Settle(view);
        Capture(view, output, "replay");
        Check(replay.Model.Page is TodoPage.Detail { Id: "work" } && Owner(view).ViewControllers!.Length == 2 &&
            File.ReadAllBytes(Path.Combine(output, "todo-final.png")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, "todo-replay.png"))),
            "replay reproduces the page path and native pixels");
        view.RemoveFromSuperview();
        host.View.AddSubview(original);
        Settle(original);
    }

    static void Restore(Program<TodoModel, TodoMessage> program, NativeView view, string output)
    {
        Back(Owner(view));
        Settle(view);
        Check(program.RestoreModel(Path.Combine(output, "todo-model.json")), "saved Todo model restores");
        Settle(view);
        Check(program.Model.Page is TodoPage.Detail { Id: "work" } && program.History.Count == 1 && Owner(view).ViewControllers!.Length == 2,
            "restore rebuilds the saved detail page without messages");
    }

    static string Draft(Program<TodoModel, TodoMessage> program, string id) => program.Model.Lists.Single(list => list.Id == id).List.Draft;

    static void Select(NativeView view, int row)
    {
        var table = All(Owner(view).TopViewController!.View!).OfType<UITableView>().Single(table => table.AccessibilityIdentifier == "lists");
        using var index = NSIndexPath.FromRowSection(row, 0);
        table.Source!.RowSelected(table, index);
    }

    static void Back(UINavigationController nav)
    {
        var button = (UIButton)nav.TopViewController!.NavigationItem.LeftBarButtonItem!.CustomView!;
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    }

    static void Tap(UINavigationController nav, string key) =>
        Find<UIButton>(nav.TopViewController!.View!, key).SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);

    static void Check(bool value, string description)
    {
        if (!value)
            throw new InvalidOperationException(description);
        checks.Add(description);
        Console.WriteLine("PASS " + description);
    }

    static UINavigationController Owner(UIView view)
    {
        foreach (var child in All(view))
            for (UIResponder? responder = child; responder is not null; responder = responder.NextResponder)
                if (responder is UINavigationController nav)
                    return nav;
        throw new InvalidOperationException("No native navigation controller");
    }

    static bool Has(UIView view, string key) => All(view).Any(child => child.AccessibilityIdentifier == key);
    static T Find<T>(UIView view, string key) where T : UIView => All(view).OfType<T>().Single(child => child.AccessibilityIdentifier == key);
    static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));

    static void Settle(NativeView view)
    {
        for (var i = 0; i < 4; i++)
        {
            view.Render();
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
        }
    }

    static void Capture(UIView view, string output, string name)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed");
        File.WriteAllBytes(Path.Combine(output, $"todo-{name}.png"), png.ToArray());
        File.WriteAllLines(Path.Combine(output, $"todo-{name}.txt"), All(view).Select(v => $"{v.GetType().Name} {v.AccessibilityIdentifier} {v.Frame}"));
    }
}
