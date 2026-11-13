using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Kumiko.UI;

namespace CRMLite;

/// <summary>
/// The New deal company picker. <paramref name="Request"/> numbers directory searches so only the latest answer
/// lands; <see cref="Results"/> are the company IDs it returned.
/// </summary>
public sealed record CompanySearch(DropdownState Picker, int Request = 0, bool Loading = false, string? Error = null)
{
    public ImmutableArray<string> Results { get; init; } = [];
}

public sealed partial record CrmModel
{
    /// <summary>The Companies industry filter; its chosen IDs are industry names.</summary>
    public DropdownState? IndustryPicker
    {
        get; init;
    }

    [JsonIgnore] public ImmutableArray<string> Industries => IndustryPicker is { } picker ? picker.SelectedIds : [];
}
