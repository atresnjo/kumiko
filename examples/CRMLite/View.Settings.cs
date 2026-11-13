using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    sealed record SettingsPage(SettingsSection Section, string Title, string Icon, string Description);

    static readonly ImmutableArray<SettingsPage> SettingsPages =
    [
        new(SettingsSection.General, "General", Hugeicons.SlidersHorizontal, "Workspace name, language and where you land."),
        new(SettingsSection.Appearance, "Appearance", Hugeicons.PaintBoard, "Theme, accent color and layout."),
        new(SettingsSection.Notifications, "Notifications", Hugeicons.Notification03, "Choose what reaches you and how often."),
        new(SettingsSection.Security, "Security", Hugeicons.Shield01, "Sign in rules and session policy for everyone."),
        new(SettingsSection.Members, "Members", Hugeicons.UserMultiple, "Who can see and change this workspace."),
        new(SettingsSection.Billing, "Billing", Hugeicons.CreditCard, "Plan, payment method and invoices.")
    ];
    void SettingsSheet()
    {
        var s = Model.Settings;
        // The scrim fades and the sheet rises into place; both leave the same way when the sheet closes.
        using var overlay = C.Overlay("settings").Center().TabStop(false).Semantics(Role.Group).Background(p.Scrim)
            .Enter(Transition.Fade with
            {
                Motion = Motion.Gentle
            }).Exit(Transition.Fade);
        var backdrop = overlay.Control();
        if (s.Menu is null && (backdrop.Clicked || C.Shortcut(Key.Escape, Modifiers.None)))
            Ui.Dispatch(new CrmMsg.CloseSettings());
        using var panel = C.Div(key: "panel").RaisedSurface(p).Width(MathF.Min(Sizes.Settings, C.Size.Width - 64))
            .Height(MathF.Min(Sizes.SettingsHeight, C.Size.Height - 64)).Rounded(Corners.Sheet).Clip()
            .TabStop(false).Semantics(Role.Group, "Settings").Enter(Transition.Rise with
            {
                Y = 16,
                Motion = Motion.Gentle
            }).Exit(Transition.Rise with
            {
                Y = 8
            });
        panel.Shadow(48, 18, Color.Black.Alpha(p.Dark ? .55f : .18f));
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        if (C.Shortcut(Key.F))
        {
            // The search field is the sheet's first control.
            C.FocusControl(panel.Id);
            C.FocusNextControl();
        }
        SettingsHeader();
        Rule("header-rule");
        using var body = C.Div(key: "body").Row().FillWidth().Flex();
        var shown = SettingsPages.Where(page => SectionMatches(page.Section)).Select(page => page.Section).ToImmutableHashSet();
        SettingsNav(shown);
        VRule("nav-rule");
        SettingsContent(shown);
    }
    void SettingsHeader()
    {
        using var head = C.Div(key: "head").Row().FillWidth().Height(64).ItemsCenter().Gap(Space.S3).Padding(new Edges(0, 16, 0, 28));
        C.Text("Settings", new(Size: TextSize.Sheet, Weight: Weight.Medium, Color: p.Text), key: "title");
        C.Spacer(key: "space");
        using (var search = C.Div(key: "search").Row().Width(320).Height(38).ItemsCenter().Gap(8).Padding(new Edges(0, 6, 0, 12))
            .Rounded(Corners.Card).Background(p.Page).Border(p.Border))
        {
            C.Icon(Hugeicons.Search01, 15, p.Faint, key: "icon");
            using (var field = C.Div(key: "field").Flex())
            {
                if (C.FocusWithin(field.Id))
                    search.AccentOutline(p);
                BareField("query", "Search settings", Model.Settings.Query, text => new CrmMsg.SetSettingsQuery(text), MathF.Max(field.PreviousRect?.Width ?? 220, 80), TextSize.Body);
            }
            C.Kit.Kbd(Kit.ShortcutLabel("F"), key: "shortcut");
        }
        using var close = C.Div(key: "close").IconAction(32).Rounded(Corners.Button);
        var hit = Ui.Button(close, "Close settings", new CrmMsg.CloseSettings(), true);
        Feedback(close, hit);
        C.Icon(Hugeicons.Cancel01, 13, p.Secondary, key: "icon");
    }
    void SettingsNav(ImmutableHashSet<SettingsSection> shown)
    {
        const float item = 36, gap = 2;
        using var nav = C.Div(key: "nav").Column().Width(Sizes.SettingsNav).FillHeight().Padding(new Edges(16, 14, 16, 14)).Gap(gap)
            .Background(p.Sidebar).Semantics(Role.Group, "Settings sections");
        var index = SettingsPages.IndexOf(SettingsPages.First(page => page.Section == Model.Settings.Section));
        using (var pill = C.Div(key: "highlight").Absolute().FillWidth().Height(item).Rounded(Corners.Button).Background(p.Page)
            .Border(p.Border).Offset(0, index * (item + gap), Motion.Standard))
        {
            if (!p.Dark)
                pill.Shadow(2, 1, p.Shade);
        }
        foreach (var page in SettingsPages)
        {
            var active = page.Section == Model.Settings.Section;
            var enabled = shown.Contains(page.Section);
            using var row = C.Div(key: page.Title).Row().FillWidth().Height(item).ItemsCenter().Gap(10).Px(12).Rounded(Corners.Button)
                .Accessibility(new(Selected: active));
            var hit = Ui.Button(row, page.Title, new CrmMsg.JumpToSection(page.Section), enabled);
            if (!enabled)
                row.Opacity(.4f);
            else if (hit.Hovered && !active)
                row.Background(p.Hover);
            if (hit.Hovered && enabled)
                C.Cursor = Cursor.Hand;
            if (hit.Focused && C.FocusVisible)
                row.AccentOutline(p);
            C.Icon(page.Icon, 16, active ? p.Text : p.Secondary, key: "icon");
            Label(page.Title, TextSize.Nav, active ? p.Text : p.Secondary, key: "label");
        }
        C.Spacer(key: "account-space");
        AccountSession();
    }
    void SettingsContent(ImmutableHashSet<SettingsSection> shown)
    {
        const float revealMargin = 28;
        var s = Model.Settings;
        using var viewport = C.Div(key: "content").Flex().FillHeight().Scroll().Semantics(Role.Group, "Settings content");
        var view = viewport.PreviousRect;
        Rect? last = null;
        SettingsSection? spy = null;
        bool atDestination = false;
        using (var column = C.Div(key: "column").FillWidth().Column().Items(Align.Center).Padding(new Edges(28, 48, 0, 48)))
        using (C.Div(key: "page").FillWidth().MaxWidth(Sizes.SettingsColumn).Column().Gap(56))
        {
            if (shown.IsEmpty)
            {
                using (C.Div(key: "empty").FillWidth().Height(220).Center().Gap(6))
                {
                    Label($"No settings match “{s.Query.Trim()}”", TextSize.Setting, p.Text, key: "title");
                    Label("Try a different word, such as theme, email or invoices.", TextSize.Body, p.Secondary, key: "hint");
                }
            }
            foreach (var page in SettingsPages.Where(page => shown.Contains(page.Section)))
            {
                using var block = C.Div(key: page.Title).FillWidth().Column();
                if (s.Jump is { Arrived: false } jump && jump.Section == page.Section)
                    block.Reveal(jump.Request, revealMargin, jump.Instant ? Motion.None : Motion.Gentle);
                if (view is { } top && block.PreviousRect is { } rect)
                {
                    if (rect.Y <= top.Y + top.Height * .3f)
                        spy = page.Section;
                    if (s.Jump is { } destination && destination.Section == page.Section && MathF.Abs(rect.Y - top.Y - revealMargin) < 1)
                        atDestination = true;
                    last = rect;
                }
                SettingsSectionBody(page);
            }
            // Room below the last section so a jump can bring it to the top like every other section.
            var room = view is { } frame && last is { } end ? MathF.Max(48, frame.Height - end.Height - 28) : 48;
            using (C.Div(key: "end").FillWidth().Height(room))
            {
            }
        }
        if (shown.IsEmpty || view is null)
            return;
        var current = spy ?? SettingsPages.First(page => shown.Contains(page.Section)).Section;
        var settled = s.Jump is null or { Arrived: true } || C.Input.ScrollY != 0;
        if (settled && current != s.Section || s.Jump is { Arrived: false } pending && current == pending.Section && atDestination)
            Ui.Dispatch(new CrmMsg.ScrolledToSection(current));
    }
    bool SectionMatches(SettingsSection section) => RowsOf(section).Any(row => RowMatches(section, row));
    bool RowMatches(SettingsSection section, SettingRow row)
    {
        var words = Model.Settings.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var compare = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        var text = $"{section} {row.Title} {row.Description} {row.Keywords}";
        return words.All(word => compare.IndexOf(text, word, System.Globalization.CompareOptions.IgnoreCase) >= 0);
    }
}
