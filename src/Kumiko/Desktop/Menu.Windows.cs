using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

public sealed unsafe partial class Menu
{
    void AttachWindows(Window window)
    {
        var old = bars.GetValueOrDefault(window);
        var bar = BuildWindows(items, false);
        if (SetMenu(window.NativeHandle, window.Options.AutoHideMenuBar ? 0 : bar) == 0)
        {
            DestroyMenu(bar);
            throw new InvalidOperationException("Win32 SetMenu failed.");
        }

        bars[window] = bar;
        DrawMenuBar(window.NativeHandle);
        if (old != 0)
            DestroyMenu(old);
    }

    nint BuildWindows(MenuItem[] entries, bool popup)
    {
        var menu = popup ? CreatePopupMenu() : CreateMenu();
        if (menu == 0)
            throw new InvalidOperationException("Win32 menu allocation failed.");
        foreach (var item in entries.Where(i => !i.Hidden))
        {
            uint flags = (item.Separator ? 0x800u : 0) | (item.Disabled ? 3u : 0) | (item.Checked ? 8u : 0);
            nuint id;
            if (item.Submenu is { } children)
            {
                id = (nuint)BuildWindows(children, true);
                flags |= 0x10;
            }
            else
            {
                var existing = commands.FirstOrDefault(pair => ReferenceEquals(pair.Value, item));
                id = existing.Key != 0 ? existing.Key : NextWindowsCommand();
                if (id > 65535)
                    throw new InvalidOperationException("A native menu cannot contain more than 65,435 commands.");
                commands[(uint)id] = item;
            }

            var label = item.Label.Replace("&", "&&", StringComparison.Ordinal);
            if (item.Accelerator.Length > 0)
                label += "\t" + item.Accelerator;
            if (AppendMenuW(menu, flags, id, label) == 0)
            {
                DestroyMenu(menu);
                throw new InvalidOperationException("Win32 AppendMenu failed.");
            }
        }

        return menu;
    }

    // Retired items may leave gaps; never reuse an ID that still names a live item.
    uint NextWindowsCommand()
    {
        uint id = (uint)commands.Count + 100;
        while (commands.ContainsKey(id))
            id++;
        return id;
    }

    // Regenerated recent-document items replace their predecessors; forget the old command IDs.
    void RetireWindowsCommands(MenuItem[]? retired)
    {
        if (!OperatingSystem.IsWindows() || retired is null)
            return;
        foreach (var item in retired)
        {
            RetireWindowsCommands(item.Submenu);
            foreach (var id in commands.Where(pair => ReferenceEquals(pair.Value, item)).Select(pair => pair.Key).ToArray())
                commands.Remove(id);
        }
    }

    internal void PopupWindows(nint owner, int x, int y)
    {
        var menu = BuildWindows(items, true);
        try
        {
            uint command = TrackPopupMenuEx(menu, 0x100 | 2, x, y, owner, 0);
            if (commands.TryGetValue(command, out var item))
                Invoke(item, app.Windows.FirstOrDefault(w => w.IsFocused));
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    [DllImport("user32.dll")]
    static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll")]
    static extern nint CreateMenu();
    [DllImport("user32.dll")]
    static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int AppendMenuW(nint menu, uint flags, nuint id, string label);
    [DllImport("user32.dll")]
    static extern int SetMenu(nint window, nint menu);
    [DllImport("user32.dll")]
    static extern int DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    static extern int DrawMenuBar(nint window);
}
