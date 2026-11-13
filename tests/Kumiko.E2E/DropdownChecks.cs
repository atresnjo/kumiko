using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Search dropdown sections, multiple selection and remote (asynchronous) results.</summary>
static class DropdownChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        Sections(output, check);
        MultipleSelection(output, check);
        RemoteResults(output, check);
    }

    static Choice Option(string id, string label, string? section, string? detail = null) => new(id, label)
    {
        Section = section,
        Detail = detail
    };

    static float Top(Tester t, Role role, string name) => t.Get(role, name).Rect.Y;

    static void Sections(string output, Action<bool, string> check)
    {
        // Interleaved input: each section lists its choices together, in first-appearance order.
        Choice[] choices =
        [
            Option("maya", "Maya Brooks", "Sales"),
            Option("acme", "Acme Partners", "Partners"),
            Option("leo", "Leo Park", "Sales"),
            Option("unassigned", "Unassigned", null),
            Option("orbit", "Orbit Resellers", "Partners", "3 seats")
        ];
        var picker = new SearchDropdown();
        using var t = new Tester(c =>
        {
            using var root = c.Div(key: "root").SizeFull().Padding(24);
            picker.Draw(c, "Assignee", choices, "assignee");
        }, new(Width: 420, Height: 420));
        t.Get(Role.ComboBox, "Assignee").Click();
        t.Settle();
        check(Top(t, Role.Option, "Unassigned") < Top(t, Role.Heading, "Sales") && t.Get(Role.Heading, "Sales").Enabled && t.Get(Role.Heading, "Partners").Enabled,
            "unsectioned choices come first, then each section under its heading");
        check(Top(t, Role.Heading, "Sales") < Top(t, Role.Option, "Maya Brooks") && Top(t, Role.Option, "Leo Park") < Top(t, Role.Heading, "Partners")
            && Top(t, Role.Heading, "Partners") < Top(t, Role.Option, "Acme Partners"),
            "choices group under their section in first-appearance order");
        check(t.HasText("3 seats"), "a choice's detail is shown beside its label");
        t.Screenshot(Path.Combine(output, "dropdown-sections.png"));
        for (int i = 0; i < 4; i++)
            t.Key(Key.Down);
        t.Key(Key.Enter);
        check(picker.SelectedId == "acme" && !picker.IsOpen, "keyboard navigation moves across sections and skips headings");
        t.Get(Role.ComboBox, "Assignee").Click();
        t.TypeText("orbit");
        t.Settle();
        check(!t.Context.Accessibility.Any(n => n.Role == Role.Heading && n.Name == "Sales") && t.Get(Role.Heading, "Partners").Enabled,
            "filtering hides sections without matches");
    }

    static void MultipleSelection(string output, Action<bool, string> check)
    {
        Choice[] choices = [Option("lead", "Lead", "Open"), Option("demo", "Demo", "Open"), Option("won", "Won", "Closed")];
        var picker = new SearchDropdown
        {
            Multiple = true,
            Placeholder = "All stages"
        };
        var reported = ImmutableArray<string>.Empty;
        picker.SelectedIdsChanged += ids => reported = ids;
        using var t = new Tester(c =>
        {
            using var root = c.Div(key: "root").SizeFull().Padding(24);
            picker.Draw(c, "Stage", choices, "stage");
        }, new(Width: 420, Height: 420));
        t.Get(Role.ComboBox, "Stage").Click();
        t.Get(Role.Option, "Demo").Click();
        t.Get(Role.Option, "Lead").Click();
        t.Settle();
        check(picker.IsOpen && picker.SelectedIds.SequenceEqual(["demo", "lead"]) && reported.SequenceEqual(picker.SelectedIds),
            "a multiple-selection dropdown toggles choices and stays open");
        check(t.Context.Accessibility.Count(n => n.Role == Role.Option && n.Properties?.Selected == true) == 2,
            "each chosen option reports its selected state");
        check(t.Context.Accessibility.Single(n => n.Role == Role.ComboBox && n.Name == "Stage").Value == "Lead, Demo" && t.HasText("Lead +1"),
            "the trigger summarizes several choices in list order");
        t.Screenshot(Path.Combine(output, "dropdown-multiple.png"));
        t.Key(Key.Down);
        t.Key(Key.Enter);
        check(picker.SelectedIds.SequenceEqual(["demo"]) && picker.IsOpen, "Enter toggles the active choice without closing");
        t.Get(Role.Button, "Clear selection").Click();
        t.Settle();
        check(picker.SelectedIds.IsEmpty && t.HasText("All stages"), "clearing removes every choice and restores the placeholder");
        t.Key(Key.Escape);
        check(!picker.IsOpen, "Escape closes a multiple-selection dropdown");
        var state = picker.Capture();
        var restored = new SearchDropdown
        {
            Multiple = true
        };
        restored.Restore(state with
        {
            SelectedIds = ["won"]
        });
        check(restored.SelectedIds.SequenceEqual(["won"]) && restored.Capture().SelectedIds.SequenceEqual(["won"]),
            "snapshots carry multiple selections");
    }

    sealed record Lookup(DropdownState Picker, string Searched = "", bool Loading = false, string? Error = null)
    {
        public ImmutableArray<Choice> Results { get; init; } = [];
    }

    static void RemoteResults(string output, Action<bool, string> check)
    {
        var model = new Lookup(new());
        var queries = new List<string>();
        var options = new DropdownOptions(Width: 260, Placeholder: "Choose a company", Remote: true, EmptyMessage: "No companies");
        using var t = new Tester(c =>
        {
            using var ui = new Ui<DropdownState>(c, state =>
            {
                if (state.Query != model.Picker.Query || state.IsOpen && !model.Picker.IsOpen)
                {
                    queries.Add(state.Query);
                    model = model with
                    {
                        Searched = state.Query,
                        Loading = true,
                        Error = null
                    };
                }
                model = model with
                {
                    Picker = state
                };
            });
            using var root = c.Div(key: "root").SizeFull().Padding(24);
            ui.Dropdown("company", "Company", model.Picker, model.Results, state => state, options with
            {
                Loading = model.Loading,
                LoadError = model.Error
            });
        }, new(Width: 420, Height: 420));
        t.Get(Role.ComboBox, "Company").Click();
        t.Settle();
        check(queries.SequenceEqual([""]) && t.HasText("Searching…"), "opening a remote dropdown requests results and shows progress");
        t.TypeText("glc");
        t.Settle();
        check(queries[^1] == "glc" && model.Picker.Query == "glc", "typing reports each query to the application");
        t.Get(Role.TextField, "Search Company").Fill("glc ");
        t.Settle();
        check(queries[^1] == "glc " && model.Picker.Query == "glc ", "assistive technology edits to the search field reach the application");
        // The server answers with a fuzzy match the local substring filter would reject.
        model = model with
        {
            Loading = false,
            Results = [Option("glacier", "Glacier Cloud", "Your accounts", "glaciercloud.net"), Option("galaxy", "Galaxy Labs", "Team accounts")]
        };
        t.Settle();
        check(t.Get(Role.Option, "Glacier Cloud").Enabled && t.Get(Role.Option, "Galaxy Labs").Enabled && !t.HasText("Searching…"),
            "remote choices are shown exactly as the application supplies them");
        t.Screenshot(Path.Combine(output, "dropdown-remote.png"));
        t.Get(Role.Option, "Glacier Cloud").Click();
        t.Settle();
        check(model.Picker.SelectedId == "glacier" && !model.Picker.IsOpen, "choosing a remote result selects it");
        model = model with
        {
            Results = []
        };
        t.Settle();
        check(t.HasText("Glacier Cloud"), "the trigger keeps the chosen label after the results change");
        t.Get(Role.ComboBox, "Company").Click();
        model = model with
        {
            Loading = false,
            Error = "Directory offline"
        };
        t.Settle();
        check(t.HasText("Directory offline") && !t.HasText("No companies"), "a failed search explains the failure instead of claiming no matches");
        model = model with
        {
            Error = null
        };
        t.Settle();
        check(t.HasText("No companies"), "an empty remote answer shows the empty message");
    }
}
