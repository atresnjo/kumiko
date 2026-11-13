using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;

internal sealed record ComponentsModel(ImmutableArray<NativeListItem> Items, int NextId = 4, string? SelectedId = null)
{
    /// <summary>The list screen's history; the details drawer is an overlay, not a destination.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("list");
}
