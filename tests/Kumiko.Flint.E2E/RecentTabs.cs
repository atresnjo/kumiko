using Flint;
using Kumiko.UI;

/// <summary>Control/Option+Tab switches repositories in most recently used order and commits on release.</summary>
static class RecentTabs
{
    public static void Run(Harness h, string output)
    {
        var tester = h.Tester;
        var a = h.Model.Workspace.Active!;
        var order = h.Model.RecentOrder;
        h.Check(order.Length == 3 && order[0] == a, "Recent order starts with the active repository");
        var b = order[1];
        var c = order[2];
        var focus = tester.Context.Focus;

        h.Press(Key.Tab, Modifiers.Control);
        h.Check(h.Model.Workspace.Active == b && h.Model.Switcher is null, "A quick Control+Tab switches to the previous repository");
        h.Check(tester.Context.Focus == focus, "Control+Tab does not move keyboard focus");
        h.Press(Key.Tab, Modifiers.Control);
        h.Check(h.Model.Workspace.Active == a, "A second quick Control+Tab returns, like Alt+Tab");
        h.Press(Key.Tab, Modifiers.Alt);
        h.Check(h.Model.Workspace.Active == b, "Option+Tab switches like Control+Tab");
        h.Press(Key.Tab, Modifiers.Alt);
        h.Check(h.Model.Workspace.Active == a && h.Model.RecentOrder.SequenceEqual([a, b, c]), "Switching keeps most recently used order");

        tester.Context.Input.HeldModifiers = Modifiers.Control;
        h.Press(Key.Tab, Modifiers.Control);
        h.Check(h.Model.Switcher?.Target == b && h.Model.Workspace.Active == a, "Holding Control keeps the repository until release");
        h.Check(!h.Has(Role.List, "Recent repositories"), "A short hold shows no overlay");
        h.Press(Key.Tab, Modifiers.Control);
        h.Check(h.Model.Switcher?.Target == c && h.Model.Workspace.Active == a, "Tab steps through recent repositories without switching");
        h.WaitFor(() => h.Has(Role.List, "Recent repositories"), "Holding shows the recent repositories overlay");
        h.Check(h.Has(Role.ListItem, Name(h, c)), "The overlay lists the target repository");
        h.Shot("recent-tabs");
        tester.Context.Input.HeldModifiers = Modifiers.None;
        h.Idle();
        h.Check(h.Model.Workspace.Active == c && h.Model.Switcher is null, "Releasing Control switches to the chosen repository");
        h.Check(h.Model.RecentOrder.SequenceEqual([c, a, b]), "The chosen repository becomes most recent");

        tester.Context.Input.HeldModifiers = Modifiers.Control;
        h.Press(Key.Tab, Modifiers.Control | Modifiers.Shift);
        h.Check(h.Model.Switcher?.Target == b, "Shift reverses to the least recent repository");
        h.Press(Key.Escape);
        tester.Context.Input.HeldModifiers = Modifiers.None;
        h.Idle();
        h.Check(h.Model.Workspace.Active == c && h.Model.Switcher is null, "Escape cancels the switch");

        tester.Context.Input.HeldModifiers = Modifiers.Control;
        h.Press(Key.Tab, Modifiers.Control);
        h.WaitFor(() => h.Has(Role.List, "Recent repositories"), "The next held switch shows its modal overlay");
        h.Press(Key.Escape);
        h.Check(h.Model.Workspace.Active == c && h.Model.Switcher is null, "Escape cancels a visible switcher before modifier release");
        tester.Context.Input.HeldModifiers = Modifiers.None;
        h.Idle();

        tester.Context.Input.HeldModifiers = Modifiers.Alt;
        h.Press(Key.Tab, Modifiers.Alt);
        tester.Context.Input.HeldModifiers = Modifiers.None;
        h.Idle();
        h.Check(h.Model.Workspace.Active == a, "Releasing Option commits its switch");
    }

    static string Name(Harness h, string path) => (h.Model.Repositories.TryGetValue(path, out var tab) ? tab.Snapshot.Name : null) ?? Path.GetFileName(path);
}
