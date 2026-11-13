using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    enum ChoiceStyle
    {
        Segmented,
        Underline,
        Pill,
        Toggle,
        ToggleOutline
    }

    /// <summary>
    /// A tab list. Arrow keys activate the adjacent tab (wrapping); only the active tab is a Tab stop. Build the
    /// selected panel after this call.
    /// </summary>
    public bool Tabs(
        string name,
        IReadOnlyList<Choice> tabs,
        string selected,
        Action<string> changed,
        TabsProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var style = props.Variant switch
        {
            TabsVariant.Underline => ChoiceStyle.Underline,
            TabsVariant.Pill => ChoiceStyle.Pill,
            _ => ChoiceStyle.Segmented
        };
        return Choices(name, tabs, selected, changed, style, props.Size, props.Fill, false, null, key, source, line, props.RenderItem);
    }

    /// <summary>A single-select row of toggle buttons; <paramref name="icons"/> are shown before labels.</summary>
    public bool ToggleGroup(
        string name,
        IReadOnlyList<Choice> choices,
        string? selected,
        Action<string> changed,
        ToggleGroupProps props = default,
        IReadOnlyList<string>? icons = null,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => Choices(name, choices, selected, changed, props.Outline ? ChoiceStyle.ToggleOutline : ChoiceStyle.Toggle, props.Size,
        false, props.Disabled, icons, key, source, line);

    bool Choices(string name, IReadOnlyList<Choice> choices, string? selected, Action<string> changed, ChoiceStyle style,
        ControlSize size, bool fill, bool disabled, IReadOnlyList<string>? icons, NodeKey key, string source, int line,
        Action<Context, Choice, TabItemState>? renderItem = null)
    {
        var t = Theme;
        var compact = size is ControlSize.XSmall or ControlSize.Small;
        var height = t.Metrics.Height(size);
        var font = compact ? t.FontSize - 1 : t.FontSize;
        var inset = style == ChoiceStyle.Segmented ? 3f : 0f;
        using var outer = Box(key, source, line).Column().Semantics(Role.Group, name);
        if (fill || style == ChoiceStyle.Underline)
            outer.FillWidth();
        var ids = new ulong[choices.Count];
        int focused = -1;
        int clicked = -1;
        int anchor = Math.Max(0, IndexOf(choices, selected));
        using (var list = context.Div(key: "list").Row().ItemsCenter())
        {
            if (fill || style == ChoiceStyle.Underline)
                list.FillWidth();
            switch (style)
            {
                case ChoiceStyle.Segmented:
                    list.Padding(inset).Gap(2).Height(height + 2).Rounded(t.RadiusLarge).Background(t.TabBar);
                    break;
                case ChoiceStyle.Underline:
                    list.Gap(4);
                    break;
                case ChoiceStyle.ToggleOutline:
                    list.Padding(1).Gap(1).Rounded(t.Radius).Border(t.Input).Background(t.Background);
                    break;
                default:
                    list.Gap(4);
                    break;
            }
            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var on = choice.Id == selected;
                var enabled = !disabled && !choice.Disabled;
                using var item = context.Div(key: choice.Id)
                    .Row()
                    .ItemsCenter()
                    .Justify(Justify.Center)
                    .Gap(6)
                    .Px(compact ? 8 : 12)
                    .TabStop(i == anchor)
                    .Semantics(Role.Option, choice.Label, isChecked: on)
                    .Accessibility(new(Selected: on));
                ids[i] = item.Id;
                if (fill)
                    item.Flex();
                var hit = item.Control(enabled);
                if (hit.Focused)
                    focused = i;
                var hovered = hit.Hovered && enabled;
                var foreground = PaintChoice(item, style, on, hovered, height, inset, t, outer.Id);
                Ring(item, hit, t);
                if (renderItem is not null)
                    context.RenderContent(renderItem, choice, new(on, hovered, enabled && (hovered || hit.Focused), hit.Focused, !enabled, foreground));
                else
                {
                    if (icons is not null && i < icons.Count)
                        context.Icon(icons[i], compact ? 14 : 16, foreground, key: "icon");
                    context.Text(choice.Label, Text(font, foreground, Weight.Medium), key: "label");
                }
                if (!enabled)
                    item.Opacity(t.Metrics.DisabledOpacity);
                else
                    Hand(hit);
                if (hit.Clicked && enabled && !on)
                    clicked = i;
            }
        }
        if (style == ChoiceStyle.Underline)
        {
            using (context.Div(key: "rule").FillWidth().Height(1).Background(t.Border))
            {
            }
        }
        if (clicked >= 0)
        {
            changed(choices[clicked].Id);
            return true;
        }
        var step = focused < 0 ? 0 : context.Logical(context.Input.Key) switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0
        };
        var target = Step(choices, focused, step, disabled);
        if (target < 0)
            return false;
        context.Input.Key = Key.None;
        context.FocusControl(ids[target]);
        changed(choices[target].Id);
        return true;
    }

    Color PaintChoice(Div item, ChoiceStyle style, bool on, bool hovered, float height, float inset, KitTheme t, ulong group)
    {
        switch (style)
        {
            case ChoiceStyle.Segmented:
                item.Height(height - inset * 2 + 2).Rounded(t.Radius);
                if (on)
                {
                    item.Background(t.TabActive);
                    if (t.Shadows)
                        item.Shadow(3, 1, Color.Black.Alpha(t.IsDark ? .3f : .08f));
                    if (t.IsDark)
                        item.Border(t.Input);
                }
                return on ? t.TabActiveForeground : hovered ? t.Foreground : t.TabForeground;
            case ChoiceStyle.Underline:
                item.Height(height + 4).Rounded(0);
                if (on)
                {
                    // One underline per tab list glides to the selected tab.
                    using (context.Div(key: "indicator").Absolute().Offset(0, height + 2).FillWidth().Height(2)
                        .Background(t.Primary.Background).SharedLayout($"kumiko.tabs:{group}"))
                    {
                    }
                }
                return on || hovered ? t.Foreground : t.MutedForeground;
            case ChoiceStyle.Pill:
                item.Height(height).Rounded(t.RadiusFull);
                item.Background(on ? t.Primary.Background : hovered ? t.Accent : Color.Transparent);
                return on ? t.Primary.Foreground : t.Foreground;
            default:
                item.Height(style == ChoiceStyle.ToggleOutline ? height - 2 : height).Rounded(MathF.Max(0, t.Radius - 1));
                item.Background(on ? t.Accent : hovered ? t.Muted : Color.Transparent);
                return on ? t.AccentForeground : hovered ? t.Foreground : t.MutedForeground;
        }
    }

    /// <summary>A path of locations; the last is the current page. <paramref name="navigate"/> receives choice IDs.</summary>
    public void Breadcrumb(
        IReadOnlyList<Choice> items,
        Action<string>? navigate = null,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        using var row = Box(key, source, line).Row().ItemsCenter().Gap(6).Semantics(Role.Group, "Breadcrumb");
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var last = i == items.Count - 1;
            using var crumb = context.Div(key: item.Id).Row().ItemsCenter().Gap(6);
            if (last)
            {
                context.Text(item.Label, Text(t.FontSize, t.Foreground, Weight.Medium), key: "label");
                continue;
            }
            using (var link = context.Div(key: "link").Rounded(t.RadiusSmall).Semantics(Role.Button, item.Label))
            {
                var hit = link.Control(navigate is not null && !item.Disabled);
                Ring(link, hit, t);
                Hand(hit);
                context.Text(item.Label, Text(t.FontSize, hit.Hovered ? t.Foreground : t.MutedForeground), key: "label");
                if (hit.Clicked)
                    navigate?.Invoke(item.Id);
            }
            context.Icon("chevron.right", 14, t.MutedForeground, key: "separator");
        }
    }

    /// <summary>Previous/next and numbered page buttons with ellipses beyond <paramref name="siblings"/>.</summary>
    public bool Pagination(
        int page,
        int pages,
        Action<int> changed,
        int siblings = 1,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        pages = Math.Max(1, pages);
        page = Math.Clamp(page, 1, pages);
        int target = 0;
        using var row = Box(key, source, line).Row().ItemsCenter().Gap(4).Semantics(Role.Group, "Pagination");
        if (ButtonCore("Previous", "Previous page", new(Appearance: Appearance.Ghost, Disabled: page <= 1), false, "previous", source,
            line).Clicked)
            target = page - 1;
        int lastShown = 0;
        for (int n = 1; n <= pages; n++)
        {
            if (n != 1 && n != pages && Math.Abs(n - page) > siblings)
                continue;
            if (n - lastShown > 1)
                Ellipsis((ulong)n);
            lastShown = n;
            var label = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var props = new ButtonProps(Appearance: n == page ? Appearance.Outline : Appearance.Ghost, Selected: n == page,
                Width: Theme.Metrics.HeightMedium);
            if (ButtonCore(label, $"Page {label}", props, false, (ulong)n, source, line).Clicked && n != page)
                target = n;
        }
        if (ButtonCore("Next", "Next page", new(Appearance: Appearance.Ghost, Disabled: page >= pages, TrailingIcon: "chevron.right"),
            false, "next", source, line).Clicked)
            target = page + 1;
        if (target == 0)
            return false;
        changed(target);
        return true;
    }

    void Ellipsis(ulong before)
    {
        var t = Theme;
        using (context.Div(key: $"gap-{before}").Width(t.Metrics.HeightMedium).Height(t.Metrics.HeightMedium).Center())
            context.Text("…", Text(t.FontSize, t.MutedForeground), key: "dots");
    }

    /// <summary>
    /// A full-height sidebar column with its trailing border. Dispose with using; add labels and items inside.
    /// </summary>
    public Section Sidebar(float? width = null, NodeKey key = default, [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0)
    {
        var t = Theme;
#pragma warning disable KUM001 // The returned composite scope owns and disposes these Divs.
        var outer = Box(key, source, line).Row().FillHeight();
#pragma warning restore KUM001
#pragma warning disable KUM001 // Disposed by the returned Section.
        var content = context.Div(key: "content")
            .Column()
            .Width((width ?? t.Metrics.SidebarWidth) - 1)
            .FillHeight()
            .Padding(12)
            .Gap(2)
            .Background(t.Sidebar);
#pragma warning restore KUM001
        return new(context, outer, content, true, t.SidebarBorder, vertical: true);
    }

    /// <summary>A muted group heading inside a sidebar.</summary>
    public void SidebarLabel(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var t = Theme;
        using (Box(key, source, line).FillWidth().Padding(new Edges(12, 8, 4, 8)))
            context.Text(text, Text(t.FontSize - 2, t.MutedForeground, Weight.Medium), key: "text");
    }

    /// <summary>A navigation row; selected rows use the sidebar accent.</summary>
    public Interaction SidebarItem(
        string label,
        SidebarItemProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        using var item = Box(key, source, line)
            .Row()
            .FillWidth()
            .ItemsCenter()
            .Gap(8)
            .Height(t.Metrics.HeightMedium)
            .Padding(new Edges(0, 8, 0, 8 + props.Depth * 12))
            .Rounded(t.Radius)
            .Semantics(Role.Button, label)
            .Accessibility(new(Selected: props.Selected));
        var hit = item.Control(!props.Disabled);
        if (props.Selected)
            item.Background(t.SidebarAccent);
        else if (hit.Hovered && !props.Disabled)
            item.Background(t.SidebarAccent.Alpha(.6f));
        Ring(item, hit, t);
        if (props.Disabled)
            item.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        var foreground = props.Selected ? t.SidebarForeground : Color.Mix(t.SidebarForeground, t.Sidebar, .2f);
        if (props.Icon is { } icon)
            context.Icon(icon, 16, foreground, key: "icon");
        using (context.Div(key: "label").Flex())
            context.Text(label, Text(t.FontSize, foreground, props.Selected ? Weight.Medium : Weight.Regular), key: "text");
        if (props.Badge is { } badge)
            context.Text(badge, Text(t.FontSize - 2, Color.Mix(t.SidebarForeground, t.Sidebar, .35f), Weight.Medium), key: "badge");
        return hit;
    }
}
