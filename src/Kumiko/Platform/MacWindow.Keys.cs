using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed partial class MacWindow
{
    /// <summary>Routes a key-down through <see cref="KeyCharacters.Route"/>; undelivered keys reach the input system.</summary>
    void HandleKey(nint e)
    {
        var editing = markedLength > 0 || window.Context is { } c && c.Edits.ContainsKey(c.Focus);
        var route = KeyCharacters.Route((int)O.N(e, "keyCode"), O.Text(O.O(e, "charactersIgnoringModifiers")),
            O.Text(O.O(e, "characters")), Mods(e), editing);
        if (route.Deliver)
        {
            window.Input(i =>
            {
                i.Key = route.Key;
                i.Character = route.Character;
                i.Modifiers = route.Modifiers;
                i.HeldModifiers = route.Modifiers;
            });
            return;
        }

        var events = O.O(O.Class("NSArray"), "arrayWithObject:", e);
        O.V(view, "interpretKeyEvents:", events);
    }
}
