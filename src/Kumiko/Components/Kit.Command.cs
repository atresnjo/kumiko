using System.Globalization;
using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>A searchable command or destination. <paramref name="Keywords"/> match without being shown.</summary>
public sealed record CommandItem(string Id, string Label, string? Group = null, string? Icon = null, string? Hint = null,
    string Keywords = "")
{
    public bool Disabled
    {
        get; init;
    }
    public bool Checked
    {
        get; init;
    }
    /// <summary>Supplementary accessible value, independent of the visible shortcut hint.</summary>
    public string? Description
    {
        get; init;
    }
}

/// <summary>Command layout and content. Omitted fields use their documented defaults, including on default-valued props.</summary>
public readonly record struct CommandProps
{
    readonly string? placeholder, empty;
    readonly float? width, maxHeight, resultsPadding, resultsGap;
    readonly Side? side;
    readonly PopoverAlign? align;

    public CommandProps()
    {
    }

    public CommandProps(string Placeholder = "Type to search…", string Empty = "No results found.", float Width = 380,
        float MaxHeight = 320, Side Side = Side.Bottom, PopoverAlign Align = PopoverAlign.Start)
    {
        this.Placeholder = Placeholder;
        this.Empty = Empty;
        this.Width = Width;
        this.MaxHeight = MaxHeight;
        this.Side = Side;
        this.Align = Align;
    }

    public string Placeholder
    {
        get => placeholder ?? "Type to search…"; init => placeholder = value;
    }
    public string Empty
    {
        get => empty ?? "No results found."; init => empty = value;
    }
    public float Width
    {
        get => width ?? 380; init => width = value;
    }
    public float MaxHeight
    {
        get => maxHeight ?? 320; init => maxHeight = value;
    }
    public Side Side
    {
        get => side ?? Side.Bottom; init => side = value;
    }
    public PopoverAlign Align
    {
        get => align ?? PopoverAlign.Start; init => align = value;
    }

    public void Deconstruct(out string Placeholder, out string Empty, out float Width, out float MaxHeight,
        out Side Side, out PopoverAlign Align)
    {
        Placeholder = this.Placeholder;
        Empty = this.Empty;
        Width = this.Width;
        MaxHeight = this.MaxHeight;
        Side = this.Side;
        Align = this.Align;
    }

    /// <summary>Renders each result's content. Content determines its height; Kit owns chrome, the hit target and semantics.</summary>
    public Action<Context, CommandItem, CommandItemState>? RenderItem
    {
        get; init;
    }
    /// <summary>Renders a search header inside Kit's keyboard scope. Include a bound text field using the caller's query.</summary>
    public Action<Context>? RenderSearch
    {
        get; init;
    }
    /// <summary>Renders a group heading's content.</summary>
    public Action<Context, string>? RenderGroup
    {
        get; init;
    }
    /// <summary>Renders the empty-results content.</summary>
    public Action<Context>? RenderEmpty
    {
        get; init;
    }
    /// <summary>Build inside the current panel instead of opening a popover. The host owns placement and dismissal.</summary>
    public bool Embedded
    {
        get; init;
    }
    /// <summary>Result viewport inset in logical pixels. Defaults to 4; explicit zero is preserved.</summary>
    public float ResultsPadding
    {
        get => resultsPadding ?? 4; init => resultsPadding = value;
    }
    /// <summary>Spacing between result rows in logical pixels. Defaults to 2; explicit zero is preserved.</summary>
    public float ResultsGap
    {
        get => resultsGap ?? 2; init => resultsGap = value;
    }
}

public readonly partial struct Kit
{
    /// <summary>
    /// A command menu (shadcn Command) in a popover: a focused search field over grouped, filtered results.
    /// Down moves into the results, Enter in the field picks the first match, Escape or an outside click
    /// calls <paramref name="onClose"/>. The query belongs to the caller.
    /// </summary>
    public bool CommandMenu(
        Rect? anchor,
        string query,
        Action<string> queryChanged,
        IReadOnlyList<CommandItem> items,
        Action<string> selected,
        Action onClose,
        CommandProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var matches = Filter(items, query);
        var firstEnabled = matches.FirstOrDefault(item => !item.Disabled);
        string? chosen = null;
        using (KitScope? popup = props.Embedded ? null : Popover(anchor, onClose,
            new(props.Side, props.Align, props.Width, Name: props.Placeholder, Padding: 0), key, source, line))
        using (Div? embedded = props.Embedded ? Box(key, source, line).FillWidth() : null)
        {
            var focusResults = false;
            var searchFocused = false;
            using (var search = context.Div(key: "search").FillWidth().Padding(8))
            {
                searchFocused = context.FocusWithin(search.Id);
                if (searchFocused && !context.NavigationCompositionActive)
                {
                    if (context.Input.Key == Key.Enter && matches.Count > 0)
                    {
                        context.Input.Key = Key.None;
                        chosen = firstEnabled?.Id;
                    }
                    else if (context.Input.Key == Key.Down && matches.Count > 0)
                    {
                        context.Input.Key = Key.None;
                        focusResults = true;
                    }
                }
                if (props.RenderSearch is { } renderSearch)
                {
                    search.Padding(0);
                    renderSearch(context);
                }
                else
                    context.TextField(query, queryChanged, new(props.Placeholder, props.Width - 16, Name: props.Placeholder), key: "query");
            }
            using (context.Div(key: "rule").FillWidth().Height(1).Background(t.Border))
            {
            }
            if (focusResults)
                context.FocusNextControl();
            using var results = context.Div(key: "results").FillWidth().MaxHeight(props.MaxHeight).Scroll().Padding(props.ResultsPadding).Gap(props.ResultsGap)
                .Semantics(Role.List, "Results");
            if (matches.Count == 0)
            {
                using var empty = context.Div(key: "empty").FillWidth();
                if (props.RenderEmpty is { } renderEmpty)
                    context.RenderContent(static (ctx, render, _) => render(ctx), renderEmpty, false);
                else
                {
                    empty.Center().Py(20);
                    context.Text(props.Empty, Text(t.FontSize, t.MutedForeground), key: "text");
                }
            }
            string? group = null;
            foreach (var item in matches)
            {
                if (item.Group is { } heading && heading != group)
                {
                    if (props.RenderGroup is { } renderGroup)
                    {
                        using var label = context.Div(key: $"group:{heading}").FillWidth();
                        context.RenderContent(static (ctx, render, text) => render(ctx, text), renderGroup, heading);
                    }
                    else
                        MenuLabel(heading, key: $"group:{heading}");
                }
                group = item.Group;
                var itemProps = new MenuItemProps(item.Icon, item.Hint, Disabled: item.Disabled, Checked: item.Checked);
                var suggested = searchFocused && item == firstEnabled;
                if (MenuItemCore(item.Label, itemProps, item.Id, source, line, item, props.RenderItem, suggested).Clicked)
                    chosen = item.Id;
            }
        }
        if (chosen is null)
            return false;
        selected(chosen);
        return true;
    }

    /// <summary>Items whose label, group or keywords contain every word of the query, in their original order.</summary>
    List<CommandItem> Filter(IReadOnlyList<CommandItem> items, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var compare = context.Culture.CompareInfo;
        var result = new List<CommandItem>(items.Count);
        foreach (var item in items)
        {
            var text = $"{item.Label} {item.Group} {item.Keywords}";
            if (words.All(word => compare.IndexOf(text, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
                result.Add(item);
        }
        return result;
    }
}
