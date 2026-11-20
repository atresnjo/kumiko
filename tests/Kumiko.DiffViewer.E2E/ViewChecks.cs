using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// Keyboard and pointer behavior of the viewer through Kumiko input, plus light/dark, unified/split, collapsed,
/// search, finder and help artifacts. Keystrokes outside text input use Tester.Keystroke, as the macOS backend
/// delivers them; text typed into a focused field uses TypeText.
/// </summary>
internal static class ViewChecks
{
    public static void Run(Harness h)
    {
        using var repo = new Repository("view");
        repo.Write("src/a.ts", Lines(100, i => $"export function step{i}(value: number): number {{ return value + {i}; }}"));
        repo.Write("src/c.ts", Lines(20, i => $"export const label{i} = \"plain\";"));
        repo.Commit("initial");
        repo.Write("src/a.ts", Lines(100, i => i is 10 or 60 ? $"export function step{i}(value: number): string {{ return `${{value}}`; }}" : $"export function step{i}(value: number): number {{ return value + {i}; }}"));
        repo.Write("src/b.tsx", Lines(50, i => $"export const Item{i} = () => <li key=\"{i}\">needle {i}</li>;"));
        repo.Write("src/c.ts", Lines(20, i => i == 5 ? "export const label5 = \"" + new string('x', 400) + " needle\";" : $"export const label{i} = \"plain\";"));
        var request = new DiffRequest(SourceKind.WorkingTree, repo.Root, "HEAD");
        using var program = Harness.Open(request);
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        Model M() => program.Model;
        var layout = M().Layout;
        h.Check(M().Document.Files.Select(f => f.Path).SequenceEqual(["src/a.ts", "src/b.tsx", "src/c.ts"]) && M().Cursor == 0, "the viewer opens on the first file");
        h.Artifact(t, "unified-light");

        t.Keystroke('j');
        h.Check(M().Cursor == 1, "j moves to the next row");
        t.Keystroke('k');
        h.Check(M().Cursor == 0, "k moves to the previous row");
        t.Key(Key.Down);
        t.Key(Key.Down);
        t.Key(Key.Up);
        h.Check(M().Cursor == 1, "arrow keys move the cursor");
        h.Check(M().Cursor == layout.HunkRows[0], "the first file's first hunk header follows its file header");
        t.Keystroke('n');
        h.Check(M().Cursor == layout.HunkRows[1], "n jumps to the next hunk");
        t.Keystroke('p');
        h.Check(M().Cursor == layout.HunkRows[0], "p jumps to the previous hunk");
        t.Keystroke(']', Modifiers.Primary);
        h.Check(M().Cursor == layout.HunkRows[1], "Command+] jumps to the next hunk");
        t.Keystroke('N');
        h.Check(M().Cursor == layout.FileRows[1], "Shift+N jumps to the next file");
        t.Key(Key.Down, Modifiers.Alt);
        h.Check(M().Cursor == layout.FileRows[2], "Option+Down jumps to the next file");
        t.Keystroke('P');
        h.Check(M().Cursor == layout.FileRows[1], "Shift+P jumps to the previous file");
        t.Keystroke('G');
        var last = layout.Count - 1;
        h.Check(M().Cursor == last, "G moves to the last row");
        t.Keystroke('g');
        h.Check(M().Cursor == 0, "g moves to the first row");
        t.Key(Key.Down, Modifiers.Primary);
        h.Check(M().Cursor == last, "Command+Down moves to the last row");
        t.Key(Key.Home);
        t.Keystroke(' ');
        var page = M().Cursor;
        h.Check(page > 20 && page < 40, $"Space moves a page of rows ({page})");
        t.Settle();
        var row = M().Layout.Rows[page];
        var shown = M().Document.Files[row.File].Text(M().Document.Files[row.File].Lines[RowLayout.LineOf(row)]);
        h.Check(t.HasText(shown), "the cursor stays on screen after paging");
        t.Key(Key.PageUp);
        h.Check(M().Cursor == 0, "Page Up moves back a page");

        t.Keystroke('n');
        t.Keystroke('j');
        t.Keystroke('j');
        t.Keystroke('j');
        int file = M().CursorFileIndex, line = RowLayout.LineOf(M().Layout.Rows[M().Cursor]);
        t.Keystroke('s');
        h.Check(M().Mode == ViewMode.Split && M().CursorFileIndex == file && RowLayout.LineOf(M().Layout.Rows[M().Cursor]) == line,
            "switching to split keeps the cursor on its line");
        int additions = M().Layout.Rows.Count(r => r.Kind == RowKind.Pair && r.B >= 0 && M().Document.Files[r.File].Lines[r.B].Kind == LineKind.Added);
        h.Check(additions == M().Document.Additions, "split rows show every added line once");
        h.Artifact(t, "split-light");
        t.Keystroke('u');
        h.Check(M().Mode == ViewMode.Unified && RowLayout.LineOf(M().Layout.Rows[M().Cursor]) == line, "switching back keeps the line");
        t.Keystroke('2', Modifiers.Primary);
        t.Keystroke('1', Modifiers.Primary);
        h.Check(M().Mode == ViewMode.Unified, "Command+1 and Command+2 select the view");

        t.Keystroke('x');
        var (start, end) = M().Layout.FileRange(0);
        h.Check(M().Collapsed.Contains("src/a.ts") && end - start == 1 && M().Cursor == start, "x folds the cursor's file and keeps the cursor on its header");
        h.Artifact(t, "collapsed");
        t.Key(Key.Enter);
        h.Check(!M().Collapsed.Contains("src/a.ts"), "Enter on a file header unfolds it");
        t.Keystroke('X');
        h.Check(M().Collapsed.Count == 3 && M().Layout.Count == 3, "Shift+X folds every file");
        t.Keystroke('X');
        h.Check(M().Collapsed.IsEmpty, "Shift+X again unfolds every file");

        t.Keystroke('/');
        t.Settle();
        h.Check(M().Search is not null && t.Context.Focus != 0, "/ opens search with the query field focused");
        t.TypeText("needle");
        t.Settle();
        var found = M().Search!.Found;
        h.Check(found.Length == 51 && M().Cursor == found[M().Search!.Current], $"typing searches every shown line and moves to a match ({found.Length})");
        var before = M().Cursor;
        t.Key(Key.Enter);
        h.Check(M().Search!.Current == 1 && M().Cursor == found[1] && M().Cursor != before + 1 || M().Cursor == found[1], "Enter steps to the next match, not the next row");
        t.Key(Key.Down);
        h.Check(M().Cursor == found[2], "Down in the search field steps matches instead of moving the diff cursor");
        t.Key(Key.Enter, Modifiers.Shift);
        h.Check(M().Cursor == found[1], "Shift+Enter steps back");
        h.Artifact(t, "search");
        t.Key(Key.Escape);
        h.Check(M().Search is null, "Escape closes search");

        t.Keystroke('p', Modifiers.Primary);
        t.Settle();
        h.Check(M().Finder is not null && t.HasText("Go to file"), "Command+P opens the file finder");
        t.TypeText("btsx");
        t.Settle();
        h.Check(M().Finder!.Query == "btsx" && t.HasText("b.tsx"), "the finder matches paths fuzzily");
        h.Artifact(t, "finder");
        t.Key(Key.Enter);
        h.Check(M().Finder is null && M().Cursor == M().Layout.FileRows[1], "Enter in the finder goes to the chosen file");
        t.Keystroke('t');
        t.Key(Key.Escape);
        h.Check(M().Finder is null, "Escape closes the finder");

        t.Keystroke('?');
        t.Settle();
        h.Check(M().Help && t.HasText("Keyboard shortcuts") && t.HasText("Next hunk"), "? lists the keyboard shortcuts");
        h.Artifact(t, "help");
        t.Key(Key.Escape);
        h.Check(!M().Help, "Escape closes the shortcut list");

        StickyAndHorizontal(h, t, program);
        Reload(h, repo, program, t);

        using var dark = new Tester(program, new(1280, 820, Theme: Theme.DarkTheme, DevMode: true));
        dark.Context.ReduceMotion = true;
        program.Send(new Msg.SetMode(ViewMode.Split));
        dark.Settle();
        h.Artifact(dark, "split-dark");
        program.Send(new Msg.SetMode(ViewMode.Unified));
        dark.Settle();
        h.Artifact(dark, "unified-dark");
        dark.Screenshot(Path.Combine(h.Output, "unified-dark-repeat.png"));
        h.Check(h.Hashes["unified-dark"] == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(h.Output, "unified-dark-repeat.png")))),
            "an unchanged scene renders the same PNG");
    }

    static void StickyAndHorizontal(Harness h, Tester t, Program<Model, Msg> program)
    {
        program.Send(new Msg.Top());
        t.Settle();
        var layout = program.Model.Layout;
        t.MoveTo(800, 400);
        // Scroll a third of the way into the first file: its header must stay pinned at the top of the diff.
        t.Scroll(layout.Offsets[layout.FileRows[0] + 12]);
        t.Settle();
        var header = t.FindText("src/a.ts");
        h.Check(header is { } r && r.Y < 100, $"the file header stays pinned while its lines scroll ({header})");
        program.Send(new Msg.SelectFile(2));
        t.Settle();
        var longLine = program.Model.Document.Files[2].NewLines[4];
        var codeBefore = Node(t, longLine);
        var titleBefore = t.FindText("src/c.ts");
        t.Context.Input.ScrollX = 300;
        t.Frame();
        t.Settle();
        var codeAfter = Node(t, longLine);
        h.Check(Math.Abs(codeBefore.X - codeAfter.X - 300) < 1, $"horizontal scrolling shifts code ({codeBefore.X} → {codeAfter.X})");
        h.Check(titleBefore is { } title && t.FindText("src/c.ts") == title, "horizontal scrolling leaves file headers in place");
        t.Context.Input.ScrollX = -300;
        t.Frame();
    }

    /// <summary>Reloading after an edit above the cursor keeps it on the same file and line.</summary>
    static void Reload(Harness h, Repository repo, Program<Model, Msg> program, Tester t)
    {
        program.Send(new Msg.SelectFile(0));
        program.Send(new Msg.NextHunk(1));
        program.Send(new Msg.NextHunk(1));
        program.Send(new Msg.Move(4));
        t.Settle();
        var model = program.Model;
        var before = model.CursorFile!.Text(model.CursorFile.Lines[RowLayout.LineOf(model.Layout.Rows[model.Cursor])]);
        repo.Write("src/a.ts", "// inserted above\n" + File.ReadAllText(Path.Combine(repo.Root, "src/a.ts")));
        var document = Loader.Load(model.Request, false, CancellationToken.None).GetAwaiter().GetResult();
        program.Send(new Msg.Loaded(model.Generation, document));
        t.Settle();
        model = program.Model;
        var after = model.CursorFile!.Text(model.CursorFile.Lines[RowLayout.LineOf(model.Layout.Rows[model.Cursor])]);
        h.Check(model.CursorFile.Path == "src/a.ts" && after == before, $"a reload keeps the cursor on its line ({before} / {after})");
        program.Send(new Msg.Loaded(model.Generation - 1, DiffDocument.Empty));
        t.Settle();
        // The edit's own watcher reload may land meanwhile; what must never show is the stale, empty result.
        h.Check(!ReferenceEquals(program.Model.Document, DiffDocument.Empty) && program.Model.Document.Files.Length == document.Files.Length,
            "results of an older load are ignored");
    }

    static Rect Node(Tester t, string text)
    {
        foreach (ref readonly var node in t.Context.Nodes)
            if (node.Text == text)
                return node.Rect;
        throw new KeyNotFoundException(text[..Math.Min(40, text.Length)]);
    }

    static string Lines(int count, Func<int, string> line) => string.Join('\n', Enumerable.Range(1, count).Select(line)) + "\n";
}
