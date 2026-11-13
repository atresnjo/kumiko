using System.Security.Cryptography;
using System.Text.Json;
using Components;
using Kumiko.Components;
using Kumiko.UI;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/components");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name);
    checks.Add(name);
}

ThemeChecks.Run(Check);
ToastChecks.Run(output, Check);
using (var showcase = new ShowcaseSession())
{
    FormChecks.Run(showcase, Check);
    NavigationChecks.Run(showcase, Check);
    OverlayChecks.Run(showcase, output, Check);
    PopoverChecks.Run(showcase, output, Check);
    DataChecks.Run(showcase, output, Check);
    ArtifactChecks.Run(showcase, output, Check);
}

File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(checks, ReportJson.Default.ListString));
Console.WriteLine($"{checks.Count} component checks passed; artifacts: {output}");

/// <summary>Drives the Components example through Kumiko input only; no window or system input is used.</summary>
sealed class ShowcaseSession : IDisposable
{
    public Kumiko.Runtime.Program<ShowcaseModel, ShowcaseMsg> Program { get; } = ComponentsApp.CreateProgram(ComponentsApp.Init);
    public Tester Tester
    {
        get;
    }
    public ShowcaseModel Model => Program.Model;

    public ShowcaseSession()
    {
        Tester = new Tester(Program, new(1180, 860, DevMode: true));
        Tester.Context.ReduceMotion = true;
        Tester.Frame(0);
    }

    public AccessibleNode Node(Role role, string name) =>
        Tester.Context.Accessibility.Single(n => n.Role == role && n.Name == name && !n.Offscreen);

    public bool Exists(Role role, string name) =>
        Tester.Context.Accessibility.Any(n => n.Role == role && n.Name == name && !n.Offscreen);

    public AccessibleNode? Focused => Tester.Context.Accessibility.FirstOrDefault(n => n.Focused);

    public void Open(string section)
    {
        Tester.Get(Role.Button, section).Click();
        Tester.Settle();
    }

    public void Click(Role role, string name)
    {
        Tester.Get(role, name).Click();
        Tester.Settle();
    }

    public void Key(Key key, Modifiers modifiers = Modifiers.None)
    {
        Tester.Key(key, modifiers);
        Tester.Settle();
    }

    public void Dispose()
    {
        Tester.Dispose();
        Program.Dispose();
    }
}

static class ThemeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var light = KitTheme.Light;
        check(ReferenceEquals(light.Theme, light.Theme), "a kit theme maps to one stable core theme instance");
        check(ReferenceEquals((light with
        {
        }).Theme, light.Theme), "value-equal kit themes share their core theme");
        check(light.Theme.Accent == light.Primary.Background && light.Theme.Radius.Md == light.Radius,
            "core widgets inherit the kit primary color and radius");
        var brand = light with
        {
            Primary = KitTheme.Brand(Color.Hex(0x7c3aed), false),
            Radius = 10
        };
        check(brand.Theme.Accent == Color.Hex(0x7c3aed) && brand.Theme.Radius.Md == 10 && brand.Theme.Radius.Lg > 10,
            "customized kit themes regenerate the core theme scales");
        check(KitTheme.Of(brand.Theme) == brand, "a core theme created by a kit resolves back to that kit");
        var legacy = KitTheme.Of(Theme.DarkTheme);
        check(legacy.IsDark && legacy.Primary.Background == Theme.DarkTheme.Accent,
            "kit components derive their palette from themes they did not create");
    }
}

static class FormChecks
{
    public static void Run(ShowcaseSession s, Action<bool, string> check)
    {
        s.Open("Forms");
        check(s.Node(Role.Button, "Forms").Properties?.Selected == true, "sidebar items expose the selected section");
        check(!s.Node(Role.Button, "Create account").Enabled, "submit stays disabled until terms are accepted");
        s.Click(Role.Checkbox, "Accept terms and conditions");
        check(s.Model.Terms && s.Node(Role.Checkbox, "Accept terms and conditions").Checked, "checkbox clicks dispatch and expose checked state");
        check(s.Node(Role.Button, "Create account").Enabled, "dependent controls enable in the same interaction");

        s.Click(Role.Switch, "Airplane mode");
        check(s.Model.Airplane && s.Node(Role.Switch, "Airplane mode").Checked, "switch clicks toggle the model value");
        s.Key(Key.Space);
        check(!s.Model.Airplane, "a focused switch toggles with Space");
        check(!s.Node(Role.Switch, "Bluetooth").Enabled, "disabled switches are not operable");

        s.Click(Role.Option, "Team");
        check(s.Model.Plan == "team" && s.Node(Role.Option, "Team").Properties?.Selected == true, "radio items select their choice");
        s.Key(Key.Up);
        check(s.Model.Plan == "pro" && s.Focused?.Name == "Pro", "arrow keys move radio selection and focus together");

        s.Click(Role.Slider, "Volume");
        s.Key(Key.Home);
        check(s.Model.Volume == 0, "Home moves a slider to its minimum");
        s.Key(Key.Right);
        check(s.Model.Volume == 5 && s.Node(Role.Slider, "Volume").Value == "5", "arrow keys step a slider by its step size");
        s.Key(Key.End);
        check(s.Model.Volume == 100, "End moves a slider to its maximum");

        s.Click(Role.Option, "List");
        check(s.Model.Layout == "list", "toggle groups select a single value");

        s.Click(Role.TextField, "Email");
        s.Tester.TypeText("ada@");
        s.Tester.Settle();
        check(s.Model.Email == "ada@" && s.Tester.HasText("Enter a valid email address."), "fields show validation messages from the model");
        s.Tester.TypeText("example.com");
        s.Tester.Settle();
        check(!s.Tester.HasText("Enter a valid email address."), "valid input removes the field error");
        s.Tester.Context.Blur();
        s.Tester.Settle();
    }
}

static class NavigationChecks
{
    public static void Run(ShowcaseSession s, Action<bool, string> check)
    {
        s.Open("Navigation");
        s.Click(Role.Option, "Password");
        check(s.Model.Tab == "password" && s.Tester.ContainsText("Change your password here."), "tabs switch their panel");
        s.Key(Key.Right);
        check(s.Model.Tab == "notifications" && s.Focused?.Name == "Notifications", "arrow keys activate the adjacent tab");
        s.Key(Key.Right);
        check(s.Model.Tab == "account", "tab keyboard navigation wraps");

        check(s.Node(Role.Button, "Shipping").Properties?.Expanded == true, "accordion items expose expansion");
        s.Click(Role.Button, "Is it accessible?");
        check(s.Model.Expanded == "a11y" && s.Tester.ContainsText("keyboard"), "opening an accordion item shows its content");
        check(s.Node(Role.Button, "Shipping").Properties?.Expanded == false, "single accordions close the previous item");
        s.Click(Role.Button, "Is it accessible?");
        check(s.Model.Expanded is null, "clicking an open accordion item collapses it");

        check(!s.Node(Role.Button, "Previous page").Enabled, "pagination disables Previous on the first page");
        s.Click(Role.Button, "Next page");
        check(!s.Exists(Role.Button, "Page 4") && s.Exists(Role.Button, "Page 10"), "pagination elides distant pages but keeps the last");
        s.Click(Role.Button, "Page 3");
        s.Click(Role.Button, "Page 4");
        check(s.Model.Page == 4 && s.Node(Role.Button, "Page 4").Properties?.Selected == true, "pagination selects pages");

        s.Click(Role.Button, "5 stars");
        check(s.Model.Rating == 5, "rating stars dispatch their value");
    }
}

static class OverlayChecks
{
    public static void Run(ShowcaseSession s, string output, Action<bool, string> check)
    {
        s.Open("Overlays");
        s.Click(Role.Button, "Delete project");
        check(s.Model.DialogOpen && s.Exists(Role.Group, "Delete project?"), "buttons open a modal dialog");
        s.Tester.Screenshot(Path.Combine(output, "dialog-light.png"));
        s.Key(Key.Tab);
        check(s.Focused?.Name is "Cancel" or "Delete" or "Close", "Tab stays inside the dialog");
        s.Key(Key.Escape);
        check(!s.Model.DialogOpen && !s.Exists(Role.Group, "Delete project?"), "Escape dismisses the dialog");
        s.Click(Role.Button, "Delete project");
        s.Click(Role.Button, "Cancel");
        check(!s.Model.DialogOpen && s.Model.LastAction == "Nothing yet", "Cancel closes without acting");
        s.Click(Role.Button, "Delete project");
        s.Click(Role.Button, "Delete");
        check(!s.Model.DialogOpen && s.Model.LastAction == "Project deleted", "confirming runs the dialog action once");
    }
}

static class PopoverChecks
{
    public static void Run(ShowcaseSession s, string output, Action<bool, string> check)
    {
        s.Click(Role.Button, "Account");
        check(s.Model.AccountOpen && s.Exists(Role.MenuItem, "Billing"), "the account row opens its menu popover");
        var account = s.Node(Role.Button, "Account").Rect;
        var menu = s.Node(Role.Group, "Account menu").Rect;
        check(menu.Bottom <= account.Y && menu.X >= 0 && menu.Bottom <= s.Tester.Context.Size.Height,
            "a popover near the window bottom flips above its trigger and stays on screen");
        check(s.Focused?.Role == Role.MenuItem, "opening a menu moves focus to its first item");
        var first = s.Focused?.Name;
        s.Key(Key.Down);
        check(s.Focused?.Role == Role.MenuItem && s.Focused?.Name != first, "Down moves focus to the next menu item");
        s.Key(Key.Up);
        check(s.Focused?.Name == first, "Up moves focus back to the previous menu item");
        s.Tester.Screenshot(Path.Combine(output, "account-menu.png"));
        s.Key(Key.Escape);
        check(!s.Model.AccountOpen && !s.Exists(Role.MenuItem, "Billing"), "Escape closes the popover");
        s.Click(Role.Button, "Account");
        s.Click(Role.MenuItem, "Billing");
        check(!s.Model.AccountOpen && s.Model.LastAction == "Billing", "choosing a menu item acts once and closes the menu");
        s.Click(Role.Button, "Account");
        s.Tester.Click(s.Tester.Context.Size.Width - 20, s.Tester.Context.Size.Height / 2);
        s.Tester.Settle();
        check(!s.Model.AccountOpen, "clicking outside a popover closes it");

        s.Open("Overview");
        s.Key(Key.F, Modifiers.Primary);
        check(s.Model.SearchOpen && s.Focused?.Role == Role.TextField, "Primary+F opens component search with the query focused");
        s.Tester.TypeText("slid");
        s.Tester.Settle();
        check(s.Model.Query == "slid" && s.Exists(Role.MenuItem, "Slider") && !s.Exists(Role.MenuItem, "Button"),
            "typing filters search results");
        s.Tester.Screenshot(Path.Combine(output, "search.png"));
        s.Key(Key.Enter);
        check(!s.Model.SearchOpen && s.Model.Section == "forms" && s.Model.Query == "", "Enter opens the first result's section");
        s.Click(Role.Button, "Search components");
        s.Tester.TypeText("zzzz");
        s.Tester.Settle();
        check(s.Tester.ContainsText("No results found."), "search explains when nothing matches");
        s.Key(Key.Escape);
        s.Click(Role.Button, "Search components");
        check(s.Model.Query == "", "closing search clears the query");
        s.Key(Key.Down);
        s.Key(Key.Enter);
        check(!s.Model.SearchOpen && s.Model.Section == "overview", "Down then Enter picks a highlighted result");
    }
}

static class DataChecks
{
    public static void Run(ShowcaseSession s, string output, Action<bool, string> check)
    {
        s.Tester.Context.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        s.Open("Data");
        check(!s.Node(Role.Button, "Book review").Enabled, "booking waits for a due date");
        s.Click(Role.Button, "Due date");
        check(s.Model.DueOpen && s.Focused?.Name == "Tuesday, October 20, 2026", "the due date picker opens on the pinned today");
        s.Tester.Screenshot(Path.Combine(output, "date-picker.png"));
        s.Key(Key.Down);
        s.Key(Key.Enter);
        check(!s.Model.DueOpen && s.Model.Due == new DateOnly(2026, 10, 27) && s.Node(Role.Button, "Due date").Value == "10/27/2026",
            "choosing a date dispatches it and closes the picker");
        check(s.Node(Role.Button, "Book review").Enabled, "a chosen date enables booking");
        s.Click(Role.Button, "Clear");
        check(s.Model.Due is null, "the clear button removes the date");

        s.Click(Role.TreeItem, "App.cs");
        s.Key(Key.Down);
        s.Key(Key.Right);
        check(s.Model.File == "src/ui" && s.Model.Folders?.Contains("src/ui") == true && s.Exists(Role.TreeItem, "View.cs"),
            "tree keyboard navigation dispatches selection and expansion");
        var handle = s.Node(Role.Separator, "File list width");
        s.Tester.Context.PerformAccessibilityAction(handle.Id, new(AccessibilityAction.SetRange, Number: 280));
        s.Tester.Settle();
        check(s.Model.Pane == 280, "the pane divider is adjustable as a range");

        s.Click(Role.TextField, "Budget");
        s.Key(Key.Up);
        check(s.Model.Budget == 1300.5, "the budget steps by its step size");
        s.Tester.Context.Blur();
        s.Tester.Settle();
        check(s.Node(Role.TextField, "Budget").Value == "1,300.50", "the budget shows grouped culture digits");
        s.Tester.Screenshot(Path.Combine(output, "data-flows.png"));
    }
}

static class ArtifactChecks
{
    public static void Run(ShowcaseSession s, string output, Action<bool, string> check)
    {
        string[] sections = ["Overview", "Forms", "Navigation", "Overlays", "Data", "Theme"];
        foreach (var dark in new[] { false, true })
        {
            if (s.Model.Dark != dark)
                s.Click(Role.Switch, "Dark mode");
            check(s.Tester.Context.Theme.Dark == dark, $"the showcase applies the {(dark ? "dark" : "light")} kit theme");
            foreach (var section in sections)
            {
                s.Open(section);
                var name = $"{section.ToLowerInvariant()}-{(dark ? "dark" : "light")}";
                s.Tester.Screenshot(Path.Combine(output, name + ".png"));
                File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
                s.Tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
                var warnings = s.Tester.Context.Warnings.Where(w => !(w.Code == "contrast" && InsideDisabled(s.Tester.Context, w.Node))).ToArray();
                check(warnings.Length == 0, $"{name} renders without layout, overlap or contrast warnings: " +
                    string.Join("; ", warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));
                var findings = s.Tester.Context.AuditAccessibility();
                File.WriteAllLines(Path.Combine(output, name + ".audit.txt"), findings.Select(f => $"{f.Code} {f.Path} {f.Message}"));
                check(findings.Count == 0, $"{name} passes the accessibility audit: " +
                    string.Join("; ", findings.Select(f => $"{f.Code} {f.Path}")));
            }
        }
        if (s.Model.Dark)
            s.Click(Role.Switch, "Dark mode");
        s.Tester.Context.Culture = System.Globalization.CultureInfo.GetCultureInfo("he-IL");
        foreach (var section in sections)
        {
            s.Open(section);
            var name = $"{section.ToLowerInvariant()}-rtl";
            s.Tester.Screenshot(Path.Combine(output, name + ".png"));
            File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
            s.Tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
            var warnings = s.Tester.Context.Warnings.Where(w => !(w.Code == "contrast" && InsideDisabled(s.Tester.Context, w.Node))).ToArray();
            check(warnings.Length == 0, $"{name} mirrors without layout, overlap or contrast warnings: " +
                string.Join("; ", warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));
        }
        s.Tester.Context.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        s.Open("Theme");
        s.Click(Role.Option, "Violet");
        check(s.Tester.Context.Theme.Accent != KitTheme.Dark.Primary.Background, "accent choices restyle every primary control");
        s.Click(Role.Option, "12");
        check(s.Tester.Context.Theme.Radius.Md == 12, "radius choices restyle every rounded control");
        var first = Hash(s.Tester);
        var second = Hash(s.Tester);
        check(first == second, "repeated renders of the same model are pixel identical");
        s.Tester.Screenshot(Path.Combine(output, "theme-custom.png"));
    }

    /// <summary>WCAG 1.4.3 exempts text in inactive controls, which disabled controls dim on purpose.</summary>
    static bool InsideDisabled(Context context, int node)
    {
        var nodes = context.Nodes;
        for (var i = node; i >= 0 && i < nodes.Length; i = nodes[i].Parent)
            if (nodes[i].Disabled)
                return true;
        return false;
    }

    static string Hash(Tester tester)
    {
        tester.Frame(0);
        var pixels = tester.Render();
        return Convert.ToHexString(SHA256.HashData(pixels.Bytes));
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
partial class ReportJson : System.Text.Json.Serialization.JsonSerializerContext;
