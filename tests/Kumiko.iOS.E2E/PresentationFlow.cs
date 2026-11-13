using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;

internal static partial class PresentationFlow
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var facts = new List<string>();
        using var program = CatalogApp.CreateProgram();
        program.Record(Path.Combine(output, "presentation-session.jsonl"));
        // The detached host exercises the same controllers, actions and fields without a window.
        using var view = Detached(program);
        Exception? error = null;
        view.UnhandledException += exception => error = exception;
        ContextMenu(view, program, output, facts);
        Alerts(view, program, output, facts);
        Fields(view, program);
        AboveSheet(view, program, output);
        Require(error is null, "Presentation callbacks remain guarded");
        Scroll(view, "scroll", 1900);
        Capture(view, output, "live");
        using (var replayProgram = CatalogApp.CreateProgram())
        {
            replayProgram.Replay(Path.Combine(output, "presentation-session.jsonl"));
            using var replay = Detached(replayProgram);
            Require(replayProgram.Model == program.Model, "Presentation session replays to the same model");
            Scroll(replay, "scroll", 1900);
            Capture(replay, output, "replay");
        }
        Require(File.ReadAllBytes(Path.Combine(output, "presentation-live.png")).AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "presentation-replay.png"))), "Replay reproduces the visible catalog");
        Presented(facts);
        Regular(output, facts);
        Probes(facts);
        File.WriteAllText(Path.Combine(output, "presentation-native.json"), "{\n" + string.Join(",\n", facts) + "\n}\n");
        File.WriteAllText(Path.Combine(output, "presentation-report.json"),
            """
            {"passed":true,"offscreen":true,"syntheticTouch":false,"systemAlert":true,"actionOrderAndStyles":true,
             "exactlyOnce":true,"cancelDismisses":true,"disabledActionRejected":true,"modelRemovalSilent":true,
             "freshController":true,"disposedAfterRemoval":true,"modalIsolation":true,"boundFields":true,
             "editsBeforeAction":true,"alertAboveSheet":true,"realPresentation":true,"presentedFromSheet":true,
             "queuedBehindTransition":true,"retainedDuringTransition":true,"actionSheet":true,"popoverAnchor":true,
             "anchorFallback":true,"reanchor":true,"rejectedConflicts":true,"disposeMidTransition":true,
             "contextMenu":true,"replay":true,"completedTransitions":false}
            """);
    }

    static void ContextMenu(NativeView view, Program<CatalogModel, CatalogMessage> program, string output, List<string> facts)
    {
        var card = Find<UIView>(view, "context-card");
        Require(card.Interactions.OfType<UIContextMenuInteraction>().Count() == 1, "Context menu attaches one native interaction");
        var menu = Menu(card)!;
        var items = menu.Children.Cast<UIAction>().ToArray();
        facts.Add($"\"contextMenu\":[{string.Join(",", items.Select(item => $"\"{item.Title}:{item.Attributes}:{item.Image is not null}\""))}]");
        Require(menu.Title == "Sample" && items.Select(item => item.Title).SequenceEqual(CatalogApp.ContextMenu.Select(item => item.Label)),
            "Context menu keeps title and command order");
        Require(items.All(item => item.Image is not null), "Context menu commands show SF Symbols");
        Require(items[2].Attributes.HasFlag(UIMenuElementAttributes.Disabled) && items[3].Attributes.HasFlag(UIMenuElementAttributes.Destructive),
            "Context menu preserves disabled and destructive commands");
        var edits = program.Model.Edits;
        Perform(items[0]);
        PumpUntil(() => program.Model.Favorite, "Context menu command reaches the model");
        Pump();
        Require(program.Model.Edits == edits + 1 && program.Model.LastCommand == "favorite", "Context menu command sends exactly one message");
        Perform(items[2]);
        Pump();
        Require(program.Model.Edits == edits + 1, "Disabled context menu command is rejected");
        Scroll(view, "scroll", 1900);
        Capture(view, output, "phone");
    }

    static void Alerts(NativeView view, Program<CatalogModel, CatalogMessage> program, string output, List<string> facts)
    {
        Press(view, "show-alert");
        var alert = Alert(view, "discard-alert");
        facts.Add($"\"alert\":{{\"title\":\"{alert.Title}\",\"style\":\"{alert.PreferredStyle}\",\"actions\":[{Describe(alert)}]}}");
        Require(alert.PreferredStyle == UIAlertControllerStyle.Alert && alert.Title == "Discard note changes?" &&
            alert.Message == "The notes editor returns to its original text.", "Declared alert owns a configured UIAlertController");
        Require(alert.Actions.Select(action => action.Style).SequenceEqual([UIAlertActionStyle.Cancel, UIAlertActionStyle.Destructive]) &&
            alert.Actions.Select(action => action.Title).SequenceEqual(["Keep editing", "Discard"]), "Alert keeps action order and styles");
        var edits = program.Model.Edits;
        Press(view, "show-rename");
        Pump();
        Require(program.Model.Edits == edits && program.Model.Presented == "alert", "Alert makes background controls inert");
        Require(Menu(Find<UIView>(view, "context-card")) is null, "Alert blocks the background context menu");
        program.Send(new CatalogMessage.NotesChanged("Edited notes"));
        view.Render();
        Require(ReferenceEquals(alert, Alert(view, "discard-alert")), "Unrelated updates keep the alert controller");
        edits = program.Model.Edits;
        Fire(alert.Actions[1]);
        // A second handler call before the frame consumes the first is ignored.
        Fire(alert.Actions[0]);
        PumpUntil(() => program.Model.Presented is null, "Alert action reaches the model");
        Pump();
        Require(program.Model.Edits == edits + 1 && program.Model.LastPresentation == "discard" &&
            program.Model.Notes == CatalogApp.Init.Notes && program.Model.Dismissals == 0, "Alert action sends exactly one message");
        Require(alert.Handle == IntPtr.Zero && All(view).All(child => child.AccessibilityIdentifier != "discard-alert"),
            "Removed alert controller is disposed");
        Press(view, "show-alert");
        var again = Alert(view, "discard-alert");
        Require(!ReferenceEquals(again, alert), "Re-presenting creates a fresh controller");
        edits = program.Model.Edits;
        Fire(again.Actions[0]);
        PumpUntil(() => program.Model.Presented is null, "Cancel dismisses the alert");
        Require(program.Model.Dismissals == 1 && program.Model.Edits == edits + 1 && program.Model.LastPresentation == "dismissed",
            "Cancel sends exactly one dismissal message");
        Press(view, "show-alert");
        var removed = Alert(view, "discard-alert");
        edits = program.Model.Edits;
        program.Send(new CatalogMessage.Present("none"));
        view.Render();
        Pump();
        Require(removed.Handle == IntPtr.Zero && program.Model.Edits == edits + 1 && program.Model.Dismissals == 1,
            "Model removal disposes the alert without sending a message");
        Press(view, "show-delete");
        var dialog = Alert(view, "delete-dialog");
        facts.Add($"\"dialog\":{{\"style\":\"{dialog.PreferredStyle}\",\"actions\":[{Describe(dialog)}]}}");
        Require(dialog.PreferredStyle == UIAlertControllerStyle.ActionSheet && dialog.Title is null &&
            dialog.Message == "Delete the sample item?" && dialog.Actions.Length == 3, "Confirmation dialog owns an action sheet");
        Fire(dialog.Actions[1]);
        PumpUntil(() => program.Model.LastPresentation == "archive" && program.Model.Presented is null, "Dialog action reaches the model");
    }

    static void Fields(NativeView view, Program<CatalogModel, CatalogMessage> program)
    {
        Press(view, "show-rename");
        var alert = Alert(view, "rename-alert");
        var field = alert.TextFields![0];
        Require(field.Text == "Sample" && field.Placeholder == "Name" && field.AccessibilityIdentifier == "name",
            "Alert field shows its model value");
        var rename = alert.Actions[1];
        field.Text = "";
        field.SendActionForControlEvents(UIControlEvent.EditingChanged);
        PumpUntil(() => program.Model.Draft == "" && !rename.Enabled, "Field edits reach the model and disable Rename");
        var edits = program.Model.Edits;
        Fire(rename);
        Pump();
        Require(program.Model.Edits == edits && program.Model.Presented == "rename" && ReferenceEquals(alert, Alert(view, "rename-alert")),
            "Disabled alert action cannot fire");
        program.Send(new CatalogMessage.DraftChanged("From model"));
        view.Render();
        Require(field.Text == "From model" && rename.Enabled, "Model updates reach the alert field");
        field.Text = "Renamed";
        field.SendActionForControlEvents(UIControlEvent.EditingChanged);
        Fire(rename);
        PumpUntil(() => program.Model.Presented is null, "Rename finishes the alert");
        Require(program.Model.Name == "Renamed" && program.Model.Draft == "Renamed", "A field edit arrives before the action that follows it");
        view.Render();
        Require(Menu(Find<UIView>(view, "context-card"))?.Title == "Renamed", "Context menu follows model changes");
    }

    static void AboveSheet(NativeView view, Program<CatalogModel, CatalogMessage> program, string output)
    {
        program.Send(new CatalogMessage.Command("details"));
        program.Send(new CatalogMessage.Present("reset"));
        view.Render();
        var alert = Alert(view, "reset-alert");
        Require(All(view).Any(child => child.AccessibilityIdentifier == "panel"), "Alert is declared above an open sheet");
        Capture(view, output, "sheet");
        var edits = program.Model.Edits;
        Press(view, "close");
        Pump();
        Require(program.Model.Edits == edits && program.Model.Details, "Alert makes the sheet beneath it inert");
        Fire(alert.Actions[0]);
        PumpUntil(() => program.Model.Presented is null, "Cancel closes only the alert");
        Require(program.Model.Details, "Sheet survives its alert");
        Press(view, "show-reset");
        Fire(Alert(view, "reset-alert").Actions[1]);
        PumpUntil(() => !program.Model.Details && program.Model.LastPresentation == "reset", "Reset closes the alert and the sheet");
        view.Render();
    }
}
