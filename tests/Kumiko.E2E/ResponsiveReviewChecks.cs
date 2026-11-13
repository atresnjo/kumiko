using System.Text.Json;
using System.Text.Json.Serialization;
using CRMLite;
using Kumiko.Desktop;
using Kumiko.UI;

internal static class ResponsiveReviewChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var results = new List<ResponsiveReviewCheck>();
        void Record(bool passed, string name) => results.Add(new(name, passed));
        foreach (var width in new[] { 1199, 1200, 1280 })
        {
            var initial = Data.Initial() with
            {
                SidebarWidth = CrmModel.MaxSidebar
            };
            using var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
            using var tester = new Tester(program, new(width, 900, DevMode: true));
            tester.Context.ReduceMotion = true;
            tester.Settle();
            ResponsiveEnvironmentChecks.Save(tester, output, $"review-wide-sidebar-{width}");
            var main = ResponsiveEnvironmentChecks.Box(tester, "main");
            var bar = ResponsiveEnvironmentChecks.Box(tester, "toolbar");
            Rect[] controls = [tester.Get(Role.Group, "Company lists").Rect,
                tester.Get(Role.TextField, "Search companies").Rect, tester.Get(Role.ComboBox, "Industry").Rect,
                tester.Get(Role.Button, "Stage filter").Rect, tester.Get(Role.Button, "Owner filter").Rect];
            var fits = controls.All(box => box.X >= main.X && box.Right <= main.Right && box.Y >= bar.Y && box.Bottom <= bar.Bottom);
            Record(fits, $"toolbar targets fit the main pane with maximum sidebar at {width}");
            Record(controls[0].Bottom <= controls[1].Y, $"narrow main pane puts search below list tabs despite window breakpoint at {width}");
            Record(!controls.Where((box, i) => controls.Skip(i + 1).Any(other => box.Intersect(other) is { Width: > .1f, Height: > .1f })).Any(),
                $"toolbar targets do not overlap with maximum sidebar at {width}");
            Record(tester.Context.Warnings.Count == 0, $"maximum-sidebar scene has no layout warnings at {width}");
            var footer = tester.Get(Role.Text, "23 companies").Rect;
            Record(footer.X >= main.X && footer.Right <= main.Right && footer.Bottom <= main.Bottom &&
                ResponsiveEnvironmentChecks.Box(tester, "rows").Bottom <= footer.Y,
                $"fixed count footer remains visible below scrolling rows with maximum sidebar at {width}");
            if (!fits)
                continue;
            tester.Get(Role.TextField, "Search companies").Fill("northwind");
            Record(Crm.Visible(program.Model).Length == 1, $"pane-constrained search works at {width}");
            tester.Get(Role.Button, "Stage filter").Click();
            tester.Settle();
            tester.Get(Role.MenuItem, "Demo").Click();
            tester.Settle();
            Record(program.Model.StageFilter == Stage.Demo, $"reflowed filter dispatches the intended choice at {width}");
        }
        Rail(output, Record);
        File.WriteAllText(Path.Combine(output, "responsive-review-report.json"), JsonSerializer.Serialize(
            new ResponsiveReviewReport(results.All(r => r.Passed), results), ResponsiveReviewJson.Default.ResponsiveReviewReport));
        foreach (var result in results)
            check(result.Passed, result.Name);
    }

    static void Rail(string output, Action<bool, string> record)
    {
        var initial = Data.Initial();
        using var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(seed: initial).Services());
        using var tester = new Tester(program, new(800, 900, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Settle();
        var headerId = tester.Context.Nodes.ToArray().Single(n => n.Key == "sidebar-toggle").Id;
        var header = tester.Context.Accessibility.Single(n => n.Id == headerId);
        record(!header.Enabled && header.Name == "Expand sidebar", "forced rail exposes an accurately named disabled header toggle");
        tester.Context.PerformAction(headerId, new(AccessibilityAction.Invoke));
        tester.Settle();
        record(!program.Model.SidebarCollapsed, "disabled header cannot change the saved sidebar preference");
        Restore();
        var menu = tester.DesktopMenu.Single(item => item.Id == CrmCommands.Sidebar);
        record(menu.Disabled && menu.Label.StartsWith("Show Sidebar", StringComparison.Ordinal), "forced rail disables and accurately labels its native-menu projection");
        menu.Click!(menu);
        tester.Settle();
        record(!program.Model.SidebarCollapsed, "disabled menu callback cannot change the saved sidebar preference");
        Restore();
        tester.Key(Key.Backslash, Modifiers.Primary);
        tester.Settle();
        record(!program.Model.SidebarCollapsed, "forced rail rejects the sidebar keyboard shortcut");
        Restore();
        ResponsiveEnvironmentChecks.Save(tester, output, "review-rail-800");
        File.WriteAllLines(Path.Combine(output, "responsive-review-rail-menu.txt"), tester.DesktopMenu.Select(item => $"{item.Id}: {item.Label} disabled={item.Disabled}"));
        OpenSettings();
        var toggle = tester.Get(Role.Switch, "Collapsed sidebar");
        var setting = tester.Context.Accessibility.Single(n => n.Id == toggle.Id);
        record(!toggle.Enabled && setting.Checked, "forced rail Settings switch is disabled and checked");
        tester.Context.PerformAction(toggle.Id, new(AccessibilityAction.Invoke));
        tester.Settle();
        record(!program.Model.SidebarCollapsed, "disabled Settings switch cannot change the saved preference");
        ResponsiveEnvironmentChecks.Save(tester, output, "review-rail-settings-800");
        tester.Get(Role.Button, "Close settings").Click();
        Restore();
        tester.Resize(1280, 900);
        tester.Settle();
        record(tester.Get(Role.Button, "Collapse sidebar").Enabled && !tester.DesktopMenu.Single(item => item.Id == CrmCommands.Sidebar).Disabled,
            "resizing out of the forced rail restores header and menu controls");
        tester.Get(Role.Button, "Collapse sidebar").Click();
        tester.Settle();
        record(program.Model.SidebarCollapsed, "restored header visibly collapses the sidebar");
        var show = tester.DesktopMenu.Single(item => item.Id == CrmCommands.Sidebar);
        show.Click!(show);
        tester.Settle();
        record(!program.Model.SidebarCollapsed, "restored menu expands the sidebar");
        OpenSettings();
        toggle = tester.Get(Role.Switch, "Collapsed sidebar");
        record(toggle.Enabled && !tester.Context.Accessibility.Single(n => n.Id == toggle.Id).Checked,
            "wide Settings switch reflects the retained expanded preference");
        toggle.Click();
        tester.Settle();
        record(program.Model.SidebarCollapsed, "restored Settings switch changes the sidebar preference");

        void Restore()
        {
            if (program.Model.SidebarCollapsed)
                program.Send(new CrmMsg.ToggleSidebar());
            tester.Settle();
        }
        void OpenSettings()
        {
            program.Send(new CrmMsg.OpenSettings(SettingsSection.Appearance));
            tester.Settle();
            tester.Get(Role.TextField, "Search settings").Fill("Collapsed sidebar");
            tester.Settle();
        }
    }
}
internal sealed record ResponsiveReviewCheck(string Name, bool Passed);
internal sealed record ResponsiveReviewReport(bool Passed, List<ResponsiveReviewCheck> Checks);
[JsonSerializable(typeof(ResponsiveReviewReport))]
partial class ResponsiveReviewJson : JsonSerializerContext;
