using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;

internal sealed class WindowsGlobalShortcutBackend : IGlobalShortcutBackend
{
    readonly WindowsMessageWindow host;
    readonly Dictionary<ShortcutChord, int> ids = new();
    readonly Dictionary<int, Action> callbacks = new();
    int nextId;
    internal WindowsGlobalShortcutBackend(App app)
    {
        host = new(app, (message, w, l) =>
        {
            if (message != 0x312)
                return null;
            if (callbacks.TryGetValue((int)w, out var callback))
                callback();
            return 0;
        });
    }

    internal static uint VirtualKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return key[0];
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int f) && f is >= 1 and <= 24)
            return (uint)(111 + f);
        return key switch
        {
            "SPACE" => 32,
            "ENTER" or "RETURN" => 13,
            "TAB" => 9,
            "ESCAPE" or "ESC" => 27,
            "LEFT" => 37,
            "UP" => 38,
            "RIGHT" => 39,
            "DOWN" => 40,
            "HOME" => 36,
            "END" => 35,
            "PAGEUP" => 33,
            "PAGEDOWN" => 34,
            "DELETE" => 46,
            "BACKSPACE" => 8,
            _ => throw new ArgumentException($"Unsupported shortcut key: {key}")
        };
    }

    public bool Register(ShortcutChord chord, Action callback)
    {
        uint key = VirtualKey(chord.Key);
        uint mods = 0x4000u | (chord.Alt ? 1u : 0) | (chord.Control ? 2u : 0) | (chord.Shift ? 4u : 0) | (chord.Meta ? 8u : 0);
        int id = ++nextId;
        if (RegisterHotKey(host.Handle, id, mods, key) == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == 1409)
                return false;
            throw new System.ComponentModel.Win32Exception(error, "RegisterHotKey failed.");
        }

        ids.Add(chord, id);
        callbacks.Add(id, callback);
        return true;
    }

    public void Unregister(ShortcutChord chord)
    {
        if (!ids.Remove(chord, out int id))
            return;
        UnregisterHotKey(host.Handle, id);
        callbacks.Remove(id);
    }

    public void Dispose()
    {
        foreach (var chord in ids.Keys.ToArray())
            Unregister(chord);
        host.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern int RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern int UnregisterHotKey(nint window, int id);
}
