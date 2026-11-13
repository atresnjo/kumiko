using Kumiko.Components;
using Kumiko.UI;

static class HeadlessChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        HeadlessDefaults.Run(output, check);
        HeadlessReviewChecks.Run(output, check);
        Command(output, check);
        Tabs(output, check);
        Tree(output, check);
        RejectNestedControls(check);
        PreserveBehavior(check);
    }

    static Tester Open(Action<Context> render)
    {
        var t = new Tester(c =>
        {
            using var root = c.Div(key: "root").SizeFull().Padding(16);
            render(c);
        }, new(Width: 520, Height: 420, Scale: 1, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        return t;
    }

    static AccessibleNode Node(Tester t, Role role, string name) =>
        t.Context.Accessibility.Single(n => n.Role == role && n.Name == name);

    static void Save(Tester t, string output, string name)
    {
        t.Screenshot(Path.Combine(output, "parts-custom-" + name + ".png"));
        File.WriteAllText(Path.Combine(output, "parts-custom-" + name + ".txt"), t.Dump());
        t.WriteWarnings(Path.Combine(output, "parts-custom-" + name + ".warnings.json"));
    }

    static void Inside(Tester t, Role role, string name, string content, Action<bool, string> check)
    {
        var row = Node(t, role, name).Rect;
        var text = t.Get(Role.Text, content).Rect;
        check(text.X >= row.X && text.Y >= row.Y && text.Right <= row.Right && text.Bottom <= row.Bottom,
            role + " custom content stays inside the component's hit target");
    }

    static void Command(string output, Action<bool, string> check)
    {
        string query = "", selected = "";
        var closed = false;
        var states = new Dictionary<string, CommandItemState>();
        CommandItem[] items = [new("a", "Ada") { Checked = true }, new("off", "Disabled") { Disabled = true }, new("b", "Bea", Keywords: "engineer")];
        var props = new CommandProps(Placeholder: "Find people")
        {
            RenderItem = (c, item, state) =>
            {
                states[item.Id] = state;
                using var row = c.Div(key: "content").Row().FillWidth().Height(48).ItemsCenter().Gap(8).Px(8);
                c.Kit.Avatar(item.Label, key: "avatar");
                using var labels = c.Div(key: "labels").Flex();
                c.Text(item.Label + " custom", key: "title");
                c.Text("Engineer · " + (state.Highlighted ? "active" : "idle"), new(Size: 11), key: "detail");
            }
        };
        using var t = Open(c => c.Kit.CommandMenu(new Rect(20, 20, 200, 20), query, value => query = value,
            items, value => selected = value, () => closed = true, props, key: "command"));
        Inside(t, Role.MenuItem, "Ada", "Ada custom", check);
        check(Node(t, Role.MenuItem, "Ada").Checked && !Node(t, Role.MenuItem, "Disabled").Enabled &&
            states["a"].Checked && states["off"].Disabled, "custom commands retain checked/disabled semantics and renderer state");
        t.Key(Key.Down);
        t.Settle();
        check(Node(t, Role.MenuItem, "Ada").Focused && states["a"] is { Focused: true, Highlighted: true },
            "Down from custom command search focuses and highlights the first enabled result");
        t.Key(Key.Down);
        t.Settle();
        check(Node(t, Role.MenuItem, "Bea").Focused, "custom command navigation skips disabled results");
        t.Key(Key.Up);
        t.Key(Key.Enter);
        check(selected == "a", "custom command Up/Enter selects the focused item once");
        selected = "";
        t.Get(Role.TextField, "Find people").Click();
        t.TypeText("engineer");
        t.Settle();
        check(t.HasText("Bea custom") && !t.HasText("Ada custom"), "custom commands use the component's filtering");
        t.Key(Key.Enter);
        check(selected == "b", "Enter in custom command search selects the first matching enabled result");
        Save(t, output, "command");
        t.Key(Key.Escape);
        check(closed, "Escape closes the custom command menu");

        using var embedded = Open(c => c.Kit.CommandMenu(null, "", _ => { }, items, value => selected = value, () => { },
            props with
            {
                Embedded = true,
                RenderSearch = ctx => ctx.TextField("", _ => { }, new(Name: "Embedded search"), key: "query")
            }, key: "embedded"));
        embedded.Get(Role.TextField, "Embedded search").Click();
        embedded.Key(Key.Down);
        embedded.Key(Key.Enter);
        check(selected == "a", "embedded custom search retains command navigation");
    }

    static void Tabs(string output, Action<bool, string> check)
    {
        var selected = "a";
        var states = new Dictionary<string, TabItemState>();
        using var t = Open(c => c.Kit.Tabs("Plans", [new("a", "Starter"), new("off", "Unavailable", true), new("b", "Team")],
            selected, value => selected = value, new TabsProps
            {
                RenderItem = (ctx, item, state) =>
                {
                    states[item.Id] = state;
                    ctx.Text(item.Label + " custom", key: "title");
                    ctx.Kit.Badge("3", key: "badge");
                }
            }, key: "tabs"));
        Inside(t, Role.Option, "Starter", "Starter custom", check);
        check(Node(t, Role.Option, "Starter").Properties?.Selected == true && !Node(t, Role.Option, "Unavailable").Enabled &&
            states["a"].Selected && states["off"].Disabled, "custom tabs retain selected/disabled semantics and renderer state");
        t.Key(Key.Tab);
        t.Key(Key.Right);
        t.Settle();
        check(selected == "b" && Node(t, Role.Option, "Team").Focused && states["b"] is { Selected: true, Focused: true, Highlighted: true, Hovered: false },
            "custom tab arrows skip disabled items and select/focus the next tab");
        t.Key(Key.Right);
        check(selected == "a", "custom tab arrows wrap");
        t.Key(Key.Left);
        check(selected == "b", "custom tab reverse navigation wraps past disabled items");
        Save(t, output, "tabs");
    }

    static void Tree(string output, Action<bool, string> check)
    {
        var expanded = new HashSet<string>();
        string selected = "root";
        var activated = "";
        var states = new Dictionary<string, TreeItemState>();
        TreeNode[] roots = [new("root", "People", [new("off", "Unavailable", Disabled: true), new("ada", "Ada")]), new("end", "End")];
        using var t = Open(c => c.Kit.Tree("Team", roots, expanded, selected,
            (id, open) => { if (open) expanded.Add(id); else expanded.Remove(id); }, id => selected = id,
            new TreeProps(Height: 180, Activated: id => activated = id)
            {
                RenderItem = (ctx, item, state) =>
                {
                    states[item.Id] = state;
                    ctx.Text(item.Label + " custom", key: "title");
                    ctx.Kit.Badge(state.HasChildren ? "Group" : "Person", key: "badge");
                }
            }, key: "tree"));
        Inside(t, Role.TreeItem, "People", "People custom", check);
        check(!t.HasText("Ada custom") && Node(t, Role.TreeItem, "People").Properties is { Selected: true, Expanded: false, Level: 1 },
            "custom trees retain selection, collapsed state and depth without building hidden children");
        t.Key(Key.Tab);
        t.Key(Key.Right);
        t.Settle();
        check(expanded.Contains("root") && states["root"] is { Expanded: true, HasChildren: true, Depth: 0 } &&
            states["ada"].Depth == 1 && states["off"].Disabled && !Node(t, Role.TreeItem, "Unavailable").Enabled,
            "custom tree expansion updates renderer state and disabled/depth semantics");
        t.Key(Key.Down);
        t.Settle();
        check(selected == "ada" && Node(t, Role.TreeItem, "Ada").Focused &&
            Node(t, Role.TreeItem, "Ada").Properties is { Selected: true, Level: 2 } && states["ada"] is { Selected: true, Focused: true, Highlighted: true, Hovered: false },
            "custom tree Down skips disabled children and selects/focuses the next item");
        t.Key(Key.Enter);
        check(activated == "ada", "custom tree Enter activates the leaf");
        t.Key(Key.Left);
        t.Key(Key.Left);
        t.Settle();
        check(selected == "root" && !expanded.Contains("root"), "custom tree Left returns to and collapses its parent");
        t.Key(Key.End);
        check(selected == "end", "custom tree End reaches the final row");
        Save(t, output, "tree");
    }

    static void PreserveBehavior(Action<bool, string> check)
    {
        var selected = "a";
        using var t = Open(c => c.Kit.Tabs("Content isolation", [new("a", "Alpha"), new("b", "Beta")], selected,
            id => selected = id, new TabsProps
            {
                RenderItem = (ctx, item, _) =>
                {
                    ctx.Input.Key = Key.None;
                    ctx.FocusControl(0);
                    ctx.FocusNextControl();
                    ctx.Text(item.Label, key: "label");
                }
            }, key: "tabs"));
        t.Key(Key.Tab);
        t.Key(Key.Right);
        check(selected == "b" && Node(t, Role.Option, "Beta").Focused,
            "content rendering cannot consume navigation keys or change the component's focus");
    }

    static void RejectNestedControls(Action<bool, string> check)
    {
        foreach (var kind in new[] { "command", "tabs", "tree" })
        {
            try
            {
                using var t = Open(c =>
                {
                    switch (kind)
                    {
                        case "command":
                            c.Kit.CommandMenu(null, "", _ => { }, [new("a", "Alpha")], _ => { }, () => { },
                                new CommandProps { RenderItem = (ctx, _, _) => ctx.Button("Nested") }, key: "command");
                            break;
                        case "tabs":
                            c.Kit.Tabs("Plans", [new("a", "Alpha")], "a", _ => { },
                                new TabsProps { RenderItem = (ctx, _, _) => ctx.Button("Nested") }, key: "tabs");
                            break;
                        default:
                            c.Kit.Tree("Files", [new("a", "Alpha")], new HashSet<string>(), "a", (_, _) => { }, _ => { },
                                new TreeProps { RenderItem = (ctx, _, _) => ctx.Button("Nested") }, key: "tree");
                            break;
                    }
                });
                check(false, kind + " renderer must reject nested controls");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("content renderer", StringComparison.Ordinal))
            {
                check(true, kind + " content cannot replace the component's keyboard/hit target");
            }
        }
    }
}
