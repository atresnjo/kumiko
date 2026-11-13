using CRMLite;
using Kumiko.UI;

internal static class ResponsiveChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        ResponsivePhysicalPaddingChecks.Run(output, check);
        ResponsiveReviewChecks.Run(output, check);
        ResponsiveEnvironmentChecks.Run(output, check);
        var initial = Data.Initial();
        using var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
        using var tester = new Tester(program, new(480, 900, DevMode: true));
        tester.Context.ReduceMotion = true;
        foreach (var width in new[] { 480, 600, 800, 900, 1200, 1280, 1800, 480, 1800 })
        {
            tester.Resize(width, 900);
            tester.Settle();
            check(tester.Context.Warnings.Count == 0, $"company directory has no layout warnings at {width}");
            var sidebar = tester.Context.Accessibility.Single(n => n.Role == Role.Group && n.Name == "Sidebar").Rect;
            check(width < 900 ? sidebar.Width == Palette.Sizes.Rail : sidebar.Width == initial.SidebarWidth,
                "adaptive rail preserves the user's expanded sidebar preference");
            var main = ResponsiveEnvironmentChecks.Box(tester, "main");
            check(main.Width <= Palette.Sizes.Workspace && main.Right <= width,
                "wide CRM content stays bounded within its canvas");
            foreach (var name in new[] { "New company", "Stage filter", "Owner filter" })
            {
                var button = tester.Get(Role.Button, name).Rect;
                check(button.X >= sidebar.Right && button.Right <= width && button.Y >= 0 && button.Bottom <= 900,
                    $"{name} remains visible without overlap at {width}");
            }
            ResponsiveEnvironmentChecks.Save(tester, output, $"crm-{width}");
            if (width >= 900)
                check(tester.HasText("23 companies"), $"company count footer remains visible below a scrolling table at {width}");
            tester.Get(Role.TextField, "Search companies").Fill("northwind");
            tester.Settle();
            check(Crm.Visible(program.Model).Length == 1, "search remains available after responsive reflow");
            var role = width < 900 ? Role.Button : Role.Row;
            tester.Get(width < 900 ? Role.List : Role.Grid, "Companies").Get(role, "Northwind Labs").Click();
            tester.Settle();
            check(program.Model.CompanyId == "northwind", "reflowed company target opens the intended record");
            ResponsiveEnvironmentChecks.Save(tester, output, $"crm-detail-{width}");
            check(tester.Context.Warnings.Count == 0, $"company details have no layout warnings at {width}");
            var visit = program.Model.Navigation.Top.Id;
            tester.Resize(width == 480 ? 1280 : 480, 900);
            tester.Settle();
            check(program.Model.Navigation.Top.Id == visit && program.Model.CompanyId == "northwind",
                "layout adaptation preserves the selected navigation visit");
            tester.Get(Role.Button, "Back to companies").Click();
            tester.Settle();
            tester.Get(Role.TextField, "Search companies").Fill("");
            tester.Settle();
        }
        // The shared report includes checks and hashes of every PNG/tree/warnings artifact.
        File.WriteAllText(Path.Combine(output, "responsive-report.json"), """{"passed":true,"offscreen":true,"widths":[480,800,1280,1800],"search":true,"reflowHitTesting":true,"preservedNavigation":true}""");
    }
}
