using Kumiko.Runtime;
using Kumiko.UI;

static class ComplexFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var results = new List<(bool Passed, string Name)>();
        void Record(bool passed, string name) => results.Add((passed, name));
        Overlays(output, Record);
        Stacks(output, Record);
        File.WriteAllLines(Path.Combine(output, "complex-results.txt"), results.Select(result =>
            (result.Passed ? "PASS " : "FAIL ") + result.Name));
        foreach (var result in results)
            check(result.Passed, result.Name);
    }

    static void Capture(Tester tester, string output, string name)
    {
        tester.MoveTo(0, 0);
        tester.Settle();
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
        if (tester.Context.Warnings.Count != 0)
            throw new InvalidOperationException(name + ": " + string.Join("; ", tester.Context.Warnings.Select(w => w.Message)));
    }

    static void Overlays(string output, Action<bool, string> check)
    {
        foreach (var drawerOutside in new[] { true, false })
        {
            var name = drawerOutside ? "dialog-over-drawer" : "drawer-over-dialog";
            using var program = ComplexApp.CreateProgram(ComplexApp.Init with
            {
                DrawerOutside = drawerOutside
            });
            using var tester = new Tester(program, new(900, 700, DevMode: true));
            tester.Context.ReduceMotion = true;
            tester.Get(Role.Button, "Open outer").Invoke();
            tester.Get(Role.Button, "Open inner").Invoke();
            tester.Settle();
            Capture(tester, output, "complex-" + name + "-open");
            tester.Navigate(NavigationDirection.Back);
            check(program.Model.Navigation.Position == 1 && program.Model.BackCount == 0, name + " blocks covered screen and stack Back");
            check(!tester.Get(Role.Button, "Open outer").Enabled && !tester.Get(Role.Button, "Open inner").Enabled,
                name + " blocks semantic background actions");
            for (var i = 0; i < 8; i++)
                tester.Key(Key.Tab);
            check(tester.Context.Accessibility.Any(n => n.Focused && n.Enabled && !n.Inert && n.Name is "Inner action" or "Close"),
                name + " confines Tab to the top layer");
            tester.Key(Key.Escape);
            tester.Settle();
            check(program.Model.Outer && !program.Model.Inner, name + " Escape dismisses only the top layer");
            Capture(tester, output, "complex-" + name + "-escape");
            if (program.Model.Outer && !program.Model.Inner)
            {
                check(tester.Get(Role.Button, "Open inner").Enabled, name + " restores the underlying modal scope");
                tester.Get(Role.Button, "Open inner").Invoke();
                tester.Get(Role.Button, "Inner action").Invoke();
                check(program.Model.Outer && !program.Model.Inner, name + " underlying flow remains usable after dismissal");
                tester.Key(Key.Escape);
                tester.Settle();
                check(!program.Model.Outer, name + " a second Escape dismisses the parent");
            }
        }
        foreach (var picker in new[] { true, false })
        {
            var name = picker ? "picker" : "popover";
            using var program = ComplexApp.CreateProgram(ComplexApp.Init with
            {
                DrawerOutside = false,
                Outer = true
            });
            using var tester = new Tester(program, new(900, 700, DevMode: true));
            tester.Context.ReduceMotion = true;
            tester.Get(picker ? Role.ComboBox : Role.Button, picker ? "Fruit" : "Open menu").Click();
            tester.Settle();
            Capture(tester, output, "complex-" + name + "-open");
            tester.Key(Key.Escape);
            tester.Settle();
            check(program.Model.Outer && !program.Model.Menu && program.Model.Picker?.IsOpen != true,
                name + " Escape closes the popup without dismissing its stock dialog");
            Capture(tester, output, "complex-" + name + "-escape");
        }
        using (var program = ComplexApp.CreateProgram(ComplexApp.Init with
        {
            Outer = true
        }))
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Get(Role.TextField, "Modal draft").Click();
            tester.Compose("pending", false);
            tester.Key(Key.Escape);
            tester.Settle();
            check(program.Model.Outer, "Escape during IME composition does not dismiss the drawer");
        }
    }

    static void Stacks(string output, Action<bool, string> check)
    {
        using (var program = ComplexApp.CreateProgram())
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Context.ReduceMotion = true;
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Navigation.Position == 1 && program.Model.Left.Entries.Length == 1 && program.Model.BackCount == 1,
                "mouse Back dispatches exactly once to the child stack before the root");
            check(program.History.Last().Source?.Path == "/navigation/left", "child Back records its own navigation source");
            Capture(tester, output, "complex-child-back");
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Navigation.Current.Route == "home" && program.Model.BackCount == 1, "child stack root bubbles Back to screen history");
            tester.Navigate(NavigationDirection.Forward);
            tester.Settle();
            check(program.Model.Navigation.Current.Route == "workspace", "path-only child stacks leave Forward to screen history");
            var session = Path.Combine(output, "complex-session.jsonl");
            program.Save(session);
            using var replay = ComplexApp.CreateProgram();
            replay.Replay(session);
            using var replayTester = new Tester(replay, new(900, 700, DevMode: true));
            check(replay.Model.Left.Entries.SequenceEqual(program.Model.Left.Entries) && replay.Model.BackCount == 1 &&
                replay.Model.Navigation.Current.Route == "workspace", "nested Back and root history replay without synthetic requests");
            Capture(replayTester, output, "complex-replay");
        }
        using (var program = ComplexApp.CreateProgram(ComplexApp.Init with
        {
            Nested = true
        }))
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Right.Entries.Length == 1 && program.Model.Left.Entries.Length == 2 && program.Model.Navigation.Position == 1,
                "deepest nested stack handles Back first without requiring focus");
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Left.Entries.Length == 1 && program.Model.Navigation.Position == 1, "exhausted nested stack bubbles Back to its parent stack");
        }
        using (var program = ComplexApp.CreateProgram(ComplexApp.Init with
        {
            Siblings = true
        }))
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Get(Role.Button, "Right action").Click();
            tester.Key(OperatingSystem.IsMacOS() ? Key.BracketLeft : Key.Left,
                OperatingSystem.IsMacOS() ? Modifiers.Primary : Modifiers.Alt);
            tester.Settle();
            check(program.Model.Right.Entries.Length == 1 && program.Model.Left.Entries.Length == 2,
                "keyboard Back follows focus between sibling stacks");
            tester.Get(Role.TextField, "Left draft").Click();
            tester.Compose("pending", false);
            tester.Navigate(NavigationDirection.Back);
            check(program.Model.Left.Entries.Length == 2 && program.Model.BackCount == 1, "IME composition blocks child Back");
            tester.Compose("done", true);
            tester.Settle();
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Left.Entries.Length == 1 && program.Model.Draft == "done", "committed input reaches Update before child Back");
            Capture(tester, output, "complex-sibling-back");
        }
        using (var program = ComplexApp.CreateProgram(ComplexApp.Init with
        {
            Block = true
        }))
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.BackCount == 1 && program.Model.Left.Entries.Length == 2 && program.Model.Navigation.Position == 1,
                "a rejected child Back does not fall through to root navigation");
        }
        using (var program = ComplexApp.CreateProgram(ComplexApp.Init with
        {
            HideLeft = true
        }))
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Navigation.Current.Route == "home" && program.Model.BackCount == 0,
                "a hidden child stack never consumes screen Back");
        }
        using (var program = ComplexApp.CreateProgram(ComplexApp.Init with
        {
            Siblings = true,
            Right = NavigationPath<string, NavigationVisit<string>>.Start("right-root")
        }))
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Get(Role.Button, "Right action").Click();
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Navigation.Current.Route == "home" && program.Model.Left.Entries.Length == 2 && program.Model.BackCount == 0,
                "an exhausted focused sibling never pops another sibling stack");
        }
        using (var program = ComplexApp.CreateProgram(ComplexApp.Init with
        {
            Outer = true,
            ModalStack = true
        }))
        using (var tester = new Tester(program, new(900, 700, DevMode: true)))
        {
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.Right.Entries.Length == 1 && program.Model.Left.Entries.Length == 2 && program.Model.Navigation.Position == 1,
                "Back reaches a stack inside the active modal without escaping to the page");
            tester.Navigate(NavigationDirection.Back);
            tester.Settle();
            check(program.Model.BackCount == 1 && program.Model.Navigation.Position == 1, "modal stack root does not bubble Back to the covered screen");
            Capture(tester, output, "complex-modal-stack");
        }
    }
}
