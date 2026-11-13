using System.Collections.Immutable;
using DiffViewer.Core;
using Kumiko.UI;

namespace DiffViewer;

/// <summary>
/// Keyboard control. Letters and punctuation match the character the active layout produces, so they work on
/// QWERTZ and AZERTY too; they never fire while a text field has focus. Every action also has a toolbar or menu
/// equivalent where it makes sense.
/// </summary>
internal sealed partial class DiffScreen
{
    void Keys(VirtualListView view, ulong searchField)
    {
        if (searchField != 0 && C.FocusWithin(searchField))
            return;
        const Modifiers none = Modifiers.None;
        const Modifiers command = Modifiers.Primary;
        int page = PageRows(view);
        if (C.Shortcut('k', command) || C.Shortcut('o', command))
            Ui.Dispatch(new Msg.OpenQuick());
        else if (C.Shortcut('P', command) || C.Shortcut('P', command | Modifiers.Shift))
            Ui.Dispatch(new Msg.OpenQuick(">"));
        else if (C.Shortcut(Key.Down, none) || C.Shortcut('j'))
            Ui.Dispatch(new Msg.Move(1));
        else if (C.Shortcut(Key.Up, none) || C.Shortcut('k'))
            Ui.Dispatch(new Msg.Move(-1));
        else if (C.Shortcut(Key.PageDown, none) || C.Shortcut(Key.Space, none) || C.Shortcut('f', Modifiers.Control))
            Ui.Dispatch(new Msg.Move(page));
        else if (C.Shortcut(Key.PageUp, none) || C.Shortcut(Key.Space, Modifiers.Shift) || C.Shortcut('b', Modifiers.Control))
            Ui.Dispatch(new Msg.Move(-page));
        else if (C.Shortcut('d', Modifiers.Control))
            Ui.Dispatch(new Msg.Move(Math.Max(1, page / 2)));
        else if (C.Shortcut('u', Modifiers.Control))
            Ui.Dispatch(new Msg.Move(-Math.Max(1, page / 2)));
        else if (C.Shortcut(Key.Home, none) || C.Shortcut(Key.Up, command) || C.Shortcut('g'))
            Ui.Dispatch(new Msg.Top());
        else if (C.Shortcut(Key.End, none) || C.Shortcut(Key.Down, command) || C.Shortcut('G'))
            Ui.Dispatch(new Msg.Bottom());
        else if (C.Shortcut('n') || C.Shortcut(']') || C.Shortcut(']', command))
            Ui.Dispatch(new Msg.NextHunk(1));
        else if (C.Shortcut('p') || C.Shortcut('[') || C.Shortcut('[', command))
            Ui.Dispatch(new Msg.NextHunk(-1));
        else if (Model.Zen is not null && C.Shortcut(Key.Right, none))
            Ui.Dispatch(new Msg.NextFile(1));
        else if (Model.Zen is not null && C.Shortcut(Key.Left, none))
            Ui.Dispatch(new Msg.NextFile(-1));
        else if (C.Shortcut('N') || C.Shortcut('}') || C.Shortcut(Key.Down, Modifiers.Alt))
            Ui.Dispatch(new Msg.NextFile(1));
        else if (C.Shortcut('P') || C.Shortcut('{') || C.Shortcut(Key.Up, Modifiers.Alt))
            Ui.Dispatch(new Msg.NextFile(-1));
        else if (C.Shortcut('x') || C.Shortcut('o') || Model.Layout.Count > 0 && Model.Layout.Rows[Model.Cursor].Kind == RowKind.File && C.Shortcut(Key.Enter, none))
            Ui.Dispatch(new Msg.ToggleFile(-1));
        else if (C.Shortcut('X'))
            Ui.Dispatch(new Msg.SetAllCollapsed(Model.Collapsed.Count < Model.Document.Files.Length));
        else if (C.Shortcut('u') || C.Shortcut('1', command))
            Ui.Dispatch(new Msg.SetMode(ViewMode.Unified));
        else if (C.Shortcut('s') || C.Shortcut('2', command))
            Ui.Dispatch(new Msg.SetMode(ViewMode.Split));
        else if (C.Shortcut('v'))
            Ui.Dispatch(new Msg.SetMode(Model.Mode == ViewMode.Unified ? ViewMode.Split : ViewMode.Unified));
        else if (C.Shortcut('w'))
            Ui.Dispatch(new Msg.ToggleWhitespace());
        else if (C.Shortcut('/') || C.Shortcut('f', command))
            Ui.Dispatch(new Msg.OpenSearch());
        else if (C.Shortcut('t') || C.Shortcut('p', command))
            Ui.Dispatch(new Msg.OpenFinder());
        else if (C.Shortcut('r') || C.Shortcut('r', command))
            Ui.Dispatch(new Msg.Reload());
        else if (C.Shortcut('b', command) || C.Shortcut('\\', command))
            Ui.Dispatch(new Msg.ToggleSidebar());
        else if (C.Shortcut('z'))
            Ui.Dispatch(new Msg.ToggleZen());
        else if (C.Shortcut('?') || C.Shortcut('/', command))
            Ui.Dispatch(new Msg.SetHelp(true));
        else if (Model.Search is not null && C.Shortcut(Key.Escape, none))
            Ui.Dispatch(new Msg.CloseOverlay());
        else if (Model.Selection is not null && C.Shortcut(Key.Escape, none))
            Ui.Dispatch(new Msg.ClearSelection());
        else if (Model.Zen is not null && C.Shortcut(Key.Escape, none))
            Ui.Dispatch(new Msg.ToggleZen());
        else if (Model.Search is { Found.Length: > 0 } && C.Shortcut('g', command))
            Ui.Dispatch(new Msg.SearchStep(1));
        else if (Model.Search is { Found.Length: > 0 } && C.Shortcut('G', command))
            Ui.Dispatch(new Msg.SearchStep(-1));
        else if (Model.Selection is { Text.Length: > 0 } copied && C.Shortcut('c', command))
            Copy(copied.Text);
        else if (Model.Selection is not null && (C.Shortcut('m') || C.Shortcut('g', command)))
            Ui.Dispatch(new Msg.SelectionStep(1));
        else if (Model.Selection is not null && (C.Shortcut('M') || C.Shortcut('G', command)))
            Ui.Dispatch(new Msg.SelectionStep(-1));
    }

    /// <summary>Puts text on the clipboard: the window's own, and the system's when the window has one.</summary>
    void Copy(string text)
    {
        C.Clipboard = text;
        C.WriteClipboard?.Invoke(text);
    }

    /// <summary>Rows a page moves: the viewport's worth of lines minus two lines of overlap.</summary>
    static int PageRows(VirtualListView view) => Math.Max(1, (int)(view.Viewport / RowLayout.LineHeight) - 2);

    /// <summary>Shortcuts listed by the help overlay, grouped as they are read.</summary>
    internal static readonly ImmutableArray<(string Group, ImmutableArray<(string Keys, string Action)> Items)> Shortcuts =
    [
        ("Move", [
            ("j  ↓", "Next line"),
            ("k  ↑", "Previous line"),
            ("Space  PgDn  ⌃F", "Page down"),
            ("⇧Space  PgUp  ⌃B", "Page up"),
            ("⌃D  ⌃U", "Half page down / up"),
            ("g  Home  ⌘↑", "Top"),
            ("G  End  ⌘↓", "Bottom")
        ]),
        ("Jump", [
            ("n  ]  ⌘]", "Next hunk"),
            ("p  [  ⌘[", "Previous hunk"),
            ("⇧N  }  ⌥↓", "Next file"),
            ("⇧P  {  ⌥↑", "Previous file"),
            ("⌘K  ⌘O", "Open project or worktree"),
            ("⇧⌘P", "Commands"),
            ("t  ⌘P", "Go to file"),
            ("/  ⌘F", "Search lines"),
            ("Enter  ⇧Enter  ⌘G", "Next / previous match")
        ]),
        ("Select", [
            ("Drag  ⇧Click", "Select text and its matches"),
            ("Double / triple click", "Select word / line"),
            ("m  ⌘G", "Next occurrence"),
            ("⇧M  ⇧⌘G", "Previous occurrence"),
            ("⌘C", "Copy selection"),
            ("Esc", "Clear selection")
        ]),
        ("View", [
            ("u  ⌘1", "Unified"),
            ("s  ⌘2", "Split"),
            ("v", "Toggle unified / split"),
            ("x  o  Enter", "Fold file"),
            ("⇧X", "Fold or unfold all"),
            ("w", "Ignore whitespace"),
            ("⌘B", "Toggle sidebar"),
            ("z", "Zen mode (one file, full screen)"),
            ("n p  ← →", "Zen: change / file"),
            ("r  ⌘R", "Reload"),
            ("?", "Keyboard shortcuts"),
            ("Esc", "Close")
        ])
    ];
}
