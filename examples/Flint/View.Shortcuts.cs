using Flint.Git;
using Kumiko.UI;

namespace Flint;

public sealed partial class FlintView
{
    /// <summary>The original main.tsx shortcuts. An open palette, menu or dialog owns the keyboard.</summary>
    void Shortcuts()
    {
        if (SwitcherKeys())
            return;
        if (Model.Palette != PaletteMode.Closed || Model.Pending is not null || Model.Menu is not null || Model.RemoteEditor is not null)
            return;
        var tab = Model.Current;
        if (tab is { Finding: true })
        {
            if (C.Shortcut(Key.Escape, Modifiers.None))
                Ui.Dispatch(new Msg.SetFind(false));
            else if (C.Shortcut(Key.Enter, Modifiers.Shift))
                Ui.Dispatch(new Msg.StepMatch(-1));
            else if (C.Shortcut(Key.Enter, Modifiers.None))
                Ui.Dispatch(new Msg.StepMatch(1));
        }
        if (C.Shortcut(Key.Tab, Modifiers.Control) || C.Shortcut(Key.Tab, Modifiers.Alt))
            Ui.Dispatch(new Msg.CycleTabs(1));
        else if (C.Shortcut(Key.Tab, Modifiers.Control | Modifiers.Shift) || C.Shortcut(Key.Tab, Modifiers.Alt | Modifiers.Shift))
            Ui.Dispatch(new Msg.CycleTabs(-1));
        var shift = Modifiers.Primary | Modifiers.Shift;
        if (C.Shortcut(Key.F, shift))
            Ui.Dispatch(new Msg.Run(new(GitAction.Fetch)));
        else if (C.Shortcut(Key.L, shift))
            Ui.Dispatch(new Msg.Run(new(GitAction.Pull)));
        else if (C.Shortcut(Key.P, shift))
            Ui.Dispatch(new Msg.Run(new(GitAction.Push)));
        else if (C.Shortcut(Key.S, shift))
            Ui.Dispatch(new Msg.Run(new(GitAction.Stash)));
        else if (C.Shortcut(Key.B, shift))
            Ui.Dispatch(new Msg.ShowPalette(PaletteMode.NewBranch));
        else if (C.Shortcut(Key.W, shift))
            Ui.Dispatch(new Msg.ShowPalette(PaletteMode.Workspaces));
        else if (C.Shortcut(Key.BracketRight, shift) || C.Shortcut(Key.Right, Modifiers.Primary | Modifiers.Alt))
            Ui.Dispatch(new Msg.SwitchTab(1, true, HistoryOffset()));
        else if (C.Shortcut(Key.BracketLeft, shift) || C.Shortcut(Key.Left, Modifiers.Primary | Modifiers.Alt))
            Ui.Dispatch(new Msg.SwitchTab(-1, true, HistoryOffset()));
        else if (C.Shortcut(Key.Enter, shift))
            Ui.Dispatch(new Msg.Run(new(GitAction.Commit, ThenPush: true)));
        else if (C.Shortcut(Key.Enter))
            Ui.Dispatch(new Msg.Run(new(GitAction.Commit)));
        else if (C.Shortcut(Key.P) || C.Shortcut(Key.K))
            Ui.Dispatch(new Msg.ShowPalette(PaletteMode.QuickLaunch));
        else if (C.Shortcut(Key.O) || C.Shortcut(Key.T))
            Ui.Dispatch(new Msg.ShowPalette(PaletteMode.Open));
        else if (C.Shortcut(Key.B))
            Ui.Dispatch(new Msg.ShowPalette(PaletteMode.Branch));
        else if (C.Shortcut(Key.L))
            Ui.Dispatch(new Msg.Navigate(Page.Changes));
        else if (C.Shortcut(Key.G))
            Ui.Dispatch(new Msg.Navigate(Page.History));
        else if (C.Shortcut(Key.Backslash) || C.Shortcut(Key.D0))
            Ui.Dispatch(new Msg.ToggleSidebar());
        else if (C.Shortcut(Key.R))
            Ui.Dispatch(new Msg.Refresh());
        else if (C.Shortcut(Key.W) && Model.Workspace.Active is { } path)
            Ui.Dispatch(new Msg.CloseRepo(path));
        else if (C.Shortcut(Key.F) && tab is not null && !tab.Lines.IsDefaultOrEmpty)
            Ui.Dispatch(new Msg.SetFind(true));
        for (var index = 0; index < 9; index++)
            if (C.Shortcut((Key)((int)Key.D1 + index)))
                Ui.Dispatch(new Msg.SwitchTab(index, Scroll: HistoryOffset()));
    }
}
