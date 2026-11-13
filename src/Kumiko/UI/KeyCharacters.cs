namespace Kumiko.UI;

internal readonly record struct KeyRoute(Key Key, char Character, Modifiers Modifiers, bool Deliver);

internal static class KeyCharacters
{
    /// <summary>
    /// Decides how a macOS key-down reaches the app. Navigation and function keys are always delivered as keys.
    /// Printable keys follow the active layout: while a text input has focus they go to the input system (IME,
    /// dead keys) unless Command is held; otherwise they arrive as a key plus the produced character.
    /// </summary>
    /// <param name="code">The virtual key code.</param>
    /// <param name="ignoring">charactersIgnoringModifiers: the key's character with Shift only.</param>
    /// <param name="produced">characters: what the keystroke types, Option and dead keys included.</param>
    /// <param name="modifiers">The modifier keys held with the key.</param>
    /// <param name="editing">Whether a text input (field, editor or IME composition) has focus.</param>
    internal static KeyRoute Route(int code, string ignoring, string produced, Modifiers modifiers, bool editing)
    {
        var key = Special(code);
        var printable = key == UI.Key.None;
        var character = '\0';
        if (printable)
        {
            key = ignoring.Length == 1 ? Key(ignoring[0]) : UI.Key.None;
            if (key == UI.Key.None)
                key = Physical(code);
            // Control and Command keep the character the key shows; Shift and Option choose it.
            var chord = (modifiers & (Modifiers.Control | Modifiers.Meta)) != 0;
            var typed = chord ? ignoring : produced;
            if (typed.Length == 1 && !char.IsControl(typed[0]))
                character = typed[0];
        }
        if (printable && editing && (modifiers & Modifiers.Meta) == 0)
            return new(UI.Key.None, '\0', modifiers, false);
        return new(key, character, modifiers, key != UI.Key.None || character != '\0');
    }

    /// <summary>The key a layout's unshifted character names, so shortcuts follow QWERTZ, AZERTY or Dvorak.</summary>
    internal static Key Key(char value) => char.ToLowerInvariant(value) switch
    {
        ' ' => UI.Key.Space,
        'a' => UI.Key.A,
        'b' => UI.Key.B,
        'c' => UI.Key.C,
        'd' => UI.Key.D,
        'e' => UI.Key.E,
        'f' => UI.Key.F,
        'g' => UI.Key.G,
        'h' => UI.Key.H,
        'i' => UI.Key.I,
        'j' => UI.Key.J,
        'k' => UI.Key.K,
        'l' => UI.Key.L,
        'm' => UI.Key.M,
        'n' => UI.Key.N,
        'o' => UI.Key.O,
        'p' => UI.Key.P,
        'q' => UI.Key.Q,
        'r' => UI.Key.R,
        's' => UI.Key.S,
        't' => UI.Key.T,
        'u' => UI.Key.U,
        'v' => UI.Key.V,
        'w' => UI.Key.W,
        'x' => UI.Key.X,
        'y' => UI.Key.Y,
        'z' => UI.Key.Z,
        '0' => UI.Key.D0,
        '1' => UI.Key.D1,
        '2' => UI.Key.D2,
        '3' => UI.Key.D3,
        '4' => UI.Key.D4,
        '5' => UI.Key.D5,
        '6' => UI.Key.D6,
        '7' => UI.Key.D7,
        '8' => UI.Key.D8,
        '9' => UI.Key.D9,
        '/' => UI.Key.Slash,
        ',' => UI.Key.Comma,
        '.' => UI.Key.Period,
        '-' => UI.Key.Minus,
        '=' => UI.Key.Equal,
        ';' => UI.Key.Semicolon,
        '\'' => UI.Key.Quote,
        '`' => UI.Key.Backquote,
        '[' => UI.Key.BracketLeft,
        ']' => UI.Key.BracketRight,
        '\\' => UI.Key.Backslash,
        _ => UI.Key.None
    };

    internal static Key Special(int code) => code switch
    {
        36 or 76 => UI.Key.Enter,
        48 => UI.Key.Tab,
        51 => UI.Key.Backspace,
        117 => UI.Key.Delete,
        53 => UI.Key.Escape,
        123 => UI.Key.Left,
        124 => UI.Key.Right,
        125 => UI.Key.Down,
        126 => UI.Key.Up,
        115 => UI.Key.Home,
        119 => UI.Key.End,
        116 => UI.Key.PageUp,
        121 => UI.Key.PageDown,
        122 => UI.Key.F1,
        120 => UI.Key.F2,
        99 => UI.Key.F3,
        118 => UI.Key.F4,
        96 => UI.Key.F5,
        97 => UI.Key.F6,
        98 => UI.Key.F7,
        100 => UI.Key.F8,
        101 => UI.Key.F9,
        109 => UI.Key.F10,
        103 => UI.Key.F11,
        111 => UI.Key.F12,
        _ => UI.Key.None
    };

    /// <summary>ANSI key positions, used when the layout's character names no key (for example Cyrillic).</summary>
    internal static Key Physical(int code) => code switch
    {
        49 => UI.Key.Space,
        0 => UI.Key.A,
        11 => UI.Key.B,
        8 => UI.Key.C,
        2 => UI.Key.D,
        14 => UI.Key.E,
        3 => UI.Key.F,
        5 => UI.Key.G,
        4 => UI.Key.H,
        34 => UI.Key.I,
        38 => UI.Key.J,
        40 => UI.Key.K,
        37 => UI.Key.L,
        46 => UI.Key.M,
        45 => UI.Key.N,
        31 => UI.Key.O,
        35 => UI.Key.P,
        12 => UI.Key.Q,
        15 => UI.Key.R,
        1 => UI.Key.S,
        17 => UI.Key.T,
        32 => UI.Key.U,
        9 => UI.Key.V,
        13 => UI.Key.W,
        7 => UI.Key.X,
        16 => UI.Key.Y,
        6 => UI.Key.Z,
        29 => UI.Key.D0,
        18 => UI.Key.D1,
        19 => UI.Key.D2,
        20 => UI.Key.D3,
        21 => UI.Key.D4,
        23 => UI.Key.D5,
        22 => UI.Key.D6,
        26 => UI.Key.D7,
        28 => UI.Key.D8,
        25 => UI.Key.D9,
        44 => UI.Key.Slash,
        43 => UI.Key.Comma,
        47 => UI.Key.Period,
        27 => UI.Key.Minus,
        24 => UI.Key.Equal,
        41 => UI.Key.Semicolon,
        39 => UI.Key.Quote,
        50 => UI.Key.Backquote,
        33 => UI.Key.BracketLeft,
        30 => UI.Key.BracketRight,
        42 => UI.Key.Backslash,
        _ => UI.Key.None
    };
}
