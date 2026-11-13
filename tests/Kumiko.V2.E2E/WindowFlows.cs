using System.Collections.Immutable;
using Kumiko;
using Kumiko.Runtime;
using Kumiko.UI;

static class WindowFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = WindowsApp.CreateProgram();
        program.DevMode = true;
        using var tester = new Tester(program, new(Width: 420, Height: 260, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Get(Role.Button, "Open inspector").Click();
        tester.Settle();
        var inspector = tester.In(new Win.Inspector());
        tester.Get(Role.Button, "Increment").Click();
        tester.Settle();
        check(program.Windows.Count == 2 && inspector.HasText("Shared: 1"), "an inspector reads changes from the same model on its next frame without input");
        var opened = program.Position;
        tester.Get(Role.Button, "Open editors").Click();
        tester.Settle();
        var first = tester.In(new Win.Editor(1));
        var second = tester.In(new Win.Editor(2));
        first.Get(Role.Button, "Select").Click();
        first.Get(Role.Button, "Edit shared document").Click();
        tester.Settle();
        check(first.HasText("Selection: 1") && second.HasText("Selection: 0") && second.HasText("Document: changed"),
            "two windows share a document and retain independent selection");
        check(program.History.Last().Source?.Window == program.Windows.Single(w => w.Title == "Editor 1").Key,
            "recorded UI messages identify their source window");
        first.Settle(5);
        second.Frame();
        var sourceFrame = first.Context.FrameId;
        var sourceTime = first.Context.Time;
        first.Context.PerformAction(first.Get(Role.TextField, "Shared text").Id, new(AccessibilityAction.SetValue, Text: "accessible"));
        second.Frame();
        check(program.Model.Document == "accessible" && program.History.Last().Frame == sourceFrame && program.History.Last().Time == sourceTime,
            "an accessibility edit drained by another window retains the originating frame and time");
        first.Get(Role.Button, "Edit shared document").Click();
        first.Get(Role.Button, "Close").Click();
        tester.Settle();
        check(program.Windows.Count == 3 && program.Model.Editors.Count == 1, "a window can close itself without disposing its context mid-frame or the shared program");
        var live = program.Position;
        tester.Step(0);
        check(tester.Windows.Count == 1 && program.IsPaused, "rewinding removes model-derived windows and pauses effects");
        tester.Step(live);
        check(tester.Windows.Count == 3 && tester.In(new Win.Editor(2)).HasText("Document: changed"), "forward navigation recreates windows from the selected immutable model");
        tester.Step(opened);
        tester.Step(live);
        var session = Path.Combine(output, "windows-session.jsonl");
        program.Save(session);
        program.SaveModel(Path.Combine(output, "windows-model.json"));
        using var replay = WindowsApp.CreateProgram();
        replay.Replay(session);
        using var replayTester = new Tester(replay, new(Width: 420, Height: 260));
        check(replay.Windows.Select(w => w.Key).SequenceEqual(program.Windows.Select(w => w.Key)) &&
            replayTester.In(new Win.Editor(2)).HasText("Document: changed"), "session replay restores the window set and each window's view");
        check(replay.History.Last().Source?.Window == program.History.Last().Source?.Window, "session round-trip retains source window identity");
        using var restored = WindowsApp.CreateProgram();
        check(restored.RestoreModel(Path.Combine(output, "windows-model.json")) && restored.Windows.Count == 3,
            "saved-model restore restores the window set without replaying effects");
        inspector = tester.In(new Win.Inspector());
        inspector.Screenshot(Path.Combine(output, "windows-inspector.png"));
        File.WriteAllText(Path.Combine(output, "windows-inspector.txt"), inspector.Dump());
        tester.In(new Win.Editor(2)).Screenshot(Path.Combine(output, "windows-editor.png"));

        using var app = new App(new("V2 hidden windows", QuitOnLastWindow: false, Hidden: true));
        var nativeErrors = new List<Exception>();
        app.UnhandledException += nativeErrors.Add;
        using var native = WindowsApp.CreateProgram();
        var persisted = Path.Combine(output, "windows-hot-reload.json");
        File.Delete(persisted);
        HotReload.Persist(native, app, persisted);
        app.NewWindows(native, new(Visible: false));
        native.Send(new WindowMsg.OpenInspector());
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 2 && app.Windows.All(w => !w.IsVisible), "hidden native windows reconcile without showing or activating applications");
        var idleFrames = app.Windows.Select(w => w.Context!.FrameId).ToArray();
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Select(w => w.Context!.FrameId).SequenceEqual(idleFrames), "idle hidden windows do not poll or redraw");
        var nativeInspector = app.Windows.Single(w => w.Options.Title == "Inspector");
        nativeInspector.Close();
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 2 && native.Model.Inspector, "Update can refuse a native close request");
        native.Send(new WindowMsg.AllowClose());
        app.RunFor(TimeSpan.FromMilliseconds(40));
        nativeInspector.Close();
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 1 && !native.Model.Inspector, "accepted close requests remove only that native window");
        native.Send(new WindowMsg.Increment());
        app.RunFor(TimeSpan.FromMilliseconds(40));
        check(native.Model.Count == 1, "closing a secondary native window leaves the program alive");
        native.Step(1);
        app.RunFor(TimeSpan.FromMilliseconds(60));
        check(app.Windows.Count == 2, "native time travel recreates closed windows");
        native.Save(Path.Combine(output, "windows-native-session.jsonl"));
        native.Send(new WindowMsg.Increment());
        native.ProcessMessages();
        native.Dispose();
        using var afterRestart = WindowsApp.CreateProgram();
        check(afterRestart.RestoreModel(persisted) && afterRestart.Model.Count == 1 && afterRestart.Windows.Count == 2,
            "hot reload flushes the final whole-program model before disposal");
        File.WriteAllLines(Path.Combine(output, "windows-native-errors.txt"), nativeErrors.Select(e => e.ToString()));
        check(nativeErrors.Count == 0, "hidden native lifecycle reports no callback or frame errors");
    }
}

internal abstract partial record Win
{
    public sealed record Main : Win;
    public sealed record Inspector : Win;
    public sealed record Editor(int Id) : Win;
}
internal sealed record WindowModel(int Count = 0, bool Inspector = false, string Document = "initial",
    ImmutableSortedDictionary<int, int>? EditorState = null, bool CanClose = false)
{
    public ImmutableSortedDictionary<int, int> Editors => EditorState ?? ImmutableSortedDictionary<int, int>.Empty;
}
internal abstract partial record WindowMsg
{
    public sealed record OpenInspector : WindowMsg;
    public sealed record Increment : WindowMsg;
    public sealed record OpenEditors : WindowMsg;
    public sealed record Select(int Id) : WindowMsg;
    public sealed record Edit(string Text = "changed") : WindowMsg;
    public sealed record Close(Win Window) : WindowMsg;
    public sealed record AllowClose : WindowMsg;
}
internal sealed partial class WindowsApp : IKumiko<WindowModel, WindowMsg>, IKumikoWindows<WindowModel, WindowMsg, Win>
{
    public static NavigationRoot<WindowMsg> WindowRoot(WindowModel model, Win window) => NavigationRoot<WindowMsg>.Create(
        NavigationHistory<Win>.Start(window), "window", route => new WindowsView(model, route).Map<WindowMsg>(static message => message),
        static _ => throw new InvalidOperationException("This window has one root screen."));
    public static WindowModel Init => new();
    public static (WindowModel, Cmd<WindowMsg>) Update(WindowModel model, WindowMsg message) => (message switch
    {
        WindowMsg.OpenInspector => model with { Inspector = true },
        WindowMsg.Increment => model with { Count = model.Count + 1 },
        WindowMsg.OpenEditors => model with { EditorState = model.Editors.SetItem(1, 0).SetItem(2, 0) },
        WindowMsg.Select select => model with { EditorState = model.Editors.SetItem(select.Id, model.Editors[select.Id] + 1) },
        WindowMsg.Edit edit => model with { Document = edit.Text },
        WindowMsg.Close { Window: Win.Editor editor } => model with { EditorState = model.Editors.Remove(editor.Id) },
        WindowMsg.Close { Window: Win.Inspector } when model.CanClose => model with { Inspector = false },
        WindowMsg.Close => model,
        WindowMsg.AllowClose => model with { CanClose = true },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<WindowMsg>.None);
    public static IEnumerable<WindowSpec<Win, WindowMsg>> Windows(WindowModel model)
    {
        yield return new(new Win.Main(), "Main", new WindowMsg.Close(new Win.Main()))
        {
            Width = 420,
            Height = 260
        };
        if (model.Inspector)
            yield return new(new Win.Inspector(), "Inspector", new WindowMsg.Close(new Win.Inspector()))
            {
                Width = 420,
                Height = 260
            };
        foreach (var id in model.Editors.Keys)
            yield return new(new Win.Editor(id), $"Editor {id}", new WindowMsg.Close(new Win.Editor(id)))
            {
                Width = 420,
                Height = 360
            };
    }
}
internal sealed class WindowsView(WindowModel model, Win window) : WindowScreen<WindowModel, WindowMsg, Win>(model, window)
{
    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Padding(16).Gap(12);
        switch (Window)
        {
            case Win.Main:
                Ui.Button("Open inspector", new WindowMsg.OpenInspector(), key: "inspector");
                Ui.Button("Increment", new WindowMsg.Increment(), key: "increment");
                Ui.Button("Open editors", new WindowMsg.OpenEditors(), key: "editors");
                break;
            case Win.Inspector:
                C.Text("Shared: " + Model.Count, key: "count");
                break;
            case Win.Editor editor:
                C.Text("Document: " + Model.Document, key: "document");
                C.Text("Selection: " + Model.Editors[editor.Id], key: "selection");
                Ui.TextField("shared-text", "Shared text", Model.Document, static text => new WindowMsg.Edit(text), new(Width: 340));
                Ui.Button("Select", new WindowMsg.Select(editor.Id), key: "select");
                Ui.Button("Edit shared document", new WindowMsg.Edit(), key: "edit");
                Ui.Button("Close", new WindowMsg.Close(editor), key: "close");
                break;
        }
    }
}
