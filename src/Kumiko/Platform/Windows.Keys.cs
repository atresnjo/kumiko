using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Platform;

/// <summary>
/// Key-downs follow the macOS key contract. Navigation and function keys always arrive as keys.
/// Printable keys follow the active layout through <see cref="KeyCharacters.Route"/>: while a text input has focus
/// they stay text (WM_CHAR, dead keys, AltGr) unless Control or Alt is held; otherwise they arrive as a key plus
/// the produced character and their WM_CHAR is consumed, so they never also type.
/// </summary>
internal sealed unsafe partial class WindowsWindow
{
    /// <summary>WM_KEYDOWN and WM_SYSKEYDOWN; true when DefWindowProc must not see the key.</summary>
    bool KeyDown(nuint w, nint l, bool system)
    {
        int code = (int)w;
        var modifiers = GetModifiers();
        // Shift, Control, Alt (either side), the Windows keys and Caps Lock only change the held modifiers.
        if (code is 16 or 17 or 18 or 20 or 0x5b or 0x5c or (>= 0xa0 and <= 0xa5))
        {
            window.Input(i => i.HeldModifiers = modifiers);
            return false;
        }
        // VK_PROCESSKEY: the IME owns this keystroke and reports it through WM_IME_COMPOSITION.
        if (code == 0xe5)
            return false;
        var special = Special(code);
        if (special != Key.None)
        {
            Deliver(special, '\0', modifiers);
            return special is Key.F2 or Key.F10;
        }

        // TranslateMessage posted this keystroke's WM_CHAR before dispatching it; those are what it types,
        // dead keys and AltGr included. WM_SYSCHAR stays queued for DefWindowProc (Alt+Space opens the system menu).
        var typed = new List<Win32.Message>();
        while (Win32.PeekMessageW(out var next, handle, 0x102, 0x103, 1) != 0)
            typed.Add(next);
        var produced = Produced(typed, system);
        // AltGr is Control+Alt on Windows; when it types a character it chooses that character, as Option does.
        bool altGr = (modifiers & (Modifiers.Control | Modifiers.Alt)) == (Modifiers.Control | Modifiers.Alt)
            && produced.Length > 0 && !char.IsControl(produced[0]);
        var routed = altGr ? modifiers & ~Modifiers.Control : modifiers;
        // Control and Alt chords bypass text input; Alt types nothing on Windows, so its chords are never text.
        bool chord = !altGr && (modifiers & (Modifiers.Control | Modifiers.Alt)) != 0;
        bool editing = window.Context is { } c && c.IsEditing;
        if (editing && !chord)
        {
            foreach (var message in typed)
                if (message.Id == 0x102)
                    Character((char)message.WParam);
            return false;
        }

        int scan = (int)(((long)l >> 16) & 0xff);
        bool extended = ((long)l & (1 << 24)) != 0;
        // Code -1 names no macOS key, so Route resolves the key from the layout's character alone.
        var route = KeyCharacters.Route(-1, Unmodified(code, scan, modifiers), produced, routed, editing: false);
        var key = route.Key != Key.None ? route.Key : extended ? Key.None : Physical(scan);
        if (key != Key.None || route.Character != '\0')
            Deliver(key, route.Character, routed);
        return false;
    }

    void Deliver(Key key, char character, Modifiers modifiers) => window.Input(i =>
    {
        i.Key = key;
        i.Character = character;
        i.Modifiers = modifiers;
        i.HeldModifiers = modifiers;
    });

    /// <summary>WM_CHAR text; Control chords type nothing, but AltGr (Control+Alt) characters do.</summary>
    void Character(char ch)
    {
        if (char.IsHighSurrogate(ch))
        {
            highSurrogate = ch;
            return;
        }

        var modifiers = GetModifiers();
        bool control = (modifiers & Modifiers.Control) != 0 && (modifiers & Modifiers.Alt) == 0;
        if (ch >= 32 && ch != 127 && !control)
        {
            string text = highSurrogate != 0 ? new string([highSurrogate, ch]) : ch.ToString();
            window.Input(i => i.Text = text);
        }
        highSurrogate = default;
    }

    /// <summary>What the keystroke types: its WM_CHAR text, or for an Alt chord the queued WM_SYSCHAR.</summary>
    string Produced(List<Win32.Message> typed, bool system)
    {
        var chars = new List<char>();
        foreach (var message in typed)
            if (message.Id == 0x102)
                chars.Add((char)message.WParam);
        if (system && chars.Count == 0 && Win32.PeekMessageW(out var next, handle, 0x106, 0x106, 0) != 0)
            chars.Add((char)next.WParam);
        return new string([.. chars]);
    }

    /// <summary>
    /// The key's character with Shift only, as macOS charactersIgnoringModifiers: ToUnicodeEx with a keyboard
    /// state holding just Shift. Flag 4 (Windows 10 1607) leaves the dead-key state TranslateMessage set intact.
    /// </summary>
    static string Unmodified(int code, int scan, Modifiers modifiers)
    {
        var state = stackalloc byte[256];
        if ((modifiers & Modifiers.Shift) != 0)
            state[16] = 0x80;
        var buffer = stackalloc char[8];
        int count = ToUnicodeEx((uint)code, (uint)scan, state, buffer, 8, 4, GetKeyboardLayout(0));
        // A dead key reports -1 with its spacing accent; MapVirtualKey covers layouts that report nothing.
        if (count < 0)
            count = 1;
        if (count > 0)
            return new string(buffer, 0, Math.Min(count, 8));
        uint mapped = MapVirtualKeyW((uint)code, 2) & 0x7fffffff;
        return mapped is > 0 and < 0xffff ? ((char)mapped).ToString() : "";
    }

    internal static Key Special(int code) => code switch
    {
        13 => Key.Enter,
        9 => Key.Tab,
        8 => Key.Backspace,
        46 => Key.Delete,
        27 => Key.Escape,
        37 => Key.Left,
        39 => Key.Right,
        38 => Key.Up,
        40 => Key.Down,
        36 => Key.Home,
        35 => Key.End,
        33 => Key.PageUp,
        34 => Key.PageDown,
        0x70 => Key.F1,
        0x71 => Key.F2,
        0x72 => Key.F3,
        0x73 => Key.F4,
        0x74 => Key.F5,
        0x75 => Key.F6,
        0x76 => Key.F7,
        0x77 => Key.F8,
        0x78 => Key.F9,
        0x79 => Key.F10,
        0x7a => Key.F11,
        0x7b => Key.F12,
        _ => Key.None
    };

    /// <summary>US key positions by set-1 scan code, used when the layout's character names no key (Cyrillic).</summary>
    internal static Key Physical(int scan) => scan switch
    {
        0x39 => Key.Space,
        0x1e => Key.A,
        0x30 => Key.B,
        0x2e => Key.C,
        0x20 => Key.D,
        0x12 => Key.E,
        0x21 => Key.F,
        0x22 => Key.G,
        0x23 => Key.H,
        0x17 => Key.I,
        0x24 => Key.J,
        0x25 => Key.K,
        0x26 => Key.L,
        0x32 => Key.M,
        0x31 => Key.N,
        0x18 => Key.O,
        0x19 => Key.P,
        0x10 => Key.Q,
        0x13 => Key.R,
        0x1f => Key.S,
        0x14 => Key.T,
        0x16 => Key.U,
        0x2f => Key.V,
        0x11 => Key.W,
        0x2d => Key.X,
        0x15 => Key.Y,
        0x2c => Key.Z,
        0x0b => Key.D0,
        0x02 => Key.D1,
        0x03 => Key.D2,
        0x04 => Key.D3,
        0x05 => Key.D4,
        0x06 => Key.D5,
        0x07 => Key.D6,
        0x08 => Key.D7,
        0x09 => Key.D8,
        0x0a => Key.D9,
        0x35 => Key.Slash,
        0x33 => Key.Comma,
        0x34 => Key.Period,
        0x0c => Key.Minus,
        0x0d => Key.Equal,
        0x27 => Key.Semicolon,
        0x28 => Key.Quote,
        0x29 => Key.Backquote,
        0x1a => Key.BracketLeft,
        0x1b => Key.BracketRight,
        0x2b => Key.Backslash,
        _ => Key.None
    };

    [DllImport("user32.dll")]
    static extern int ToUnicodeEx(uint code, uint scan, byte* state, char* buffer, int length, uint flags, nint layout);
    [DllImport("user32.dll")]
    static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")]
    static extern uint MapVirtualKeyW(uint code, uint type);
}
