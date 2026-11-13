using System.Runtime.InteropServices;
using System.Text.Json;
using CRMLite;
using Kumiko;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;
using MenuItem = Kumiko.Desktop.MenuItem;

/// <summary>
/// One typed action set drives keyboard shortcuts, the projected menu rows and the native application menu, under
/// the CRM's dialog, settings, palette and editor precedence.
/// </summary>
static partial class DesktopMenuFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        Shortcuts(output, check);
        Native(output, check);
    }

    static int Count<T>(CrmSession s) => s.Program.History.Count(step => step.Message is T);

    static void Shortcuts(string output, Action<bool, string> check)
    {
        using var s = DesktopFlows.Session(new RecordingDesktop());
        MenuItem Item(string id) => s.Tester.DesktopMenu.Single(item => item.Id == id);
        check(CrmCommands.All.All(id => s.Tester.DesktopMenu.Any(item => item.Id == id)), "every CRM command is projected as a typed action");
        check(!Item(CrmCommands.NewCompany).Disabled && Item(CrmCommands.Save).Disabled && Item(CrmCommands.Back).Disabled,
            "enabled state follows the model: nothing to save and no visit to go back to");
        var retained = Item(CrmCommands.NewCompany);
        s.Key(Key.N, Modifiers.Primary);
        check(s.Model.Overlay is Overlay.NewCompany && Count<CrmMsg.OpenCompanyDialog>(s) == 1, "Primary+N opens New company once");
        s.Key(Key.N, Modifiers.Primary);
        s.Key(Key.D4, Modifiers.Primary);
        check(Count<CrmMsg.OpenCompanyDialog>(s) == 1 && s.Model.Screen is Screen.Companies && Item(CrmCommands.NewDeal).Disabled &&
            Item(CrmCommands.GoDeals).Disabled, "an open dialog owns the keyboard and disables menu commands");
        retained.Click!(retained);
        s.Tester.Settle();
        check(Count<CrmMsg.OpenCompanyDialog>(s) == 1, "a menu row retained from an earlier frame cannot dispatch");
        s.Type(Role.TextField, "Company name", "Acme");
        s.Key(Key.A, Modifiers.Primary);
        s.Tester.TypeText("Beta");
        s.Tester.Settle();
        check(s.Model.CompanyDraft?.Name == "Beta", "Primary+A stays an editor chord inside the dialog");
        s.Key(Key.Escape);
        check(s.Model.Overlay is Overlay.None, "Escape still closes the dialog");

        s.Key(Key.D4, Modifiers.Primary);
        check(s.Model.Screen is Screen.Deals && Item(CrmCommands.GoDeals).Checked && !Item(CrmCommands.GoCompanies).Checked,
            "Primary+4 shows Deals and the Go menu checks it");
        check(Item(CrmCommands.GoDeals).Label == "Deals   " + Kit.ShortcutLabel("4") &&
            Item(CrmCommands.Sidebar).Label.EndsWith(Kit.ShortcutLabel("\\"), StringComparison.Ordinal),
            "menu rows show digit and backslash shortcuts as their key characters");
        var people = Item(CrmCommands.GoPeople);
        people.Click!(people);
        s.Tester.Settle();
        check(s.Model.Screen is Screen.People && s.Program.History[^1].Message is CrmMsg.Navigate { Page: ScreenKind.People },
            "the menu row dispatches the same typed Navigate message as its shortcut");

        s.Key(Key.F, Modifiers.Primary);
        check(s.Model.Popup == Popup.Search, "Primary+F opens search through its action");
        s.Key(Key.N, Modifiers.Primary);
        check(s.Model.Overlay is Overlay.Menu && Count<CrmMsg.OpenCompanyDialog>(s) == 1, "the search palette blocks other shortcuts");
        s.Key(Key.F, Modifiers.Primary);
        check(s.Model.Popup == Popup.None, "Primary+F inside the palette closes it");

        var settings = Item(CrmCommands.Settings);
        settings.Click!(settings);
        s.Tester.Settle();
        s.Key(Key.D2, Modifiers.Primary);
        check(s.Model.Settings.Open && s.Model.Screen is Screen.People && Item(CrmCommands.NewCompany).Disabled,
            "open settings block navigation shortcuts and creation commands");
        s.Key(Key.Escape);

        s.Key(Key.D2, Modifiers.Primary);
        s.Click(Role.Row, "Northwind Labs");
        check(!Item(CrmCommands.Back).Disabled, "Back enables once there is a visit to return to");
        s.Click(Role.TextField, "Add a note");
        s.Tester.Compose("未", false);
        s.Key(Key.D4, Modifiers.Primary);
        check(s.Model.CompanyId == "northwind", "an uncommitted IME composition keeps navigation shortcuts out");
        s.Tester.Compose("", true);
        s.Tester.Settle();
        var back = Item(CrmCommands.Back);
        back.Click!(back);
        s.Tester.Settle();
        check(s.Model.Screen is Screen.Companies, "the Back menu command uses the guarded Back message");

        s.Key(Key.Backslash, Modifiers.Primary);
        check(s.Model.SidebarCollapsed && Item(CrmCommands.Sidebar).Label.StartsWith("Show Sidebar", StringComparison.Ordinal),
            "Primary+Backslash collapses the sidebar and relabels its command");
        s.Key(Key.Backslash, Modifiers.Primary);
        var dark = Item(CrmCommands.Dark);
        dark.Click!(dark);
        s.Tester.Settle();
        check(s.Model.Dark && Item(CrmCommands.Dark).Checked, "Dark Appearance is a checked command");
        AuthFlows.Capture(s, output, "desktop-menu-dark");
        File.WriteAllText(Path.Combine(output, "desktop-menu.json"), JsonSerializer.Serialize(
            s.Tester.DesktopMenu.Select(item => $"{item.Id}: {item.Label} disabled={item.Disabled} checked={item.Checked}").ToList(),
            DesktopJson.Default.ListString));

        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.SignOut()));
        s.Idle();
        check(s.Tester.DesktopMenu.All(item => item.Disabled), "signed out, every CRM command is disabled but still listed");
    }

    static void Native(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var desktop = new RecordingDesktop();
        var seed = Data.Initial() with
        {
            LiveAgent = false
        };
        var backend = new FakeCrmBackend(TimeSpan.Zero, seed);
        using var app = new App(new("CRMLite", Hidden: true, QuitOnLastWindow: false));
        using var program = CrmApp.CreateProgram(seed, backend.Services(desktop));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        app.SetMenu(CrmMenu.Template(app.Options.Name));
        app.NewWindow(new(Title: "CRMLite", Width: 1280, Height: 820, Visible: false), program);
        app.RunFor(TimeSpan.FromMilliseconds(150));
        var main = Get(Get(Class("NSApplication"), "sharedApplication"), "mainMenu");
        var evidence = new List<string>();
        var titles = Enumerable.Range(0, (int)Get(main, "numberOfItems")).Select(i => Title(Item(main, i))).ToArray();
        check(titles.SequenceEqual(["CRMLite", "File", "Edit", "View", "Go", "Window", "Help"]),
            "the native menu bar has App, File, Edit, View, Go, Window and Help menus: " + string.Join(", ", titles));
        nint Find(string menu, string prefix, out nint index)
        {
            var submenu = Get(Item(main, Array.IndexOf(titles, menu)), "submenu");
            for (index = 0; index < Get(submenu, "numberOfItems"); index++)
                if (Title(Item(submenu, index)).StartsWith(prefix, StringComparison.Ordinal))
                    return submenu;
            throw new InvalidOperationException($"No {prefix} item in {menu}.");
        }
        foreach (var (menu, label) in new[] { ("File", "New Company…"), ("File", "Save Changes"), ("Go", "Deals"), ("Edit", "Search…") })
        {
            var submenu = Find(menu, label, out var index);
            var item = Item(submenu, index);
            evidence.Add($"{menu} > {Title(item)}: key={Text(Get(item, "keyEquivalent"))} enabled={Get(item, "isEnabled")}");
            check(Text(Get(item, "keyEquivalent")).Length == 0, label + " installs no native key equivalent; Kumiko routes its shortcut");
        }
        var quit = Find("CRMLite", "Quit", out var quitIndex);
        check(Text(Get(Item(quit, quitIndex), "keyEquivalent")) == "q", "standard roles keep their native key equivalents");
        var file = Find("File", "New Company…", out var create);
        var before = program.History.Count(step => step.Message is CrmMsg.OpenCompanyDialog);
        SendIndex(file, Selector("performActionForItemAtIndex:"), create);
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(program.Model.Overlay is Overlay.NewCompany && program.History.Count(step => step.Message is CrmMsg.OpenCompanyDialog) == before + 1,
            "AppKit activation of File > New Company opens the dialog once");
        SendIndex(file, Selector("performActionForItemAtIndex:"), create);
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(program.History.Count(step => step.Message is CrmMsg.OpenCompanyDialog) == before + 1 && app.Menu!.Find(CrmCommands.NewCompany)!.Disabled,
            "with the dialog open the native item is disabled and a forced activation does nothing");
        var frames = app.Windows[0].Context!.FrameId;
        app.RunFor(TimeSpan.FromMilliseconds(120));
        check(Get(Get(Class("NSApplication"), "sharedApplication"), "mainMenu") == main && app.Windows[0].Context!.FrameId == frames,
            "the installed menu is kept and an idle window does not redraw");
        check(app.Windows.All(window => !window.IsVisible), "native menu checks keep the CRM window hidden");
        using (var shell = new NativeCrmDesktop(app))
        {
            var signals = new List<DesktopSignal>();
            using var listening = shell.Listen(signals.Add);
            var forward = shell.BringForward(CancellationToken.None);
            var post = shell.Post(new("plan:x", "Title", "Body"), CancellationToken.None);
            app.RunFor(TimeSpan.FromMilliseconds(40));
            check(signals is [DesktopSignal.Access { Value: NoticeAccess.Unavailable }] && forward.IsCompletedSuccessfully &&
                post.Exception?.InnerException is PlatformNotSupportedException && app.Windows.All(window => !window.IsVisible),
                "the native shell in a hidden app reports notifications unavailable, refuses to post and never activates");
        }
        app.Windows[0].Close();
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 0 && app.Menu!.Find(CrmCommands.NewCompany)!.Disabled && app.Menu.Find(CrmCommands.GoDeals)!.Disabled,
            "closing the CRM window disables every placeholder");
        File.WriteAllText(Path.Combine(output, "desktop-native-menu.json"), JsonSerializer.Serialize(evidence, DesktopJson.Default.ListString));
        File.WriteAllLines(Path.Combine(output, "desktop-native-errors.txt"), errors.Select(e => e.ToString()));
        check(errors.Count == 0 && desktop.Posts.Count == 0, "the hidden native host raises no callback errors and posts nothing");
    }

    const string ObjC = "/usr/lib/libobjc.A.dylib";
    static nint Get(nint receiver, string name) => Send(receiver, Selector(name));
    static nint Item(nint menu, nint index) => SendIndex(menu, Selector("itemAtIndex:"), index);
    static string Title(nint item) => Text(Get(item, "title"));
    static string Text(nint value) => Marshal.PtrToStringUTF8(Get(value, "UTF8String")) ?? "";
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Class(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendIndex(nint receiver, nint selector, nint index);
}
