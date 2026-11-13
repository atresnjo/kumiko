using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// Zen mode through the keyboard: the chrome disappears, both sides show, and the window is held in full screen
/// through a recording stand-in. On macOS the real full-screen service then runs against a hidden window.
/// </summary>
internal static class ZenChecks
{
    public static void Run(Harness h)
    {
        using var repo = new Repository("zen");
        foreach (var name in new[] { "a", "b", "c" })
            repo.Write($"src/{name}.ts", Lines(i => $"export const {name}{i} = {i};"));
        repo.Commit("initial");
        foreach (var name in new[] { "a", "b", "c" })
            repo.Write($"src/{name}.ts", Lines(i => i is 12 or 30 ? $"export const {name}{i} = \"changed\";" : $"export const {name}{i} = {i};"));
        var window = new RecordingFullScreen();
        using var program = Harness.Open(new DiffRequest(SourceKind.WorkingTree, repo.Root, "HEAD"), services: window.Services());
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        Model M() => program.Model;
        // Zen mode reloads the diff to read its file whole; the load runs in the background.
        void Wait()
        {
            t.Settle();
            for (int i = 0; i < 1000 && M().Loading; i++)
            {
                Thread.Sleep(5);
                t.Settle(1);
            }
            t.Settle();
        }
        // Every one of the file's 40 lines is shown (the two changed ones as removed and added), and no other file is whole.
        bool Whole(int file) => M().Document.Files.Select((f, i) => f.Lines.Length == 42 == (i == file)).All(x => x) &&
            M().Layout.Rows.Count(r => r.Kind == RowKind.Pair) == 40;
        h.Check(t.HasText("Changed files") && t.HasText("Whitespace") && M().Mode == ViewMode.Unified, "the viewer starts with its chrome in the unified view");

        h.Check(M().Document.Files.All(f => f.Lines.Length < 40), "outside zen mode files show their hunks with context");
        t.Keystroke('z');
        Wait();
        h.Check(M().Zen is not null && M().Mode == ViewMode.Split, "z enters zen mode with the two sides of the diff");
        h.Check(Whole(0) && t.ContainsText("a1 = 1;") && M().CursorFile!.Text(M().CursorFile!.Lines[^1]).Contains("a40", StringComparison.Ordinal),
            "zen mode shows its file whole, unchanged lines far from the change included");
        h.Check(!t.HasText("Changed files") && !t.HasText("Whitespace") && !t.ContainsText("⌘K projects"),
            "zen mode hides the toolbar, sidebar and status line");
        h.Check(window.Entered == 1 && window.Holding, "zen mode takes the window full screen");
        // The new side of the cursor's row when it shows a change, such as "a30".
        string Change()
        {
            var row = M().Layout.Rows[M().Cursor];
            var file = M().CursorFile!;
            return row.Kind == RowKind.Pair && row.B >= 0 && file.Lines[row.B].Kind == LineKind.Added ? file.Text(file.Lines[row.B]).Split(' ')[2] : "";
        }
        t.Keystroke('n');
        h.Check(Change() == "a12", $"n jumps to the file's first change in zen mode ({Change()})");
        t.Keystroke('n');
        h.Check(Change() == "a30", "n jumps to the next change in the file");
        t.Keystroke('n');
        h.Check(Change() == "a30", "n stays on the file's last change");
        t.Keystroke('p');
        h.Check(Change() == "a12", "p jumps to the previous change");
        t.Keystroke(']');
        h.Check(Change() == "a30", "] jumps to the next change too");
        t.Key(Key.Right);
        Wait();
        h.Check(Only(1) && Whole(1), "Right shows the next file in zen mode");
        t.Key(Key.Left);
        Wait();
        h.Check(Only(0) && Whole(0), "Left shows the previous file in zen mode");
        t.Key(Key.Up, Modifiers.Primary);
        var pane = t.FindText("src/a.ts");
        h.Check(pane is { } header && header.X < 100, $"the diff starts at the window's left edge ({pane})");
        bool Only(int file) => M().Layout.Count > 0 && M().Layout.Rows.All(r => r.File == file) && M().CursorFileIndex == file;
        h.Check(Only(0) && !t.ContainsText("b.ts") && !t.ContainsText("c.ts"), "zen mode shows only the file under the cursor");
        h.Artifact(t, "zen");

        t.Keystroke('N');
        Wait();
        h.Check(Only(1) && t.ContainsText("b.ts") && !t.ContainsText("a.ts"), "Shift+N shows the next file alone in zen mode");
        h.Check(Whole(1), "the next file is shown whole");
        t.Keystroke('N');
        Wait();
        t.Keystroke('N');
        Wait();
        h.Check(Only(2) && Whole(2), "Shift+N stops at the last file");
        t.Keystroke('P');
        Wait();
        h.Check(Only(1), "Shift+P shows the previous file alone in zen mode");
        t.Keystroke('x');
        t.Settle();
        h.Check(Only(1) && M().Layout.Count > 3, "the zen file stays unfolded");
        t.Keystroke('x');
        t.Keystroke('p', Modifiers.Primary);
        t.Settle();
        t.TypeText("/c");
        t.Settle();
        t.Key(Key.Enter);
        Wait();
        h.Check(Only(2) && Whole(2) && M().Zen is not null, "the file finder shows the chosen file alone and whole in zen mode");

        t.Keystroke('p', Modifiers.Primary);
        t.Settle();
        t.TypeText("a.ts");
        t.Settle();
        h.Check(M().Finder is { Query: "a.ts" } && M().Zen is not null, "⌘P opens the file finder in zen mode and takes a query");
        t.Key(Key.Escape);
        t.Settle();
        h.Check(M().Finder is null && M().Zen is not null, "Escape closes the file finder and stays in zen mode");

        t.Keystroke('/');
        t.Settle();
        t.Key(Key.Escape);
        h.Check(M().Search is null && M().Zen is not null, "Escape closes search before it leaves zen mode");
        t.Key(Key.Escape);
        t.Settle();
        h.Check(M().Zen is null && M().Mode == ViewMode.Unified && t.HasText("Changed files"), "Escape leaves zen mode and restores the view and chrome");
        Wait();
        h.Check(M().Layout.Rows.Any(r => r.File == 0) && M().CursorFileIndex == 2, "leaving zen mode shows every file again and keeps the cursor in the zen file");
        h.Check(M().Document.Files.All(f => f.Lines.Length < 40), "leaving zen mode returns to hunks with context");
        h.Check(window.Left == 1 && !window.Holding, "leaving zen mode releases full screen");

        t.Keystroke('s');
        t.Keystroke('z');
        t.Keystroke('u');
        t.Keystroke('z');
        h.Check(M().Zen is null && M().Mode == ViewMode.Split, "zen mode returns to the view mode it started from");

        t.Keystroke('z');
        window.LeaveFromWindow();
        t.Settle();
        h.Check(M().Zen is null && t.HasText("Changed files") && !window.Holding, "leaving full screen from the window ends zen mode");

        window.Already = true;
        t.Keystroke('z');
        t.Keystroke('z');
        h.Check(window.Already, "a window that was already full screen stays full screen after zen mode");
        if (OperatingSystem.IsMacOS())
            NativeWindow(h, repo);
    }

    /// <summary>
    /// The app's real full-screen service on a hidden window, which is never shown or made full screen. Subscriptions
    /// start while Kumiko forbids window calls; a failure there aborted every later frame, so a palette query kept
    /// growing until the window froze.
    /// </summary>
    static void NativeWindow(Harness h, Repository repo)
    {
        var errors = new List<Exception>();
        using var app = new Kumiko.App(new("Diff zen E2E", Hidden: true));
        app.UnhandledException += errors.Add;
        var fullScreen = new WindowFullScreen(app);
        using var program = Harness.Open(new DiffRequest(SourceKind.WorkingTree, repo.Root, "HEAD"),
            services: new KumikoServices().Add<IFullScreen>(fullScreen));
        program.UnhandledException += errors.Add;
        using var window = app.NewWindow(new(Title: "Diff zen E2E", Width: 1280, Height: 820, Visible: false), program);
        fullScreen.Window = window;
        void Run() => app.RunFor(TimeSpan.FromMilliseconds(60));
        Run();
        program.Send(new Msg.ToggleZen());
        Run();
        program.Send(new Msg.OpenQuick());
        program.Send(new Msg.QuickQuery("zen"));
        Run();
        Run();
        h.Check(errors.Count == 0, $"zen mode holds the native window without errors ({errors.FirstOrDefault()?.Message})");
        h.Check(program.Model is { Zen: not null, Quick.Query: "zen" }, "the palette query in zen mode stays as typed");
        h.Check(!window.IsVisible && !window.IsFullScreen, "a hidden window is never made full screen");
        program.Send(new Msg.CloseOverlay());
        program.Send(new Msg.ToggleZen());
        Run();
        h.Check(errors.Count == 0 && program.Model.Zen is null, "leaving zen mode releases the native window without errors");
    }

    /// <summary>Records full-screen requests; <see cref="Already"/> stands for a window the user put in full screen.</summary>
    sealed class RecordingFullScreen : IFullScreen
    {
        Action? left;
        public int Entered
        {
            get; private set;
        }
        public int Left
        {
            get; private set;
        }
        public bool Holding => left is not null;
        public bool Already
        {
            get; set;
        }

        public KumikoServices Services() => new KumikoServices().Add<IFullScreen>(this);

        public IDisposable Hold(Action left)
        {
            bool already = Already;
            if (!already)
            {
                Entered++;
                Already = true;
            }
            this.left = left;
            return new Release(() =>
            {
                this.left = null;
                if (already)
                    return;
                Left++;
                Already = false;
            });
        }

        public void LeaveFromWindow()
        {
            Already = false;
            left?.Invoke();
        }

        sealed class Release(Action release) : IDisposable
        {
            public void Dispose() => release();
        }
    }

    static string Lines(Func<int, string> line) => string.Join('\n', Enumerable.Range(1, 40).Select(line)) + "\n";
}
