using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite.Components;

/// <summary>A stable local key and visible label paired with the value reported to the parent.</summary>
public readonly record struct Segment<TValue>(string Id, string Label, TValue Value);

/// <summary>Controlled, typed choices with private track and selection styles.</summary>
public static class SegmentedChoice
{
    [KumikoView]
    public static void View<TValue, TMessage>(Ui<TMessage> ui, Palette palette, string name, TValue value,
        ImmutableArray<Segment<TValue>> options, Func<TValue, TMessage> onChoose, NodeKey key = default)
    {
        var c = ui.Context;
        using var track = Track(c.Div(key: key), palette).Semantics(Role.Group, name);
        foreach (var option in options)
        {
            var selected = Equals(value, option.Value);
            using var segment = Surface(c.Div(key: option.Id), palette, selected)
                .Semantics(Role.Option, option.Label, isChecked: selected);
            var hit = segment.Control();
            if (!selected && hit.Clicked)
                ui.OnPress(hit, onChoose(option.Value));
            if (!selected && hit.Hovered)
                c.Cursor = Cursor.Hand;
            if (hit.Focused && c.FocusVisible)
                segment.AccentOutline(palette);
            c.Text(option.Label, new(Size: TextSize.Body, Color: selected ? palette.Text : palette.Secondary,
                Weight: selected ? Weight.Medium : Weight.Regular), key: "label");
        }
    }

    static Div Track(Div div, Palette palette) => div.Row().Height(36).ItemsCenter().Gap(2).Padding(3)
        .Rounded(Corners.Card).Background(palette.Lane);

    static Div Surface(Div div, Palette palette, bool selected)
    {
        div.Row().FillHeight().ItemsCenter().Px(18).Rounded(Corners.Control);
        if (selected)
        {
            div.Background(palette.Raised).Border(palette.Border);
            if (!palette.Dark)
                div.Shadow(2, 1, palette.Shade);
        }
        return div;
    }
}
