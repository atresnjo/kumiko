using Kumiko.Desktop;
using Kumiko.UI;
using Launcher;

static class BehaviorFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using (var ui = new Session())
        {
            check(ui.Model.Entries.Length == 6 && ui.Desktop.Visible, "fixture startup loads six searchable applications and summons");
            check(ui.Desktop.StartupWrites == 0, "ordinary startup reads preferences without registering a login item");
            check(ui.Tester.Context.Accessibility.Any(n => n.Name == "Search applications" && n.Id == ui.Tester.Context.Focus), "summon focuses the accessible search field");
            ui.Scene(output, "launcher-light");
            ui.Query("eDiToR");
            check(ui.Model.Results is [{ Name: "Code Editor" }], "search filters application display names without case sensitivity");
            ui.Key(Key.Enter);
            check(ui.Desktop.Events.TakeLast(2).SequenceEqual(["hide", "launch:dev.fixture.editor"]), "Enter hides before launching exactly the filtered target");
            check(!ui.Desktop.Visible && !ui.Model.Visible && !ui.Desktop.QuitRequested, "launch dismissal preserves the host instead of quitting");
            ui.Host.Summon();
            ui.Pump();
            ui.Key(Key.Down);
            ui.Key(Key.Down);
            check(ui.Model.Selected == 2, "Down selects the next application while the search field stays focused");
            ui.Key(Key.Up);
            ui.Key(Key.Enter);
            check(ui.Desktop.Events.Last() == "launch:dev.fixture.editor", "Up then Enter launches the keyboard-selected identity");
            ui.Host.Summon();
            ui.Pump();
            ui.Key(Key.Up);
            check(ui.Model.Selected == 0, "Up at the first result remains in bounds");
            ui.Key(Key.Down, Modifiers.Alt);
            check(ui.Model.Selected == 0, "modified arrow keys retain editor precedence");
            ui.Query("no-such-application");
            check(ui.Model.Results.Length == 0 && ui.Tester.ContainsText("No matching applications"), "no-result search provides an explanatory state");
            ui.Scene(output, "launcher-no-results");
            var launches = ui.Desktop.Events.Count(e => e.StartsWith("launch:", StringComparison.Ordinal));
            ui.Key(Key.Enter);
            check(ui.Desktop.Events.Count(e => e.StartsWith("launch:", StringComparison.Ordinal)) == launches, "Enter with no results cannot launch a stale selection");
            ui.Key(Key.Escape);
            check(!ui.Desktop.Visible && !ui.Desktop.QuitRequested, "Escape dismisses the panel without exiting");
            for (var i = 0; i < 20; i++)
            {
                ui.Host.Summon();
                ui.Pump();
                ui.Desktop.Blur();
                ui.Pump();
            }
            check(!ui.Model.Visible && ui.Desktop.Shows == 23, "repeated summon and blur preserve the same controller and dismiss state");
            ui.Host.Summon();
            ui.Pump();
            ui.Tester.Compose("日本", false);
            ui.Key(Key.Down);
            check(ui.Model.Selected == 0 && ui.Desktop.Visible, "marked composition owns arrow keys");
            ui.Key(Key.Enter);
            check(ui.Desktop.Visible, "composition Enter does not launch an application");
            ui.Tester.Compose("日本", false);
            ui.Key(Key.Escape);
            check(ui.Desktop.Visible, "composition Escape cancels marked text without dismissing the panel");
            ui.Tester.Compose("", true);
            ui.Pump();
        }
        using (var dark = new Session(theme: Theme.DarkTheme))
            dark.Scene(output, "launcher-dark");
        using (var scroll = new Session())
        {
            var initialHeight = scroll.Tester.Get(Role.Button, "Launch Calendar").Rect.Height;
            for (var i = 0; i < 5; i++)
                scroll.Key(Key.Down);
            check(scroll.Model.Selected == 5, "keyboard navigation reaches the final application in a scrollable catalog");
            scroll.Scene(output, "launcher-last-result");
            for (var i = 0; i < 5; i++)
                scroll.Key(Key.Up);
            scroll.Scene(output, "launcher-first-result");
            var first = scroll.Tester.Get(Role.Button, "Launch Calendar").Rect;
            var viewport = scroll.Tester.Get(Role.List, "Applications").Rect;
            check(scroll.Model.Selected == 0 && first.Height == initialHeight && first.Y >= viewport.Y && first.Bottom <= viewport.Bottom, "keyboard return to a previously revealed result scrolls the whole selected row into view");
        }
        Preferences(output, check);
        FailureFlows(output, check);
    }

    static void Preferences(string output, Action<bool, string> check)
    {
        using var ui = new Session();
        ui.Click("Settings");
        ui.Tester.Get(Role.Checkbox, "Start at login").Click();
        ui.Pump();
        check(ui.Model.Startup.State == StartupState.Enabled && ui.Desktop.StartupWrites == 1, "explicit startup control enables the injected login item once");
        ui.Desktop.StartupFails = true;
        ui.Tester.Get(Role.Checkbox, "Start at login").Click();
        ui.Pump();
        check(ui.Model.Startup.State == StartupState.Enabled && ui.Tester.ContainsText("Could not change start at login"), "failed startup mutation preserves the observed setting and explains recovery");
        ui.Desktop.StartupFails = false;
        ui.Tester.Get(Role.Checkbox, "Start at login").Click();
        ui.Pump();
        check(ui.Model.Startup.State == StartupState.Disabled, "explicit startup control can disable the same owned item");
        ui.Desktop.ShortcutConflict = true;
        ui.Tester.Get(Role.TextField, "Global shortcut").Fill("Control+Alt+K");
        ui.Click("Apply shortcut");
        check(ui.Model.Shortcut == "Alt+Shift+Space" && ui.Desktop.Shortcuts.ContainsKey("Alt+Shift+Space"), "shortcut conflict retains the previous working registration");
        check(ui.Tester.ContainsText("Shortcut unavailable"), "shortcut conflict is visible in Settings");
        ui.Scene(output, "launcher-settings-error");
        ui.Desktop.ShortcutConflict = false;
        ui.Click("Apply shortcut");
        check(ui.Model.Shortcut == "Control+Alt+K" && !ui.Desktop.Shortcuts.ContainsKey("Alt+Shift+Space"), "successful shortcut replacement retires the previous registration");
        ui.Key(Key.Escape);
        check(!ui.Model.Settings && ui.Desktop.Visible, "Escape leaves settings before dismissing search");
        ui.Desktop.Shortcuts["Control+Alt+K"]();
        ui.Pump();
        check(!ui.Desktop.Visible, "registered fixture shortcut toggles the existing panel");
        ui.Host.Summon();
        ui.Pump();
        ui.Click("Settings");
        ui.Click("Quit launcher");
        check(ui.Desktop.QuitRequested, "Quit is available independently of a working shortcut or tray");
    }

    static void FailureFlows(string output, Action<bool, string> check)
    {
        using (var empty = new Session(new()
        {
            Load = _ => Task.FromResult<IReadOnlyList<InstalledApplication>>([])
        }))
        {
            check(empty.Tester.ContainsText("No applications available"), "empty catalog is distinct from an unmatched query");
            empty.Scene(output, "launcher-empty");
        }
        using (var failed = new Session(new()
        {
            Load = _ => Task.FromException<IReadOnlyList<InstalledApplication>>(new IOException("Fixture scan unavailable"))
        }))
        {
            check(!failed.Model.Loading && failed.Tester.ContainsText("Fixture scan unavailable"), "catalog failure ends loading and preserves retry access");
            failed.Scene(output, "launcher-catalog-error");
            failed.Desktop.Load = _ => Task.FromResult<IReadOnlyList<InstalledApplication>>(FixtureLauncherDesktop.Applications);
            failed.Click("Refresh applications");
            check(failed.Model.Entries.Length == 6 && failed.Model.Error is null, "catalog retry restores searchable results");
        }
        using (var launch = new Session(new()
        {
            LaunchFails = true
        }))
        {
            launch.Key(Key.Enter);
            launch.Wait(() => launch.Model.Error is not null);
            check(launch.Desktop.Visible && launch.Tester.ContainsText("Could not open Calendar"), "launch failure reopens the existing panel with an actionable error");
            launch.Scene(output, "launcher-launch-error");
        }
        var apps = FixtureLauncherDesktop.Applications;
        using (var duplicates = new Session(new()
        {
            Load = _ => Task.FromResult<IReadOnlyList<InstalledApplication>>([apps[0], apps[0] with { Path = apps[1].Path }, apps[0] with { Id = "alias" }, apps[1] with { Name = "Calendar" }])
        }))
            check(duplicates.Model.Entries.Length == 2, "duplicate identities and paths collapse while equal names at different identities survive");
        using (var unsupported = new Session(new()
        {
            ShortcutsUnsupported = true
        }))
        {
            check(unsupported.Desktop.Visible && unsupported.Model.Entries.Length == 6 && unsupported.Tester.ContainsText("shortcut service unavailable"), "unsupported shortcut service leaves the catalog, panel and recovery controls usable");
            unsupported.Click("Settings");
            unsupported.Click("Quit launcher");
            check(unsupported.Desktop.QuitRequested, "Quit remains available when the desktop cannot supply a global shortcut");
        }
        using var login = new Session(new()
        {
            LoginLaunch = true
        });
        check(!login.Desktop.Visible && login.Model.Entries.Length == 6, "login launch loads the catalog without summoning");
        login.Host.Summon();
        login.Pump();
        check(login.Desktop.Visible, "second-instance summon routes a login-hidden host to the existing panel");
    }
}
