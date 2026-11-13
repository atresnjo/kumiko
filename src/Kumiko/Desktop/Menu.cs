using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

public enum MenuRole
{
    None,
    About,
    Hide,
    HideOthers,
    Unhide,
    Quit,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    Delete,
    SelectAll,
    Minimize,
    Zoom,
    Close,
    ToggleFullScreen,
    Front,
    OpenRecent,
    FileMenu,
    EditMenu,
    ViewMenu,
    WindowMenu,
    HelpMenu,
    AppMenu,
    /// <summary>Template placeholder for the desktop action whose ID matches <see cref="MenuItem.Id"/>. The single
    /// window projecting that ID supplies label, enabled and checked state; activation invokes its current projection.
    /// Placeholders need a fallback label and carry no accelerator, callback or submenu.</summary>
    DesktopAction
}

public sealed record MenuItem(string Label = "", string Accelerator = "", Action<MenuItem>? Click = null, MenuItem[]? Submenu = null, string Id = "", MenuRole Role = MenuRole.None, bool Separator = false)
{
    string label = Label, accelerator = Accelerator;
    MenuItem[]? submenu = Submenu;
    public MenuItem[]? Submenu
    {
        get => submenu; init => submenu = value;
    }

    internal void SetChildren(MenuItem[] children) => submenu = children;
    public string Label
    {
        get => label; init => label = value;
    }
    public string Accelerator
    {
        get => accelerator; init => accelerator = value;
    }
    public bool Radio
    {
        get; init;
    }
    public string ToolTip { get; init; } = "";

    internal event Action? Changed;

    /// <summary>Applies a bound desktop action's state without per-property refreshes.</summary>
    internal bool Bind(string boundLabel, bool disabled, bool isChecked)
    {
        if (label == boundLabel && Disabled == disabled && Checked == isChecked)
            return false;
        label = boundLabel;
        Disabled = disabled;
        Checked = isChecked;
        return true;
    }

    public void SetLabel(string value)
    {
        label = value;
        Changed?.Invoke();
    }

    public void SetAccelerator(string value)
    {
        accelerator = value;
        Changed?.Invoke();
    }

    public void SetEnabled(bool value)
    {
        Disabled = !value;
        Changed?.Invoke();
    }

    public void SetVisible(bool value)
    {
        Hidden = !value;
        Changed?.Invoke();
    }

    public void SetChecked(bool value)
    {
        Checked = value;
        Changed?.Invoke();
    }

    public bool Checked
    {
        get; set;
    }
    public bool Disabled
    {
        get; set;
    }
    public bool Hidden
    {
        get; set;
    }
}

public sealed unsafe partial class Menu : IDisposable
{
    static readonly Dictionary<nint, (Menu, MenuItem)> actions = new();
    readonly App app;
    MenuItem[] items;
    readonly List<nint> nativeItems = new();
    readonly Dictionary<nint, MenuItem> nativeDefinitions = new();
    nint native;
    readonly nint target;
    bool disposed;
    int nativeActionDepth;
    bool rebuildQueued;
    internal void CheckOwner(App owner)
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (app != owner)
            throw new ArgumentException("The menu belongs to another App.");
    }
    public Menu(App app, params MenuItem[] items)
    {
        app.CheckThread();
        this.app = app;
        this.items = Expand(items);
        Subscribe();
        app.RecentDocumentsChanged += RecentChanged;
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var cls = O.Define("NanoCSharpMenuTarget", "NSObject", ("activate:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Activate, "v@:@"));
            target = O.O(cls, "new");
            native = Build(this.items);
        }
    }

    public MenuItem? Find(string id)
    {
        MenuItem? Search(MenuItem[] entries)
        {
            foreach (var item in entries)
            {
                if (item.Id == id)
                    return item;
                if (item.Submenu is { } child && Search(child) is { } result)
                    return result;
            }

            return null;
        }

        return Search(items);
    }

    internal void Install()
    {
        if (native != 0)
        {
            O.V(O.O(O.Class("NSApplication"), "sharedApplication"), "setMainMenu:", native);
            InstallStandardMenus();
        }
        else
            foreach (var window in app.Windows.Where(w => w.Menu is null))
                Attach(window);
    }

    nint Build(MenuItem[] entries)
    {
        var menu = O.New("NSMenu");
        O.V(menu, "setAutoenablesItems:", (byte)0);
        foreach (var item in entries)
        {
            nint entry;
            if (item.Separator)
                entry = O.O(O.O(O.Class("NSMenuItem"), "separatorItem"), "retain");
            else
            {
                var (key, mods) = ParseMac(item.Accelerator);
                string role = item.Role == MenuRole.Quit ? "" : Selector(item.Role);
                entry = O.O(O.O(O.Class("NSMenuItem"), "alloc"), "initWithTitle:action:keyEquivalent:", O.TempString(item.Label), O.Sel(role.Length > 0 ? role : "activate:"), O.TempString(key));
                if (role.Length == 0)
                {
                    O.V(entry, "setTarget:", target);
                    actions[entry] = (this, item);
                }

                O.V(entry, "setKeyEquivalentModifierMask:", mods);
                if (item.ToolTip.Length > 0)
                    O.V(entry, "setToolTip:", O.TempString(item.ToolTip));
                O.V(entry, "setState:", (nuint)(item.Checked ? 1 : 0));
                O.V(entry, "setEnabled:", (byte)(item.Disabled ? 0 : 1));
                O.V(entry, "setHidden:", (byte)(item.Hidden ? 1 : 0));
                if (item.Submenu is { } children)
                {
                    var child = Build(children);
                    O.V(entry, "setSubmenu:", child);
                    O.Release(child);
                }
            }

            O.V(menu, "addItem:", entry);
            nativeItems.Add(entry);
            nativeDefinitions[entry] = item;
        }

        return menu;
    }

    static (string Key, nuint Modifiers) ParseMac(string accelerator)
    {
        if (accelerator.Length == 0)
            return ("", 0);
        nuint mods = 0;
        string key = "";
        foreach (var part in accelerator.Split('+'))
        {
            switch (part.ToLowerInvariant())
            {
                case "primary":
                case "cmdorctrl":
                case "commandorcontrol":
                case "command":
                case "cmd":
                case "meta":
                    mods |= 1u << 20;
                    break;
                case "ctrl":
                case "control":
                    mods |= 1u << 18;
                    break;
                case "alt":
                case "option":
                    mods |= 1u << 19;
                    break;
                case "shift":
                    mods |= 1u << 17;
                    break;
                default:
                    key = part.ToLowerInvariant() switch
                    {
                        "enter" => "\r",
                        "tab" => "\t",
                        "space" => " ",
                        "escape" => "\x1b",
                        "left" => "\uf702",
                        "right" => "\uf703",
                        "up" => "\uf700",
                        "down" => "\uf701",
                        "" => "+",
                        var s => s
                    };
                    break;
            }
        }

        return (key, mods);
    }

    static string Selector(MenuRole role) => role switch
    {
        MenuRole.About => "orderFrontStandardAboutPanel:",
        MenuRole.Hide => "hide:",
        MenuRole.HideOthers => "hideOtherApplications:",
        MenuRole.Unhide => "unhideAllApplications:",
        MenuRole.Quit => "terminate:",
        MenuRole.Undo => "undo:",
        MenuRole.Redo => "redo:",
        MenuRole.Cut => "cut:",
        MenuRole.Copy => "copy:",
        MenuRole.Paste => "paste:",
        MenuRole.Delete => "delete:",
        MenuRole.SelectAll => "selectAll:",
        MenuRole.Minimize => "performMiniaturize:",
        MenuRole.Zoom => "performZoom:",
        MenuRole.Close => "performClose:",
        MenuRole.ToggleFullScreen => "toggleFullScreen:",
        MenuRole.Front => "arrangeInFront:",
        _ => ""
    };
    [UnmanagedCallersOnly]
    static void Activate(nint self, nint cmd, nint sender)
    {
        if (!actions.TryGetValue(sender, out var entry))
            return;
        // AppKit may still inspect the sender, target and menu after this callback.
        // Keep them alive until its enclosing autorelease pool drains, even when
        // the action disposes this owner or runs a nested event loop.
        O.O(O.O(sender, "retain"), "autorelease");
        O.O(O.O(self, "retain"), "autorelease");
        O.O(O.O(entry.Item1.native, "retain"), "autorelease");
        entry.Item1.nativeActionDepth++;
        try
        {
            entry.Item1.Invoke(entry.Item2, null);
        }
        catch (Exception ex)
        {
            entry.Item1.app.Report(ex);
        }
        finally
        {
            entry.Item1.nativeActionDepth--;
            if (entry.Item1.nativeActionDepth == 0 && entry.Item1.rebuildQueued && !entry.Item1.disposed)
            {
                entry.Item1.rebuildQueued = false;
                try
                {
                    entry.Item1.app.Dispatch(() =>
                    {
                        if (!entry.Item1.disposed)
                            entry.Item1.Rebuild();
                    });
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    public void Refresh()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        RefreshPortable();
        foreach (var entry in nativeItems)
            if (nativeDefinitions.TryGetValue(entry, out var definition))
            {
                var item = (this, definition);
                O.V(entry, "setTitle:", O.TempString(item.Item2.Label));
                var (key, mods) = ParseMac(item.Item2.Accelerator);
                O.V(entry, "setKeyEquivalent:", O.TempString(key));
                O.V(entry, "setKeyEquivalentModifierMask:", mods);
                O.V(entry, "setState:", (nuint)(item.Item2.Checked ? 1 : 0));
                O.V(entry, "setEnabled:", (byte)(item.Item2.Disabled ? 0 : 1));
                O.V(entry, "setHidden:", (byte)(item.Item2.Hidden ? 1 : 0));
            }
    }

    internal bool HandleInput(Window window, Input input)
    {
        bool Search(MenuItem[] entries)
        {
            foreach (var item in entries)
            {
                if (item.Hidden || item.Disabled)
                    continue;
                if (item.Submenu is { } children && Search(children))
                    return true;
                if (item.Accelerator.Length == 0)
                    continue;
                var parts = item.Accelerator.Split('+');
                string key = parts[^1];
                if (!Enum.TryParse<Key>(key, true, out var expected) || input.Key != expected)
                    continue;
                Modifiers required = Modifiers.None;
                bool valid = true;
                foreach (string part in parts[..^1])
                {
                    var modifier = part.Trim().ToLowerInvariant() switch
                    {
                        "primary" or "cmdorctrl" or "commandorcontrol" => OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control,
                        "ctrl" or "control" => Modifiers.Control,
                        "cmd" or "command" or "meta" or "super" => Modifiers.Meta,
                        "alt" or "option" => Modifiers.Alt,
                        "shift" => Modifiers.Shift,
                        _ => Modifiers.None
                    };
                    valid &= modifier != Modifiers.None;
                    required |= modifier;
                }
                var actual = input.Modifiers & ~Modifiers.Primary;
                if ((input.Modifiers & Modifiers.Primary) != 0)
                    actual |= OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control;
                if (!valid || required != actual)
                    continue;
                if (item.Click is not null)
                    item.Click(item);
                else if (item.Role == MenuRole.Quit)
                    app.Quit();
                else if (item.Role == MenuRole.Close)
                    window.Close();
                else
                    return false;
                return true;
            }

            return false;
        }

        return Search(items);
    }

    internal nint Native => native;

    public void Dispose()
    {
        if (disposed)
            return;
        app.CheckThread();
        disposed = true;
        app.RecentDocumentsChanged -= RecentChanged;
        Unsubscribe();
        DisposePortable();
        if (!OperatingSystem.IsMacOS())
            return;
        var nativeApp = O.O(O.Class("NSApplication"), "sharedApplication");
        if (O.O(nativeApp, "mainMenu") == native)
            O.V(nativeApp, "setMainMenu:", (nint)0);
        RemoveStandardMenus();
        foreach (var item in nativeItems)
        {
            actions.Remove(item);
            O.Release(item);
        }

        O.Release(native);
        O.Release(target);
    }
}
