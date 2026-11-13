using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko.Desktop;

public sealed unsafe partial class Menu
{
    readonly HashSet<Window> attached = new();
    readonly List<(MenuItem Item, Action Handler)> subscribed = new();
    readonly Dictionary<uint, MenuItem> commands = new();
    readonly Dictionary<Window, nint> bars = new();
    void Subscribe()
    {
        void Visit(MenuItem[] entries)
        {
            foreach (var item in entries)
            {
                Action handler = () =>
                {
                    if (item.Radio && item.Checked)
                        SelectRadio(item);
                    Refresh();
                };
                item.Changed += handler;
                subscribed.Add((item, handler));
                if (item.Submenu is { } children)
                    Visit(children);
            }
        }

        Visit(items);
    }

    void Unsubscribe()
    {
        foreach (var subscription in subscribed)
            subscription.Item.Changed -= subscription.Handler;
        subscribed.Clear();
    }

    public void Append(MenuItem item) => Insert(items.Length, item);
    public void Insert(int index, MenuItem item)
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if ((uint)index > (uint)items.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        ArgumentNullException.ThrowIfNull(item);
        Unsubscribe();
        var list = items.ToList();
        list.InsertRange(index, Expand([item]));
        items = list.ToArray();
        Subscribe();
        Rebuild();
    }

    internal void Detach(Window window)
    {
        app.CheckThread();
        if (!attached.Remove(window))
            return;
        window.Closed -= WindowClosed;
        if (OperatingSystem.IsWindows())
        {
            SetMenu(window.NativeHandle, 0);
            if (bars.Remove(window, out var bar))
                DestroyMenu(bar);
        }
        else if (OperatingSystem.IsLinux())
            LinuxRemove(window);
    }

    internal void Attach(Window window)
    {
        app.CheckThread();
        if (window.App != app)
            throw new ArgumentException("Menu belongs to another App.", nameof(window));
        if (OperatingSystem.IsMacOS())
            return;
        attached.Add(window);
        window.Closed -= WindowClosed;
        window.Closed += WindowClosed;
        if (OperatingSystem.IsWindows())
            AttachWindows(window);
        else
            AttachLinux(window);
    }

    void WindowClosed()
    {
        foreach (var window in attached.Where(w => !app.Windows.Contains(w)).ToArray())
        {
            if (!OperatingSystem.IsWindows())
                LinuxRemove(window);
            if (bars.Remove(window, out var bar) && OperatingSystem.IsWindows())
                DestroyMenu(bar);
            attached.Remove(window);
            window.Closed -= WindowClosed;
        }
    }

    internal bool InvokeCommand(uint id, Window window)
    {
        if (disposed || !commands.TryGetValue(id, out var item))
            return false;
        Invoke(item, window);
        return true;
    }

    void SelectRadio(MenuItem item)
    {
        MenuItem[]? Find(MenuItem[] entries)
        {
            if (entries.Contains(item))
                return entries;
            foreach (var entry in entries)
                if (entry.Submenu is { } children && Find(children) is { } result)
                    return result;
            return null;
        }

        if (Find(items) is { } group)
        {
            int position = Array.IndexOf(group, item), start = position, end = position;
            while (start > 0 && group[start - 1].Radio)
                start--;
            while (end + 1 < group.Length && group[end + 1].Radio)
                end++;
            for (int i = start; i <= end; i++)
                group[i].Checked = group[i] == item;
        }
    }

    void Invoke(MenuItem item, Window? window)
    {
        if (disposed || item.Hidden || item.Disabled)
            return;
        if (item.Radio)
            SelectRadio(item);
        if (item.Click is { } callback)
            callback(item);
        else if (item.Role == MenuRole.DesktopAction)
            app.InvokeDesktopAction(item.Id);
        else if (item.Role == MenuRole.Quit)
            app.Quit();
        else if (window is not null)
        {
            switch (item.Role)
            {
                case MenuRole.Close:
                    window.Close();
                    break;
                case MenuRole.Minimize:
                    window.Minimize();
                    break;
                case MenuRole.Zoom:
                    window.ToggleMaximize();
                    break;
                case MenuRole.ToggleFullScreen:
                    window.ToggleFullScreen();
                    break;
                default:
                    var key = item.Role switch
                    {
                        MenuRole.Undo => Key.Z,
                        MenuRole.Redo => Key.Y,
                        MenuRole.Cut => Key.X,
                        MenuRole.Copy => Key.C,
                        MenuRole.Paste => Key.V,
                        MenuRole.SelectAll => Key.A,
                        MenuRole.Delete => Key.Delete,
                        _ => Key.None
                    };
                    if (key != Key.None)
                        window.Input(i =>
                        {
                            i.Key = key;
                            i.Modifiers = key == Key.Delete ? 0 : Modifiers.Primary;
                        });
                    break;
            }
        }

        if (!disposed)
            Refresh();
    }

    void Rebuild()
    {
        if (nativeActionDepth > 0)
        {
            rebuildQueued = true;
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            bool installed = ObjC.O(ObjC.O(ObjC.Class("NSApplication"), "sharedApplication"), "mainMenu") == native;
            foreach (var item in nativeItems)
            {
                actions.Remove(item);
                ObjC.Release(item);
            }

            nativeItems.Clear();
            nativeDefinitions.Clear();
            ObjC.Release(native);
            native = Build(items);
            if (installed)
                Install();
        }
        else
            foreach (var window in attached.ToArray())
                Attach(window);
    }

    void RefreshPortable()
    {
        if (!OperatingSystem.IsMacOS())
            Rebuild();
    }

    void DisposePortable()
    {
        foreach (var window in attached)
        {
            window.Closed -= WindowClosed;
            if (OperatingSystem.IsWindows())
                SetMenu(window.NativeHandle, 0);
            else
                LinuxRemove(window);
        }

        if (OperatingSystem.IsWindows())
            foreach (var bar in bars.Values)
                DestroyMenu(bar);
        bars.Clear();
        commands.Clear();
        attached.Clear();
    }
}
