using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    const Modifiers SwitchModifiers = Modifiers.Control | Modifiers.Alt;

    /// <summary>
    /// While Control/Option+Tab is held, Tab steps, releasing the modifier or Enter commits and Escape cancels.
    /// Returns true when the switcher owns the keyboard this frame.
    /// </summary>
    bool SwitcherKeys()
    {
        if (Model.Switcher is not { } switcher)
            return false;
        if (C.Input.Key == Key.Escape && C.Input.Modifiers == Modifiers.None)
        {
            // A visible switcher handles Escape inside its modal layer below.
            if (!switcher.Visible && C.Shortcut(Key.Escape, Modifiers.None))
                Ui.Dispatch(new Msg.Cancel());
            return true;
        }
        if ((C.Input.HeldModifiers & SwitchModifiers) == 0 || C.Shortcut(Key.Enter, Modifiers.None)
            || C.Shortcut(Key.Enter, Modifiers.Control) || C.Shortcut(Key.Enter, Modifiers.Alt))
            Ui.Dispatch(new Msg.CommitTabs(HistoryOffset()));
        else if (C.Shortcut(Key.Tab, Modifiers.Control) || C.Shortcut(Key.Tab, Modifiers.Alt))
            Ui.Dispatch(new Msg.CycleTabs(1));
        else if (C.Shortcut(Key.Tab, Modifiers.Control | Modifiers.Shift) || C.Shortcut(Key.Tab, Modifiers.Alt | Modifiers.Shift))
            Ui.Dispatch(new Msg.CycleTabs(-1));
        return true;
    }

    /// <summary>The held switcher: recent repositories in a centered sheet, the release target highlighted.</summary>
    void SwitcherView()
    {
        if (Model.Switcher is not { Visible: true } switcher)
            return;
        using var overlay = C.Overlay("switcher").Center().Padding(24);
        if (C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.Cancel());
        using var panel = C.Div(key: "panel").Width(MathF.Min(360, C.Size.Width - 80)).Padding(6).Gap(2)
            .Rounded(12).Background(Canvas).Border(BorderStrong).Shadow(48, 18, Color.Black.Alpha(.25f))
            .Semantics(Role.List, "Recent repositories");
        foreach (var path in Model.RecentOrder)
        {
            var selected = path == switcher.Target;
            var tab = Model.Repositories.Lookup(path);
            var name = tab?.Snapshot.Name ?? System.IO.Path.GetFileName(path);
            using var row = C.Div(key: path).Row().FillWidth().Height(34).ItemsCenter().Gap(10).Px(10).Rounded(7)
                .Semantics(Role.ListItem, name).Accessibility(new(Selected: selected));
            if (selected)
                row.Background(Accent);
            C.Icon(Icons.Folder, 15, selected ? Color.White : Muted, key: "icon");
            using (C.Div(key: "text").Flex().MinWidth(0).Gap(1).Clip())
            {
                Label(name, "name", 13, selected ? Color.White : Text, Weight.Medium);
                if (tab?.Snapshot.Head.Branch is { Length: > 0 } branch)
                    Label(branch, "branch", 11, selected ? Color.White.Alpha(.8f) : Muted);
            }
            if (path == Model.Workspace.Active)
                Label("current", "current", 11, selected ? Color.White.Alpha(.8f) : Muted);
        }
    }
}
