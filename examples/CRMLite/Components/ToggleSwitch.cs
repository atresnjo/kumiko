using Kumiko.Runtime;
using Kumiko.UI;

namespace CRMLite.Components;

public enum ToggleVariant
{
    Compact,
    Settings
}

/// <summary>A controlled switch. The parent owns its value and supplies its toggle message.</summary>
public static class ToggleSwitch
{
    [KumikoView]
    public static void View<TMessage>(Ui<TMessage> ui, Palette palette, string name, bool value, TMessage onToggle,
        ToggleVariant variant = ToggleVariant.Compact, NodeKey key = default, bool enabled = true)
    {
        var c = ui.Context;
        var large = variant == ToggleVariant.Settings;
        using var track = Track(c.Div(key: key), palette, value, large)
            .Semantics(Role.Switch, name, isChecked: value);
        var hit = track.Control(enabled);
        if (!enabled)
            track.Opacity(.5f);
        ui.OnPress(hit, onToggle);
        if (hit.Hovered)
            c.Cursor = Cursor.Hand;
        if (hit.Focused && c.FocusVisible)
            track.AccentOutline(palette);
        using var thumb = Thumb(c.Div(key: "thumb"), palette, value, large)
            .Offset(value ? large ? 18 : 12 : 0, 0, Motion.Quick);
    }

    static Div Track(Div div, Palette palette, bool value, bool large) => div
        .Width(large ? 42 : 30).Height(large ? 24 : 18).Padding(2).Rounded(Radius.Pill)
        .Background(value ? large ? palette.Text : palette.Accent : palette.Kit.Switch);

    static Div Thumb(Div div, Palette palette, bool value, bool large)
    {
        div.Width(large ? 20 : 14).Height(large ? 20 : 14).Rounded(Radius.Pill)
            .Background(large && value ? palette.Page : Color.White);
        if (large)
            div.Shadow(2, 1, Color.Black.Alpha(.18f));
        return div;
    }
}
