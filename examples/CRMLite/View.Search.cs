using System.Collections.Immutable;
using System.Globalization;
using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    /// <summary>A search result: a "page:", "company:" or "contact:" target with its group and description.</summary>
    sealed record Hit(string Target, string Label, string Group, string Icon, string Hint, string Keywords = "");

    static readonly ImmutableArray<Hit> PageHits =
    [
        new("page:Inbox", "Inbox", "Pages", Hugeicons.Inbox, "Unread threads from your accounts."),
        new("page:Companies", "Companies", "Pages", Hugeicons.Building03, "Every account with stage, ARR and next step."),
        new("page:People", "People", "Pages", Hugeicons.UserMultiple, "Contacts at your companies."),
        new("page:Deals", "Deals", "Pages", Hugeicons.Briefcase01, "The pipeline board and forecast.")
    ];
    ImmutableArray<Hit> SearchHits(CultureInfo culture)
    {
        ImmutableArray<Hit> all =
        [
            .. PageHits,
            .. Model.Companies.Select(co => new Hit("company:" + co.Id, co.Name, "Companies", Hugeicons.Building03, $"{co.Industry} · {co.Domain}", co.NextStep)),
            .. Model.Contacts.Select(person => new Hit("contact:" + person.Id, person.Name, "People", Hugeicons.User,
                $"{person.Title} at {Crm.Company(Model, person.CompanyId)?.Name}", person.Email))
        ];
        var words = Model.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var compare = culture.CompareInfo;
        return [.. all.Where(hit => words.All(word =>
            compare.IndexOf($"{hit.Label} {hit.Hint} {hit.Keywords}", word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))];
    }
    void SearchPalette()
    {
        var hits = SearchHits(C.Culture);
        using var overlay = C.Overlay("search-palette").Column().Items(Align.Center).Padding(new Edges(MathF.Round(C.Size.Height * .1f), 24, 24, 24))
            .TabStop(false).Background(p.Scrim).Semantics(Role.Group).Enter(Transition.Fade).Exit(Transition.Fade with
            {
                Motion = Motion.Quick
            });
        var backdrop = overlay.Control();
        // The palette owns the keyboard, so Primary+F closes it here; the Search… action only opens it.
        if (backdrop.Clicked || C.Shortcut(Key.Escape, Modifiers.None) || C.Shortcut(Key.F))
            Ui.Dispatch(new CrmMsg.SetPopup(Popup.None));
        using var panel = C.Div(key: "panel").RaisedSurface(p).Width(MathF.Min(Sizes.Palette, C.Size.Width - 48))
            .Rounded(Corners.Palette).Clip().TabStop(false).Semantics(Role.Group, "Search")
            .Enter(Transition.Pop).Exit(Transition.Pop with
            {
                Motion = Motion.Quick
            });
        panel.Shadow(40, 16, Color.Black.Alpha(p.Dark ? .55f : .2f));
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        var send = Ui.BindDispatcher();
        var width = MathF.Min(Sizes.Palette, C.Size.Width - 48);
        var items = hits.Select(hit => new CommandItem(hit.Target, hit.Label, hit.Group, hit.Icon, hit.Hint,
            $"{hit.Hint} {hit.Keywords}")
        {
            Description = hit.Hint
        }).ToArray();
        using var commandTheme = C.Kit.Use(C.Kit.Theme with
        {
            Radius = Corners.Card + 2
        });
        C.Kit.CommandMenu(null, Model.Query, text => send(new CrmMsg.SetQuery(text)), items,
            target => send(new CrmMsg.Jump(target)), () => send(new CrmMsg.SetPopup(Popup.None)),
            new(Placeholder: "Search companies, people and pages", Width: width, MaxHeight: MathF.Max(160, C.Size.Height * .6f))
            {
                Embedded = true,
                ResultsPadding = Space.S2,
                ResultsGap = 0,
                RenderSearch = _ => SearchHeader(),
                RenderGroup = (_, group) => SearchGroup(group),
                RenderEmpty = _ => SearchEmpty(),
                RenderItem = (_, item, state) => Result(item, state)
            }, key: "commands");
    }
    void SearchHeader()
    {
        using var head = C.Div(key: "head").Row().FillWidth().Height(56).ItemsCenter().Gap(Space.S3).Px(Space.S4);
        C.Icon(Hugeicons.Search01, 18, p.Faint, key: "icon");
        using (var field = C.Div(key: "field").Flex())
            QueryField(MathF.Max(field.PreviousRect?.Width ?? 360, 120));
        Chip("Esc", "esc");
        Chip(Kit.ShortcutLabel("F"), "shortcut");
    }
    void SearchGroup(string group)
    {
        using (C.Div(key: "heading").FillWidth().Padding(new Edges(10, 10, 6, 10)))
            Label(group, TextSize.Small, p.Faint, Weight.Medium, "text");
    }
    void SearchEmpty()
    {
        using (C.Div(key: "message").FillWidth().Height(96).Center())
            Label($"No results for “{Model.Query.Trim()}”", TextSize.Body, p.Secondary, key: "text");
    }
    void QueryField(float width) =>
            BareField("query", "Search companies, people and pages", Model.Query, text => new CrmMsg.SetQuery(text), width, TextSize.Search);
    void BareField(NodeKey key, string label, string value, Func<string, CrmMsg> changed, float width, float size)
    {
        var theme = C.Theme;
        C.Theme = theme with
        {
            Surface = Color.Transparent,
            InputBorder = Color.Transparent,
            FocusRing = Color.Transparent,
            Typography = theme.Typography with
            {
                Body = theme.Typography.Body with
                {
                    Size = size
                }
            }
        };
        Ui.TextField(key: key, label: label, value: value, onChange: changed, options: new(label, width));
        C.Theme = theme;
    }
    void Chip(string text, NodeKey key)
    {
        using (C.Div(key: key).Height(24).Px(8).Center().Rounded(Corners.Control).Border(p.Border))
            Label(text, TextSize.Caption, p.Faint, Weight.Medium, "text");
    }
    void Result(CommandItem item, CommandItemState state)
    {
        using var row = C.Div(key: "content").Row().FillWidth().Height(38).ItemsCenter().Gap(Space.S3).Px(10);
        if (state.Hovered)
            C.Cursor = Cursor.Hand;
        C.Icon(item.Icon!, 16, p.Secondary, key: "icon");
        Label(item.Label, TextSize.Title, state.Foreground, Weight.Medium, "label");
        using (C.Div(key: "hint").Flex().Clip())
            Label(item.Hint!, TextSize.Body, state.Highlighted ? p.Secondary : p.Faint, key: "text");
    }
}
