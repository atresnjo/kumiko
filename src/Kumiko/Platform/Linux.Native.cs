using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal static unsafe class Gtk
{
    internal const string Library = "libgtk-3.so.0", GLib = "libglib-2.0.so.0", GObject = "libgobject-2.0.so.0", Gdk = "libgdk-3.so.0", Cairo = "libcairo.so.2";
    [DllImport(Library)]
    internal static extern int gtk_init_check(nint argc, nint argv);
    [DllImport(Library)]
    internal static extern nint gtk_window_new(int type);
    [DllImport(Library)]
    internal static extern void gtk_main();
    [DllImport(Library)]
    internal static extern void gtk_main_quit();
    [DllImport(Library)]
    internal static extern uint gtk_main_level();
    [DllImport(Library)]
    internal static extern void gtk_window_set_default_size(nint w, int width, int height);
    [DllImport(Library)]
    internal static extern void gtk_window_resize(nint w, int width, int height);
    [DllImport(Library, CharSet = CharSet.Ansi)]
    internal static extern void gtk_window_set_title(nint w, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
    [DllImport(Library)]
    internal static extern void gtk_window_set_resizable(nint w, int value);
    [DllImport(Library)]
    internal static extern void gtk_window_set_decorated(nint w, int value);
    [DllImport(Library)]
    internal static extern void gtk_window_set_keep_above(nint w, int value);
    [DllImport(Library)]
    internal static extern void gtk_window_move(nint w, int x, int y);
    [DllImport(Library)]
    internal static extern void gtk_window_iconify(nint w);
    [DllImport(Library)]
    internal static extern void gtk_window_maximize(nint w);
    [DllImport(Library)]
    internal static extern void gtk_drag_dest_set(nint widget, int flags, nint targets, int count, int actions);
    [DllImport(Library)]
    internal static extern nint gtk_selection_data_get_uris(nint selection);
    [DllImport(GLib)]
    internal static extern nint g_filename_from_uri(nint uri, nint hostname, nint error);
    [DllImport(GLib)]
    internal static extern void g_strfreev(nint list);
    [DllImport(Library)]
    internal static extern void gtk_window_get_position(nint w, out int x, out int y);
    [DllImport(Library)]
    internal static extern void gtk_window_get_size(nint w, out int width, out int height);
    [DllImport(Library)]
    internal static extern int gtk_window_is_maximized(nint w);
    [DllImport(Library)]
    internal static extern void gtk_window_unmaximize(nint w);
    [DllImport(Library)]
    internal static extern void gtk_window_present(nint w);
    [DllImport(Library)]
    internal static extern nint gtk_drawing_area_new();
    [DllImport(Library)]
    internal static extern void gtk_container_add(nint parent, nint child);
    [DllImport(Library)]
    internal static extern void gtk_widget_show_all(nint w);
    [DllImport(Library)]
    internal static extern void gtk_widget_show(nint w);
    [DllImport(Library)]
    internal static extern void gtk_widget_hide(nint w);
    [DllImport(Library)]
    internal static extern int gtk_widget_get_visible(nint w);
    [DllImport(Library)]
    internal static extern void gtk_widget_destroy(nint w);
    [DllImport(Library)]
    internal static extern void gtk_widget_queue_draw(nint w);
    [DllImport(Library)]
    internal static extern void gtk_widget_set_can_focus(nint w, int value);
    [DllImport(Library)]
    internal static extern void gtk_widget_grab_focus(nint w);
    [DllImport(Library)]
    internal static extern void gtk_widget_add_events(nint w, int events);
    [DllImport(Library)]
    internal static extern int gtk_widget_get_allocated_width(nint w);
    [DllImport(Library)]
    internal static extern int gtk_widget_get_allocated_height(nint w);
    [DllImport(Library)]
    internal static extern int gtk_widget_get_scale_factor(nint w);
    [DllImport(Library)]
    internal static extern nint gtk_widget_get_window(nint w);
    [DllImport(Library)]
    internal static extern nint gtk_im_multicontext_new();
    [DllImport(Library)]
    internal static extern void gtk_window_begin_move_drag(nint w, int button, int rootX, int rootY, uint time);
    [DllImport(Gdk)]
    internal static extern nint gdk_display_get_default();
    [DllImport(Gdk)]
    internal static extern nint gdk_display_get_default_seat(nint display);
    [DllImport(Gdk)]
    internal static extern nint gdk_seat_get_pointer(nint seat);
    [DllImport(Gdk)]
    internal static extern nint gdk_get_default_root_window();
    [DllImport(Gdk)]
    internal static extern nint gdk_window_get_device_position(nint window, nint device, out int x, out int y, out uint mask);
    [DllImport(Library)] internal static extern void gtk_im_context_focus_in(nint context);
    [DllImport(Library)] internal static extern void gtk_im_context_focus_out(nint context);
    [DllImport(Library)] internal static extern void gtk_im_context_set_cursor_location(nint context, ref CursorRect rect);
    [StructLayout(LayoutKind.Sequential)]
    internal struct CursorRect
    {
        public int X, Y, Width, Height;
    }
    [DllImport(Library)]
    internal static extern void gtk_im_context_set_client_window(nint im, nint window);
    [DllImport(Library)]
    internal static extern int gtk_im_context_filter_keypress(nint im, nint e);
    [DllImport(Library)]
    internal static extern void gtk_im_context_get_preedit_string(nint im, out nint text, out nint attrs, out int cursor);
    [DllImport(GLib)]
    internal static extern void g_free(nint value);
    [DllImport(GObject)]
    internal static extern void g_object_unref(nint value);
    [DllImport(GObject)]
    internal static extern ulong g_signal_connect_data(
        nint obj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal,
        nint callback,
        nint data,
        nint destroy,
        int flags
    );
    [DllImport(GLib)]
    internal static extern uint g_idle_add_full(int priority, delegate* unmanaged<nint, int> callback, nint data, nint notify);
    [DllImport(GLib)]
    internal static extern uint g_timeout_add(uint interval, delegate* unmanaged<nint, int> callback, nint data);
    [DllImport(GLib)]
    internal static extern int g_main_context_iteration(nint context, int block);
    [DllImport(GLib)]
    internal static extern int g_source_remove(uint source);
    [DllImport(Cairo)]
    internal static extern nint cairo_image_surface_create_for_data(void* data, int format, int width, int height, int stride);
    [DllImport(Cairo)]
    internal static extern void cairo_set_source_surface(nint cairo, nint surface, double x, double y);
    [DllImport(Cairo)]
    internal static extern void cairo_surface_destroy(nint surface);
    [DllImport(Cairo)]
    internal static extern void cairo_paint(nint cairo);
    [DllImport(Cairo)]
    internal static extern void cairo_scale(nint cairo, double x, double y);
    [DllImport("libpango-1.0.so.0")]
    internal static extern void pango_attr_list_unref(nint attrs);
    [StructLayout(LayoutKind.Sequential)]
    internal struct ButtonEvent
    {
        public int Type;
        public nint Window;
        public sbyte Sent;
        public uint Time;
        public double X, Y;
        public nint Axes;
        public uint State, Button;
        public nint Device;
        public double RootX, RootY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyEvent
    {
        public int Type;
        public nint Window;
        public sbyte Sent;
        public uint Time, State, Key;
        public int Length;
        public nint Text;
        public ushort Hardware;
        public byte Group;
        public uint Modifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScrollEvent
    {
        public int Type;
        public nint Window;
        public sbyte Sent;
        public uint Time;
        public double X, Y;
        public uint State;
        public int Direction;
        public nint Device;
        public double RootX, RootY, DeltaX, DeltaY;
        public uint Stop;
    }
}
