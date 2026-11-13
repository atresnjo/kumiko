using CRMLite.Components;
using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void Sidebar()
    {
        var culture = C.Culture;
        using var outer = C.Div(key: "sidebar").Row().FillHeight();
        var adaptiveRail = ForcedRail;
        var reveal = adaptiveRail ? 0 : outer.Animate(Model.SidebarCollapsed ? 0 : 1, Motion.Gentle);
        var fade = Fade(reveal);
        var width = MathF.Round(Sizes.Rail + (Model.SidebarWidth - Sizes.Rail) * reveal);
        using (C.Div(key: "content").Column().Width(width).FillHeight().Padding(new Edges(10, 10, 8, 10)).Gap(2)
            .Background(p.Sidebar).Clip().Semantics(Role.Group, "Sidebar"))
        {
            Workspace(fade);
            SearchBox(fade);
            using (C.Div(key: "gap").Height(8))
            {
            }
            var unread = Crm.UnreadCount(Model);
            (ScreenKind Page, string Label, string Icon, int Count)[] pages =
            [
                (ScreenKind.Inbox, "Inbox", Hugeicons.Inbox, unread),
                (ScreenKind.Companies, "Companies", Hugeicons.Building03, Model.Companies.Length),
                (ScreenKind.People, "People", Hugeicons.UserMultiple, Model.Contacts.Length),
                (ScreenKind.Deals, "Deals", Hugeicons.Briefcase01, Model.Deals.Length)
            ];
            foreach (var (page, label, icon, count) in pages)
            {
                var selected = ScreenRoutes.Kind(Model.Screen) == page && (page != ScreenKind.Companies || Model.List == CompanyList.All);
                var strong = page == ScreenKind.Inbox && count > 0;
                Ui.OnPress(NavItem(label, () => C.Icon(icon, 16, selected ? p.Accent : p.Secondary, key: "icon"), Format.Count(count, culture), selected, fade, strong, key: label), page == ScreenKind.Companies ? new CrmMsg.ShowList(CompanyList.All) : new CrmMsg.Navigate(page));
            }
            Section("Favorites", fade, "favorites-label");
            foreach (var id in Model.Favorites)
            {
                if (Crm.Company(Model, id) is not { } company)
                    continue;
                var selected = ScreenRoutes.Kind(Model.Screen) == ScreenKind.Company && Model.CompanyId == id;
                Ui.OnPress(NavItem(company.Name, () => Logo(company, 16, "logo"), null, selected, fade, key: "fav-" + id), new CrmMsg.OpenCompany(id));
            }
            Section("Lists", fade, "lists-label");
            (CompanyList List, string Label, string Icon)[] lists = [(CompanyList.Mine, "My companies", Hugeicons.User), (CompanyList.Hot, "Hot accounts", Hugeicons.Fire)];
            foreach (var (list, label, icon) in lists)
            {
                var count = Crm.InList(Model, list).Count();
                var selected = ScreenRoutes.Kind(Model.Screen) == ScreenKind.Companies && Model.List == list;
                Ui.OnPress(NavItem(label, () => C.Icon(icon, 16, selected ? p.Accent : p.Secondary, key: "icon"), Format.Count(count, culture), selected, fade, key: label), new CrmMsg.ShowList(list));
            }
            C.Spacer(key: "space");
            if (fade > 0 && !Model.Settings.Open)
            {
                using var status = C.Div(key: "sync").FillWidth().Opacity(fade).Clip();
                BackendStatus();
            }
            Rule("rule");
            Account(fade);
        }
        VRule("border");
        if (adaptiveRail || Model.SidebarCollapsed || reveal < 1)
            return;
        // The 6 px grip sits just inside the border, clear of the page's controls. Its own rule is hidden; while
        // hovered or dragged it draws an accent line that joins the border into one highlighted edge.
        using (C.Div(key: "edge").Absolute().Offset(width - 5, 0).Width(6).FillHeight())
        using (C.Kit.Use(p.Kit with
        {
            Border = Color.Transparent
        }))
            Ui.ResizeHandle("Sidebar width", Model.SidebarWidth, w => new CrmMsg.ResizeSidebar(w),
                new(Min: CrmModel.MinSidebar, Max: CrmModel.MaxSidebar), key: "handle");
    }

    /// <summary>Label opacity for a reveal value: hidden below 45% open, then a smooth rise to fully shown.</summary>
    static float Fade(float reveal)
    {
        var t = Math.Clamp((reveal - .45f) / .55f, 0, 1);
        return t * t * (3 - 2 * t);
    }
    bool Caption(Interaction hit, string name, float fade, Action content)
    {
        if (fade <= 0)
        {
            C.Kit.Tooltip(hit, name, new(Side.Right), key: "tip");
            return false;
        }
        using (C.Div(key: "caption").Flex().FillHeight().Row().ItemsCenter().Gap(6).Padding(new Edges(0, 0, 0, 8)).Clip().Opacity(fade))
            content();
        return true;
    }
    void Section(string text, float fade, NodeKey key)
    {
        using var box = C.Div(key: key).FillWidth().Height(36).Clip();
        if (fade > 0)
        {
            using (C.Div(key: "text").Absolute().Offset(8, 14).Opacity(fade))
                Label(text, TextSize.Small, p.Faint, key: "label");
        }
        if (fade < 1)
        {
            using (C.Div(key: "rule").Absolute().Offset(8, 22).Width(16).Height(1).Background(p.Border).Opacity(1 - fade))
            {
            }
        }
    }
    Interaction NavItem(string label, Action leading, string? count, bool selected, float fade, bool strong = false, NodeKey key = default)
    {
        using var item = C.Div(key: key).Row().FillWidth().Height(Sizes.NavItem).ItemsCenter().Rounded(Corners.Button)
            .Semantics(Role.Button, label).Accessibility(new(Selected: selected));
        var hit = item.Control();
        if (!selected)
            Feedback(item, hit);
        else if (hit.Hovered)
            C.Cursor = Cursor.Hand;
        if (selected)
        {
            // One selection card for the whole sidebar glides to the selected item.
            using var card = C.Div(key: "selection").Absolute().SizeFull().Rounded(Corners.Button).Background(p.Page).Border(p.Border)
                .SharedLayout("sidebar-selection");
            if (!p.Dark)
                card.Shadow(2, 1, p.Shade);
            if (hit.Focused && C.FocusVisible)
                card.AccentOutline(p);
        }
        using var content = C.Div(key: "content").Row().FillWidth().FillHeight().ItemsCenter().Px(8);
        leading();
        Caption(hit, label, fade, () =>
        {
            using (C.Div(key: "label").Flex().Clip())
                Label(label, TextSize.Nav, selected ? p.Text : p.Secondary, key: "text");
            if (count is not null)
                Label(count, TextSize.Body, selected ? p.AccentSoft : strong ? p.Text : p.Faint, strong ? Weight.Semibold : Weight.Regular, "count");
        });
        return hit;
    }
    void Workspace(float fade)
    {
        Rect? anchor;
        using (var row = C.Div(key: "workspace").Row().FillWidth().Height(32).ItemsCenter().Px(7).Rounded(Corners.Button)
            .Semantics(Role.Button, "Workspace", "Harbor").Accessibility(new(Expanded: Model.Popup == Popup.Workspace)))
        {
            var hit = row.Control();
            anchor = hit.Rect;
            Feedback(row, hit, Model.Popup == Popup.Workspace);
            using (C.Div(key: "mark").Width(18).Height(18))
            {
                using (C.Div(key: "back").Absolute().Offset(6, 0).Width(12).Height(12).Rounded(3).Background(p.Accent.Alpha(.5f)))
                {
                }
                using (C.Div(key: "front").Absolute().Offset(0, 6).Width(12).Height(12).Rounded(3).Background(p.Accent))
                {
                }
            }
            Caption(hit, "Harbor", fade, () =>
            {
                Label("Harbor", TextSize.Title, p.Text, Weight.Semibold, "name");
                C.Icon(Hugeicons.UnfoldMore, 11, p.Faint, key: "chevron");
            });
            Ui.OnPress(hit, new CrmMsg.SetPopup(Model.Popup == Popup.Workspace ? Popup.None : Popup.Workspace));
        }
        if (Model.Popup != Popup.Workspace)
            return;
        var kit = C.Kit;
        using var menu = kit.Popover(anchor, () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), new(Width: 220, Name: "Workspaces"), key: "workspace-menu");
        kit.MenuLabel("Workspaces", key: "label");
        Ui.OnPress(kit.MenuItem("Harbor", new(Hugeicons.Building03, Checked: true), key: "harbor"), new CrmMsg.SetPopup(Popup.None));
    }
    void SearchBox(float fade)
    {
        var kit = C.Kit;
        using (var box = C.Div(key: "search").Row().FillWidth().Height(34).ItemsCenter().Px(8).Rounded(Corners.Button)
            .Background(p.Page).Border(p.Border).Semantics(Role.Button, "Search").Accessibility(new(Expanded: Model.Popup == Popup.Search)))
        {
            var hit = box.Control();
            if (hit.Focused && C.FocusVisible)
                box.AccentOutline(p);
            if (hit.Hovered)
                C.Cursor = Cursor.Hand;
            C.Icon(Hugeicons.Search01, 16, p.Text, key: "icon");
            Caption(hit, "Search", fade, () =>
            {
                using (C.Div(key: "hint").Flex().Clip())
                    Label("Search", TextSize.Nav, p.Text, key: "text");
                kit.Kbd(Kit.ShortcutLabel("F"), key: "shortcut");
            });
            Ui.OnPress(hit, new CrmMsg.SetPopup(Model.Popup == Popup.Search ? Popup.None : Popup.Search));
        }
    }
    void Account(float fade)
    {
        var me = Model.CurrentUser;
        using var row = C.Div(key: "account").Row().FillWidth().Height(44).ItemsCenter().Px(3).Rounded(Corners.Button)
            .Semantics(Role.Button, "Account", me.Name);
        var hit = row.Control();
        Feedback(row, hit, Model.Settings.Open);
        Avatar(me, 26, "avatar");
        Caption(hit, me.Name, fade, () =>
        {
            using (C.Div(key: "name").Flex().Clip())
                AccountIdentity.View(C, p, me, Model.Auth.Session!.Workspace, Model.Auth.Session.CanEdit, compact: true, key: "identity");
            C.Icon(Hugeicons.Settings01, 13, p.Faint, key: "settings");
        });
        Ui.OnPress(hit, new CrmMsg.OpenSettings());
    }
}
