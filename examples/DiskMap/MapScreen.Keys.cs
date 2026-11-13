using DiskMap.Core;
using Kumiko.UI;

namespace DiskMap;

/// <summary>
/// Keyboard control. Letters match the character the active layout produces and never fire while the filter
/// field has focus; every action also has a toolbar, menu or pointer equivalent.
/// </summary>
internal sealed partial class MapScreen
{
    void Keys(ulong filter)
    {
        if (filter != 0 && C.FocusWithin(filter))
        {
            if (C.Shortcut(Key.Down, Modifiers.None) || C.Shortcut(Key.Enter, Modifiers.None))
                Ui.Dispatch(new Msg.Step(1));
            return;
        }
        const Modifiers none = Modifiers.None;
        const Modifiers command = Modifiers.Primary;
        if (!Model.Loaded)
        {
            if (C.Shortcut('o', command))
                Ui.Dispatch(new Msg.ChooseFolder());
            return;
        }
        if (C.Shortcut(Key.Down, none) || C.Shortcut('j'))
            Ui.Dispatch(new Msg.Step(1));
        else if (C.Shortcut(Key.Up, none) || C.Shortcut('k'))
            Ui.Dispatch(new Msg.Step(-1));
        else if (C.Shortcut(Key.Enter, none) || C.Shortcut(Key.Right, none) || C.Shortcut(Key.Down, command))
            Ui.Dispatch(new Msg.OpenSelected());
        else if (C.Shortcut(Key.Backspace, command) || C.Shortcut(Key.Delete, command))
            Ui.Dispatch(new Msg.AskTrash());
        else if (C.Shortcut(Key.Backspace, none) || C.Shortcut(Key.Left, none) || C.Shortcut(Key.Up, command) || C.Shortcut(Key.Up, Modifiers.Alt))
            Ui.Dispatch(new Msg.Up());
        else if (C.Shortcut(Key.Escape, none))
            Ui.Dispatch(Model.Filter.Length > 0 ? new Msg.SetFilter("") : Model.Selected is not null && Model.Trail.Length == 1 ? new Msg.Select([]) : new Msg.Up());
        else if (C.Shortcut(Key.Space, none))
            Ui.Dispatch(new Msg.ToggleMark());
        else if (C.Shortcut('r', command) || C.Shortcut('r'))
            Ui.Dispatch(new Msg.Rescan());
        else if (C.Shortcut('f', command) || C.Shortcut('/'))
            Ui.Dispatch(new Msg.FocusFilter());
        else if (C.Shortcut('o', command))
            Ui.Dispatch(new Msg.ChooseFolder());
        else if (C.Shortcut('c', command))
            Copy(Model.Selected is { Item.Kind: not ItemKind.Rest } selected ? Model.PathOf(selected) : Model.FolderPath);
        else if (C.Shortcut('1') || C.Shortcut('1', command))
            Ui.Dispatch(new Msg.SetMetric(Metric.Disk));
        else if (C.Shortcut('2') || C.Shortcut('2', command))
            Ui.Dispatch(new Msg.SetMetric(Metric.Apparent));
        else if (C.Shortcut('3') || C.Shortcut('3', command))
            Ui.Dispatch(new Msg.SetMetric(Metric.Files));
        else if (C.Shortcut('?') || C.Shortcut('/', command))
            Ui.Dispatch(new Msg.SetHelp(true));
        else if (C.Shortcut('u'))
            Ui.Dispatch(new Msg.ClearMarks());
    }

    /// <summary>Puts text on the clipboard: the window's own, and the system's when the window has one.</summary>
    void Copy(string text)
    {
        C.Clipboard = text;
        C.WriteClipboard?.Invoke(text);
    }
}
