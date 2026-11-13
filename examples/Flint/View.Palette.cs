using System.Collections.Immutable;
using Flint.Git;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    bool Prompt => Model.Palette is PaletteMode.NewBranch or PaletteMode.NewWorkspace or PaletteMode.RenameWorkspace;
    string PromptTitle() => Model.Palette switch
    {
        PaletteMode.NewBranch => Model.PaletteTarget is { } from ? "New branch from " + from : "New branch from HEAD",
        PaletteMode.NewWorkspace => "New workspace",
        _ => $"Rename workspace \"{Model.Workspaces.FirstOrDefault(w => w.Id == (Model.PaletteTarget ?? Model.ActiveWorkspace))?.Name}\""
    };
    (string Name, string Placeholder) PaletteField() => Model.Palette switch
    {
        PaletteMode.Open => ("Open repository", "Open repository…"),
        PaletteMode.Branch => ("Checkout branch", "Checkout branch…"),
        PaletteMode.Workspaces => ("Switch workspace", "Switch workspace…"),
        PaletteMode.Account => ("GitHub account", "Choose the GitHub account for this workspace…"),
        PaletteMode.NewBranch => ("Branch name", "feat/my-branch"),
        PaletteMode.NewWorkspace or PaletteMode.RenameWorkspace => ("Workspace name", "Workspace name"),
        _ => ("Quick Launch", "Search repositories, branches, commands…")
    };
    /// <summary>The original Palette: a 680 px sheet with a 48 px input, grouped results and inline row actions.</summary>
    void PaletteView()
    {
        var results = Prompt ? [] : Results();
        var highlight = Math.Clamp(Model.Highlight, 0, Math.Max(0, results.Length - 1));
        using var overlay = C.Overlay("palette").Items(Align.Center).Padding(new Edges(90, 24, 24, 24)).Background(Overlay).Semantics(Role.Group);
        var backdrop = overlay.Control();
        if (backdrop.Clicked || C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.Cancel());
        PaletteKeys(results, highlight);
        var (name, placeholder) = PaletteField();
        using var panel = C.Div(key: "panel").Width(MathF.Min(680, C.Size.Width - 80)).MaxHeight(MathF.Min(520, C.Size.Height - 160))
            .Rounded(12).Background(Canvas).Border(BorderStrong).Shadow(48, 18, Color.Black.Alpha(.25f)).Clip()
            .Semantics(Role.Dialog, Prompt ? PromptTitle() : name).TabStop(false);
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        if (Prompt)
            using (C.Div(key: "prompt-title").Height(28).Row().ItemsCenter().Padding(new Edges(6, 0, 0, 16)))
                Label(PromptTitle(), "text", 12, Muted, Weight.Semibold);
        using (C.Div(key: "input").FillWidth().Height(48).Row().ItemsCenter().Gap(10).Px(16))
        {
            C.Icon(Prompt ? Icons.Branch : Icons.Search, 16, Muted, key: "icon");
            Field("query", name, Model.Query, static text => new Msg.QueryChanged(text), placeholder, MathF.Min(680, C.Size.Width - 80) - 96, 30, 16, borderless: true);
            Label("esc", "esc", 11, Muted);
        }
        if (Model.Error.Length > 0)
            using (C.Div(key: "error").FillWidth().MaxHeight(80).Scroll().Padding(new Edges(0, 16, 8, 16)))
                C.Text(Model.Error, new(Size: 12, Color: Red, Wrap: true), key: "text");
        if (Prompt)
        {
            using var hint = C.Div(key: "hint").Height(34).Row().ItemsCenter().Padding(new Edges(0, 0, 0, 16));
            Label("Press ↵ to confirm", "text", 12, Muted);
            return;
        }
        Rule("input-rule");
        if (results.IsEmpty)
        {
            using var empty = C.Div(key: "empty").FillWidth().Height(54).Center();
            Label(Model.Palette == PaletteMode.Open && Model.Known.IsEmpty ? "Scanning for repositories… or type a path" : "No results", "text", 13, Muted);
            return;
        }
        var grouped = Model.Query.Length == 0;
        var groups = grouped ? results.Select(r => r.Group).Distinct().Count() : 0;
        using var list = C.Div(key: "results").FillWidth().Height(MathF.Min(MathF.Min(440, results.Length * 30 + groups * 24 + 12), C.Size.Height - 260))
            .Scroll().Padding(6).Semantics(Role.List, "Results");
        var last = "";
        for (var i = 0; i < results.Length; i++)
        {
            var item = results[i];
            using var entry = C.Div(key: item.Id).FillWidth();
            if (grouped && item.Group != last)
                using (C.Div(key: "group").Height(24).Row().ItemsCenter().Padding(new Edges(4, 0, 0, 18)))
                    Label(item.Group.ToUpperInvariant(), "text", 11, Muted, Weight.Semibold);
            last = item.Group;
            ResultRow(item, i == highlight);
        }
    }
    void ResultRow(LaunchItem item, bool selected)
    {
        using var row = C.Div(key: "row").FillWidth().Height(30).Row().ItemsCenter().Gap(10).Px(12).Rounded(7);
        var hit = row.Semantics(Role.Button, item.Label).Control();
        if (selected)
            row.Background(Accent);
        else if (hit.Hovered)
            row.Background(Hover);
        if (hit.Clicked)
            RunItem(item);
        var ink = selected ? Color.White : Text;
        C.Icon(item.Icon, 14, selected ? Color.White : Muted, key: "icon");
        using (C.Div(key: "label").MaxWidth(360).Clip())
            Label(item.Label, "text", 13, ink, Weight.Medium);
        using (C.Div(key: "detail").Flex().MinWidth(0).Clip())
            Label(item.Detail, "text", 12, selected ? SelectedMuted : Muted);
        if (item.Shortcut.Length > 0)
            Label(item.Shortcut, "shortcut", 11, selected ? SelectedMuted : Muted);
        if (!selected)
            return;
        foreach (var action in item.Actions)
        {
            var armed = Model.Armed == item.Id + "/" + action.Id;
            using var chip = C.Div(key: action.Id).Row().ItemsCenter().Gap(5).Height(22).Padding(new Edges(0, 8, 0, 7)).Rounded(5)
                .Background(armed ? Danger : Color.White.Alpha(.15f));
            var press = chip.Semantics(Role.Button, armed ? "Confirm delete" : action.Label).Control();
            if (press.Hovered)
                chip.Background(armed ? Color.Hex(0xc0001a) : Color.White.Alpha(.25f));
            if (press.Clicked)
                RunAction(item, action);
            C.Icon(action.Icon, 12, Color.White, key: "icon");
            Label(armed ? "Confirm delete" : action.Label, "label", 11, Color.White, Weight.Medium);
            Label(action.Hint, "hint", 10, SelectedMuted);
        }
    }
    void RunItem(LaunchItem item)
    {
        if (!item.KeepOpen)
            Ui.Dispatch(new Msg.ShowPalette(PaletteMode.Closed));
        Ui.Dispatch(item.Message);
    }
    void RunAction(LaunchItem item, RowAction action)
    {
        var key = item.Id + "/" + action.Id;
        if (action.Confirm && Model.Armed != key)
            Ui.Dispatch(new Msg.Arm(key));
        else
            Ui.Dispatch(action.Message);
    }
    /// <summary>Arrow keys (and Ctrl+N/P) move the highlight; Enter runs it or submits a prompt; Primary+G/R/D run row actions.</summary>
    void PaletteKeys(ImmutableArray<LaunchItem> results, int highlight)
    {
        if (C.Shortcut(Key.Down, Modifiers.None) || C.Shortcut(Key.N, Modifiers.Control))
            Ui.Dispatch(new Msg.Highlight(Math.Min(results.Length - 1, highlight + 1)));
        else if (C.Shortcut(Key.Up, Modifiers.None) || C.Shortcut(Key.P, Modifiers.Control))
            Ui.Dispatch(new Msg.Highlight(Math.Max(0, highlight - 1)));
        else if (C.Shortcut(Key.Enter, Modifiers.None))
            Submit(results, highlight);
        else if (highlight < results.Length && !results[highlight].Actions.IsEmpty)
        {
            var item = results[highlight];
            var action = C.Shortcut(Key.G) ? "account" : C.Shortcut(Key.R) ? "rename" : C.Shortcut(Key.D) ? "delete" : null;
            if (item.Actions.FirstOrDefault(a => a.Id == action) is { } chosen)
                RunAction(item, chosen);
        }
    }
    void Submit(ImmutableArray<LaunchItem> results, int highlight)
    {
        var query = Model.Query.Trim();
        if (Prompt)
        {
            if (query.Length == 0)
                return;
            Ui.Dispatch(Model.Palette switch
            {
                PaletteMode.NewBranch => new Msg.Run(new(GitAction.NewBranch, Value: query, Body: Model.PaletteTarget ?? "")),
                PaletteMode.NewWorkspace => new Msg.CreateWorkspace(query),
                _ => new Msg.RenameWorkspace(Model.PaletteTarget ?? Model.ActiveWorkspace, query)
            });
            return;
        }
        // A typed path opens directly.
        if (Model.Palette != PaletteMode.Branch && (query.StartsWith('/') || query.StartsWith('~') || query.Contains(":\\", StringComparison.Ordinal)))
            Ui.Dispatch(new Msg.Open(query));
        else if (highlight < results.Length)
            RunItem(results[highlight]);
    }
}
