using Kumiko.iOS;
using Kumiko.UI;
using static CRMLite.Palette;
using static CRMLite.Parts;

namespace CRMLite;

/// <summary>
/// The phone's Harbor pieces: the phone scale for the parts shared with desktop (<see cref="Parts"/>) and the touch-sized
/// actions, chips, tabs and fields only the phone draws, in the same palette, shapes and icons.
/// </summary>
internal static class Harbor
{
    /// <summary>The phone type scale. Desktop sizes stay in Palette.TextSize.</summary>
    internal static class Scale
    {
        internal const float Caption = 12;
        internal const float Small = 13;
        internal const float Body = 15;
        internal const float Row = 16;
        internal const float Title = 17;
        internal const float Name = 22;
        internal const float Metric = 22;
    }

    internal const float Gutter = 16;

    /// <summary>The phone sizes of the shared <see cref="Parts"/>; the desktop's are in CRMLite View.Parts.cs.</summary>
    internal static readonly PartScale Phone = new(11, TextOverflow.Ellipsis,
        new(24, 6, 8, Corners.Tag + 1, Scale.Small),
        new(new Edges(12, 14, 12, 14), 5, Corners.Card + 2, 22, Scale.Body, Scale.Small, 28, 8, 26, FixedHeight: false),
        new(Scale.Small, Scale.Body, Scale.Small, Scale.Caption, 18, 24, 15, 11, new Edges(10, 12, 10, 12), Corners.Card + 2,
            Wrap: true));

    /// <summary>The accent-filled action: white label and optional icon, at least 44 points high.</summary>
    internal static void Primary<TMessage>(Ui<TMessage> ui, Palette p, string label, TMessage message, string? icon = null,
        bool fill = false, NodeKey key = default)
    {
        var c = ui.Context;
        using var press = ui.NativePressable(label, message, key: key);
        if (fill)
            press.FillWidth();
        using var box = c.Div(key: "face").Height(44).Row().Center().Gap(6).Px(16).Rounded(Corners.Dialog - 2).Background(p.Accent);
        if (fill)
            box.FillWidth();
        if (icon is not null)
            c.Icon(icon, 16, Color.White, key: "icon");
        Label(c, Phone, label, Scale.Body, Color.White, Weight.Semibold, "label");
    }

    /// <summary>The quiet full-width action: page fill, input border and the primary text color.</summary>
    internal static void Secondary<TMessage>(Ui<TMessage> ui, Palette p, string label, TMessage message, NodeKey key = default)
    {
        var c = ui.Context;
        using var press = ui.NativePressable(label, message, key: key);
        press.FillWidth();
        using var box = c.Div(key: "face").FillWidth().Height(44).Row().Center().Px(16).Rounded(Corners.Dialog - 2)
            .Background(p.Page).Border(p.Kit.Input);
        Label(c, Phone, label, Scale.Body, p.Text, Weight.Medium, "label");
    }

    /// <summary>A quiet bordered chip; active chips fill with the menu tone and darken their label.</summary>
    internal static void Chip<TMessage>(Ui<TMessage> ui, Palette p, string label, string? icon, bool active, TMessage message,
        NodeKey key = default)
    {
        var c = ui.Context;
        using var press = ui.NativePressable(label, message, key: key);
        using var box = c.Div(key: "face").Row().Height(36).ItemsCenter().Gap(6).Px(12).Rounded(18)
            .Background(active ? p.Menu : p.Page).Border(active ? p.Kit.Input : p.Border);
        if (icon is not null)
            c.Icon(icon, 14, active ? p.Text : p.Secondary, key: "icon");
        Label(c, Phone, label, Scale.Small + 1, active ? p.Text : p.Secondary, active ? Weight.Medium : Weight.Regular, "label");
    }

    /// <summary>Desktop underline tabs: label, faint count and a 2-point rule under the active tab.</summary>
    internal static void Tab<TMessage>(Ui<TMessage> ui, Palette p, string label, string count, bool active, TMessage message,
        string? icon = null, NodeKey key = default)
    {
        var c = ui.Context;
        using var press = ui.NativePressable(label, message, key: key);
        using var tab = c.Div(key: "face").Column().Height(44);
        using (c.Div(key: "body").Row().Grow().ItemsCenter().Gap(5).Px(2))
        {
            if (icon is not null)
                c.Icon(icon, 14, active ? p.Text : p.Secondary, key: "icon");
            Label(c, Phone, label, Scale.Body, active ? p.Text : p.Secondary, active ? Weight.Medium : Weight.Regular, "label");
            Label(c, Phone, count, Scale.Small, p.Faint, key: "count");
        }
        using (c.Div(key: "rule").FillWidth().Height(2).Rounded(1).Background(active ? p.Text : Color.Transparent))
        {
        }
    }

    /// <summary>The desktop field surface around a borderless native field with a leading search icon.</summary>
    internal static void SearchField<TMessage>(Ui<TMessage> ui, Palette p, string label, string value, Func<string, TMessage> changed,
        NodeKey key = default)
    {
        var c = ui.Context;
        using var surface = c.Div(key: key).Row().Flex().MinWidth(0).Height(40).ItemsCenter().Gap(8).Px(12)
            .Rounded(Corners.Dialog - 2).Background(p.Lane).Border(p.Border);
        c.Icon(Kumiko.Icons.Hugeicons.Search01, 16, p.Faint, key: "icon");
        using (c.Div(key: "input").Flex().MinWidth(0).Height(40))
            ui.NativeTextField(label, value, changed, Input(p), label, height: 40, key: "field");
    }

    internal static NativeInputStyle Input(Palette p) =>
        new(new(Size: Scale.Body, Color: p.Text), p.Faint, p.Accent, p.Dark);

    /// <summary>A label and value on one row of a details card.</summary>
    internal static void Property(Context c, Palette p, string label, NodeKey key, Action value)
    {
        using var row = c.Div(key: key).Row().FillWidth().MinHeight(44).ItemsCenter().Gap(Space.S3).Px(14);
        using (c.Div(key: "label").Width(112))
            Label(c, Phone, label, Scale.Body, p.Secondary, key: "text");
        using var cell = c.Div(key: "value").Flex().MinWidth(0).Row().ItemsCenter().Gap(8);
        value();
    }

    /// <summary>The navigation bar title for a visit.</summary>
    internal static string PageTitle(CrmModel model, CrmEntry entry) => entry.Route switch
    {
        Screen.CompanyDetails company => Crm.Company(model, company.Id)?.Name ?? "Company",
        Screen.Deals => "Deals",
        Screen.People => "People",
        Screen.Inbox => "Inbox",
        _ => "Companies"
    };

    /// <summary>The adaptation of NativePalette for sheets drawn in CRM colors.</summary>
    internal static NativePalette Sheet(Palette p) =>
        new(p.Page, p.Page, p.Raised, p.Text, p.Secondary, p.Accent, p.Stale, p.Border);
}
