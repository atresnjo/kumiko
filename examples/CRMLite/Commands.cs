using Kumiko.Desktop;
using Kumiko.UI;

namespace CRMLite;

/// <summary>Stable identities shared by the typed actions and the native menu template.</summary>
public static class CrmCommands
{
    public const string Settings = "settings", NotificationSettings = "notification-settings";
    public const string NewCompany = "new-company", NewDeal = "new-deal", Save = "save", Refresh = "refresh", SignOut = "sign-out";
    public const string Find = "find", Sidebar = "sidebar", Dark = "dark", Back = "back";
    public const string GoInbox = "go-inbox", GoCompanies = "go-companies", GoPeople = "go-people", GoDeals = "go-deals";

    public static IReadOnlyList<string> All
    {
        get;
    } =
        [Settings, NotificationSettings, NewCompany, NewDeal, Save, Refresh, SignOut, Find, Sidebar, Dark, Back, GoInbox, GoCompanies, GoPeople, GoDeals];
}

public sealed partial class CrmApp
{
    /// <summary>
    /// Every CRM command as one typed action projected from the model. Shortcuts, the projected menu rows and the
    /// native menu all dispatch these messages; dialogs, settings, the search palette and confirmations disable them.
    /// </summary>
    public static IEnumerable<DesktopAction<CrmMsg>> Actions(CrmModel m)
    {
        var signedIn = m.Auth.Session is not null && m.Backend.Loaded && !m.Backend.Loading;
        var calm = m.Overlay is not (Overlay.NewDeal or Overlay.NewCompany or Overlay.Plan) && !m.Settings.Open && m.PendingNavigation is null;
        var free = signedIn && calm;
        var page = ScreenRoutes.Kind(m.Screen);
        yield return new(CrmCommands.Settings, "Settings…", new CrmMsg.OpenSettings(), Enabled: free, Scope: DesktopActionScope.App);
        yield return new(CrmCommands.NotificationSettings, "Notification Settings…", new CrmMsg.OpenSettings(SettingsSection.Notifications),
            Enabled: free, Scope: DesktopActionScope.App);
        yield return new(CrmCommands.NewCompany, "New Company…", new CrmMsg.OpenCompanyDialog(), Key.N, Enabled: free && m.CanEdit);
        yield return new(CrmCommands.NewDeal, "New Deal…", new CrmMsg.OpenDealDialog(m.CompanyId), Key.N, Modifiers.Primary | Modifiers.Shift,
            Enabled: free && m.CanEdit);
        yield return new(CrmCommands.Save, "Save Changes", new CrmMsg.Authentication(new AuthMsg.RetrySave()), Key.S,
            Enabled: m.CanEdit && m.Backend.Dirty && !m.Backend.Saving);
        yield return new(CrmCommands.Refresh, "Refresh Data", new CrmMsg.Authentication(new AuthMsg.RefreshWorkspace()), Key.R,
            Enabled: m.Auth.Session is not null && !m.Backend.Loading && !m.Backend.Dirty && calm);
        yield return new(CrmCommands.SignOut, "Sign Out", new CrmMsg.Authentication(new AuthMsg.SignOut()),
            Enabled: m.Auth.Session is not null && !m.Auth.Busy && calm, Scope: DesktopActionScope.App);
        yield return new(CrmCommands.Find, "Search…", new CrmMsg.SetPopup(Popup.Search), Key.F, Enabled: free);
        yield return new(CrmCommands.Sidebar, m.SidebarCollapsed ? "Show Sidebar" : "Hide Sidebar", new CrmMsg.ToggleSidebar(), Key.Backslash,
            Enabled: free);
        yield return new(CrmCommands.Dark, "Dark Appearance", new CrmMsg.SetDark(!m.Dark), Enabled: signedIn, Checked: m.Dark,
            Scope: DesktopActionScope.App);
        // Back keeps its platform chord (Primary+[ or Alt+Left) and mouse buttons through BackShortcut.
        var entries = m.Navigation.Entries;
        var previous = entries.Length > 1 ? entries[^2] : entries[^1];
        yield return new(CrmCommands.Back, "Back", new CrmMsg.Back(m.BackTo(previous.Id)), Enabled: free && entries.Length > 1);
        yield return Go(CrmCommands.GoInbox, "Inbox", ScreenKind.Inbox, Key.D1);
        yield return Go(CrmCommands.GoCompanies, "Companies", ScreenKind.Companies, Key.D2);
        yield return Go(CrmCommands.GoPeople, "People", ScreenKind.People, Key.D3);
        yield return Go(CrmCommands.GoDeals, "Deals", ScreenKind.Deals, Key.D4);

        DesktopAction<CrmMsg> Go(string id, string label, ScreenKind kind, Key key) =>
            new(id, label, new CrmMsg.Navigate(kind), key, Enabled: free, Checked: signedIn && page == kind);
    }
}

/// <summary>The native application menu: standard roles plus placeholders bound to the typed actions by identity.</summary>
public static class CrmMenu
{
    static MenuItem Action(string id, string label) => new(label, Id: id, Role: MenuRole.DesktopAction);

    static MenuItem Separator => new(Separator: true);

    public static MenuItem[] Template(string name)
    {
        MenuItem[] settings = [Action(CrmCommands.Settings, "Settings…"), Action(CrmCommands.NotificationSettings, "Notification Settings…")];
        MenuItem[] file = [Action(CrmCommands.NewCompany, "New Company…"), Action(CrmCommands.NewDeal, "New Deal…"), Separator,
            Action(CrmCommands.Save, "Save Changes"), Action(CrmCommands.Refresh, "Refresh Data"), Separator,
            new("Close Window", "Primary+W", Role: MenuRole.Close), Separator, Action(CrmCommands.SignOut, "Sign Out")];
        MenuItem[] edit = [new("Undo", "Primary+Z", Role: MenuRole.Undo), new("Redo", "Primary+Shift+Z", Role: MenuRole.Redo), Separator,
            new("Cut", "Primary+X", Role: MenuRole.Cut), new("Copy", "Primary+C", Role: MenuRole.Copy), new("Paste", "Primary+V", Role: MenuRole.Paste),
            new("Select All", "Primary+A", Role: MenuRole.SelectAll), Separator, Action(CrmCommands.Find, "Search…")];
        MenuItem[] view = [Action(CrmCommands.Sidebar, "Hide Sidebar"), Action(CrmCommands.Dark, "Dark Appearance"), Separator,
            new("Toggle Full Screen", Role: MenuRole.ToggleFullScreen)];
        MenuItem[] go = [Action(CrmCommands.GoInbox, "Inbox"), Action(CrmCommands.GoCompanies, "Companies"), Action(CrmCommands.GoPeople, "People"),
            Action(CrmCommands.GoDeals, "Deals"), Separator, Action(CrmCommands.Back, "Back")];
        // Other platforms drop the App menu; settings and quitting move into File.
        if (!OperatingSystem.IsMacOS())
            file = [.. file, Separator, .. settings, Separator, new("Exit", Role: MenuRole.Quit)];
        return
        [
            new(Role: MenuRole.AppMenu, Submenu: [new("About " + name, Role: MenuRole.About), Separator, .. settings, Separator,
                new("Hide " + name, "Primary+H", Role: MenuRole.Hide), new("Hide Others", "Primary+Alt+H", Role: MenuRole.HideOthers),
                new("Show All", Role: MenuRole.Unhide), Separator, new("Quit " + name, "Primary+Q", Role: MenuRole.Quit)]),
            new(Role: MenuRole.FileMenu, Submenu: file),
            new(Role: MenuRole.EditMenu, Submenu: edit),
            new(Role: MenuRole.ViewMenu, Submenu: view),
            new("Go", Submenu: go),
            new(Role: MenuRole.WindowMenu),
            new(Role: MenuRole.HelpMenu)
        ];
    }
}
