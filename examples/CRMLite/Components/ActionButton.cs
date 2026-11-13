using Kumiko.Icons;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite.Components;

/// <summary>Controlled action surfaces usable with any parent message type through Ui.OnPress.</summary>
public static class ActionButton
{
    /// <summary>A quiet toolbar button: icon and label, filled on hover.</summary>
    public static Interaction Ghost(Context c, Palette p, string label, string? icon, string name, string? value = null, bool active = false,
        NodeKey key = default)
    {
        using var box = c.Div(key: key).ControlRow()
            .Semantics(Role.Button, name, value);
        var hit = box.Control();
        Feedback(c, p, box, hit, active);
        if (icon is not null)
            c.Icon(icon, 13, active ? p.Text : p.Secondary, key: "icon");
        Text(c, label, TextSize.Body, active ? p.Text : p.Secondary, active ? Weight.Medium : Weight.Regular, "label");
        return hit;
    }

    public static Interaction IconButton(Context c, Palette p, string icon, string name, bool enabled = true, Color? color = null, NodeKey key = default)
    {
        using var box = c.Div(key: key).IconAction()
            .Semantics(Role.Button, name);
        var hit = box.Control(enabled);
        if (enabled)
            Feedback(c, p, box, hit);
        else
            box.Opacity(.4f);
        c.Icon(icon, 14, color ?? p.Secondary, key: "icon");
        return hit;
    }

    public static Interaction Primary(Context c, Palette p, string label, NodeKey key = default) =>
        Primary(c, p, label, Hugeicons.Add01, key);

    public static Interaction Primary(Context c, Palette p, string label, string? icon, NodeKey key = default)
    {
        using var box = c.Div(key: key).PrimaryAction(p).Semantics(Role.Button, label);
        var hit = box.Control();
        if (hit.Hovered)
        {
            box.Background(Color.Mix(p.Accent, Color.Black, .1f));
            c.Cursor = Cursor.Hand;
        }
        if (hit.Focused && c.FocusVisible)
            box.Border(p.Accent.Alpha(.45f), 3);
        if (icon is not null)
            c.Icon(icon, 12, Color.White, key: "icon");
        Text(c, label, TextSize.Body, Color.White, Weight.Medium, "label");
        return hit;
    }

    static void Text(Context c, string text, float size, Color color, Weight weight, NodeKey key) =>
        c.Text(text, new(Size: size, Weight: weight, Color: color), key: key);

    static void Feedback(Context c, Palette p, Div box, Interaction hit, bool active = false)
    {
        if (active || hit.Hovered)
            box.Background(p.Hover);
        if (hit.Focused && c.FocusVisible)
            box.AccentOutline(p);
        if (hit.Hovered)
            c.Cursor = Cursor.Hand;
    }
}
