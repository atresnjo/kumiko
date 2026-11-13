using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

internal sealed unsafe class X11GlobalShortcutBackend : IGlobalShortcutBackend
{
    const string X11 = "libX11.so.6";
    readonly App app;
    readonly nint display;
    readonly nuint root;
    readonly Watch watch;
    readonly Dictionary<ShortcutChord, (int Key, uint Mods, Action Callback)> registrations = new();
    static int grabError;
    internal X11GlobalShortcutBackend(App app)
    {
        this.app = app;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            throw new PlatformNotSupportedException("Wayland global shortcuts require an XDG GlobalShortcuts portal session; the portal backend is not implemented.");
        display = XOpenDisplay(0);
        if (display == 0)
            throw new PlatformNotSupportedException("No X11 display is available for global shortcuts.");
        root = XDefaultRootWindow(display);
        var modifiers = XGetModifierMapping(display);
        if (modifiers != 0)
        {
            try
            {
                var map = (ModifierMap*)modifiers;
                byte numLock = XKeysymToKeycode(display, 0xff7f);
                for (int modifier = 0; modifier < 8; modifier++)
                    for (int slot = 0; slot < map->Count; slot++)
                        if (numLock != 0 && map->Keys[modifier * map->Count + slot] == numLock)
                            numLockMask |= 1u << modifier;
            }
            finally
            {
                XFreeModifiermap(modifiers);
            }
        }

        Locks = new uint[]
        {
            0,
            2,
            numLockMask,
            2 | numLockMask
        }.Distinct().ToArray();
        try
        {
            watch = Watch.Readable(app, XConnectionNumber(display), Drain);
        }
        catch
        {
            XCloseDisplay(display);
            throw;
        }
    }

    readonly uint[] Locks;
    uint numLockMask;
    public bool Register(ShortcutChord chord, Action callback)
    {
        var name = chord.Key switch
        {
            "SPACE" => "space",
            "ENTER" => "Return",
            "ESCAPE" or "ESC" => "Escape",
            "PAGEUP" => "Prior",
            "PAGEDOWN" => "Next",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "UP" => "Up",
            "DOWN" => "Down",
            "HOME" => "Home",
            "END" => "End",
            "TAB" => "Tab",
            "DELETE" => "Delete",
            "BACKSPACE" => "BackSpace",
            _ => chord.Key.Length == 1 ? chord.Key.ToLowerInvariant() : chord.Key
        };
        var symbol = XStringToKeysym(name);
        int key = XKeysymToKeycode(display, symbol);
        if (symbol == 0 || key == 0)
            throw new ArgumentException($"No X11 keycode for {chord.Key}.");
        uint mods = (chord.Control ? 4u : 0) | (chord.Alt ? 8u : 0) | (chord.Shift ? 1u : 0) | (chord.Meta ? 64u : 0);
        XSync(display, 0);
        grabError = 0;
        var previous = XSetErrorHandler((nint)(delegate* unmanaged<nint, ErrorEvent*, int>)&Error);
        try
        {
            foreach (uint locks in Locks)
                XGrabKey(display, key, mods | locks, root, 0, 1, 1);
            XSync(display, 0);
        }
        finally
        {
            XSetErrorHandler(previous);
        }

        if (grabError != 0)
        {
            foreach (uint locks in Locks)
                XUngrabKey(display, key, mods | locks, root);
            XFlush(display);
            if (grabError == 10)
                return false; // BadAccess: another client owns the chord.
            throw new InvalidOperationException($"XGrabKey failed with X11 error {grabError}.");
        }

        registrations.Add(chord, (key, mods, callback));
        XFlush(display);
        return true;
    }

    void Drain()
    {
        long* ev = stackalloc long[24];
        while (XPending(display) > 0)
        {
            XNextEvent(display, (nint)ev);
            if (*(int*)ev != 2)
                continue;
            var key = (KeyEvent*)ev;
            uint modifiers = key->State & ~(2u | numLockMask);
            foreach (var registration in registrations.Values.ToArray())
                if (registration.Key == key->KeyCode && registration.Mods == modifiers)
                    registration.Callback();
        }
    }

    public void Unregister(ShortcutChord chord)
    {
        if (!registrations.Remove(chord, out var registration))
            return;
        foreach (uint locks in Locks)
            XUngrabKey(display, registration.Key, registration.Mods | locks, root);
        XFlush(display);
    }

    public void Dispose()
    {
        app.CheckThread();
        watch.Dispose();
        foreach (var chord in registrations.Keys.ToArray())
            Unregister(chord);
        XCloseDisplay(display);
    }

    [UnmanagedCallersOnly]
    static int Error(nint display, ErrorEvent* ev)
    {
        grabError = ev->Code;
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ErrorEvent
    {
        public int Type;
        public nint Display;
        public nuint Resource, Serial;
        public byte Code, Request, Minor;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KeyEvent
    {
        public int Type;
        public nuint Serial;
        public int Send;
        public nint Display;
        public nuint Window, Root, Subwindow, Time;
        public int X, Y, RootX, RootY;
        public uint State, KeyCode;
        public int SameScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ModifierMap
    {
        public int Count;
        public byte* Keys;
    }

    [DllImport(X11)]
    static extern nint XGetModifierMapping(nint display);
    [DllImport(X11)]
    static extern int XFreeModifiermap(nint mapping);
    [DllImport(X11)]
    static extern nint XOpenDisplay(nint name);
    [DllImport(X11)]
    static extern int XCloseDisplay(nint display);
    [DllImport(X11)]
    static extern nuint XDefaultRootWindow(nint display);
    [DllImport(X11)]
    static extern int XConnectionNumber(nint display);
    [DllImport(X11)]
    static extern nuint XStringToKeysym([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(X11)]
    static extern byte XKeysymToKeycode(nint display, nuint symbol);
    [DllImport(X11)]
    static extern int XGrabKey(nint display, int key, uint modifiers, nuint window, int owner, int pointerMode, int keyboardMode);
    [DllImport(X11)]
    static extern int XUngrabKey(nint display, int key, uint modifiers, nuint window);
    [DllImport(X11)]
    static extern int XSync(nint display, int discard);
    [DllImport(X11)]
    static extern int XFlush(nint display);
    [DllImport(X11)]
    static extern nint XSetErrorHandler(nint handler);
    [DllImport(X11)]
    static extern int XPending(nint display);
    [DllImport(X11)]
    static extern int XNextEvent(nint display, nint ev);
}
