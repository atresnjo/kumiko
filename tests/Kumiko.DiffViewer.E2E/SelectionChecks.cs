using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// Text selection through the pointer: dragging over characters of a line selects them and every other place they
/// show, and the keys step between them. a.ts adds two log("trace") lines; b.ts adds one indented and moves a.ts's
/// removed "const a5 = 5;" in.
/// </summary>
internal static class SelectionChecks
{
    const string Log = "log(\"trace\");";

    public static void Run(Harness h)
    {
        using var repo = new Repository("selection");
        repo.Write("src/a.ts", Lines(i => $"const a{i} = {i};"));
        repo.Write("src/b.ts", Lines(i => $"const b{i} = {i};"));
        repo.Commit("initial");
        repo.Write("src/a.ts", Lines(i => i switch
        {
            5 => Log,
            20 => "log(\"trace\", 2);",
            _ => $"const a{i} = {i};"
        }));
        repo.Write("src/b.ts", Lines(i => i switch
        {
            10 => "    " + Log,
            20 => "const a5 = 5;",
            _ => $"const b{i} = {i};"
        }));
        using var program = Harness.Open(new DiffRequest(SourceKind.WorkingTree, repo.Root, "HEAD"));
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        Model M() => program.Model;
        string Shown()
        {
            var s = M().Selection!;
            var file = M().Document.Files[M().Layout.Rows[s.Row].File];
            return file.Path + ":" + file.Text(file.Lines[s.Line]);
        }

        Drag(t, Log, 5, 10);
        var selection = M().Selection;
        h.Check(selection is { Text: "trace", Dragging: false, Start: 5, End: 10 } && Shown() == "src/a.ts:" + Log, "dragging over characters selects them");
        h.Check(selection!.Found.Length == 3 && selection.Current == 0, $"every other place the text shows is found ({selection.Found.Length})");
        h.Check(t.HasText("trace") && t.ContainsText("1 of 3"), "the selection bar shows the text and which occurrence is selected");
        h.Artifact(t, "selection-unified");

        t.Keystroke('m');
        h.Check(M().Selection!.Current == 1 && Shown() == "src/a.ts:log(\"trace\", 2);" && M().Cursor == M().Selection!.Row, "m moves the selection to the next occurrence");
        h.Check(M().Selection is { Start: 5, End: 10 }, "the selection covers the occurrence's characters");
        t.Keystroke('m');
        h.Check(Shown() == "src/b.ts:    " + Log && M().Selection!.Start == 9 && t.ContainsText("3 of 3"), "m moves into the next file");
        t.Keystroke('m');
        h.Check(M().Selection!.Current == 0, "m wraps to the first occurrence");
        t.Keystroke('M');
        h.Check(M().Selection!.Current == 2, "Shift+M wraps back to the last occurrence");
        t.Keystroke('g', Modifiers.Primary);
        h.Check(M().Selection!.Current == 0, "Command+G steps occurrences while search is closed");
        t.Keystroke('G', Modifiers.Primary);
        h.Check(M().Selection!.Current == 2, "Shift+Command+G steps back");
        t.Keystroke('c', Modifiers.Primary);
        h.Check(t.Clipboard == "trace", "Command+C copies the selected text to the window's clipboard");

        t.Keystroke('g');
        t.Settle();
        Drag(t, Log, 12, 13);
        h.Check(M().Selection is { Text: ";" } single && single.Found.Length > 20, "a single character is selected and found everywhere");
        Click(t, Log, 4);
        h.Check(M().Selection is null && M().Layout.Rows[M().Cursor].Kind == RowKind.Line, "a click without a drag clears the selection and moves the cursor");

        Click(t, Log, 7, clicks: 2);
        h.Check(M().Selection is { Text: "trace", Found.Length: 3 }, "a double click selects the word");
        Click(t, Log, 3, modifiers: Modifiers.Shift);
        h.Check(M().Selection is { Anchor: 5, Head: 3, Text: "(\"" }, "Shift+click moves the selection's head");
        Click(t, Log, 2, clicks: 3);
        h.Check(M().Selection is { Text: Log, Found.Length: 2 }, "a triple click selects the line without its indentation");

        Drag(t, Log, 5, 10);
        t.Keystroke('s');
        h.Check(M().Mode == ViewMode.Split && M().Selection is { Text: "trace", Found.Length: 3 }, "switching to split finds the text in the new rows");
        h.Artifact(t, "selection-split");
        Drag(t, "const a5 = 5;", 6, 8);
        h.Check(M().Selection is { Text: "a5", Left: true, Found.Length: 2 }, "the left side of a split row selects the removed line and finds it moved in");
        t.Keystroke('m');
        h.Check(Shown() == "src/b.ts:const a5 = 5;" && !M().Selection!.Left, "stepping reaches the added copy on the right side");
        t.Keystroke('u');

        t.Keystroke('g');
        t.Settle();
        Drag(t, Log, 5, 10);
        t.Keystroke('x');
        h.Check(M().Selection is { Found.Length: 1 } && M().Collapsed.Contains("src/a.ts"), "folding a file drops its occurrences");
        t.Keystroke('x');
        h.Check(M().Selection is { Found.Length: 3 }, "unfolding finds them again");

        t.Keystroke('/');
        t.Settle();
        t.TypeText("const");
        t.Settle();
        int match = M().Search!.Current, occurrence = M().Selection!.Current;
        t.Keystroke('g', Modifiers.Primary);
        h.Check(M().Search!.Current == match + 1 && M().Selection!.Current == occurrence, "Command+G steps search matches while search is open");
        t.Key(Key.Escape);
        h.Check(M().Search is null && M().Selection is not null, "Escape closes search before the selection");
        t.Key(Key.Escape);
        h.Check(M().Selection is null, "Escape then clears the selection");

        using var dark = new Tester(program, new(1280, 820, Theme: Theme.DarkTheme, DevMode: true));
        dark.Context.ReduceMotion = true;
        dark.Settle();
        Drag(dark, Log, 5, 10);
        h.Check(M().Selection is { Text: "trace" }, "selection works in the dark theme");
        h.Artifact(dark, "selection-dark");
    }

    /// <summary>The x of the boundary before character <paramref name="column"/> of a shown line, nudged inside it.</summary>
    static (float X, float Y) At(Tester t, string text, int column)
    {
        var r = t.FindText(text) ?? throw new KeyNotFoundException(text);
        var style = new TextStyle(12, Mono: true);
        return (r.X + t.Context.TextSystem.Measure(text[..column], style).Width + 1, r.Y + r.Height / 2);
    }

    static void Drag(Tester t, string text, int from, int to)
    {
        var (x0, y) = At(t, text, from);
        var (x1, _) = At(t, text, to);
        t.Press(x0, y);
        t.MoveTo((x0 + x1) / 2, y);
        t.MoveTo(x1, y);
        t.Release(x1, y);
        t.Settle();
    }

    static void Click(Tester t, string text, int column, int clicks = 1, Modifiers modifiers = Modifiers.None)
    {
        var (x, y) = At(t, text, column);
        t.Context.Input.ClickCount = clicks;
        t.Context.Input.Modifiers = modifiers;
        t.Press(x, y);
        t.Context.Input.Modifiers = modifiers;
        t.Release(x, y);
        t.Context.Input.ClickCount = 1;
        t.Settle();
    }

    static string Lines(Func<int, string> line) => string.Join('\n', Enumerable.Range(1, 30).Select(line)) + "\n";
}
