namespace Kumiko.Desktop;

public sealed partial class Menu
{
    nint installedWindowsMenu, installedHelpMenu;
    nint StandardMenu(MenuRole role) => nativeDefinitions.FirstOrDefault(pair => pair.Value.Role == role) is var entry && entry.Key != 0
        ? Platform.ObjC.O(entry.Key, "submenu") : 0;

    void InstallStandardMenus()
    {
        RemoveStandardMenus();
        var nativeApp = Platform.ObjC.O(Platform.ObjC.Class("NSApplication"), "sharedApplication");
        installedWindowsMenu = Platform.ObjC.O(StandardMenu(MenuRole.WindowMenu), "retain");
        installedHelpMenu = Platform.ObjC.O(StandardMenu(MenuRole.HelpMenu), "retain");
        Platform.ObjC.V(nativeApp, "setWindowsMenu:", installedWindowsMenu);
        Platform.ObjC.V(nativeApp, "setHelpMenu:", installedHelpMenu);
    }

    void RemoveStandardMenus()
    {
        var nativeApp = Platform.ObjC.O(Platform.ObjC.Class("NSApplication"), "sharedApplication");
        if (installedWindowsMenu is var windows && windows != 0 && Platform.ObjC.O(nativeApp, "windowsMenu") == windows)
        {
            Platform.ObjC.V(nativeApp, "setWindowsMenu:", (nint)0);
            if (Platform.ObjC.O(nativeApp, "windowsMenu") == windows)
            {
                // AppKit can retain the retired Window menu. Leave it empty,
                // with no callbacks into this owner, before releasing our retain.
                Platform.ObjC.V(windows, "removeAllItems");
            }
        }
        if (installedHelpMenu is var help && help != 0 && Platform.ObjC.O(nativeApp, "helpMenu") == help)
            Platform.ObjC.V(nativeApp, "setHelpMenu:", (nint)0);
        Platform.ObjC.Release(installedWindowsMenu);
        Platform.ObjC.Release(installedHelpMenu);
        installedWindowsMenu = installedHelpMenu = 0;
    }

    readonly HashSet<MenuItem> generatedRecent = new(ReferenceEqualityComparer.Instance);

    // Windows keeps an app-owned recent history; Linux has no system history behind OpenRecent yet.
    MenuItem[] Expand(MenuItem[] entries) => entries
        .Where(item => OperatingSystem.IsMacOS() || item.Role is not (MenuRole.About or MenuRole.Hide or MenuRole.HideOthers or MenuRole.Unhide or MenuRole.Front or MenuRole.AppMenu or MenuRole.OpenRecent) ||
            item.Role == MenuRole.OpenRecent && OperatingSystem.IsWindows())
        .Select(ExpandItem).ToArray();

    readonly Dictionary<MenuItem, string> placeholders = new(ReferenceEqualityComparer.Instance);
    internal bool HasDesktopActions => placeholders.Count > 0;

    MenuItem ExpandItem(MenuItem item)
    {
        if (item.Role == MenuRole.DesktopAction)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Desktop action placeholders need an action ID and a fallback label.", nameof(item));
            // Keyboard routing stays in the window after editors and overlays; the action supplies the callback.
            if (item.Accelerator.Length > 0 || item.Click is not null || item.Submenu is not null)
                throw new ArgumentException("Desktop action placeholders cannot carry an accelerator, callback or submenu.", nameof(item));
            placeholders.TryAdd(item, item.Label);
            return item;
        }
        string fallback = item.Role switch
        {
            MenuRole.AppMenu => app.Options.Name,
            MenuRole.FileMenu => "File",
            MenuRole.EditMenu => "Edit",
            MenuRole.ViewMenu => "View",
            MenuRole.WindowMenu => "Window",
            MenuRole.HelpMenu => "Help",
            MenuRole.OpenRecent => "Open Recent",
            _ => ""
        };
        if (item.Label.Length == 0 && fallback.Length > 0)
            item.SetLabel(fallback);
        // Authored submenus take precedence over generated role defaults.
        // Mutate the logical item once; never clone a caller's retained handle.
        var children = item.Submenu;
        if (children is null)
        {
            children = item.Role switch
            {
                MenuRole.AppMenu => [new("About " + app.Options.Name, Role: MenuRole.About), new(Separator: true), new("Hide " + app.Options.Name, "Primary+H", Role: MenuRole.Hide), new("Hide Others", "Primary+Alt+H", Role: MenuRole.HideOthers), new("Show All", Role: MenuRole.Unhide), new(Separator: true), new("Quit " + app.Options.Name, "Primary+Q", Role: MenuRole.Quit)],
                MenuRole.FileMenu => [new(Role: MenuRole.OpenRecent), new("Close", "Primary+W", Role: MenuRole.Close), new("Quit", Role: MenuRole.Quit)],
                MenuRole.EditMenu => [new("Undo", "Primary+Z", Role: MenuRole.Undo), new("Redo", "Primary+Shift+Z", Role: MenuRole.Redo), new(Separator: true), new("Cut", "Primary+X", Role: MenuRole.Cut), new("Copy", "Primary+C", Role: MenuRole.Copy), new("Paste", "Primary+V", Role: MenuRole.Paste), new("Select All", "Primary+A", Role: MenuRole.SelectAll)],
                MenuRole.ViewMenu => [new("Full Screen", Role: MenuRole.ToggleFullScreen)],
                MenuRole.WindowMenu => [new("Minimize", "Primary+M", Role: MenuRole.Minimize), new("Zoom", Role: MenuRole.Zoom), new("Bring All to Front", Role: MenuRole.Front)],
                MenuRole.HelpMenu => [],
                MenuRole.OpenRecent => RecentItems(),
                _ => null
            };
            if (item.Role == MenuRole.OpenRecent)
                generatedRecent.Add(item);
        }
        if (children is not null)
            item.SetChildren(Expand(children));
        return item;
    }

    MenuItem[] RecentItems()
    {
        var entries = app.RecentDocuments.Select(path => new MenuItem(Path.GetFileName(path), Click: _ => app.DeliverOpen(path, false))).ToList();
        if (entries.Count > 0)
            entries.Add(new(Separator: true));
        entries.Add(new("Clear Menu", Click: _ => app.ClearRecentDocuments())
        {
            Disabled = entries.Count == 0
        });
        return entries.ToArray();
    }

    /// <summary>Copies the current projection's label and state into each placeholder; refreshes natives only on change.</summary>
    internal void BindDesktopActions(Func<string, MenuItem?> resolve)
    {
        if (disposed)
            return;
        bool changed = false;
        foreach (var (item, authored) in placeholders)
        {
            var action = resolve(item.Id);
            changed |= item.Bind(action?.Label ?? authored, action is null || action.Disabled, action?.Checked ?? false);
        }
        if (changed)
            Refresh();
    }

    void RecentChanged()
    {
        bool changed = false;
        void Visit(MenuItem[] entries)
        {
            foreach (var item in entries)
            {
                if (generatedRecent.Contains(item))
                {
                    RetireWindowsCommands(item.Submenu);
                    item.SetChildren(RecentItems());
                    changed = true;
                }
                else if (item.Submenu is { } children)
                    Visit(children);
            }
        }
        Unsubscribe();
        Visit(items);
        Subscribe();
        if (changed)
            Rebuild();
    }
}
