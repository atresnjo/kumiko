using Flint;
using Flint.Git;
using Kumiko.UI;

static class Notifications
{
    public static void Run(Harness h, Repositories fixture)
    {
        var output = "fatal: 'feature/adnan/shared-msgraph-event' is already used by worktree at '"
            + Path.Combine(fixture.Root, new string('x', 180), "linked-worktree") + "'";
        h.Program.Send(new Msg.Notify(output, ToastKind.Error));
        h.Idle();
        h.Tester.Resize(900, 560);
        h.Tester.Settle();
        h.Shot("notification-compact");
        h.Check(h.Has(Role.Button, "Show notification: " + output), "Long notification offers a clickable details action");
        h.Click("Show notification: " + output);
        h.Check(h.Has(Role.Dialog, "Error details"), "Click opens notification details");
        h.Shot("notification-details");
        h.Click("Copy Output");
        h.Check(h.Tester.Clipboard == output, "Copy includes the entire long worktree path");
        var page = h.Tab.Page;
        h.Press(Key.L, Modifiers.Primary);
        h.Check(h.Tab.Page == page, "Open notification details own keyboard shortcuts");
        h.Press(Key.Escape);
        h.Check(h.Model.Pending is null, "Escape closes notification details");
        h.Tester.Get(Role.Button, "Show notification: " + output).Perform(new(AccessibilityAction.Focus));
        h.Press(Key.Enter);
        h.Check(h.Model.Pending?.Details == output, "Keyboard activation opens focused notification details");
        h.Click("Close");
        var multiline = string.Join('\n', Enumerable.Range(0, 450).Select(i => $"output line {i:D3}"));
        h.Program.Send(new Msg.Notify(multiline, ToastKind.Error));
        h.Idle();
        h.Click("Show notification: " + multiline);
        h.Check(h.Tester.ContainsText("output line 000") && h.Tester.ContainsText("output line 449"), "Details retain all output lines including the first and last");
        var toast = h.Model.Toasts.Last();
        h.Program.Send(new Msg.DismissToast(toast.Id));
        h.Idle();
        h.Check(h.Model.Pending?.Details == multiline, "Toast expiry preserves open details");
        h.Click("Copy Output");
        h.Check(h.Tester.Clipboard == multiline, "Multiline output copies without truncation");
        h.Shot("notification-multiline");
        h.Click("Close");
        h.Tester.Resize(1440, 900);
        h.Tester.Settle();
        var worktree = Path.Combine(fixture.Root, new string('w', 180), "linked-worktree");
        Repositories.Git(fixture.Repo, "worktree", "add", "-b", "notification-conflict", worktree);
        h.Program.Send(new Msg.Run(new(GitAction.Checkout, Value: "notification-conflict")));
        h.Idle();
        var failure = h.Model.Toasts.Last();
        h.Check(failure.Text.Contains(worktree, StringComparison.Ordinal), "Real Git checkout failure preserves the conflicting worktree path");
        h.Click("Show notification: " + failure.Text);
        h.Click("Copy Output");
        h.Check(h.Tester.Clipboard.Contains(worktree, StringComparison.Ordinal)
            && h.Tester.Clipboard.Contains("fatal:", StringComparison.Ordinal), "Real checkout error opens and copies complete command output");
        h.Shot("notification-real-git");
        h.Click("Close");
        Repositories.Git(fixture.Repo, "worktree", "remove", worktree);
        Repositories.Git(fixture.Repo, "branch", "-D", "notification-conflict");
        h.Press(Key.R, Modifiers.Primary);
    }
}
