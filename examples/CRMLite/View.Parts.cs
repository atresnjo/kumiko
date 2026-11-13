using System.Collections.Immutable;
using CRMLite.Components;
using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

/// <summary>Small building blocks shared by every page.</summary>
public sealed partial class CrmView
{
    void Property(string label, NodeKey key, Action value) =>
            PropertyRow.View(C, p, label, value, PropertyRowProps.Compact, key);
    void DrawerProperty(string label, NodeKey key, Action value) =>
            PropertyRow.View(C, p, label, value, PropertyRowProps.Comfortable, key);
    /// <summary>The desktop sizes of the shared <see cref="Parts"/>; the phone's are in CRMLite.iOS Harbor.cs.</summary>
    static readonly PartScale Desktop = new(8, TextOverflow.Visible,
        new(20, 5, Spacing.Tag, Corners.Tag, TextSize.Caption),
        new(new Edges(10, 12, 10, 12), 4, Corners.Card, 18, TextSize.Card, TextSize.Small, 24, 7, 22, FixedHeight: true),
        new(TextSize.Caption, TextSize.Body, TextSize.Caption, TextSize.Caption, 16, 22, 13, 9, new Edges(9, 12, 9, 12), Corners.Card,
            Wrap: false));
    void Label(string text, float size, Color color, Weight weight = Weight.Regular, NodeKey key = default) =>
            Parts.Label(C, Desktop, text, size, color, weight, key);
    void Rule(NodeKey key = default) =>
            Parts.Rule(C, p.Border, key);
    void VRule(NodeKey key = default)
    {
        using (C.Div(key: key).Width(1).FillHeight().Background(p.Line))
        {
        }
    }
    void Feedback(Div box, Interaction hit, bool active = false)
    {
        if (active || hit.Hovered)
            box.Background(p.Hover);
        if (hit.Focused && C.FocusVisible)
            box.AccentOutline(p);
        if (hit.Hovered)
            C.Cursor = Cursor.Hand;
    }
    void Logo(Company company, float size, NodeKey key = default) =>
            Parts.Logo(C, Desktop, company, size, key);
    void Avatar(Member member, float size, NodeKey key = default) =>
            Parts.Avatar(C, p, Desktop, member, size, key);
    void Avatar(Contact contact, float size, NodeKey key = default) =>
            Parts.Person(C, p, Desktop, contact.Name, size, key);
    void Person(string name, float size, NodeKey key = default) =>
            Parts.Person(C, p, Desktop, name, size, key);
    void StageTag(Stage stage, NodeKey key = default) =>
            Parts.StageTag(C, p, Desktop, stage, key);
    void Dot(Color color, float size, NodeKey key = default) =>
            Parts.Dot(C, color, size, key);
    void Timeline(ImmutableArray<Activity> items, NodeKey key) =>
            Parts.Timeline(C, p, Desktop, items, Model.Now, key);
    Interaction Option(string label, bool on, ref ulong previous, Action? leading = null, string? hint = null, NodeKey key = default)
    {
        using var row = C.Div(key: key).Row().FillWidth().Height(30).ItemsCenter().Gap(8).Px(8).Rounded(Corners.Tag)
            .Semantics(Role.MenuItem, label, isChecked: on);
        var hit = row.Control();
        if (hit.Focused && C.Shortcut(Key.Down, Modifiers.None))
            C.FocusNextControl();
        else if (hit.Focused && previous != 0 && C.Shortcut(Key.Up, Modifiers.None))
            C.FocusControl(previous);
        previous = row.Id;
        if (C.Kit.Highlighted(hit))
            row.Background(p.Menu);
        if (hit.Hovered)
            C.Cursor = Cursor.Hand;
        leading?.Invoke();
        using (C.Div(key: "label").Flex())
            Label(label, TextSize.Body, p.Text, key: "text");
        if (hint is not null)
            Label(hint, TextSize.Caption, p.Faint, key: "hint");
        if (on)
            C.Icon(Hugeicons.Tick02, 13, p.Accent, key: "check");
        return hit;
    }
    void MenuHeading(string text, NodeKey key = default)
    {
        using (C.Div(key: key).FillWidth().Padding(new Edges(6, 8, 4, 8)))
            Label(text, TextSize.Caption, p.Faint, Weight.Medium, "text");
    }
    void MenuRule(NodeKey key = default)
    {
        using (C.Div(key: key).FillWidth().Py(4))
            Rule("line");
    }
    Div Header(string icon, NodeKey key = default)
    {
#pragma warning disable KUM001 // The caller disposes the returned header scope.
        var bar = C.Div(key: key).Row().FillWidth().Height(Sizes.Header).ItemsCenter().Gap(Space.S2).Padding(new Edges(0, Space.S4, 0, 10));
#pragma warning restore KUM001
        Ui.OnPress(ActionButton.IconButton(C, p, Hugeicons.SidebarLeft, ForcedRail || Model.SidebarCollapsed ? "Expand sidebar" : "Collapse sidebar", enabled: !ForcedRail, key: "sidebar-toggle"), new CrmMsg.ToggleSidebar());
        using (C.Div(key: "toggle-rule").Width(1).Height(16).Background(p.Border))
        {
        }
        C.Icon(icon, 15, p.Secondary, key: "icon");
        return bar;
    }
}
