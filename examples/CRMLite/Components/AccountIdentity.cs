using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite.Components;

/// <summary>The same identity in a compact sidebar and a full account panel. No CRM model or service dependency.</summary>
public static class AccountIdentity
{
    [KumikoView]
    public static void View(Context c, Palette p, Member user, string workspace, bool canEdit, bool compact = false, NodeKey key = default)
    {
        using var root = c.Div(key: key).FillWidth().Column().Gap(4).Clip().Semantics(Role.Group, "Signed-in account", user.Email);
        c.Text(user.Name, new(Size: TextSize.Body, Color: p.Text, Weight: Weight.Medium), key: "name");
        if (!compact)
        {
            c.Text(user.Email, new(Size: TextSize.Body, Color: p.Secondary), key: "email");
            c.Text(workspace + " · " + (canEdit ? "Can edit" : "Read-only access"), new(Size: TextSize.Caption, Color: p.Secondary), key: "access");
        }
    }
}
