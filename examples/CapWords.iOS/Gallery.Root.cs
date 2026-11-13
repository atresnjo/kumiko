using System.Collections.Immutable;
using Kumiko.Runtime;

/// <summary>The gallery cards need only the CapWords artwork; every other preview is static.</summary>
internal sealed record GalleryHomeProps(ImmutableArray<Word> Words);

internal sealed partial class AnimationGalleryApp
{
    /// <summary>The navigation host key; history requests from any other host are ignored.</summary>
    internal const string Host = "AnimationGalleryApp";

    public static NavigationRoot<GalleryMessage> Root(GalleryModel model) => NavigationRoot<GalleryMessage>.Create(
        model.Navigation, Host, route => route switch
        {
            GalleryScreen.Home => new GalleryHomeScreen(new(model.CapWords.Words)).Map<GalleryMessage>(static message => message),
            GalleryScreen.CapWords => new CapWordsScreen(model.CapWords).Map<GalleryMessage>(static message => new GalleryMessage.CapWords(message)),
            GalleryScreen.Alma => new AlmaScreen(model.Alma).Map<GalleryMessage>(static message => new GalleryMessage.Alma(message)),
            GalleryScreen.Slack => new SlackScreen(model.Slack).Map<GalleryMessage>(static message => new GalleryMessage.Slack(message)),
            GalleryScreen.Opal => new OpalScreen(model.Opal).Map<GalleryMessage>(static message => new GalleryMessage.Opal(message)),
            GalleryScreen.Unwind => new UnwindScreen(model.Unwind).Map<GalleryMessage>(static message => new GalleryMessage.Unwind(message)),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        },
        static request => new GalleryMessage.Navigate(request));
}
