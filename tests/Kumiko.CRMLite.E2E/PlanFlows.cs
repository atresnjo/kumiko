using CRMLite;
using Kumiko.UI;

/// <summary>
/// A deal's agent plan from draft to done: editing, suggestions, reordering, approval, the work lines arriving,
/// a failed step with Retry, pausing, and the toast that keeps a closed plan's progress in view. The agent advances
/// only on explicit ticks, so every state is repeatable.
/// </summary>
static class PlanFlows
{
    const string Card = "Juniper Retail, Partner billing", Deal = "d-juniper";
    const string Review = "Review the last 30 days with Juniper Retail", Summary = "Summarize Zoe Harper's open questions";
    const string Proposal = "Draft a proposal at $392k", Recap = "Send a recap to Daniel Okafor";

    public static void Run(string output, Action<bool, string> check)
    {
        using var s = new CrmSession(model: Data.Initial() with
        {
            LiveAgent = false
        });
        check(!CrmApp.Subscriptions(s.Model).Any(sub => sub.Key == "agent"), "nothing ticks before a plan runs");
        s.Click(Role.Button, "Deals");
        s.Click(Role.Button, Card);
        s.Click(Role.Button, "Plan with agent");
        Draft(s, output, check);
        Run(s, output, check);
        Toasts(s, output, check);
    }

    static AgentPlan Plan(CrmSession s) => s.Model.PlanFor(Deal)!;

    static string[] Steps(CrmSession s) => [.. Plan(s).Steps.Select(step => step.Title)];

    static void Draft(CrmSession s, string output, Action<bool, string> check)
    {
        check(s.Model.Overlay is Overlay.Plan { DealId: Deal } && s.Exists(Role.Dialog, "Agent plan"), "the drawer opens the deal's plan in a dialog");
        check(Steps(s) is [Review, Summary, Proposal, "Book a discovery call with Zoe next week", "Move the deal to Qualified and update the forecast", Recap],
            "the agent drafts six steps from the deal, its contact and owner");
        check(s.Tester.HasText("Plan · 6 steps") && s.Tester.HasText(AgentScript.Model) && s.Tester.HasText("Move Partner billing with Juniper Retail to Qualified"),
            "the header names the step count, the model and the goal");
        Capture(s, output, "plan-draft", check);

        s.Click(Role.Button, "Keep planning");
        check(Steps(s).Length == 7 && Steps(s)[5] == "Offer a two-year term with a capped uplift" &&
            s.Node(Role.Button, "Offer a two-year term with a capped uplift").Value == "Suggested",
            "Keep planning adds a suggestion before the last step");
        s.Click(Role.Button, "Keep planning");
        check(Steps(s).Length == 8 && !s.Node(Role.Button, "Keep planning").Enabled,
            "Keep planning is spent after the agent's last idea");
        Capture(s, output, "plan-suggested", check);

        s.Click(Role.Button, Proposal);
        check(s.Model.Overlay is Overlay.Plan { Editing: "propose" } && s.Exists(Role.TextField, "Step"), "pressing a step edits its title in place");
        s.Tester.TypeText(" with a pilot");
        s.Key(Key.Enter);
        check(Steps(s)[2] == Proposal + " with a pilot" && s.Model.Overlay is Overlay.Plan { Editing: null }, "Enter keeps the edited title");

        s.Click(Role.Button, Recap);
        s.Click(Role.Button, "Remove " + Recap);
        check(!Steps(s).Contains(Recap), "the remove button deletes a step");
        s.Click(Role.Button, "Add step");
        s.Tester.TypeText("Send a recap to the team");
        s.Key(Key.Enter);
        check(Steps(s)[^1] == "Send a recap to the team", "Add step appends a step and edits it");
        s.Click(Role.Button, "Add step");
        s.Key(Key.Escape);
        check(Steps(s)[^1] == "Send a recap to the team" && s.Model.Overlay is Overlay.Plan, "Escape drops an empty new step before it closes anything");

        var to = s.Node(Role.Button, Summary).Rect;
        s.Drag(s.Node(Role.Button, Review), new(to.X, to.Y + 6, to.Width, to.Height));
        check(Steps(s) is [Summary, Review, ..], "dragging a step's row moves it");
        check(s.Tester.Context.Warnings.Count == 0, "the edited draft renders without warnings: " +
            string.Join("; ", s.Tester.Context.Warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));

        s.Key(Key.Escape);
        check(s.Model.Overlay is Overlay.None && !Plan(s).Minimized && !s.All(Role.Alert).Any(), "a closed draft waits without a toast");
        s.Click(Role.Button, Card);
        s.Click(Role.Button, "Review plan");
        check(Steps(s).Length == 8, "reopening the drawer's plan keeps the draft");
    }

    static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        s.Click(Role.Button, "Approve plan");
        check(Plan(s).Status == PlanStatus.Running && s.Node(Role.ListItem, Summary).Value == "Working" && !CrmApp.Subscriptions(s.Model).Any(sub => sub.Key == "agent"),
            "approving starts the first step; the test clock leaves the timer off");
        check(CrmApp.Subscriptions(s.Model with
        {
            LiveAgent = true
        }).Count(sub => sub.Key == "agent") == 1, "a live running plan ticks on a timer");
        check(s.Tester.HasText("Working on step 1") && s.Tester.HasText("1 of 8"), "the footer and header follow the running step");

        s.Tester.Context.ReduceMotion = false;
        float Below() => s.Node(Role.ListItem, Review).Rect.Y;
        var before = Below();
        s.Program.Send(new CrmMsg.AgentTick());
        for (var i = 0; i < 6; i++)
            s.Tester.Frame(1 / 60f);
        var moving = Below();
        s.Tester.Screenshot(Path.Combine(output, "plan-work-arriving.png"));
        for (var i = 0; i < 60; i++)
            s.Tester.Frame(1 / 60f);
        var after = Below();
        check(moving > before && moving < after, $"the rows below glide down as a work line arrives ({before:0} → {moving:0} → {after:0})");
        s.Tester.Context.ReduceMotion = true;
        s.Tester.Settle();
        check(s.Tester.HasText("Read calls/juniper-oct-14.md"), "the running step lists its work");

        TickUntil(s, m => m.PlanFor(Deal)!.Steps.Any(step => step.Origin == StepOrigin.Agent));
        check(Steps(s)[1] == "Answer the security review questionnaire" && s.Tester.HasText("Added by agent") &&
            s.Node(Role.ListItem, Summary).Value!.StartsWith("Done in ", StringComparison.Ordinal),
            "after reading the questions the agent adds a step of its own");
        TickUntil(s, m => m.PlanFor(Deal)!.Steps[2].State == StepState.Running);
        Capture(s, output, "plan-running", check);

        TickUntil(s, m => m.PlanFor(Deal)!.Status == PlanStatus.Failed);
        var failed = Plan(s).Current + 1;
        check(Plan(s).Steps[failed - 1].Title == "Book a discovery call with Zoe next week" && s.Tester.HasText($"Step {failed} failed") &&
            s.Tester.ContainsText("No shared free slot next week") && s.Exists(Role.Button, "Retry") && s.Exists(Role.Button, "Skip"),
            "a failed step explains why and offers Retry and Skip");
        Capture(s, output, "plan-failed", check);
        s.Program.Send(new CrmMsg.SetDark(true));
        s.Tester.Settle();
        Capture(s, output, "plan-failed-dark", check);
        s.Program.Send(new CrmMsg.SetDark(false));
        s.Tester.Settle();

        s.Click(Role.Button, "Retry");
        check(Plan(s).Status == PlanStatus.Running && Plan(s).Steps[failed - 1] is { State: StepState.Running, Attempt: 1, Shown: 0 }, "Retry runs the step again");
        s.Click(Role.Button, "Pause");
        var paused = Plan(s);
        Tick(s);
        check(Plan(s) == paused && s.Tester.HasText($"Paused on step {failed}"), "a paused plan ignores the clock");
        s.Click(Role.Button, "Resume");
        TickUntil(s, m => m.PlanFor(Deal)!.Steps[failed - 1].State == StepState.Done);
        check(Plan(s).Status == PlanStatus.Running, "the retried step succeeds and the plan moves on");
    }

    static void Toasts(CrmSession s, string output, Action<bool, string> check)
    {
        s.Key(Key.Escape);
        string Title() => $"Working on step {Plan(s).Current + 1} of {Plan(s).Steps.Length}";
        check(s.Model.Overlay is Overlay.None && Plan(s).Minimized && s.Exists(Role.Alert, Title()), "closing a running plan leaves a progress toast");
        var toast = s.Node(Role.Alert, Title());
        check(toast.Rect.Right > s.Tester.Context.Size.Width - 40 && toast.Rect.Bottom > s.Tester.Context.Size.Height - 40,
            "the toast sits at the bottom right");
        Capture(s, output, "plan-toast", check);
        Tick(s);
        check(s.All(Role.Alert).Single().Name == Title(), "the toast follows the agent while the dialog is closed");

        s.Click(Role.Alert, Title());
        check(s.Model.Overlay is Overlay.Plan { DealId: Deal } && !Plan(s).Minimized, "clicking the toast opens the plan again");
        s.Key(Key.Escape);
        var rect = s.Node(Role.Alert, Title()).Rect;
        var (x, y) = (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        s.Tester.Press(x, y);
        for (var i = 1; i <= 6; i++)
            s.Tester.MoveTo(x + i * 25, y);
        s.Tester.Release(x + 150, y);
        s.Tester.Settle();
        check(!Plan(s).Minimized && !s.All(Role.Alert).Any() && s.Model.Overlay is Overlay.None, "swiping the toast away dismisses it without opening the plan");

        s.Click(Role.Button, Card);
        s.Click(Role.Button, s.All(Role.Button).First(b => b.Name?.StartsWith("Plan, step", StringComparison.Ordinal) == true).Name!);
        s.Key(Key.Escape);
        TickUntil(s, m => m.PlanFor(Deal)!.Status == PlanStatus.Done);
        check(s.Model.Deals.Single(d => d.Id == Deal).Stage == Stage.Qualified, "the agent moves the deal to its next stage on the board");
        check(s.Exists(Role.Alert, "Plan done") && s.Model.Overlay is Overlay.None, "a finished plan's toast says so");
        Capture(s, output, "plan-toast-done", check);
        s.Tester.MoveTo(20, 20);
        s.Tester.Advance(9);
        s.Tester.Settle();
        check(!Plan(s).Minimized && !s.All(Role.Alert).Any(), "a finished plan's toast leaves on its own");
        s.Click(Role.Button, Card);
        check(s.Exists(Role.Button, "Plan done"), "the drawer reports the finished plan");
    }

    static void Tick(CrmSession s)
    {
        s.Program.Send(new CrmMsg.AgentTick());
        s.Tester.Settle();
    }

    static void TickUntil(CrmSession s, Func<CrmModel, bool> done)
    {
        for (var i = 0; i < 80 && !done(s.Model); i++)
            Tick(s);
        if (!done(s.Model))
            throw new InvalidOperationException("The plan did not reach the expected state.");
    }

    static void Capture(CrmSession s, string output, string name, Action<bool, string> check)
    {
        s.Tester.Settle();
        s.Tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
        s.Tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
        var warnings = s.Tester.Context.Warnings;
        check(warnings.Count == 0, $"{name} renders without layout, overlap or contrast warnings: " +
            string.Join("; ", warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));
    }
}
