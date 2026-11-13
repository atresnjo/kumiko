using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite.Components;

public readonly record struct PropertyRowProps(float LabelWidth, float MinimumHeight, float LabelSize,
    bool Muted = true, float ValueGap = 0)
{
    public static PropertyRowProps Compact => new(108, 30, TextSize.Small);
    public static PropertyRowProps Comfortable => new(140, 36, TextSize.Body, Muted: false, ValueGap: Space.S2);
}

/// <summary>A property label and caller-supplied value slot, without CRM model or message dependencies.</summary>
public static class PropertyRow
{
    [KumikoView]
    public static void View(Context c, Palette palette, string label, Action value, PropertyRowProps props, NodeKey key = default)
    {
        using var row = Root(c.Div(key: key), props);
        using (c.Div(key: "label").Width(props.LabelWidth))
            c.Text(label, new(Size: props.LabelSize, Color: props.Muted ? palette.Faint : palette.Secondary), key: "text");
        using (Value(c.Div(key: "value"), props))
            value();
    }

    static Div Root(Div div, PropertyRowProps props) => div.Row().FillWidth().MinHeight(props.MinimumHeight).ItemsCenter();

    static Div Value(Div div, PropertyRowProps props)
    {
        div.Flex().Row().ItemsCenter();
        if (props.ValueGap != 0)
            div.Gap(props.ValueGap);
        return div;
    }
}
