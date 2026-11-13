using System.Collections.Immutable;
using CRMLite.Components;
using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    const float FieldWidth = (Sizes.Dialog - 2 * Space.S5 - Space.S4) / 2;
    void DealDialog(DealDraft d)
    {
        var errors = d.Submitted ? Crm.DealErrors(Model, d) : default;
        using var overlay = Backdrop("deal-dialog");
        using var panel = Panel("New deal");
        using var form = Ui.Form(key: "form", onSubmit: new CrmMsg.SubmitDeal(), label: "Deal details");
        DialogHead("New deal", "It lands on the board in the stage you pick.");
        using (C.Div(key: "body").FillWidth().Column().Gap(Space.S4).Padding(Space.S5))
        {
            using (C.Div(key: "row1").Row().FillWidth().Gap(Space.S4).Items(Align.Start))
            {
                FormField("Company", errors.Company, "company", () => CompanyField(errors.Company is not null));
                FormField("Deal name", errors.Name, "name", () =>
                    Ui.TextField(key: "input", label: "Deal name", value: d.Name,
                        onChange: text => new CrmMsg.EditDeal(d with { Name = text }),
                        options: new("Annual expansion", FieldWidth, Error: errors.Name)));
            }
            using (C.Div(key: "row2").Row().FillWidth().Gap(Space.S4).Items(Align.Start))
            {
                FormField("Value", errors.Value, "value", () =>
                    Ui.TextField(key: "input", label: "Value", value: d.Value,
                        onChange: text => new CrmMsg.EditDeal(d with { Value = text }),
                        options: new("$120k", FieldWidth, Error: errors.Value)));
                FormField("Stage", null, "stage", () =>
                {
                    var open = Model.Popup == Popup.DraftStage;
                    var dot = p.Of(d.Stage).Dot;
                    var hit = Select("Stage", d.Stage.ToString(), "", open, false, () => Dot(dot, 7, "dot"), "select");
                    Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : Popup.DraftStage));
                    if (open)
                        StageMenu(hit.Rect, d.Stage, stage => Ui.Dispatch(new CrmMsg.EditDeal(d with { Stage = stage })), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), "stage-menu", true);
                });
            }
            using (C.Div(key: "row3").Row().FillWidth().Gap(Space.S4).Items(Align.Start))
                OwnerField(d.OwnerId, Model.Popup == Popup.DraftOwner, Popup.DraftOwner, id => Ui.Dispatch(new CrmMsg.EditDeal(d with { OwnerId = id })));
        }
        Rule("body-rule");
        DialogFoot("Create deal");
    }
    void CompanyDialog(CompanyDraft d)
    {
        var errors = d.Submitted ? Crm.CompanyErrors(d) : default;
        using var overlay = Backdrop("company-dialog");
        using var panel = Panel("New company");
        using var form = Ui.Form(key: "form", onSubmit: new CrmMsg.SubmitCompany(), label: "Company details");
        DialogHead("New company", "Add an account to track its deals and activity.");
        using (C.Div(key: "body").FillWidth().Column().Gap(Space.S4).Padding(Space.S5))
        {
            using (C.Div(key: "row1").Row().FillWidth().Gap(Space.S4).Items(Align.Start))
            {
                FormField("Company name", errors.Name, "name", () =>
                    Ui.TextField(key: "input", label: "Company name", value: d.Name,
                        onChange: text => new CrmMsg.EditCompany(d with { Name = text }),
                        options: new("Acme Inc.", FieldWidth, Error: errors.Name)));
                FormField("Domain", errors.Domain, "domain", () =>
                    Ui.TextField(key: "input", label: "Domain", value: d.Domain,
                        onChange: text => new CrmMsg.EditCompany(d with { Domain = text }),
                        options: new("acme.com", FieldWidth, Error: errors.Domain)));
            }
            using (C.Div(key: "row2").Row().FillWidth().Gap(Space.S4).Items(Align.Start))
            {
                FormField("Industry", null, "industry", () =>
                    Ui.TextField(key: "input", label: "Industry", value: d.Industry,
                        onChange: text => new CrmMsg.EditCompany(d with { Industry = text }),
                        options: new("Software", FieldWidth)));
                OwnerField(d.OwnerId, Model.Popup == Popup.NewCompanyOwner, Popup.NewCompanyOwner, id => Ui.Dispatch(new CrmMsg.EditCompany(d with { OwnerId = id })));
            }
        }
        Rule("body-rule");
        DialogFoot("Create company");
    }
    Div Backdrop(NodeKey key)
    {
#pragma warning disable KUM001 // The caller disposes the overlay.
        var overlay = C.Overlay(key).Center().TabStop(false).Background(p.Scrim).Semantics(Role.Group).Enter(Transition.Fade)
            .Exit(Transition.Fade with
            {
                Motion = Motion.Quick
            });
#pragma warning restore KUM001
        var hit = overlay.Control();
        var idle = Model.Popup == Popup.None;
        if (idle && (hit.Clicked || C.Shortcut(Key.Escape, Modifiers.None)))
            Ui.Dispatch(new CrmMsg.CloseDialog());
        return overlay;
    }
    Div Panel(string title)
    {
#pragma warning disable KUM001 // The caller disposes the panel.
        var panel = C.Div(key: "panel").RaisedSurface(p).Width(MathF.Min(Sizes.Dialog, MathF.Max(0, C.Size.Width - 32)))
            .TabStop(false).Semantics(Role.Dialog, title).Enter(Transition.Pop)
            .Exit(Transition.Pop with
            {
                Motion = Motion.Quick
            });
#pragma warning restore KUM001
        panel.Shadow(32, 12, Color.Black.Alpha(p.Dark ? .5f : .16f));
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        return panel;
    }
    void DialogHead(string title, string description)
    {
        using (C.Div(key: "head").Row().FillWidth().Items(Align.Start).Gap(Space.S3).Padding(new Edges(Space.S5, Space.S5, 18, Space.S5)))
        {
            using (C.Div(key: "titles").Flex().Column().Gap(6))
            {
                C.Text(title, new(Size: TextSize.Dialog, Weight: Weight.Semibold, Color: p.Text), key: "title");
                C.Text(description, new(Size: TextSize.Body, Color: p.Secondary, Wrap: true), key: "description");
            }
            using var close = C.Div(key: "close").IconAction(30).Rounded(Corners.Button).Border(p.Border).Background(p.Page)
                .Semantics(Role.Button, "Close");
            var hit = close.Control();
            Feedback(close, hit);
            C.Icon(Hugeicons.Cancel01, 12, p.Secondary, key: "icon");
            Ui.OnPress(hit, new CrmMsg.CloseDialog());
        }
        Rule("head-rule");
    }
    void DialogFoot(string action)
    {
        using var foot = C.Div(key: "foot").Row().FillWidth().ItemsCenter().Justify(Justify.End).Gap(Space.S2).Padding(new Edges(14, Space.S5, 14, Space.S5));
        Ui.OnPress(ActionButton.Ghost(C, p, "Cancel", null, "Cancel", key: "cancel"), new CrmMsg.CloseDialog());
        using var button = C.Div(key: "submit").PrimaryAction(p).Height(32).Gap(8).Padding(new Edges(0, 6, 0, 12))
            .Semantics(Role.Button, action);
        var hit = Ui.Submit(button, action, true);
        if (hit.Hovered)
        {
            button.Background(Color.Mix(p.Accent, Color.Black, .1f));
            C.Cursor = Cursor.Hand;
        }
        if (hit.Focused && C.FocusVisible)
            button.Border(p.Accent.Alpha(.45f), 3);
        Label(action, TextSize.Body, Color.White, Weight.Medium, "label");
        using (C.Div(key: "keys").Height(20).Px(6).Center().Rounded(Corners.Tag).Background(Color.Black.Alpha(.22f)))
            Label(Kit.ShortcutLabel("↵"), TextSize.Caption, Color.White, Weight.Medium, "text");
    }
    void FormField(string label, string? error, NodeKey key, Action control)
    {
        using var field = C.Div(key: key).Width(FieldWidth).Column().Gap(7);
        Label(label, TextSize.Body, p.Text, Weight.Medium, "label");
        control();
        if (error is not null)
            C.Text(error, new(Size: TextSize.Caption, Color: p.Danger, Wrap: true), key: "error");
    }
    Interaction Select(string name, string? value, string placeholder, bool open, bool invalid, Action? leading, NodeKey key)
    {
        using var box = C.Div(key: key).FieldSurface(p, invalid).Width(FieldWidth).Semantics(Role.ComboBox, name, value)
            .Accessibility(new(Expanded: open));
        var hit = box.Control();
        if (hit.Hovered)
            C.Cursor = Cursor.Hand;
        if (hit.Focused && C.FocusVisible || open)
            box.AccentOutline(p);
        leading?.Invoke();
        using (C.Div(key: "value").Flex().Clip())
            Label(value ?? placeholder, TextSize.Body, value is null ? p.Faint : p.Text, key: "text");
        C.Icon(Hugeicons.ArrowDown01, 11, p.Secondary, key: "chevron");
        return hit;
    }
    void OwnerField(string ownerId, bool open, Popup popup, Action<string> chosen)
    {
        var currentUserId = Model.CurrentUser.Id;
        FormField("Owner", null, "owner", () =>
        {
            var owner = Crm.Member(ownerId);
            var label = owner.Id == Model.CurrentUser.Id ? $"{owner.Name} (you)" : owner.Name;
            var hit = Select("Owner", label, "", open, false, () => Avatar(owner, 18, "avatar"), "select");
            Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : popup));
            if (open)
                OwnerMenu(hit.Rect, ownerId, id => chosen(id ?? currentUserId), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), "owner-menu", false);
        });
    }
    void CompanyField(bool invalid)
    {
        var search = (Model.Overlay as Overlay.NewDeal)?.Search ?? new(new());
        ImmutableArray<Choice> results =
        [
            .. search.Results.Select(id => Crm.Company(Model, id)).OfType<Company>()
                .OrderBy(company => company.OwnerId != Model.CurrentUser.Id)
                .ThenBy(company => company.Name, StringComparer.Ordinal)
                .Select(company => new Choice(company.Id, company.Name)
                {
                    Section = company.OwnerId == Model.CurrentUser.Id ? "Your accounts" : "Team accounts",
                    Detail = company.Domain
                })
        ];
        Ui.Dropdown("select", "Company", search.Picker with
        {
            IsOpen = Model.Popup == Popup.DraftCompany
        }, results,
            state => new CrmMsg.PickCompany(state), new(FieldWidth, "Choose a company", "No companies match", Remote: true,
                Loading: search.Loading, LoadError: search.Error, Invalid: invalid));
    }
}
