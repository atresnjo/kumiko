using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;

namespace Kumiko.Platform;

internal sealed unsafe class LinuxApp : IAppBackend
{
    readonly App app;
    internal LinuxApp(App app)
    {
        this.app = app;
        if (Gtk.gtk_init_check(0, 0) == 0)
            throw new InvalidOperationException("GTK 3 needs a display. Use Xvfb for headless native-window tests, or Tester for display-free views.");
    }

    public IWindowBackend CreateWindow(Window window, PageOptions? page)
    {
        var backend = new LinuxWindow(window);
        try
        {
            if (page is not null)
            {
                window.Page = new Page(window, page, backend.NativeHandle);
                window.Page.Ready += window.NativePageReady;
            }
            return backend;
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }
    public void Dispatch(Action action) => Gtk.g_idle_add_full(0, &Invoke, GCHandle.ToIntPtr(GCHandle.Alloc(action)), 0);
    [UnmanagedCallersOnly]
    static int Invoke(nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var action = (Action)handle.Target!;
        handle.Free();
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    static int Wake(nint data) => 0;
    public void Run() => Gtk.gtk_main();
    public void RunFor(TimeSpan duration)
    {
        var stop = Stopwatch.StartNew();
        uint wake = Gtk.g_timeout_add((uint)Math.Max(1, duration.TotalMilliseconds), &Wake, 0);
        while (stop.Elapsed < duration)
            Gtk.g_main_context_iteration(0, 1);
        // The one-shot timeout removes itself; no source survives this call.
        GC.KeepAlive(wake);
    }

    public void Quit()
    {
        if (Gtk.gtk_main_level() > 0)
            Gtk.gtk_main_quit();
    }

    public void Activate()
    {
        if (app.Windows.FirstOrDefault() is { } window)
            window.Focus();
    }

    public void Dispose()
    {
    }
}

internal sealed unsafe partial class LinuxWindow : IWindowBackend
{
    static readonly Dictionary<nint, LinuxWindow> instances = new();
    readonly Window owner;
    readonly nint window, view, im;
    readonly LinuxAccessibility accessibility;
    LinuxGpu? gpu;
    OffscreenGpu? captureGpu;
    readonly CpuCanvas canvas = new();
    bool gpuFailed;
    public string Renderer { get; private set; } = "OpenGL (pending)";

    Pixels? pixels;
    bool disposed, maximized;
    bool imeFocused;
    public bool IsVisible => Gtk.gtk_widget_get_visible(window) != 0;

    internal LinuxWindow(Window owner)
    {
        this.owner = owner;
        gpuFailed = Environment.GetEnvironmentVariable("KUMIKO_RENDERER") == "cpu";
        if (gpuFailed)
            Renderer = "Software (KUMIKO_RENDERER=cpu)";
        var o = owner.Options;
        window = Gtk.gtk_window_new(0);
        view = LinuxAccessibility.CreateView();
        accessibility = new(owner, view);
        im = Gtk.gtk_im_multicontext_new();
        instances[window] = instances[view] = instances[im] = this;
        Gtk.gtk_window_set_title(window, o.Title);
        Gtk.gtk_window_set_default_size(window, (int)o.Width, (int)o.Height);
        Gtk.gtk_window_set_resizable(window, o.Resizable ? 1 : 0);
        Gtk.gtk_window_set_decorated(window, o.Frameless ? 0 : 1);
        if (o.HiddenTitleBar && !o.Frameless)
        {
            var header = gtk_header_bar_new();
            gtk_header_bar_set_show_close_button(header, 1);
            gtk_header_bar_set_has_subtitle(header, 0);
            gtk_window_set_titlebar(window, header);
            Gtk.gtk_widget_show(header);
        }

        Gtk.gtk_window_set_keep_above(window, o.AlwaysOnTop ? 1 : 0);
        if (o.X is { } x && o.Y is { } y)
            Gtk.gtk_window_move(window, (int)x, (int)y);
        var container = gtk_box_new(1, 0);
        Desktop.Menu.LinuxContainers[window] = container;
        Gtk.gtk_container_add(window, container);
        gtk_box_pack_start(container, view, 1, 1, 0);
        Gtk.gtk_widget_show(container);
        Gtk.gtk_widget_set_can_focus(view, 1);
        Gtk.gtk_widget_add_events(view, (1 << 2) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 14) | (1 << 21) | (1 << 23));
        Connect(window, "delete-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&Delete);
        Connect(view, "draw", (nint)(delegate* unmanaged<nint, nint, nint, int>)&Draw);
        Connect(view, "size-allocate", (nint)(delegate* unmanaged<nint, nint, nint, void>)&ResizeEvent);
        Connect(view, "button-press-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&Press);
        Connect(view, "button-release-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&Release);
        Connect(view, "motion-notify-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&Motion);
        Connect(view, "scroll-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&Scroll);
        Connect(view, "key-press-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&KeyPress);
        Connect(view, "key-release-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&KeyRelease);
        Connect(view, "focus-out-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&FocusOut);
        Connect(window, "configure-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&ShellChanged);
        Connect(window, "window-state-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&WindowStateChanged);
        Connect(window, "focus-in-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&ShellChanged);
        Connect(window, "focus-out-event", (nint)(delegate* unmanaged<nint, nint, nint, int>)&ShellChanged);
        AcceptFileDrops();
        Connect(im, "commit", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Commit);
        Connect(im, "preedit-changed", (nint)(delegate* unmanaged<nint, nint, void>)&Preedit);
        if (!owner.App.Options.Hidden && owner.Context is { } c)
        {
            c.ReadClipboard = Desktop.Clipboard.ReadText;
            c.WriteClipboard = Desktop.Clipboard.WriteText;
            c.ReadRichClipboard = static () => Desktop.Clipboard.Read();
            c.WriteRichClipboard = static data => Desktop.Clipboard.Write(data);
        }

        Configure(o);
        requested = new(o.X ?? 0, o.Y ?? 0, o.Width, o.Height);
        if (o.Visible && !owner.App.Options.Hidden)
            Show();
        else
            Gtk.gtk_widget_show(view);
    }

    static void Connect(nint obj, string name, nint callback) => Gtk.g_signal_connect_data(obj, name, callback, 0, 0, 0);
    static void Guard(nint self, Action<LinuxWindow> action)
    {
        if (!instances.TryGetValue(self, out var w) || w.disposed)
            return;
        try
        {
            action(w);
        }
        catch (Exception ex)
        {
            w.owner.App.Report(ex);
        }
    }

    [UnmanagedCallersOnly]
    static int Delete(nint self, nint e, nint data)
    {
        Guard(self, w =>
        {
            if (w.owner.CanNativeClose())
                w.owner.NativeClosed();
        });
        return 1;
    }

    [UnmanagedCallersOnly]
    static int Draw(nint self, nint cairo, nint data)
    {
        Guard(self, w =>
        {
            if (w.owner.Context is not { } context)
                return;
            if (!w.gpuFailed)
            {
                try
                {
                    w.gpu ??= new(self);
                    w.gpu.Draw(context, cairo, self);
                    w.Renderer = "OpenGL / " + w.gpu.Driver;
                    return;
                }
                catch (Exception ex)
                {
                    w.gpu?.Dispose();
                    w.gpu = null;
                    w.gpuFailed = true;
                    w.Renderer = "Software fallback: " + ex.Message;
                }
            }

            var pixels = w.pixels = w.canvas.Render(context);
            float scale = Math.Max(1, Gtk.gtk_widget_get_scale_factor(w.view));
            fixed (byte* p = pixels.Bytes)
            {
                var surface = Gtk.cairo_image_surface_create_for_data(p, 0, pixels.Width, pixels.Height, pixels.Width * 4);
                Gtk.cairo_scale(cairo, 1 / scale, 1 / scale);
                Gtk.cairo_set_source_surface(cairo, surface, 0, 0);
                Gtk.cairo_paint(cairo);
                Gtk.cairo_surface_destroy(surface);
            }
        });
        return 0;
    }

    static readonly nint uriList = Marshal.StringToCoTaskMemUTF8("text/uri-list");
    /// <summary>Accepts files dropped from file managers as text/uri-list; GTK finishes the drop for us.</summary>
    void AcceptFileDrops()
    {
        var target = stackalloc nint[2];
        target[0] = uriList;
        target[1] = 0;
        Gtk.gtk_drag_dest_set(view, 7, (nint)target, 1, 2);
        Connect(view, "drag-data-received", (nint)(delegate* unmanaged<nint, nint, int, int, nint, uint, uint, nint, void>)&DragReceived);
    }

    [UnmanagedCallersOnly]
    static void DragReceived(nint self, nint context, int x, int y, nint selection, uint info, uint time, nint data) => Guard(self, w =>
    {
        var uris = Gtk.gtk_selection_data_get_uris(selection);
        if (uris == 0)
            return;
        var files = new List<string>();
        try
        {
            for (var i = 0; ((nint*)uris)[i] != 0; i++)
            {
                var path = Gtk.g_filename_from_uri(((nint*)uris)[i], 0, 0);
                if (path == 0)
                    continue;
                files.Add(Marshal.PtrToStringUTF8(path) ?? "");
                Gtk.g_free(path);
            }
        }
        finally
        {
            Gtk.g_strfreev(uris);
        }

        if (files.Count > 0)
            w.owner.Input(i =>
            {
                i.Pointer = new(x, y);
                i.DragFiles = null;
                i.DroppedFiles = [.. files];
            });
    });
    [UnmanagedCallersOnly]
    static void ResizeEvent(nint self, nint allocation, nint data) => Guard(self, w => w.owner.ResizeFromNative(Gtk.gtk_widget_get_allocated_width(self), Gtk.gtk_widget_get_allocated_height(self), Gtk.gtk_widget_get_scale_factor(self)));
    [UnmanagedCallersOnly]
    static int Press(nint self, nint e, nint data)
    {
        var ev = *(Gtk.ButtonEvent*)e;
        if (ev.Button > 3)
        {
            // GDK also sends double/triple-click notifications after the actual button press.
            if (ev.Type == 4 && ev.Button is 8 or 9)
                Guard(self, w => w.owner.Input(i => i.Navigation = ev.Button == 8 ? NavigationDirection.Back : NavigationDirection.Forward));
            return 1;
        }

        Guard(self, w =>
        {
            Gtk.gtk_widget_grab_focus(w.view);
            w.owner.Input(i =>
            {
                i.Pointer = new((float)ev.X, (float)ev.Y);
                i.Down = i.Pressed = true;
                i.Button = ev.Button == 3 ? PointerButton.Secondary : ev.Button == 2 ? PointerButton.Middle : PointerButton.Primary;
                i.ClickCount = ev.Type == 5 ? 2 : 1;
                i.Modifiers = ButtonMods(ev.State);
            });
        });
        return 1;
    }

    [UnmanagedCallersOnly]
    static int Release(nint self, nint e, nint data)
    {
        var ev = *(Gtk.ButtonEvent*)e;
        if (ev.Button > 3)
            return 1;
        Guard(self, w => w.owner.Input(i =>
        {
            i.Pointer = new((float)ev.X, (float)ev.Y);
            i.Down = false;
            i.Released = true;
            i.Button = ev.Button == 3 ? PointerButton.Secondary : ev.Button == 2 ? PointerButton.Middle : PointerButton.Primary;
            i.Modifiers = ButtonMods(ev.State);
        }));
        return 1;
    }

    static Modifiers ButtonMods(uint state) => ((state & 1) != 0 ? Modifiers.Shift : 0) | ((state & 4) != 0 ? Modifiers.Control : 0) | ((state & 8) != 0 ? Modifiers.Alt : 0);
    [UnmanagedCallersOnly]
    static int Motion(nint self, nint e, nint data)
    {
        var ev = *(Gtk.ButtonEvent*)e;
        Guard(self, w => w.owner.Input(i => i.Pointer = new((float)ev.X, (float)ev.Y)));
        return 1;
    }

    [UnmanagedCallersOnly]
    static int Scroll(nint self, nint e, nint data)
    {
        var ev = *(Gtk.ScrollEvent*)e;
        Guard(self, w => w.owner.Input(i =>
        {
            i.Pointer = new((float)ev.X, (float)ev.Y);
            i.ScrollY = ev.Direction == 4 ? (float)ev.DeltaY * 40 : ev.Direction == 0 ? -40 : ev.Direction == 1 ? 40 : 0;
            i.ScrollX = ev.Direction == 4 ? (float)ev.DeltaX * 40 : ev.Direction == 2 ? -40 : ev.Direction == 3 ? 40 : 0;
            // GDK_SCROLL_SMOOTH (4) carries touchpad deltas; discrete wheel clicks glide.
            i.ScrollPrecise = ev.Direction == 4;
            i.Modifiers = ButtonMods(ev.State);
        }));
        return 1;
    }

    /// <summary>GDK reports the state before the event, so a modifier key's own bit is applied here.</summary>
    static Modifiers? Held(Gtk.KeyEvent ev, bool pressed)
    {
        Modifiers own = ev.Key switch
        {
            0xffe1 or 0xffe2 => Modifiers.Shift,
            0xffe3 or 0xffe4 => Modifiers.Control,
            0xffe9 or 0xffea => Modifiers.Alt,
            _ => Modifiers.None
        };
        if (own == Modifiers.None)
            return null;
        var state = ButtonMods(ev.State);
        return pressed ? state | own : state & ~own;
    }

    [UnmanagedCallersOnly]
    static int KeyRelease(nint self, nint e, nint data)
    {
        var ev = *(Gtk.KeyEvent*)e;
        if (Held(ev, false) is { } held)
            Guard(self, w => w.owner.Input(i => i.HeldModifiers = held));
        return 0;
    }

    /// <summary>Releases arrive in another window after focus leaves.</summary>
    [UnmanagedCallersOnly]
    static int FocusOut(nint self, nint e, nint data)
    {
        Guard(self, w => w.owner.Input(i => i.HeldModifiers = Modifiers.None));
        return 0;
    }

    [UnmanagedCallersOnly]
    static int KeyPress(nint self, nint e, nint data)
    {
        var ev = *(Gtk.KeyEvent*)e;
        // Input methods still receive modifier presses, for example Shift toggling an input mode.
        if (Held(ev, true) is { } pressedModifiers)
            Guard(self, w => w.owner.Input(i => i.HeldModifiers = pressedModifiers));
        Guard(self, w =>
        {
            var key = ev.Key switch
            {
                0xff0d => Key.Enter,
                0xff09 => Key.Tab,
                0xff1b => Key.Escape,
                0xff08 => Key.Backspace,
                0xffff => Key.Delete,
                0xff51 => Key.Left,
                0xff53 => Key.Right,
                0xff52 => Key.Up,
                0xff54 => Key.Down,
                0xff50 => Key.Home,
                0xff57 => Key.End,
                0xff55 => Key.PageUp,
                0xff56 => Key.PageDown,
                0xffbf => Key.F2,
                0xffc7 => Key.F10,
                32 => Key.Space,
                97 => Key.A,
                99 => Key.C,
                120 => Key.X,
                118 => Key.V,
                122 => Key.Z,
                121 => Key.Y,
                98 or 66 => Key.B,
                103 or 71 => Key.G,
                107 or 75 => Key.K,
                108 or 76 => Key.L,
                111 or 79 => Key.O,
                114 or 82 => Key.R,
                115 or 83 => Key.S,
                116 or 84 => Key.T,
                119 or 87 => Key.W,
                112 or 80 => Key.P,
                102 or 70 => Key.F,
                105 or 73 => Key.I,
                117 or 85 => Key.U,
                101 or 69 => Key.E,
                113 or 81 => Key.Q,
                49 => Key.D1,
                50 => Key.D2,
                51 => Key.D3,
                52 => Key.D4,
                53 => Key.D5,
                54 => Key.D6,
                55 => Key.D7,
                56 => Key.D8,
                57 => Key.D9,
                48 => Key.D0,
                110 or 78 => Key.N,
                92 or 124 => Key.Backslash,
                91 or 123 => Key.BracketLeft,
                93 or 125 => Key.BracketRight,
                100 or 68 => Key.D,
                _ => Key.None
            };
            var mods = ((ev.State & 1) != 0 ? Modifiers.Shift : 0) | ((ev.State & 4) != 0 ? Modifiers.Control : 0) | ((ev.State & 8) != 0 ? Modifiers.Alt : 0);
            bool editing = w.owner.Context?.Edits.ContainsKey(w.owner.Context.Focus) == true;
            if ((mods & Modifiers.Control) != 0 || (key != Key.None && key is not (Key.Space or Key.A or Key.C or Key.X or Key.V or Key.Z or Key.Y or Key.P or Key.B or Key.G or Key.K or Key.L or Key.O or Key.R or Key.S or Key.T or Key.W or Key.F or Key.I or Key.Q or Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6 or Key.D7 or Key.D8 or Key.D9 or Key.D0 or Key.N or Key.Backslash or Key.BracketLeft or Key.BracketRight or Key.D or Key.U or Key.E)))
                w.owner.Input(i =>
                {
                    i.Key = key;
                    i.Modifiers = mods;
                    i.HeldModifiers = mods;
                });
            else if (key == Key.Space && !editing)
                w.owner.Input(i => i.Key = Key.Space);
            else
            {
                Gtk.gtk_im_context_set_client_window(w.im, Gtk.gtk_widget_get_window(w.view));
                Gtk.gtk_im_context_filter_keypress(w.im, e);
            }
        });
        return 1;
    }

    [UnmanagedCallersOnly]
    static void Commit(nint self, nint text, nint data) => Guard(self, w => w.owner.Input(i =>
    {
        i.Composition = Marshal.PtrToStringUTF8(text) ?? "";
        i.CommitComposition = true;
    }));
    [UnmanagedCallersOnly]
    static void Preedit(nint self, nint data) => Guard(self, w =>
    {
        Gtk.gtk_im_context_get_preedit_string(self, out var text, out var attrs, out _);
        try
        {
            var value = Marshal.PtrToStringUTF8(text) ?? "";
            w.owner.Input(i => i.Composition = value);
        }
        finally
        {
            Gtk.g_free(text);
            if (attrs != 0)
                Gtk.pango_attr_list_unref(attrs);
        }
    });
    public void Present(Context context)
    {
        accessibility.Update(context);
        if (context.IsEditing != imeFocused)
        {
            imeFocused = context.IsEditing;
            if (imeFocused)
                Gtk.gtk_im_context_focus_in(im);
            else
                Gtk.gtk_im_context_focus_out(im);
        }

        if (context.CaretRectangle is { } caret)
        {
            var rect = new Gtk.CursorRect
            {
                X = (int)caret.X,
                Y = (int)caret.Y,
                Width = 1,
                Height = (int)caret.Height
            };
            Gtk.gtk_im_context_set_cursor_location(im, ref rect);
        }

        Gtk.gtk_widget_queue_draw(view);
    }

    public Pixels Capture(Context c)
    {
        if (gpuFailed)
            return canvas.Capture(c);
        try
        {
            captureGpu ??= new();
            var pixels = captureGpu.Capture(c);
            Renderer = "OpenGL / " + captureGpu.Driver;
            return pixels;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Renderer = "Software fallback: " + ex.Message;
            return canvas.Capture(c);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        DisposeShell();
        accessibility.Dispose();
        gpu?.Dispose();
        captureGpu?.Dispose();
        instances.Remove(window);
        instances.Remove(view);
        instances.Remove(im);
        Desktop.Menu.LinuxContainers.Remove(window);
        Gtk.gtk_widget_destroy(window);
        Gtk.g_object_unref(im);
    }

    [DllImport(Gtk.Library)]
    static extern nint gtk_box_new(int orientation, int spacing);
    [DllImport(Gtk.Library)]
    static extern void gtk_box_pack_start(nint box, nint child, int expand, int fill, uint padding);
}
