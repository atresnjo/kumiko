using System.Collections.Immutable;
using System.Globalization;
using Kumiko.Components;
using Kumiko.UI;

/// <summary>Business-application controls driven through Kumiko input only.</summary>
static class ControlChecks
{
    static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly DateOnly Today = new(2026, 10, 20);

    public static void Run(string output, Action<bool, string> check)
    {
        Calendar(output, check);
        DatePicker(output, check);
        TimePicker(check);
        NumberInput(output, check);
        Tooltip(output, check);
        Tree(output, check);
        ResizeHandle(check);
    }

    static Tester Open(Action<Context> view, CultureInfo culture, float width = 520, float height = 520)
    {
        var tester = new Tester(c =>
        {
            c.Culture = culture;
            using var page = c.Div(key: "page").SizeFull().Padding(Space.S4).Gap(Space.S3);
            view(c);
        }, new(Width: width, Height: height, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Settle();
        return tester;
    }

    static AccessibleNode Node(Tester t, Role role, string name) =>
        t.Context.Accessibility.Single(n => n.Role == role && n.Name == name && !n.Offscreen);

    static AccessibleNode? Focused(Tester t) => t.Context.Accessibility.FirstOrDefault(n => n.Focused);

    static void Artifacts(Tester t, string output, string name)
    {
        t.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), t.Dump());
        t.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
    }

    static void Calendar(string output, Action<bool, string> check)
    {
        DateOnly? date = new DateOnly(2026, 10, 21);
        var props = new CalendarProps(Min: new(2026, 10, 5), Max: new(2026, 12, 31),
            IsDisabled: d => d == new DateOnly(2026, 10, 23), Today: Today);
        using var t = Open(c => new Kit(c).Calendar("Due date", date, d => date = d, props, key: "calendar"), Us);
        var headers = t.Context.Accessibility.Where(n => n.Role == Role.ColumnHeader && !n.Offscreen).ToArray();
        check(headers.Length == 7 && headers[0].Name == "Sunday", "en-US calendars start the week on Sunday");
        check(t.Context.Accessibility.Count(n => n.Role == Role.Row) == 6, "calendars always lay out six week rows");
        check(t.HasText("October 2026"), "the calendar names its visible month in the culture's format");
        var selected = Node(t, Role.Cell, "Wednesday, October 21, 2026");
        check(selected.Properties?.Selected == true, "the selected date is exposed as selected");
        check(!Node(t, Role.Cell, "Friday, October 23, 2026").Enabled, "disabled dates are not operable");
        check(!Node(t, Role.Cell, "Sunday, October 4, 2026").Enabled, "dates before Min are not operable");
        var disabled = Node(t, Role.Cell, "Friday, October 23, 2026").Rect;
        t.Click(disabled.X + disabled.Width / 2, disabled.Y + disabled.Height / 2);
        check(date == new DateOnly(2026, 10, 21), "clicking a disabled date does not select it");
        t.Get(Role.Cell, "Thursday, October 22, 2026").Click();
        t.Settle();
        check(date == new DateOnly(2026, 10, 22) && Focused(t)?.Name == "Thursday, October 22, 2026",
            "clicking a date selects and focuses it");
        Artifacts(t, output, "controls-calendar");
        t.Key(Key.Right);
        t.Settle();
        check(Focused(t)?.Name == "Saturday, October 24, 2026" && date == new DateOnly(2026, 10, 22),
            "Right moves focus past a disabled date without selecting");
        t.Key(Key.PageDown);
        t.Settle();
        check(Focused(t)?.Name == "Tuesday, November 24, 2026" && t.HasText("November 2026"),
            "Page Down moves to the same day of the next month and shows it");
        t.Key(Key.PageDown, Modifiers.Shift);
        t.Settle();
        check(Focused(t)?.Name == "Thursday, December 31, 2026", "Shift+Page Down clamps a year step to Max");
        check(!t.Get(Role.Button, "Next month").Enabled, "next-month navigation stops at Max");
        t.Key(Key.Home);
        t.Settle();
        check(Focused(t)?.Name == "Sunday, December 27, 2026", "Home moves to the start of the week");
        t.Key(Key.End);
        t.Settle();
        check(Focused(t)?.Name == "Thursday, December 31, 2026", "End stops at Max when the week crosses it");
        t.Key(Key.Enter);
        t.Settle();
        check(date == new DateOnly(2026, 12, 31), "Enter selects the focused date");
        t.Get(Role.Button, "Previous month").Click();
        t.Get(Role.Button, "Previous month").Click();
        t.Settle();
        check(t.HasText("October 2026"), "month navigation buttons change the visible month");
        for (var i = 0; i < 3; i++)
        {
            t.Get(Role.Cell, "Monday, October 5, 2026").Click();
            t.Key(Key.Up);
            t.Settle();
        }
        check(Focused(t)?.Name == "Monday, October 5, 2026", "Up clamps to Min instead of leaving the range");

        DateOnly? german = new DateOnly(2026, 2, 10);
        using var de = Open(c => new Kit(c).Calendar("Termin", german, d => german = d,
            new(Today: Today), key: "calendar"), German);
        var dayHeaders = de.Context.Accessibility.Where(n => n.Role == Role.ColumnHeader && !n.Offscreen).ToArray();
        check(dayHeaders[0].Name == "Montag" && de.HasText("Februar 2026"), "de-DE calendars start on Monday with German names");
        check(de.Context.Accessibility.Count(n => n.Role == Role.Row) == 6, "a four-week February still has six rows");
        Artifacts(de, output, "controls-calendar-de");

        using var custom = Open(c =>
        {
            c.Strings = UiStrings.English with
            {
                PreviousMonth = "Mois précédent"
            };
            new Kit(c).Calendar("Échéance", german, d => german = d, new(Today: Today), key: "calendar");
        }, CultureInfo.GetCultureInfo("fr-FR"));
        check(custom.Get(Role.Button, "Mois précédent").Enabled && custom.HasText("février 2026"),
            "built-in control strings can be replaced per window");
    }

    static void DatePicker(string output, Action<bool, string> check)
    {
        DateOnly? value = null;
        var open = false;
        var changes = 0;
        var culture = Us;
        using var t = Open(c =>
        {
            c.Culture = culture;
            new Kit(c).DatePicker("Start date", value, open, d =>
            {
                value = d;
                changes++;
            }, o => open = o, new(Calendar: new(Today: Today)), key: "start");
        }, Us);
        var trigger = Node(t, Role.Button, "Start date");
        check(trigger.Value == "Pick a date", "an empty date picker shows its placeholder");
        t.Get(Role.Button, "Start date").Click();
        t.Settle();
        check(open && Focused(t)?.Name == "Tuesday, October 20, 2026", "opening focuses today when nothing is selected");
        Artifacts(t, output, "controls-date-picker");
        t.Key(Key.Right);
        t.Key(Key.Enter);
        t.Settle();
        check(!open && value == new DateOnly(2026, 10, 21) && changes == 1, "choosing a date reports it once and closes");
        check(Focused(t)?.Name == "Start date", "focus returns to the trigger after a choice");
        check(Node(t, Role.Button, "Start date").Value == "10/21/2026", "the trigger shows the culture's short date");
        culture = German;
        t.Settle();
        check(Node(t, Role.Button, "Start date").Value == "21.10.2026", "the trigger follows the window culture");
        t.Key(Key.Enter);
        t.Settle();
        check(open && Focused(t)?.Name == "Mittwoch, 21. Oktober 2026", "Enter opens on the selected date");
        t.Key(Key.Escape);
        t.Settle();
        check(!open && changes == 1 && Focused(t)?.Name == "Start date", "Escape closes without a change and restores focus");
    }

    static void TimePicker(Action<bool, string> check)
    {
        TimeOnly? value = new TimeOnly(9, 30);
        var open = false;
        string Short(int hour, int minute) => new TimeOnly(hour, minute).ToString("t", Us);
        using var t = Open(c => new Kit(c).TimePicker("Start time", value, open, v => value = v, o => open = o,
            new(StepMinutes: 30, Min: new(8, 0), Max: new(18, 0)), key: "time"), Us);
        check(Node(t, Role.Button, "Start time").Value == Short(9, 30) && Short(9, 30).StartsWith("9:30", StringComparison.Ordinal),
            "the time trigger uses the culture's short time");
        t.Get(Role.Button, "Start time").Click();
        t.Settle();
        var options = t.Context.Accessibility.Where(n => n.Role == Role.MenuItem).ToArray();
        check(options.Length == 21 && options[0].Name == Short(8, 0) && options[^1].Name == Short(18, 0),
            "time options follow Min, Max and the step");
        check(Focused(t)?.Name == Short(9, 30) && Node(t, Role.MenuItem, Short(9, 30)).Checked, "the open list focuses the current time");
        t.Key(Key.Down);
        t.Key(Key.Enter);
        t.Settle();
        check(!open && value == new TimeOnly(10, 0), "choosing a time reports it and closes");
    }

    static void NumberInput(string output, Action<bool, string> check)
    {
        double? amount = 12.5;
        var props = new NumberInputProps(Min: 0, Max: 100, Step: .5, Decimals: 2);
        using var t = Open(c => new Kit(c).NumberInput("Amount", amount, v => amount = v, props, key: "amount"), German);
        var field = Node(t, Role.TextField, "Amount");
        check(field.Value == "12,50", "numbers display with the culture's decimal separator");
        check(field.Properties is { Minimum: 0, Maximum: 100, Number: 12.5 }, "number inputs expose their range");
        t.Get(Role.TextField, "Amount").Click();
        t.Key(Key.A, Modifiers.Primary);
        t.TypeText("1,");
        t.Settle();
        check(Node(t, Role.TextField, "Amount").Value == "1,", "a partially typed number is kept while editing");
        t.TypeText("5");
        t.Settle();
        check(amount == 1.5, "a parsed culture number updates the model while typing");
        t.Key(Key.A, Modifiers.Primary);
        t.TypeText("500");
        t.Settle();
        check(amount == 1.5 && Node(t, Role.TextField, "Amount").Properties?.Error is { Length: > 0 },
            "out-of-range input is explained and not reported");
        t.Key(Key.Enter);
        t.Settle();
        check(amount == 100 && Node(t, Role.TextField, "Amount").Value == "100,00", "committing clamps to the range");
        t.Key(Key.Down);
        t.Settle();
        check(amount == 99.5, "Down steps by Step");
        t.Key(Key.Down, Modifiers.Shift);
        t.Settle();
        check(amount == 94.5, "Shift+Down steps ten times faster");
        t.Get(Role.Button, string.Format(German, UiStrings.For(German).Increase, "Amount")).Click();
        t.Settle();
        check(amount == 95, "the increase button steps the value");
        t.Get(Role.TextField, "Amount").Click();
        t.Key(Key.A, Modifiers.Primary);
        t.TypeText("abc");
        t.Settle();
        check(amount == 95 && Node(t, Role.TextField, "Amount").Properties?.Error is { Length: > 0 },
            "unparsable input shows an error without changing the model");
        Artifacts(t, output, "controls-number");
        t.Context.Blur();
        t.Settle();
        check(Node(t, Role.TextField, "Amount").Value == "95,00", "leaving the field restores the last valid value");
    }

    static void Tooltip(string output, Action<bool, string> check)
    {
        var saves = 0;
        using var t = Open(c =>
        {
            var kit = new Kit(c);
            using (c.Div(key: "spacer").Height(80))
            {
            }
            var hit = kit.Button("Save", key: "save");
            kit.Tooltip(hit, "Save changes", key: "save-tip");
            if (hit.Clicked)
                saves++;
        }, Us);
        check(Node(t, Role.Button, "Save").Properties?.Description == "Save changes", "the tooltip is the trigger's accessible description");
        var save = t.Get(Role.Button, "Save").Rect;
        t.MoveTo(save.X + save.Width / 2, save.Y + save.Height / 2);
        t.Frame(.1f);
        check(!t.Context.Accessibility.Any(n => n.Role == Role.Tooltip), "a tooltip waits for its hover delay");
        check(t.Context.NextFrame is { } wake && wake > TimeSpan.Zero && wake <= TimeSpan.FromSeconds(.5),
            "the hover delay schedules one wake-up instead of polling");
        t.Advance(.5f);
        t.Frame(0);
        var tip = t.Context.Accessibility.SingleOrDefault(n => n.Role == Role.Tooltip);
        check(tip is { Name: "Save changes" }, "a hovered trigger shows its tooltip after the delay");
        check(tip!.Rect.Bottom <= save.Y && tip.Rect.X >= 0 && tip.Rect.Right <= t.Context.Size.Width, "tooltips open above the trigger inside the window");
        check(t.Context.NextFrame is null, "a visible tooltip leaves the loop idle");
        Artifacts(t, output, "controls-tooltip");
        t.MoveTo(tip.Rect.X + tip.Rect.Width / 2, tip.Rect.Y + tip.Rect.Height / 2);
        t.Settle();
        check(t.Context.Accessibility.Any(n => n.Role == Role.Tooltip), "moving the pointer onto a tooltip keeps it open (WCAG 1.4.13)");
        t.Click(save.X + save.Width / 2, save.Y + save.Height / 2);
        t.Settle();
        check(saves == 1 && !t.Context.Accessibility.Any(n => n.Role == Role.Tooltip), "clicking the trigger acts once and hides the tooltip");
        t.MoveTo(5, 5);
        t.Settle();
        t.Context.Blur();
        t.Key(Key.Tab);
        t.Settle();
        check(Focused(t)?.Name == "Save" && t.Context.Accessibility.Any(n => n.Role == Role.Tooltip), "keyboard focus shows the tooltip");
        t.Key(Key.Escape);
        t.Settle();
        check(!t.Context.Accessibility.Any(n => n.Role == Role.Tooltip), "Escape hides the tooltip");
        t.Context.Input.HeldModifiers = Modifiers.Control;
        t.Key(Key.Tab, Modifiers.Control);
        t.Key(Key.Tab, Modifiers.Alt | Modifiers.Shift);
        t.Settle();
        check(Focused(t)?.Name == "Save", "Control/Option+Tab is left to application shortcuts and keeps focus");
        check(t.Context.Input.HeldModifiers == Modifiers.Control, "Held modifiers persist across frames until released");
        t.Context.Input.HeldModifiers = Modifiers.None;
    }

    static void Tree(string output, Action<bool, string> check)
    {
        var archive = Enumerable.Range(0, 10_000).Select(i => new TreeNode($"item.{i}", $"Item {i}")).ToImmutableArray();
        ImmutableArray<TreeNode> roots =
        [
            new("documents", "Documents", [new("reports", "Reports", [new("q1", "Q1"), new("q2", "Q2")]), new("invoices", "Invoices")]),
            new("pictures", "Pictures"),
            new("archive", "Archive", archive)
        ];
        var expanded = ImmutableHashSet<string>.Empty;
        string? selected = null;
        using var t = Open(c => new Kit(c).Tree("Files", roots, expanded, selected,
            (id, open) => expanded = open ? expanded.Add(id) : expanded.Remove(id), id => selected = id,
            new(Height: 300), key: "files"), Us, height: 420);
        var items = t.Context.Accessibility.Where(n => n.Role == Role.TreeItem).ToArray();
        check(items.Length == 3 && items.All(n => n.Properties?.Level == 1), "collapsed branches build only their roots");
        check(Node(t, Role.TreeItem, "Documents").Properties?.Expanded == false && Node(t, Role.TreeItem, "Pictures").Properties?.Expanded is null,
            "branches expose expansion and leaves do not");
        t.Get(Role.TreeItem, "Documents").Click();
        t.Key(Key.Right);
        t.Settle();
        check(expanded.Contains("documents") && Node(t, Role.TreeItem, "Reports").Properties?.Level == 2, "Right expands a collapsed branch");
        t.Key(Key.Right);
        t.Settle();
        check(selected == "reports" && Focused(t)?.Name == "Reports", "Right on an open branch moves to its first child");
        t.Key(Key.Right);
        t.Key(Key.Left);
        t.Settle();
        check(!expanded.Contains("reports") && Focused(t)?.Name == "Reports", "Left collapses an open branch");
        t.Key(Key.Left);
        t.Settle();
        check(selected == "documents" && Focused(t)?.Name == "Documents", "Left on a closed item moves to its parent");
        t.Key(Key.End);
        t.Key(Key.Right);
        t.Settle();
        check(expanded.Contains("archive") && t.Context.Accessibility.Count(n => n.Role == Role.TreeItem) < 100,
            "a large expanded branch builds only visible rows");
        t.Key(Key.End);
        t.Settle();
        check(selected == "item.9999" && Focused(t) is { Name: "Item 9999", Offscreen: false }, "End reveals and focuses the last visible item");
        Artifacts(t, output, "controls-tree");
        t.Key(Key.Home);
        t.Settle();
        check(selected == "documents" && Focused(t)?.Name == "Documents", "Home returns to the first item");
    }

    static void ResizeHandle(Action<bool, string> check)
    {
        float width = 200;
        using var t = Open(c =>
        {
            var kit = new Kit(c);
            using var row = c.Div(key: "split").Row().FillWidth().Height(200);
            using (c.Div(key: "sidebar").Width(width).FillHeight().Background(c.Theme.Surface))
            {
            }
            kit.ResizeHandle("Sidebar width", width, w => width = w, new(Min: 120, Max: 400), key: "handle");
            using (c.Div(key: "content").Flex().FillHeight())
            {
            }
        }, Us, width: 700);
        var handle = t.Get(Role.Separator, "Sidebar width").Rect;
        var y = handle.Y + handle.Height / 2;
        t.Press(handle.X + 1, y);
        t.MoveTo(handle.X + 61, y);
        t.Settle();
        t.Release(handle.X + 61, y);
        t.Settle();
        check(Math.Abs(width - 260) <= 1, "dragging a handle grabbed off-center resizes by the pointer movement");
        handle = t.Get(Role.Separator, "Sidebar width").Rect;
        t.Press(handle.X + 2, y);
        t.MoveTo(10, y);
        t.Release(10, y);
        t.Settle();
        check(width == 120, "a handle stops at its minimum");
        t.Key(Key.Right);
        t.Settle();
        check(width == 130 && Focused(t)?.Name == "Sidebar width", "arrow keys step a focused handle");
        t.Key(Key.End);
        t.Settle();
        check(width == 400, "End moves a handle to its maximum");
        var node = Node(t, Role.Separator, "Sidebar width");
        check(node.Properties is { Minimum: 120, Maximum: 400, Number: 400 }, "resize handles expose their range");
        t.Context.PerformAccessibilityAction(node.Id, new(AccessibilityAction.SetRange, Number: 300));
        t.Settle();
        check(width == 300, "assistive technology can set a handle's position");
    }
}
