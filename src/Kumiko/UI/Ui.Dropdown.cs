namespace Kumiko.UI;

/// <summary>
/// Options for a bound dropdown. <paramref name="Remote"/> shows the choices unfiltered because the application searched
/// for <see cref="DropdownState.Query"/> itself; <paramref name="Loading"/> and <paramref name="LoadError"/> describe
/// that search. <paramref name="Multiple"/> chooses several IDs into <see cref="DropdownState.SelectedIds"/>.
/// </summary>
public readonly record struct DropdownOptions(
    float? Width = null,
    string? Placeholder = null,
    string? EmptyMessage = null,
    bool Multiple = false,
    bool Remote = false,
    bool Loading = false,
    string? LoadError = null,
    bool Invalid = false,
    bool Disabled = false
);

public sealed partial class Ui<TMessage>
{
    /// <summary>
    /// A searchable dropdown whose interaction state lives in the model. Every change (opening, typing, choosing)
    /// dispatches the next state; compare its query with the previous one to start a remote search.
    /// </summary>
    public void Dropdown(NodeKey key, string label, DropdownState state, IReadOnlyList<Choice> choices,
        Func<DropdownState, TMessage> onChange, DropdownOptions options = default)
    {
        Check();
        ArgumentNullException.ThrowIfNull(state);
        var dropdown = new SearchDropdown
        {
            Multiple = options.Multiple,
            Remote = options.Remote,
            Loading = options.Loading,
            LoadError = options.LoadError,
            Invalid = options.Invalid,
            Disabled = options.Disabled,
            Placeholder = options.Placeholder ?? "Select…"
        };
        dropdown.Restore(state);
        // Accessibility actions can change the dropdown after this view returns, so every change dispatches.
        var reported = state;
        var dispatch = send;
        dropdown.StateChanged += next =>
        {
            reported = next;
            dispatch(onChange(next));
        };
        dropdown.Draw(context, label, choices, key, options.Width ?? 240, options.EmptyMessage ?? "No matches");
        var settled = dropdown.Capture();
        if (settled != reported)
            context.WithSource(settled.TriggerId, () => send(onChange(settled)));
    }
}
