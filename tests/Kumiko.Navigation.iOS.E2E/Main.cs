using System.Text.Json;
using CoreGraphics;
using CRMLite;
using Foundation;
using Kumiko.iOS;
using UIKit;

internal static class EntryPoint
{
    static int Main(string[] args)
    {
        try
        {
            var output = Environment.GetEnvironmentVariable("KUMIKO_IOS_ARTIFACTS") ?? args.Single();
            Directory.CreateDirectory(output);
            DesignFlow.Run(output);
            StackFlow.Run(output);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

internal static class StackFlow
{
    static readonly List<string> checks = new();
    static readonly List<NativeFrame> frames = new();
    static Func<CrmModel>? readModel;
    internal static void Run(string output)
    {
        var initial = Data.Initial();
        using var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
        readModel = () => program.Model;
        var errors = new List<Exception>();
        program.Record(Path.Combine(output, "navigation-recording.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
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
        Check(nav.ViewControllers!.Length == 1, "native root has identified controller");
        var edge = nav.InteractivePopGestureRecognizer!;
        var content = OperatingSystem.IsIOSVersionAtLeast(26) ? nav.InteractiveContentPopGestureRecognizer : null;
        Check(edge.WeakDelegate is { } stackDelegate && stackDelegate.Class.Name != "_UINavigationInteractiveTransition" &&
            (content is null || ReferenceEquals(content.WeakDelegate, stackDelegate)),
            "every system pop gesture, including the content swipe, consults the stack first");
        Capture(view, output, "root");
        Press(view, "Northwind Labs");
        Settle(view);
        Check(ScreenRoutes.Kind(program.Model.Screen) == ScreenKind.Company, "native list selection opens shared CRM company");
        var first = program.Model.Navigation.Top.Id;
        var beforeCancelled = program.History.Count;
        var recognizer = nav.InteractivePopGestureRecognizer!;
        var started = recognizer.Delegate!.ShouldBegin(recognizer);
        Check(started, "UIKit interactive-pop delegate admits a clean page");
        nav.Delegate!.WillShowViewController(nav, nav.ViewControllers![0], true);
        nav.Delegate.DidShowViewController(nav, nav.TopViewController!, true);
        Settle(view);
        Check(program.History.Count == beforeCancelled, "cancelled admitted swipe sends nothing");
        var editor = All(view).OfType<UITextView>().Single();
        editor.Text = "Pending native draft";
        // Deliberately do not send Changed: Back must settle native input before checking dirty state.
        Back(nav);
        Settle(view);
        Check(program.Model.Note == "Pending native draft" && program.Model.PendingNavigation is not null,
            "Back flushes native edits before evaluating unsaved guard");
        Check(nav.ViewControllers!.Length == 2, "guard keeps native controller and desired path aligned");
        program.Send(new CrmMsg.CancelNavigation());
        Settle(view);
        if (content is not null)
        {
            editor.Text = "Pending content swipe";
            Check(!content.Delegate!.ShouldBegin(content), "content swipe on an unsaved page does not begin");
            Settle(view);
            Check(program.Model.Note == "Pending content swipe" && program.Model.PendingNavigation is not null,
                "content swipe settles native input and asks for confirmation");
            program.Send(new CrmMsg.CancelNavigation());
            Settle(view);
        }
        Check(((UIButton)nav.TopViewController!.NavigationItem.LeftBarButtonItem!.CustomView!).Title(UIControlState.Normal) ==
            Kumiko.UI.UiStrings.English.Back, "native Back item uses localized framework text");
        for (var edit = 0; edit < 10; edit++)
        {
            editor.Text = "Draft committed while covered " + edit;
            editor.Delegate!.Changed(editor);
        }
        program.Send(new CrmMsg.OpenCompany("kestrel"));
        Settle(view);
        Check(program.Model.Navigation.Entries.Single(e => e.Id == first).Note == "Draft committed while covered 9",
            "programmatic covering commits pending input to the correct entry");
        Check(ReferenceEquals(editor, All(nav.ViewControllers![1].View!).OfType<UITextView>().Single()),
            "covered native editor is retained");
        Check(nav.ViewControllers[1].View!.AccessibilityElementsHidden && !editor.IsFirstResponder,
            "covered page is inaccessible and cannot keep keyboard");
        program.Send(new CrmMsg.OpenCompany("northwind"));
        Settle(view);
        Check(program.Model.Note == "" && program.Model.Navigation.Top.Id != first, "same company opens an independent visit");
        Capture(view, output, "repeated");
        Check(nav.ViewControllers!.Length == program.Model.Navigation.Entries.Length &&
            nav.TopViewController!.View!.AccessibilityIdentifier == "navigation-page-" + program.Model.Navigation.Top.Id,
            $"native stack shows the repeated visit on top (native {string.Join(",", nav.ViewControllers.Select(c => c.View!.AccessibilityIdentifier))}, model {string.Join(",", program.Model.Navigation.Entries.Select(e => e.Id))}, errors {string.Join(" | ", errors.Select(e => e.ToString()[..Math.Min(e.ToString().Length, 600)]))})");
        var repeated = All(nav.TopViewController!.View!).OfType<UITextView>().Single();
        for (var edit = 0; edit < 10; edit++)
        {
            repeated.Text = "Burst " + edit;
            repeated.Delegate!.Changed(repeated);
        }
        Back(nav); // Ten queued values exceed one frame's settle passes.
        Settle(view);
        Check(program.Model.Note == "Burst 9" && program.Model.PendingNavigation is not null,
            $"Back drains a queued text backlog before the guard (note '{program.Model.Note}', pending {program.Model.PendingNavigation is not null}, pages {program.Model.Navigation.Entries.Length})");
        program.Send(new CrmMsg.CancelNavigation());
        program.Send(new CrmMsg.SetNote(program.Model.Navigation.Top.Id, ""));
        Settle(view);
        var visits = program.Model.Navigation.Entries.Select(e => e.Id).ToArray();
        view.Frame = new CGRect(0, 0, 320, 640);
        Settle(view);
        Check(visits.SequenceEqual(program.Model.Navigation.Entries.Select(e => e.Id)) && nav.ViewControllers!.Length == visits.Length,
            "resize preserves the desired and native path with covered pages");
        Capture(view, output, "narrow");
        view.Frame = new CGRect(0, 0, 393, 759);
        Settle(view);
        Check(edge.Delegate!.ShouldBegin(edge), "clean repeated page permits native pop");
        // A real animated transition always has a coordinator; this one never completes or reports didShow.
        var abandoned = nav.ViewControllers![^2];
        nav.Delegate!.WillShowViewController(nav, abandoned, true);
        program.Send(new CrmMsg.OpenCompany("lumen"));
        Settle(view);
        Check(nav.ViewControllers!.Length == program.Model.Navigation.Entries.Length,
            "a transition that ended without didShow does not freeze reconciliation");
        nav.Delegate.DidShowViewController(nav, abandoned, true);
        Settle(view);
        Check(program.Model.CompanyId == "lumen", "late completion for a page no longer on top cannot remove newer work");
        program.Send(new CrmMsg.Back(program.Model.BackTo(program.Model.Navigation.Entries[^2].Id)));
        Settle(view);
        var history = program.History.Count;
        // UIKit's delegate didShow with the unchanged controller represents a cancelled transition.
        nav.Delegate!.DidShowViewController(nav, nav.TopViewController!, true);
        Settle(view);
        Check(program.History.Count == history, "cancelled native transition sends no message");
        nav.PopViewController(false);
        nav.Delegate!.DidShowViewController(nav, nav.TopViewController!, false);
        Settle(view);
        Check(program.Model.CompanyId == "kestrel" && nav.ViewControllers!.Length == program.Model.Navigation.Entries.Length &&
            program.History.Count == history + 1,
            "completed native pop updates model exactly once");
        var after = program.History.Count;
        nav.Delegate.DidShowViewController(nav, nav.TopViewController!, false);
        Settle(view);
        Check(program.History.Count == after, "duplicate completion is ignored");
        program.Send(new CrmMsg.Back(program.Model.BackTo(first)));
        Settle(view);
        Check(program.Model.Note == "Draft committed while covered 9" && ReferenceEquals(editor, All(view).OfType<UITextView>().First(v => v.Text == "Draft committed while covered 9")),
            "return restores original editor and draft");
        Capture(view, output, "restored");
        program.Send(new CrmMsg.Back(program.Model.BackTo(program.Model.Navigation.Entries[0].Id)));
        Settle(view);
        var confirmation = program.Model.PendingNavigation!.Request;
        program.Send(new CrmMsg.ConfirmNavigation(confirmation));
        Settle(view);
        Check(nav.ViewControllers!.Length == 1 && ScreenRoutes.Kind(program.Model.Screen) == ScreenKind.Companies, "confirmed Back reconciles root");
        Capture(view, output, "final");
        var count = program.History.Count;
        var staleListener = nav.Delegate!;
        program.Step(count - 2);
        Settle(view);
        nav = Owner(view);
        staleListener.DidShowViewController(nav, nav.TopViewController!, false);
        Settle(view);
        Check(program.History.Count == count, "time travel does not synthesize navigation messages");
        program.Step(count - 1);
        Settle(view);
        nav = Owner(view);
        Capture(view, output, "time-travel");
        Check(File.ReadAllBytes(Path.Combine(output, "final.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "time-travel.png"))),
            "time travel restores native appearance");
        program.Save(Path.Combine(output, "navigation-session.jsonl"));
        using var replay = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
        replay.Replay(Path.Combine(output, "navigation-session.jsonl"));
        using var replayView = new NativeView(replay) { Frame = view.Frame };
        view.RemoveFromSuperview();
        host.View.AddSubview(replayView);
        Settle(replayView);
        Capture(replayView, output, "replay");
        Check(File.ReadAllBytes(Path.Combine(output, "final.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "replay.png"))), "native replay pixels match");
        var savedModel = Path.Combine(output, "navigation-model.json");
        program.SaveModel(savedModel);
        program.Send(new CrmMsg.OpenCompany("northwind"));
        Settle(view);
        nav = Owner(view);
        Back(nav); // Queued native request must not survive a model restore.
        Check(program.RestoreModel(savedModel), "saved CRM navigation model restores");
        Settle(view);
        nav = Owner(view);
        // Restarted subscriptions may report desktop service status; no native or navigation message may follow.
        Check(ScreenRoutes.Kind(program.Model.Screen) == ScreenKind.Companies && nav.ViewControllers!.Length == 1 &&
            program.History.Skip(1).All(step => step.Source is null && step.Message is CrmMsg.Desktop),
            $"restore drops pending native callbacks and rebuilds root without messages (screen {program.Model.Screen}, native pages {nav.ViewControllers!.Length}, after restore {string.Join(", ", program.History.Skip(1).Select(step => step.Message))})");
        var root = program.Model.RootHistory;
        var path = program.Model.Navigation;
        program.Send(new CrmMsg.MoveRoot(new("other", root.Path.Revision, root.Current.Id, root.Current.Id)));
        program.Send(new CrmMsg.MoveRoot(new(CrmApp.RootHost, root.Path.Revision, root.Current.Id, root.Current.Id)));
        Settle(view);
        Check(ReferenceEquals(program.Model.RootHistory, root) && ReferenceEquals(program.Model.Navigation, path) && Owner(view).ViewControllers!.Length == 1,
            "foreign or nonadjacent root history requests change neither the screen nor the page path");
        var finalNativePages = nav.ViewControllers!.Length;
        replayView.Dispose();
        view.Dispose();
        Check(window.Hidden, "navigation verification never shows the private window");
        Check(errors.Count == 0, "native callbacks complete without escaped or swallowed errors");
        File.WriteAllText(Path.Combine(output, "nav-report.json"), JsonSerializer.Serialize(
            new NavigationReport(true, checks, program.Model.Navigation.Entries.Length, finalNativePages, frames), NavigationJson.Default.NavigationReport));
        Console.WriteLine($"PASS {checks.Count} UIKit navigation checks");
    }
    static void Check(bool value, string description)
    {
        if (!value)
            throw new InvalidOperationException(description);
        checks.Add(description);
    }
    static UINavigationController Owner(UIView view)
    {
        foreach (var child in All(view))
            for (UIResponder? responder = child; responder is not null; responder = responder.NextResponder)
                if (responder is UINavigationController nav)
                    return nav;
        throw new InvalidOperationException("No native navigation controller");
    }
    /// <summary>Sends the native primary action of the visible pressable row with this accessible name.</summary>
    internal static void Press(UIView view, string name)
    {
        var button = All(view).OfType<UIButton>().Single(b => b.AccessibilityLabel == name && b.Window is not null &&
            !b.AccessibilityElementsHidden);
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    }
    static void Back(UINavigationController nav)
    {
        // UIAction has no public Perform method. The stack's UIButton sends the same native primary action.
        var button = (UIButton)nav.TopViewController!.NavigationItem.LeftBarButtonItem!.CustomView!;
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    }
    internal static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    static void Settle(NativeView view)
    {
        for (var i = 0; i < 4; i++)
        {
            view.Render();
            if (readModel?.Invoke() is { } model)
                frames.Add(new(model.Navigation.Revision, model.Navigation.Entries.Select(e => e.Id).ToArray(),
                    Owner(view).ViewControllers!.Select(c => int.Parse(c.View!.AccessibilityIdentifier!["navigation-page-".Length..],
                        System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
        }
    }
    internal static void Capture(UIView view, string output, string name)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed");
        File.WriteAllBytes(Path.Combine(output, name + ".png"), png.ToArray());
        File.WriteAllLines(Path.Combine(output, name + ".txt"), All(view).Select(v => $"{v.GetType().Name} {v.AccessibilityIdentifier} {v.Frame}"));
    }
}
internal sealed record NativeFrame(long Revision, int[] Model, int[] Native);
internal sealed record NavigationReport(bool passed, List<string> Checks, int ModelPages, int NativePages, List<NativeFrame> Frames);
[System.Text.Json.Serialization.JsonSerializable(typeof(NavigationReport))]
internal partial class NavigationJson : System.Text.Json.Serialization.JsonSerializerContext;
