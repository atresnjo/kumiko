using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed unsafe partial class LinuxWindow
{
    public void Show()
    {
        Gtk.gtk_widget_show_all(window);
        Gtk.gtk_window_present(window);
    }

    public void Hide() => Gtk.gtk_widget_hide(window);
    public void SetTitle(string title) => Gtk.gtk_window_set_title(window, title);
    public void Resize(float width, float height)
    {
        requested = (requested ?? new WindowState(0, 0, width, height)) with
        {
            Width = width,
            Height = height
        };
        Gtk.gtk_window_resize(window, (int)width, (int)height);
        owner.ResizeFromNative(width, height, 1);
    }

    WindowState? requested;
    /// <summary>Unmapped GTK windows report no geometry, so their last requested placement is returned.</summary>
    public WindowState? State
    {
        get
        {
            if (!IsVisible)
                return requested;
            Gtk.gtk_window_get_position(window, out var x, out var y);
            Gtk.gtk_window_get_size(window, out var width, out var height);
            return new(x, y, width, height, Gtk.gtk_window_is_maximized(window) != 0);
        }
    }

    public void Restore(WindowState state)
    {
        requested = state;
        Gtk.gtk_window_move(window, (int)state.X, (int)state.Y);
        Gtk.gtk_window_resize(window, (int)state.Width, (int)state.Height);
        if (state.Maximized)
            Gtk.gtk_window_maximize(window);
        else
            Gtk.gtk_window_unmaximize(window);
        maximized = state.Maximized;
        owner.ResizeFromNative(state.Width, state.Height, 1);
    }

    public void Minimize() => Gtk.gtk_window_iconify(window);
    public bool BeginMove()
    {
        nint device = Gtk.gdk_seat_get_pointer(Gtk.gdk_display_get_default_seat(Gtk.gdk_display_get_default()));
        nint root = Gtk.gdk_get_default_root_window();
        if (device == 0 || root == 0)
            return false;
        Gtk.gdk_window_get_device_position(root, device, out int x, out int y, out uint mask);
        // GDK_BUTTON1_MASK: only a held primary button may drive the window manager move.
        if ((mask & 0x100) == 0)
            return false;
        Gtk.gtk_window_begin_move_drag(window, 1, x, y, 0);
        return true;
    }

    public void ToggleMaximize()
    {
        if (maximized)
            Gtk.gtk_window_unmaximize(window);
        else
            Gtk.gtk_window_maximize(window);
        maximized = !maximized;
    }

    bool fullScreen, minimized;
    public nint NativeHandle => window;
    public bool IsFocused => gtk_window_is_active(window) != 0;
    public bool IsMaximized => IsVisible ? Gtk.gtk_window_is_maximized(window) != 0 : maximized;
    public bool IsMinimized => minimized;
    public bool IsFullScreen => fullScreen;

    public Rect Bounds
    {
        get
        {
            if (!IsVisible && requested is { } state)
                return new(state.X, state.Y, state.Width, state.Height);
            gtk_window_get_position(window, out var x, out var y);
            Gtk.gtk_window_get_size(window, out var width, out var height);
            return new(x, y, width, height);
        }
    }

    public Size ContentSize => !IsVisible && requested is { } state ? new(state.Width, state.Height) : new(Gtk.gtk_widget_get_allocated_width(view), Gtk.gtk_widget_get_allocated_height(view));

    public void SetBounds(Rect bounds)
    {
        requested = new(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized, fullScreen);
        Gtk.gtk_window_move(window, (int)bounds.X, (int)bounds.Y);
        Gtk.gtk_window_resize(window, (int)bounds.Width, (int)bounds.Height);
    }

    public void Configure(WindowOptions o)
    {
        if (o.Panel)
            gtk_window_set_type_hint(window, 5); // GDK_WINDOW_TYPE_HINT_UTILITY

        var geometry = new Geometry
        {
            MinWidth = (int)o.MinWidth,
            MinHeight = (int)o.MinHeight,
            MaxWidth = o.MaxWidth > 0 ? (int)o.MaxWidth : int.MaxValue,
            MaxHeight = o.MaxHeight > 0 ? (int)o.MaxHeight : int.MaxValue
        };
        gtk_window_set_geometry_hints(window, 0, ref geometry, 2 | 4);
        gtk_widget_set_opacity(window, o.Opacity);
        gtk_window_set_skip_taskbar_hint(window, o.SkipTaskbar || o.Panel ? 1 : 0);
        gtk_window_set_deletable(window, o.Closable ? 1 : 0);
        if (o.VisibleOnAllWorkspaces)
            gtk_window_stick(window);
        else
            gtk_window_unstick(window);
        if (o.Transparent)
        {
            var visual = gdk_screen_get_rgba_visual(gtk_widget_get_screen(window));
            if (visual == 0)
                throw new PlatformNotSupportedException("The GTK display has no RGBA visual.");
            gtk_widget_set_visual(window, visual);
            gtk_widget_set_app_paintable(window, 1);
        }

        if (o.Parent is { } parent)
        {
            gtk_window_set_transient_for(window, parent.NativeHandle);
            gtk_window_set_modal(window, o.Modal ? 1 : 0);
        }

        if (o.IgnoreMouseEvents)
        {
            gtk_widget_realize(window);
            var region = cairo_region_create();
            gdk_window_input_shape_combine_region(Gtk.gtk_widget_get_window(window), region, 0, 0);
            cairo_region_destroy(region);
        }
        else if (Gtk.gtk_widget_get_window(window) is var native && native != 0)
            gdk_window_input_shape_combine_region(native, 0, 0, 0);
    }

    public void Perform(WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Focus:
                Gtk.gtk_window_present(window);
                break;
            case WindowAction.Blur:
                throw new PlatformNotSupportedException("GTK/compositors do not expose portable focus relinquishment.");
            case WindowAction.Maximize:
                Gtk.gtk_window_maximize(window);
                maximized = true;
                break;
            case WindowAction.Unmaximize:
                Gtk.gtk_window_unmaximize(window);
                maximized = false;
                break;
            case WindowAction.Restore:
                gtk_window_deiconify(window);
                Gtk.gtk_window_unmaximize(window);
                minimized = maximized = false;
                break;
            case WindowAction.EnterFullScreen:
                gtk_window_fullscreen(window);
                fullScreen = true;
                break;
            case WindowAction.LeaveFullScreen:
                gtk_window_unfullscreen(window);
                fullScreen = false;
                break;
        }
    }

    public void Progress(double? value) => throw new PlatformNotSupportedException("GTK 3 has no standard taskbar progress protocol.");
    public void Attention(bool enabled) => gtk_window_set_urgency_hint(window, enabled ? 1 : 0);
    void DisposeShell() => dragSnapshot?.Dispose();
    [UnmanagedCallersOnly]
    static int ShellChanged(nint self, nint ev, nint data)
    {
        Guard(self, w =>
        {
            w.owner.NativeShellChanged();
            w.owner.App.Dispatch(() => w.owner.App.NativeActiveChanged(w.owner.App.Windows.Any(window => window.IsFocused)));
        });
        return 0;
    }

    [UnmanagedCallersOnly]
    static int WindowStateChanged(nint self, nint ev, nint data)
    {
        var state = *(StateEvent*)ev;
        Guard(self, w =>
        {
            w.fullScreen = (state.State & 16) != 0;
            w.minimized = (state.State & 2) != 0;
            w.maximized = (state.State & 4) != 0;
            w.owner.NativeShellChanged();
        });
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StateEvent
    {
        public int Type;
        public nint Window;
        public byte Sent;
        public uint Changed, State;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Geometry
    {
        public int MinWidth, MinHeight, MaxWidth, MaxHeight, BaseWidth, BaseHeight, WidthIncrement, HeightIncrement;
        public double MinAspect, MaxAspect;
        public int Gravity;
    }

    [DllImport(Gtk.Library)]
    static extern nint gtk_header_bar_new();
    [DllImport(Gtk.Library)]
    static extern void gtk_header_bar_set_show_close_button(nint header, int show);
    [DllImport(Gtk.Library)]
    static extern void gtk_header_bar_set_has_subtitle(nint header, int subtitle);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_titlebar(nint window, nint titlebar);
    [DllImport(Gtk.Library)]
    static extern int gtk_window_is_active(nint window);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_get_position(nint window, out int x, out int y);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_geometry_hints(nint window, nint widget, ref Geometry geometry, int hints);
    [DllImport(Gtk.Library)]
    static extern void gtk_widget_set_opacity(nint widget, double opacity);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_skip_taskbar_hint(nint window, int skip);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_deletable(nint window, int deletable);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_stick(nint window);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_unstick(nint window);
    [DllImport(Gtk.Library)]
    static extern nint gtk_widget_get_screen(nint widget);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_screen_get_rgba_visual(nint screen);
    [DllImport(Gtk.Library)]
    static extern void gtk_widget_set_visual(nint widget, nint visual);
    [DllImport(Gtk.Library)]
    static extern void gtk_widget_set_app_paintable(nint widget, int paintable);
    [DllImport(Gtk.Library)]
    static extern void gtk_widget_realize(nint widget);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_transient_for(nint window, nint parent);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_modal(nint window, int modal);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_deiconify(nint window);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_fullscreen(nint window);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_unfullscreen(nint window);
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_urgency_hint(nint window, int urgent);
    [DllImport(Gtk.Cairo)]
    static extern nint cairo_region_create();
    [DllImport(Gtk.Cairo)]
    static extern void cairo_region_destroy(nint region);
    [DllImport(Gtk.Gdk)]
    static extern void gdk_window_input_shape_combine_region(nint window, nint region, int x, int y);
}
