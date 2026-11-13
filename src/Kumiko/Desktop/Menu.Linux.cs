using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;

public sealed unsafe partial class Menu
{
    static readonly Dictionary<nint, (Menu Menu, MenuItem Item, Window? Window)> linuxItems = new();
    readonly Dictionary<Window, nint> linuxBars = new();
    internal static readonly Dictionary<nint, nint> LinuxContainers = new();
    void AttachLinux(Window window)
    {
        LinuxRemove(window);
        if (!LinuxContainers.TryGetValue(window.NativeHandle, out var container))
            throw new InvalidOperationException("The GTK window has no menu container.");
        var bar = BuildLinux(items, window, true);
        linuxBars[window] = bar;
        gtk_box_pack_start(container, bar, 0, 0, 0);
        gtk_box_reorder_child(container, bar, 0);
        if (!window.Options.AutoHideMenuBar)
            Gtk.gtk_widget_show_all(bar);
    }

    void LinuxRemove(Window window)
    {
        foreach (var pair in linuxItems.Where(p => p.Value.Menu == this && p.Value.Window == window).ToArray())
            linuxItems.Remove(pair.Key);
        if (linuxBars.Remove(window, out var bar))
            Gtk.gtk_widget_destroy(bar);
    }

    nint BuildLinux(MenuItem[] entries, Window? window, bool bar)
    {
        var menu = bar ? gtk_menu_bar_new() : gtk_menu_new();
        foreach (var item in entries.Where(i => !i.Hidden))
        {
            var label = item.Label + (item.Accelerator.Length > 0 ? "    " + item.Accelerator : "");
            var entry = item.Separator ? gtk_separator_menu_item_new() : gtk_check_menu_item_new_with_label(label);
            if (!item.Separator)
            {
                gtk_check_menu_item_set_active(entry, item.Checked ? 1 : 0);
                gtk_check_menu_item_set_draw_as_radio(entry, item.Radio ? 1 : 0);
                gtk_widget_set_sensitive(entry, item.Disabled ? 0 : 1);
                if (item.ToolTip.Length > 0)
                    gtk_widget_set_tooltip_text(entry, item.ToolTip);
                if (item.Submenu is { } children)
                    gtk_menu_item_set_submenu(entry, BuildLinux(children, window, false));
                else
                {
                    linuxItems[entry] = (this, item, window);
                    Gtk.g_signal_connect_data(entry, "activate", (nint)(delegate* unmanaged<nint, nint, void>)&LinuxActivate, 0, 0, 0);
                }
            }

            gtk_menu_shell_append(menu, entry);
        }

        return menu;
    }

    internal (nint Handle, Action Release) CreateLinuxTrayMenu()
    {
        var previous = linuxItems.Keys.ToHashSet();
        var menu = BuildLinux(items, null, false);
        var added = linuxItems.Keys.Where(key => !previous.Contains(key)).ToArray();
        Gtk.gtk_widget_show_all(menu);
        return (menu, () =>
        {
            foreach (var key in added)
                linuxItems.Remove(key);
            Gtk.gtk_widget_destroy(menu);
        }
        );
    }

    [UnmanagedCallersOnly]
    static void LinuxActivate(nint item, nint data)
    {
        if (!linuxItems.TryGetValue(item, out var target))
            return;
        try
        {
            target.Menu.Invoke(target.Item, target.Window);
        }
        catch (Exception ex)
        {
            target.Menu.app.Report(ex);
        }
    }

    [DllImport(Gtk.Library)]
    static extern nint gtk_menu_bar_new();
    [DllImport(Gtk.Library)]
    static extern nint gtk_menu_new();
    [DllImport(Gtk.Library)]
    static extern nint gtk_separator_menu_item_new();
    [DllImport(Gtk.Library)]
    static extern nint gtk_check_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk.Library)]
    static extern void gtk_check_menu_item_set_active(nint item, int active);
    [DllImport(Gtk.Library)]
    static extern void gtk_check_menu_item_set_draw_as_radio(nint item, int radio);
    [DllImport(Gtk.Library)]
    static extern void gtk_menu_shell_append(nint menu, nint child);
    [DllImport(Gtk.Library)]
    static extern void gtk_menu_item_set_submenu(nint item, nint submenu);
    [DllImport(Gtk.Library)]
    static extern void gtk_widget_set_sensitive(nint widget, int enabled);
    [DllImport(Gtk.Library)]
    static extern void gtk_widget_set_tooltip_text(nint widget, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Gtk.Library)]
    static extern void gtk_box_pack_start(nint box, nint child, int expand, int fill, uint padding);
    [DllImport(Gtk.Library)]
    static extern void gtk_box_reorder_child(nint box, nint child, int position);
}
