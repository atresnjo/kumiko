using Kumiko.Examples.Workbench;
using Kumiko.UI;

static class WorkbenchFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var app = new InventoryWorkbench();
        app.LoadFixture(500);
        using var ui = new Tester(app.Program, new(Width: 1180, Height: 760, DevMode: true));
        ui.Screenshot(Path.Combine(output, "workbench-light.png"));
        ui.Get(Role.Cell, "Name: Precision bearing").Click();
        ui.Key(Key.F2);
        ui.Key(Key.A, Modifiers.Primary);
        ui.TypeText("Session edit");
        ui.Key(Key.Tab);
        check(ui.Context.IsEditing && ui.Context.Accessibility.Any(n => n.Name == "Edit Stock"), "Tab commits the draft and advances to the next editable cell");
        ui.Key(Key.Escape);
        check(app.Grid.SelectedRow?.Name == "Session edit", "Tab preserves the edit on the same record");
        ui.Get(Role.ComboBox, "Supplier").Click();
        ui.TypeText("north");
        ui.Screenshot(Path.Combine(output, "workbench-dropdown.png"));
        var selectedId = app.Grid.SelectedId;
        app.Theme = Theme.ShadcnDark;
        ui.Settle();
        check(app.SupplierPicker.IsOpen && app.Grid.SelectedId == selectedId, "live dark theme preserves selection and the open search popup");
        ui.Screenshot(Path.Combine(output, "workbench-dropdown-dark.png"));
        check(ui.Context.Warnings.All(w => w.Code != "overlap"), "window-level popups do not produce false overlap diagnostics");
        ui.Key(Key.Tab);
        check(ui.Context.Accessibility.Single(n => n.Id == ui.Context.Focus).Role == Role.Option, "Tab enters an option without stopping on popup scaffolding");
        ui.Key(Key.Tab);
        check(ui.Context.Accessibility.Single(n => n.Id == ui.Context.Focus).Name == "Search Supplier", "Tab wraps within the open popup");
        ui.MoveTo(900, 500);
        ui.Scroll(1200);
        check(ui.HasText("Session edit"), "wheel events behind a popup do not scroll the grid");
        ui.Key(Key.Escape);
        ui.Get(Role.Cell, "Name: Session edit").Click();
        var cell = ui.Get(Role.Cell, "Name: Session edit").Rect;
        ui.RightClick(cell.X + 10, cell.Y + 10);
        check(ui.Context.Accessibility.Any(n => n.Role == Role.Menu), "isolated right click opens the row context menu");
        ui.Key(Key.Escape);
        ui.Key(Key.F10, Modifiers.Shift);
        ui.Screenshot(Path.Combine(output, "workbench-menu-dark.png"));
        ui.Key(Key.Escape);
        app.Theme = Theme.ShadcnLight with
        {
            Accent = Color.Hex(0x6d28d9),
            AccentText = Color.White,
            FocusRing = Color.Hex(0x8b5cf6),
            Selection = Color.Hex(0xede9fe),
            SelectionText = Color.Hex(0x4c1d95),
            Controls = new(RowHeight: 32, Height: 32),
            Radius = new(Sm: 2, Md: 4, Lg: 6)
        };
        ui.Settle();
        check(app.Grid.SelectedId == selectedId && ui.HasText("Session edit"), "brand color and density changes retain record state");
        ui.Screenshot(Path.Combine(output, "workbench-violet-compact.png"));
        ui.Get(Role.Cell, "Stock: 24").Click();
        ui.Key(Key.F2);
        ui.Key(Key.A, Modifiers.Primary);
        ui.TypeText("invalid");
        ui.Key(Key.Tab);
        check(app.Grid.IsEditing && app.Grid.EditError is not null, "invalid drafts block Tab navigation and retain focus");
        ui.Screenshot(Path.Combine(output, "workbench-validation.png"));
        ui.Key(Key.Escape);
        var older = new TaskCompletionSource<IReadOnlyList<InventoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = new TaskCompletionSource<IReadOnlyList<InventoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = app.LoadAsync(_ => older.Task);
        Task second = app.LoadAsync(_ => newer.Task);
        ui.Settle();
        check(ui.HasText("Loading…"), "background loading exposes a cancellable state while the existing grid remains usable");
        newer.SetResult([new(1, "Server version", "PT-1", 40, "northstar", "New bay"), new(2, "New record", "PT-2", 4, "atlas", "Bay 2")]);
        second.GetAwaiter().GetResult();
        ui.Settle();
        check(ui.HasText("Session edit") && ui.HasText("New record"), "background results preserve local edits while adding server records");
        older.SetResult([new(9, "Stale record", "STALE", 1, "atlas", "Old bay")]);
        first.GetAwaiter().GetResult();
        ui.Settle();
        check(!ui.HasText("Stale record") && app.Grid.Count == 2, "a late superseded request cannot overwrite newer results");
        app.LoadAsync(_ => Task.FromException<IReadOnlyList<InventoryItem>>(new IOException("Example offline failure"))).GetAwaiter().GetResult();
        ui.Settle();
        check(ui.HasText("Retry") && app.LoadError is not null, "background failures keep existing data and offer retry");
        ui.Screenshot(Path.Combine(output, "workbench-load-error.png"));
        var cancelled = new TaskCompletionSource<IReadOnlyList<InventoryItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task last = app.LoadAsync(_ => cancelled.Task);
        app.CancelLoad();
        cancelled.SetResult([new(10, "Cancelled result", "CANCEL", 1, "atlas", "Bay")]);
        last.GetAwaiter().GetResult();
        ui.Settle();
        check(!app.IsLoading && app.Grid.Count == 2, "cancelled loading leaves the current inventory intact");
        ui.Get(Role.TextField, "Search inventory").Click();
        ui.TypeText("nothing matches this");
        check(ui.HasText("No records found"), "search has a recoverable empty state");
        ui.Resize(800, 620);
        ui.Screenshot(Path.Combine(output, "workbench-empty-narrow.png"));
        check(ui.Context.Warnings.All(w => w.Code != "overflow"), "the narrow workbench keeps controls inside their viewport");
        ui.Context.Blur();
        ui.MoveTo(-100, -100);
        ui.Frame(0);
        var session = Path.Combine(output, "workbench-session.jsonl");
        app.Program.Save(session);
        var expected = ui.Dump();
        using var replay = new InventoryWorkbench();
        replay.Program.Replay(session);
        using var restored = new Tester(replay.Program, new(Width: 800, Height: 620, DevMode: true));
        check(restored.Dump() == expected, "Workbench records and replays editing, theme, dropdown and loading messages without services");
        restored.Screenshot(Path.Combine(output, "workbench-replay.png"));
        File.WriteAllText(Path.Combine(output, "workbench-replay.txt"), restored.Dump());
    }
}
