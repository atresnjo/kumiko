using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

internal interface ICarbonHotKeys
{
    int Install(nint callback, nint context, uint eventClass, uint kind, out nint handler);
    int Register(uint key, uint modifiers, uint id, uint options, out nint reference);
    int ReadId(nint ev, out uint signature, out uint id);
    void Unregister(nint reference);
    void Remove(nint handler);
}

internal sealed unsafe class MacGlobalShortcutBackend : IGlobalShortcutBackend
{
    const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    readonly App app;
    readonly ICarbonHotKeys api;
    readonly Dictionary<ShortcutChord, (nint Reference, uint Id)> registrations = new();
    readonly Dictionary<uint, Action> callbacks = new();
    GCHandle context;
    nint handler;
    static int nextId;
    [StructLayout(LayoutKind.Sequential)]
    struct EventType
    {
        public uint Class, Kind;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HotKeyId
    {
        public uint Signature, Id;
    }

    internal MacGlobalShortcutBackend(App app, ICarbonHotKeys? api = null)
    {
        this.app = app;
        this.api = api ?? new CarbonHotKeys();
        context = GCHandle.Alloc(this);
        int status = this.api.Install((nint)(delegate* unmanaged<nint, nint, nint, int>)&Pressed, GCHandle.ToIntPtr(context), 0x6b657962, 5, out handler);
        if (status != 0)
        {
            context.Free();
            throw new InvalidOperationException($"Carbon InstallEventHandler: {status}");
        }
    }

    static uint KeyCode(string key)
    {
        const string letters = "ASDFHGZXCV§BQWERYT123465=97-80]OU[IP\rLJ'K;\\,/NM.\t `\b";
        string lookup = key switch
        {
            "ENTER" or "RETURN" => "\r",
            "TAB" => "\t",
            "SPACE" => " ",
            "BACKSPACE" => "\b",
            _ => key
        };
        if (lookup.Length == 1 && letters.IndexOf(lookup, StringComparison.Ordinal) is var index && index >= 0)
            return (uint)index;
        return key switch
        {
            "ESCAPE" or "ESC" => 53,
            "LEFT" => 123,
            "RIGHT" => 124,
            "UP" => 126,
            "DOWN" => 125,
            "HOME" => 115,
            "END" => 119,
            "PAGEUP" => 116,
            "PAGEDOWN" => 121,
            "DELETE" => 117,
            "F1" => 122,
            "F2" => 120,
            "F3" => 99,
            "F4" => 118,
            "F5" => 96,
            "F6" => 97,
            "F7" => 98,
            "F8" => 100,
            "F9" => 101,
            "F10" => 109,
            "F11" => 103,
            "F12" => 111,
            _ => throw new ArgumentException($"Unsupported Carbon shortcut key: {key}")
        };
    }

    public bool Register(ShortcutChord chord, Action callback)
    {
        uint id = checked((uint)Interlocked.Increment(ref nextId));
        uint modifiers = (chord.Meta ? 1u << 8 : 0) | (chord.Shift ? 1u << 9 : 0) | (chord.Alt ? 1u << 11 : 0) | (chord.Control ? 1u << 12 : 0);
        int status = api.Register(KeyCode(chord.Key), modifiers, id, 1, out var reference);
        if (status == -9878)
            return false;
        if (status != 0)
            throw new InvalidOperationException($"Carbon RegisterEventHotKey: {status}");
        registrations.Add(chord, (reference, id));
        callbacks.Add(id, callback);
        return true;
    }

    [UnmanagedCallersOnly]
    static int Pressed(nint next, nint ev, nint data)
    {
        var owner = (MacGlobalShortcutBackend)GCHandle.FromIntPtr(data).Target!;
        try
        {
            int status = owner.api.ReadId(ev, out uint signature, out uint id);
            if (status != 0)
                return status;
            if (signature != 0x4b554d49 || !owner.callbacks.TryGetValue(id, out var callback))
                return -9874;
            callback();
            return 0;
        }
        catch (Exception ex)
        {
            owner.app.Report(ex);
            return -9874;
        }
    }

    public void Unregister(ShortcutChord chord)
    {
        if (registrations.Remove(chord, out var registration))
        {
            api.Unregister(registration.Reference);
            callbacks.Remove(registration.Id);
        }
    }

    public void Dispose()
    {
        foreach (var chord in registrations.Keys.ToArray())
            Unregister(chord);
        if (handler != 0)
            api.Remove(handler);
        handler = 0;
        if (context.IsAllocated)
            context.Free();
    }

    sealed class CarbonHotKeys : ICarbonHotKeys
    {
        public int Install(nint callback, nint context, uint eventClass, uint kind, out nint handler)
        {
            var type = new EventType { Class = eventClass, Kind = kind };
            return InstallEventHandler(GetApplicationEventTarget(), (delegate* unmanaged<nint, nint, nint, int>)callback, 1, &type, context, out handler);
        }
        public int Register(uint key, uint modifiers, uint id, uint options, out nint reference) => RegisterEventHotKey(key, modifiers,
            new()
            {
                Signature = 0x4b554d49,
                Id = id
            }, GetApplicationEventTarget(), options, out reference);
        public int ReadId(nint ev, out uint signature, out uint id)
        {
            HotKeyId result;
            int status = GetEventParameter(ev, 0x2d2d2d2d, 0x686b6964, 0, (nuint)sizeof(HotKeyId), 0, &result);
            signature = result.Signature;
            id = result.Id;
            return status;
        }
        public void Unregister(nint reference) => UnregisterEventHotKey(reference);
        public void Remove(nint handler) => RemoveEventHandler(handler);
    }

    [DllImport(Carbon)]
    static extern nint GetApplicationEventTarget();
    [DllImport(Carbon)]
    static extern int InstallEventHandler(nint target, delegate* unmanaged<nint, nint, nint, int> callback, nuint count, EventType* types, nint data, out nint handler);
    [DllImport(Carbon)]
    static extern int RegisterEventHotKey(uint key, uint modifiers, HotKeyId id, nint target, uint options, out nint reference);
    [DllImport(Carbon)]
    static extern int UnregisterEventHotKey(nint reference);
    [DllImport(Carbon)]
    static extern int RemoveEventHandler(nint handler);
    [DllImport(Carbon)]
    static extern int GetEventParameter(nint ev, uint name, uint type, nint actualType, nuint size, nint actualSize, HotKeyId* data);
}
