using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;

namespace Kumiko.Platform;

internal sealed class WindowsApp : IAppBackend
{
    readonly uint thread = Win32.GetCurrentThreadId();
    readonly ConcurrentQueue<Action> queue = new();
    readonly App app;
    bool quit;
    readonly bool rejoinMultithreaded;
    const uint DispatchMessage = 0x8001;
    const int ChangedMode = unchecked((int)0x80010106);
    const uint MultithreadedApartment = 0;
    internal WindowsApp(App app)
    {
        this.app = app;
        rejoinMultithreaded = EnterSingleThreadedApartment();
        Win32.SetProcessDpiAwarenessContext(-4);
        Win32.PeekMessageW(out _, 0, 0, 0, 0);
    }

    // OLE drag and drop, the clipboard and IME need a single-threaded apartment. .NET joins the main
    // thread to the multithreaded one before Main runs unless Main has [STAThread], which top-level
    // programs can't declare, so leave the runtime's apartment once and let OLE create its own.
    // Returns whether the runtime's apartment was left, so Dispose can give it back.
    static bool EnterSingleThreadedApartment()
    {
        int result = OleInitialize(0);
        if (result != ChangedMode)
        {
            Marshal.ThrowExceptionForHR(result);
            return false;
        }
        CoUninitialize();
        result = OleInitialize(0);
        if (result == ChangedMode)
        {
            CoInitializeEx(0, MultithreadedApartment);
            throw new InvalidOperationException("Kumiko needs a single-threaded COM apartment, but this thread initialized COM as multithreaded. Create the App before initializing COM, or mark Main with [STAThread].");
        }
        Marshal.ThrowExceptionForHR(result);
        return true;
    }

    public IWindowBackend CreateWindow(Window window, PageOptions? page)
    {
        var backend = new WindowsWindow(window);
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
    public void Dispatch(Action action)
    {
        queue.Enqueue(action);
        Win32.PostThreadMessageW(thread, DispatchMessage, 0, 0);
    }

    void Process(ref Win32.Message message)
    {
        if (message.Id == DispatchMessage)
        {
            while (queue.TryDequeue(out var action))
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    app.Report(ex);
                }
        }
        else
        {
            Win32.TranslateMessage(ref message);
            Win32.DispatchMessageW(ref message);
        }
    }

    public void Run()
    {
        quit = false;
        while (!quit && Win32.GetMessageW(out var message, 0, 0, 0) > 0)
            Process(ref message);
    }

    public void RunFor(TimeSpan duration)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed < duration)
        {
            while (Win32.PeekMessageW(out var message, 0, 0, 0, 1) != 0)
                Process(ref message);
            Win32.MsgWaitForMultipleObjects(0, 0, 0, (uint)Math.Max(1, (duration - timer.Elapsed).TotalMilliseconds), 0x04ff);
        }
    }

    public void Quit()
    {
        quit = true;
        Win32.PostThreadMessageW(thread, DispatchMessage, 0, 0);
    }

    public void Activate()
    {
        if (app.Windows.FirstOrDefault() is { } window)
            window.Focus();
    }

    public void Dispose()
    {
        OleUninitialize();
        // Restore the apartment .NET set up, so its own CoUninitialize at exit stays balanced.
        if (rejoinMultithreaded)
            CoInitializeEx(0, MultithreadedApartment);
    }

    [DllImport("ole32.dll")]
    static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")]
    static extern void OleUninitialize();
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(nint reserved, uint model);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
}

internal sealed unsafe partial class WindowsWindow : IWindowBackend
{
    static readonly Dictionary<nint, WindowsWindow> windows = new();
    static ushort registered;
    readonly Window window;
    readonly nint handle;
    uint style, extended;
    readonly WindowsAccessibility accessibility;
    WglContext? gpuContext;
    OpenGl? gpu;
    readonly CpuCanvas canvas = new();
    public string Renderer { get; private set; } = "Software";

    Pixels? pixels;
    bool disposed;
    char highSurrogate;
    public bool IsVisible => Win32.IsWindowVisible(handle) != 0;

    internal WindowsWindow(Window window)
    {
        this.window = window;
        var o = window.Options;
        if (registered == 0)
        {
            var cls = new Win32.WindowClass
            {
                ClassName = "Kumiko.CSharp",
                Procedure = &Procedure,
                Instance = Win32.GetModuleHandleW(null),
                Style = 3 | 8 | 0x20
            };
            registered = Win32.RegisterClassW(ref cls);
            if (registered == 0)
                throw new InvalidOperationException("Win32 window class registration failed.");
        }

        style = o.Frameless ? 0x80000000u : 0x00cf0000u;
        if (!o.Resizable)
            style &= ~(0x40000u | 0x10000u);
        extended = o.AlwaysOnTop ? 8u : 0;
        var r = new Win32.NativeRectangle
        {
            Right = (int)o.Width,
            Bottom = (int)o.Height
        };
        Win32.AdjustWindowRectEx(ref r, style, 0, extended);
        handle = Win32.CreateWindowExW(extended, "Kumiko.CSharp", o.Title, style, o.X is { } x ? (int)x : unchecked((int)0x80000000), o.Y is { } y ? (int)y : unchecked((int)0x80000000), r.Right - r.Left, r.Bottom - r.Top, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (handle == 0)
            throw new InvalidOperationException("Win32 window creation failed.");
        windows[handle] = this;
        if (!window.App.Options.Hidden && window.Context is { } c)
        {
            c.ReadClipboard = Desktop.Clipboard.ReadText;
            c.WriteClipboard = Desktop.Clipboard.WriteText;
            c.ReadRichClipboard = static () => Desktop.Clipboard.Read();
            c.WriteRichClipboard = static data => Desktop.Clipboard.Write(data);
        }

        accessibility = new(window, handle);
        RegisterDrop();
        string? requested = Environment.GetEnvironmentVariable("KUMIKO_RENDERER");
        if (requested == "cpu")
            Renderer = "Software (KUMIKO_RENDERER=cpu)";
        else if (requested is "d3d11" or "warp")
            StartDirect3D(requested == "warp");
        else
            try
            {
                gpuContext = new(handle);
                gpu = new(gpuContext.Load);
                Renderer = "OpenGL / " + gpu.Driver;
            }
            catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            {
                gpuContext?.Dispose();
                gpuContext = null;
                Renderer = "Software fallback: " + ex.Message;
            }

        // CreateWindowExW sizes are physical pixels; apply the logical size at this monitor's DPI.
        Resize(o.Width, o.Height);
        Configure(o);
        if (o.Visible && !window.App.Options.Hidden)
            Show();
    }

    [UnmanagedCallersOnly]
    static nint Procedure(nint handle, uint message, nuint w, nint l)
    {
        if (windows.TryGetValue(handle, out var owner))
        {
            try
            {
                if (owner.Message(message, w, l, out var result))
                    return result;
            }
            catch (Exception ex)
            {
                owner.window.App.Report(ex);
            }
        }

        return Win32.DefWindowProcW(handle, message, w, l);
    }

    bool Message(uint message, nuint w, nint l, out nint result)
    {
        result = 0;
        if (ShellMessage(message, w, l, out result))
            return true;
        if (Ime(message, l))
            return true;
        float scale = Math.Max(1, Win32.GetDpiForWindow(handle) / 96f);
        var p = new Point((short)((long)l & 65535) / scale, (short)(((long)l >> 16) & 65535) / scale);
        switch (message)
        {
            case 0x20b: // WM_XBUTTONDOWN. History input never becomes pointer activation.
            case 0x20d: // WM_XBUTTONDBLCLK is the second press, so it is another history request.
                var extraButton = (w >> 16) & 65535;
                if (extraButton is 1 or 2)
                    window.Input(i => i.Navigation = extraButton == 1 ? NavigationDirection.Back : NavigationDirection.Forward);
                result = 1;
                return true;
            case 0x20c: // WM_XBUTTONUP
                result = 1;
                return true;
            case 0x319: // WM_APPCOMMAND from a browser-history key or mouse driver.
                var command = ((long)l >> 16) & 0xfff;
                if (command is not (1 or 2))
                    return false;
                window.Input(i => i.Navigation = command == 1 ? NavigationDirection.Back : NavigationDirection.Forward);
                result = 1;
                return true;
            case 0x02e0:
                DpiChanged(l);
                return true;
            case 0x0233:
                DropFiles((nint)w);
                return true;
            case 0x3d:
                result = accessibility.GetObject(w, l);
                return true;
            case 0x1a:
            case 0x31a:
                window.SystemPreferencesChanged();
                return false;
            case 0x10:
                if (window.CanNativeClose())
                    window.NativeClosed();
                return true;
            case 5:
                ResizeFromNative();
                return true;
            case 0x0f:
                var dc = Win32.BeginPaint(handle, out var paint);
                if ((gpu is not null || direct3D is not null) && window.Context is { } view)
                    if (!DrawGpu(view, true))
                        pixels = canvas.Render(view);
                if (pixels is { } px)
                {
                    Win32.GetClientRect(handle, out var rect);
                    var info = new Win32.BitmapInfo
                    {
                        Size = 40,
                        Width = px.Width,
                        Height = -px.Height,
                        Planes = 1,
                        Bits = 32
                    };
                    fixed (byte* bytes = px.Bytes)
                        Win32.StretchDIBits(dc, 0, 0, rect.Right, rect.Bottom, 0, 0, px.Width, px.Height, bytes, ref info, 0, 0x00cc0020);
                }

                Win32.EndPaint(handle, ref paint);
                return true;
            case 0x201:
            case 0x204:
                Win32.SetCapture(handle);
                window.Input(i =>
                {
                    i.Pointer = p;
                    i.Down = i.Pressed = true;
                    i.Button = message == 0x204 ? PointerButton.Secondary : PointerButton.Primary;
                    i.ClickCount = 1;
                    i.Modifiers = GetModifiers();
                });
                return true;
            case 0x203:
                Win32.SetCapture(handle);
                window.Input(i =>
                {
                    i.Pointer = p;
                    i.Down = i.Pressed = true;
                    i.ClickCount = 2;
                    i.Button = PointerButton.Primary;
                    i.Modifiers = GetModifiers();
                });
                return true;
            case 0x202:
            case 0x205:
                Win32.ReleaseCapture();
                window.Input(i =>
                {
                    i.Pointer = p;
                    i.Down = false;
                    i.Released = true;
                    i.Button = message == 0x205 ? PointerButton.Secondary : PointerButton.Primary;
                    i.Modifiers = GetModifiers();
                });
                return true;
            case 0x200:
                // Motion waits for the next composition, so a 1000 Hz mouse costs one frame per refresh.
                window.Input(i => i.Pointer = p, coalesce: true);
                return true;
            case 0x20a:
                window.Input(i =>
                {
                    var wheel = (short)((w >> 16) & 65535);
                    // Notches coalesced into one frame add up.
                    i.ScrollY += -wheel / 120f * 40;
                    // Whole notches glide; high-resolution wheels report fractions of a notch and stay direct.
                    i.ScrollPrecise = wheel % 120 != 0;
                    i.Modifiers = GetModifiers();
                }, coalesce: true);
                return true;
            case 0x20e:
                window.Input(i =>
                {
                    var tilt = (short)((w >> 16) & 65535);
                    i.ScrollX += tilt / 120f * 40;
                    i.ScrollPrecise = tilt % 120 != 0;
                    i.Modifiers = GetModifiers();
                }, coalesce: true);
                return true;
            case 0x100: // WM_KEYDOWN
            case 0x104: // WM_SYSKEYDOWN
                return KeyDown(w, l, message == 0x104);
            case 0x101: // WM_KEYUP
            case 0x105: // WM_SYSKEYUP keeps its default handling, such as Alt activating the menu.
                if (w is 16 or 17 or 18)
                {
                    var held = GetModifiers();
                    window.Input(i => i.HeldModifiers = held);
                }

                return false;
            case 0x08: // WM_KILLFOCUS: releases arrive in another window.
                window.Input(i => i.HeldModifiers = Modifiers.None);
                return false;
            case 0x102: // WM_CHAR not consumed by its key-down, such as IME results and VK_PACKET text.
                Character((char)w);
                return true;
            case FrameMessage:
                DeliverFrame();
                return true;
        }

        return false;
    }

    static Modifiers GetModifiers() => (Win32.GetKeyState(16) < 0 ? Modifiers.Shift : 0) | (Win32.GetKeyState(17) < 0 ? Modifiers.Control : 0) | (Win32.GetKeyState(18) < 0 ? Modifiers.Alt : 0);
    void ResizeFromNative()
    {
        Win32.GetClientRect(handle, out var r);
        float scale = Math.Max(1, Win32.GetDpiForWindow(handle) / 96f);
        window.ResizeFromNative(r.Right / scale, r.Bottom / scale, scale);
    }

    public void Show()
    {
        if (window.Options is { Modal: true, Parent: { } parent })
            EnableWindow(parent.NativeHandle, 0);
        Win32.ShowWindow(handle, 5);
    }

    public void Hide()
    {
        Win32.ShowWindow(handle, 0);
        if (window.Options is { Modal: true, Parent: { } parent })
            EnableWindow(parent.NativeHandle, 1);
    }

    public void SetTitle(string title) => Win32.SetWindowTextW(handle, title);
    public void Resize(float width, float height)
    {
        float scale = Math.Max(1, Win32.GetDpiForWindow(handle) / 96f);
        var r = new Win32.NativeRectangle
        {
            Right = (int)(width * scale),
            Bottom = (int)(height * scale)
        };
        Win32.AdjustWindowRectEx(ref r, style, 0, extended);
        Win32.SetWindowPos(handle, 0, 0, 0, r.Right - r.Left, r.Bottom - r.Top, 6);
        ResizeFromNative();
    }

    public void Minimize() => Win32.ShowWindow(handle, 6);
    public void ToggleMaximize() => Win32.ShowWindow(handle, Win32.IsZoomed(handle) != 0 ? 9 : 3);
    public bool BeginMove()
    {
        // The physical primary button is the right button when the user swapped buttons.
        int primary = Win32.GetSystemMetrics(23) != 0 ? 2 : 1;
        if (Win32.GetAsyncKeyState(primary) >= 0 || Win32.GetCursorPos(out var cursor) == 0)
            return false;
        // A caption press hands the held button to the system move loop.
        Win32.ReleaseCapture();
        Win32.PostMessageW(handle, 0x00A1, 2, (nint)(ushort)cursor.X | (nint)((ushort)cursor.Y << 16));
        return true;
    }
    public void Present(Context context)
    {
        accessibility.Update(context);
        PositionIme(context);
        if (!DrawGpu(context, IsVisible))
            pixels = canvas.Render(context);
        Win32.InvalidateRect(handle, 0, 0);
    }

    bool DrawGpu(Context context, bool present)
    {
        if (direct3D is not null)
            return DrawDirect3D(context, present);
        if (gpu is null || gpuContext is null)
            return false;
        try
        {
            gpuContext.MakeCurrent();
            gpu.Render(context, present);
            if (present)
                gpuContext.Swap();
            pixels = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Renderer = "Software fallback: " + ex.Message;
            gpu.Dispose();
            gpuContext.Dispose();
            gpu = null;
            gpuContext = null;
            return false;
        }
    }

    public Pixels Capture(Context c)
    {
        if (direct3D is not null)
            return CaptureDirect3D(c);
        return DrawGpu(c, false) ? gpu!.Readback() : canvas.Capture(c);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        DisposePacer();
        RevokeDrop();
        DisposeShell();
        accessibility.Dispose();
        direct3D?.Dispose();
        if (gpuContext is not null)
        {
            gpuContext.MakeCurrent();
            gpu?.Dispose();
            gpuContext.Dispose();
        }

        windows.Remove(handle);
        Win32.DestroyWindow(handle);
    }
}
