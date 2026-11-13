using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko.Web;

/// <summary>WebKitGTK 4.1 (GTK3) adapter. Runtime libraries/codecs must be installed by the host application.</summary>
internal sealed unsafe class LinuxPageAdapter : IPageAdapter
{
    const string WebKit = "libwebkit2gtk-4.1.so.0";
    const string Jsc = "libjavascriptcoregtk-4.1.so.0";
    const string GtkLib = "libgtk-3.so.0";
    const string Gio = "libgio-2.0.so.0";
    const string GLib = "libglib-2.0.so.0";
    const string GObject = "libgobject-2.0.so.0";
    readonly Page page;
    nint webview, manager, context, fixedLayer;
    GCHandle handle;
    nuint signal, navigationSignal, failureSignal;
    long navigationId;
    string? navigationUri, committedUri;
    bool retainedSource;
    bool disposed, registered;
    internal LinuxPageAdapter(Page page, nint window)
    {
        this.page = page;
        handle = GCHandle.Alloc(this);
        try
        {
            context = page.Owner.App.Options.Hidden ? webkit_web_context_new_ephemeral() : webkit_web_context_new();
            manager = webkit_user_content_manager_new();
            webview = g_object_new(webkit_web_view_get_type(), "web-context", context, "user-content-manager", manager, 0);
            g_object_ref_sink(webview);
            webkit_web_context_register_uri_scheme(context, "kumiko", &Serve, GCHandle.ToIntPtr(handle), (nint)(delegate* unmanaged<nint, void>)&Released);
            registered = true;
            if (webkit_user_content_manager_register_script_message_handler(manager, "kumiko") == 0)
                throw new IOException("Could not register WebKit message channel.");
            failureSignal = g_signal_connect_data(webview, "load-failed", (nint)(delegate* unmanaged<nint, int, nint, nint, nint, int>)&Failed, GCHandle.ToIntPtr(handle), 0, 0);
            navigationSignal = g_signal_connect_data(webview, "load-changed", (nint)(delegate* unmanaged<nint, int, nint, void>)&Navigation, GCHandle.ToIntPtr(handle), 0, 0);
            signal = g_signal_connect_data(manager, "script-message-received::kumiko", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Message, GCHandle.ToIntPtr(handle), 0, 0);
            nint script = webkit_user_script_new(page.Script, 1, 0, 0, 0);
            try
            {
                webkit_user_content_manager_add_script(manager, script);
            }
            finally
            {
                webkit_user_script_unref(script);
            }
            if (!Desktop.Menu.LinuxContainers.TryGetValue(window, out nint container))
                throw new InvalidOperationException("Missing GTK window container.");
            if (page.Owner.Context is null)
            {
                nint children = gtk_container_get_children(container);
                if (children != 0)
                {
                    nint child = Marshal.ReadIntPtr(children);
                    gtk_widget_set_no_show_all(child, 1);
                    gtk_widget_hide(child);
                    g_list_free(children);
                }
            }
            if (page.Owner.Context is not null)
            {
                fixedLayer = page.Owner.LinuxWebLayer;
                if (fixedLayer == 0)
                {
                    nint children = gtk_container_get_children(container);
                    if (children == 0)
                        throw new InvalidOperationException("Missing native drawing surface.");
                    nint view = Marshal.ReadIntPtr(children);
                    g_list_free(children);
                    g_object_ref(view);
                    gtk_container_remove(container, view);
                    nint overlay = gtk_overlay_new();
                    gtk_container_add(overlay, view);
                    g_object_unref(view);
                    fixedLayer = gtk_fixed_new();
                    gtk_widget_set_hexpand(fixedLayer, 1);
                    gtk_widget_set_vexpand(fixedLayer, 1);
                    gtk_overlay_add_overlay(overlay, fixedLayer);
                    gtk_box_pack_start(container, overlay, 1, 1, 0);
                    gtk_widget_show(overlay);
                    gtk_widget_show(fixedLayer);
                    page.Owner.LinuxWebLayer = fixedLayer;
                }
                gtk_fixed_put(fixedLayer, webview, 0, 0);
            }
            else
                gtk_box_pack_start(container, webview, 1, 1, 0);
            gtk_widget_show(webview);
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    [UnmanagedCallersOnly]
    static void Released(nint data) => GCHandle.FromIntPtr(data).Free();
    [UnmanagedCallersOnly]
    static void Message(nint manager, nint result, nint user)
    {
        try
        {
            var adapter = (LinuxPageAdapter)GCHandle.FromIntPtr(user).Target!;
            if (adapter.disposed)
                return;
            nint value = webkit_javascript_result_get_js_value(result), text = jsc_value_to_string(value);
            try
            {
                string source = adapter.retainedSource ? adapter.committedUri ?? "" : Marshal.PtrToStringUTF8(webkit_web_view_get_uri(adapter.webview)) ?? "";
                adapter.page.NativeMessage(Marshal.PtrToStringUTF8(text) ?? "", source);
            }
            finally
            {
                g_free(text);
            }
        }
        catch (Exception error)
        {
            try
            {
                ((LinuxPageAdapter)GCHandle.FromIntPtr(user).Target!).page.NativeError(error);
            }
            catch { }
        }
    }
    [UnmanagedCallersOnly]
    static void Navigation(nint view, int loadEvent, nint user)
    {
        var adapter = (LinuxPageAdapter)GCHandle.FromIntPtr(user).Target!;
        try
        {
            if (!adapter.disposed && loadEvent == 0)
            {
                adapter.retainedSource = false;
                adapter.navigationUri = Marshal.PtrToStringUTF8(webkit_web_view_get_uri(view));
                adapter.page.NativeNavigationStarted(++adapter.navigationId);
            }
            else if (!adapter.disposed && loadEvent == 1)
                adapter.navigationUri = Marshal.PtrToStringUTF8(webkit_web_view_get_uri(view));
            else if (!adapter.disposed && loadEvent == 2)
                adapter.committedUri = Marshal.PtrToStringUTF8(webkit_web_view_get_uri(view));
        }
        catch (Exception error)
        {
            adapter.page.NativeError(error);
        }
    }
    [UnmanagedCallersOnly]
    static int Failed(nint view, int loadEvent, nint uri, nint error, nint user)
    {
        var adapter = (LinuxPageAdapter)GCHandle.FromIntPtr(user).Target!;
        try
        {
            if (!adapter.disposed && loadEvent == 0 && Marshal.PtrToStringUTF8(uri) == adapter.navigationUri)
            {
                // get_uri can still name the failed provisional target, not this retained DOM.
                adapter.retainedSource = true;
                adapter.page.NativeNavigationFailed(adapter.navigationId);
                return 1; // Preserve the retained DOM instead of replacing it with GTK's stock error page.
            }
        }
        catch (Exception failure)
        {
            adapter.page.NativeError(failure);
        }
        return 0;
    }
    [UnmanagedCallersOnly]
    static void Serve(nint request, nint user)
    {
        try
        {
            var adapter = (LinuxPageAdapter)GCHandle.FromIntPtr(user).Target!;
            if (adapter.disposed)
                return;
            string path = Marshal.PtrToStringUTF8(webkit_uri_scheme_request_get_path(request)) ?? "/";
            var asset = adapter.page.FindAsset(path);
            byte[] bytes = asset?.Data ?? "Not found"u8.ToArray();
            nint data = g_malloc((nuint)bytes.Length);
            Marshal.Copy(bytes, 0, data, bytes.Length);
            nint stream = g_memory_input_stream_new_from_data(data, bytes.Length, (nint)(delegate* unmanaged<nint, void>)&Free);
            try
            {
                webkit_uri_scheme_request_finish(request, stream, bytes.Length, asset?.Mime ?? Mime(path));
            }
            finally
            {
                g_object_unref(stream);
            }
        }
        catch (Exception error)
        {
            try
            {
                ((LinuxPageAdapter)GCHandle.FromIntPtr(user).Target!).page.NativeError(error);
            }
            catch { }
        }
    }
    [UnmanagedCallersOnly]
    static void Free(nint data) => g_free(data);
    static string Mime(string path) => System.IO.Path.GetExtension(path) switch { ".js" => "text/javascript", ".css" => "text/css", ".png" => "image/png", ".json" => "application/json", _ => "text/html" };
    public string Url
    {
        get
        {
            nint item = webkit_back_forward_list_get_current_item(webkit_web_view_get_back_forward_list(webview));
            return Marshal.PtrToStringUTF8(item == 0 ? webkit_web_view_get_uri(webview) : webkit_back_forward_list_item_get_uri(item)) ?? "";
        }
    }
    public void Stop() => webkit_web_view_stop_loading(webview);
    public void Load(string url) => webkit_web_view_load_uri(webview, url);
    public void Evaluate(string script) => webkit_web_view_evaluate_javascript(webview, script, -1, 0, 0, 0, 0, 0);
    public void Place(Rect bounds, bool visible)
    {
        if (fixedLayer != 0)
            gtk_fixed_move(fixedLayer, webview, (int)bounds.X, (int)bounds.Y);
        gtk_widget_set_size_request(webview, (int)bounds.Width, (int)bounds.Height);
        gtk_widget_set_no_show_all(webview, visible ? 0 : 1);
        if (visible)
            gtk_widget_show(webview);
        else
            gtk_widget_hide(webview);
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (manager != 0)
        {
            if (signal != 0)
                g_signal_handler_disconnect(manager, signal);
            webkit_user_content_manager_unregister_script_message_handler(manager, "kumiko");
        }
        if (webview != 0)
        {
            if (navigationSignal != 0)
                g_signal_handler_disconnect(webview, navigationSignal);
            if (failureSignal != 0)
                g_signal_handler_disconnect(webview, failureSignal);
            webkit_web_view_stop_loading(webview);
            gtk_widget_destroy(webview);
            g_object_unref(webview);
        }
        if (manager != 0)
            g_object_unref(manager);
        if (context != 0)
            g_object_unref(context);
        if (!registered && handle.IsAllocated)
            handle.Free();
    }
    [DllImport(GObject)] static extern nint g_object_ref(nint value);
    [DllImport(GtkLib)] static extern nint gtk_overlay_new();
    [DllImport(GtkLib)] static extern nint gtk_fixed_new();
    [DllImport(GtkLib)] static extern void gtk_overlay_add_overlay(nint overlay, nint child);
    [DllImport(GtkLib)] static extern void gtk_container_add(nint container, nint child);
    [DllImport(GtkLib)] static extern void gtk_container_remove(nint container, nint child);
    [DllImport(GtkLib)] static extern void gtk_fixed_put(nint container, nint child, int x, int y);
    [DllImport(GtkLib)] static extern void gtk_fixed_move(nint container, nint child, int x, int y);
    [DllImport(GtkLib)] static extern void gtk_widget_set_hexpand(nint child, int expand);
    [DllImport(GtkLib)] static extern void gtk_widget_set_vexpand(nint child, int expand);
    [DllImport(WebKit)] static extern nint webkit_web_context_new();
    [DllImport(WebKit)] static extern nint webkit_web_context_new_ephemeral();
    [DllImport(WebKit)] static extern nint webkit_user_content_manager_new();
    [DllImport(WebKit)] static extern nuint webkit_web_view_get_type();
    [DllImport(WebKit)] static extern void webkit_web_context_register_uri_scheme(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroy);
    [DllImport(WebKit)] static extern int webkit_user_content_manager_register_script_message_handler(nint manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(WebKit)] static extern void webkit_user_content_manager_unregister_script_message_handler(nint manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(WebKit)] static extern nint webkit_user_script_new([MarshalAs(UnmanagedType.LPUTF8Str)] string source, int frames, int time, nint allow, nint block);
    [DllImport(WebKit)] static extern void webkit_user_content_manager_add_script(nint manager, nint script);
    [DllImport(WebKit)] static extern void webkit_user_script_unref(nint script);
    [DllImport(WebKit)] static extern nint webkit_javascript_result_get_js_value(nint result);
    [DllImport(WebKit)] static extern nint webkit_web_view_get_uri(nint view);
    [DllImport(WebKit)] static extern nint webkit_web_view_get_back_forward_list(nint view);
    [DllImport(WebKit)] static extern nint webkit_back_forward_list_get_current_item(nint list);
    [DllImport(WebKit)] static extern nint webkit_back_forward_list_item_get_uri(nint item);
    [DllImport(WebKit)] static extern void webkit_web_view_load_uri(nint view, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri);
    [DllImport(WebKit)] static extern void webkit_web_view_evaluate_javascript(nint view, [MarshalAs(UnmanagedType.LPUTF8Str)] string script, nint length, nint world, nint source, nint cancel, nint callback, nint data);
    [DllImport(WebKit)] static extern void webkit_web_view_stop_loading(nint view);
    [DllImport(WebKit)] static extern nint webkit_uri_scheme_request_get_path(nint request);
    [DllImport(WebKit)] static extern void webkit_uri_scheme_request_finish(nint request, nint stream, long length, [MarshalAs(UnmanagedType.LPUTF8Str)] string mime);
    [DllImport(Jsc)] static extern nint jsc_value_to_string(nint value);
    [DllImport(GObject)] static extern nint g_object_new(nuint type, [MarshalAs(UnmanagedType.LPUTF8Str)] string first, nint a, [MarshalAs(UnmanagedType.LPUTF8Str)] string second, nint b, nint end);
    [DllImport(GObject)] static extern nint g_object_ref_sink(nint value);
    [DllImport(GObject)] static extern void g_object_unref(nint value);
    [DllImport(GObject)] static extern nuint g_signal_connect_data(nint value, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint callback, nint data, nint destroy, int flags);
    [DllImport(GObject)] static extern void g_signal_handler_disconnect(nint value, nuint signal);
    [DllImport(GtkLib)] static extern void gtk_box_pack_start(nint box, nint child, int expand, int fill, uint padding);
    [DllImport(GtkLib)] static extern void gtk_widget_show(nint widget);
    [DllImport(GtkLib)] static extern void gtk_widget_hide(nint widget);
    [DllImport(GtkLib)] static extern void gtk_widget_set_no_show_all(nint widget, int noShow);
    [DllImport(GtkLib)] static extern void gtk_widget_destroy(nint widget);
    [DllImport(GtkLib)] static extern void gtk_widget_set_size_request(nint widget, int width, int height);
    [DllImport(GtkLib)] static extern nint gtk_container_get_children(nint container);
    [DllImport(GLib)] static extern void g_list_free(nint list);
    [DllImport(GLib)] static extern nint g_malloc(nuint length);
    [DllImport(GLib)] static extern void g_free(nint value);
    [DllImport(Gio)] static extern nint g_memory_input_stream_new_from_data(nint bytes, nint length, nint destroy);
}
