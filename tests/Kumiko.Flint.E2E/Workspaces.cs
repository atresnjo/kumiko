using Kumiko.UI;

/// <summary>Workspace palette rows, the gh account picker, Quick Launch groups and deleting a non-empty workspace.</summary>
static class Workspaces
{
    public static void Run(Harness h, Repositories fixture)
    {
        h.Click("Workspace: Default");
        h.Press(Key.G, Modifiers.Primary);
        h.WaitFor(() => h.Has(Role.Button, "octo-work"), "The GitHub action lists gh logins for the highlighted workspace");
        h.Check(h.Tester.ContainsText("gh active account") && h.Tester.ContainsText("repo, read:org"), "Account rows show active state and scopes");
        h.Shot("account-picker");
        h.Click("octo-work");
        h.Check(h.Model.Workspace.Account == "octo-work" && h.Tester.ContainsText("Default now pushes as octo-work"), "Picking a login pins it to the workspace");
        h.Check(h.Tester.ContainsText("octo-work"), "The head pill shows the pinned account");
        h.Click("Workspace: Default");
        h.Click("New Workspace…");
        h.Type("Workspace name", "Review");
        h.Press(Key.Enter);
        h.Check(h.Model.Palette == Flint.PaletteMode.Open, "A new workspace continues to Open Repository");
        h.Press(Key.Escape);
        h.Check(h.Model.Workspace.Name == "Review" && h.Model.Current is null, "Workspace has independent repository tabs");
        h.Click("Quick Launch");
        h.Check(h.Tester.ContainsText("OTHER WORKSPACES") && h.Tester.ContainsText("COMMANDS"), "Quick Launch groups repositories from other workspaces and commands");
        h.Type("Quick Launch", "flint-fixture");
        h.Check(h.Tester.ContainsText("open in Default"), "Repositories open elsewhere name their workspace");
        h.Press(Key.Enter);
        h.Check(h.Model.Workspace.Name == "Default" && h.Model.Current?.Snapshot.Path == fixture.Repo, "Opening a repository from another workspace switches to it");
        h.Click("Workspace: Default");
        h.Click("New Workspace…");
        h.Type("Workspace name", "Temp");
        h.Press(Key.Enter);
        h.Type("Open repository", fixture.History);
        h.Press(Key.Enter);
        h.Check(h.Model.Workspace.Name == "Temp" && h.Model.Workspace.Repositories.Length == 1, "A workspace holds its own repositories");
        h.Click("Workspace: Temp");
        h.Press(Key.Down);
        h.Press(Key.Down);
        h.Check(h.Model.Highlight == 2, "Arrow keys move the palette highlight");
        h.Press(Key.D, Modifiers.Primary);
        h.Check(h.Has(Role.Button, "Confirm delete") && h.Model.Workspaces.Length == 3, "Deleting a workspace asks for a second press");
        h.Press(Key.D, Modifiers.Primary);
        h.Check(h.Model.Workspaces.Length == 2 && h.Model.Workspaces.All(w => w.Name != "Temp") && Directory.Exists(fixture.History),
            "A workspace with repositories can be deleted; the repositories stay on disk");
        h.Check(h.Model.Workspace.Name == "Default" && h.Model.Current?.Snapshot.Path == fixture.Repo, "Deleting the active workspace switches to another");
    }
}
