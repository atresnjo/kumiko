using System.Collections.Immutable;
using CRMLite;
using Kumiko.Icons;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Harbor;
using static CRMLite.Parts;

/// <summary>
/// The phone shell: the native stack for the selected section above the system tab bar, then any sheet or confirmation.
/// Colors come from the desktop Palette, so light, dark and accent settings match the desktop app.
/// </summary>
internal sealed class CrmIosView(CrmModel model) : Kumiko.Runtime.Screen<CrmModel, CrmMsg>(model)
{
    static readonly ImmutableArray<NativeTabItem> Tabs =
    [
        new(nameof(ScreenKind.Inbox), "Inbox", "tray"),
        new(nameof(ScreenKind.Companies), "Companies", "building.2"),
        new(nameof(ScreenKind.People), "People", "person.2"),
        new(nameof(ScreenKind.Deals), "Deals", "briefcase")
    ];

    protected override void Render()
    {
        var p = new Palette(Model.Dark, Model.Preferences.Accent);
        var safe = C.SafeAreaInsets;
        // UIKit's bars, switches and keyboard follow the app's theme, not only the system setting.
        C.NativeInterfaceStyle(Model.Dark);
        // Text draws the exact size and color it asks for, so the parts shared with desktop look the same here.
        C.NativeExactText();
        using (C.Div(key: "root").SizeFull().Column().Background(p.Page))
        {
            // Safe-area insets are physical: right-to-left layouts must not mirror them. Content runs to the bottom
            // edge under the floating tab bar; the stack's scroll views inset themselves by the bar's height.
            using (C.Div(key: "panes").Row().FillWidth().Flex().MinHeight(0).Padding(LayoutEdge.Top, safe.Top)
                .Padding(LayoutEdge.Left, safe.Left).Padding(LayoutEdge.Right, safe.Right))
            {
                // Same keys in every size class, so the stack and its editors survive compact/regular reflow.
                if (C.HorizontalSizeClass == SizeClass.Regular && Model.Navigation.Entries[0].Route is Screen.Companies)
                {
                    using var pane = C.Div(key: "directory-pane").Width(Length.Percent(34)).MinWidth(280).MaxWidth(380).FillHeight().Row();
                    using (C.Div(key: "directory").Flex().MinWidth(0).FillHeight())
                        Ui.Child(new CompanyDirectory(new(Model, C.NativeTabBarHeight())), "directory", static message => message);
                    using (C.Div(key: "divider").Width(1).FillHeight().Background(p.Border))
                    {
                    }
                }
                using var detail = C.Div(key: "detail-pane").Flex().MinWidth(0).FillHeight();
                C.NativeNavigationAppearance(new(p.Page, p.Text, p.AccentSoft, p.Border, BottomInset: C.NativeTabBarHeight()));
                Ui.Stack(Model.Navigation, Model, static (bound, model, entry) =>
                    ScreenRoutes.Render(bound, new CrmPage(model, entry), entry.Route),
                    static back => new CrmMsg.Back(back), static (model, entry) => PageTitle(model, entry),
                    static entry => !string.IsNullOrWhiteSpace(entry.Note), key: "crm-stack");
            }
            var unread = Crm.UnreadCount(Model);
            var badge = unread > 0 ? Format.Count(unread, C.Culture) : null;
            ImmutableArray<NativeTabItem> tabs = [.. Tabs.Select(tab => tab.Id == nameof(ScreenKind.Inbox) ? tab with { Badge = badge } : tab)];
            using (C.Div(key: "tab-dock").Absolute().Inset(LayoutEdge.Left, 0).Inset(LayoutEdge.Right, 0).Inset(LayoutEdge.Bottom, 0))
                Ui.NativeTabBar(tabs, ScreenRoutes.Kind(Model.Navigation.Entries[0].Route).ToString(),
                    static id => new CrmMsg.Navigate(Enum.Parse<ScreenKind>(id)), p.Accent, key: "tab-bar");
            if (Model.PendingNavigation is { } pending)
                ConfirmLeave(p, pending.Request);
        }
        Sheets(p);
    }

    /// <summary>The desktop leave dialog as a bottom card over a scrim that keeps touches off the covered page.</summary>
    void ConfirmLeave(Palette p, CrmBackRequest request)
    {
        using var scrim = C.Div(key: "confirm-leave").Absolute().SizeFull().Column().Justify(Justify.End).Background(p.Scrim)
            .Padding(new Edges(0, 12, MathF.Max(C.SafeAreaInsets.Bottom, 12), 12));
        using var panel = C.Div(key: "panel").FillWidth().Column().Padding(20).Gap(Space.S4).Rounded(Palette.Corners.Sheet + 4)
            .Background(p.Raised).Border(p.Border);
        Label(C, Phone, "Discard unsaved notes?", Scale.Title, p.Text, Weight.Semibold, "title");
        Label(C, Phone, "Notes in the pages you leave will be discarded.", Scale.Body, p.Secondary, key: "explanation", wrap: true);
        using var actions = C.Div(key: "actions").Column().FillWidth().Gap(Space.S2);
        Primary(Ui, p, "Discard and leave", new CrmMsg.ConfirmNavigation(request), fill: true, key: "confirm");
        Secondary(Ui, p, "Keep editing", new CrmMsg.CancelNavigation(), key: "cancel");
    }

    void Sheets(Palette p)
    {
        switch (Model.Popup)
        {
            case Popup.StageFilter:
                StageSheet(p);
                return;
            case Popup.OwnerFilter:
                OwnerSheet(p, "Owner", Model.OwnerFilter, static id => new CrmMsg.SetOwnerFilter(id));
                return;
            case Popup.BoardOwner:
                OwnerSheet(p, "Deal owner", Model.BoardOwner, static id => new CrmMsg.SetBoardOwner(id));
                return;
        }
        if (Model.Overlay is Overlay.DealDrawer drawer)
            Ui.Child(new DealSheet(new(Model, drawer.DealId)), "deal-sheet", static message => message);
    }

    void StageSheet(Palette p)
    {
        using var sheet = Ui.NativeBottomDrawer("Stage", new CrmMsg.SetPopup(Popup.None), 470, Sheet(p), key: "stage-sheet");
        Option(p, "All stages", Model.StageFilter is null, new CrmMsg.SetStageFilter(null), null, "all");
        foreach (var stage in Enum.GetValues<Stage>())
            Option(p, stage.ToString(), Model.StageFilter == stage, new CrmMsg.SetStageFilter(stage), p.Of(stage).Dot, stage.ToString());
    }

    void OwnerSheet(Palette p, string title, string? selected, Func<string?, CrmMsg> choose)
    {
        using var sheet = Ui.NativeBottomDrawer(title, new CrmMsg.SetPopup(Popup.None), 430, Sheet(p), key: "owner-sheet");
        Option(p, "All owners", selected is null, choose(null), null, "all");
        foreach (var member in Data.Team)
        {
            using var row = Ui.NativePressable(member.Name, choose(member.Id), key: member.Id);
            row.FillWidth();
            using var face = C.Div(key: "face").Row().FillWidth().Height(48).ItemsCenter().Gap(12).Px(4);
            Avatar(C, p, Phone, member, 28, "avatar");
            using (C.Div(key: "name").Flex().MinWidth(0))
                Label(C, Phone, member.Id == Model.CurrentUser.Id ? member.Name + " (you)" : member.Name, Scale.Row, p.Text, key: "text");
            if (selected == member.Id)
                C.Icon(Hugeicons.Tick02, 18, p.Accent, key: "check");
        }
    }

    void Option(Palette p, string label, bool on, CrmMsg message, Color? dot, NodeKey key)
    {
        using var row = Ui.NativePressable(label, message, key: key);
        row.FillWidth();
        using var face = C.Div(key: "face").Row().FillWidth().Height(48).ItemsCenter().Gap(12).Px(4);
        if (dot is { } color)
            Dot(C, color, 9, "dot");
        using (C.Div(key: "name").Flex().MinWidth(0))
            Label(C, Phone, label, Scale.Row, p.Text, on ? Weight.Medium : Weight.Regular, "text");
        if (on)
            C.Icon(Hugeicons.Tick02, 18, p.Accent, key: "check");
    }
}
