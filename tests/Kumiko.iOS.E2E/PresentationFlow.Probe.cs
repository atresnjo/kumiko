using System.Collections.Immutable;
using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;

internal static partial class PresentationFlow
{
    // This harness has no UIApplication, so UIKit starts presentations synchronously but never
    // completes their transitions. These checks cover what UIKit establishes before completion.
    static void Presented(List<string> facts)
    {
        using (var program = CatalogApp.CreateProgram())
        using (var host = new HiddenHost(program, 393, 852))
        {
            Press(host.View, "show-alert");
            PumpUntil(() => host.Presented is UIAlertController, "A windowed host presents the declared alert");
            var alert = (UIAlertController)host.Presented!;
            Require(alert.PresentingViewController == host.Controller && Find<UIView>(host.View, "discard-alert").Subviews.Length == 0,
                "UIKit presents the alert from the host controller, outside the overlay");
            facts.Add($"\"presented\":{{\"presenter\":\"{alert.PresentingViewController?.GetType().Name}\",\"transitionCompleted\":false}}");
            Fire(alert.Actions[1]);
            PumpUntil(() => program.Model.Presented is null && program.Model.LastPresentation == "discard", "Presented alert action reaches the model");
            Pump();
            Require(alert.Handle != IntPtr.Zero && host.Presented == alert,
                "A removed controller stays alive while its transition is outstanding");
            Require(host.Window.Hidden, "Presentation checks never show their window");
        }
        using (var program = CatalogApp.CreateProgram())
        using (var host = new HiddenHost(program, 393, 852))
        {
            program.Send(new CatalogMessage.Command("details"));
            program.Send(new CatalogMessage.Present("reset"));
            PumpUntil(() => host.Presented?.SheetPresentationController is not null, "The sheet beneath the alert presents");
            Pump();
            var sheet = host.Presented!;
            Require(sheet.PresentedViewController is null && Find<UIView>(host.View, "reset-alert").Subviews.Length == 0,
                "An alert waits while the sheet's presentation is outstanding");
            var edits = program.Model.Edits;
            Press(sheet.View!, "close");
            Pump();
            Require(program.Model.Edits == edits, "The waiting alert already makes the sheet inert");
        }
    }

    static void Regular(string output, List<string> facts)
    {
        using var program = CatalogApp.CreateProgram();
        using var host = new HiddenHost(program, 820, 1180, regular: true);
        Require(host.View.TraitCollection.HorizontalSizeClass == UIUserInterfaceSizeClass.Regular, "Probe host has regular width");
        Press(host.View, "show-delete");
        PumpUntil(() => host.Presented is UIAlertController, "Regular-width dialog presents");
        var dialog = (UIAlertController)host.Presented!;
        var source = Find<UIButton>(host.View, "show-delete");
        var popover = dialog.PopoverPresentationController!;
        facts.Add($"\"regularDialog\":{{\"presentation\":\"{dialog.PresentationController?.Class.Name}\",\"source\":\"{popover.SourceView?.AccessibilityIdentifier}\",\"rect\":\"{popover.SourceRect}\",\"arrows\":\"{popover.PermittedArrowDirections}\"}}");
        Require(dialog.PresentationController is UIPopoverPresentationController, "Regular-width dialog uses a popover presentation");
        Require(popover.SourceView == source && popover.SourceRect == source.Bounds &&
            popover.PermittedArrowDirections == UIPopoverArrowDirection.Any, "Popover anchors to the keyed source control");
        Scroll(host.View, "scroll", 1900);
        Capture(host.View, output, "regular");
        // UIKit runs the hidden Cancel action's handler for a tap outside the popover.
        Fire(dialog.Actions.Single(action => action.Style == UIAlertActionStyle.Cancel));
        PumpUntil(() => program.Model.Presented is null, "Outside-tap cancellation reaches the model");
        Require(program.Model.Dismissals == 1 && program.Model.LastPresentation == "dismissed",
            "Outside-tap cancellation sends one dismissal message");
    }

    static void Probes(List<string> facts)
    {
        using (var program = PresentationProbeApp.CreateProgram(new PresentationProbeModel(0)))
        using (var host = new HiddenHost(program, 820, 1180, regular: true))
        {
            PumpUntil(() => host.Presented is UIAlertController, "Anchored probe dialog presents");
            var popover = host.Presented!.PopoverPresentationController!;
            Require(popover.SourceView == Find<UIButton>(host.View, "source"), "Probe dialog anchors to its sibling source");
            program.Send(new PresentationProbeMessage.Toggle());
            PumpUntil(() => popover.SourceView == host.View && popover.PermittedArrowDirections == 0,
                "Removing the source re-anchors the popover to the host");
            Require(host.Presented is UIAlertController && program.Model.Messages == 0, "Re-anchoring keeps the dialog without messages");
        }
        using (var program = PresentationProbeApp.CreateProgram(new PresentationProbeModel(1)))
        using (var host = new HiddenHost(program, 820, 1180, regular: true))
        {
            PumpUntil(() => host.Presented is UIAlertController, "Unanchored probe dialog presents");
            var popover = host.Presented!.PopoverPresentationController!;
            facts.Add($"\"fallback\":{{\"source\":\"{popover.SourceView?.GetType().Name}\",\"rect\":\"{popover.SourceRect}\"}}");
            Require(popover.SourceView == host.View && popover.SourceRect.Width == 0 && popover.PermittedArrowDirections == 0,
                "A missing source falls back to the host center without crashing");
        }
        for (var scenario = 2; scenario <= 8; scenario++)
        {
            using var program = PresentationProbeApp.CreateProgram(new PresentationProbeModel(scenario));
            using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
            Exception? rejected = null;
            try
            {
                view.Render();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                rejected = error;
            }
            Require(rejected is not null, "Invalid presentation is rejected explicitly: " + scenario);
            facts.Add($"\"rejected{scenario}\":\"{rejected!.GetType().Name}\"");
        }
        foreach (var scenario in new[] { 9, 10 })
        {
            using var program = PresentationProbeApp.CreateProgram(new PresentationProbeModel(scenario));
            using var host = new HiddenHost(program, 393, 852);
            Exception? error = null;
            host.View.UnhandledException += exception => error = exception;
            PumpUntil(() => host.Presented is not null, "Probe presentation starts: " + scenario);
            var started = host.Presented!;
            // Dispose while UIKit's presentation transition is still outstanding.
            host.View.Dispose();
            Pump();
            Require(error is null && program.Model.Messages == 0 && host.Presented == started,
                "Disposing mid-transition sends nothing and leaves UIKit's transition intact: " + scenario);
        }
        using (var program = PresentationProbeApp.CreateProgram(new PresentationProbeModel(11)))
        using (var host = new HiddenHost(program, 393, 852))
        using (var foreign = new UIViewController())
        {
            host.View.UnhandledException += _ => { };
            host.Controller.PresentViewController(foreign, false, null);
            PumpUntil(() => host.Presented == foreign, "A foreign controller is presented");
            program.Send(new PresentationProbeMessage.Toggle());
            Exception? rejected = null;
            try
            {
                host.View.Render();
            }
            catch (InvalidOperationException error)
            {
                rejected = error;
            }
            Require(rejected is not null && host.Presented == foreign, "An alert above a foreign presentation is rejected explicitly");
        }
    }
}

internal sealed record PresentationProbeModel(int Scenario, bool Source = true, int Messages = 0);
internal abstract partial record PresentationProbeMessage
{
    public sealed record Act(string Id) : PresentationProbeMessage;
    public sealed record Dismissed : PresentationProbeMessage;
    public sealed record Toggle : PresentationProbeMessage;
}

internal sealed partial class PresentationProbeApp : IKumiko<PresentationProbeModel, PresentationProbeMessage>
{
    public static PresentationProbeModel Init => new(0);
    public static (PresentationProbeModel, Cmd<PresentationProbeMessage>) Update(PresentationProbeModel model,
        PresentationProbeMessage message) => (message is PresentationProbeMessage.Toggle
            ? model with
            {
                Source = !model.Source
            } : model with
            {
                Messages = model.Messages + 1
            }, Cmd<PresentationProbeMessage>.None);
    public static NavigationRoot<PresentationProbeMessage> Root(PresentationProbeModel model) => NavigationRoot<PresentationProbeMessage>.Create(
        NavigationHistory<string>.Start("root"), "probe", _ => new PresentationProbeScreen(model).Map<PresentationProbeMessage>(static m => m),
        static _ => new PresentationProbeMessage.Toggle());
}

internal sealed class PresentationProbeScreen(PresentationProbeModel model)
    : Kumiko.Runtime.Screen<PresentationProbeModel, PresentationProbeMessage>(model)
{
    static readonly ImmutableArray<NativeAlertAction> Actions = [new("go", "Continue"), new("cancel", "Cancel", NativeAlertActionStyle.Cancel)];

    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Padding(Space.S4).Gap(Space.S3);
        using (C.Div(key: "row").Row().FillWidth().Gap(Space.S2))
        {
            if (Model.Source)
                Ui.Button("Source", new PresentationProbeMessage.Toggle(), key: "source");
            if (Model.Scenario is 0 or 1)
                Ui.NativeConfirmationDialog("Probe", null, Actions, Model.Scenario == 0 ? "source" : "absent",
                    id => new PresentationProbeMessage.Act(id), new PresentationProbeMessage.Dismissed(), key: "dialog");
        }
        switch (Model.Scenario)
        {
            case 2:
                Alert("first");
                Alert("second");
                break;
            case 3:
                Alert("alert");
                using (Ui.NativeDrawer("Late sheet", new PresentationProbeMessage.Dismissed(), key: "drawer"))
                    C.Text("Content", key: "content");
                break;
            case 4:
                Ui.NativeConfirmationDialog("Probe", null, [new("go", "Continue")], "source",
                    id => new PresentationProbeMessage.Act(id), new PresentationProbeMessage.Dismissed(), key: "dialog");
                break;
            case 5:
                Ui.NativeAlert("Probe", null, [new("a", "One", NativeAlertActionStyle.Cancel), new("b", "Two", NativeAlertActionStyle.Cancel)],
                    id => new PresentationProbeMessage.Act(id), new PresentationProbeMessage.Dismissed(), key: "alert");
                break;
            case 6:
                Ui.NativeAlert("Probe", null, [new("same", "One"), new("same", "Two")],
                    id => new PresentationProbeMessage.Act(id), new PresentationProbeMessage.Dismissed(), key: "alert");
                break;
            case 7:
                Ui.NativeAlert("Probe", null, Actions, id => new PresentationProbeMessage.Act(id),
                    new PresentationProbeMessage.Dismissed(), [new("name", "")], key: "alert");
                break;
            case 8:
                using (Ui.NativeContextMenu("Empty", [], id => new PresentationProbeMessage.Act(id), key: "menu"))
                    C.Text("Content", key: "content");
                break;
            case 9:
                using (Ui.NativeDrawer("Sheet", new PresentationProbeMessage.Dismissed(), key: "drawer"))
                    C.Text("Content", key: "content");
                Alert("alert");
                break;
            case 10:
                Alert("alert");
                break;
            case 11 when !Model.Source:
                Alert("alert");
                break;
        }
    }

    void Alert(string key) => Ui.NativeAlert("Probe", "Message", Actions, static id => new PresentationProbeMessage.Act(id),
        new PresentationProbeMessage.Dismissed(), key: key);
}
