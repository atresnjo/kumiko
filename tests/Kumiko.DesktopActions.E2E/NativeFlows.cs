using DesktopNotes;
using Kumiko;
using Kumiko.Runtime;

static class NativeFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var app = new App(new("Desktop actions hidden", Hidden: true, QuitOnLastWindow: false));
        using var program = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(new MemoryNoteStore()));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        app.NewWindows(program, new(Visible: false));
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 2 && app.Windows.All(w => !w.IsVisible), "native command hosts remain hidden");
        check(app.Menu is not null && app.Menu.Find("pin") is not null, "hidden native host installs the typed menu projection");
        NativeMenuFlows.Run(app, program, output, check);
        var beforePin = program.History.Count(step => step.Message is NoteMessage.Pin);
        var oldPin = app.Menu!.Find("pin")!;
        oldPin.Click!(oldPin);
        oldPin.Click!(oldPin);
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(program.Model.Notes[1].Pinned && !program.Model.Notes[2].Pinned &&
            program.History.Count(step => step.Message is NoteMessage.Pin) == beforePin + 1, "native callback targets its named document once");
        oldPin.Click!(oldPin);
        app.RunFor(TimeSpan.FromMilliseconds(40));
        check(program.Model.Notes[1].Pinned, "old native menu callback expires after refresh");
        var frames = app.Windows.Select(w => w.Context!.FrameId).ToArray();
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Select(w => w.Context!.FrameId).SequenceEqual(frames), "desktop commands add no idle polling or redraw loop");
        program.Save(Path.Combine(output, "native-session.jsonl"));
        program.Send(new NoteMessage.Close(1));
        program.Send(new NoteMessage.Close(2));
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 0 && app.Menu?.Find("pin") is null,
            "closing the final document clears its native action menu");
        File.WriteAllLines(Path.Combine(output, "native-errors.txt"), errors.Select(e => e.ToString()));
        check(errors.Count == 0, "native command lifecycle has no unhandled callback errors");
    }
}
