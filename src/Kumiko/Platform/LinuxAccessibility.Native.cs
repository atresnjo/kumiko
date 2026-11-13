using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal static unsafe class Atk
{
    internal const string Lib = "libatk-1.0.so.0", GObject = "libgobject-2.0.so.0", Gtk = "libgtk-3.so.0";
    [StructLayout(LayoutKind.Sequential)]
    internal struct TypeInfo
    {
        public nuint Type; public nint Name; public uint ClassSize, InstanceSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct InterfaceInfo
    {
        public nint Init, Finalize, Data;
    }
    [DllImport(GObject)] internal static extern void g_type_query(nuint type, out TypeInfo query);
    [DllImport(GObject)] internal static extern nuint g_object_get_type();
    [DllImport(GObject)] internal static extern nuint g_type_register_static_simple(nuint parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint classSize, nint classInit, uint instanceSize, nint init, uint flags);
    [DllImport(GObject)] internal static extern void g_type_add_interface_static(nuint type, nuint iface, ref InterfaceInfo info);
    [DllImport(GObject)] internal static extern nint g_object_new(nuint type, nint end);
    [DllImport(GObject)] internal static extern nint g_object_ref(nint value);
    [DllImport(GObject)] internal static extern void g_object_unref(nint value);
    [DllImport(GObject, EntryPoint = "g_signal_emit_by_name")]
    internal static extern void ChildChanged(nint value, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, uint index, nint child, nint result);
    [DllImport(GObject, EntryPoint = "g_signal_emit_by_name")]
    internal static extern void TextChanged(nint value, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, int position, int count, nint result);
    [DllImport(GObject, EntryPoint = "g_signal_emit_by_name")]
    internal static extern void Changed(nint value, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, nint result);
    [DllImport("libglib-2.0.so.0")] internal static extern nint g_strdup([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Lib)] internal static extern nuint atk_object_get_type();
    [DllImport(Lib)] internal static extern nuint atk_component_get_type();
    [DllImport(Lib)] internal static extern nuint atk_action_get_type();
    [DllImport(Lib)] internal static extern nuint atk_text_get_type();
    [DllImport(Lib)] internal static extern nuint atk_editable_text_get_type();
    [DllImport(Lib)] internal static extern nuint atk_value_get_type();
    [DllImport(Lib)] internal static extern void atk_object_set_name(nint value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] internal static extern void atk_object_set_description(nint value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] internal static extern void atk_object_set_role(nint value, int role);
    [DllImport(Lib)] internal static extern int atk_role_for_name([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] internal static extern void atk_object_set_parent(nint value, nint parent);
    [DllImport(Lib)] internal static extern nint atk_state_set_new();
    [DllImport(Lib)] internal static extern int atk_state_type_for_name([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] internal static extern int atk_state_set_add_state(nint set, int state);
    [DllImport(Lib)] internal static extern void atk_object_notify_state_change(nint value, int state, int enabled);
    [DllImport(Lib)] internal static extern nint atk_range_new(double minimum, double maximum, [MarshalAs(UnmanagedType.LPUTF8Str)] string description);
    [DllImport(Gtk)] internal static extern nuint gtk_drawing_area_get_type();
    [DllImport(Gtk)] internal static extern nuint gtk_widget_accessible_get_type();
    [DllImport(Gtk)] internal static extern void gtk_widget_class_set_accessible_type(nint klass, nuint type);
    [DllImport(Gtk)] internal static extern nint gtk_widget_get_accessible(nint widget);
    [DllImport(Gtk)] internal static extern nint gtk_widget_get_window(nint widget);
    [DllImport("libgdk-3.so.0")] internal static extern int gdk_window_get_origin(nint window, out int x, out int y);
}
