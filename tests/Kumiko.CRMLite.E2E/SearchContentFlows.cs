using Kumiko.UI;

/// <summary>Repeatable evidence for the palette's custom command content.</summary>
static class SearchContentFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Key(Key.F, Modifiers.Primary);
        Save(s, output, "search-empty");
        check(s.Node(Role.MenuItem, "Inbox").Value == "Unread threads from your accounts.",
            "search results preserve their accessible description");
        var target = s.Node(Role.MenuItem, "Inbox").Rect;
        var label = s.Tester.Get(Role.Text, "Unread threads from your accounts.").Rect;
        check(label.X >= target.X && label.Right <= target.Right, "search result descriptions render inside their menu hit targets");
        s.Tester.MoveTo(target.X + target.Width / 2, target.Y + target.Height / 2);
        s.Tester.Settle();
        check(s.Tester.Context.Cursor == Cursor.Hand, "CRM search results keep their hand cursor on hover");
        Save(s, output, "search-hover");
        s.Tester.MoveTo(0, 0);
        s.Tester.Get(Role.TextField, "Search companies, people and pages").Click();
        s.Tester.TypeText("finance");
        s.Tester.Settle();
        Save(s, output, "search-finance");
        check(s.Exists(Role.MenuItem, "Ruth Adeyemi"), "the search renderer retains contact descriptions and filtering");
        s.Key(Key.Down);
        s.Key(Key.Down);
        s.Key(Key.Enter);
        check(s.Model.Popup == CRMLite.Popup.None, "custom search rows navigate and select through Kit");
    }

    static void Save(CrmSession s, string output, string name)
    {
        s.Tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
        s.Tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
    }
}
