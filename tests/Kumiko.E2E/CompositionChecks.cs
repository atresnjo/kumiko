using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Adversarial render invocations through the offscreen host, with intentionally unsafe captures.</summary>
static class CompositionChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var messages = new List<int>();
        var failures = new HashSet<string>();
        Ui<int>? escaped = null;
        using var tester = new Tester(c =>
        {
            using var ui = new Ui<int>(c, messages.Add);
            using var row = c.Div(key: "row").Row().Gap(Space.S3);
            var child = new Probe(0, value => escaped = value);
            ui.Child(child, "first", static message => message + 10);
            Reject(() => escaped!.Dispatch(99), "disposed child", failures);
            Reject(() => ui.Child(child, "second", static message => message), "reused view", failures);
            Reject(() => ui.Child(new Probe(0, null), "first", static message => message), "duplicate key", failures);
            Reject(() => ui.Child(new Probe(0, null), default, static message => message), "empty key", failures);
            Reject(() => ui.Child(new Probe(1, value => escaped = value), "throw", static message => message), "render exception", failures);
            Reject(() => escaped!.Dispatch(99), "exception disposed child", failures);
            ui.Child(new Probe(0, null), "last", static message => message + 20);
        }, new(Width: 500, Height: 200, DevMode: true));
        tester.Settle();
        check(failures.Count == 6, "disposed/reused views, duplicate/empty keys and throwing renders are rejected");
        var buttons = tester.Context.Accessibility.Where(n => n.Role == Role.Button && n.Name == "Send").ToArray();
        check(buttons.Length == 2 && buttons[0].Rect.Y == buttons[1].Rect.Y && buttons[0].Rect.Right < buttons[1].Rect.X,
            "failed composition unwinds identity and layout before a later sibling renders");
        var result = tester.Context.PerformAction(buttons[1].Id, new(AccessibilityAction.Invoke));
        tester.Settle();
        check(result.Succeeded && messages.SequenceEqual([21]), "a sibling still routes input after rejected and throwing child invocations");
        var captured = escaped!;
        var workerRejected = Task.Run(() =>
        {
            try
            {
#pragma warning disable KUM003 // Deliberately exercise the runtime wrong-thread boundary.
                captured.Dispatch(99);
#pragma warning restore KUM003
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }).GetAwaiter().GetResult();
        check(workerRejected && messages.Count == 1, "escaped child bindings reject wrong-thread use before dispatch");
        tester.Screenshot(Path.Combine(output, "composition-cleanup.png"));
        File.WriteAllText(Path.Combine(output, "composition-cleanup.txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, "composition-cleanup.warnings.json"));
        File.WriteAllLines(Path.Combine(output, "composition-cleanup-report.txt"), failures.Order());
    }

    static void Reject(Action action, string name, HashSet<string> failures)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            failures.Add(name);
            return;
        }
        throw new Exception(name + " was accepted");
    }

    sealed class Probe(int mode, Action<Ui<int>>? capture) : View<int, int>(mode)
    {
        protected override void Render()
        {
            capture?.Invoke(Ui);
            if (Model != 0)
            {
                using var temporary = C.Div(key: "temporary").Width(0).Height(0);
                throw new InvalidOperationException("Deliberate render failure");
            }
            Ui.Button("Send", 1, key: "send");
        }
    }
}
