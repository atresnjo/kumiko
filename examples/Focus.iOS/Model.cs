using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Kumiko.Runtime;

/// <summary>The five bottom-bar destinations, in tab-bar order.</summary>
internal enum FocusTab
{
    Now,
    Blocking,
    Activity,
    Health,
    Settings
}

internal sealed record FocusModel(int Minutes = 45, int Remaining = 2700,
    bool Running = false, bool Started = false, string? Drawer = null)
{
    /// <summary>One visit: a tab switch replaces it, so tabs never leave a Back entry.</summary>
    public NavigationHistory<FocusTab> Navigation { get; init; } = NavigationHistory<FocusTab>.Start(FocusTab.Now);

    [JsonIgnore] public string Tab => FocusApp.Tabs[(int)Navigation.Current.Route].Id;

    public ImmutableHashSet<string> Blocked
    {
        get; init;
    } =
        ["app-mail", "app-video", "category-social", "site-news", "site-reddit", "site-instagram",
         "site-facebook", "site-tiktok", "site-x"];
}
