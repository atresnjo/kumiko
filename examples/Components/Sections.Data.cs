using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp
{
    static readonly ImmutableArray<TreeNode> Files =
    [
        new("src", "src", [
            new("src/app", "App.cs", Icon: "doc.text"),
            new("src/ui", "ui", [new("src/ui/view", "View.cs", Icon: "doc.text"), new("src/ui/theme", "Theme.cs", Icon: "doc.text")],
                Icon: "folder"),
            new("src/data", "data", [new("src/data/orders", "Orders.cs", Icon: "doc.text")], Icon: "folder")
        ], Icon: "folder"),
        new("docs", "docs", [new("docs/readme", "README.md", Icon: "doc.richtext")], Icon: "folder"),
        new("tests", "tests", [new("tests/e2e", "E2E.cs", Icon: "doc.text")], Icon: "folder")
    ];

    static ImmutableHashSet<string> Folders(ShowcaseModel m) => m.Folders ?? ["src"];

    static void Data(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send) =>
        Columns(c, "data", () => Scheduling(c, m, send), () => Explorer(c, m, send));

    static void Scheduling(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "schedule").FillWidth();
        ui.CardHeader("Schedule a review", "Dates, times and amounts follow the window culture.", key: "header");
        ui.Label("Due date", key: "due-label");
        ui.DatePicker("Due date", m.Due, m.DueOpen, date => send(new ShowcaseMsg.SetDue(date)),
            open => send(new ShowcaseMsg.SetDueOpen(open)),
            new(new(Min: ShowcaseModel.Today, Today: ShowcaseModel.Today), Width: 240, Clearable: true), key: "due");
        ui.Label("Start time", key: "start-label");
        ui.TimePicker("Start time", m.Start, m.StartOpen, time => send(new ShowcaseMsg.SetStart(time)),
            open => send(new ShowcaseMsg.SetStartOpen(open)), new(StepMinutes: 30, Min: new(8, 0), Max: new(18, 0), Width: 240),
            key: "start");
        ui.NumberInput("Budget", m.Budget, amount => send(new ShowcaseMsg.SetBudget(amount)),
            new(Min: 0, Max: 100_000, Step: 50, Decimals: 2, Width: 240, Grouping: true,
                Description: "Up and Down step by 50; Shift steps by 500."), key: "budget");
        using var footer = ui.Footer(key: "footer");
        var help = ui.IconButton("questionmark.circle", "Scheduling help", new(Appearance: Appearance.Ghost), key: "help");
        ui.Tooltip(help, "Reviews are booked in your local time zone.", key: "help-tip");
        if (ui.Button("Book review", new(Tone.Primary, Disabled: m.Due is null), key: "book").Clicked)
            send(new ShowcaseMsg.Click("Book review"));
    }

    static void Explorer(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        var t = ui.Theme;
        using var card = ui.Card(key: "explorer").FillWidth();
        ui.CardHeader("Project files", "Arrow keys browse the tree; drag the divider to resize.", key: "header");
        using var split = c.Div(key: "split").Row().FillWidth().Height(260).Border(t.Border).Rounded(t.Radius).Clip();
        using (c.Div(key: "files").Width(m.Pane).FillHeight().Padding(4))
        {
            ui.Tree("Project files", Files, Folders(m), m.File, (id, open) => send(new ShowcaseMsg.SetFolder(id, open)),
                id => send(new ShowcaseMsg.SelectFile(id)), new(Height: 252), key: "tree");
        }
        ui.ResizeHandle("File list width", m.Pane, width => send(new ShowcaseMsg.SetPane(width)), new(Min: 160, Max: 320),
            key: "divider");
        using var detail = c.Div(key: "detail").Flex().FillHeight().Padding(16).Gap(6);
        ui.Label(m.File ?? "Nothing selected", key: "path");
        c.Text(m.File is null ? "Choose a file in the tree." : "Opened from the project tree.",
            new(Size: t.FontSize - 1, Color: t.MutedForeground, Wrap: true), key: "hint");
    }
}
