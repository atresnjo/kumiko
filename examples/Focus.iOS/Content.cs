using System.Collections.Immutable;
using Kumiko.iOS;

internal sealed partial class FocusApp
{
    internal static ImmutableArray<NativeTabItem> Tabs => [
        new("now", "Now", "circle.circle"), new("blocking", "Blocking", "shield"),
        new("activity", "Activity", "chart.bar.xaxis"), new("health", "Health", "heart"),
        new("settings", "Settings", "slider.horizontal.3")];

    /// <summary>The simulated Mac connection shown in every header.</summary>
    internal const string Status = "Mac preview · Demo session";

    /// <summary>The sample timeline's current-time marker, as a fraction of the day.</summary>
    internal const float Now = .38f;

    internal static ImmutableArray<NativeListItem> Targets => [
        new("app-mail", "Mail", "App"), new("app-video", "YouTube", "App"),
        new("category-social", "Social", "Category"), new("site-news", "news.ycombinator.com", "Website"),
        new("site-reddit", "reddit.com", "Website"), new("site-instagram", "instagram.com", "Website"),
        new("site-facebook", "facebook.com", "Website"), new("site-tiktok", "tiktok.com", "Website"),
        new("site-x", "x.com", "Website")];

    static string BlockingSummary(ImmutableHashSet<string> blocked)
    {
        var apps = blocked.Count(id => id.StartsWith("app-", StringComparison.Ordinal));
        var categories = blocked.Count(id => id.StartsWith("category-", StringComparison.Ordinal));
        var sites = blocked.Count(id => id.StartsWith("site-", StringComparison.Ordinal));
        return $"block {apps} {(apps == 1 ? "app" : "apps")} · {categories} {(categories == 1 ? "category" : "categories")} · {sites} {(sites == 1 ? "site" : "sites")}";
    }
}
