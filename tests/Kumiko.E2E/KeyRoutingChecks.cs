using Kumiko.UI;

/// <summary>
/// Keystrokes follow the active keyboard layout. Plain letters and punctuation reach Context.Shortcut only while
/// no text input has focus; a German layout's Z, '/' (Shift+7) and '[' (Option+5) match their characters.
/// </summary>
internal static class KeyRoutingChecks
{
    // ANSI virtual key codes.
    const int J = 38, Y = 16, Seven = 26, Five = 23, D = 2, Down = 125;

    internal static void Run(Action<bool, string> check)
    {
        var j = KeyCharacters.Route(J, "j", "j", Modifiers.None, editing: false);
        check(j is { Deliver: true, Key: Key.J, Character: 'j' }, "a plain letter is delivered as a key and character outside text input");
        check(!KeyCharacters.Route(J, "j", "j", Modifiers.None, editing: true).Deliver, "a plain letter goes to the input system while typing");
        check(KeyCharacters.Route(J, "j", "j", Modifiers.Meta, editing: true) is { Deliver: true, Key: Key.J }, "Command shortcuts still arrive while typing");
        check(KeyCharacters.Route(Down, "", "", Modifiers.None, editing: true) is { Deliver: true, Key: Key.Down, Character: '\0' },
            "navigation keys are delivered without a character");
        // On QWERTZ the key at the ANSI Y position types z; Command+Z there is undo, not redo.
        check(KeyCharacters.Route(Y, "z", "z", Modifiers.Meta, editing: false).Key == Key.Z, "letter keys follow the layout, not the ANSI position");
        check(KeyCharacters.Route(Seven, "/", "/", Modifiers.Shift, editing: false) is { Key: Key.Slash, Character: '/' }, "a shifted layout character names its key");
        check(KeyCharacters.Route(Five, "5", "[", Modifiers.Alt, editing: false) is { Character: '[' }, "an Option character is the produced character");
        check(KeyCharacters.Route(D, "d", "\u0004", Modifiers.Control, editing: false) is { Key: Key.D, Character: 'd' }, "Control keeps the key's own character");
        check(KeyCharacters.Route(0, "Ж", "Ж", Modifiers.None, editing: false) is { Key: Key.A, Character: 'Ж' }, "a non-Latin layout falls back to the key position");

        var hits = new List<string>();
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull();
            if (c.Shortcut('?'))
                hits.Add("?");
            if (c.Shortcut('g'))
                hits.Add("g");
            if (c.Shortcut('G'))
                hits.Add("G");
            if (c.Shortcut('p', Modifiers.Primary))
                hits.Add("cmd-p");
            if (c.Shortcut('p'))
                hits.Add("p");
            if (c.Shortcut('['))
                hits.Add("[");
        }, new(200, 100));
        t.Keystroke('?');
        t.Keystroke('G');
        t.Keystroke('g');
        t.Keystroke('p', Modifiers.Primary);
        t.Keystroke('p');
        t.Context.Input.Key = Key.D5;
        t.Context.Input.Character = '[';
        t.Context.Input.Modifiers = Modifiers.Alt;
        t.Frame();
        check(string.Join(" ", hits) == "? G g cmd-p p [", $"character shortcuts match case, chords and Option characters exactly ({string.Join(" ", hits)})");
    }
}
