using System.Runtime.InteropServices;
using Kumiko;

static partial class NativeDesktopChecks
{
    const string Gtk = "libgtk-3.so.0", Atk = "libatk-1.0.so.0";
    [DllImport(Gtk)] static extern nint gtk_window_list_toplevels();
    [DllImport(Gtk)] static extern nint gtk_window_get_title(nint window);
    [DllImport(Gtk)] static extern nint gtk_bin_get_child(nint window);
    [DllImport(Gtk)] static extern nint gtk_widget_get_accessible(nint widget);
    [DllImport(Atk)] static extern nint atk_object_get_name(nint value);
    [DllImport(Atk)] static extern int atk_object_get_n_accessible_children(nint value);
    [DllImport(Atk)] static extern nint atk_object_ref_accessible_child(nint value, int index);
    [DllImport(Atk)] static extern void atk_editable_text_set_text_contents(nint value, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Atk)] static extern nint atk_text_get_text(nint value, int start, int end);
    [DllImport(Atk)] static extern int atk_text_set_selection(nint value, int number, int start, int end);
    [DllImport(Atk)] static extern void atk_value_set_value(nint value, double number);
    [DllImport(Atk)] static extern int atk_action_do_action(nint value, int number);
    [DllImport("libgobject-2.0.so.0")] static extern void g_object_unref(nint value);
    [DllImport("libgobject-2.0.so.0")] static extern nint g_object_ref(nint value);
    [DllImport("libglib-2.0.so.0")] static extern void g_list_free(nint value);
    [DllImport("libglib-2.0.so.0")] static extern void g_free(nint value);
    static string Utf8(nint value) => Marshal.PtrToStringUTF8(value) ?? "";
    static nint FindAtk(nint root, string name)
    {
        if (Utf8(atk_object_get_name(root)) == name)
            return g_object_ref(root);
        for (int i = 0; i < atk_object_get_n_accessible_children(root); i++)
        {
            var child = atk_object_ref_accessible_child(root, i);
            var found = FindAtk(child, name);
            g_object_unref(child);
            if (found != 0)
                return found;
        }
        return 0;
    }
    static void Linux(App app, Action<bool, string> check)
    {
        var list = gtk_window_list_toplevels();
        nint view = 0;
        try
        {
            for (var item = list; item != 0; item = Marshal.ReadIntPtr(item, nint.Size))
            {
                var window = Marshal.ReadIntPtr(item);
                if (Utf8(gtk_window_get_title(window)) == "Desktop foundation verification")
                    view = gtk_bin_get_child(window);
            }
        }
        finally { g_list_free(list); }
        check(view != 0, "ATK query selects only the owned hidden GTK window");
        var root = gtk_widget_get_accessible(view);
        var editor = FindAtk(root, "Native editor");
        var slider = FindAtk(root, "Native amount");
        var toggle = FindAtk(root, "Native toggle");
        var password = FindAtk(root, "Native password");
        var disabled = FindAtk(root, "Native disabled");
        try
        {
            check(editor != 0 && slider != 0 && toggle != 0 && password != 0, "GTK exposes the native semantic tree through ATK");
            atk_editable_text_set_text_contents(editor, "Native updated 日本語");
            atk_value_set_value(slider, 65);
            check(atk_action_do_action(toggle, 0) != 0, "ATK action reaches the checkbox");
            check(atk_action_do_action(disabled, 0) == 0, "ATK rejects disabled native actions");
            app.RunFor(TimeSpan.FromMilliseconds(80));
            var text = atk_text_get_text(editor, 0, -1);
            try
            {
                check(Utf8(text) == "Native updated 日本語", "ATK text query returns the updated native value");
            }
            finally { g_free(text); }
            check(atk_text_set_selection(editor, 0, 0, 6) != 0, "ATK exposes editable text selection");
            var secret = atk_text_get_text(password, 0, -1);
            try
            {
                check(Utf8(secret) == "", "ATK never exposes password contents");
            }
            finally { g_free(secret); }
        }
        finally
        {
            foreach (var pointer in new[] { editor, slider, toggle, password, disabled })
                if (pointer != 0)
                    g_object_unref(pointer);
        }
    }
}
