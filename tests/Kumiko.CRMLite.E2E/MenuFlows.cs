using CRMLite;
using Kumiko.UI;

/// <summary>Menu highlight: one row at a time, following the pointer or the keyboard, never both.</summary>
static class MenuFlows
{
    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        RowMenu(s, output, check);
        s.Click(Role.Button, "Deals");
        s.Click(Role.Button, "New deal");
        s.Click(Role.ComboBox, "Owner");
        check(Focused(s) == "Maya Brooks" && Highlighted(s).Length == 0,
            "a menu opened by the pointer focuses its first option without highlighting it");
        MoveTo(s, "Leo Marchetti");
        check(Highlighted(s) is ["Leo Marchetti"], "the hovered option is the only highlighted option");
        MoveTo(s, "Sara Lindqvist");
        check(Highlighted(s) is ["Sara Lindqvist"], "the highlight follows the pointer between options");
        s.Key(Key.Down);
        check(Highlighted(s) is ["Leo Marchetti"] && Focused(s) == "Leo Marchetti",
            "Down continues from the hovered option and the resting pointer keeps no highlight");
        s.Tester.Frame();
        check(Highlighted(s) is ["Leo Marchetti"], "a frame without pointer movement keeps the keyboard highlight");
        MoveTo(s, "Daniel Okafor");
        check(Highlighted(s) is ["Daniel Okafor"], "moving the pointer takes the highlight back from the keyboard");
        var panel = s.Tester.Context.Accessibility.First(n => n.Role == Role.Group && n.Name == "Owner").Rect;
        s.Tester.MoveTo(panel.X + panel.Width / 2, panel.Bottom + 40);
        s.Tester.Settle();
        check(Highlighted(s).Length == 0, "moving the pointer off the menu clears a pointer highlight");
        s.Key(Key.Enter);
        check(s.Model.DealDraft?.OwnerId == "daniel" && s.Model.Popup == Popup.None, "Enter chooses the focused option");

        for (var i = 0; i < 12 && Focused(s) != "Owner"; i++)
            s.Key(Key.Tab);
        s.Key(Key.Enter);
        check(Highlighted(s) is ["Maya Brooks"],
            "a menu opened by the keyboard highlights its focused option");
    }

    /// <summary>Right-clicking a company row opens its actions at the pointer without opening the company.</summary>
    static void RowMenu(CrmSession s, string output, Action<bool, string> check)
    {
        var row = s.Node(Role.Row, "Glacier Cloud").Rect;
        var (x, y) = (row.X + 300, row.Y + row.Height / 2);
        s.Tester.RightClick(x, y);
        s.Tester.Settle();
        var menu = s.Tester.Context.Accessibility.FirstOrDefault(n => n.Role == Role.Group && n.Name == "Glacier Cloud actions");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && menu is not null, "right-clicking a row opens its actions instead of the company");
        check(menu is { } m && MathF.Abs(m.Rect.X - x) < 12 && m.Rect.Y >= y && m.Rect.Y < y + 12, "the row menu opens at the pointer");
        check(Highlighted(s).Length == 0 && s.Exists(Role.MenuItem, "Mark as hot account") && s.Exists(Role.MenuItem, "Add to favorites"),
            "the row menu offers the company's actions");
        Capture(s, output, "row-menu", check);
        s.Click(Role.MenuItem, "Add to favorites");
        check(s.Model.Favorites.Contains("glacier") && s.Model.Overlay is Overlay.None, "a row action applies and closes the menu");

        s.Tester.RightClick(x, y);
        s.Tester.Settle();
        s.Click(Role.MenuItem, "Mark as hot account");
        check(s.Model.Companies.Single(c => c.Id == "glacier").Hot && s.Model.Overlay is Overlay.None, "marking a hot account closes the menu");
        s.Tester.RightClick(x, y);
        s.Tester.Settle();
        check(s.Exists(Role.MenuItem, "Remove from hot accounts") && s.Exists(Role.MenuItem, "Remove from favorites"),
            "the row menu reflects the company's state");
        s.Key(Key.Escape);
        check(s.Model.Overlay is Overlay.None && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies, "Escape closes the row menu");

        s.Tester.RightClick(x, y);
        s.Tester.Settle();
        s.Click(Role.MenuItem, "New deal…");
        check(s.Model.DealDraft?.CompanyId == "glacier", "New deal from a row preselects its company");
        s.Key(Key.Escape);
        s.Tester.RightClick(x, y);
        s.Tester.Settle();
        s.Click(Role.MenuItem, "Open company");
        check(s.Model.CompanyId == "glacier", "Open company opens the right-clicked company");
        s.Click(Role.Button, "Back to companies");
    }

    static void Capture(CrmSession s, string output, string name, Action<bool, string> check)
    {
        s.Tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
        check(s.Tester.Context.Warnings.Count == 0, $"{name} renders without layout, overlap or contrast warnings");
    }

    static string? Focused(CrmSession s) => s.Tester.Context.Accessibility.FirstOrDefault(n => n.Focused)?.Name;

    static void MoveTo(CrmSession s, string option)
    {
        var rect = s.Node(Role.MenuItem, option).Rect;
        s.Tester.MoveTo(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        s.Tester.Settle();
    }

    /// <summary>Menu options painted with a background fill spanning their whole row.</summary>
    static string[] Highlighted(CrmSession s)
    {
        var instances = s.Tester.Context.Instances;
        return [.. s.All(Role.MenuItem)
            .Where(n => instances.Any(i => i.Color.W > 0 && Near(i.Rect.X, n.Rect.X) && Near(i.Rect.Y, n.Rect.Y) && Near(i.Rect.Z, n.Rect.Width) &&
                Near(i.Rect.W, n.Rect.Height)))
            .Select(n => n.Name ?? "")];
    }

    static bool Near(float a, float b) => MathF.Abs(a - b) < .5f;
}
