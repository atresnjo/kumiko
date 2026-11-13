using Kumiko.Icons;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void SettingSelect(SettingRow row, string value, string[] options, Func<string, CrmMsg> choose, float width = 220)
    {
        var open = Model.Settings.Menu == row.Key;
        using var box = C.Div(key: "select").FieldSurface(p).Width(width).Height(36).Padding(new Edges(0, 12, 0, 14))
            .Rounded(Corners.Card).Semantics(Role.ComboBox, row.Title, value)
            .Accessibility(new(Expanded: open));
        var hit = box.Control();
        Ui.OnPress(hit, new CrmMsg.SetSettingsMenu(open ? null : row.Key));
        if (hit.Hovered)
        {
            box.Background(Color.Mix(p.Page, p.Text, .025f));
            C.Cursor = Cursor.Hand;
        }
        if (hit.Focused && C.FocusVisible || open)
            box.AccentOutline(p);
        using (C.Div(key: "value").Flex().Clip())
            Label(value, TextSize.Setting - .5f, p.Text, key: "text");
        C.Icon(Hugeicons.ArrowDown01, 11, p.Secondary, key: "chevron");
        if (!open)
            return;
        using var menu = C.Kit.Popover(hit.Rect, () => Ui.Dispatch(new CrmMsg.SetSettingsMenu(null)),
            new(Name: row.Title, MatchAnchorWidth: true), key: "menu");
        ulong previous = 0;
        foreach (var option in options)
        {
            if (Option(option, option == value, ref previous, key: option).Clicked)
                Ui.Dispatch(choose(option));
        }
    }
    void Swatches()
    {
        using var row = C.Div(key: "swatches").Row().ItemsCenter().Gap(6).Semantics(Role.Group, "Accent color");
        foreach (var tint in Enum.GetValues<Accent>())
        {
            var selected = Model.Preferences.Accent == tint;
            using var ring = C.Div(key: tint.ToString()).Width(30).Height(30).Padding(4).Rounded(Radius.Pill)
                .Border(selected ? p.Text : Color.Transparent, 2).Semantics(Role.Option, tint.ToString(), isChecked: selected);
            var hit = ring.Control();
            Ui.OnPress(hit, new CrmMsg.SetPreferences(Model.Preferences with
            {
                Accent = tint
            }));
            if (hit.Hovered)
            {
                C.Cursor = Cursor.Hand;
                if (!selected)
                    ring.Border(p.Border, 2);
            }
            if (hit.Focused && C.FocusVisible)
                ring.AccentOutline(p);
            using (C.Div(key: "dot").SizeFull().Rounded(Radius.Pill).Background(new Palette(Model.Dark, tint).Accent))
            {
            }
        }
    }
    void PlanUsage()
    {
        const int used = 5, seats = 10;
        using var usage = C.Div(key: "usage").Width(220).Column().Gap(8);
        using (C.Div(key: "labels").Row().FillWidth())
        {
            Label($"{used} of {seats} seats", TextSize.Body, p.Text, key: "seats");
            C.Spacer(key: "space");
            Label($"{used * 100 / seats}%", TextSize.Body, p.Secondary, key: "share");
        }
        using (C.Div(key: "track").FillWidth().Height(6).Rounded(Radius.Pill).Background(p.Lane).Clip())
        using (C.Div(key: "fill").Width(Length.Percent(used * 100f / seats)).FillHeight().Rounded(Radius.Pill).Background(p.Accent))
        {
        }
    }
    void CardMark()
    {
        using var card = C.Div(key: "card").Row().ItemsCenter().Gap(10);
        using (C.Div(key: "brand").Width(40).Height(26).Center().Rounded(5).Background(Color.Hex(0x1a1f71)))
            Label("VISA", TextSize.Tiny, Color.White, Weight.Bold, "text");
        Label("•••• 4242", TextSize.Body, p.Secondary, key: "number");
    }
    void Invoices()
    {
        (string Date, string Amount)[] invoices = [("Oct 1, 2026", "$145.00"), ("Sep 1, 2026", "$145.00"), ("Aug 1, 2026", "$116.00")];
        using var list = C.Div(key: "invoice-list").FillWidth().Column().OutlinedCard(p).Clip();
        for (var i = 0; i < invoices.Length; i++)
        {
            var (date, amount) = invoices[i];
            if (i > 0)
                Rule("rule-" + date);
            using var row = C.Div(key: date).Row().FillWidth().Height(44).ItemsCenter().Gap(Space.S3).Px(16);
            C.Icon(Hugeicons.Note01, 14, p.Faint, key: "icon");
            using (C.Div(key: "date").Flex())
                Label(date, TextSize.Body, p.Text, key: "text");
            Label(amount, TextSize.Body, p.Text, key: "amount");
            using (C.Div(key: "paid").Height(22).Px(8).Center().Rounded(Corners.Tag).Background(p.Of(Stage.Won).Fill))
                Label("Paid", TextSize.Caption, p.Of(Stage.Won).Text, Weight.Medium, "text");
        }
    }
}
