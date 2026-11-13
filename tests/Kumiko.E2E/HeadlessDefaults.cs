using Kumiko.Components;
using Kumiko.UI;

/// <summary>Unmodified controls retained as before/after PNG and semantic evidence.</summary>
static class HeadlessDefaults
{
    public static void Run(string output, Action<bool, string> check)
    {
        foreach (var name in new[] { "command", "tabs", "tree" })
        {
            using var t = new Tester(c => Render(c, name), new(Width: 520, Height: 420, Scale: 1));
            t.Context.ReduceMotion = true;
            t.Settle();
            t.Screenshot(Path.Combine(output, "parts-default-" + name + ".png"));
            File.WriteAllText(Path.Combine(output, "parts-default-" + name + ".txt"), t.Dump());
            check(t.HasText("Alpha") && t.HasText("Beta"), name + " default content renders");
        }
    }

    static void Render(Context c, string name)
    {
        using var root = c.Div(key: "root").SizeFull().Padding(16);
        var kit = c.Kit;
        switch (name)
        {
            case "command":
                kit.CommandMenu(new Rect(20, 20, 200, 20), "", _ => { },
                    [new("a", "Alpha", "People", Hint: "⌘A") { Checked = true }, new("b", "Beta") { Disabled = true }],
                    _ => { }, () => { }, key: "command");
                break;
            case "tabs":
                kit.Tabs("Plans", [new("a", "Alpha"), new("b", "Beta"), new("c", "Unavailable", true)], "a", _ => { }, key: "tabs");
                break;
            case "tree":
                kit.Tree("Files", [new("a", "Alpha", [new("b", "Beta")]), new("c", "Unavailable", Disabled: true)],
                    new HashSet<string> { "a" }, "b", (_, _) => { }, _ => { }, key: "tree");
                break;
        }
    }
}
