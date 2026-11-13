using Kumiko.Runtime;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

/// <summary>The original App.tsx composition: sidebar, toolbar with tab bar, the active repository, then overlays.</summary>
public sealed partial class FlintView(FlintModel model) : Kumiko.Runtime.Screen<FlintModel, Msg>(model)
{
    protected override void Render()
    {
        C.Theme = Palette.Theme;
        C.TitleBarHeight = 54;
        Shortcuts();
        using (C.Div(key: "flint").SizeFull().Row().Background(Canvas))
        {
            var sidebarSpace = SidebarArea();
            using (C.Div(key: "main").Flex().FillHeight().MinWidth(0))
            {
                using (C.Div(key: "header").FillWidth().Background(Toolbar).Padding(new Edges(0, 0, 6, 0)))
                {
                    ToolbarView(sidebarSpace);
                    TabBar();
                }
                Rule("header-rule");
                using (C.Div(key: Model.Workspace.Active ?? "welcome").FillWidth().Flex().MinHeight(0))
                {
                    if (Model.Workspace.Active is null)
                        Welcome();
                    else if (Model.Current is not { } repo)
                        Empty(Model.Error.Length > 0 ? Model.Error : "Loading…");
                    else if (repo.Page == Page.Changes)
                        ChangesView(repo, sidebarSpace);
                    else
                        HistoryView(repo, sidebarSpace);
                }
            }
            Toasts();
        }
        if (Model.Menu is not null)
            Menu();
        if (Model.RemoteEditor is not null)
            RemoteDialog();
        else if (Model.Pending is not null)
            Confirmation();
        else if (Model.Palette != PaletteMode.Closed)
            PaletteView();
        SwitcherView();
    }
    void Label(string text, NodeKey key, float size = 13, Color? color = null, Weight weight = Weight.Regular, bool mono = false)
        => C.Text(text, new(Size: size, Weight: weight, Color: color ?? Text, Mono: mono), key);
    void Rule(NodeKey key, bool vertical = false)
    {
        using var rule = vertical ? C.Div(key: key).Width(1).FillHeight().Background(Border) : C.Div(key: key).FillWidth().Height(1).Background(Border);
    }
    /// <summary>The original kit Button: 24 px, white with a strong border, or accent when primary.</summary>
    Interaction Button(string label, Msg? message, NodeKey key, bool primary = false, bool enabled = true, bool destructive = false, string? name = null)
    {
        var fill = destructive ? Danger : primary ? Accent : Canvas;
        using var box = C.Div(key: key).Row().Height(24).Px(12).Center().Rounded(6).Border(primary || destructive ? fill : BorderStrong).Background(fill);
        var hit = message is null ? box.Semantics(Role.Button, name ?? label).Control(enabled) : Ui.Button(box, name ?? label, message, enabled);
        if (!enabled)
            box.Opacity(.45f);
        else if (hit.Pressed)
            box.Background(primary || destructive ? Color.Hex(0x1f49ad) : Pressed);
        else if (hit.Hovered)
            box.Background(destructive ? Color.Hex(0xc0001a) : primary ? AccentHover : Hover);
        if (hit.Focused && C.FocusVisible)
            box.Border(Accent, 2);
        Label(label, "label", 12, primary || destructive ? Color.White : Text, Weight.Medium);
        return hit;
    }
    /// <summary>The original ToolbarButton: a 17 px icon over an 11 px label, with an optional count badge.</summary>
    void ToolButton(string label, string icon, Msg message, NodeKey key, bool enabled = true, int badge = 0, string? name = null)
    {
        using var box = C.Div(key: key).Height(44).MinWidth(46).Px(8).Center().Gap(3).Rounded(6);
        var hit = Ui.Button(box, name ?? label, message, enabled);
        if (!enabled)
            box.Opacity(.4f);
        else if (hit.Pressed)
            box.Background(Pressed);
        else if (hit.Hovered)
            box.Background(Hover);
        if (hit.Focused && C.FocusVisible)
            box.Border(Accent, 2);
        C.Icon(icon, 17, Text, key: "glyph");
        Label(label, "label", 11, MutedStrong);
        if (badge > 0)
        {
            // Overhangs the icon's trailing side, anchored to the button so it stays inside it.
            using var count = C.Div(key: "badge").Absolute().Inset(LayoutEdge.Start, Length.Percent(50)).Inset(LayoutEdge.Top, 0)
                .Margin(new Edges(0, 0, 0, 2.5f)).MinWidth(15).Height(15).Px(4).Center().Rounded(8).Background(Muted).Border(Toolbar, 1.5f);
            Label(badge > 99 ? "99+" : badge.ToString(C.Culture), "count", 9.5f, Color.White, Weight.Bold);
        }
    }
    void IconButton(string label, string icon, Msg message, NodeKey key, float size = 30, float glyph = 17, bool enabled = true)
    {
        using var box = C.Div(key: key).Width(size).Height(size).Center().Rounded(6);
        var hit = Ui.Button(box, label, message, enabled);
        if (hit.Hovered && enabled)
            box.Background(Hover);
        if (hit.Focused && C.FocusVisible)
            box.Border(Accent, 2);
        C.Icon(icon, glyph, Muted, key: "icon");
    }
    void Empty(string text)
    {
        using var box = C.Div(key: "empty").FillWidth().Flex().MinHeight(60).Center();
        Label(text, "message", 12, Muted);
    }
    void Welcome()
    {
        using var welcome = C.Div(key: "welcome-body").SizeFull().Center().Gap(14);
        C.Icon(Icons.Branch, 40, Faint, key: "logo");
        Label("Flint", "title", 20, weight: Weight.Semibold);
        Label(Model.Known.Length > 0 ? $"{Model.Known.Length} repositories found on this machine" : "The fastest way to look at git.", "tagline", 13, Muted);
        using var actions = C.Div(key: "actions").Row().Gap(8).Margin(new Edges(6, 0, 0, 0));
        Button("Open Repository ⌘O", new Msg.ShowPalette(PaletteMode.Open), "open", primary: true);
        Button("Quick Launch ⌘P", new Msg.ShowPalette(PaletteMode.QuickLaunch), "quick");
    }
    /// <summary>The original's bottom-right toast stack; errors stay longer and use a red dot.</summary>
    void Toasts()
    {
        if (Model.Toasts.IsEmpty)
            return;
        using var layer = C.Div(key: "toasts").Absolute().Inset(LayoutEdge.Right, 20).Inset(LayoutEdge.Bottom, 20)
            .MaxWidth(MathF.Max(0, C.Size.Width - 40)).Items(Align.End).Gap(8);
        foreach (var toast in Model.Toasts)
        {
            using var row = C.Div(key: toast.Id).Row().ItemsCenter().Gap(8).MaxWidth(520).Padding(new Edges(8, 14, 8, 12)).Rounded(8)
                .Background(toast.Kind == ToastKind.Error ? Color.Hex(0x2a1414) : Color.Hex(0x1d1d1f)).Shadow(20, 6, Color.Black.Alpha(.19f))
                .Semantics(Role.Button, "Show notification: " + toast.Text);
            var hit = Ui.Button(row, "Show notification: " + toast.Text, new Msg.Ask(new(
                toast.Kind == ToastKind.Error ? "Error details" : "Notification details", "", "", CancelLabel: "Close",
                Details: toast.Details.Length > 0 ? toast.Details : toast.Text)), enabled: true);
            if (hit.Focused && C.FocusVisible)
                row.Border(Accent, 2);
            using (C.Div(key: "dot").Width(8).Height(8).Rounded(4).Background(toast.Kind == ToastKind.Error ? Color.Hex(0xff3b30)
                : toast.Kind == ToastKind.Success ? Color.Hex(0x34c759) : Accent))
            {
            }
            using (C.Div(key: "text").Shrink().MinWidth(0).Clip())
                C.Text(toast.Text.Replace('\n', ' '), new TextStyle(Size: 12, Color: Color.White) with
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "label");
        }
    }
    /// <summary>The visible history list's scroll offset from the previous frame's accessibility tree, if shown.</summary>
    float? HistoryOffset() => Model.Current is { Page: Page.History }
        ? (float?)C.Accessibility.FirstOrDefault(n => n.Role == Role.List && n.Key == "commits")?.Properties?.Number : null;
    static ulong StableId(string value)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in value)
            hash = unchecked((hash ^ ch) * 1099511628211UL);
        return hash;
    }
}
