using System.Text.Json.Serialization;
using Kumiko.Runtime;

internal enum GalleryScreen
{
    Home,
    CapWords,
    Alma,
    Slack,
    Opal,
    Unwind
}

/// <summary>
/// Each study's state survives switching. Navigation is the gallery followed by at most one study visit, which a
/// study's back arrow leaves by moving Back to the gallery's own visit.
/// </summary>
internal sealed record GalleryModel(CapWordsModel CapWords, AlmaModel Alma, SlackModel Slack, OpalModel Opal, UnwindModel Unwind)
{
    public NavigationHistory<GalleryScreen> Navigation { get; init; } = NavigationHistory<GalleryScreen>.Start(GalleryScreen.Home);

    [JsonIgnore] public GalleryScreen Screen => Navigation.Current.Route;
}

internal abstract partial record GalleryMessage
{
    public sealed record Open(GalleryScreen Screen) : GalleryMessage;
    public sealed record Home : GalleryMessage;
    public sealed record Navigate(HistoryRequest Request) : GalleryMessage;
    public sealed record CapWords(CapWordsMessage Message) : GalleryMessage;
    public sealed record Alma(AlmaMessage Message) : GalleryMessage;
    public sealed record Slack(SlackMessage Message) : GalleryMessage;
    public sealed record Opal(OpalMessage Message) : GalleryMessage;
    public sealed record Unwind(UnwindMessage Message) : GalleryMessage;
}
