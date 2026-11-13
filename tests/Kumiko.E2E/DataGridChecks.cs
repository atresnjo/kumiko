using System.Globalization;
using Kumiko.UI;

/// <summary>Data grid selection, clipboard, export, column, filter and grouping flows.</summary>
static class DataGridChecks
{
    sealed record Person(ulong Id, string Name, string Team, int Age);

    static readonly Person[] People =
    [
        new(1, "Ada", "Engineering", 36),
        new(2, "Grace", "Engineering", 45),
        new(3, "Linus", "Engineering", 28),
        new(4, "Marie \"Curie\"", "Research", 52),
        new(5, "Tab\tName", "Research", 40),
        new(6, "=SUM(A1)", "Finance", 30),
        new(7, "Line\nBreak", "Finance", 33),
        new(8, "Smith, John", "Finance", 41)
    ];

    public static void Run(string output, Action<bool, string> check)
    {
        CellMenu(check);
        var state = new DataGridState();
        var filter = "";
        string? group = null;
        DataGrid<Person>? grid = null;
        DataGrid<Person> Build(DataGridState snapshot)
        {
            var built = new DataGrid<Person>(p => p.Id,
                new("name", "Name", 160, p => p.Name),
                new("team", "Team", 140, p => p.Team),
                new("age", "Age", 80, p => p.Age.ToString(CultureInfo.InvariantCulture), Compare: (a, b) => a.Age.CompareTo(b.Age)))
            {
                Name = "People",
                MultiSelect = true,
                Filter = filter,
                GroupBy = group
            };
            built.SetRows(People);
            built.Restore(snapshot);
            return built;
        }
        void View(Context c)
        {
            grid = Build(state);
            grid.StateChanged += s => state = s;
            using var page = c.Div(key: "page").SizeFull().Padding(Space.S2);
            grid.Draw(c, "people");
            var captured = grid.Capture();
            if (captured != state)
                state = captured;
        }

        using var t = new Tester(View, new(Width: 640, Height: 420, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        var cell = (string name) => t.Get(Role.Cell, "Name: " + name).Rect;

        Click(t, cell("Ada"));
        check(grid!.SelectedIds.SequenceEqual([1UL]), "a click selects one row");
        Click(t, cell("Grace"), Modifiers.Primary);
        check(grid.SelectedIds.Order().SequenceEqual([1UL, 2UL]), "Primary-click adds a row to the selection");
        Click(t, cell("Linus"), Modifiers.Shift);
        check(grid.SelectedIds.Order().SequenceEqual([2UL, 3UL]), "Shift-click selects the range from the anchor");
        t.Key(Key.Down, Modifiers.Shift);
        t.Settle();
        check(grid.SelectedIds.Order().SequenceEqual([2UL, 3UL, 4UL]), "Shift+Down extends the selection");
        t.Key(Key.C, Modifiers.Primary);
        check(t.Clipboard == "Grace\tEngineering\t45\r\nLinus\tEngineering\t28\r\n\"Marie \"\"Curie\"\"\"\tResearch\t52",
            "Primary+C copies the selected rows as tab-separated text in screen order");
        t.Key(Key.A, Modifiers.Primary);
        t.Settle();
        check(grid.SelectedIds.Count == People.Length, "Primary+A selects every row");
        var again = Build(state);
        check(again.Capture() == state && grid.Capture() == grid.Capture(), "unchanged grid state captures as an equal snapshot");

        filter = "ENG";
        t.Settle();
        check(grid.Count == 3 && grid.TotalCount == 8 && grid.SelectedIds.Order().SequenceEqual([1UL, 2UL, 3UL]),
            "the quick filter matches displayed values case-insensitively and drops hidden rows from the selection");
        filter = "";
        t.Settle();

        t.Get(Role.ColumnHeader, "Age").Click();
        t.Settle();
        const string csv = "Name,Team,Age\r\n" +
            "Linus,Engineering,28\r\n" +
            "'=SUM(A1),Finance,30\r\n" +
            "\"Line\nBreak\",Finance,33\r\n" +
            "Ada,Engineering,36\r\n" +
            "Tab\tName,Research,40\r\n" +
            "\"Smith, John\",Finance,41\r\n" +
            "Grace,Engineering,45\r\n" +
            "\"Marie \"\"Curie\"\"\",Research,52\r\n";
        var exported = grid.ToCsv();
        File.WriteAllText(Path.Combine(output, "grid.csv"), exported);
        check(exported == csv, "CSV export follows the sort, quotes fields and neutralizes formulas");

        Focus(t, Role.ColumnHeader, "Age");
        t.Key(Key.Left, Modifiers.Primary | Modifiers.Shift);
        t.Settle();
        check(grid.ColumnOrder.SequenceEqual(["name", "age", "team"]), "Primary+Shift+Left moves a focused column");
        check(grid.ToCsv().StartsWith("Name,Age,Team\r\nLinus,28,Engineering", StringComparison.Ordinal), "export follows the column order");

        Focus(t, Role.ColumnHeader, "Name");
        t.Key(Key.Right, Modifiers.Alt);
        t.Settle();
        check(grid.ColumnWidth("name") == 176, "Alt+Right widens a focused column");
        var header = t.Get(Role.ColumnHeader, "Name").Rect;
        var y = header.Y + header.Height / 2;
        t.Press(header.Right - 2, y);
        t.MoveTo(header.Right + 38, y);
        t.Release(header.Right + 38, y);
        t.Settle();
        check(Math.Abs(grid.ColumnWidth("name") - 216) < .5f, "dragging a header edge resizes that column");
        header = t.Get(Role.ColumnHeader, "Name").Rect;
        t.Press(header.Right - 2, y);
        t.MoveTo(0, y);
        t.Release(0, y);
        t.Settle();
        check(grid.ColumnWidth("name") == 48, "a resized column stops at its minimum width");

        var sort = grid.SortColumn;
        var team = t.Get(Role.ColumnHeader, "Team").Rect;
        var age = t.Get(Role.ColumnHeader, "Age").Rect;
        t.Press(team.X + team.Width / 2, y);
        t.MoveTo(age.X + 4, y);
        t.Release(age.X + 4, y);
        t.Settle();
        check(grid.ColumnOrder.SequenceEqual(["name", "team", "age"]) && grid.SortColumn == sort,
            "dragging a header across its neighbour reorders without sorting");
        t.Screenshot(Path.Combine(output, "grid-columns.png"));

        group = "team";
        t.Settle();
        var groups = t.Context.Accessibility.Where(n => n.Role == Role.Row && n.Properties?.Expanded is not null).Select(n => n.Name).ToArray();
        check(groups.SequenceEqual(["Team: Engineering (3)", "Team: Finance (3)", "Team: Research (2)"]),
            "grouping adds one expanded group row per value in order");
        t.Get(Role.Row, "Team: Finance (3)").Click();
        t.Settle();
        check(state.CollapsedGroups.Contains("Finance") && !t.Context.Accessibility.Any(n => n.Name == "Name: Smith, John"),
            "collapsing a group removes its rows");
        check(Build(state).Capture() == state, "collapsed groups, widths and order survive the state snapshot");
        t.Screenshot(Path.Combine(output, "grid-groups.png"));
        File.WriteAllText(Path.Combine(output, "grid-groups.txt"), t.Dump());
        t.WriteWarnings(Path.Combine(output, "grid-groups.warnings.json"));
    }

    /// <summary>A right-clicked cell offers that column's actions; other cells fall back to the row's.</summary>
    static void CellMenu(Action<bool, string> check)
    {
        var ran = "";
        var grid = new DataGrid<Person>(p => p.Id, new("name", "Name", 160, p => p.Name), new("team", "Team", 140, p => p.Team))
        {
            Name = "People",
            RowActions = p => [new("open", "Open " + p.Name, () => ran = "open " + p.Name)],
            CellActions = (p, column) => column.Id == "team" ? [new("only", "Only " + p.Team, () => ran = p.Team)] : []
        };
        grid.SetRows(People);
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(Space.S2);
            grid.Draw(c, "people");
        }, new(Width: 640, Height: 420));
        t.Context.ReduceMotion = true;
        t.Settle();
        var team = t.Context.Accessibility.First(n => n.Role == Role.Cell && n.Name == "Team: Research").Rect;
        t.RightClick(team.X + team.Width / 2, team.Y + team.Height / 2);
        t.Settle();
        var items = t.Context.Accessibility.Where(n => n.Role == Role.MenuItem).Select(n => n.Name).ToArray();
        check(items is ["Only Research"], "right-clicking a cell offers that cell's actions");
        t.Get(Role.MenuItem, "Only Research").Click();
        t.Settle();
        check(ran == "Research", "a cell action runs for the clicked cell's row");
        var name = t.Get(Role.Cell, "Name: Grace").Rect;
        t.RightClick(name.X + name.Width / 2, name.Y + name.Height / 2);
        t.Settle();
        check(t.Context.Accessibility.Any(n => n.Role == Role.MenuItem && n.Name == "Open Grace"),
            "a cell without its own actions offers the row's actions");
    }

    static void Click(Tester t, Rect rect, Modifiers modifiers = Modifiers.None)
    {
        var x = rect.X + rect.Width / 2;
        var y = rect.Y + rect.Height / 2;
        t.Context.Input.Modifiers = modifiers;
        t.Press(x, y);
        t.Context.Input.Modifiers = modifiers;
        t.Release(x, y);
        t.Settle();
    }

    static void Focus(Tester t, Role role, string name)
    {
        t.Context.PerformAccessibilityAction(t.Get(role, name).Id, new(AccessibilityAction.Focus));
        t.Settle();
    }
}
