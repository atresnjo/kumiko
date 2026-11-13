namespace Kumiko.UI;

public sealed record MenuCommand(string Id, string Label, Action Run, bool Disabled = false, bool Destructive = false, string Shortcut = "");

/// <summary>A keyboard-operable, window-level menu. Create once outside the view.</summary>
public sealed partial class ContextMenu
{
    IReadOnlyList<MenuCommand> commands = [];
    Point position;
    ulong returnFocus;
    int active;
    bool focusFirst;
    public bool IsOpen
    {
        get; private set;
    }

    public void Open(Point point, ulong focus, IReadOnlyList<MenuCommand> items)
    {
        commands = items;
        position = point;
        returnFocus = focus;
        active = Enumerable.Range(0, items.Count).FirstOrDefault(i => !items[i].Disabled);
        focusFirst = true;
        IsOpen = items.Count > 0;
    }

    public void Close(Context c)
    {
        IsOpen = false;
        c.FocusControl(returnFocus);
        c.Changed = true;
    }

    public void Draw(Context c, NodeKey key = default)
    {
        if (!IsOpen)
            return;
        using var overlay = c.Overlay(key.Value is null ? "context-menu" : key).TabStop(false).Semantics(Role.Group);
        if (c.Shortcut(Key.Escape, Modifiers.None))
        {
            Close(c);
            return;
        }
        if (c.Input.Key is Key.Up or Key.Down or Key.Home or Key.End)
        {
            int direction = c.Input.Key == Key.Up ? -1 : 1;
            active = c.Input.Key switch
            {
                Key.Home => -1,
                Key.End => commands.Count,
                _ => active
            };
            if (c.Input.Key == Key.End)
                direction = -1;
            for (int i = 0; i < commands.Count; i++)
            {
                active = (active + direction + commands.Count) % commands.Count;
                if (!commands[active].Disabled)
                    break;
            }
            focusFirst = true;
            c.Input.Key = Key.None;
        }
        if (c.Shortcut(Key.Enter, Modifiers.None) && !commands[active].Disabled)
        {
            var action = commands[active];
            Close(c);
            action.Run();
            return;
        }
        var t = c.Theme;
        float width = Math.Min(t.Controls.PopupWidth, c.Size.Width);
        float height = Math.Min(commands.Count * t.Controls.MenuItemHeight + t.Space.S2, c.Size.Height);
        // The menu's start edge sits at the pointer; window layers mirror in right-to-left windows.
        var start = c.ToLayout(new Rect(position.X, position.Y, 0, 0)).X;
        float x = Math.Clamp(start, 0, Math.Max(0, c.Size.Width - width));
        float y = Math.Clamp(position.Y, 0, Math.Max(0, c.Size.Height - height));
        var backdrop = overlay.Control();
        if (backdrop.Clicked)
        {
            Close(c);
            return;
        }
        using var panel = c.Div(key: "panel").Absolute().Offset(x, y).Width(width).Height(height)
            .Padding(t.Space.S1).Background(t.Popover).Border(t.Border).Rounded(t.Radius.Md)
            .Scroll().TabStop(false).Semantics(Role.Menu, "Actions").Enter(Transition.Pop with
            {
                Motion = Motion.Quick
            }).Exit(Transition.Fade with
            {
                Motion = Motion.Quick
            });
        panel.Control();
        for (int i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            if (focusFirst && i == active && !command.Disabled)
                c.FocusNextControl();
            using var item = c.Div(key: command.Id).Row().FillWidth().Height(t.Controls.MenuItemHeight)
                .ItemsCenter().Px(t.Space.S2).Rounded(t.Radius.Sm).Semantics(Role.MenuItem, command.Label);
            var hit = item.Control(!command.Disabled);
            if (hit.Focused)
                active = i;
            if (!command.Disabled && c.MenuHighlight(hit))
                item.Background(t.Selection);
            if (command.Disabled)
                item.Opacity(t.Controls.DisabledOpacity);
            c.Text(command.Label, t.Typography.Body with
            {
                Color = command.Destructive ? t.Danger : t.PopoverText
            }, key: "label");
            c.Spacer(key: "space");
            c.Text(command.Shortcut, t.Typography.Caption with
            {
                Color = t.Muted
            }, key: "shortcut");
            if (hit.Clicked)
            {
                Close(c);
                command.Run();
                break;
            }
        }
        focusFirst = false;
    }
}
