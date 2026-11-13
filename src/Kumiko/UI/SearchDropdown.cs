using System.Collections.Immutable;

namespace Kumiko.UI;

/// <summary>A dropdown choice. Choices sharing a <see cref="Section"/> are listed together beneath its heading.</summary>
public sealed record Choice(string Id, string Label, bool Disabled = false)
{
    /// <summary>The heading the choice is listed under; choices without one are listed first.</summary>
    public string? Section
    {
        get; init;
    }
    /// <summary>Secondary text after the label, such as a domain or a count.</summary>
    public string? Detail
    {
        get; init;
    }
}

/// <summary>
/// Searchable native combobox with window-level popup and stable option identity. It can group choices into sections,
/// choose several choices (<see cref="Multiple"/>) and show results that the application searches for itself
/// (<see cref="Remote"/>), for example from a server.
/// </summary>
public sealed partial class SearchDropdown
{
    readonly TextBuffer query = new();
    int active;
    bool focusSearch;
    ulong triggerId;
    Rect anchor;
    ImmutableArray<string> selectedIds = [];
    ImmutableArray<Choice> selection = [];
    public string? SelectedId
    {
        get; set;
    }
    /// <summary>The chosen IDs, in the order they were chosen, when <see cref="Multiple"/> is set.</summary>
    public ImmutableArray<string> SelectedIds
    {
        get => selectedIds;
        set => selectedIds = value.IsDefault ? [] : value;
    }
    public bool IsOpen
    {
        get; private set;
    }
    public bool Disabled
    {
        get; set;
    }
    /// <summary>Choosing toggles a choice in <see cref="SelectedIds"/> and keeps the list open.</summary>
    public bool Multiple
    {
        get; set;
    }
    /// <summary>
    /// The application searches: choices are the results for the current query and are shown unfiltered.
    /// Handle <see cref="QueryChanged"/> (or the query in <see cref="StateChanged"/>) to start a search.
    /// </summary>
    public bool Remote
    {
        get; set;
    }
    /// <summary>A search is running; the list says so above any earlier results.</summary>
    public bool Loading
    {
        get; set;
    }
    /// <summary>Why the last search failed, shown in place of the empty message.</summary>
    public string? LoadError
    {
        get; set;
    }
    /// <summary>Marks the trigger as failing validation.</summary>
    public bool Invalid
    {
        get; set;
    }
    public string Placeholder { get; set; } = "Select…";
    public event Action<DropdownState>? StateChanged;
    public event Action<string?>? SelectionChanged;
    /// <summary>The chosen IDs changed in a multiple-selection dropdown.</summary>
    public event Action<ImmutableArray<string>>? SelectedIdsChanged;
    /// <summary>The search text changed, or a remote dropdown opened with an empty query.</summary>
    public event Action<string>? QueryChanged;

    void Close(Context c)
    {
        IsOpen = false;
        c.FocusControl(triggerId);
        c.Changed = true;
        StateChanged?.Invoke(Capture());
    }

    void Open(Context c)
    {
        query.Text = "";
        active = -1;
        focusSearch = true;
        IsOpen = true;
        c.Changed = true;
        StateChanged?.Invoke(Capture());
        if (Remote)
            QueryChanged?.Invoke("");
    }

    public void Draw(Context c, string name, IReadOnlyList<Choice> choices, NodeKey key, float width = 240,
        string emptyMessage = "No matches")
    {
        var t = c.Theme;
        if (Disabled)
            IsOpen = false;
        var chosen = Chosen(choices);
        var labels = chosen.Count == 0 ? null : string.Join(", ", chosen.Select(o => o.Label));
        using (var trigger = c.Div(key).Row().Width(width).MinHeight(t.Controls.Height).ItemsCenter()
            .Px(t.Space.S3).Gap(t.Space.S2).Background(t.Surface).Border(Invalid ? t.Danger : t.InputBorder).Rounded(t.Radius.Md)
            .Semantics(Role.ComboBox, name, labels).Accessibility(new(Expanded: IsOpen), request =>
            {
                if (request.Action == AccessibilityAction.Expand)
                    Open(c);
                else if (request.Action == AccessibilityAction.Collapse)
                    Close(c);
                else
                    return false;
                return true;
            }))
        {
            triggerId = trigger.Id;
            anchor = trigger.PreviousRect ?? anchor;
            var hit = trigger.Control(!Disabled);
            if (Disabled)
                trigger.Opacity(t.Controls.DisabledOpacity);
            if (hit.Focused)
                trigger.Border(t.FocusRing, t.Controls.FocusWidth);
            if (hit.Hovered)
                trigger.Background(t.SurfaceHover);
            var label = chosen.Count switch
            {
                0 => Placeholder,
                1 => chosen[0].Label,
                _ => $"{chosen[0].Label} +{chosen.Count - 1}"
            };
            using (c.Div(key: "value").Flex().Clip())
                c.Text(label, t.Typography.Body with
                {
                    Color = chosen.Count == 0 ? t.Muted : t.Text
                }, key: "label");
            c.Icon("chevron.down", 14, t.Muted, key: "arrow");
            if (hit.Clicked || hit.Focused && c.Input.Key == Key.Down)
            {
                c.Input.Key = Key.None;
                if (IsOpen)
                    Close(c);
                else
                    Open(c);
            }
        }
        if (IsOpen)
            DrawPopup(c, name, choices, key, width, emptyMessage);
    }

    /// <summary>The chosen choices in list order; remembered choices absent from <paramref name="choices"/> follow.</summary>
    List<Choice> Chosen(IReadOnlyList<Choice> choices)
    {
        ImmutableArray<string> ids = Multiple ? selectedIds : SelectedId is { } id ? [id] : [];
        if (ids.IsEmpty)
            return [];
        var chosen = Grouped(choices).Where(o => ids.Contains(o.Id)).ToList();
        foreach (var missing in ids)
        {
            if (chosen.All(o => o.Id != missing) && selection.FirstOrDefault(o => o.Id == missing) is { } kept)
                chosen.Add(kept);
        }
        return chosen;
    }

    /// <summary>Unsectioned choices first, then each section's choices in first-appearance order.</summary>
    static Choice[] Grouped(IEnumerable<Choice> choices) =>
        [.. choices.Where(o => o.Section is null), .. choices.Where(o => o.Section is not null).GroupBy(o => o.Section).SelectMany(g => g)];

    bool IsChosen(Choice choice) => Multiple ? selectedIds.Contains(choice.Id) : choice.Id == SelectedId;

    void Choose(Context c, Choice choice)
    {
        if (!Multiple)
        {
            SelectedId = choice.Id;
            selection = [choice];
            Close(c);
            SelectionChanged?.Invoke(choice.Id);
            return;
        }
        var on = selectedIds.Contains(choice.Id);
        selectedIds = on ? selectedIds.Remove(choice.Id) : selectedIds.Add(choice.Id);
        selection = selection.RemoveAll(o => o.Id == choice.Id);
        if (!on)
            selection = selection.Add(choice);
        ChangedIds(c);
    }

    void Clear(Context c)
    {
        selectedIds = [];
        selection = [];
        ChangedIds(c);
    }

    /// <summary>Reports a multiple-selection change and returns focus to the search field for the next query.</summary>
    void ChangedIds(Context c)
    {
        focusSearch = true;
        c.Changed = true;
        StateChanged?.Invoke(Capture());
        SelectedIdsChanged?.Invoke(selectedIds);
    }
}
