using CRMLite;
using Kumiko.UI;

/// <summary>Repeatable PNG, tree and warning artifacts for every page in light and dark.</summary>
static class ArtifactFlows
{
    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        foreach (var dark in new[] { false, true })
        {
            var suffix = dark ? "-dark" : "";
            if (dark)
            {
                s.Click(Role.Button, "Account");
                s.Click(Role.Option, "Dark");
                s.Click(Role.Button, "Close settings");
                check(s.Model.Dark && s.Tester.Context.Theme.Dark, "settings switch the whole app to dark mode");
            }
            s.Click(Role.Button, "Companies");
            Capture(s, output, "companies" + suffix, check);
            s.Click(Role.Row, "Northwind Labs");
            Capture(s, output, "company" + suffix, check);
            s.Click(Role.Button, "Deals");
            Capture(s, output, "deals" + suffix, check);
            s.Click(Role.Button, "Owner filter");
            Capture(s, output, "owner-menu" + suffix, check);
            s.Key(Key.Escape);
            s.Click(Role.Button, "New deal");
            Capture(s, output, "new-deal" + suffix, check);
            s.Key(Key.Escape);
            s.Click(Role.Button, "Search");
            Capture(s, output, "search" + suffix, check);
            s.Key(Key.Escape);
            s.Click(Role.Button, "People");
            Capture(s, output, "people" + suffix, check);
            s.Click(Role.Button, "Inbox");
            Capture(s, output, "inbox" + suffix, check);
        }
        s.Tester.Resize(1100, 720);
        s.Click(Role.Button, "Companies");
        Capture(s, output, "companies-narrow", check);
    }

    static void Capture(CrmSession s, string output, string name, Action<bool, string> check)
    {
        s.Tester.Settle();
        s.Tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
        File.WriteAllLines(Path.Combine(output, name + ".ids.txt"), s.Tester.Context.Nodes.ToArray().Select(n => $"{n.Id:x16} {n.Key}"));
        s.Tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
        var warnings = s.Tester.Context.Warnings;
        check(warnings.Count == 0, $"{name} renders without layout, overlap or contrast warnings: " +
            string.Join("; ", warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));
    }
}
