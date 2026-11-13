using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void Table()
    {
        var rows = Crm.Visible(Model);
        using var viewport = C.Div(key: "table").FillWidth().Flex().ScrollX().Semantics(Role.ScrollArea, "Company columns");
        var width = MathF.Max(viewport.PreviousRect?.Width ?? Sizes.TableMin, Sizes.TableMin);
        using var grid = C.Div(key: "grid").Column().Width(width).FillHeight().Semantics(Role.Grid, "Companies");
        HeaderRow();
        using (C.Div(key: "rows").FillWidth().Flex().Scroll().Semantics(Role.ScrollArea, "Company rows"))
        {
            foreach (var company in rows)
                CompanyRow(company);
            if (rows.Length == 0)
            {
                using (C.Div(key: "empty").FillWidth().Height(120).Center())
                    Label("No companies match these filters.", TextSize.Body, p.Secondary, key: "text");
            }
        }
        Footer(rows.Sum(co => Crm.Arr(Model, co.Id)), rows.Length);
        RowMenu();
    }
    void RowMenu()
    {
        if (Model.Overlay is not Overlay.RowMenu { } open || Crm.Company(Model, open.CompanyId) is not { } company)
            return;
        var kit = C.Kit;
        using var menu = kit.Popover(new Rect(open.At.X, open.At.Y, 0, 0), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)),
            new(Width: 230, Offset: 2, Name: $"{company.Name} actions"), key: "row-menu");
        if (open.OwnerCell)
        {
            kit.MenuLabel("Owner", key: "owner-label");
            foreach (var member in Data.Team)
                Ui.OnPress(kit.MenuItem(member.Name, new(Checked: member.Id == company.OwnerId, Disabled: !Model.CanEdit), key: "owner-" + member.Id),
                    new CrmMsg.SetOwner(company.Id, member.Id));
            kit.MenuSeparator(key: "owner-rule");
        }
        Ui.OnPress(kit.MenuItem("Open company", new(Hugeicons.ArrowUpRight01), key: "open"), new CrmMsg.OpenCompany(company.Id));
        Ui.OnPress(kit.MenuItem("New deal…", new(Hugeicons.Add01, Disabled: !Model.CanEdit), key: "deal"), new CrmMsg.OpenDealDialog(company.Id));
        kit.MenuSeparator(key: "rule");
        Ui.OnPress(kit.MenuItem(company.Hot ? "Remove from hot accounts" : "Mark as hot account", new(Hugeicons.Fire, Disabled: !Model.CanEdit), key: "hot"),
            new CrmMsg.SetHot(company.Id, !company.Hot));
        var favorite = Model.Favorites.Contains(company.Id);
        Ui.OnPress(kit.MenuItem(favorite ? "Remove from favorites" : "Add to favorites", new(Hugeicons.Star, Disabled: !Model.CanEdit), key: "favorite"),
            new CrmMsg.ToggleFavorite(company.Id));
    }
    void HeaderRow()
    {
        using (C.Div(key: "head").TableHeaderRow())
        {
            foreach (var column in Columns)
            {
                if (column.Key == SortKey.Stage)
                    Divider("divider");
                using var cell = Cell(column, column.Label);
                cell.Semantics(Role.ColumnHeader, column.Label);
                var sorted = column.Key is { } key && Model.Sort == key;
                if (column.Key is { } sortable)
                {
                    var hit = cell.Control();
                    if (hit.Hovered)
                        C.Cursor = Cursor.Hand;
                    if (hit.Focused && C.FocusVisible)
                        cell.AccentOutline(p);
                    Ui.OnPress(hit, new CrmMsg.SortBy(sortable));
                }
                Label(column.Label, TextSize.Caption, sorted ? p.Text : p.Faint, sorted ? Weight.Medium : Weight.Regular, "label");
                if (sorted)
                    C.Icon(Model.Descending ? Hugeicons.ArrowDown02 : Hugeicons.ArrowUp02, 10, p.Text, key: "arrow");
            }
        }
        Rule("head-rule");
    }
    Div Cell(Column column, NodeKey key)
    {
#pragma warning disable KUM001 // The caller disposes the cell.
        var cell = C.Div(key: key).Row().FillHeight().ItemsCenter().Gap(6);
#pragma warning restore KUM001
        if (column.Width > 0)
            cell.Width(column.Width);
        else
            cell.Flex().Clip();
        if (column.Right)
            cell.Justify(Justify.End).Padding(new Edges(0, 18, 0, 0));
        if (column.Key == SortKey.Owner)
            cell.Padding(new Edges(0, 0, 0, 22));
        return cell;
    }
    void Divider(NodeKey key)
    {
        using (C.Div(key: key).Width(13).FillHeight().Row())
        {
            VRule("line");
        }
    }
    void CompanyRow(Company company)
    {
        var culture = C.Culture;
        using var row = C.Div(key: company.Id).Column().FillWidth().Height(Sizes.Row).Semantics(Role.Row, company.Name);
        var hit = row.Control();
        Feedback(row, hit, Model.Overlay is Overlay.RowMenu { } menu && menu.CompanyId == company.Id);
        Ui.OnPress(hit, new CrmMsg.OpenCompany(company.Id));
        Rect? ownerCell = null;
        using (C.Div(key: "cells").TableBodyRow())
        {
            var owner = Crm.Member(company.OwnerId);
            foreach (var column in Columns)
            {
                if (column.Key == SortKey.Stage)
                    Divider("divider");
                using var cell = Cell(column, column.Label);
                if (column.Key == SortKey.Owner)
                    ownerCell = cell.PreviousRect;
                switch (column.Key)
                {
                    case SortKey.Company:
                        Logo(company, 16, "logo");
                        Label(company.Name, TextSize.Body, p.Text, Weight.Medium, "name");
                        if (company.Hot)
                            C.Icon(Hugeicons.Fire, 11, p.Flame, key: "hot", accessibleName: "Hot account");
                        break;
                    case SortKey.Stage when Crm.StageOf(Model, company.Id) is { } stage:
                        StageTag(stage, "tag");
                        break;
                    case SortKey.Stage:
                        Label("No deal", TextSize.Body, p.Faint, key: "none");
                        break;
                    case SortKey.Arr:
                        var arr = Crm.Arr(Model, company.Id);
                        Label(arr > 0 ? Format.Money(arr, culture) : "—", TextSize.Body, p.Text, key: "value");
                        break;
                    case SortKey.Employees:
                        Label(company.Employees > 0 ? Format.Count(company.Employees, culture) : "—", TextSize.Body, p.Text, key: "value");
                        break;
                    case SortKey.Owner:
                        Avatar(owner, 18, "avatar");
                        Label(owner.Name, TextSize.Body, p.Text, key: "name");
                        break;
                    case SortKey.LastInteraction:
                        var stale = Format.Stale(company.LastInteraction, Model.Now);
                        if (stale)
                            Dot(p.Stale, 5, "dot");
                        Label(Format.Relative(company.LastInteraction, Model.Now, culture), TextSize.Body, stale ? p.Stale : p.Secondary, key: "value");
                        break;
                    default:
                        Label(company.NextStep, TextSize.Body, p.Secondary, key: "value");
                        break;
                }
            }
        }
        using (C.Div(key: "line").FillWidth().Height(1).Background(p.Line))
        {
        }
        Ui.OnContextMenu(hit, at => new CrmMsg.OpenRowMenu(company.Id, at, ownerCell?.Contains(at) == true));
    }
    void Footer(long sum, int count)
    {
        var culture = C.Culture;
        using var footer = C.Div(key: "footer").Row().FillWidth().Height(36).ItemsCenter().Px(Spacing.Gutter);
        using (Cell(Columns[0], "count"))
            Label(count == 1 ? "1 company" : $"{Format.Count(count, culture)} companies", TextSize.Caption, p.Faint, key: "text");
        Divider("divider");
        using (Cell(Columns[1], "stage"))
        {
        }
        using (Cell(Columns[2], "sum"))
        {
            Label("Sum", TextSize.Caption, p.Faint, key: "label");
            Label(Format.Short(sum, culture), TextSize.Caption, p.Text, Weight.Semibold, "value");
        }
    }
}
