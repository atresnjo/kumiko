using CRMLite.Components;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void ConfirmLeave(CrmBackRequest request)
    {
        using var overlay = C.Overlay("confirm-leave").Center().Background(p.Scrim);
        using var panel = C.Div(key: "panel").Column().Width(380).Padding(Space.S5).Gap(Space.S4)
            .Background(p.Page).Rounded(Corners.Card).TabStop(false).Semantics(Role.Dialog, "Unsaved note");
        // The decision owns the keyboard: typing cannot continue in the covered draft behind it.
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        if (C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new CrmMsg.CancelNavigation());
        C.Text("Discard unsaved notes?", key: "title");
        C.Text("Notes in the pages you leave will be discarded.", Typography.Body with
        {
            Wrap = true
        }, key: "explanation");
        using var actions = C.Div(key: "actions").Row().Gap(Space.S3);
        Ui.OnPress(ActionButton.Ghost(C, p, "Keep editing", null, "Keep editing", key: "cancel"), new CrmMsg.CancelNavigation());
        Ui.OnPress(ActionButton.Primary(C, p, "Discard and leave", null, key: "confirm"), new CrmMsg.ConfirmNavigation(request));
    }
}
