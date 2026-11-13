using Kumiko.UI;

static class FormFlows
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var program = FormApp.CreateProgram(FormApp.Init);
        program.Record(Path.Combine(output, "session.jsonl"));
        using var tester = new Tester(program, new(640, 640));
        tester.Settle();
        var checks = new List<string>();
        void Check(bool success, string name)
        {
            if (!success)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }
        var name = tester.Context.Query(new(Role.TextField, "Name", Key: "name")).First();
        tester.Context.PerformAction(name.Id, new(AccessibilityAction.SetValue, Text: "Ada"));
        tester.Settle();
        tester.Context.PerformAction(name.Id, new(AccessibilityAction.Focus));
        tester.Settle();
        tester.Key(Key.Enter);
        tester.Settle();
        Check(program.Model.Saves == 1 && program.Model.InnerSaves == 0, "Enter submits its nearest form exactly once");
        tester.Get(Role.TextField, "Notes").Perform(new(AccessibilityAction.Focus));
        tester.Key(Key.Enter);
        Check(program.Model.Notes.Contains('\n') && program.Model.Saves == 1, "multiline Enter edits without submitting");
        tester.Key(Key.Enter, Modifiers.Primary);
        Check(program.Model.Saves == 2, "Primary+Enter submits from multiline editor");
        tester.Get(Role.Checkbox, "Enable saving").Invoke();
        tester.Context.PerformAction(name.Id, new(AccessibilityAction.Focus));
        tester.Settle();
        tester.Key(Key.Enter);
        Check(program.Model.Saves == 2, "disabled submit rejects keyboard submission");
        tester.Get(Role.Checkbox, "Enable saving").Invoke();
        tester.Context.PerformAction(name.Id, new(AccessibilityAction.Focus));
        tester.Settle();
        tester.Compose("かな", false);
        tester.Key(Key.Enter);
        Check(program.Model.Saves == 2, "IME composition does not submit");
        tester.Compose("かな", true);
        var nested = tester.Get(Role.Form, "Nested");
        nested.Get(Role.TextField, "Name").Perform(new(AccessibilityAction.Focus));
        tester.Key(Key.Enter);
        Check(program.Model.InnerSaves == 1 && program.Model.Saves == 2, "nested form does not submit its ancestor");
        nested.Get(Role.Button, "Save").Invoke();
        Check(program.Model.InnerSaves == 2, "scoped submit invocation selects the intended form");
        var revision = tester.Context.Revision;
        tester.Settle();
        var stale = tester.Context.PerformAction(nested.Get(Role.Button, "Save").Id, new(AccessibilityAction.Invoke), revision);
        Check(stale.Status == ActionStatus.StaleRevision && program.Model.InnerSaves == 2, "stale revision cannot submit");
        var unsupported = tester.Context.PerformAction(nested.Id, new(AccessibilityAction.SetValue, Text: "bad"));
        Check(unsupported.Status == ActionStatus.Unsupported, "unsupported actions fail explicitly");
        tester.Screenshot(Path.Combine(output, "forms.png"));
        File.WriteAllText(Path.Combine(output, "checks.txt"), string.Join('\n', checks));
        Console.WriteLine($"Typed forms: {checks.Count} behavior checks passed");
    }
}
