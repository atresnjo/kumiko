namespace Kumiko.UI;

public sealed partial class SearchDropdown
{
    /// <summary>Lists up to this many rows in full so assistive technology can find and reveal every option.</summary>
    const int FullListRows = 200;

    /// <summary>A list row: a section heading, or the option at <paramref name="Option"/> in the shown choices.</summary>
    readonly record struct Row(int Option, string? Heading);

    void DrawPopup(Context c, string name, IReadOnlyList<Choice> choices, NodeKey key, float width, string emptyMessage)
    {
        var t = c.Theme;
        using var overlay = c.Overlay($"dropdown:{key.Value}").TabStop(false).Semantics(Role.Group);
        if (c.Shortcut(Key.Escape, Modifiers.None))
        {
            Close(c);
            return;
        }
        var text = query.Text;
        var shown = Grouped(Remote || text.Length == 0 ? choices
            : choices.Where(o => o.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase)));
        var rows = new List<Row>(shown.Length);
        var rowOf = new int[shown.Length];
        for (int i = 0; i < shown.Length; i++)
        {
            var section = shown[i].Section;
            if (section is not null && (i == 0 || shown[i - 1].Section != section))
                rows.Add(new(-1, section));
            rowOf[i] = rows.Count;
            rows.Add(new(i, null));
        }
        active = Math.Clamp(active, -1, shown.Length - 1);
        if (c.Input.Key is Key.Down or Key.Up)
        {
            int step = c.Input.Key == Key.Down ? 1 : -1;
            for (int i = 0; i < shown.Length; i++)
            {
                active = (active + step + shown.Length) % shown.Length;
                if (!shown[active].Disabled)
                    break;
            }
            c.Input.Key = Key.None;
        }
        if (c.Shortcut(Key.Enter, Modifiers.None) && active >= 0 && !shown[active].Disabled)
        {
            Choose(c, shown[active]);
            if (!IsOpen)
                return;
        }
        var status = LoadError ?? (Loading ? c.Strings.Searching : shown.Length == 0 ? emptyMessage : null);
        var footer = Multiple && !selectedIds.IsEmpty;
        width = Math.Min(width, c.Size.Width);
        float itemHeight = t.Controls.MenuItemHeight;
        float extra = (status is null ? 0 : itemHeight) + (footer ? itemHeight + 1 + 2 * t.Space.S1 : 0);
        float height = Math.Min(300 + extra, Math.Max(96, Math.Min(7, rows.Count) * itemHeight + t.Controls.Height + t.Space.S4 + extra));
        height = Math.Min(height, c.Size.Height);
        float x = Math.Clamp(c.ToLayout(anchor).X, 0, Math.Max(0, c.Size.Width - width));
        float y = anchor.Bottom + t.Controls.PopupGap;
        if (y + height > c.Size.Height)
            y = Math.Max(0, anchor.Y - height - t.Controls.PopupGap);
        if (overlay.Control().Clicked)
        {
            Close(c);
            return;
        }
        using var panel = c.Div(key: "panel").Absolute().Offset(x, y).Width(width).Height(height)
            .Padding(t.Space.S1).Gap(t.Space.S1).Background(t.Popover).Border(t.Border).Rounded(t.Radius.Md).TabStop(false).Semantics(Role.Group);
        panel.Control();
        if (focusSearch)
        {
            c.FocusNextControl();
            focusSearch = false;
        }
        if (c.TextField(query.Text, value =>
        {
            query.Text = value;
            StateChanged?.Invoke(Capture());
            QueryChanged?.Invoke(value);
        }, new(Placeholder: $"Search {name.ToLowerInvariant()}s…", Width: Math.Max(1, width - t.Space.S2), Name: $"Search {name}"), key: "search"))
            active = query.Text.Length == 0 ? -1 : 0;
        if (status is not null)
            Status(c, status, LoadError is not null);
        if (rows.Count > 0)
        {
            using var options = c.Div(key: "options").FillWidth().Grow().Semantics(Role.List, name);
            c.UniformList("choices", rows.Count, itemHeight, (ctx, i) =>
            {
                if (rows[i].Heading is { } heading)
                    Heading(ctx, heading);
                else
                    Option(ctx, shown[rows[i].Option], rows[i].Option == active);
            }, overscan: rows.Count <= FullListRows ? rows.Count : 2, revealRow: active < 0 ? null : rowOf[active],
                rowKey: i => Context.Hash(rows[i].Heading is { } heading ? "section:" + heading : shown[rows[i].Option].Id));
        }
        if (footer)
            Footer(c);
    }

    static void Status(Context c, string text, bool failed)
    {
        var t = c.Theme;
        using var status = c.Div(key: "status").Row().FillWidth().Height(t.Controls.MenuItemHeight).ItemsCenter().Px(t.Space.S2);
        if (failed)
            status.Semantics(Role.Alert, text);
        c.Text(text, t.Typography.Body with
        {
            Color = failed ? t.Danger : t.Muted
        }, key: "text");
    }

    static void Heading(Context c, string text)
    {
        var t = c.Theme;
        using var heading = c.Div(key: "heading").Row().SizeFull().Items(Align.End)
            .Padding(new Edges(0, t.Space.S2, t.Space.S1, t.Space.S2))
            .Semantics(Role.Heading, text);
        c.Text(text, t.Typography.Caption with
        {
            Color = t.Muted,
            Weight = Weight.Medium
        }, key: "text");
    }

    void Option(Context c, Choice choice, bool highlighted)
    {
        var t = c.Theme;
        var on = IsChosen(choice);
        using var option = c.Div(key: "option").Row().SizeFull().ItemsCenter().Gap(t.Space.S2).Px(t.Space.S2)
            .Rounded(t.Radius.Sm).Semantics(Role.Option, choice.Label)
            .Accessibility(new(Selected: on), request =>
            {
                if (request.Action != AccessibilityAction.Select)
                    return false;
                Choose(c, choice);
                return true;
            });
        var hit = option.Control(!choice.Disabled);
        if (highlighted || hit.Hovered || hit.Focused)
            option.Background(t.Selection);
        if (choice.Disabled)
            option.Opacity(t.Controls.DisabledOpacity);
        if (Multiple)
        {
            using var box = c.Div(key: "check").Width(16).Height(16).Center().Rounded(t.Radius.Sm)
                .Border(on ? t.Accent : t.InputBorder).Background(on ? t.Accent : t.Surface);
            if (on)
                c.Icon("checkmark", 12, t.AccentText, key: "mark");
        }
        using (c.Div(key: "label").Flex().Clip())
            c.Text(choice.Label, t.Typography.Body with
            {
                Color = t.PopoverText
            }, key: "text");
        if (choice.Detail is { } detail)
            c.Text(detail, t.Typography.Caption with
            {
                Color = t.Muted
            }, key: "detail");
        if (hit.Clicked)
            Choose(c, choice);
    }

    void Footer(Context c)
    {
        var t = c.Theme;
        using (c.Div(key: "rule").FillWidth().Height(1).Background(t.Border))
        {
        }
        using var footer = c.Div(key: "footer").Row().FillWidth().Height(t.Controls.MenuItemHeight).ItemsCenter().Px(t.Space.S2);
        c.Text(string.Format(c.Culture, c.Strings.SelectedCount, selectedIds.Length), t.Typography.Caption with
        {
            Color = t.Muted
        }, key: "count");
        c.Spacer(key: "space");
        using var clear = c.Div(key: "clear").Row().ItemsCenter().Px(t.Space.S2).Height(t.Controls.MenuItemHeight - t.Space.S2)
            .Rounded(t.Radius.Sm).Semantics(Role.Button, c.Strings.ClearSelection);
        var hit = clear.Control();
        if (hit.Hovered || hit.Focused)
            clear.Background(t.Selection);
        c.Text(c.Strings.Clear, t.Typography.Caption with
        {
            Color = t.PopoverText,
            Weight = Weight.Medium
        }, key: "text");
        if (hit.Clicked)
            Clear(c);
    }
}
