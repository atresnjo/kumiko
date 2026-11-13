using CRMLite;
using Kumiko.UI;

/// <summary>The settings sheet: one scrolling page, a nav that follows it, eased jumps, live preferences.</summary>
static class SettingsFlows
{
    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        s.Click(Role.Button, "Account");
        check(s.Model.Settings.Open, "the avatar in the sidebar opens settings");
        var sheet = s.Node(Role.Group, "Settings").Rect;
        var window = s.Tester.Context.Size;
        check(MathF.Abs(sheet.X + sheet.Width / 2 - window.Width / 2) < 1 && MathF.Abs(sheet.Y + sheet.Height / 2 - window.Height / 2) < 1,
            "settings open as a centered sheet");
        check(s.Node(Role.Button, "General").Properties?.Selected == true, "the nav starts on General");
        foreach (var section in new[] { "General", "Appearance", "Notifications", "Security", "Members", "Billing" })
            check(s.Tester.Context.Accessibility.Any(n => n is { Role: Role.Heading } && n.Name == section), $"{section} is a section of the one page");

        float Top(string section) => s.Tester.Context.Accessibility.Single(n => n.Role == Role.Heading && n.Name == section).Rect.Y;
        var viewport = s.Node(Role.ScrollArea, "Settings content").Rect;
        s.Click(Role.Button, "Security");
        check(Top("Security") - viewport.Y is >= 0 and < 48, $"the nav scrolls its section to the top ({Top("Security") - viewport.Y:0} px)");
        check(s.Node(Role.Button, "Security").Properties?.Selected == true && s.Model.Settings.Section == SettingsSection.Security,
            "the nav marks the section it scrolled to");

        s.Tester.MoveTo(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        s.Tester.Scroll(-5000);
        s.Tester.Settle();
        check(s.Model.Settings.Section == SettingsSection.General, "scrolling back to the top marks General");
        s.Tester.Scroll(5000);
        s.Tester.Settle();
        check(s.Model.Settings.Section == SettingsSection.Billing, "scrolling to the end marks the last section");
        s.Tester.Scroll(-5000);
        s.Tester.Settle();

        s.Click(Role.Button, "Notifications");
        s.Click(Role.Switch, "Weekly digest");
        check(s.Model.Preferences.WeeklyDigest && s.Node(Role.Switch, "Weekly digest").Checked, "switches change preferences");
        s.Click(Role.ComboBox, "Email delivery");
        s.Click(Role.MenuItem, "Daily digest");
        check(s.Model.Preferences.Delivery == "Daily digest" && s.Model.Settings.Menu is null, "selects choose a value and close");
        s.Click(Role.Button, "Security");
        s.Click(Role.ComboBox, "Session length");
        s.Key(Key.Escape);
        check(s.Model.Settings.Open && s.Model.Settings.Menu is null, "Escape closes an open select before the sheet");

        s.Click(Role.Button, "General");
        s.Click(Role.TextField, "Workspace name");
        s.Tester.Key(Key.A, Modifiers.Primary);
        s.Tester.TypeText("Harbor Labs");
        s.Tester.Settle();
        check(s.Model.Preferences.Workspace == "Harbor Labs" && s.Tester.HasText("Harbor Labs"), "renaming the workspace is live");
        s.Click(Role.Button, "Appearance");
        s.Click(Role.Option, "Violet");
        var accent = s.Tester.Context.Theme.Accent;
        check(s.Model.Preferences.Accent == Accent.Violet && accent == new Palette(false, Accent.Violet).Kit.Theme.Accent,
            "accent swatches recolor the app");
        s.Click(Role.Option, "Dark");
        check(s.Model.Dark && s.Tester.Context.Theme.Dark, "the theme switch turns on dark mode");
        s.Click(Role.Option, "Light");

        s.Click(Role.TextField, "Search settings");
        s.Tester.TypeText("saml");
        s.Tester.Settle();
        check(s.Exists(Role.Switch, "Enforce SAML single sign-on") && !s.Exists(Role.Switch, "Mentions") && !s.Tester.HasText("Billing email"),
            "search narrows the page to matching settings");
        s.Tester.Key(Key.A, Modifiers.Primary);
        s.Tester.TypeText("zzz");
        s.Tester.Settle();
        check(s.Tester.ContainsText("No settings match"), "an unmatched search explains itself");
        s.Tester.Key(Key.A, Modifiers.Primary);
        s.Tester.Key(Key.Backspace);
        s.Tester.Settle();
        Capture(s, output, "settings", check);

        s.Tester.Context.ReduceMotion = false;
        var start = Top("Members");
        s.Click(Role.Button, "Members");
        var path = new List<float>();
        for (var i = 0; i < 40; i++)
        {
            s.Tester.Frame(1 / 60f);
            path.Add(Top("Members"));
        }
        check(path[0] < start && path[0] > path[^1], $"a jump glides instead of cutting ({start:0} → {path[0]:0} → {path[^1]:0})");
        check(path.Zip(path.Skip(1)).All(p => p.Second <= p.First + .01f), "the glide never reverses");
        var steps = path.Zip(path.Skip(1)).Select(p => p.First - p.Second).ToArray();
        check(steps[0] < steps.Max() && steps[^1] < steps.Max(), "the glide eases in and out");
        check(Top("Members") - viewport.Y is >= 0 and < 48 && s.Node(Role.Button, "Members").Properties?.Selected == true,
            "the glide lands on its section and the nav follows");
        s.Tester.Context.ReduceMotion = true;

        s.Key(Key.Escape);
        check(!s.Model.Settings.Open, "Escape closes settings");
        s.Click(Role.Button, "Account");
        check(s.Node(Role.Button, "General").Properties?.Selected == true, "settings reopen at the top");
        s.Click(Role.Option, "Dark");
        Capture(s, output, "settings-dark", check);
        s.Click(Role.Option, "Light");
        s.Click(Role.Button, "Close settings");
        check(!s.Model.Settings.Open, "the close button closes settings");
    }

    static void Capture(CrmSession s, string output, string name, Action<bool, string> check)
    {
        s.Tester.Settle();
        s.Tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
        var warnings = s.Tester.Context.Warnings;
        check(warnings.Count == 0, $"{name} renders without warnings: " + string.Join("; ", warnings.Select(w => $"{w.Code} {w.Path} {w.Message}")));
    }
}
