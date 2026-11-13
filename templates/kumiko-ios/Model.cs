using System.Collections.Immutable;
using Kumiko.Runtime;

namespace KumikoiOSTemplate;

internal enum Route
{
    Home,
    Settings
}

internal sealed record Model(int Count = 0, string Name = "", bool Updates = true)
{
    public NavigationHistory<Route> Navigation { get; init; } = NavigationHistory<Route>.Start(Route.Home);
    public ImmutableArray<string> Log { get; init; } = [];
}
