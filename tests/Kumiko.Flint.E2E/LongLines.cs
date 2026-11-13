using Kumiko.UI;

/// <summary>
/// Generated and minified files put a megabyte on one diff line. The diff has no horizontal scrolling, so the frame
/// and the memory it keeps must follow the viewport, not the line length.
/// </summary>
static class LongLines
{
    public static void Run(Harness h, Repositories fixture)
    {
        var tester = h.Tester;
        var path = Path.Combine(fixture.Repo, "minified.js");
        var line = string.Concat(Enumerable.Repeat("var a=1;", 150_000));
        File.WriteAllText(path, line + "\n");
        h.Press(Key.R, Modifiers.Primary);
        h.Click("Unstaged: minified.js");
        h.Check(h.Tab.Lines.Any(l => l.Kind == '+' && l.Text == line), "A megabyte line reaches the diff model intact");
        h.Check(tester.ContainsText("var a=1;var a=1;"), "The diff shows the start of a megabyte line");
        h.Check(tester.Context.Stats.Instances < 20_000, $"A megabyte line paints a viewport-sized frame ({tester.Context.Stats.Instances} instances)");
        h.Check(tester.Context.RetainedInstanceBytes < 8 << 20, "A megabyte line keeps no megabyte paint buffers");
        h.Check(tester.Context.Nodes.ToArray().All(n => (n.Text?.Length ?? 0) < 10_000), "The diff lays out only the displayable part of a megabyte line");
        h.Shot("long-line");
        File.Delete(path);
        h.Press(Key.R, Modifiers.Primary);
        h.Check(!h.Tab.Snapshot.Unstaged.Any(f => f.Path == "minified.js"), "Removing the long file clears it from the working tree");
    }
}
